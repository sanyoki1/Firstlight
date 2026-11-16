using System;

namespace Firstlight.Game
{
    /// <summary>
    /// The river's two sound loops, made in code (plain C#, no Unity calls, so it can run on a worker thread and in the
    /// stand-alone listening tool in runs/sound). Both are stereo, interleaved, and repeat without a seam.
    /// <para><b>Babble</b> is the river heard from close by: what makes a brook sound like water is air bubbles ringing
    /// as they are pulled under, each a short tone that slides up in pitch (small bubbles high and brief, large ones low
    /// and longer). A few thousand of them are placed at a handful of "riffles" across the stereo field, each riffle
    /// gurgling in its own rhythm, over a soft wash of filtered noise that stands in for the countless bubbles too small
    /// to pick out.</para>
    /// <para><b>Rush</b> is the river heard from far off: the same water with the detail lost, a low, slowly swelling
    /// wash.</para>
    /// </summary>
    public static class WaterSynth
    {
        public const int Rate = 44100;
        const int Seconds = 20;
        const int Frames = Rate * Seconds;
        const int Ctl = 100;                       // the slow swells are worked out every 100 samples

        struct Rng
        {
            uint _s;
            public Rng(uint seed) { _s = seed == 0 ? 1u : seed; }
            public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s >> 8) * (1f / 16777216f); }
            public float Signed() => Next() * 2f - 1f;
            public float Range(float a, float b) => a + (b - a) * Next();
        }

        /// <summary>A two-pole filter (the usual "cookbook" low-pass / high-pass).</summary>
        struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _z1, _z2;

            public static Biquad Low(float hz, float q = 0.707f) => Make(hz, q, false);
            public static Biquad High(float hz, float q = 0.707f) => Make(hz, q, true);

            static Biquad Make(float hz, float q, bool high)
            {
                double w = 2 * Math.PI * hz / Rate, c = Math.Cos(w), al = Math.Sin(w) / (2 * q), a0 = 1 + al;
                double b1 = high ? -(1 + c) : 1 - c, b0 = Math.Abs(b1) / 2;
                return new Biquad { _b0 = (float)(b0 / a0), _b1 = (float)(b1 / a0), _b2 = (float)(b0 / a0), _a1 = (float)(-2 * c / a0), _a2 = (float)((1 - al) / a0) };
            }

            public float Run(float x)
            {
                float y = _b0 * x + _z1;
                _z1 = _b1 * x - _a1 * y + _z2;
                _z2 = _b2 * x - _a2 * y;
                return y;
            }
        }

        /// <summary>
        /// One channel of filtered noise that loops: made a little too long, the tail cross-faded into the start (equal
        /// power, the two ends being unrelated), and scaled so its RMS is 1.
        /// </summary>
        static float[] Noise(ref Rng r, bool pink, params Biquad[] chain)
        {
            int fade = Rate / 2, warm = 4000, total = Frames + fade;
            var d = new float[total];
            float p0 = 0, p1 = 0, p2 = 0;
            for (int i = -warm; i < total; i++)
            {
                float x = r.Signed();
                if (pink)
                {
                    p0 = 0.99765f * p0 + x * 0.0990460f; p1 = 0.96300f * p1 + x * 0.2965164f; p2 = 0.57000f * p2 + x * 1.0526913f;
                    x = p0 + p1 + p2 + x * 0.1848f;
                }
                for (int k = 0; k < chain.Length; k++) x = chain[k].Run(x);
                if (i >= 0) d[i] = x;
            }
            var o = new float[Frames];
            Array.Copy(d, o, Frames);
            for (int i = 0; i < fade; i++)
            {
                float k = i / (float)fade;
                o[i] = d[i] * (float)Math.Sqrt(k) + d[Frames + i] * (float)Math.Sqrt(1 - k);
            }
            double sq = 0; for (int i = 0; i < Frames; i++) sq += o[i] * o[i];
            float g = 1f / (float)Math.Sqrt(sq / Frames + 1e-12);
            for (int i = 0; i < Frames; i++) o[i] *= g;
            return o;
        }

        /// <summary>
        /// A slow, irregular swell between about -1 and 1 that loops exactly: four sines, each with a whole number of
        /// cycles in the loop (between <paramref name="kMin"/> and <paramref name="kMax"/>), at control rate.
        /// </summary>
        static float[] Swell(ref Rng r, int kMin, int kMax)
        {
            int n = Frames / Ctl;
            var o = new float[n];
            float[] wt = { 1f, 0.7f, 0.5f, 0.35f };
            for (int s = 0; s < 4; s++)
            {
                int k = kMin + (int)(r.Next() * (kMax - kMin + 0.999f));
                float ph = r.Next() * 6.2831853f, w = wt[s] / 2.55f;
                for (int i = 0; i < n; i++) o[i] += w * (float)Math.Sin(6.2831853 * k * i / n + ph);
            }
            return o;
        }

        static float At(float[] swell, int frame)
        {
            int i = frame / Ctl, j = i + 1 == swell.Length ? 0 : i + 1;
            float f = (frame - i * Ctl) / (float)Ctl;
            return swell[i] + (swell[j] - swell[i]) * f;
        }

        /// <summary>The river from close by (stereo, interleaved).</summary>
        public static float[] Babble()
        {
            var r = new Rng(90210);
            var o = new float[Frames * 2];

            // The bed of noise under the bubbles, each side its own.
            for (int ch = 0; ch < 2; ch++)
            {
                var wash = Noise(ref r, true, Biquad.High(420), Biquad.Low(2100), Biquad.Low(5200));
                var fizz = Noise(ref r, false, Biquad.High(3200), Biquad.High(3200), Biquad.Low(8500));
                var body = Noise(ref r, true, Biquad.Low(240), Biquad.Low(240), Biquad.High(60));
                float[] sWash = Swell(ref r, 2, 11), sFizz = Swell(ref r, 9, 60), sBody = Swell(ref r, 2, 7);
                for (int i = 0; i < Frames; i++)
                {
                    float fz = 0.65f + 0.35f * At(sFizz, i);
                    o[i * 2 + ch] = wash[i] * 0.20f * (0.75f + 0.25f * At(sWash, i))
                                  + fizz[i] * 0.045f * fz
                                  + body[i] * 0.06f * (0.8f + 0.2f * At(sBody, i));
                }
            }

            // The bubbles. Each riffle has its own place, its own range of bubble sizes and its own rhythm.
            const int riffles = 10;
            const float rMin = 0.0007f, rMax = 0.006f;      // bubble radius in metres: rings at about 4300 Hz down to 500 Hz
            var bub = new float[Frames * 2];
            for (int s = 0; s < riffles; s++)
            {
                float pan = (s + 0.5f) / riffles * 2f - 1f + r.Range(-0.08f, 0.08f);
                float a = (pan * 0.85f + 1f) * 0.25f * (float)Math.PI;
                float gl = (float)Math.Cos(a), gr = (float)Math.Sin(a);
                // Most riffles are small water (trickle, high); a few carry the low "glug".
                float big = s % 3 == 0 ? r.Range(0.7f, 0.95f) : r.Range(0.3f, 0.65f);
                float hi = rMin * (float)Math.Pow(rMax / rMin, big), lo = rMin * (float)Math.Pow(rMax / rMin, big * 0.15f);
                float perSecond = s % 3 == 0 ? r.Range(6f, 11f) : r.Range(30f, 55f);
                float loud = r.Range(0.7f, 1f);
                float[] slow = Swell(ref r, 3, 14), quick = Swell(ref r, 20, 90);
                const int step = 220;                        // 5 ms
                for (int t0 = 0; t0 < Frames; t0 += step)
                {
                    float m = Math.Max(0f, 0.45f + 0.75f * At(slow, t0) + 0.45f * At(quick, t0));
                    float want = perSecond * m * m * step / Rate;
                    int count = (int)want + (r.Next() < want - (int)want ? 1 : 0);
                    for (int b = 0; b < count; b++)
                    {
                        float u = r.Next();
                        float rad = lo * (float)Math.Pow(hi / lo, 1f - u * u);          // leaning to the riffle's larger sizes
                        double f0 = 3.0 / rad;
                        // How fast the ring dies (per second); the large ones cut shorter than a lone bubble in still water would
                        // ring, or they sound like notes.
                        double damp = (0.13 / rad + 0.0072 * Math.Pow(rad, -1.5)) * (1.0 + 1.4 * rad / rMax);
                        double rise = r.Range(0.05f, 0.3f) * damp;                      // the pitch slides up as it rises
                        float amp = loud * (float)Math.Pow(rad / rMax, 0.6) * (0.25f + 0.75f * r.Next() * r.Next());
                        int len = (int)Math.Min(7.0 / damp * Rate, 0.3 * Rate);
                        int start = t0 + (int)(r.Next() * step);
                        double ph = 0, env = 1, decay = Math.Exp(-damp / Rate), w0 = 2 * Math.PI * f0 / Rate;
                        // A hair of difference between the ears places it better than loudness alone.
                        int lag = (int)(pan * 12f); int lagL = lag > 0 ? lag : 0, lagR = lag < 0 ? -lag : 0;
                        for (int k = 0; k < len; k++)
                        {
                            double tt = k / (double)Rate;
                            ph += w0 * Math.Min(1.0 + rise * tt, 1.8);
                            float v = amp * (float)(Math.Sin(ph) * env);
                            env *= decay;
                            int iL = (start + k + lagL) % Frames, iR = (start + k + lagR) % Frames;
                            bub[iL * 2] += v * gl; bub[iR * 2 + 1] += v * gr;
                        }
                    }
                }
            }
            // Bubbles a touch softened at the very top (heard through water and air, not from inside the bubble).
            for (int ch = 0; ch < 2; ch++)
            {
                var lp = Biquad.Low(6500);
                for (int pass = 0; pass < 2; pass++)                         // the first pass only settles the filter for the loop
                    for (int i = 0; i < Frames; i++) { float y = lp.Run(bub[i * 2 + ch]); if (pass == 1) bub[i * 2 + ch] = y; }
            }
            float bg = 0.19f / Rms(bub);
            for (int i = 0; i < o.Length; i++) o[i] += bub[i] * bg;
            return Finish(o, 0.25f);
        }

        /// <summary>The river from far off (stereo, interleaved).</summary>
        public static float[] Rush()
        {
            var r = new Rng(1777);
            var o = new float[Frames * 2];
            for (int ch = 0; ch < 2; ch++)
            {
                var low = Noise(ref r, true, Biquad.Low(900, 0.6f), Biquad.Low(1600), Biquad.High(140));
                var mid = Noise(ref r, true, Biquad.High(800), Biquad.Low(2400), Biquad.Low(2400));
                var air = Noise(ref r, false, Biquad.High(2400), Biquad.Low(5500), Biquad.Low(5500));
                float[] sLow = Swell(ref r, 1, 6), sMid = Swell(ref r, 2, 9), sAir = Swell(ref r, 3, 14);
                for (int i = 0; i < Frames; i++)
                    o[i * 2 + ch] = low[i] * 0.30f * (0.72f + 0.28f * At(sLow, i))
                                  + mid[i] * 0.17f * (0.62f + 0.38f * At(sMid, i))
                                  + air[i] * 0.022f * (0.55f + 0.45f * At(sAir, i));
            }
            return Finish(o, 0.4f);
        }

        static float Rms(float[] d)
        {
            double sq = 0; foreach (var v in d) sq += v * v;
            return (float)Math.Sqrt(sq / d.Length + 1e-12);
        }

        /// <summary>
        /// Blends a little of each side into the other (two wholly unrelated sides sound like two rivers), takes out any
        /// DC offset, rounds off the rare tall peak and sets the peak to 0.8.
        /// </summary>
        static float[] Finish(float[] o, float cross)
        {
            double mL = 0, mR = 0;
            for (int i = 0; i < Frames; i++) { mL += o[i * 2]; mR += o[i * 2 + 1]; }
            float dl = (float)(mL / Frames), dr = (float)(mR / Frames);
            float rms = Rms(o), knee = rms * 3.2f;
            float peak = 1e-6f;
            for (int i = 0; i < Frames; i++)
            {
                float l = o[i * 2] - dl, rr = o[i * 2 + 1] - dr;
                float a = l + rr * cross, b = rr + l * cross;
                a = knee * (float)Math.Tanh(a / knee); b = knee * (float)Math.Tanh(b / knee);
                o[i * 2] = a; o[i * 2 + 1] = b;
                peak = Math.Max(peak, Math.Max(Math.Abs(a), Math.Abs(b)));
            }
            float g = 0.8f / peak;
            for (int i = 0; i < o.Length; i++) o[i] *= g;
            return o;
        }
    }
}
