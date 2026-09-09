using Raylib_cs;
using static Raylib_cs.Raylib;

namespace Afterlight;

public sealed class Audio : IDisposable
{
    readonly Dictionary<string, Sound> sounds = [];
    readonly bool ready;
    readonly Random pitch = new(37);
    public Audio()
    {
        InitAudioDevice(); ready = IsAudioDeviceReady();
        if (!ready) return;
        Add("shot", .14f, .45f, (t, n) => (Math.Sin(Math.Tau * (310 * t - 640 * t * t)) * .5 + n * .5) * Math.Exp(-t * 35));
        Add("hit", .1f, .32f, (t, n) => Math.Sin(Math.Tau * 1500 * t) * Math.Exp(-t * 60));
        Add("kill", .25f, .42f, (t, n) => (Math.Sin(Math.Tau * (440 * t - 500 * t * t)) * .3 + n * .7) * Math.Exp(-t * 18));
        Add("reload", .36f, .24f, (t, n) => n * Math.Exp(-t * 30) + Math.Sin(Math.Tau * (330 * t + 850 * t * t)) * Math.Sin(Math.PI * t / .36) * .3);
        Add("ready", .16f, .25f, (t, n) => Math.Sin(Math.Tau * 920 * t) * Math.Exp(-t * 36));
        Add("step", .09f, .12f, (t, n) => n * Math.Exp(-t * 55));
        Add("enemy", .21f, .15f, (t, n) => Math.Sin(Math.Tau * (430 * t - 350 * t * t)) * Math.Exp(-t * 23));
        Add("damage", .25f, .4f, (t, n) => (Math.Sin(Math.Tau * 66 * t) * .65 + n * .3) * Math.Exp(-t * 17));
        Add("wave", .65f, .25f, (t, n) => Math.Sin(Math.Tau * (t < .22 ? 330 : t < .44 ? 440 : 660) * t) * Math.Sin(Math.PI * t / .65) * .45);
        Add("clear", .8f, .25f, (t, n) => (Math.Sin(Math.Tau * 440 * t) + Math.Sin(Math.Tau * 550 * t) + Math.Sin(Math.Tau * 660 * t)) / 3 * Math.Sin(Math.PI * t / .8));
        Add("win", 1.3f, .28f, (t, n) => (Math.Sin(Math.Tau * 330 * t) + Math.Sin(Math.Tau * 440 * t) + Math.Sin(Math.Tau * 550 * t)) / 3 * Math.Sin(Math.PI * t / 1.3));
        Add("lost", .85f, .25f, (t, n) => (Math.Sin(Math.Tau * (220 * t - 65 * t * t)) + Math.Sin(Math.Tau * (224 * t - 65 * t * t))) * .5 * Math.Sin(Math.PI * t / .85));
    }
    void Add(string name, float duration, float volume, Func<double, double, double> sample)
    {
        const int rate = 22050;
        int count = (int)(duration * rate);
        using MemoryStream stream = new(); using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVEfmt "u8); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        Random random = new(17);
        for (int i = 0; i < count; i++)
        {
            double fade = Math.Min(1, i / 60.0) * Math.Min(1, (count - i) / 100.0);
            writer.Write((short)(Math.Clamp(sample(i / (double)rate, random.NextDouble() * 2 - 1) * fade, -1, 1) * short.MaxValue));
        }
        var wave = LoadWaveFromMemory(".wav", stream.ToArray()); var sound = LoadSoundFromWave(wave); UnloadWave(wave);
        SetSoundVolume(sound, volume); sounds[name] = sound;
    }
    public void Update(World world, bool muted)
    {
        if (ready)
        {
            SetMasterVolume(muted ? 0 : .72f);
            foreach (string name in world.Events)
                if (sounds.TryGetValue(name, out var sound))
                {
                    if (name is "shot" or "hit") SetSoundPitch(sound, 1 + ((float)pitch.NextDouble() - .5f) * .12f);
                    PlaySound(sound);
                }
        }
        world.Events.Clear();
    }
    public void Silence() { if (ready) foreach (var sound in sounds.Values) StopSound(sound); }
    public void Dispose()
    {
        if (!ready) return;
        foreach (var sound in sounds.Values) UnloadSound(sound);
        CloseAudioDevice();
    }
}
