using System.Collections;
using Firstlight.Core;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -lookshots: stills of a loaded town without the HUD, at fixed cameras, times of day and seasons, for judging the
    // look of the game (lighting, sky, water, detail) before and after a change.
    //   Firstlight.exe -batchmode -load SAVE -lookshots -outdir DIR
    public sealed partial class GameController
    {
        /// <summary>Set by -lookshots: the time of day (0..1), season position (0..4) and snow shown, whatever the sim says.</summary>
        (float day, float season, float snow)? _lookAtmo;
        /// <summary>Set by the -lookshots highlight shot: a selected building and a placement ghost, whatever the HUD says.</summary>
        (Building sel, BuildingDef ghost, int2 origin)? _highlightShot;

        /// <summary>How far a footprint's ground rises from its lowest to its highest corner.</summary>
        float FootprintRise(int2 o, int2 size)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            for (int y = o.y; y <= o.y + size.y; y++)
                for (int x = o.x; x <= o.x + size.x; x++)
                {
                    if (x < 0 || y < 0 || x > Map.W || y > Map.H) return 0;
                    float h = Map.Corners[y * (Map.W + 1) + x];
                    lo = math.min(lo, h); hi = math.max(hi, h);
                }
            return hi - lo;
        }

        IEnumerator LookShots()
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "lookshots"));
            Directory.CreateDirectory(_shotDir);
            _hideGui = true;
            Auto = null;
            // -fxscale X: every strength in the settings' Visuals tab at X (0 = all off, 1 = as shipped, 1.5 = the most).
            if (float.TryParse(Arg("-fxscale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fx))
            {
                UI.GameSettings.Current.ScaleVisuals(fx);
                UI.GameSettings.Apply(false);
            }
            for (int i = 0; i < 30; i++) yield return null;
            yield return Settle(60);
            var tc = Sim.TownCenter != null ? Sim.TownCenter.Center : new float2(Cam.Focus.x, Cam.Focus.z);
            // The nearest river cell to the town, for the water shots.
            float2 river = tc; float best = float.MaxValue;
            for (int c = 0; c < Map.W * Map.H; c++)
            {
                if (Map.Ground[c] != Ground.Water) continue;
                var p = new float2(c % Map.W + 0.5f, c / Map.W + 0.5f);
                float d = math.distancesq(p, tc);
                if (d < best) { best = d; river = p; }
            }
            var shots = new (string name, float day, float season, float snow, float2 at, float dist, float pitch, float yaw)[]
            {
                ("noon_close", 0.50f, 1.4f, 0f, tc, 30f, 42f, 30f),
                ("noon_wide", 0.50f, 1.4f, 0f, tc, 110f, 50f, 30f),
                ("noon_street", 0.45f, 1.4f, 0f, tc + new float2(3, -2), 13f, 30f, 70f),
                ("morning_river", 0.27f, 1.4f, 0f, math.lerp(tc, river, 0.8f), 34f, 30f, -20f),
                ("golden_low", 0.775f, 1.4f, 0f, tc, 48f, 26f, 115f),
                ("dusk_wide", 0.83f, 2.4f, 0f, tc, 150f, 24f, 60f),
                ("night_fires", 0.96f, 1.4f, 0f, tc, 32f, 40f, 30f),
                ("autumn_afternoon", 0.66f, 2.5f, 0f, tc, 60f, 36f, -60f),
                ("winter_noon", 0.50f, 3.5f, 0.9f, tc, 40f, 42f, 30f),
                ("horizon", 0.40f, 1.4f, 0f, tc, 160f, 20f, 200f),
                ("winter_snowfall", 0.45f, 3.03f, 0.9f, tc + new float2(3, -2), 16f, 30f, 70f),
                ("winter_first_snow", 0.55f, 3.2f, 0.4f, tc, 30f, 40f, -40f),
                ("misty_dawn", 0.235f, 2.4f, 0f, tc, 75f, 28f, 20f),
            };
            string only = Arg("-lookonly");
            foreach (var s in shots)
            {
                if (only != null && !only.Contains(s.name)) continue;
                _lookAtmo = (s.day, s.season, s.snow);
                LookAt(World(s.at), s.dist, s.pitch, s.yaw);
                yield return Settle(25);
                yield return Shot("look_" + s.name);
            }
            if (only == null || only.Contains("highlight"))
            {
                // A selected building and a placement ghost on the steepest ground near the town: the parts of their
                // rings and outlines the ground covers must still show.
                Building sel = null; float rise = -1;
                foreach (var b in Sim.Buildings)
                {
                    if (!b.Alive || b.Def.sizeX > 3 || b.Def.sizeY > 3 || math.distance(b.Center, tc) > 40) continue;
                    float r = FootprintRise(b.Origin, new int2(b.SX, b.SY));
                    if (r > rise) { rise = r; sel = b; }
                }
                var def = sel?.Def ?? Sim.Data.Buildings.Find(d => !d.path && d.sizeX >= 2);
                int2 ghostAt = default; float gRise = -1;
                var at0 = sel != null ? sel.Center : tc;
                for (int y = (int)at0.y - 25; y <= at0.y + 25; y++)
                    for (int x = (int)at0.x - 25; x <= at0.x + 25; x++)
                    {
                        var o = new int2(x, y);
                        if (!Sim.CanPlace(def, o, 0, out _)) continue;
                        float r = FootprintRise(o, new int2(def.sizeX, def.sizeY));
                        if (r > gRise) { gRise = r; ghostAt = o; }
                    }
                Debug.Log($"[LookShots] highlight: selected {sel?.Def.id} rise {rise:F2}, ghost {def.id} at {ghostAt} rise {gRise:F2}");
                _highlightShot = (sel, def, ghostAt);
                _lookAtmo = (0.5f, 1.4f, 0f);
                var ghostC = Settlement.FootprintCenter(def, ghostAt, 0);
                foreach (var (name, at, pitch, yaw) in new[]
                {
                    ("highlight_sel_top", sel?.Center ?? tc, 60f, 30f), ("highlight_sel_low", sel?.Center ?? tc, 30f, 200f),
                    ("highlight_ghost_top", ghostC, 60f, 30f), ("highlight_ghost_low", ghostC, 30f, 200f),
                })
                {
                    LookAt(World(at), 14f, pitch, yaw);
                    yield return Settle(25);
                    yield return Shot("look_" + name);
                }
                _highlightShot = null;
            }
            if (only == null || only.Contains("level"))
            {
                // The field on the steepest ground near the town: it must sit on levelled earth, every row showing.
                Building field = null; float rise = -1;
                foreach (var b in Sim.Buildings)
                {
                    if (!b.Alive || !b.Def.crop || math.distance(b.Center, tc) > 45) continue;
                    float r = FootprintRise(b.Origin, new int2(b.SX, b.SY));
                    if (r > rise) { rise = r; field = b; }
                }
                Debug.Log($"[LookShots] level: field {field?.Def.id} at {field?.Origin} rise {rise:F2}");
                var at = field?.Center ?? tc;
                foreach (var (name, season, snow, yaw) in new[] { ("level_summer", 1.4f, 0f, 30f), ("level_summer_side", 1.4f, 0f, 120f), ("level_winter", 3.5f, 0.9f, 30f) })
                {
                    _lookAtmo = (0.5f, season, snow);
                    LookAt(World(at), 16f, 34f, yaw);
                    yield return Settle(25);
                    yield return Shot("look_" + name);
                }
            }
            if (only == null || only.Contains("cinematic"))
            {
                // The cinematic view (F10): soft focus round the town fire in the evening light.
                _lookAtmo = (0.76f, 1.4f, 0f);
                _cine = true; _cineAmount = 1f;
                LookAt(World(tc), 20f, 22f, 150f);
                yield return Settle(25);
                yield return Shot("look_cinematic");
                _cine = false; _cineAmount = 0f;
            }
            if (HasArg("-lookperf"))
            {
                // What the post effects cost: the same view drawn 150 times with them on, then off, each frame forced
                // through the graphics card by reading one pixel back.
                _lookAtmo = (0.5f, 1.4f, 0f);
                LookAt(World(tc), 30f, 42f, 30f);
                yield return Settle(10);
                var one = new Texture2D(1, 1, TextureFormat.RGB24, false);
                foreach (bool on in new[] { true, false, true, false })
                {
                    Look.SetEffects(on);
                    yield return null;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    const int frames = 150;
                    for (int i = 0; i < frames; i++)
                    {
                        var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _shotRT };
                        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, req);
                        var prev = RenderTexture.active; RenderTexture.active = _shotRT;
                        one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        RenderTexture.active = prev;
                    }
                    Debug.Log($"[LookShots] perf: effects {(on ? "on" : "off")}: {sw.Elapsed.TotalMilliseconds / frames:F2} ms a frame at {_shotRT.width}x{_shotRT.height}");
                }
                Look.SetEffects(true);
            }
            Debug.Log("[LookShots] done");
            Application.Quit();
        }
    }
}
