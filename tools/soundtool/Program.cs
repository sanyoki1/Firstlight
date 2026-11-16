using System;
using System.IO;
using Firstlight.Game;

// Renders the river loops to WAV files for listening: tool <outdir>
static class Program
{
    const int Rate = 44100;

    static void Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var babble = WaterSynth.Babble();
        Console.WriteLine($"babble {sw.ElapsedMilliseconds} ms"); sw.Restart();
        var rush = WaterSynth.Rush();
        Console.WriteLine($"rush {sw.ElapsedMilliseconds} ms");
        var old = OldRiver();
        Stats("babble", babble, 2); Stats("rush", rush, 2); Stats("old", old, 1);
        Wav(Path.Combine(dir, "river-close.wav"), babble, 2);
        Wav(Path.Combine(dir, "river-far.wav"), rush, 2);
        Wav(Path.Combine(dir, "river-old.wav"), old, 1);

        // A demo as the game would mix it: old river 6 s, then the new one close up, zooming out, and back in.
        int n = Rate * 40; var demo = new float[n * 2];
        int nb = babble.Length / 2, nr = rush.Length / 2;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate, l, r;
            if (t < 6f)
            {
                float e = Math.Min(1f, Math.Min(t / 0.5f, (6f - t) / 0.5f));
                l = r = old[i % old.Length] * 0.38f * e;
            }
            else
            {
                float u = t - 6f;
                float zoom = Sat((u - 12f) / 6f) - Sat((u - 24f) / 6f);        // close 12 s, out over 6, far 6, back in
                float e = Math.Min(1f, Math.Min(u / 0.5f, (34f - u) / 0.5f));
                float vb = 0.30f * (1f - zoom) * (1f - zoom), vr = 0.20f * (0.25f + 0.75f * zoom);   // as SoundScape mixes them
                l = e * (babble[i % nb * 2] * vb + rush[i % nr * 2] * vr);
                r = e * (babble[i % nb * 2 + 1] * vb + rush[i % nr * 2 + 1] * vr);
            }
            demo[i * 2] = l; demo[i * 2 + 1] = r;
        }
        Wav(Path.Combine(dir, "river-demo.wav"), demo, 2);
    }

    static float Sat(float v) => Math.Max(0f, Math.Min(1f, v));

    static void Stats(string name, float[] d, int ch)
    {
        double sq = 0, sum = 0; float peak = 0;
        foreach (var v in d) { sq += v * v; sum += v; peak = Math.Max(peak, Math.Abs(v)); }
        Console.WriteLine($"{name}: {d.Length / ch / (float)Rate:F1} s, peak {peak:F3}, rms {Math.Sqrt(sq / d.Length):F3}, dc {sum / d.Length:F5}, join {Math.Abs(d[0] - d[d.Length - ch]):F4}");
    }

    static void Wav(string path, float[] d, int ch)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = d.Length * 2;
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(Rate); w.Write(Rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
        foreach (var v in d) w.Write((short)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 32767f));
    }

    // The river as it was before (for comparison).
    static float[] OldRiver()
    {
        var r = new Random(1234);
        int n = Rate * 7; var d = new float[n];
        float lp1 = 0, lp2 = 0, hp = 0, prev = 0;
        for (int i = 0; i < n; i++)
        {
            float w = (float)(r.NextDouble() * 2 - 1);
            lp1 += (w - lp1) * 0.25f; lp2 += (lp1 - lp2) * 0.25f;
            hp = 0.97f * (hp + lp2 - prev); prev = lp2;
            float t = i / (float)Rate;
            d[i] = hp * (0.8f + 0.2f * MathF.Sin(t * 5.1f) * MathF.Sin(t * 2.3f));
        }
        int fade = Rate / 2, m = n - fade;
        var o = new float[m];
        for (int i = 0; i < m; i++) o[i] = d[i];
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; o[i] = d[i] * MathF.Sqrt(k) + d[m + i] * MathF.Sqrt(1 - k); }
        float peak = 0.0001f; foreach (var v in o) peak = Math.Max(peak, Math.Abs(v));
        for (int i = 0; i < m; i++) o[i] *= 0.8f / peak;
        return o;
    }
}
