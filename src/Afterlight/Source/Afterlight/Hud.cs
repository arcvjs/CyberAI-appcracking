using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;
using static Afterlight.Scene;

namespace Afterlight;

public enum Screen { Menu, Playing, Paused, Results }
public enum UiAction { None, Start, Resume, Menu, Quit, Settings, Back, Mute }

public sealed class Hud(Scene scene)
{
    public bool Settings;
    public string SaveNotice = "";
    public float Clock;
    public double TrialRemaining = double.NaN;
    float scoreShown, scoreClock = -1;
    static int Accuracy(World world) => world.Shots == 0 ? 0 : (int)MathF.Round(world.Hits * 100f / world.Shots);
    public void Text(string text, float x, float y, float size, Color color, bool bold = false, bool shadow = true)
    {
        if (shadow) DrawTextEx(bold ? scene.Bold : scene.Regular, text, new(x, y + size * .05f), size, .5f, Fade(Color.Black, .4f));
        DrawTextEx(bold ? scene.Bold : scene.Regular, text, new(x, y), size, .5f, color);
    }
    void Title(string text, float x, float y, float size, Color color, bool shadow = true)
    {
        if (shadow) DrawTextEx(scene.Display, text, new(x, y + size * .05f), size, 1, Fade(Color.Black, .4f));
        DrawTextEx(scene.Display, text, new(x, y), size, 1, color);
    }
    void RightText(string text, float x, float y, float size, Color color, bool bold = false, bool shadow = true)
    { float w = MeasureTextEx(bold ? scene.Bold : scene.Regular, text, size, .5f).X; Text(text, x - w, y, size, color, bold, shadow); }
    void CenterText(string text, float x, float y, float size, Color color, bool bold = false, bool shadow = true)
    { float w = MeasureTextEx(bold ? scene.Bold : scene.Regular, text, size, .5f).X; Text(text, x - w / 2, y, size, color, bold, shadow); }
    void Label(string text, float x, float y, Color? color = null)
    {
        DrawTextEx(scene.Bold, text, new(x, y + 1), 15, 2.3f, Fade(Color.Black, .4f));
        DrawTextEx(scene.Bold, text, new(x, y), 15, 2.3f, color ?? Fade(White, .6f));
    }
    static void Line(float x, float y, float width, Color? color = null) => DrawRectangle((int)x, (int)y, (int)width, 1, color ?? Fade(White, .17f));

    public UiAction Button(string text, string key, Rectangle rect, UiAction action, bool primary = false)
    {
        bool hover = CheckCollisionPointRec(scene.Mouse(), rect);
        bool down = hover && IsMouseButtonDown(MouseButton.Left);
        Color fill = primary
            ? down ? new Color(178, 238, 226, 255) : hover ? White : Teal
            : down ? new Color(32, 56, 64, 245) : hover ? new Color(51, 71, 76, 245) : new Color(22, 41, 48, 230);
        DrawRectangleRec(rect, fill);
        if (!primary) DrawRectangleLinesEx(rect, 1, Fade(White, hover ? .32f + .14f * MathF.Sin(Clock * 6) : .18f));
        else if (hover) DrawRectangleLinesEx(rect, 1, Fade(Ink, .25f));
        float dy = down ? 1 : 0;
        Text(text, rect.X + 24, rect.Y + (rect.Height - 24) / 2 - 2 + dy, 24, primary ? Ink : White, true, !primary);
        RightText(key, rect.X + rect.Width - 22, rect.Y + (rect.Height - 18) / 2 + dy, 16, primary ? Fade(Ink, .65f) : Fade(White, .5f), false, !primary);
        if (hover) SetMouseCursor(MouseCursor.PointingHand);
        return hover && IsMouseButtonPressed(MouseButton.Left) ? action : UiAction.None;
    }

    void Brand(float x, float y)
    {
        DrawTriangle(new(x, y + 25), new(x + 28, y + 25), new(x + 14, y), Teal);
        DrawRectangle((int)x + 10, (int)y + 16, 8, 12, Ink);
        Label("AFTERLIGHT", x + 43, y + 3, White);
    }

    public UiAction Menu(Profile profile, float clock)
    {
        DrawRectangleGradientH(0, 0, 1190, Height, new(10, 26, 35, 253), new(10, 26, 35, 0));
        DrawRectangleGradientV(0, 680, Width, 220, Color.Blank, new(9, 24, 32, 245));
        Brand(60, 47);
        DrawCircle(1350, 59, 4, Teal); Label("OFFLINE / SOLO", 1365, 49, White);
        if (Settings) return DrawSettings(profile, false);
        Label("OUTPOST 07   /   CONTAINMENT PROTOCOL", 62, 173, Teal);
        Title("AFTER", 54, 207, 128, White);
        Title("LIGHT", 54, 316, 128, White);
        DrawRectangle(63, 467, 40 + (int)(6 * (.5f + .5f * MathF.Sin(clock * 2))), 3, Orange);
        Text("The outpost is quiet. Keep it that way.", 62, 495, 25, White);
        Text("One courtyard. One carbine. Three waves of rogue drones.", 62, 536, 19, Fade(White, .63f));
        Text("Move through cover, stay sharp, and make every shot count.", 62, 565, 19, Fade(White, .63f));
        var action = Button("Enter the arena", "ENTER  >", new(62, 623, 378, 66), UiAction.Start, true);
        var settings = Button("Settings", "", new(454, 623, 168, 66), UiAction.Settings);
        if (settings != UiAction.None) action = settings;
        var quit = Button("Quit", "", new(62, 706, 122, 47), UiAction.Quit);
        if (quit != UiAction.None) action = quit;
        if (profile.BestScore > 0) Label($"PERSONAL BEST   {profile.BestScore:N0}      VICTORIES   {profile.Victories}", 214, 723, Fade(White, .65f));
        Line(1040, 581, 496);
        Label("THE COURTYARD", 1040, 608, White);
        Text("An abandoned solar station, at the edge of dusk.", 1040, 638, 18, Fade(White, .68f));
        string[] values = ["03", "21", "01"]; string[] captions = ["WAVES", "HOSTILES", "CARBINE"];
        for (int i = 0; i < 3; i++) { Title(values[i], 1040 + i * 176, 681, 50, White); Label(captions[i], 1043 + i * 176, 742); }
        Line(62, 808, 1474);
        Control("W A S D", "MOVE", 62, 837); Control("MOUSE", "AIM + FIRE", 292, 837); Control("R", "RELOAD", 558, 837); Control("SHIFT", "SPRINT", 748, 837);
        RightText("OFFLINE SOLO  /  POWERED BY VAPOR", 1536, 846, 13, Fade(White, .43f));
        if (IsKeyPressed(KeyboardKey.Enter)) return UiAction.Start;
        return action;
    }

    void Control(string key, string caption, float x, float y)
    {
        float w = MeasureTextEx(scene.Bold, key, 13, .5f).X + 19;
        DrawRectangleLinesEx(new(x, y, w, 30), 1, Fade(White, .28f));
        Text(key, x + 9, y + 6, 13, White, true); Text(caption, x + w + 12, y + 7, 13, Fade(White, .55f));
    }

    public void Playing(World world, Profile profile, float clock, Camera3D camera)
    {
        DrawRectangleGradientV(0, 0, Width, 180, new(9, 24, 32, 165), Color.Blank);
        DrawRectangleGradientV(0, 720, Width, 180, Color.Blank, new(9, 24, 32, 205));
        Brand(48, 37); Label("OUTPOST 07", 91, 68);
        Text("WAVE", 660, 40, 16, Fade(White, .7f), true); Title($"{world.Wave:00}", 722, 30, 44, White); Text("/ 03", 777, 49, 18, Fade(White, .55f));
        for (int i = 0; i < 3; i++) DrawRectangle(660 + i * 76, 91, 67, 3, i < world.Wave ? Teal : Fade(White, .22f));
        CenterText(world.Phase == RunPhase.Intermission ? "AREA SECURE" : $"{world.Drones.Count:00} HOSTILES REMAINING", 770, 110, 14, Teal, true);
        if (scoreClock < 0) scoreClock = clock;
        float ease = Math.Clamp((clock - scoreClock) * 8, 0, 1); scoreClock = clock;
        if (world.Score < scoreShown) scoreShown = world.Score;
        else scoreShown += (world.Score - scoreShown) * ease;
        if (world.Score - scoreShown < 1) scoreShown = world.Score;
        RightText($"{(int)scoreShown:N0}", 1433, 34, 36, White, true); RightText("SCORE", 1431, 77, 13, Fade(White, .57f));
        DrawRectangleLinesEx(new(1481, 38, 68, 35), 1, Fade(White, .3f)); Text("ESC", 1497, 45, 16, White, true);
        Radar(world);
        Label("VITALS", 50, 758, Fade(White, .7f));
        float pulse = world.Health < 30 && world.Phase == RunPhase.Active ? .55f + .45f * MathF.Sin(clock * 6) : 1;
        Text("+", 49, 792, 28, Fade(world.Health < 30 ? Orange : Teal, pulse), true);
        Title($"{world.Health:000}", 84, 777, 57, Fade(White, pulse));
        Text("/ 100", 191, 811, 16, Fade(White, .55f));
        for (int i = 0; i < 20; i++) DrawRectangle(51 + i * 12, 851, 9, 6, i < (world.Health + 4) / 5 ? world.Health <= 30 ? Orange : Teal : Fade(White, .2f));
        RightText("AR-01 / PULSE CARBINE", 1547, 759, 15, Fade(White, .7f), true);
        RightText($"{world.Ammo:00}", 1457, 781, 61, world.Ammo <= 6 ? Orange : White, true);
        Text($"/ {World.Magazine}", 1471, 814, 24, Fade(White, .55f));
        RightText(world.ReloadLeft > 0 ? "RECHARGING CELL" : "R  RELOAD   /   UNLIMITED RESERVE", 1547, 856, 13, world.ReloadLeft > 0 ? Teal : Fade(White, .5f));
        if (world.ReloadLeft > 0)
        {
            DrawRectangle(1328, 846, 218, 3, Fade(White, .2f));
            DrawRectangle(1328, 846, (int)(218 * (1 - world.ReloadLeft / World.ReloadDuration)), 3, Teal);
        }
        Crosshair(world);
        DrawFloaters(world, camera);
        if (world.Phase == RunPhase.Intermission)
        {
            DrawRectangleRounded(new(557, 196, 486, 116), .07f, 8, new(15, 33, 40, 228));
            CenterText(world.Wave == 1 ? "SYSTEMS ONLINE" : "WAVE CLEARED", 800, 213, 16, Teal, true);
            CenterText($"Wave {world.Wave:00} inbound in {Math.Max(1, (int)MathF.Ceiling(world.WaveTimer))}", 800, 245, 29, White, true);
            CenterText(world.Wave == 1 ? "Find your footing. Use cover." : "+30 HEALTH   /   AMMO REPLENISHED", 800, 285, 13, Fade(White, .65f));
        }
        if (world.Elapsed < 17) CenterText("WASD  Move     SHIFT  Sprint     CLICK  Fire     R  Reload", 800, 848, 15, Fade(White, .72f));
        else CenterText("CLEAR THE COURTYARD", 800, 850, 12, Fade(White, .35f));
        if (world.Health < 30 && world.Phase == RunPhase.Active) CenterText("LOW HEALTH / FIND COVER", 800, 698, 15, Orange, true);
        if (world.DamageFlash > 0)
        {
            var color = Fade(Orange, world.DamageFlash * .7f);
            DrawRectangleGradientH(0, 0, 170, Height, color, Color.Blank);
            DrawRectangleGradientH(1430, 0, 170, Height, Color.Blank, color);
            DrawRectangleGradientV(0, 0, Width, 90, color, Color.Blank);
        }
        if (profile.Muted) Label("AUDIO OFF", 48, 285, Fade(White, .5f));
        if (!double.IsNaN(TrialRemaining))
        {
            double left = Math.Max(0, TrialRemaining);
            string trial = $"TRIAL  {Math.Floor(left)}:{(int)(left % 1 * 60):00}";
            bool urgent = left < 5;
            if (!urgent || MathF.Sin(clock * 5) > -.4f)
                Label(trial, 48, 260, urgent ? Orange : Fade(White, .5f));
        }
    }

    void Crosshair(World world)
    {
        const int x = 800, y = 450;
        int gap = 6 + (int)(world.Recoil * 9);
        Color c = world.HitFlash > 0 ? world.LastHitKill ? Orange : Teal : White;
        DrawCircle(x, y, 2, c);
        DrawRectangle(x - gap - 7, y - 1, 7, 2, c); DrawRectangle(x + gap, y - 1, 7, 2, c);
        DrawRectangle(x - 1, y - gap - 7, 2, 7, c); DrawRectangle(x - 1, y + gap, 2, 7, c);
        if (world.HitFlash > 0)
        {
            foreach (int sx in new[] { -1, 1 }) foreach (int sy in new[] { -1, 1 }) DrawLineEx(new(x + sx * 11, y + sy * 11), new(x + sx * 18, y + sy * 18), 2, c);
            if (world.LastHitKill) CenterText("DISABLED", 800, 491, 13, Orange, true);
        }
        if (world.ReloadLeft > 0) DrawRing(new(x, y), 28, 30, -90, -90 + (1 - world.ReloadLeft / World.ReloadDuration) * 360, 64, Teal);
    }

    void DrawFloaters(World world, Camera3D camera)
    {
        foreach (var floater in world.Floaters)
        {
            if (floater.Life <= 0) continue;
            if (Vector3.Dot(floater.Position - camera.Position, world.Forward) < 0) continue;
            var screen = GetWorldToScreen(floater.Position, camera);
            if (screen.X < -100 || screen.X > Width + 100 || screen.Y < -100 || screen.Y > Height + 100) continue;
            float alpha = Math.Clamp(floater.Life / floater.MaxLife, 0, 1);
            CenterText(floater.Text, screen.X, screen.Y - 12, 20, Fade(floater.Warm ? Orange : Teal, alpha), true);
        }
    }

    void Radar(World world)
    {
        const int x = 49, y = 122, size = 139;
        DrawRectangle(x, y, size, size, new(11, 30, 38, 185));
        DrawRectangleLinesEx(new(x, y, size, size), 1, Fade(White, .15f));
        Vector2 Point(Vector3 p) => new(x + (p.X + 21) / 42 * size, y + (p.Z + 21) / 42 * size);
        foreach (var b in World.Blocks)
        {
            var a = Point(b.Min); var end = Point(b.Max);
            DrawRectangleV(a, end - a, new(100, 128, 129, 95));
        }
        var radarCenter = new Vector2(x + size / 2f, y + size / 2f);
        DrawCircleLines((int)radarCenter.X, (int)radarCenter.Y, size / 2f, Fade(White, .10f));
        DrawCircleLines((int)radarCenter.X, (int)radarCenter.Y, size / 4f, Fade(White, .10f));
        float sweep = world.Time * 1.4f;
        DrawLineEx(radarCenter, radarCenter + new Vector2(MathF.Cos(sweep), MathF.Sin(sweep)) * (size / 2f), 1, Fade(Teal, .45f));
        foreach (var drone in world.Drones) DrawCircleV(Point(drone.Position), 2.8f, Orange);
        var p = Point(world.Player); var forward = new Vector2(world.FlatForward.X, world.FlatForward.Z); var right = new Vector2(world.Right.X, world.Right.Z);
        DrawTriangle(p + forward * 6, p - forward * 4 - right * 4, p - forward * 4 + right * 4, Teal);
        Text("N", x + size / 2 - 4, y - 18, 11, Fade(White, .65f), true);
    }

    public UiAction Pause(World world, Profile profile)
    {
        DrawRectangle(0, 0, Width, Height, new(9, 24, 32, 219));
        Brand(60, 47);
        if (Settings) return DrawSettings(profile, true);
        Label("TAKE A BREATHER", 602, 223, Teal); Title("Paused.", 593, 255, 83, White);
        Text("Your run will be right here.", 603, 353, 22, Fade(White, .62f));
        var action = Button("Resume", "ESC  >", new(602, 416, 396, 64), UiAction.Resume, true);
        var settings = Button("Settings", "", new(602, 494, 396, 56), UiAction.Settings); if (settings != UiAction.None) action = settings;
        var menu = Button("End run", "MAIN MENU", new(602, 564, 396, 56), UiAction.Menu); if (menu != UiAction.None) action = menu;
        CenterText($"WAVE {world.Wave:00} / 03    |    SCORE {world.Score:N0}", 800, 664, 15, Fade(White, .5f));
        CenterText($"{world.Kills:00} DISABLED      {Accuracy(world)}% ACCURACY", 800, 690, 13, Fade(White, .45f));
        CenterText("F11  Fullscreen     M  Sound", 800, 826, 15, Fade(White, .45f));
        return action;
    }

    UiAction DrawSettings(Profile profile, bool paused)
    {
        DrawRectangle(498, 168, 604, 582, new(13, 32, 40, 248));
        DrawRectangleLinesEx(new(498, 168, 604, 582), 1, Fade(White, .18f));
        Label("MAKE YOURSELF COMFORTABLE", 542, 211, Teal); Title("Settings", 537, 246, 63, White);
        Text("Mouse sensitivity", 542, 349, 22, White, true); RightText($"{profile.Sensitivity:0.0}x", 1058, 350, 22, Teal);
        DrawRectangle(543, 402, 515, 4, Fade(White, .2f));
        float ratio = (profile.Sensitivity - .3f) / 2.2f;
        DrawRectangle(543, 402, (int)(515 * ratio), 4, Teal); DrawCircle(543 + (int)(515 * ratio), 404, 8, White);
        for (int i = 0; i <= 4; i++) DrawRectangle(543 + i * 515 / 4 - 1, 410, 2, 6, Fade(White, .25f));
        var slider = new Rectangle(532, 386, 538, 37);
        if (CheckCollisionPointRec(scene.Mouse(), slider))
        {
            SetMouseCursor(MouseCursor.PointingHand);
            if (IsMouseButtonDown(MouseButton.Left)) profile.Sensitivity = MathF.Round((.3f + Math.Clamp((scene.Mouse().X - 543) / 515, 0, 1) * 2.2f) * 10) / 10;
        }
        var mute = Button("Sound", profile.Muted ? "OFF" : "ON", new(542, 449, 516, 55), UiAction.Mute);
        Text("F11 toggles fullscreen. Esc pauses a run.", 542, 533, 18, Fade(White, .61f));
        Text("Settings and your best score are saved on this PC.", 542, 563, 17, Fade(White, .45f));
        var back = Button("Done", "ESC  >", new(542, 647, 516, 60), UiAction.Back, true);
        return mute != UiAction.None ? mute : back;
    }

    public UiAction Results(World world, Profile profile, bool newBest, float clock, float reveal = 1)
    {
        DrawRectangleGradientH(0, 0, Width, Height, new(9, 24, 32, 252), new(9, 24, 32, 188));
        Brand(60, 47); bool won = world.Phase == RunPhase.Won;
        float eased = 1 - MathF.Pow(1 - Math.Clamp(reveal, 0, 1), 3);
        Label(won ? "CONTAINMENT COMPLETE" : "SIGNAL LOST", 162, 208, won ? Teal : Orange);
        Title(won ? "Quiet. At last." : "One more run?", 154, 252, 92, White);
        Text(won ? "All three waves cleared. The outpost is yours again." : "The drones got this round. The courtyard is waiting.", 162, 375, 24, Fade(White, .7f));
        Line(162, 440, 1275);
        string[] values = [$"{(int)(world.Score * eased):N0}", $"{(int)(world.Kills * eased):00}", $"{Accuracy(world)}%", $"{(int)world.Elapsed / 60:00}:{(int)world.Elapsed % 60:00}"];
        string[] labels = ["FINAL SCORE", "DRONES DISABLED", "ACCURACY", "TIME IN ARENA"];
        for (int i = 0; i < 4; i++) { Label(labels[i], 162 + i * 325, 476); Title(values[i], 157 + i * 325, 513, 65, i == 0 ? Teal : White); }
        Line(162, 613, 1275);
        if (newBest) Label($"NEW PERSONAL BEST      VICTORIES   {profile.Victories}", 162, 644, Teal);
        else Label($"PERSONAL BEST   {profile.BestScore:N0}      VICTORIES   {profile.Victories}", 162, 644);
        var action = Button("Play again", "ENTER  >", new(162, 712, 350, 65), UiAction.Start, true);
        var menu = Button("Main menu", "", new(531, 712, 240, 65), UiAction.Menu); if (menu != UiAction.None) action = menu;
        if (IsKeyPressed(KeyboardKey.Enter)) return UiAction.Start;
        if (SaveNotice.Length > 0) Text(SaveNotice, 162, 815, 16, Orange);
        return action;
    }
}
