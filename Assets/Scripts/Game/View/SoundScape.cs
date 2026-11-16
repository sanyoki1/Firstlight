using Firstlight.Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Procedural sound, made in code at start (no imported assets): ambience loops whose volume follows what is near
    /// the camera's focus (wind, stronger high up and in winter; the river; fire crackle near a burning fire), a soft
    /// click for the interface and a low tone for an age-up. Volumes come from the settings. Quiet by design.
    /// <para>The river is two stereo loops from <see cref="WaterSynth"/>: a bubbling babble heard close in, which gives
    /// way to a low rush as the camera pulls back. It leans to the side of the screen the water is on, a wide river
    /// carries further than a narrow one, and a flood makes the rush louder and deeper.</para>
    /// </summary>
    public sealed class SoundScape
    {
        readonly GameController G;
        readonly AudioSource _wind, _river, _rush, _fire, _ui;
        readonly AudioClip _click, _ageUp;
        int _ages = -1;
        float _scan;
        float _riverNear, _riverWide, _riverSide, _fireNear;
        const int Rate = 44100;

        /// <summary>No audio in batch mode or without an output device.</summary>
        public static bool Available => !Application.isBatchMode && AudioSettings.outputSampleRate > 0;

        public SoundScape(GameController g)
        {
            G = g;
            if (!Available) return;
            var host = g.Cam.gameObject;
            // The clips are made once per run and kept (a new game or a load rebuilds the scene, not the sounds).
            if (_clips == null)
            {
                var b = Buffers();
                _clips = new AudioClip[b.Length];
                for (int i = 0; i < b.Length; i++)
                {
                    _clips[i] = AudioClip.Create(Names[i], b[i].Length / Channels[i], Channels[i], Rate, false);
                    _clips[i].hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _clips[i].SetData(b[i], 0);
                }
            }
            _wind = Loop(host, _clips[0]);
            _river = Loop(host, _clips[1]);
            _rush = Loop(host, _clips[2]);
            _fire = Loop(host, _clips[3]);
            _ui = host.AddComponent<AudioSource>();
            _ui.playOnAwake = false; _ui.spatialBlend = 0;
            _click = _clips[4];
            _ageUp = _clips[5];
        }

        static AudioClip[] _clips;
        public static readonly string[] Names = { "wind", "river", "rush", "fire", "click", "ageup" };
        /// <summary>Channels per buffer (the stereo ones are interleaved).</summary>
        public static readonly int[] Channels = { 1, 2, 2, 1, 1, 1 };
        const int Loops = 4;                               // the first four repeat

        /// <summary>The sample buffers (44.1 kHz): the four ambience loops, the click and the age-up tone.</summary>
        public static float[][] Buffers()
        {
            var rng = new System.Random(1234);
            return new[] { Wind(rng), WaterSynth.Babble(), WaterSynth.Rush(), Fire(rng), Click(rng), Tone() };
        }

        /// <summary>
        /// -soundcheck: the generated buffers measured (they can't be listened to in a test): peak, RMS, DC offset, NaN,
        /// and for the loops the step where the end joins the start, against the typical step between samples.
        /// </summary>
        public static string Check(out bool ok)
        {
            var sb = new System.Text.StringBuilder();
            ok = true;
            var b = Buffers();
            for (int i = 0; i < b.Length; i++)
            {
                var d = b[i]; double sum = 0, sq = 0, step = 0; float peak = 0; int bad = 0, ch = Channels[i];
                for (int k = 0; k < d.Length; k++)
                {
                    if (!float.IsFinite(d[k])) { bad++; continue; }
                    sum += d[k]; sq += d[k] * d[k]; peak = math.max(peak, math.abs(d[k]));
                    if (k >= ch) step += math.abs(d[k] - d[k - ch]);
                }
                double mean = sum / d.Length, rms = math.sqrt(sq / d.Length), avgStep = step / (d.Length - ch);
                bool loop = i < Loops;
                float join = 0;
                for (int c = 0; c < ch; c++) join = math.max(join, math.abs(d[c] - d[d.Length - ch + c]));
                bool good = bad == 0 && peak <= 0.95f && math.abs(mean) < 0.02 && rms > 0.01 && (!loop || join <= avgStep * 6 + 1e-4);
                ok &= good;
                sb.AppendLine($"{Names[i]}: {d.Length / ch / (float)Rate:F2} s{(ch == 2 ? " stereo" : "")}, peak {peak:F3}, rms {rms:F3}, dc {mean:F4}, nan {bad}" + (loop ? $", loop join step {join:F4} (typical {avgStep:F4})" : "") + (good ? "" : "  <-- BAD"));
            }
            // The worst case mix: every loop at its loudest volume at once, plus a click and the age-up tone.
            // The loops trade places with the zoom, so the worst is the louder of far out (winter wind and a flood's rush)
            // and close in (a little wind, the babble, some rush, a fire).
            float worst = 0.8f * math.max(0.44f + 0.36f, 0.22f + 0.30f + 0.09f + 0.32f) + 0.5f * Peak(b[4]) + 0.6f * Peak(b[5]);
            sb.AppendLine($"worst-case sum of peaks at full volume: {worst:F2}" + (worst > 1.2f ? "  <-- may clip" : ""));
            return sb.ToString();
        }

        static float Peak(float[] d) { float p = 0; foreach (var v in d) p = math.max(p, math.abs(v)); return p; }

        static AudioSource Loop(GameObject host, AudioClip clip)
        {
            var s = host.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0; s.spatialBlend = 0; s.playOnAwake = false;
            s.Play();
            return s;
        }

        public void Click() { if (_ui != null) _ui.PlayOneShot(_click, 0.5f * GameSettings.Current.volUi * GameSettings.Current.volMaster); }

        public void Update(float dt)
        {
            if (_wind == null) return;
            var st = GameSettings.Current;
            float master = st.volMaster * st.volAmbience;
            var s = G.Sim; var m = G.Map;
            var focus = G.Cam.Focus;
            // What is near the focus: rescanned a few times a second (cheap: a small square of cells).
            _scan -= dt;
            if (_scan <= 0)
            {
                _scan = 0.3f;
                _riverNear = 0; _fireNear = 0;
                // The square looked at grows with the view, so a river on screen is heard from high up too.
                int cx = (int)focus.x, cy = (int)focus.z, rad = (int)math.clamp(G.Cam.Distance * 0.5f, 14f, 36f), step = math.max(2, rad / 7);
                float yaw = math.radians(G.Cam.Yaw);
                var right = new float2(math.cos(yaw), -math.sin(yaw));           // screen right, on the ground
                float best = 999, weight = 0, side = 0; int seen = 0;
                for (int y = cy - rad; y <= cy + rad; y += step)
                    for (int x = cx - rad; x <= cx + rad; x += step)
                    {
                        seen++;
                        if (!m.InBounds(x, y)) continue;
                        byte wc = m.WaterClass[m.Cell(x, y)];
                        if (wc == 0) continue;
                        var off = new float2(x - cx, y - cy);
                        float dist = math.length(off);
                        if (wc >= 2) best = math.min(best, dist);
                        float wgt = (wc >= 2 ? 1f : 0.35f) * math.saturate(1f - dist / (rad * 1.3f));   // shallows count for less
                        weight += wgt; side += wgt * math.dot(off, right) / rad;
                    }
                _riverNear = math.saturate(1f - best / rad);
                _riverWide = math.saturate(weight / (seen * 0.10f));              // 1 = a broad river (or a flood) fills the view
                _riverSide = weight > 0 ? math.clamp(side / weight * 1.6f, -1f, 1f) : 0f;
                if (s != null && !s.NeedsCampfire)
                {
                    float fb = 99;
                    foreach (var b in s.Buildings)
                    {
                        if (!b.Active) continue;
                        bool lit = b.Def.warmthRadius > 0 ? b.Fueled : b.Def.knowledgePerWorker > 0 && b.Workers > 0;
                        if (!lit) continue;
                        fb = math.min(fb, math.distance(b.Center, new float2(focus.x, focus.z)));
                    }
                    _fireNear = math.saturate(1f - fb / 12f);
                }
            }
            float zoom = math.saturate((G.Cam.Distance - 10f) / 90f);          // close in: the river and fire; far out: wind
            bool winter = s != null && !s.NeedsCampfire && s.Season == 3;
            float paused = G.Paused ? 0.4f : 1f;
            Ease(_wind, master * paused * (0.10f + 0.22f * zoom + (winter ? 0.12f : 0)), dt);
            // The river: the babble close in, the rush from further off; a flood swells and deepens the rush.
            float flood = G.Water != null ? math.saturate(G.Water.SourceBoost / GameController.FloodPeak) : 0f;
            float close = (1f - zoom) * (1f - zoom);
            Ease(_river, master * paused * 0.30f * _riverNear * close * (0.75f + 0.25f * _riverWide), dt);
            Ease(_rush, master * paused * 0.20f * _riverNear * (0.25f + 0.75f * zoom) * (0.6f + 0.4f * _riverWide) * (1f + 0.8f * flood), dt);
            float k = math.saturate(dt * 2f);
            _river.panStereo = math.lerp(_river.panStereo, _riverSide * 0.55f, k);
            _rush.panStereo = math.lerp(_rush.panStereo, _riverSide * 0.35f, k);
            _rush.pitch = math.lerp(_rush.pitch, 1f - 0.1f * flood, k);
            Ease(_fire, master * paused * 0.32f * _fireNear * (1f - zoom), dt);
            // An age-up: a low, warm tone.
            if (s != null)
            {
                if (_ages < 0) _ages = s.AgeLog.Count;
                if (s.AgeLog.Count > _ages) { _ages = s.AgeLog.Count; _ui.PlayOneShot(_ageUp, 0.6f * st.volMaster * st.volUi); }
            }
        }

        static void Ease(AudioSource s, float target, float dt) => s.volume = math.lerp(s.volume, target, math.saturate(dt * 2f));

        // ------------------------------------------------------------------ synthesis

        /// <summary>A loop with its ends cross-faded so it repeats without a click.</summary>
        static float[] Make(float[] d)
        {
            // Equal-power cross-fade of the end into the start (the two are unrelated noise, so a straight blend would dip
            // by 3 dB once per loop), the DC offset taken out, and the peak set to 0.8.
            int fade = Rate / 2, n = d.Length - fade;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; o[i] = d[i] * math.sqrt(k) + d[n + i] * math.sqrt(1 - k); }
            double mean = 0; foreach (var v in o) mean += v; mean /= n;
            for (int i = 0; i < n; i++) o[i] -= (float)mean;
            float peak = 0.0001f; foreach (var v in o) peak = math.max(peak, math.abs(v));
            for (int i = 0; i < n; i++) o[i] *= 0.8f / peak;
            return o;
        }

        static float Noise(System.Random r) => (float)(r.NextDouble() * 2 - 1);

        /// <summary>Wind: brown noise through a gentle low-pass, swelling and easing every few seconds.</summary>
        static float[] Wind(System.Random r)
        {
            int n = Rate * 9; var d = new float[n];
            float brown = 0, lp = 0;
            for (int i = 0; i < n; i++)
            {
                brown = math.clamp(brown + Noise(r) * 0.02f, -1, 1) * 0.998f;
                lp += (brown - lp) * 0.05f;
                float t = i / (float)Rate;
                float swell = 0.55f + 0.3f * math.sin(t * 0.7f) + 0.15f * math.sin(t * 1.9f + 1.3f);
                d[i] = lp * swell;
            }
            return Make(d);
        }

        /// <summary>Fire: a low breathing rumble with short random crackles.</summary>
        static float[] Fire(System.Random r)
        {
            int n = Rate * 6; var d = new float[n];
            float lp = 0, env = 0;
            for (int i = 0; i < n; i++)
            {
                lp += (Noise(r) - lp) * 0.02f;
                if (r.NextDouble() < 9.0 / Rate) env = 0.6f + (float)r.NextDouble() * 0.6f;   // ~9 crackles a second
                env *= 0.9965f;
                d[i] = lp * 0.5f + Noise(r) * env * env * 0.5f;
            }
            return Make(d);
        }

        /// <summary>A soft click: a few milliseconds of filtered noise with a fast decay.</summary>
        static float[] Click(System.Random r)
        {
            int n = Rate / 25; var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++) { lp += (Noise(r) - lp) * 0.35f; d[i] = lp * math.exp(-i / (Rate * 0.006f)) * 0.6f; }
            return d;
        }

        /// <summary>The age-up tone: a low fifth with a slow swell and fade.</summary>
        static float[] Tone()
        {
            int n = (int)(Rate * 2.4f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = math.saturate(t / 0.35f) * math.exp(-math.max(0, t - 0.35f) * 1.6f);
                d[i] = env * 0.35f * (math.sin(t * 2 * math.PI * 110f) + 0.6f * math.sin(t * 2 * math.PI * 165f) + 0.25f * math.sin(t * 2 * math.PI * 220f));
            }
            return d;
        }
    }
}
