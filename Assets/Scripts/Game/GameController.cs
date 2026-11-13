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
        public Wildlife Wild { get; private set; }
        public Band Band { get; private set; }
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
            Wild = new Wildlife(Map, Water, _seed);
            View.AttachWildlife(Wild);

            Cam.BoundsMax = new Vector2(Map.W, Map.H);
            Cam.MaxDistance = 160f;
            Cam.HeightAt = Map.HeightAt;
            var site = FindViewpoint();
            Cam.SetView(new Vector3(site.x, Map.HeightAt(site.x, site.y), site.y), 45f, 50f, 30f);

            // A band camps on open ground near the starting view and hunts every few minutes (F7 sends them now).
            var camp = OpenGroundNear(new Vector3(site.x, 0, site.y));
            Band = new Band(Map, Wild, new float2(camp.x, camp.z), _seed);
            View.AttachBand(Band);

            if (HasArg("-shots")) StartCoroutine(DensityShots());
            if (HasArg("-watershots")) StartCoroutine(WaterShots());
            if (HasArg("-deershots")) StartCoroutine(DeerShots());
            if (HasArg("-deerpair")) StartCoroutine(DeerPairShots());
            if (HasArg("-huntshots")) StartCoroutine(HuntShots());
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
                if (kb.f7Key.wasPressedThisFrame) Band.StartHunt();
            }
            UpdateWater(Time.deltaTime);
            Map.CommitCostChanges();
            Band.Update(Time.deltaTime);      // first, so the deer see where people are this frame
            Wild.Update(Time.deltaTime);
            WorldLighting.UpdateForZoom(Cam.Distance);
            View.Draw(Time.time);
        }

        void OnGUI()
        {
            if (_hideGui) return;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14 };
            style.normal.textColor = new Color(0.92f, 0.9f, 0.85f);
            GUI.Box(new Rect(10, 10, 400, 102), $"Firstlight  seed {_seed}  texture density {MaterialLibrary.Density} px/m\nF6 = flood surge   F7 = send the hunters   WASD, Q/E, wheel, right-drag\nwater sim {Water.LastStepMs:F2} ms/frame   river level +{Water.SourceBoost:F2}\ndeer drinks so far: {Wild.DrinksTaken}\nhunts {Band.Hunts}   spears thrown {Band.Throws}   deer brought home {Band.Kills}", style);
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

        /// <summary>
        /// Deer review: a still of the model line-up, a sped-up clip of one herd walking to the river and
        /// drinking, and a real-time close-up of drinking. Frames go to deer_frames / deer_close (and deer_lineup.png).
        /// </summary>
        IEnumerator DeerShots()
        {
            _hideGui = true;
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs");
            Directory.CreateDirectory(Path.Combine(outDir, "deer_frames"));
            Directory.CreateDirectory(Path.Combine(outDir, "deer_close"));

            // 1. Model line-up on open ground, seen from the side: doe and stag standing, walking, head down.
            var site = FindViewpoint();
            var ground = OpenGroundNear(new Vector3(site.x, 0, site.y));
            var lineup = new (int pose, bool stag)[]
            {
                ((int)DeerModel.Pose.Stand, false), ((int)DeerModel.Pose.Walk0 + 2, false), ((int)DeerModel.Pose.HeadDown, false),
                ((int)DeerModel.Pose.Stand, true), ((int)DeerModel.Pose.Walk0 + 6, true), ((int)DeerModel.Pose.HeadMid, true),
            };
            for (int k = 0; k < lineup.Length; k++)
            {
                float x = ground.x + (k - 2.5f) * 1.15f, z = ground.z;
                View.ExtraDeer.Add((lineup[k].pose, lineup[k].stag, new InstanceData(new float3(x, Map.HeightAt(x, z), z), math.PI * 0.5f, new float3(1f), Pal.Pack(Color.white))));
            }
            Cam.SetView(new Vector3(ground.x, Map.HeightAt(ground.x, ground.z) + 0.3f, ground.z), 6.5f, 14f, 0f);
            for (int i = 0; i < 30; i++) yield return null;
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "deer_lineup.png"));
            yield return null; yield return null;
            View.ExtraDeer.Clear();

            // 2. The herd with the closest water goes to drink; the camera frames home and bank together.
            int herd = 0; float best = float.MaxValue;
            for (int h = 0; h < Map.Herds.Count; h++)
            {
                int wc = Wild.WateringPlace(h);
                if (wc < 0) continue;
                float d = math.distance(Map.Herds[h].Pos, new float2(wc % Map.W + 0.5f, wc / Map.W + 0.5f));
                if (d < best) { best = d; herd = h; }
            }
            int water = Wild.WateringPlace(herd);
            var home = Map.Herds[herd].Pos;
            var bank = new float2(water % Map.W + 0.5f, water / Map.W + 0.5f);
            var mid = (home + bank) * 0.5f;
            Debug.Log($"[Deer] herd {herd} of {Map.Herds.Count}: home {home}, watering place {bank}, {best:F1} cells apart");
            // Look from behind the herd's home towards the water, so the walk runs away from the camera.
            float toWater = math.degrees(math.atan2(bank.x - home.x, bank.y - home.y));
            var focus = math.lerp(home, bank, 0.6f);
            Cam.SetView(new Vector3(focus.x, Map.HeightAt(focus.x, focus.y), focus.y), math.max(16f, best * 1.6f), 62f, toWater);
            Wild.MakeThirsty(herd);
            Time.captureFramerate = 10;
            Time.timeScale = 3f;                 // each frame = 0.3 s of game time
            int frame = 0;
            for (; frame < 400; frame++)
            {
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "deer_frames", $"f{frame:000}.png"));
                if (frame > 60 && Wild.DrinksTaken > 0 && AllHome(herd)) break;
            }
            var states = new System.Text.StringBuilder();
            for (int i = 0; i < Wildlife.ActiveCount(Map.Herds[herd], Wild.Herds[herd]); i++)
            {
                var dd = Wild.Herds[herd][i];
                states.Append($"\n  #{i} {dd.State} thirst {dd.Thirst:F2} pos {dd.Pos.x:F1},{dd.Pos.y:F1} drink {dd.DrinkCell % Map.W},{dd.DrinkCell / Map.W}");
            }
            Debug.Log($"[Deer] trip took {frame * 0.3f:F0} s of game time, {Wild.DrinksTaken} drinks; herd now {states}");

            // 3. Real-time close-up of a deer drinking.
            Time.timeScale = 1f;
            Time.captureFramerate = 15;
            Wild.MakeThirsty(herd);
            for (int i = 0; i < 1500; i++)
            {
                yield return null;
                if (FirstDrinker(herd, out _)) break;
            }
            FirstDrinker(herd, out var drinker);
            // Side-on to the drinking deer, from the land side.
            var face = drinker.Face;
            float side = math.degrees(math.atan2(face.x, face.y)) + 60f;
            Cam.SetView(new Vector3(drinker.Pos.x, Map.HeightAt(drinker.Pos.x, drinker.Pos.y) + 0.2f, drinker.Pos.y), 5.5f, 24f, side);
            for (int i = 0; i < 10; i++) yield return null;
            for (int i = 0; i < 75; i++)
            {
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "deer_close", $"f{i:000}.png"));
            }
            Time.captureFramerate = 0;
            Debug.Log($"[Deer] done: {Wild.DrinksTaken} drinks in total");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>
        /// Two deer only (one herd thinned to a doe and a stag, the others hidden): they wander and graze, then
        /// get thirsty, walk to the river, drink and walk home. Frames go to deer_pair/ at 12 fps, 1.5x speed.
        /// </summary>
        IEnumerator DeerPairShots()
        {
            _hideGui = true;
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs");
            Directory.CreateDirectory(Path.Combine(outDir, "deer_pair"));
            // The herd with the most open ground between home and water (trees hide deer from above).
            int herd = 0; float best = float.MaxValue, bestScore = float.MaxValue;
            for (int h = 0; h < Map.Herds.Count; h++)
            {
                int wc = Wild.WateringPlace(h);
                if (wc < 0) continue;
                var hp = Map.Herds[h].Pos; var wp = new float2(wc % Map.W + 0.5f, wc / Map.W + 0.5f);
                float d = math.distance(hp, wp);
                float score = d + TreesAlongPath(Map.CellAt(hp), wc) * 3f;
                if (score < bestScore) { bestScore = score; best = d; herd = h; }
            }
            for (int h = 0; h < Map.Herds.Count; h++) Map.Herds[h].Pop = h == herd ? 2.5f : 0f;   // deer 0 is a stag, 1 a doe
            for (int i = 0; i < 2; i++) Wild.Herds[herd][i].Thirst = 0.2f + i * 0.05f;

            int water = Wild.WateringPlace(herd);
            var home = Map.Herds[herd].Pos;
            var bank = new float2(water % Map.W + 0.5f, water / Map.W + 0.5f);
            var focus = math.lerp(home, bank, 0.35f);
            // Side-on to the walk, so the deer cross the picture.
            float yaw = math.degrees(math.atan2(bank.x - home.x, bank.y - home.y)) - 90f;
            // A fixed camera (so the GIF only stores what moves), framing home and the bank together.
            Cam.SetView(new Vector3(focus.x, Map.HeightAt(focus.x, focus.y), focus.y), math.max(9f, best * 1.6f), 50f, yaw);
            for (int i = 0; i < 20; i++) yield return null;

            Time.captureFramerate = 12;
            Time.timeScale = 2.5f;                 // each frame = 0.21 s of game time
            Wild.StartWander(herd);
            int frame = 0, sinceHome = -1;
            for (; frame < 520; frame++)
            {
                yield return null;
                ScreenCapture.CaptureScreenshot(Path.Combine(outDir, "deer_pair", $"f{frame:000}.png"));
                if (frame == 30) Wild.MakeThirsty(herd);               // about 6 s of wandering first
                if (frame > 120 && sinceHome < 0 && Wild.DrinksTaken > 0 && AllHome(herd)) sinceHome = frame;
                if (sinceHome >= 0 && frame - sinceHome > 15) break;   // a moment of grazing back home
            }
            Time.timeScale = 1f; Time.captureFramerate = 0;
            Debug.Log($"[DeerPair] herd {herd} ({best:F1} cells to water), {frame + 1} frames, {Wild.DrinksTaken} drinks, home after {sinceHome}; " +
                      $"{Wild.Herds[herd][0].State} {Wild.Herds[herd][1].State}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        /// <summary>
        /// Hunt clip in three fixed shots (frames to hunt/): the camp with the hunters setting off; the stalk, throw
        /// and kill (slowed to real time around each throw); and the carrier bringing the deer home. One herd of
        /// six is kept, the camp is placed on open ground 14-17 cells from it, and the time between shots is skipped.
        /// </summary>
        IEnumerator HuntShots()
        {
            _hideGui = true;
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs");
            string frames = Path.Combine(outDir, "hunt");
            Directory.CreateDirectory(frames);
            int frame = 0;
            void Capture() => ScreenCapture.CaptureScreenshot(Path.Combine(frames, $"f{frame++:000}.png"));

            // Herd and camp: open ground round the herd, and few trees on the walk between them.
            int herd = -1; float2 camp = 0; float bestScore = float.MaxValue;
            for (int h = 0; h < Map.Herds.Count; h++)
            {
                var hp = Map.Herds[h].Pos;
                float around = Map.CountFeature("trees", hp, 6) * 2f;
                for (int a = 0; a < 24; a++)
                    for (float r = 14f; r <= 17f; r += 1.5f)
                    {
                        float ang = a * math.PI * 2f / 24f;
                        var c = hp + new float2(math.sin(ang), math.cos(ang)) * r;
                        int cx = (int)c.x, cy = (int)c.y;
                        if (cx < 4 || cy < 4 || cx >= Map.W - 4 || cy >= Map.H - 4) continue;
                        int cell = Map.Cell(cx, cy);
                        if (Map.Flow.Cost[cell] != 1 || Map.CountFeature("trees", c, 3) > 0 || Map.CountFeature("rocks", c, 3) > 0 || Map.CountFeature("water", c, 4) > 0) continue;
                        float score = around + TreesAlongPath(cell, Map.CellAt(hp)) * 3f;
                        if (score < bestScore) { bestScore = score; herd = h; camp = new float2(cx + 0.5f, cy + 0.5f); }
                    }
            }
            if (herd < 0) { Debug.LogError("[Hunt] no open herd/camp pair found"); Application.Quit(); yield break; }
            for (int h = 0; h < Map.Herds.Count; h++) Map.Herds[h].Pop = h == herd ? 6.5f : 0f;
            Band = new Band(Map, Wild, camp, _seed) { AutoHunt = false };
            View.AttachBand(Band);
            var home = Map.Herds[herd].Pos;
            Debug.Log($"[Hunt] herd {herd} at {home}, camp at {camp}, {math.distance(home, camp):F1} cells apart, score {bestScore}");
            float toHerd = math.degrees(math.atan2(home.x - camp.x, home.y - camp.y));
            for (int i = 0; i < 30; i++) yield return null;

            // 1. Camp: a moment of camp life, then the hunters set off.
            Cam.SetView(new Vector3(camp.x, Map.HeightAt(camp.x, camp.y), camp.y), 7.5f, 38f, toHerd + 140f);
            for (int i = 0; i < 10; i++) yield return null;
            Time.captureFramerate = 12;
            Time.timeScale = 1f;
            for (int i = 0; i < 24; i++) { yield return null; Capture(); }
            Band.StartHunt(herd);
            for (int i = 0; i < 36; i++) { yield return null; Capture(); }
            Debug.Log($"[Hunt] shot 1 ends at frame {frame}");

            // Skip the walk out.
            Time.captureFramerate = 0;
            Time.timeScale = 4f;
            float t0 = Time.time;
            while (Time.time - t0 < 120f && !HuntersClose(7f)) yield return null;

            // 2. The hunt: a fixed camera side-on to the line from the hunters to the herd.
            float2 Hunters() { float2 s2 = 0; int n = 0; foreach (var p in Band.People) if (p.Hunter) { s2 += p.Pos; n++; } return s2 / math.max(1, n); }
            float2 NearestDeerTo(float2 from)
            {
                float2 best = Map.Herds[herd].Pos; float bd = float.MaxValue;
                int n = Wildlife.ActiveCount(Map.Herds[herd], Wild.Herds[herd]);
                for (int i = 0; i < n; i++)
                {
                    float d = math.distance(Wild.Herds[herd][i].Pos, from);
                    if (d < bd) { bd = d; best = Wild.Herds[herd][i].Pos; }
                }
                return best;
            }
            float2 focus = 0, focusTarget = 0; float camDist = 7.5f, hunterOff = 0, targetOff = 0;
            void Frame()
            {
                var hu = Hunters(); var he = Band.Prey != null ? Band.Prey.Pos : NearestDeerTo(hu);
                if (math.distance(hu, he) > 7f) he = hu + math.normalizesafe(he - hu) * 7f;   // keep the hunters in the picture
                focus = math.lerp(hu, he, 0.65f);            // nearer the deer: the hunters close in on them
                focusTarget = he;
                hunterOff = math.distance(hu, focus); targetOff = math.distance(he, focus);
                camDist = 5f + math.distance(hu, he) * 0.65f;
                float side = math.degrees(math.atan2(he.x - hu.x, he.y - hu.y)) - 90f;
                Cam.SetView(new Vector3(focus.x, Map.HeightAt(focus.x, focus.y), focus.y), camDist, 54f, side);
            }
            Frame();
            Time.timeScale = 0f;                                   // let the camera settle onto the ground height
            for (int i = 0; i < 12; i++) yield return null;
            Time.captureFramerate = 12;
            int carryFrames = 0, cuts = 0, shot2Start = frame;
            while (frame - shot2Start < 520)
            {
                bool action = Band.Spears.Exists(sp => sp.Flying);
                foreach (var p in Band.People) if (p.State is PersonState.Throw or PersonState.Recover or PersonState.Lift) action = true;
                Time.timeScale = action ? 0.8f : 2f;               // slow down for throws and the lift
                { yield return null; Capture(); }
                bool carrying = Band.Carrier >= 0 && Band.People[Band.Carrier].State == PersonState.Carry;
                if (carrying && ++carryFrames > 30) break;
                // Re-frame (a cut) if the chase leaves the picture.
                // Cut to a new framing only when the hunters or their quarry have moved well away from where they were framed.
                var target = Band.Prey != null ? Band.Prey.Pos : NearestDeerTo(Hunters());
                if ((math.distance(Hunters(), focus) > hunterOff + 2.5f || math.distance(target, focus) > targetOff + 3f) && cuts < 8) { Frame(); cuts++; }
            }
            Debug.Log($"[Hunt] shot 2 ends at frame {frame}: throws {Band.Throws}, deer killed {6 - Wildlife.ActiveCount(Map.Herds[herd], Wild.Herds[herd])}, cuts {cuts}");

            // Skip the walk home.
            Time.captureFramerate = 0;
            Time.timeScale = 4f;
            t0 = Time.time;
            while (Time.time - t0 < 120f && Band.Carrier >= 0 && math.distance(Band.People[Band.Carrier].Pos, Band.Camp) > 5f) yield return null;

            // 3. Home: the carrier lays the deer by the fire.
            Cam.SetView(new Vector3(camp.x, Map.HeightAt(camp.x, camp.y), camp.y), 7.5f, 38f, toHerd + 140f);
            Time.timeScale = 0f;
            for (int i = 0; i < 12; i++) yield return null;
            Time.captureFramerate = 12;
            Time.timeScale = 1.5f;
            int after = -1;
            for (int i = 0; i < 200; i++)
            {
                { yield return null; Capture(); }
                if (after < 0 && Band.Kills > 0) after = i;
                if (after >= 0 && i - after > 40) break;
            }
            Time.captureFramerate = 0; Time.timeScale = 1f;
            Debug.Log($"[Hunt] done: {frame} frames, throws {Band.Throws}, brought home {Band.Kills}");
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }

        bool HuntersClose(float range)
        {
            if (Band.HuntHerd < 0) return true;
            var deer = Wild.Herds[Band.HuntHerd];
            int n = Wildlife.ActiveCount(Map.Herds[Band.HuntHerd], deer);
            foreach (var p in Band.People)
            {
                if (!p.Hunter) continue;
                for (int i = 0; i < n; i++) if (math.distance(p.Pos, deer[i].Pos) < range) return true;
            }
            return false;
        }


        /// <summary>Trees on or beside the flow-field path from one cell to another (deer under canopy can't be seen).</summary>
        int TreesAlongPath(int from, int to)
        {
            int slot = Map.Flow.Resolve(to), cell = from, trees = 0;
            for (int s = 0; s < 64 && cell != to; s++)
            {
                trees += Map.CountFeature("trees", new float2(cell % Map.W + 0.5f, cell / Map.W + 0.5f), 1);
                byte code = Map.Flow.Fields[slot * Map.Flow.Cells + cell];
                if (code == FlowDir.None || code == FlowDir.Target) break;
                int2 xy = Map.XY(cell) + FlowDir.Offset(code);
                cell = Map.Cell(xy.x, xy.y);
            }
            return trees;
        }

        bool AllHome(int herd)
        {
            int n = Wildlife.ActiveCount(Map.Herds[herd], Wild.Herds[herd]);
            for (int i = 0; i < n; i++)
            {
                var s = Wild.Herds[herd][i].State;
                if (s == DeerState.ToWater || s == DeerState.Drink || s == DeerState.Return) return false;
            }
            return true;
        }

        bool FirstDrinker(int herd, out Deer deer)
        {
            foreach (var d in Wild.Herds[herd])
                if (d.State == DeerState.Drink && d.Head > 0.9f) { deer = d; return true; }
            deer = Wild.Herds[herd][0];
            return false;
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
