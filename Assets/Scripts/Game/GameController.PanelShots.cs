using System.Collections;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -panelshots -seed N -outdir DIR (windowed, like -guideshots): screenshots of pass 2 batch 4b (panels, numbers, controls,
    // readability). The suggested campfire site with its rings and judged counts; the auto-player then plays from it and the
    // run stops to show: a homeless person's panel in the first days (with Follow on), the Paused tag, the Jobs panel, a
    // building's hover tag, the demolish tool's second-click tip, the Events panel's dates, a build list with locked rows,
    // F1; the layout at 1920x1080 160% and 70% and 1280x720 100% and 160%; the needs labels in the Agricultural Age; the end
    // screen (when the auto-player reaches the Bronze Age, else opened by hand and named "staged"); the load list. Writes
    // nothing to the real save folder (a scratch folder, see DataPaths) and quits.
    public sealed partial class GameController
    {
        /// <summary>A shot taken while paused keeps the pause (the Paused tag shows); others run at 1x for the shot.</summary>
        bool _shotKeepPause;

        IEnumerator PanelShots()
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "panelshots"));
            Directory.CreateDirectory(_shotDir);
            foreach (var f in Directory.GetFiles(_shotDir, "*.png")) File.Delete(f);
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            Sim.SetSpeed(0);
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[PanelShots] screen {Screen.width}x{Screen.height}, seed {_seed}");

            // "Show a good site": the rings on the ground and the judged counts in the tip.
            Hud.PointAtSuggestedSite();
            var site = Hud.SuggestedSite ?? Sim.TownCenter?.Center ?? new float2(64, 64);
            LookAt(World(site), 40f, 52f, 30f);
            yield return Settle(6);
            Aim(site + new float2(0.01f, 0.01f));
            yield return null; Aim(site + new float2(0.01f, 0.01f));
            yield return Shot("suggested_site");
            Hud.ClickWorld();
            Hud.PointerOverride = new Vector2(-100, -100);
            Auto = new AutoPlayer(Sim);

            // The first days: everyone sleeps by the fire, and happiness sinks below 60 %.
            yield return Advance(() => Sim.Day >= 2 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f && Sim.Citizens.Any(c => !c.Gone && c.Home == null && c.Happiness < 0.6f), 2000, 1);
            var homeless = Sim.Citizens.FirstOrDefault(c => !c.Gone && c.Home == null && c.Happiness < 0.6f && c.Age >= Data.Balance.adultAge)
                           ?? Sim.Citizens.FirstOrDefault(c => !c.Gone && c.Home == null);
            if (homeless != null)
            {
                Hud.SelectCitizen(homeless);
                Hud.Follow(homeless);
                yield return Settle(30);
                yield return Shot("citizen_homeless");
                Debug.Log($"[PanelShots] homeless {homeless.Name}, happiness {homeless.Happiness:P0}");
            }
            else Debug.Log("[PanelShots] nobody without a home in this run");
            Hud.SelectCitizen(null);

            // The second summer, midday.
            yield return Advance(() => Sim.Year >= 1 && Sim.Season == 1 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f, 6000, 2);
            Hud.ClearBanner(); Hud.ShowHint(null);

            // Paused: the tag under the top bar.
            LookAt(World(TownMiddle()), 34f, 50f, 25f);
            yield return Settle(4);
            Sim.SetSpeed(0);
            _shotKeepPause = true;
            yield return Shot("paused_tag");
            _shotKeepPause = false;

            // Jobs: coloured counts, the header and the reasons.
            Hud.ToggleJobs();
            yield return Shot("jobs");
            Hud.ToggleJobs();

            // A workplace's hover tag (the pointer on its middle).
            var work = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "woodcutter")
                       ?? Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.IsWorkplace && !b.Def.crop);
            if (work != null)
            {
                LookAt(World(work.Center), 22f, 50f, 25f);
                yield return Settle(4);
                Aim(work.Center); yield return null; Aim(work.Center);
                yield return Shot("hover_building");
                Hud.PointerOverride = new Vector2(-100, -100);
            }

            // The demolish tool on a lived-in home: the first click asks again and says what goes with it.
            var home = Sim.Buildings.Where(b => b.Active && b.Def.housing > 0 && b.Residents > 0).OrderByDescending(b => b.Residents).FirstOrDefault();
            if (home != null)
            {
                LookAt(World(home.Center), 22f, 50f, 25f);
                yield return Settle(4);
                Hud.StartDemolish();
                Aim(home.Center); yield return null; Aim(home.Center);
                Hud.ClickWorld();
                yield return Shot("demolish_confirm");
                Debug.Log($"[PanelShots] demolish asked again: {home.Def.name} still standing = {home.Alive}");
                Hud.PointerOverride = new Vector2(-100, -100);
                Hud.CancelTool();
            }

            // The Events panel's dates; a build list with locked rows and the guide's step counter; F1.
            LookAt(World(TownMiddle()), 34f, 50f, 25f);
            yield return Settle(2);
            Hud.ShowEvents(false, false);
            yield return Shot("events_dates");
            Hud.EventsOpen = false;
            Hud.GuideHidden = false;
            Hud.OpenCategory("Crafts");
            yield return Shot("build_list_locked");
            Hud.BuildCategory = null;
            Hud.HelpOpen = true;
            yield return Shot("help");
            Hud.HelpOpen = false;

            // Layout checks: 1920x1080 at 160% and 70%, 1280x720 at 100% and 160% (Jobs, a citizen and a build list open; Events; F1).
            var someone = Sim.Citizens.FirstOrDefault(c => !c.Gone && c.Home == null) ?? Sim.Citizens.FirstOrDefault(c => !c.Gone && c.Job != null) ?? Sim.Citizens.FirstOrDefault(c => !c.Gone);
            foreach (var (w, h, ui) in new[] { (1920, 1080, 1.6f), (1920, 1080, 0.7f), (1280, 720, 1f), (1280, 720, 1.6f) })
            {
                Firstlight.Game.UI.UiTheme.UserScale = ui;
                if (Screen.width != w || Screen.height != h) Screen.SetResolution(w, h, FullScreenMode.Windowed);
                for (int i = 0; i < 12; i++) yield return null;
                string tag = $"{h}p_{ui * 100:0}";
                if (someone != null) Hud.SelectCitizen(someone);
                Hud.ToggleJobs();
                Hud.OpenCategory("Crafts");
                yield return Shot($"layout_{tag}");
                Hud.ToggleJobs(); Hud.BuildCategory = null; Hud.SelectCitizen(null);
                Hud.ShowEvents(false, false);
                yield return Shot($"layout_{tag}_events");
                Hud.EventsOpen = false;
                Hud.HelpOpen = true;
                yield return Shot($"layout_{tag}_help");
                Hud.HelpOpen = false;
            }
            Firstlight.Game.UI.UiTheme.UserScale = 1f;
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            for (int i = 0; i < 12; i++) yield return null;

            // Later: the Agricultural Age's needs in the top bar (the Pots tooltip shown as a hover would show it).
            yield return Advance(() => Sim.Age.order >= 1 && Sim.NeedActive("goods") && Sim.Season == 1 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f, 12000, 3);
            Hud.ClearBanner(); Hud.ShowHint(null);
            LookAt(World(TownMiddle()), 38f, 50f, 25f);
            yield return Settle(6);
            Hud.ForceTooltip(Hud.NeedTipFor("goods"), new Vector2(1500, 84));
            yield return Shot("needs_labels");
            Hud.ForceTooltip(null, default);

            // The end screen when the auto-player reaches the Bronze Age (opened by hand if it has not within the run).
            yield return Advance(() => Hud.EndOpen, 12000, 3);
            bool staged = !Hud.EndOpen;
            Hud.EndOpen = true;
            Hud.ClearBanner();
            yield return Settle(4);
            yield return Shot(staged ? "end_screen_staged" : "end_screen");
            Hud.EndOpen = false;

            // The load list (a save made now, in the scratch folder) without the stray file-name line.
            SaveTo(SaveGame.PathFor("Panel shots"), "Panel shots", "manual");
            Hud.OpenMenu(); Hud.ShowScreen(Firstlight.Game.UI.Hud.MenuScreen.Load);
            yield return Shot("load_list");
            Hud.CloseMenu();
            Firstlight.Game.UI.UiTheme.UserScale = 1f;
            Debug.Log("[PanelShots] done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }
    }
}
