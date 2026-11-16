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

        IEnumerator LookShots()
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "lookshots"));
            Directory.CreateDirectory(_shotDir);
            _hideGui = true;
            Auto = null;
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
