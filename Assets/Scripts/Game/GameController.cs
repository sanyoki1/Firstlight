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
        Light _sun;
        int _seed;

        void Start()
        {
            Data = GameData.Load();
            MaterialLibrary.SetDensity(ArgInt("-texels", 16));
            Cam = WorldLighting.CreateCamera();
            _sun = WorldLighting.CreateSun();

            _seed = ArgInt("-seed", Environment.TickCount & 0xffff);
            Map = new GameMap(Data.Balance.mapSize);
            Map.Generate(_seed, Data.Balance);
            View = new WorldView(Map);
            View.Build();

            Cam.BoundsMax = new Vector2(Map.W, Map.H);
            Cam.MaxDistance = 160f;
            Cam.HeightAt = Map.HeightAt;
            var site = FindViewpoint();
            Cam.SetView(new Vector3(site.x, Map.HeightAt(site.x, site.y), site.y), 45f, 50f, 30f);

            if (HasArg("-shots")) StartCoroutine(DensityShots());
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
                if (kb.f1Key.wasPressedThisFrame) MaterialLibrary.SetDensity(8);
                if (kb.f2Key.wasPressedThisFrame) MaterialLibrary.SetDensity(16);
                if (kb.f3Key.wasPressedThisFrame) MaterialLibrary.SetDensity(32);
                if (kb.f4Key.wasPressedThisFrame) MaterialLibrary.SetDensity(64);
                if (kb.escapeKey.wasPressedThisFrame && !Application.isEditor) Application.Quit();
            }
            Map.CommitCostChanges();
            WorldLighting.UpdateForZoom(Cam.Distance);
            View.Draw(Time.time);
        }

        void OnGUI()
        {
            if (_hideGui) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
            style.normal.textColor = new Color(0.92f, 0.9f, 0.85f);
            GUI.Box(new Rect(10, 10, 360, 48), $"Firstlight  seed {_seed}  texture density {MaterialLibrary.Density} px/m\nF1-F4 = 8/16/32/64 px/m   WASD, Q/E, wheel, right-drag", style);
        }

        bool _hideGui;

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
            Map?.Dispose();
        }
    }
}
