using System.Numerics;

namespace Afterlight;

public static class Verification
{
    public static int Run()
    {
        List<string> results = [];
        void Test(string name, Action action)
        {
            try { action(); results.Add("PASS  " + name); }
            catch (Exception e) { results.Add("FAIL  " + name + " / " + e.Message); }
        }
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Tick(World world, int frames, Controls input = default) { for (int i = 0; i < frames; i++) world.Update(1 / 60f, input); }
        Test("Arena boundaries and solid cover block player movement", () =>
        {
            Check(World.CanStand(new(0, 0, 15)), "Start must be clear");
            Check(!World.CanStand(new(-6.6f, 0, 5.5f)), "Pillar must be solid");
            var p = World.MoveBody(new(0, 0, 18), new(0, 0, 20), .34f);
            Check(p.Z <= 20.17f, "Player tunnelled through outer wall");
            p = World.MoveBody(new(-8, 0, 5.5f), new(3, 0, 1), .34f);
            Check(World.CanStand(p), "Sliding left player inside cover");
        });
        Test("Diagonal movement has the same speed as straight movement", () =>
        {
            var a = new World(); var b = new World();
            a.Update(.05f, new(new(0, 1))); b.Update(.05f, new(new(1, 1)));
            Check(MathF.Abs(Vector3.Distance(a.Player, new(0, 1.65f, 15)) - Vector3.Distance(b.Player, new(0, 1.65f, 15))) < .001f, "Diagonal speed boost");
        });
        Test("Hitscan consumes ammo, damages and disables visible drones", () =>
        {
            var w = new World(); w.Phase = RunPhase.Active;
            w.Drones.Add(new() { Position = new(0, 0, 7), Health = 2, Cooldown = 20 });
            w.Shoot(); Check(w.Ammo == 23 && w.Drones[0].Health == 1 && w.Hits == 1, "First hit failed");
            w.FireCooldown = 0; w.Shoot(); Check(w.Kills == 1 && w.Drones.Count == 0 && w.Score == 120, "Kill did not register");
        });
        Test("Cover occludes shots and enemy vision", () =>
        {
            var w = new World { Player = new(-6.6f, 1.65f, 10), Phase = RunPhase.Active };
            w.Drones.Add(new() { Position = new(-6.6f, 0, 0), Health = 2, Cooldown = 20 });
            w.Shoot(); Check(w.Hits == 0 && w.Drones[0].Health == 2, "Shot passed through pillar");
            Check(!World.Visible(w.Player, w.Drones[0].Center(0)), "Vision passed through pillar");
        });
        Test("Reload blocks firing and refills the magazine", () =>
        {
            var w = new World { Ammo = 3 };
            w.Reload(); w.Shoot(); Check(w.Ammo == 3 && w.Shots == 0, "Fired while reloading");
            Tick(w, 80); Check(w.Ammo == World.Magazine && w.ReloadLeft == 0, "Reload did not finish");
            w.Ammo = 1; w.Shoot(); Check(w.Ammo == 0 && w.ReloadLeft > 0, "Empty magazine did not auto reload");
        });
        Test("Projectile swept collisions apply damage and end a run", () =>
        {
            var w = new World { Health = 11, Phase = RunPhase.Active };
            w.Drones.Add(new() { Position = new(10, 0, -10), Health = 2, Cooldown = 20 });
            w.Bolts.Add(new() { Position = w.Player + new Vector3(0, -.35f, -1), Velocity = new(0, 0, 40) });
            w.Update(.05f, default); Check(w.Health == 0 && w.Phase == RunPhase.Lost, "Lethal projectile failed");
        });
        Test("All three waves progress and award the survival bonus once", () =>
        {
            var w = new World(); w.StartWave(); Check(w.Drones.Count == 5, "Wave 1 spawn count");
            w.Health = 55; w.Drones.Clear(); w.Update(.01f, default);
            Check(w.Wave == 2 && w.Health == 85 && w.Ammo == 24, "Recovery did not apply");
            w.StartWave(); Check(w.Drones.Count == 7, "Wave 2 spawn count");
            w.Drones.Clear(); w.Update(.01f, default); w.StartWave(); Check(w.Drones.Count == 9, "Wave 3 spawn count");
            w.Drones.Clear(); w.Update(.01f, default); Check(w.Phase == RunPhase.Won && w.Score == 1000, "Victory bonus failed");
            Tick(w, 120); Check(w.Score == 1000, "Victory bonus duplicated");
        });
        Test("Every spawn is reachable and navigation keeps drones out of walls", () =>
        {
            foreach (var spawn in World.Spawns) Check(World.CanStand(spawn, .58f), "Blocked spawn");
            var w = new World(); w.StartWave();
            for (int i = 0; i < 1400; i++)
            {
                w.Health = 100; w.Update(1 / 60f, default);
                foreach (var d in w.Drones) Check(World.CanStand(d.Position, .579f), "Drone entered a wall");
            }
            Check(w.Drones.All(d => World.Visible(d.Center(w.Time), w.Player)), "A drone could not navigate to a firing position");
        });
        Test("Finished runs cannot shoot or move", () =>
        {
            var w = new World { Phase = RunPhase.Lost }; Vector3 before = w.Player;
            w.Update(.05f, new(new(1, 1), Fire: true)); w.Shoot();
            Check(w.Player == before && w.Ammo == 24, "Finished run accepts gameplay input");
        });
        Test("A complete run can be won using normal combat and automatic reload", () =>
        {
            var w = new World(109);
            for (int i = 0; i < 9000 && w.Phase is not (RunPhase.Won or RunPhase.Lost); i++)
            {
                var target = w.Drones.Where(d => World.Visible(w.Player, d.Center(w.Time))).OrderBy(d => Vector3.DistanceSquared(w.Player, d.Position)).FirstOrDefault();
                if (target != null)
                {
                    var direction = Vector3.Normalize(target.Center(w.Time) - w.Player);
                    w.Yaw = MathF.Atan2(direction.X, -direction.Z); w.Pitch = MathF.Asin(direction.Y);
                }
                w.Update(1 / 60f, new(Vector2.Zero, Fire: target != null));
            }
            Check(w.Phase == RunPhase.Won && w.Kills == 21, $"Run ended {w.Phase}, kills {w.Kills}, health {w.Health}");
            Check(w.Shots >= 46 && w.Ammo >= 0 && w.Health > 0, "Invalid combat totals");
        });
        string output = string.Join(Environment.NewLine, results) + Environment.NewLine;
        string path = Path.Combine(AppContext.BaseDirectory, "verification.txt"); File.WriteAllText(path, output);
        Console.Write(output);
        return results.Any(r => r.StartsWith("FAIL")) ? 1 : 0;
    }
}
