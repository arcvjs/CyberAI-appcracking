using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Afterlight;

public sealed class Scene : IDisposable
{
    public static readonly Color Teal = new(101, 233, 213, 255);
    public static readonly Color Orange = new(255, 125, 77, 255);
    public static readonly Color Ink = new(15, 29, 37, 255);
    public static readonly Color White = new(241, 238, 225, 255);
    readonly Shader lighting, finish;
    readonly int eyeLocation, timeLocation;
    readonly RenderTexture2D weaponTarget;
    public readonly Font Regular, Bold, Display;
    readonly bool[] ownFonts = new bool[3];
    static readonly Drone[] MenuDrones = [new() { Position = new Vector3(3, 0, -7), Phase = 0, Health = 2, Cooldown = 3 }, new() { Position = new Vector3(6.4f, 0, -3), Phase = 1, Heavy = true, Health = 2, Cooldown = 3 }, new() { Position = new Vector3(9.8f, 0, 1), Phase = 2, Health = 2, Cooldown = 3 }];
    public readonly RenderTexture2D Target;
    public readonly RenderTexture2D FinalTarget;
    public const int Width = 1600, Height = 900;

    public Scene()
    {
        Regular = FontFromWindows("segoeui.ttf", 48, 0);
        Bold = FontFromWindows("seguisb.ttf", 64, 1);
        Display = FontFromWindows("bahnschrift.ttf", 144, 2);
        Target = LoadRenderTexture(Width, Height);
        FinalTarget = LoadRenderTexture(Width, Height);
        weaponTarget = LoadRenderTexture(Width, Height);
        SetTextureFilter(Target.Texture, TextureFilter.Bilinear);
        SetTextureFilter(FinalTarget.Texture, TextureFilter.Bilinear);
        lighting = LoadShaderFromMemory(VertexShader, FragmentShader);
        finish = LoadShaderFromMemory(null, FinishShader);
        eyeLocation = GetShaderLocation(lighting, "eye");
        timeLocation = GetShaderLocation(finish, "time");
    }

    Font FontFromWindows(string filename, int size, int index)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), filename);
        if (!File.Exists(path)) return GetFontDefault();
        var font = LoadFontEx(path, size, null, 0);
        if (!IsFontValid(font)) return GetFontDefault();
        ownFonts[index] = true; SetTextureFilter(font.Texture, TextureFilter.Bilinear); return font;
    }

    public Camera3D Camera(World world, bool menu, float clock)
    {
        if (menu)
        {
            var position = new Vector3(14.5f + MathF.Sin(clock * .065f) * 1.8f, 8.2f, 17.5f);
            return new Camera3D { Position = position, Target = new(-2, 1.1f, -3), Up = Vector3.UnitY, FovY = 54, Projection = CameraProjection.Perspective };
        }
        float bob = world.Moving ? MathF.Sin(world.Travel) * .028f : MathF.Sin(clock * 1.6f) * .007f;
        var eye = world.Player + Vector3.UnitY * bob;
        return new Camera3D { Position = eye, Target = eye + world.Forward, Up = Vector3.Normalize(Vector3.UnitY + world.Right * (world.Moving ? MathF.Sin(world.Travel * .5f) * .006f : 0)), FovY = world.Sprinting ? 76 : 70, Projection = CameraProjection.Perspective };
    }

    public void DrawWorld(World world, Camera3D camera, float clock, bool menu)
    {
        DrawRectangleGradientV(0, 0, Width, Height, new(79, 120, 137, 255), new(243, 187, 140, 255));
        var sun = GetWorldToScreen(new Vector3(-70, 47, -130), camera);
        if (Vector3.Dot(new Vector3(-70, 47, -130) - camera.Position, camera.Target - camera.Position) > 0)
        {
            DrawCircleGradient(sun, 220, new(255, 218, 159, 42), Color.Blank);
            DrawCircleV(sun, 64, new(255, 224, 172, 255));
        }
        BeginMode3D(camera);
        SetShaderValue(lighting, eyeLocation, camera.Position, ShaderUniformDataType.Vec3);
        BeginShaderMode(lighting);
        // A restrained, handmade skyline gives the small courtyard a larger setting.
        for (int i = 0; i < 17; i++)
        {
            float x = (i - 8) * 7.5f, height = 6 + (i * 7 % 15);
            Cube(new(x, height / 2 - 1, -37 - i % 3 * 6), new(5.5f, height, 7), new(117 + i % 3 * 8, 134, 135, 255));
            Cube(new(x + 1, height - .3f, -37 - i % 3 * 6), new(.08f, 3, .08f), new(67, 87, 96, 255));
        }
        Cube(new(0, -.32f, 0), new(44, .6f, 44), new(65, 83, 87, 255));
        // Inlaid floor panels and a painted route through the arena.
        for (int i = -20; i <= 20; i += 3)
        {
            Cube(new(i, .003f, 0), new(.025f, .01f, 41), new(80, 98, 100, 255));
            Cube(new(0, .003f, i), new(41, .01f, .025f), new(80, 98, 100, 255));
        }
        Cube(new(0, .011f, 0), new(7.2f, .018f, 38), new(74, 93, 95, 255));
        foreach (float x in new[] { -4.1f, 4.1f })
        {
            Cube(new(x, .024f, 0), new(.09f, .015f, 34), new(139, 180, 171, 255));
            for (int z = -16; z <= 16; z += 4) Cube(new(x + MathF.Sign(x) * .3f, .025f, z), new(.3f, .015f, .08f), new(160, 175, 164, 255));
        }
        for (int z = 3; z <= 17; z += 5)
        {
            var c = new Color(177, 190, 174, 255);
            DrawTriangle3D(new(-.62f, .04f, z + .3f), new(0, .04f, z - .35f), new(-.62f, .04f, z + .05f), c);
            DrawTriangle3D(new(0, .04f, z - .35f), new(.62f, .04f, z + .3f), new(.62f, .04f, z + .05f), c);
        }
        foreach (var block in World.Blocks) DrawArchitecture(block);
        // Back wall: oversized doorway, inset panels, and a turquoise lintel.
        Cube(new(0, 2, -20.42f), new(7.8f, 4, .18f), Ink);
        Cube(new(0, 2.05f, -20.30f), new(6.8f, 3.6f, .12f), new(48, 68, 75, 255));
        Cube(new(0, 2, -20.2f), new(.055f, 3.5f, .08f), Teal);
        Cube(new(0, 4.17f, -20.2f), new(8, .13f, .14f), Teal);
        for (int x = -18; x <= 18; x += 6)
        {
            if (x == 0) continue;
            Cube(new(x, 2.7f, -20.4f), new(3.3f, 1.65f, .16f), new(143, 152, 143, 255));
            Cube(new(x, 3.9f, -20.3f), new(1.1f, .06f, .09f), White);
        }
        // Side frames, striped panels, and small planters.
        foreach (int side in new[] { -1, 1 })
        {
            Cube(new(side * 18.3f, 4.9f, -7), new(5, .45f, 9), new(186, 178, 155, 255));
            for (int z = -10; z <= -4; z += 2) Cube(new(side * 18.3f, 4.64f, z), new(5, .15f, .12f), Ink);
            foreach (int z in new[] { -16, 15 })
            {
                Cube(new(side * 17.9f, .35f, z), new(2.6f, .7f, 2.6f), new(121, 133, 126, 255));
                for (int leaf = 0; leaf < 5; leaf++)
                    DrawCylinderEx(new(side * 17.9f + MathF.Sin(leaf * 2) * .45f, .7f, z + MathF.Cos(leaf * 2) * .45f), new(side * 17.9f + MathF.Sin(leaf * 2) * .8f, 1.3f + leaf * .13f, z + MathF.Cos(leaf * 2) * .8f), .2f, .02f, 4, new(73, 121 + leaf * 4, 108, 255));
            }
        }
        // A low central power unit and a softly pulsing energy ring.
        DrawCylinder(new(0, .03f, -1), 3.1f, 3.1f, .08f, 48, new(41, 65, 72, 255));
        for (int i = 0; i < 28; i++)
        {
            float angle = i * MathF.Tau / 28;
            Cube(new(MathF.Sin(angle) * 2.8f, .085f, -1 + MathF.Cos(angle) * 2.8f), new(.12f, .025f, .12f), Teal);
        }
        if (menu) foreach (var drone in MenuDrones) DrawDrone(drone, clock, camera.Position);
        else foreach (var drone in world.Drones) DrawDrone(drone, world.Time, world.Player);
        EndShaderMode();
        foreach (var bolt in world.Bolts)
        {
            Vector3 tail = bolt.Position - Vector3.Normalize(bolt.Velocity) * .45f;
            DrawCylinderEx(tail, bolt.Position, .035f, .075f, 6, Orange);
            DrawSphereEx(bolt.Position, .09f, 5, 6, new(255, 216, 158, 255));
        }
        foreach (var spark in world.Sparks)
            Cube(spark.Position, new Vector3(.045f + .05f * spark.Life / spark.MaxLife), Fade(spark.Warm ? Orange : Teal, spark.Life / spark.MaxLife));
        foreach (var beam in world.Beams) DrawCylinderEx(beam.Start, beam.End, .008f, .008f, 4, Fade(Teal, Math.Clamp(beam.Life / .07f, 0, 1)));
        EndMode3D();
        if (!menu) DrawEnemyMarkers(world, camera);
    }

    static void DrawArchitecture(Block b)
    {
        Color concrete = new(184, 181, 157, 255), cap = new(216, 209, 182, 255);
        // Long directional shadows, kept simple to suit the low-poly architecture.
        if (b.Style != 2) Cube(new(b.Center.X + 1.3f, .018f, b.Center.Z + .9f), new(b.Size.X + 2.6f, .012f, b.Size.Z + 1.8f), new(45, 67, 74, 255));
        switch (b.Style)
        {
            case 1:
                Cube(b.Center, b.Size, concrete);
                Cube(b.Center with { Y = .24f }, new(2.16f, .48f, 2.16f), new(66, 87, 91, 255));
                Cube(b.Center with { Y = 5.03f }, new(2.16f, .26f, 2.16f), cap);
                Cube(b.Center + new Vector3(0, .05f, 1.016f), new(.12f, 3.5f, .035f), Teal);
                Cube(b.Center + new Vector3(0, .05f, -1.016f), new(.12f, 3.5f, .035f), Teal);
                Cube(b.Center + new Vector3(1.016f, 0, 0), new(.035f, .35f, 1.5f), new(229, 124, 80, 255));
                break;
            case 2:
                Cube(b.Center, b.Size, new(163, 169, 152, 255));
                Cube(b.Center with { Y = .35f }, b.Size with { Y = .7f }, new(65, 87, 91, 255));
                Cube(b.Center with { Y = 5.2f }, b.Size + new Vector3(.12f, -5.05f, .12f), cap);
                break;
            case 3:
                Cube(b.Center, b.Size, new(54, 79, 86, 255));
                Cube(b.Center with { Y = b.Size.Y + .015f }, new(b.Size.X+.04f,.1f,b.Size.Z+.04f), new(120, 145, 139, 255));
                Cube(b.Center + new Vector3(0, .12f, b.Size.Z / 2 + .01f), new(b.Size.X * .7f, .07f, .025f), Teal);
                foreach (float x in new[] { -.36f, .36f }) Cube(b.Center + new Vector3(b.Size.X * x, 0, 0), new(.075f, b.Size.Y + .025f, b.Size.Z + .03f), new(162, 176, 159, 255));
                break;
            case 4:
                Cube(b.Center, b.Size, new(52, 76, 83, 255));
                Cube(b.Center with { Y = 1.32f }, new(3.2f, .18f, 3.2f), new(144, 168, 154, 255));
                Cube(b.Center with { Y = 1.43f }, new(1.3f, .08f, 1.3f), Teal);
                foreach (int side in new[] { -1, 1 }) Cube(b.Center + new Vector3(0, .1f, side * 1.805f), new(2.8f, .13f, .035f), Teal);
                break;
            default:
                Cube(b.Center, b.Size, concrete);
                Cube(b.Center with { Y = b.Size.Y + .08f }, b.Size + new Vector3(.3f, -b.Size.Y + .16f, .3f), cap);
                Cube(b.Center + new Vector3(0, .1f, b.Size.Z / 2 + .01f), new(b.Size.X * .8f, 1.5f, .04f), new(46, 70, 79, 255));
                for (int i = -2; i <= 2; i++) Cube(b.Center + new Vector3(i * .65f, .1f, b.Size.Z / 2 + .05f), new(.045f, 1.45f, .04f), new(141, 162, 152, 255));
                Cube(b.Center + new Vector3(0, 1.2f, b.Size.Z / 2 + .05f), new(2.5f, .09f, .04f), Orange);
                break;
        }
    }

    static void DrawDrone(Drone drone, float clock, Vector3 player)
    {
        Vector3 p = drone.Center(clock);
        float scale = drone.Heavy ? 1.2f : 1;
        var body = drone.Flash > 0 ? White : drone.Heavy ? new Color(183, 94, 65, 255) : new Color(195, 192, 166, 255);
        DrawCylinder(drone.Position + new Vector3(.3f, .04f, .3f), .85f * scale, .85f * scale, .018f, 20, new(39, 62, 70, 255));
        Vector3 direction = player - p; direction.Y = 0;
        if (direction.LengthSquared() < .001f) direction = -Vector3.UnitZ;
        direction = Vector3.Normalize(direction);
        Vector3 side = Vector3.Cross(direction, Vector3.UnitY);
        DrawSphereEx(p, .66f * scale, 4, 8, body);
        Cube(p + new Vector3(0, -.13f * scale, 0), new Vector3(.77f, .35f, .77f) * scale, new(32, 48, 58, 255));
        foreach (int s in new[] { -1, 1 })
        {
            Vector3 wing = p + side * s * .89f * scale;
            DrawCylinderEx(p + side * s * .45f * scale, wing, .085f, .085f, 6, Ink);
            Cube(wing, new Vector3(.3f, .38f, .55f) * scale, body);
            DrawCylinder(wing - Vector3.UnitY * .25f * scale, .13f * scale, .06f, .25f + MathF.Sin(clock * 20 + drone.Phase) * .06f, 7, Teal);
            DrawCylinder(wing - Vector3.UnitY * .22f * scale, .08f, .06f, .1f, 6, White);
        }
        Vector3 face = p + direction * .6f * scale + Vector3.UnitY * .09f;
        DrawSphereEx(face, .3f * scale, 5, 8, Ink);
        var eye = drone.Cooldown < .55f ? new Color(255, 218, 154, 255) : Orange;
        DrawSphereEx(face + direction * .22f * scale, .16f * scale, 5, 8, eye);
        Cube(p + Vector3.UnitY * .63f * scale, new(.06f, .25f, .06f), Ink);
        DrawSphereEx(p + Vector3.UnitY * .79f * scale, .06f, 4, 5, Orange);
    }

    void DrawEnemyMarkers(World world, Camera3D camera)
    {
        foreach (var drone in world.Drones)
        {
            var p = drone.Center(world.Time);
            if (drone.Flash <= 0 || !World.Visible(world.Player, p) || Vector3.Dot(p - camera.Position, world.Forward) < 0) continue;
            var screen = GetWorldToScreen(p + Vector3.UnitY * 1.03f, camera);
            int total = drone.Heavy ? 3 : 2;
            for (int i = 0; i < total; i++) DrawRectangle((int)screen.X - total * 8 + i * 16, (int)screen.Y, 12, 4, i < drone.Health ? Orange : Fade(White, .3f));
        }
    }

    public void DrawWeapon(World world, float clock)
    {
        BeginTextureMode(weaponTarget); ClearBackground(Color.Blank);
        var camera = new Camera3D { Position = Vector3.Zero, Target = -Vector3.UnitZ, Up = Vector3.UnitY, FovY = 60, Projection = CameraProjection.Perspective };
        BeginMode3D(camera);
        BeginShaderMode(lighting); SetShaderValue(lighting, eyeLocation, Vector3.Zero, ShaderUniformDataType.Vec3);
        float reload = world.ReloadLeft > 0 ? MathF.Sin((1 - world.ReloadLeft / World.ReloadDuration) * MathF.PI) : 0;
        float bob = world.Moving ? MathF.Sin(world.Travel) * .012f : MathF.Sin(clock * 1.8f) * .003f;
        Vector3 origin = new(.30f + bob * .6f, -.32f + bob - reload * .32f - (world.Sprinting ? .05f : 0), -.67f + world.Recoil * .065f + reload * .12f);
        // Original low-poly pulse carbine: cream shell, exposed dark rail and emissive cell.
        Cube(origin + new Vector3(.018f, -.17f, .22f), new(.14f, .23f, .33f), new(48, 68, 72, 255));
        Cube(origin + new Vector3(.006f, -.115f, .10f), new(.13f, .14f, .16f), new(83, 116, 115, 255));
        Cube(origin, new(.185f, .16f, .5f), new(205, 200, 177, 255));
        Cube(origin + new Vector3(0, -.065f, -.03f), new(.19f, .07f, .42f), new(45, 60, 65, 255));
        Cube(origin + new Vector3(.097f, .004f, -.06f), new(.014f, .06f, .26f), new(55, 78, 82, 255));
        Cube(origin + new Vector3(.106f, .009f, -.07f), new(.008f, .022f, .18f), Teal);
        Cube(origin + new Vector3(0, .095f, .015f), new(.105f, .035f, .44f), new(34, 49, 56, 255));
        Cube(origin + new Vector3(0, .145f, .10f), new(.085f, .07f, .05f), new(45, 62, 66, 255));
        Cube(origin + new Vector3(0, .188f, .09f), new(.026f, .018f, .018f), Teal);
        Cube(origin + new Vector3(0, .128f, -.18f), new(.021f, .04f, .025f), Teal);
        Cube(origin + new Vector3(0, -.005f, -.32f), new(.11f, .09f, .16f), new(30, 45, 53, 255));
        Cube(origin + new Vector3(0, -.005f, -.4f), new(.125f, .11f, .04f), new(75, 98, 100, 255));
        Cube(origin + new Vector3(0, -.005f, -.424f), new(.065f, .046f, .009f), Ink);
        for (int i = 0; i < 4; i++) Cube(origin + new Vector3(-.093f, .015f, -.13f + i * .045f), new(.006f, .055f, .018f), new(55, 78, 82, 255));
        Cube(origin + new Vector3(-.09f, -.13f, -.15f), new(.10f, .11f, .19f), new(61, 86, 88, 255));
        Cube(origin + new Vector3(-.14f, -.23f, -.045f), new(.13f, .20f, .18f), new(123, 147, 133, 255));
        EndShaderMode();
        if (world.Recoil > .58f)
        {
            var muzzle = origin + new Vector3(0, -.005f, -.46f);
            DrawSphereEx(muzzle, .075f * world.Recoil, 4, 6, White);
            DrawCylinderEx(muzzle, muzzle - Vector3.UnitZ * .21f, .08f, 0, 5, Teal);
        }
        EndMode3D(); EndTextureMode();
    }

    public void CompositeWeapon() => DrawTextureRec(weaponTarget.Texture, new(0, 0, Width, -Height), Vector2.Zero, Color.White);

    public void Present(float clock)
    {
        float scale = Math.Min(GetScreenWidth() / (float)Width, GetScreenHeight() / (float)Height);
        float w = Width * scale, h = Height * scale;
        DrawTexturePro(FinalTarget.Texture, new(0, 0, Width, -Height), new((GetScreenWidth() - w) / 2, (GetScreenHeight() - h) / 2, w, h), Vector2.Zero, 0, Color.White);
    }

    public void PostProcess(float clock)
    {
        SetShaderValue(finish, timeLocation, clock, ShaderUniformDataType.Float);
        BeginShaderMode(finish);
        DrawTextureRec(Target.Texture, new(0, 0, Width, -Height), Vector2.Zero, Color.White);
        EndShaderMode();
    }

    public Vector2 Mouse()
    {
        float scale = Math.Min(GetScreenWidth() / (float)Width, GetScreenHeight() / (float)Height);
        return (GetMousePosition() - new Vector2((GetScreenWidth() - Width * scale) / 2, (GetScreenHeight() - Height * scale) / 2)) / scale;
    }
    static void Cube(Vector3 position, Vector3 size, Color color) => DrawCubeV(position, size, color);
    public void Dispose()
    {
        UnloadRenderTexture(Target); UnloadRenderTexture(FinalTarget); UnloadRenderTexture(weaponTarget); UnloadShader(lighting); UnloadShader(finish);
        if (ownFonts[0]) UnloadFont(Regular); if (ownFonts[1]) UnloadFont(Bold); if (ownFonts[2]) UnloadFont(Display);
    }
    const string VertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec3 worldPosition;
        out vec3 normal;
        out vec4 color;
        void main() {
            worldPosition = vertexPosition;
            normal = vertexNormal;
            color = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;
    const string FragmentShader = """
        #version 330
        in vec3 worldPosition;
        in vec3 normal;
        in vec4 color;
        uniform vec3 eye;
        out vec4 finalColor;
        void main() {
            vec3 n = normalize(normal);
            float diffuse = max(dot(n, normalize(vec3(-0.5, 0.85, 0.35))), 0.0);
            vec3 lit = color.rgb * (vec3(0.71, 0.75, 0.76) + vec3(0.35, 0.29, 0.20) * diffuse);
            float fog = smoothstep(16.0, 100.0, distance(eye, worldPosition)) * 0.83;
            lit = mix(lit, vec3(0.64, 0.69, 0.65), fog);
            finalColor = vec4(lit, color.a);
        }
        """;
    const string FinishShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float time;
        out vec4 finalColor;
        void main() {
            vec2 uv = fragTexCoord;
            vec2 texel = vec2(1.0/1600.0, 1.0/900.0);
            vec3 nw = texture(texture0, uv + vec2(-1.0, -1.0) * texel).rgb;
            vec3 ne = texture(texture0, uv + vec2(1.0, -1.0) * texel).rgb;
            vec3 sw = texture(texture0, uv + vec2(-1.0, 1.0) * texel).rgb;
            vec3 se = texture(texture0, uv + vec2(1.0, 1.0) * texel).rgb;
            vec3 center = texture(texture0, uv).rgb;
            vec3 luma = vec3(0.299, 0.587, 0.114);
            float lnw = dot(nw,luma), lne = dot(ne,luma), lsw = dot(sw,luma), lse = dot(se,luma), lm = dot(center,luma);
            vec2 dir = vec2(-((lnw+lne)-(lsw+lse)), (lnw+lsw)-(lne+lse));
            float reduce = max((lnw+lne+lsw+lse)*0.03125, 0.0078125);
            float rcp = 1.0/(min(abs(dir.x),abs(dir.y))+reduce);
            dir = clamp(dir*rcp, vec2(-8.0),vec2(8.0))*texel;
            vec3 a = 0.5*(texture(texture0,uv+dir*(-1.0/6.0)).rgb+texture(texture0,uv+dir*(1.0/6.0)).rgb);
            vec3 b = a*0.5+0.25*(texture(texture0,uv+dir*(-0.5)).rgb+texture(texture0,uv+dir*0.5).rgb);
            float lb = dot(b,luma);
            float lmin = min(lm,min(min(lnw,lne),min(lsw,lse)));
            float lmax = max(lm,max(max(lnw,lne),max(lsw,lse)));
            vec3 c = (lb<lmin||lb>lmax)?a:b;
            c = mix(vec3(dot(c,luma)),c,1.08);
            float v = uv.x * uv.y * (1.0-uv.x) * (1.0-uv.y);
            c *= 0.83 + 0.17 * pow(clamp(v * 16.0, 0.0, 1.0), 0.3);
            finalColor = vec4(c, 1.0);
        }
        """;
}
