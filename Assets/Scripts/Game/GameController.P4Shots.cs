using System.Collections;
using System.IO;
using System.Linq;
using Firstlight.Game.UI;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -p4shots -load SAVE -outdir DIR (windowed, like -uishots): screenshots of phase 4 through the same actions a player's
    // clicks take. Stage 1 is the title menu (menu, new game, settings), stage 2 a new game (campfire, the guide at three
    // steps, a hint, a turned building), stage 3 the given mid-game save (top bar, path drag, traders, game menu, save and
    // load screens, night firelight, canopy cut-away, a flood, the top bar at 1280x720). Stages pass on through a static
    // because each one reloads the scene, as the menu does.
    public sealed partial class GameController
    {
        static int _p4Stage;
        static string _p4Dir, _p4Save;

        IEnumerator P4Shots()
        {
            if (_p4Stage == 0)
            {
                _p4Stage = 1;
                _p4Dir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "p4shots"));
                _p4Save = Arg("-load");
                Directory.CreateDirectory(_p4Dir);
                foreach (var f in Directory.GetFiles(_p4Dir, "*.png")) File.Delete(f);
                // The title menu first (this run was started with -load, which skips the menu): reload into it.
                ToMainMenu();
                yield break;
            }
            _shotDir = _p4Dir;
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[P4Shots] stage {_p4Stage}, screen {Screen.width}x{Screen.height}, in menu {InMenu}");

            if (_p4Stage == 1)
            {
                // The title screen over the turning world; new game; settings.
                for (int i = 0; i < 20; i++) yield return null;
                yield return P4Shot("menu_title");
                Hud.ShowScreen(Hud.MenuScreen.NewGame);
                yield return P4Shot("menu_new_game");
                Hud.ShowScreen(Hud.MenuScreen.Settings);
                yield return P4Shot("menu_settings");
                _p4Stage = 2;
                Hud.MenuNewGame(7, 1);
                yield break;
            }

            if (_p4Stage == 2)
            {
                Sim.SetSpeed(0);
                var tcDef = Data.Buildings.Find(b => b.townCenter);
                var site = Sim.SuggestCampfireSite();
                var center = Settlement.FootprintCenter(tcDef, site);
                LookAt(World(center), 30f, 52f, 30f);
                yield return Settle(8);
                Aim(center + new float2(0.01f, 0.01f));
                Hud.GuideHidden = false;
                yield return P4Shot("guide_1_campfire");
                Hud.ClickWorld();
                Hud.PointerOverride = new Vector2(-100, -100);
                yield return Advance(() => Sim.DayFraction > 0.42f, 400, 2);
                LookAt(World(Sim.TownCenter.Center + new float2(0, -1)), 20f, 46f, 20f);
                yield return Settle(20);
                yield return P4Shot("guide_2_leanto");
                // A turned building: a lean-to ghost turned a quarter (its door marker on the side), on a spot it fits.
                var lean = Data.Building("lean_to");
                var spot = TurnedSite(lean, 1) ?? Sim.TownCenter.Origin + new int2(6, 0);
                Hud.OpenCategory("Housing");
                Hud.StartPlacing(lean);
                Hud.RotatePlacement(1);
                var lc = Settlement.FootprintCenter(lean, spot, 1);
                LookAt(World(lc), 12f, 52f, 200f);
                yield return Settle(4);
                Aim(lc);
                yield return null;
                Aim(lc);
                yield return P4Shot("rotated_ghost");
                Hud.CancelTool(); Hud.BuildCategory = null;
                Hud.PointerOverride = new Vector2(-100, -100);
                // The guide further on (each shown as it would stand there), and a hint; by day.
                Auto = new AutoPlayer(Sim);
                yield return Advance(() => Sim.Day >= 3 && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.5f, 2000, 2);
                LookAt(World(Sim.TownCenter.Center), 28f, 50f, 20f);
                yield return Settle(10);
                Hud.GuideShowStep(5);
                yield return P4Shot("guide_6_elders");
                Hud.GuideShowStep(6);
                Hud.ShowHint("winter");
                yield return P4Shot("guide_7_winter_hint");
                Hud.ShowHint(null);
                Hud.GuideRelease();
                Auto = null;
                _p4Stage = 3;
                if (_p4Save != null && File.Exists(_p4Save)) LoadGame(_p4Save);
                else { Debug.LogWarning("[P4Shots] no -load save: done"); Application.Quit(); }
                yield break;
            }

            // Stage 3: the grown town from the save.
            Sim.SetSpeed(0);
            Auto = null;
            Hud.GuideHidden = true;
            Hud.EndShown = true; Hud.EndOpen = false;     // the save is past the Bronze age-up (no end screen here)
            Hud.ShowHint(null); Hud.ClearBanner(); Hud.Toast(null);
            yield return Advance(() => Sim.DayFraction > 0.42f && Sim.DayFraction < 0.5f, 400, 1);
            var mid = TownMiddle();
            LookAt(World(mid), 34f, 50f, 25f);
            yield return Settle(20);
            yield return P4Shot("topbar_1920");

            // Path dragging with the real drag handler: press at one end, move, the line follows, release lays it.
            var pathDef = Data.Buildings.Find(b => b.path);
            var (a, b2) = PathRun();
            LookAt(World((a + b2) / 2), 20f, 55f, 20f);
            yield return Settle(4);
            Hud.StartPlacing(pathDef);
            Aim(a); yield return null; Aim(a);
            Hud.PathDrag(true, false);
            for (int k = 1; k <= 6; k++) { Aim(math.lerp(a, b2, k / 6f)); yield return null; Hud.PathDrag(false, false); }
            yield return P4Shot("path_drag");
            int before = Sim.PathCount;
            Hud.PathDrag(false, true);
            Debug.Log($"[P4Shots] path drag laid {Sim.PathCount - before} tiles; stone now {Sim.Stock("stone"):0}");
            Hud.CancelTool();
            Hud.PointerOverride = new Vector2(-100, -100);
            yield return Settle(4);
            yield return P4Shot("path_laid");

            // Traders: the player's own offer (no auto-accept).
            Sim.AutoTrade = false;
            yield return Advance(() => Sim.PendingTrade != null, 3 * Year / 4, 3);
            if (Sim.PendingTrade != null)
            {
                LookAt(World(Sim.TownCenter.Center), 30f, 50f, 25f);
                yield return Settle(6);
                yield return P4Shot("trader_offer");
                Hud.AcceptTrade();
            }

            // The game menu, the save screen and the load screen.
            Hud.OpenMenu();
            yield return P4Shot("pause_menu");
            Hud.ShowScreen(Hud.MenuScreen.Save);
            yield return P4Shot("save_screen");
            Hud.MenuSave("Shot town");
            Hud.OpenMenu();
            Hud.ShowScreen(Hud.MenuScreen.Load);
            yield return P4Shot("load_screen");
            Hud.CloseMenu();

            // Night: the fires light the ground round them.
            yield return Advance(() => Sim.DayFraction > 0.9f || Sim.DayFraction < 0.1f, 400, 1);
            LookAt(World(Sim.TownCenter.Center), 22f, 44f, 30f);
            yield return Settle(10);
            yield return P4Shot("night_firelight");

            // Canopy cut-away: a woodcutter out among the trees, close in (and the same view without it).
            yield return Advance(() => Sim.IsWorkTime && Sim.DayFraction > 0.4f && ForestWorker() != null, 600, 1);
            var worker = ForestWorker();
            if (worker != null)
            {
                Hud.SelectCitizen(worker);
                LookAt(World(worker.Pos), 14f, 50f, 30f);
                yield return Settle(10);
                LookAt(World(SView.Drawn.FirstOrDefault(d => d.c == worker).pos), 14f, 50f, 30f);
                yield return Settle(4);
                yield return P4Shot("canopy_cut");
                CanopyCutOff = true;
                yield return P4Shot("canopy_uncut");
                CanopyCutOff = false;
                Hud.SelectCitizen(null);
            }

            // A spring flood: the warning the day before (and its hint), then buildings under water. By day.
            yield return Advance(() => Sim.DayFraction > 0.4f && Sim.DayFraction < 0.5f, 400, 1);
            Sim.DebugFloodTomorrow(0.5f);
            var flooded = Sim.Buildings.Where(x => x.Alive && Sim.FloodCells != null && Hud.UnderFlood(x)).OrderBy(x => math.distance(x.Center, Sim.TownCenter.Center)).FirstOrDefault();
            var fspot = flooded?.Center ?? Sim.TownCenter.Center;
            LookAt(World(fspot), 30f, 52f, 25f);
            Hud.ShowHint("flood");
            yield return Settle(6);
            yield return P4Shot("flood_warning");
            Hud.ShowHint(null);
            yield return Advance(() => Sim.Flooding, 400, 1);
            yield return Advance(() => Sim.DayFraction > 0.36f, 400, 1);
            // Let the river rise (the water simulation runs on the frame clock).
            Sim.SetSpeed(1);
            for (int i = 0; i < 160 && Sim.FloodLevel < Sim.FloodStrength * 0.95f; i++) yield return null;
            for (int i = 0; i < 120; i++) yield return null;
            Sim.SetSpeed(0);
            var wet = Sim.Buildings.Where(x => x.Alive && x.Flooded).OrderBy(x => math.distance(x.Center, fspot)).FirstOrDefault();
            if (wet != null) { Hud.Select(wet); LookAt(World(wet.Center), 30f, 52f, 25f); yield return Settle(3); }
            yield return P4Shot("flood_buildings");
            Hud.Select(null);

            // The top bar at 1280x720 (after the flood, by day).
            yield return Advance(() => !Sim.Flooding && Sim.DayFraction > 0.42f && Sim.DayFraction < 0.5f, 800, 1);
            if (!Application.isBatchMode)
            {
                Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                for (int i = 0; i < 10; i++) yield return null;
                LookAt(World(mid), 34f, 50f, 25f);
                yield return Settle(6);
                yield return P4Shot("topbar_1280");
            }
            Debug.Log("[P4Shots] done");
            _p4Stage = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        IEnumerator P4Shot(string name) => Shot("p4_" + name);

        /// <summary>A free spot near the campfire for a building turned <paramref name="rot"/> quarter turns.</summary>
        int2? TurnedSite(BuildingDef d, int rot)
        {
            var tc = Sim.TownCenter.Center;
            for (int r = 4; r < 16; r++)
                for (int k = 0; k < 24; k++)
                {
                    float ang = k * math.PI / 12;
                    var p = tc + new float2(math.sin(ang), math.cos(ang)) * r;
                    var o = new int2((int)p.x, (int)p.y);
                    if (Sim.CanPlace(d, o, rot, out _) && Map.CountFeature("trees", Settlement.FootprintCenter(d, o, rot), 3) == 0) return o;
                }
            return null;
        }

        /// <summary>A straight run of free ground near the town for a path (start and end points).</summary>
        (float2, float2) PathRun()
        {
            var tc = Sim.TownCenter.Center;
            for (int r = 6; r < 30; r++)
                for (int k = 0; k < 16; k++)
                {
                    float ang = k * math.PI / 8;
                    var a = tc + new float2(math.sin(ang), math.cos(ang)) * r;
                    var ca = new int2((int)a.x, (int)a.y);
                    bool ok = true;
                    for (int i = 0; i < 9 && ok; i++) ok = Sim.CanPlacePath(ca + new int2(i, 0), out _) && Map.CountFeature("trees", new float2(ca.x + i + 0.5f, ca.y + 0.5f), 2) == 0;
                    if (ok) return (new float2(ca.x + 0.5f, ca.y + 0.5f), new float2(ca.x + 8.5f, ca.y + 0.5f));
                }
            return (tc + new float2(4, -4), tc + new float2(12, -4));
        }

        /// <summary>A woodcutter or gatherer working out among the trees (the trees all round them).</summary>
        Citizen ForestWorker() => Sim.Citizens.Where(c => c.AtField && c.Activity == Activity.Working && c.Job != null && c.Job.Def.nearby?.feature == "trees")
            .OrderByDescending(c => Map.CountFeature("trees", c.Pos, 3)).FirstOrDefault(c => Map.CountFeature("trees", c.Pos, 3) >= 8);
    }
}
