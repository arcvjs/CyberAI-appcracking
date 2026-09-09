using System.Numerics;
using System.Text.Json;

namespace Afterlight;

public readonly record struct Block(Vector3 Center, Vector3 Size, int Style = 0)
{
    public Vector3 Min => Center - Size / 2;
    public Vector3 Max => Center + Size / 2;
}

public sealed class Drone
{
    public Vector3 Position;
    public float Cooldown, Phase, Flash;
    public int Health;
    public bool Heavy;
    public Vector3 Center(float time) => Position + new Vector3(0, 1.85f + MathF.Sin(time * 2.5f + Phase) * .16f, 0);
}

public sealed class Bolt
{
    public Vector3 Position, Velocity;
    public float Life = 7;
}

public sealed class Spark
{
    public Vector3 Position, Velocity;
    public float Life, MaxLife;
    public bool Warm;
}

public readonly record struct Beam(Vector3 Start, Vector3 End, float Life, bool Hit);
public sealed class Floater
{
    public Vector3 Position;
    public string Text = "";
    public float Life, MaxLife = .9f;
    public bool Warm;
}
public readonly record struct Controls(Vector2 Move, bool Sprint = false, bool Fire = false, bool Reload = false);
public enum RunPhase { Active, Intermission, Won, Lost }

public sealed class World
{
    public const int Magazine = 24;
    public const float ReloadDuration = 1.25f;
    public static readonly List<Block> Blocks = BuildArena();
    public static readonly Vector3[] Spawns = [new(-16, 0, -15), new(16, 0, -15), new(-17, 0, 8), new(17, 0, 8), new(-9, 0, -17), new(9, 0, -17), new(-18, 0, -5), new(18, 0, -5)];
    readonly Random random;
    readonly int[,] navigation = new int[42, 42];
    float pathTimer, stepMark;
    public Vector3 Player = new(0, 1.65f, 15);
    public float Yaw, Pitch, Time, Elapsed, FireCooldown, ReloadLeft, DamageFlash, HitFlash, Recoil, Travel;
    public float WaveTimer = 2.6f;
    public int Health = 100, Ammo = Magazine, Wave = 1, Kills, Score, Shots, Hits;
    public bool Moving, Sprinting, LastHitKill;
    public RunPhase Phase = RunPhase.Intermission;
    public readonly List<Drone> Drones = [];
    public readonly List<Bolt> Bolts = [];
    public readonly List<Spark> Sparks = [];
    public readonly List<Beam> Beams = [];
    public readonly List<Floater> Floaters = [];
    public readonly List<string> Events = [];
    public Vector3 Forward => Vector3.Normalize(new(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), -MathF.Cos(Yaw) * MathF.Cos(Pitch)));
    public Vector3 FlatForward => new(MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
    public Vector3 Right => new(MathF.Cos(Yaw), 0, MathF.Sin(Yaw));
    public int TotalInWave => 3 + Wave * 2;
    public World(int seed = 83) { random = new Random(seed); }

    static List<Block> BuildArena() =>
    [
        new(new(-21, 2.6f, 0), new(1, 5.2f, 43), 2), new(new(21, 2.6f, 0), new(1, 5.2f, 43), 2),
        new(new(0, 2.6f, -21), new(43, 5.2f, 1), 2), new(new(0, 2.6f, 21), new(43, 5.2f, 1), 2),
        new(new(-6.6f, 2.5f, -5.5f), new(2, 5, 2), 1), new(new(6.6f, 2.5f, -5.5f), new(2, 5, 2), 1),
        new(new(-6.6f, 2.5f, 5.5f), new(2, 5, 2), 1), new(new(6.6f, 2.5f, 5.5f), new(2, 5, 2), 1),
        new(new(-14, 1.65f, -5), new(5, 3.3f, 5), 0), new(new(14, 1.65f, -5), new(5, 3.3f, 5), 0),
        new(new(-12, .65f, 10), new(4, 1.3f, 2.6f), 3), new(new(12, .65f, 10), new(4, 1.3f, 2.6f), 3),
        new(new(0, .65f, -1), new(3.6f, 1.3f, 3.6f), 4),
        new(new(-4.5f, .7f, -14), new(3.8f, 1.4f, 1.8f), 3), new(new(4.5f, .7f, -14), new(3.8f, 1.4f, 1.8f), 3),
        new(new(-3, .65f, 10), new(2.2f, 1.3f, 1.3f), 3), new(new(3, .65f, 10), new(2.2f, 1.3f, 1.3f), 3)
    ];

    public static bool CanStand(Vector3 p, float radius = .34f)
    {
        foreach (var b in Blocks)
            if (p.X > b.Min.X - radius && p.X < b.Max.X + radius && p.Z > b.Min.Z - radius && p.Z < b.Max.Z + radius) return false;
        return MathF.Abs(p.X) < 20.5f - radius && MathF.Abs(p.Z) < 20.5f - radius;
    }

    public static Vector3 MoveBody(Vector3 p, Vector3 delta, float radius)
    {
        // Small substeps prevent tunnelling through cover when a frame stalls.
        int steps = Math.Max(1, (int)MathF.Ceiling(delta.Length() / .15f));
        delta /= steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 x = p + new Vector3(delta.X, 0, 0);
            if (CanStand(x, radius)) p.X = x.X;
            Vector3 z = p + new Vector3(0, 0, delta.Z);
            if (CanStand(z, radius)) p.Z = z.Z;
        }
        return p;
    }

    public static float RayBox(Vector3 origin, Vector3 direction, Block box)
    {
        float near = 0, far = 1000;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis], min = box.Min[axis], max = box.Max[axis];
            if (MathF.Abs(d) < .00001f) { if (o < min || o > max) return float.PositiveInfinity; continue; }
            float a = (min - o) / d, b = (max - o) / d;
            near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
            if (near > far) return float.PositiveInfinity;
        }
        return near;
    }

    public static float RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius)
    {
        var offset = origin - center;
        float b = Vector3.Dot(offset, direction), c = Vector3.Dot(offset, offset) - radius * radius;
        if (c <= 0) return 0;
        float discriminant = b * b - c;
        if (discriminant < 0) return float.PositiveInfinity;
        float t = -b - MathF.Sqrt(discriminant);
        return t >= 0 ? t : float.PositiveInfinity;
    }

    public static float WallDistance(Vector3 origin, Vector3 direction)
    {
        float distance = 90;
        foreach (var b in Blocks) distance = Math.Min(distance, RayBox(origin, direction, b));
        if (direction.Y < -.001f) distance = Math.Min(distance, -origin.Y / direction.Y);
        return distance;
    }

    public static bool Visible(Vector3 from, Vector3 to)
    {
        float length = Vector3.Distance(from, to);
        return length < .001f || WallDistance(from, (to - from) / length) > length - .1f;
    }

    public void StartWave()
    {
        Phase = RunPhase.Active;
        Drones.Clear(); Bolts.Clear();
        Ammo = Magazine; ReloadLeft = 0; FireCooldown = 0;
        for (int i = 0; i < TotalInWave; i++)
        {
            // Rotate distant spawn points to avoid spawning a drone on the player.
            var candidates = Spawns.OrderByDescending(p => Vector3.DistanceSquared(p, Player)).ToArray();
            Vector3 spawn = candidates[i % candidates.Length];
            if (i >= candidates.Length) spawn += new Vector3(0, 0, 2);
            bool heavy = Wave >= 2 && i % 3 == 0;
            Drones.Add(new Drone { Position = spawn, Heavy = heavy, Health = heavy ? 3 : 2, Phase = i * 1.7f, Cooldown = 1.8f + i * .35f });
        }
        Events.Add("wave"); pathTimer = 0;
    }

    public void Reload()
    {
        if (ReloadLeft <= 0 && Ammo < Magazine && Phase is not (RunPhase.Won or RunPhase.Lost))
        { ReloadLeft = ReloadDuration; Events.Add("reload"); }
    }

    public void Shoot()
    {
        if (FireCooldown > 0 || ReloadLeft > 0 || Phase is RunPhase.Won or RunPhase.Lost) return;
        if (Ammo == 0) { Reload(); return; }
        Ammo--; Shots++; FireCooldown = .145f; Recoil = 1; Events.Add("shot");
        Vector3 direction = Forward;
        float distance = WallDistance(Player, direction);
        Drone? target = null;
        foreach (var drone in Drones)
        {
            float hit = RaySphere(Player, direction, drone.Center(Time), drone.Heavy ? .84f : .72f);
            if (hit < distance) { distance = hit; target = drone; }
        }
        Vector3 end = Player + direction * distance;
        Beams.Add(new(Player + Right * .22f - Vector3.UnitY * .17f + direction * .6f, end, .07f, target != null));
        if (target != null)
        {
            Hits++; HitFlash = .16f; target.Health--; target.Flash = .12f; LastHitKill = target.Health <= 0;
            Burst(end, 9, true);
            if (Floaters.Count > 24) Floaters.RemoveAt(0);
            if (LastHitKill)
            {
                Kills++; Score += target.Heavy ? 180 : 100;
                Burst(target.Center(Time), 28, true);
                Floaters.Add(new() { Position = target.Center(Time), Text = $"+{(target.Heavy ? 180 : 100)}", Life = .9f, Warm = true });
                Drones.Remove(target); Events.Add("kill");
            }
            else { Score += 20; Events.Add("hit"); Floaters.Add(new() { Position = end, Text = "+20", Life = .9f }); }
        }
        else Burst(end, 5, false);
        if (Ammo == 0) Reload();
    }

    public void Burst(Vector3 position, int count, bool warm)
    {
        for (int i = 0; i < count; i++)
        {
            float life = .3f + random.NextSingle() * .4f;
            Sparks.Add(new() { Position = position, Velocity = new Vector3(random.NextSingle() - .5f, random.NextSingle() - .25f, random.NextSingle() - .5f) * 6, Life = life, MaxLife = life, Warm = warm });
        }
    }

    void UpdatePaths()
    {
        pathTimer = .4f;
        for (int x = 0; x < 42; x++) for (int z = 0; z < 42; z++) navigation[x, z] = -1;
        int px = Math.Clamp((int)(Player.X + 21), 0, 41), pz = Math.Clamp((int)(Player.Z + 21), 0, 41);
        Queue<(int x, int z)> queue = new(); queue.Enqueue((px, pz)); navigation[px, pz] = 0;
        (int x, int z)[] directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.TryDequeue(out var cell))
            foreach (var d in directions)
            {
                int x = cell.x + d.x, z = cell.z + d.z;
                if (x < 0 || x >= 42 || z < 0 || z >= 42 || navigation[x, z] >= 0 || !CanStand(new(x - 20.5f, 0, z - 20.5f), .58f)) continue;
                navigation[x, z] = navigation[cell.x, cell.z] + 1; queue.Enqueue((x, z));
            }
    }

    Vector3 PathDirection(Vector3 position)
    {
        int x = Math.Clamp((int)(position.X + 21), 1, 40), z = Math.Clamp((int)(position.Z + 21), 1, 40);
        Vector3 best = Player; float cost = float.MaxValue;
        for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
        {
            if (a == 0 && b == 0 || navigation[x + a, z + b] < 0) continue;
            Vector3 candidate = new(x + a - 20.5f, 0, z + b - 20.5f);
            // Diagonals are only allowed when both adjoining cells are clear.
            if (a != 0 && b != 0 && (navigation[x + a, z] < 0 || navigation[x, z + b] < 0)) continue;
            float score = navigation[x + a, z + b] + Vector3.Distance(position, candidate) * .3f;
            if (score < cost) { cost = score; best = candidate; }
        }
        Vector3 delta = best - position; delta.Y = 0;
        return delta.LengthSquared() > .001f ? Vector3.Normalize(delta) : Vector3.Zero;
    }

    public void Update(float dt, Controls input)
    {
        dt = Math.Clamp(dt, 0, .05f);
        Time += dt;
        FireCooldown = Math.Max(0, FireCooldown - dt); HitFlash = Math.Max(0, HitFlash - dt);
        DamageFlash = Math.Max(0, DamageFlash - dt); Recoil = Math.Max(0, Recoil - dt * 7);
        for (int i = Beams.Count - 1; i >= 0; i--) { var beam = Beams[i]; if (beam.Life <= dt) Beams.RemoveAt(i); else Beams[i] = beam with { Life = beam.Life - dt }; }
        for (int i = Floaters.Count - 1; i >= 0; i--) { var floater = Floaters[i]; floater.Life -= dt; floater.Position.Y += dt * .8f; if (floater.Life <= 0) Floaters.RemoveAt(i); }
        for (int i = Sparks.Count - 1; i >= 0; i--)
        {
            var s = Sparks[i]; s.Life -= dt; s.Position += s.Velocity * dt; s.Velocity.Y -= 8 * dt;
            if (s.Life <= 0) Sparks.RemoveAt(i);
        }
        if (Phase is RunPhase.Won or RunPhase.Lost) return;
        Elapsed += dt;
        if (ReloadLeft > 0) { ReloadLeft -= dt; if (ReloadLeft <= 0) { ReloadLeft = 0; Ammo = Magazine; Events.Add("ready"); } }
        if (input.Reload) Reload();
        var move = input.Move;
        if (move.LengthSquared() > 1) move = Vector2.Normalize(move);
        Moving = move.LengthSquared() > .01f;
        Sprinting = Moving && input.Sprint && !input.Fire && ReloadLeft <= 0;
        float speed = Sprinting ? 7.6f : 4.7f;
        Player = MoveBody(Player, (Right * move.X + FlatForward * move.Y) * speed * dt, .34f);
        if (Moving)
        {
            Travel += dt * (Sprinting ? 13 : 9);
            float stride = Sprinting ? 3.4f : 2.6f;
            if (Travel - stepMark >= stride) { stepMark = Travel; Events.Add("step"); }
        }
        if (input.Fire) Shoot();
        if (Phase == RunPhase.Intermission)
        {
            WaveTimer -= dt;
            if (WaveTimer <= 0) StartWave();
            return;
        }
        pathTimer -= dt; if (pathTimer <= 0) UpdatePaths();
        foreach (var drone in Drones)
        {
            drone.Flash = Math.Max(0, drone.Flash - dt);
            var center = drone.Center(Time);
            var delta = Player - center; float distance = delta.Length();
            bool visible = Visible(center, Player);
            Vector3 direction = PathDirection(drone.Position);
            if (visible)
            {
                var flat = new Vector3(delta.X, 0, delta.Z);
                if (flat.LengthSquared() > .001f) flat = Vector3.Normalize(flat);
                direction = distance > 10 ? flat : distance < 5 ? -flat : Vector3.Zero;
                direction += new Vector3(-flat.Z, 0, flat.X) * MathF.Sin(Time * .6f + drone.Phase) * .65f;
            }
            foreach (var other in Drones)
            {
                if (other == drone) continue;
                Vector3 offset = drone.Position - other.Position; float separation = offset.Length();
                if (separation is > .01f and < 1.8f) direction += offset / separation * (1.8f - separation);
            }
            if (direction.LengthSquared() > 1) direction = Vector3.Normalize(direction);
            drone.Position = MoveBody(drone.Position, direction * (drone.Heavy ? 1.85f : 2.35f) * dt, .58f);
            drone.Cooldown -= dt;
            if (drone.Cooldown <= 0 && visible && distance < 25)
            {
                Vector3 aim = Vector3.Normalize(Player + new Vector3((random.NextSingle() - .5f) * .6f, -.1f, (random.NextSingle() - .5f) * .6f) - center);
                Bolts.Add(new() { Position = center + aim * .88f, Velocity = aim * (8 + Wave * .7f) });
                drone.Cooldown = (drone.Heavy ? 1.9f : 2.8f) + random.NextSingle() * 1.2f;
                Events.Add("enemy");
            }
        }
        for (int i = Bolts.Count - 1; i >= 0; i--)
        {
            var bolt = Bolts[i]; float length = bolt.Velocity.Length() * dt;
            var direction = Vector3.Normalize(bolt.Velocity);
            float wall = WallDistance(bolt.Position, direction);
            float playerHit = RaySphere(bolt.Position, direction, Player - Vector3.UnitY * .35f, .53f);
            bolt.Life -= dt;
            if (playerHit <= length && playerHit < wall)
            {
                Health = Math.Max(0, Health - (Wave == 3 ? 13 : 11)); DamageFlash = .35f; Events.Add("damage");
                Bolts.RemoveAt(i); continue;
            }
            if (wall <= length || bolt.Life <= 0) { Burst(bolt.Position, 4, true); Bolts.RemoveAt(i); continue; }
            bolt.Position += bolt.Velocity * dt;
        }
        if (Health <= 0) { Phase = RunPhase.Lost; Events.Add("lost"); }
        else if (Drones.Count == 0)
        {
            Bolts.Clear();
            if (Wave == 3) { Phase = RunPhase.Won; Score += Health * 10; Events.Add("win"); }
            else { Wave++; Phase = RunPhase.Intermission; WaveTimer = 4.5f; Health = Math.Min(100, Health + 30); Ammo = Magazine; ReloadLeft = 0; Events.Add("clear"); }
        }
    }
}

public sealed class Profile
{
    public int BestScore { get; set; }
    public int Victories { get; set; }
    public float Sensitivity { get; set; } = 1;
    public bool Muted { get; set; }
    public static string SavePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Afterlight", "profile.json");
    public static Profile Load()
    {
        try
        {
            var profile = JsonSerializer.Deserialize<Profile>(File.ReadAllText(SavePath)) ?? new();
            profile.Sensitivity = float.IsFinite(profile.Sensitivity) ? Math.Clamp(profile.Sensitivity, .3f, 2.5f) : 1;
            return profile;
        }
        catch { return new(); }
    }
    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            string temp = SavePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, SavePath, true); return true;
        }
        catch { return false; }
    }
}
