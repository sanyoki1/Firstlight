using System;
using System.Collections;
using System.IO;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Firstlight.Game
{
    /// <summary>
    /// Runs the game scene: loads data, generates the map and drives views and input.
    /// Command line: -seed N, -texels 8|16|32, -shots (density comparison screenshots, then quit), -outdir DIR.
    /// </summary>
    public sealed class GameController : MonoBehaviour
    {
        public GameData Data { get; private set; }
        public GameMap Map { get; private set; }
        public WorldView View { get; private set; }
        public OrbitCamera Cam { get; private set; }
        public WaterSim Water { get; private set; }
        Light _sun;
        int _seed;
        float _waterAccum, _waterSyncTimer;
        float _floodTimer = -1f;      // seconds since a flood surge started (-1 = none)
        public const float FloodPeak = 0.6f;

        void Start()
        {
            Data = GameData.Load();
            MaterialLibrary.SetDensity(ArgInt("-texels", 64));
            Cam = WorldLighting.CreateCamera();
            _sun = WorldLighting.CreateSun();

            _seed = ArgInt("-seed", Environment.TickCount & 0xffff);
            Map = new GameMap(Data.Balance.mapSize);
            Map.Generate(_seed, Data.Balance);
            View = new WorldView(Map);
            View.Build();
            Water = new WaterSim(Map);
            Water.SyncWaterClasses();
            Map.CommitCostChanges();
            View.AttachWater(Water);

            Cam.BoundsMax = new Vector2(Map.W, Map.H);
            Cam.MaxDistance = 160f;
            Cam.HeightAt = Map.HeightAt;
            var site = FindViewpoint();
            Cam.SetView(new Vector3(site.x, Map.HeightAt(site.x, site.y), site.y), 45f, 50f, 30f);

            if (HasArg("-shots")) StartCoroutine(DensityShots());
            if (HasArg("-watershots")) StartCoroutine(WaterShots());
        }

        /// <summary>An interesting spot: open ground near forest, rocks and the river.</summary>
        float2 FindViewpoint()
        {
            float best = float.MinValue; float2 bestP = new float2(Map.W / 2f, Map.H / 2f);
            for (int y = 16; y < Map.H - 16; y += 4)
                for (int x = 16; x < Map.W - 16; x += 4)
                {
                    var p = new float2(x + 0.5f, y + 0.5f);
                    int c = Map.Cell(x, y);
                    if (Map.Ground[c] != Ground.Grass || Map.Tree[c]) continue;
                    float score = math.min(Map.CountFeature("trees", p, 9), 40) * 0.5f
                                + math.min(Map.CountFeature("rocks", p, 10), 6) * 2f
                                + math.min(Map.CountFeature("water", p, 10), 12) * 1f
                                + math.min(Map.CountFeature("wildlife", p, 16), 10) * 0.5f;
                    if (score > best) { best = score; bestP = p; }
                }
            return bestP;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame && !Application.isEditor) Application.Quit();
                if (kb.f6Key.wasPressedThisFrame) StartFlood();
            }
            UpdateWater(Time.deltaTime);
            Map.CommitCostChanges();
            WorldLighting.UpdateForZoom(Cam.Distance);
            View.Draw(Time.time);
        }

        void OnGUI()
        {
            if (_hideGui) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
            style.normal.textColor = new Color(0.92f, 0.9f, 0.85f);
            GUI.Box(new Rect(10, 10, 380, 66), $"Firstlight  seed {_seed}  texture density {MaterialLibrary.Density} px/m\nF6 = flood surge   WASD, Q/E, wheel, right-drag\nwater sim {Water.LastStepMs:F2} ms/frame   river level +{Water.SourceBoost:F2}", style);
        }

        bool _hideGui;

        // ------------------------------------------------------------------ water

        public void StartFlood() => _floodTimer = 0f;

        /// <summary>Flood surge: the upstream level rises over 10 s, holds 40 s, falls back over 30 s.</summary>
        float FloodBoost(float t)
        {
            if (t < 0) return 0;
            if (t < 10) return FloodPeak * t / 10f;
            if (t < 50) return FloodPeak;
            if (t < 80) return FloodPeak * (1 - (t - 50) / 30f);
            return 0;
        }

        void UpdateWater(float dt)
        {
            if (_floodTimer >= 0)
            {
                _floodTimer += dt;
                Water.SourceBoost = FloodBoost(_floodTimer);
                if (_floodTimer > 80) _floodTimer = -1;
            }
            _waterAccum += dt;
            int steps = Mathf.Min(12, (int)(_waterAccum / WaterSim.StepSeconds));
            if (steps > 0)
            {
                _waterAccum -= steps * WaterSim.StepSeconds;
                if (_waterAccum > 1f) _waterAccum = 0;
                Water.Step(WaterSim.StepSeconds, steps);
            }
            _waterSyncTimer += dt;
            if (_waterSyncTimer > 1f)
            {
                _waterSyncTimer = 0;
                Water.SyncWaterClasses();
            }
        }

        /// <summary>Records two clips as numbered frames: the current up close, and a flood time-lapse.</summary>
        IEnumerator WaterShots()
        {
            _hideGui = true;
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs");
            Directory.CreateDirectory(Path.Combine(outDir, "water_frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "flood_frames"));
            var site = FindViewpoint();
            int rc = NearestRiverCell(site);
            var river = new Vector3(rc % Map.W + 0.5f, 0, rc / Map.W + 0.5f);
            river.y = Map.HeightAt(river.x, river.z);
            Cam.SetView(river, 24f, 60f, 20f);
            yield return new WaitForSeconds(1f);
            Time.captureFramerate = 15;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "water_frames", $"f{i:000}.png"));
            }
            // Flood time-lapse: each frame advances the water by 2 seconds.
            Cam.SetView(river, 70f, 55f, 30f);
            for (int i = 0; i < 15; i++) yield return null;
            Debug.Log($"[Water] before flood: {WetCells()} wet cells");
            int peak = 0;
            for (int i = 0; i < 72; i++)
            {
                float t = i * 2f;
                Water.SourceBoost = FloodBoost(t);
                Water.Step(WaterSim.StepSeconds, 40);
                peak = Mathf.Max(peak, WetCells());
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "flood_frames", $"f{i:000}.png"));
            }
            Time.captureFramerate = 0;
            Debug.Log($"[Water] flood peak: {peak} wet cells, after: {WetCells()} wet cells, step {Water.LastStepMs:F2} ms per 40 steps");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        int WetCells()
        {
            Water.SyncWaterClasses();
            int n = 0;
            foreach (var w in Map.WaterClass) if (w > 0) n++;
            return n;
        }

        int NearestRiverCell(float2 p)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int c = 0; c < Map.W * Map.H; c++)
            {
                if (Map.WaterClass[c] < 2) continue;
                float d = math.distancesq(new float2(c % Map.W + 0.5f, c / Map.W + 0.5f), p);
                if (d < bestD) { bestD = d; best = c; }
            }
            return best;
        }

        IEnumerator DensityShots()
        {
            _hideGui = true;
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs");
            Directory.CreateDirectory(outDir);
            var site = FindViewpoint();
            var focus = new Vector3(site.x, Map.HeightAt(site.x, site.y), site.y);
            var views = new (string name, float dist, float pitch)[] { ("wide", 110f, 55f), ("default", 45f, 50f), ("close", 15f, 38f) };
            yield return new WaitForSeconds(1f);
            foreach (int d in MaterialLibrary.Densities)
            {
                MaterialLibrary.SetDensity(d);
                foreach (var v in views)
                {
                    var f = v.name == "close" ? OpenGroundNear(focus) : focus;
                    Cam.SetView(f, v.dist, v.pitch, 30f);
                    for (int i = 0; i < 20; i++) yield return null; // let focus height and shadows settle
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"density_{d}_{v.name}.png"));
                    yield return null; yield return null;
                }
            }
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>Nearest open grass cell (no trees within 3 cells) to a point, for close-up views.</summary>
        Vector3 OpenGroundNear(Vector3 p)
        {
            for (int r = 0; r < 30; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = (int)p.x + dx, y = (int)p.z + dy;
                        if (!Map.InBounds(x, y) || Map.Ground[Map.Cell(x, y)] != Ground.Grass) continue;
                        if (Map.CountFeature("trees", new float2(x + 0.5f, y + 0.5f), 3) > 0) continue;
                        if (Map.CountFeature("trees", new float2(x + 0.5f, y + 0.5f), 7) < 3) continue;
                        return new Vector3(x + 0.5f, Map.HeightAt(x + 0.5f, y + 0.5f), y + 0.5f);
                    }
            return p;
        }

        static bool HasArg(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        static int ArgInt(string name, int fallback) => int.TryParse(Arg(name), out int v) ? v : fallback;

        void OnDestroy()
        {
            View?.Dispose();
            Water?.Dispose();
            Map?.Dispose();
        }
    }
}
