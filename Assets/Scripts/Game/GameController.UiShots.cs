using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -uishots -seed N -outdir DIR: plays a settlement with the auto-player and saves 1920x1080 screenshots of the
    // HUD and the town (run with -batchmode -screen-width 1920 -screen-height 1080). It drives the HUD through the
    // same action methods a click calls (Hud.ClickWorld, StartPlacing, Select, ToggleResearch...) and aims the
    // placement ghost with Hud.PointerOverride instead of the mouse. Ends with a frame-time probe (also with
    // -crowd N extra people) and quits.
    public sealed partial class GameController
    {
        string _shotDir;
        int _shotNo;

        IEnumerator UiShots()
        {
            _shotDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "uishots");
            _shotDir = Path.GetFullPath(_shotDir);
            Directory.CreateDirectory(_shotDir);
            // Windowed (for the HUD) the player renders slowly when nobody is at the screen, so frames are few and
            // large: a fixed 10 fps game clock, and the sim is moved on by whole hours per frame.
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            Sim.SetSpeed(0);                           // the sim is moved on by hand below
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[UiShots] screen {Screen.width}x{Screen.height}, seed {_seed}");
            var tcDef = Data.Buildings.Find(b => b.townCenter);

            // 1. Choosing the campfire site.
            var site = Sim.SuggestCampfireSite();
            var center = Settlement.FootprintCenter(tcDef, site);
            LookAt(World(center), 30f, 52f, 30f);
            yield return Settle(8);
            Aim(center + new float2(0.01f, 0.01f));
            yield return Shot("campfire_site");

            // 2. Placed by the same path as a click; the auto-player runs the settlement from here.
            Hud.ClickWorld();
            Hud.PointerOverride = new Vector2(-100, -100);
            Auto = new AutoPlayer(Sim);
            yield return Advance(() => Sim.Day >= 1 && Sim.DayFraction > 0.42f, 400, 12);
            LookAt(World(Sim.TownCenter.Center + new float2(0, -1)), 17f, 42f, 20f);
            yield return Settle(30);
            yield return Shot("early_camp");

            // 3. Build menu open, a ghost with its radius and the yield it would get.
            var gath = Data.Building("gatherers_camp");
            var spot = OpenSite(gath) ?? Auto.FindSite(gath) ?? Sim.TownCenter.Origin + new int2(8, 0);
            var gc = Settlement.FootprintCenter(gath, spot);
            LookAt(World(math.lerp(Sim.TownCenter.Center, gc, 0.35f)), 34f, 62f, 20f);
            yield return Settle(5);
            Hud.OpenCategory("Food");
            Hud.StartPlacing(gath);
            Aim(gc);
            yield return Shot("build_menu");
            // The same tool over the river: red, with the reason.
            var wet = NearestRiverCell(Sim.TownCenter.Center);
            Aim(new float2(wet % Map.W + 0.5f, wet / Map.W + 0.5f));
            LookAt(World(new float2(wet % Map.W + 0.5f, wet / Map.W + 0.5f)), 26f, 58f, 20f);
            yield return Settle(3);
            Aim(new float2(wet % Map.W + 0.5f, wet / Map.W + 0.5f));
            yield return Shot("placement_invalid");
            Hud.CancelTool(); Hud.BuildCategory = null;
            Hud.PointerOverride = new Vector2(-100, -100);

            // 4. A construction site part-way up.
            Building rising = null;
            // (the one with the fewest trees round it, so it can be seen)
            yield return Advance(() => (rising = Sim.Buildings.Where(b => b.IsSite && b.Progress > 0.5f && b.Progress < 0.8f)
                .OrderBy(b => Map.CountFeature("trees", b.Center, 4)).FirstOrDefault()) != null && Map.CountFeature("trees", rising.Center, 4) < 6, 3000, 3);
            if (rising != null)
            {
                LookAt(World(rising.Center), 13f, 50f, 25f);
                Hud.Select(rising);
                yield return Settle(15);
                yield return Shot("construction_site");
            }

            // 5. A workplace's panel, during the workday.
            yield return Advance(() => Sim.DayFraction > 0.36f && Sim.DayFraction < 0.5f && Sim.Buildings.Any(b => b.Active && b.Def.id == "woodcutter" && b.Workers > 0), 3000, 6);
            var wc = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "woodcutter") ?? Sim.Buildings.First(b => b.Active && b.Def.IsWorkplace);
            Hud.Select(wc);
            LookAt(World(wc.Center), 22f, 56f, 15f);
            yield return Settle(20);
            yield return Shot("building_panel");

            // 6. A citizen out at work (or carrying).
            // Someone at work where the trees don't hide them.
            var person = Sim.Citizens.Where(c => c.Activity is Activity.Working or Activity.Carrying or Activity.Building)
                .OrderBy(c => Map.CountFeature("trees", c.Pos, 3) * 10 - (c.Activity == Activity.Building ? 15 : 0)).FirstOrDefault() ?? Sim.Citizens[0];
            Hud.SelectCitizen(person);
            LookAt(World(person.Pos), 12f, 50f, 35f);
            yield return Settle(12);
            LookAt(World(SView.Drawn.FirstOrDefault(d => d.c == person).pos), 12f, 50f, 35f);
            yield return Settle(5);
            yield return Shot("citizen_panel");
            Hud.SelectCitizen(null);

            // 7. Research tree with a tooltip; 8. goals.
            Hud.ToggleResearch();
            yield return null;
            Hud.ShowResearchTip("farming");
            yield return Shot("research");
            Hud.ForceTooltip(null, default);
            Hud.ToggleResearch();
            Hud.ToggleGoals();
            LookAt(World(Sim.TownCenter.Center), 30f, 50f, 20f);
            yield return Shot("goals");
            Hud.ToggleGoals();

            // 9. On to a town of about 40 (the auto-player builds and researches as it goes); catch the age-up banner.
            int ages = Sim.AgeLog.Count;
            bool bannerShot = false;
            yield return Advance(() =>
            {
                if (!bannerShot && Sim.AgeLog.Count > ages) { bannerShot = true; _bannerPending = true; return true; }
                return Sim.Population >= 40 || Sim.Year >= 8;
            }, 40000, 24);
            if (_bannerPending)
            {
                _bannerPending = false;
                yield return Settle(15);
                yield return Shot("age_up");
                Hud.ClearBanner();
                yield return Advance(() => Sim.Population >= 40 || Sim.Year >= 8, 40000, 24);
            }
            Debug.Log($"[UiShots] town: day {Sim.Day} (year {Sim.Year + 1} {Sim.SeasonName}), {Sim.Population} people, {Sim.Buildings.Count(b => b.Alive)} buildings, age {Sim.Age.name}");

            // Summer midday: town close and wide, jobs and stocks panels.
            yield return Advance(() => Sim.Season == 1 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.5f, 20000, 12);
            var mid = TownMiddle();
            LookAt(World(mid), 24f, 46f, 25f);
            yield return Settle(30);
            yield return Shot("town_close");
            LookAt(World(mid), 70f, 55f, 25f);
            yield return Settle(8);
            yield return Shot("town_wide");
            Hud.ToggleJobs();
            LookAt(World(mid), 40f, 52f, 25f);
            yield return Settle(3);
            yield return Shot("jobs");
            Hud.ToggleStocks();
            yield return Shot("stocks");
            Hud.ToggleStocks();

            // Storage: a pile's panel with settings set (High, food shut out, a reserve of stone, a cap on wood), then its
            // Storage settings panel. Put back afterwards, so the rest of the run goes on as before.
            var pile = Sim.Buildings.Where(b => b.Active && b.Def.id == "storage_pile").OrderByDescending(b => b.Stored).FirstOrDefault();
            if (pile != null)
            {
                var asBuilt = Sim.GetStoreSettings(pile);
                Sim.SetStorePriority(pile, Priority.High);
                Sim.SetStoreAllowAll(pile, false, "food");
                Sim.SetKeepMin(pile, Sim.ResIndex("stone"), 40);
                Sim.SetKeepMax(pile, Sim.ResIndex("wood"), 80);
                Hud.CopyStore(pile);
                Hud.Select(pile);
                LookAt(World(pile.Center), 22f, 56f, 15f);
                yield return Settle(10);
                yield return Shot("storage_panel");
                Hud.ToggleStorage();
                yield return Settle(3);
                yield return Shot("storage_settings");
                Hud.ToggleStorage();
                Hud.Select(null);
                Sim.ApplyStoreSettings(pile, asBuilt);
            }
            Hud.ToggleHelp();
            yield return Shot("help");
            Hud.ToggleHelp();

            // Hunters out at a herd (if the camp is staffed and someone is out there now).
            Citizen hunter = null;
            yield return Advance(() => (hunter = Sim.Citizens.FirstOrDefault(c => c.AtField && c.Job != null && c.Job.Def.nearby?.feature == "wildlife" && c.Activity == Activity.Working)) != null, 300, 2);
            if (hunter != null)
            {
                Hud.SelectCitizen(hunter);
                LookAt(World(hunter.Pos), 16f, 52f, 30f);
                yield return Settle(12);
                yield return Shot("hunters");
                Hud.SelectCitizen(null);
            }

            // Night (autumn), then winter snow at midday.
            yield return Advance(() => Sim.Season == 2 && Sim.DayFraction > 0.9f, 20000, 12);
            LookAt(World(Sim.TownCenter.Center), 22f, 42f, 30f);
            yield return Settle(12);
            yield return Shot("night");
            yield return Advance(() => Sim.Season == 3 && Sim.DayOfSeason >= 3 && Sim.DayFraction > 0.45f, 20000, 12);
            LookAt(World(mid), 32f, 48f, 40f);
            yield return Settle(20);
            yield return Shot("winter");

            // On through the Agricultural Age to the Bronze Age (-noagri skips it).
            if (!HasArg("-noagri")) yield return AgriShots();
            mid = TownMiddle();

            // Frame time with the view on (batch mode only: a windowed player nobody watches is throttled): the town,
            // then with a crowd added.
            if (!Application.isBatchMode) { Debug.Log("[UiShots] done"); yield return null; Application.Quit(); yield break; }
            LookAt(World(mid), 30f, 50f, 25f);
            Sim.SetSpeed(1);
            Time.captureFramerate = 0;
            yield return Probe($"town of {Sim.Population}");
            int crowd = ArgInt("-crowd", 400);
            if (crowd > 0)
            {
                Sim.DebugSpawn(crowd);
                for (int i = 0; i < 30; i++) yield return null;
                yield return Probe($"town plus {crowd} (population {Sim.Population})");
                if (HasArg("-perfsplit"))
                {
                    SView.HidePeople = true;
                    yield return Probe("same, people not drawn");
                    SView.HidePeople = false;
                    _sun.shadows = LightShadows.None;
                    yield return Probe("same, no shadows");
                    _sun.shadows = LightShadows.Soft;
                    Sim.SetSpeed(0);
                    yield return Probe("same, sim paused");
                    Sim.SetSpeed(1);
                }
                LookAt(World(mid), 16f, 45f, 25f);
                yield return Settle(12);
                yield return Shot("crowd");
            }
            Debug.Log("[UiShots] done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        bool _bannerPending;

        void LookAt(Vector3 focus, float dist, float pitch, float yaw) { Hud.StopCamera(); Cam.SetView(focus, dist, pitch, yaw); }

        Vector3 World(float2 p) => new(p.x, Map.HeightAt(p.x, p.y), p.y);

        void Aim(float2 p) => Hud.PointerOverride = (Vector2)Cam.Cam.WorldToScreenPoint(World(p));

        /// <summary>A valid spot on open ground (no trees within 3 cells) that still has trees in working reach.</summary>
        int2? OpenSite(BuildingDef d)
        {
            int2? best = null; float bestScore = float.MinValue;
            var tc = Sim.TownCenter.Center;
            for (int dy = -18; dy <= 18; dy++)
                for (int dx = -18; dx <= 18; dx++)
                {
                    var o = new int2((int)tc.x + dx, (int)tc.y + dy);
                    if (!Sim.CanPlace(d, o)) continue;
                    var c = Settlement.FootprintCenter(d, o);
                    if (Map.CountFeature("trees", c, 3) > 0) continue;
                    float score = math.min(Map.CountFeature(d.nearby?.feature ?? "trees", c, d.nearby?.radius ?? 8), d.nearby?.full ?? 20) - math.length(new float2(dx, dy)) * 0.5f;
                    if (score > bestScore) { bestScore = score; best = o; }
                }
            return best;
        }

        float2 TownMiddle()
        {
            float2 s = 0; int n = 0;
            foreach (var b in Sim.Buildings) if (b.Alive) { s += b.Center; n++; }
            return n > 0 ? math.lerp(Sim.TownCenter.Center, s / n, 0.5f) : Sim.TownCenter.Center;
        }

        // The back buffer is not presented in batch mode (ScreenCapture saves black), so shots go through a render
        // texture: the camera renders into it for that frame, OnGUI draws the HUD into it (OnGUI only runs in a
        // windowed player, so batch-mode shots show the world only), and it is read back at the end of the frame.
        RenderTexture _shotRT;
        string _pendingShot;
        bool _guiDrawn;

        IEnumerator Shot(string name)
        {
            for (int i = 0; i < 2; i++) yield return null;
            _shotNo++;
            string path = Path.Combine(_shotDir, $"{_shotNo:00}_{name}.png");
            // The shot is the window's size (the HUD lays itself out for it: -p4shots also takes 1280x720).
            int sw = Application.isBatchMode ? 1920 : Screen.width, sh = Application.isBatchMode ? 1080 : Screen.height;
            if (_shotRT != null && (_shotRT.width != sw || _shotRT.height != sh)) { _shotRT.Release(); _shotRT = null; }
            _shotRT ??= new RenderTexture(sw, sh, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            Cam.Cam.targetTexture = _shotRT;
            _pendingShot = path; _guiDrawn = false;
            float speed = Sim.Speed;
            if (speed == 0 && !_shotKeepPause) Sim.SetSpeed(1);          // the top bar shows 1x, not the hand-driven pause
            yield return new WaitForEndOfFrame();
            if (Application.isBatchMode)
            {
                // Batch mode does not render cameras on its own: ask the pipeline for one frame into the texture.
                var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _shotRT };
                if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(Cam.Cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, req);
                else Cam.Cam.Render();
            }
            var prev = RenderTexture.active;
            RenderTexture.active = _shotRT;
            var tex = new Texture2D(_shotRT.width, _shotRT.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, _shotRT.width, _shotRT.height), 0, 0);
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
            Cam.Cam.targetTexture = null;
            _pendingShot = null;
            Sim.SetSpeed(speed);
            Debug.Log($"[UiShots] {path}: day {Sim.Day} {Sim.SeasonName} {Sim.DayFraction:F2}, population {Sim.Population}" + (_guiDrawn ? "" : " (world only: no OnGUI in batch mode)"));
            yield return null;
        }

        /// <summary>Lets the drawn world catch up at normal speed (people walk to their sim positions).</summary>
        IEnumerator Settle(int frames)
        {
            Sim.SetSpeed(1);
            for (int i = 0; i < frames; i++) yield return null;
            Sim.SetSpeed(0);
        }

        /// <summary>Runs the sim (and the auto-player, hourly) by hand, some game hours per frame, until a condition holds.</summary>
        IEnumerator Advance(Func<bool> until, int maxFrames, float hoursPerFrame)
        {
            float hour = Data.Balance.dayLengthSeconds / 24f;
            for (int f = 0; f < maxFrames && !until(); f++)
            {
                for (float h = 0; h < hoursPerFrame; h += 0.25f)
                {
                    Sim.Tick(hour * math.min(0.25f, hoursPerFrame));
                    _autoClock += hour * math.min(0.25f, hoursPerFrame);
                    if (_autoClock >= hour) { _autoClock = 0; Auto?.Think(); }
                    if (until()) break;
                }
                yield return null;
            }
        }

        IEnumerator Probe(string what)
        {
            for (int i = 0; i < 20; i++) yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            const int frames = 240;
            long gc0 = GC.CollectionCount(0);
            // Batch mode does not render by itself: render each frame into a 1920x1080 texture so drawing is counted.
            _shotRT ??= new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _shotRT };
            double cpu = 0;
            for (int i = 0; i < frames; i++)
            {
                var f0 = sw.Elapsed.TotalMilliseconds;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, req);
                cpu += sw.Elapsed.TotalMilliseconds - f0;
                yield return null;
            }
            Debug.Log($"[Perf] {what}: {sw.Elapsed.TotalMilliseconds / frames:F2} ms per frame over {frames} frames, of which rendering submit {cpu / frames:F2} ms (batch mode, rendered to 1920x1080), gen0 collections {GC.CollectionCount(0) - gc0}");
        }
    }
}
