using System.Numerics;
using System.Reflection;
using Raylib_cs;
using static Raylib_cs.Raylib;
using Vaporworks;

namespace Afterlight;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
#if INTERNAL_DIAGNOSTICS
            if (args.Contains("--self-test")) return Verification.Run();
            if (args.Contains("--drm-test")) return DrmTests.Run();
            if (args.Contains("--verify")) return Run(args);   // build-server capture path
#endif
            // Vapor DRM: no verified app ticket from the Vapor client, no game.
            if (VaporAPI.RestartAppIfNecessary()) return 0;
            var session = VaporAPI.Init();
            if (!session.Ok) return TrialGate.Blocked(session, args);
            return Run(args, session);
        }
        catch (Exception error)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Afterlight");
            Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "crash.log"), error.ToString());
            return 1;
        }
    }

    static int Run(string[] args, VaporSession? session = null)
    {
#if INTERNAL_DIAGNOSTICS
        bool verify = args.Contains("--verify");
#else
        bool verify = false;
#endif
        bool demo = args.Contains("--showcase");
        string captureFolder = Path.Combine(AppContext.BaseDirectory, "playtest");
        if (verify) Directory.CreateDirectory(captureFolder);
        SetTraceLogLevel(TraceLogLevel.Warning);
        var flags = ConfigFlags.Msaa4xHint | ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint;
        if (verify) flags |= ConfigFlags.HiddenWindow;
        SetConfigFlags(flags);
        InitWindow(1440, 810, "AFTERLIGHT | Offline arena shooter");
        SetWindowMinSize(960, 540); SetTargetFPS(120); SetExitKey(KeyboardKey.Null);
        SetWindowPosition(Math.Max(0, (GetMonitorWidth(GetCurrentMonitor()) - 1440) / 2), Math.Max(0, (GetMonitorHeight(GetCurrentMonitor()) - 810) / 2));
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Afterlight.Assets.icon.png"))
        {
            if (stream != null)
            {
                using MemoryStream memory = new(); stream.CopyTo(memory);
                var icon = LoadImageFromMemory(".png", memory.ToArray()); SetWindowIcon(icon); UnloadImage(icon);
            }
        }
        var scene = new Scene();
        var audio = new Audio();
        var hud = new Hud(scene);
        var profile = verify ? new Profile() : Profile.Load();
        var world = new World();
        Screen screen = Screen.Menu;
        Screen prevScreen = Screen.Menu;
        float fade = 1, resultsAt = 0;
        bool quit = false, newBest = false, fullscreen = false;
        int frame = 0, oldWidth = 1440, oldHeight = 810;
        float clock = 0, mouseGrace = 0;
        float verifyMinFps = float.MaxValue, verifyTotalMs = 0;
        int verifyFrames = 0;
        VaporSession? denied = null;
        Task<VaporSession>? renewal = null;
        float renewAt = 20;

        void Capture(string name)
        {
            var screenshot = LoadImageFromTexture(scene.FinalTarget.Texture);
            ImageFlipVertical(ref screenshot); ExportImage(screenshot, Path.Combine(captureFolder, name + ".png")); UnloadImage(screenshot);
        }
        void Start()
        {
            world = new World(Environment.TickCount); screen = Screen.Playing; hud.Settings = false; newBest = false; hud.SaveNotice = "";
            if (!verify) DisableCursor(); mouseGrace = .12f;
        }
        void Pause()
        {
            screen = Screen.Paused; hud.Settings = false; EnableCursor(); audio.Silence();
        }
        void Resume()
        {
            screen = Screen.Playing; hud.Settings = false; if (!verify) DisableCursor(); mouseGrace = .12f;
        }
        if (demo) { Start(); world.StartWave(); }
        while (!quit && !WindowShouldClose())
        {
            float realDt = GetFrameTime();
            float dt = verify ? 1 / 60f : Math.Clamp(realDt, .0001f, .05f);
            clock += dt; frame++; SetMouseCursor(MouseCursor.Default); hud.Clock = clock;
            if (session != null)
            {
                if (!session.TicketStillValid) { denied = new(false, session.Ticket?.License == "trial" ? "trial_ended" : "ticket_expired", "Your play license has expired. Return to Vapor to continue.", null); break; }
                if (clock >= renewAt && renewal == null) { renewal = VaporAPI.InitAsync(); renewAt = clock + 20; }
                if (renewal?.IsCompleted == true)
                {
                    var refreshed = renewal.GetAwaiter().GetResult(); renewal = null;
                    if (!refreshed.Ok) { denied = refreshed; break; }
                    session = refreshed;
                }
            }
            if (verify)
            {
                if (frame == 3) { Capture("01-menu"); Start(); world.StartWave(); }
                if (frame == 4)
                {
                    world.Drones.Clear();
                    world.Drones.Add(new Drone { Position = new(0, 0, 2), Health = 2, Cooldown = 20 });
                    world.Drones.Add(new Drone { Position = new(-4, 0, -3), Health = 2, Cooldown = 20, Phase = 2 });
                    world.Drones.Add(new Drone { Position = new(5, 0, 0), Health = 3, Heavy = true, Cooldown = 20, Phase = 4 });
                    world.Player = new(0, 1.65f, 13); world.Yaw = -.025f; world.Pitch = .01f;
                }
                if (frame == 65) Capture("02-gameplay");
                if (frame == 66) world.Shoot();
                if (frame == 67) Capture("03-firing");
                if (frame == 70) world.Reload();
                if (frame == 96) Capture("04-reloading");
                if (frame == 100) Pause();
                if (frame == 102) { Capture("05-pause"); hud.Settings = true; }
                if (frame == 104) { Capture("06-settings"); hud.Settings = false; screen = Screen.Results; world.Phase = RunPhase.Won; world.Score = 3840; world.Kills = 21; world.Shots = 53; world.Hits = 46; world.Elapsed = 136; newBest = true; }
                if (frame == 106) { Capture("07-victory"); world.Phase = RunPhase.Lost; newBest = false; }
                if (frame == 108) { Capture("08-defeat"); Resume(); world = new World(); world.StartWave(); }
                if (frame >= 110 && frame < 240) { verifyFrames++; verifyTotalMs += realDt * 1000; verifyMinFps = Math.Min(verifyMinFps, realDt > 0 ? 1 / realDt : 120); }
                if (frame == 245)
                {
                    File.WriteAllText(Path.Combine(captureFolder, "render-report.txt"), $"Native OpenGL window: OK\nUI states rendered: menu, arena, firing, reload, pause, settings, victory, defeat\nRender size: {Scene.Width}x{Scene.Height}\nMean frame: {verifyTotalMs / verifyFrames:0.0} ms\nLowest sampled FPS: {verifyMinFps:0.0}\nRender frames: {frame}\n");
                    quit = true;
                }
            }
            if (!verify && IsKeyPressed(KeyboardKey.F11))
            {
                if (!fullscreen) { oldWidth = GetScreenWidth(); oldHeight = GetScreenHeight(); }
                ToggleBorderlessWindowed(); fullscreen = !fullscreen;
                if (!fullscreen) SetWindowSize(oldWidth, oldHeight);
            }
            if (IsKeyPressed(KeyboardKey.M)) { profile.Muted = !profile.Muted; if (!verify) profile.Save(); }
            if (IsKeyPressed(KeyboardKey.Escape))
            {
                if (hud.Settings) { hud.Settings = false; if (!verify) profile.Save(); }
                else if (screen == Screen.Playing) Pause();
                else if (screen == Screen.Paused) Resume();
                else if (screen == Screen.Results) screen = Screen.Menu;
            }
            if (!verify && screen == Screen.Playing && (!IsWindowFocused() || IsWindowMinimized())) Pause();
            if (screen == Screen.Playing)
            {
                mouseGrace -= dt;
                if (!verify && mouseGrace <= 0)
                {
                    var mouse = GetMouseDelta();
                    world.Yaw += mouse.X * .0021f * profile.Sensitivity;
                    world.Yaw = MathF.IEEERemainder(world.Yaw, MathF.Tau);
                    world.Pitch = Math.Clamp(world.Pitch - mouse.Y * .0021f * profile.Sensitivity, -1.35f, 1.35f);
                }
                Vector2 move = new((Down(KeyboardKey.D) ? 1 : 0) - (Down(KeyboardKey.A) ? 1 : 0), (Down(KeyboardKey.W) ? 1 : 0) - (Down(KeyboardKey.S) ? 1 : 0));
                world.Update(dt, verify ? default : new(move, Down(KeyboardKey.LeftShift) || Down(KeyboardKey.RightShift), IsMouseButtonDown(MouseButton.Left) && mouseGrace <= 0, IsKeyPressed(KeyboardKey.R)));
                if (world.Phase is RunPhase.Won or RunPhase.Lost)
                {
                    screen = Screen.Results; EnableCursor();
                    newBest = world.Score > profile.BestScore; profile.BestScore = Math.Max(profile.BestScore, world.Score);
                    if (world.Phase == RunPhase.Won) profile.Victories++;
                    if (!verify && !profile.Save()) hud.SaveNotice = "Your score could not be saved on this PC.";
                }
            }
            audio.Update(world, profile.Muted || verify);
            bool menu = screen == Screen.Menu;
            var camera = scene.Camera(world, menu, clock);
            if (!menu) scene.DrawWeapon(world, clock);
            BeginTextureMode(scene.Target); ClearBackground(Scene.Ink);
            scene.DrawWorld(world, camera, clock, menu);
            if (!menu) scene.CompositeWeapon();
            EndTextureMode();
            BeginTextureMode(scene.FinalTarget); ClearBackground(Scene.Ink);
            scene.PostProcess(clock);
            if (verify && frame == 2)
            {
                var art = LoadImageFromTexture(scene.Target.Texture); ImageFlipVertical(ref art);
                ExportImage(art, Path.Combine(captureFolder, "00-courtyard.png")); UnloadImage(art);
            }
            UiAction action = screen switch
            {
                Screen.Menu => hud.Menu(profile, clock),
                Screen.Paused => hud.Pause(world, profile),
                Screen.Results => hud.Results(world, profile, newBest, clock, verify ? 1 : Math.Clamp((clock - resultsAt) / .9f, 0, 1)),
                _ => UiAction.None
            };
            if (screen == Screen.Playing)
            {
                hud.Playing(world, profile, clock, camera);
                if (session?.Ticket != null && session.Ticket.License == "trial")
                    hud.TrialRemaining = (session.Ticket.TrialEndsAt - session.EffectiveNow).TotalMinutes;
            }
            if (!verify && fade > 0) DrawRectangle(0, 0, Scene.Width, Scene.Height, Fade(Scene.Ink, fade * .92f));
            EndTextureMode();
            BeginDrawing(); ClearBackground(Scene.Ink); scene.Present(clock); EndDrawing();
            switch (action)
            {
                case UiAction.Start: Start(); break;
                case UiAction.Resume: Resume(); break;
                case UiAction.Menu: screen = Screen.Menu; hud.Settings = false; EnableCursor(); audio.Silence(); break;
                case UiAction.Settings: hud.Settings = true; break;
                case UiAction.Back: hud.Settings = false; if (!verify) profile.Save(); break;
                case UiAction.Mute: profile.Muted = !profile.Muted; if (!verify) profile.Save(); break;
                case UiAction.Quit: quit = true; break;
            }
            if (screen != prevScreen)
            {
                if (screen == Screen.Results) resultsAt = clock;
                if (!verify) fade = 1;
                prevScreen = screen;
            }
            if (!verify && fade > 0) fade = Math.Max(0, fade - realDt * 2.2f);
        }
        EnableCursor(); if (!verify) profile.Save();
        // The OpenGL context remains alive until all GPU resources are released.
        audio.Dispose(); scene.Dispose(); CloseWindow();
        if (denied != null) return TrialGate.Blocked(denied, args);
        return 0;
    }
    static bool Down(KeyboardKey key) => IsKeyDown(key);
}


