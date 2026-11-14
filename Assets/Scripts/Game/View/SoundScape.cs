using Firstlight.Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Procedural sound, made in code at start (no imported assets): three ambience loops whose volume follows what is
    /// near the camera's focus (wind, stronger high up and in winter; the river; fire crackle near a burning fire), a soft
    /// click for the interface and a low tone for an age-up. Volumes come from the settings. Quiet by design.
    /// </summary>
    public sealed class SoundScape
    {
        readonly GameController G;
        readonly AudioSource _wind, _river, _fire, _ui;
        readonly AudioClip _click, _ageUp;
        int _ages = -1;
        float _scan;
        float _riverNear, _fireNear;
        const int Rate = 44100;

        /// <summary>No audio in batch mode or without an output device.</summary>
        public static bool Available => !Application.isBatchMode && AudioSettings.outputSampleRate > 0;

        public SoundScape(GameController g)
        {
            G = g;
            if (!Available) return;
            var host = g.Cam.gameObject;
            var rng = new System.Random(1234);
            _wind = Loop(host, Wind(rng));
            _river = Loop(host, River(rng));
            _fire = Loop(host, Fire(rng));
            _ui = host.AddComponent<AudioSource>();
            _ui.playOnAwake = false; _ui.spatialBlend = 0;
            _click = Click(rng);
            _ageUp = Tone();
        }

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
                int cx = (int)focus.x, cy = (int)focus.z, rad = 14;
                float best = 99;
                for (int y = cy - rad; y <= cy + rad; y += 2)
                    for (int x = cx - rad; x <= cx + rad; x += 2)
                        if (m.InBounds(x, y) && m.WaterClass[m.Cell(x, y)] >= 2) best = math.min(best, math.length(new float2(x - cx, y - cy)));
                _riverNear = math.saturate(1f - best / rad);
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
            Ease(_river, master * paused * 0.38f * _riverNear * (1f - zoom * 0.7f), dt);
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
        static AudioClip Make(string name, float[] d)
        {
            int fade = Rate / 2, n = d.Length - fade;
            var o = new float[n];
            for (int i = 0; i < n; i++) o[i] = d[i];
            for (int i = 0; i < fade; i++) { float k = i / (float)fade; o[i] = d[i] * k + d[n + i] * (1 - k); }
            float peak = 0.0001f; foreach (var v in o) peak = math.max(peak, math.abs(v));
            for (int i = 0; i < n; i++) o[i] *= 0.8f / peak;
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(o, 0);
            return clip;
        }

        static float Noise(System.Random r) => (float)(r.NextDouble() * 2 - 1);

        /// <summary>Wind: brown noise through a gentle low-pass, swelling and easing every few seconds.</summary>
        static AudioClip Wind(System.Random r)
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
            return Make("wind", d);
        }

        /// <summary>River: band-limited noise (a soft rush) with a slow, small burble.</summary>
        static AudioClip River(System.Random r)
        {
            int n = Rate * 7; var d = new float[n];
            float lp1 = 0, lp2 = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float w = Noise(r);
                lp1 += (w - lp1) * 0.25f; lp2 += (lp1 - lp2) * 0.25f;
                hp = 0.97f * (hp + lp2 - prev); prev = lp2;
                float t = i / (float)Rate;
                d[i] = hp * (0.8f + 0.2f * math.sin(t * 5.1f) * math.sin(t * 2.3f));
            }
            return Make("river", d);
        }

        /// <summary>Fire: a low breathing rumble with short random crackles.</summary>
        static AudioClip Fire(System.Random r)
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
            return Make("fire", d);
        }

        /// <summary>A soft click: a few milliseconds of filtered noise with a fast decay.</summary>
        static AudioClip Click(System.Random r)
        {
            int n = Rate / 25; var d = new float[n];
            float lp = 0;
            for (int i = 0; i < n; i++) { lp += (Noise(r) - lp) * 0.35f; d[i] = lp * math.exp(-i / (Rate * 0.006f)) * 0.6f; }
            var c = AudioClip.Create("click", n, 1, Rate, false); c.SetData(d, 0); return c;
        }

        /// <summary>The age-up tone: a low fifth with a slow swell and fade.</summary>
        static AudioClip Tone()
        {
            int n = (int)(Rate * 2.4f); var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = math.saturate(t / 0.35f) * math.exp(-math.max(0, t - 0.35f) * 1.6f);
                d[i] = env * 0.35f * (math.sin(t * 2 * math.PI * 110f) + 0.6f * math.sin(t * 2 * math.PI * 165f) + 0.25f * math.sin(t * 2 * math.PI * 220f));
            }
            var c = AudioClip.Create("ageup", n, 1, Rate, false); c.SetData(d, 0); return c;
        }
    }
}
