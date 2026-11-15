using System;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -guideshots -seed N -outdir DIR (windowed, like -uishots): screenshots of the first-hour guidance (pass 2 batch 4a).
    // Stage 1, a new game played by the player's own commands up to the Elders' Fire: the guide's stone step with its two
    // buttons, the placement tip short of stone, the site waiting for stone, the site panel at night. Stage 2, the same
    // seed started again and played by the auto-player from the campfire (the hand-placed start of stage 1 is not one it
    // plans for): the autumn winter hint with its numbers, a chip clicked, the ready-to-advance chip, the Events panel and
    // its Topics, F1 help, the death hint, the spring harvest outlook, a workplace waiting for an input. Writes nothing to
    // the real save folder (a scratch folder, see DataPaths) and quits.
    public sealed partial class GameController
    {
        static int _gsStage;
        static string _gsDir;
        static int _gsShotNo;

        IEnumerator GuideShots()
        {
            if (_gsStage == 0)
            {
                _gsStage = 1;
                _gsDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "guideshots"));
                Directory.CreateDirectory(_gsDir);
                foreach (var f in Directory.GetFiles(_gsDir, "*.png")) File.Delete(f);
            }
            _shotDir = _gsDir; _shotNo = _gsShotNo;
            if (!Application.isBatchMode) Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            Time.captureFramerate = 10;
            Sim.SetSpeed(0);
            for (int i = 0; i < 10; i++) yield return null;
            Debug.Log($"[GuideShots] stage {_gsStage}, screen {Screen.width}x{Screen.height}, seed {_seed}");
            Hud.GuideHidden = false;
            if (_gsStage == 2) { yield return GuideShotsAuto(); yield break; }

            // The campfire, then what the guide's first steps ask for, placed as clicks would.
            var tcDef = Data.Buildings.Find(b => b.townCenter);
            var center = Settlement.FootprintCenter(tcDef, Sim.SuggestCampfireSite());
            LookAt(World(center), 30f, 52f, 30f);
            yield return Settle(8);
            Aim(center + new float2(0.01f, 0.01f));
            yield return null;
            Hud.ClickWorld();
            Hud.PointerOverride = new Vector2(-100, -100);
            foreach (var id in new[] { "lean_to", "gatherers_camp", "woodcutter", "storage_pile" })
            {
                var d = Data.Building(id);
                var o = OpenSite(d);
                if (o != null) Sim.Place(d, o.Value, 0, out _);
            }
            Sim.QueueResearch("basic_tools");
            // The woodcutter's site takes its 4 of the 10 stone.
            yield return Advance(() => Sim.Stock("stone") < 7.9f && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.6f || Sim.Day >= 4, 800, 1);
            var tc = Sim.TownCenter.Center;
            LookAt(World(tc + new float2(0, -2)), 28f, 50f, 20f);
            yield return Settle(10);
            yield return Shot("guide_stone_two_buttons");

            // The Elders' Fire placed short of stone: the tip says the builders will wait.
            var ef = Data.Building("elders_fire");
            var eo = OpenSite(ef) ?? Sim.TownCenter.Origin + new int2(6, 6);
            var ec = Settlement.FootprintCenter(ef, eo);
            Hud.OpenCategory(ef.category);
            Hud.StartPlacing(ef);
            LookAt(World(ec), 24f, 52f, 20f);
            yield return Settle(4);
            Aim(ec); yield return null; Aim(ec);
            yield return Shot("place_short_of_stone");
            Hud.ClickWorld();
            Hud.BuildCategory = null;
            Hud.PointerOverride = new Vector2(-100, -100);
            var fire = Sim.Buildings.FirstOrDefault(b => b.Def == ef);
            yield return Advance(() => Sim.DayFraction > 0.45f && Sim.DayFraction < 0.6f, 400, 1);
            LookAt(World(tc + new float2(0, -2)), 28f, 50f, 20f);
            yield return Settle(6);
            yield return Shot("guide_waiting_for_stone");

            // Night: the site panel says the builders are resting.
            yield return Advance(() => Sim.DayFraction > 0.86f, 400, 1);
            if (fire != null && fire.Alive) { Hud.Select(fire); LookAt(World(fire.Center), 22f, 50f, 20f); }
            yield return Settle(6);
            yield return Shot("site_builders_resting");
            Hud.Select(null);
            _gsShotNo = _shotNo;
            _gsStage = 2;
            StartNewGame(_seed, 1);
        }

        /// <summary>Stage 2: the auto-player plays from the campfire.</summary>
        IEnumerator GuideShotsAuto()
        {
            var tcDef = Data.Buildings.Find(b => b.townCenter);
            var center = Settlement.FootprintCenter(tcDef, Sim.SuggestCampfireSite());
            LookAt(World(center), 30f, 52f, 30f);
            yield return Settle(4);
            Aim(center + new float2(0.01f, 0.01f));
            yield return null;
            Hud.ClickWorld();
            Hud.PointerOverride = new Vector2(-100, -100);
            Auto = new AutoPlayer(Sim);

            // The first day of autumn: the winter hint with the numbers.
            yield return Advance(() => Sim.Season == 2 && Sim.DayFraction > 0.45f, 4000, 2);
            Hud.ShowHint("winter");
            LookAt(World(TownMiddle()), 34f, 50f, 25f);
            yield return Settle(6);
            yield return Shot("autumn_winter_hint");
            Hud.ShowHint(null);

            // A chip clicked: the camera goes to what it is about (its tooltip shown as a hover would show it).
            string chip = Sim.Warnings.FirstOrDefault(w => Hud.ChipTipText(w) != null);
            if (chip == null)
            {
                yield return Advance(() => Sim.Warnings.Any(w => Hud.ChipTipText(w) != null), 800, 2);
                chip = Sim.Warnings.FirstOrDefault(w => Hud.ChipTipText(w) != null);
            }
            if (chip != null)
            {
                Hud.CycleChip(chip);
                yield return Settle(24);
                Hud.ForceTooltip(Hud.ChipTipText(chip), new Vector2(380, 104));
                yield return Shot("chip_clicked");
                Hud.ForceTooltip(null, default);
                Hud.Select(null); Hud.SelectCitizen(null);
                Debug.Log($"[GuideShots] chip clicked: {chip}");
            }

            // Everything for the next age in place: stop before the auto-player advances.
            yield return AdvanceUntilReady(6000, 3);
            LookAt(World(TownMiddle()), 36f, 50f, 25f);
            yield return Settle(8);
            yield return Shot("ready_to_advance");

            // The Events panel: all, problems only, and the Topics tab; then F1 help.
            Hud.ShowEvents(false, false);
            yield return Shot("events");
            Hud.ShowEvents(false, true);
            yield return Shot("events_problems");
            Hud.ShowEvents(true, false);
            yield return Shot("events_topics");
            Hud.EventsOpen = false;
            Hud.HelpOpen = true;
            yield return Shot("help");
            Hud.HelpOpen = false;

            // A death, and its hint naming who and of what (the age-up banner, if the auto-player advanced, is cleared).
            yield return Advance(() => Sim.TotalDeaths > 0 && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.6f, 4000, 3);
            if (Sim.TotalDeaths > 0)
            {
                Hud.ClearBanner();
                Hud.ShowHint("death");
                LookAt(World(TownMiddle()), 36f, 50f, 25f);
                yield return Settle(6);
                yield return Shot("death_hint");
                Hud.ShowHint(null);
            }

            // Spring with fields: the harvest outlook in the food tooltip (and the warning chip if it stands).
            yield return Advance(() => Sim.Age.order >= 1 && Sim.Season == 0 && Sim.DayOfSeason >= 2 && Sim.DayFraction > 0.45f && Sim.Buildings.Any(b => b.Active && b.Def.crop), 8000, 3);
            LookAt(World(TownMiddle()), 36f, 50f, 25f);
            yield return Settle(6);
            Hud.ClearBanner();
            Hud.ForceTooltip(Hud.FoodTip(), new Vector2(690, 40));
            yield return Shot("harvest_outlook");
            Hud.ForceTooltip(null, default);
            // Staged (this run only): fields pulled down one by one until the harvest falls short, to show the warning.
            var crops = Sim.Buildings.Where(b => b.Active && b.Def.crop).ToList();
            for (int i = 0; i < crops.Count && !Sim.Warnings.Any(w => w.StartsWith("Expected harvest")); i++)
            {
                Sim.Demolish(crops[i], out _);
                yield return Advance(() => Sim.Warnings.Any(w => w.StartsWith("Expected harvest")), 2, 1);
            }
            if (Sim.Warnings.Any(w => w.StartsWith("Expected harvest")))
            {
                yield return Settle(4);
                Hud.ClearBanner();
                Hud.ForceTooltip(Hud.FoodTip(), new Vector2(690, 40));
                yield return Shot("harvest_warning_staged");
                Hud.ForceTooltip(null, default);
            }

            // A workplace waiting for an input, named.
            yield return Advance(() => Sim.Buildings.Any(b => b.Active && b.Status == "Waiting for inputs"), 3000, 2);
            var waiting = Sim.Buildings.FirstOrDefault(b => b.Active && b.Status == "Waiting for inputs");
            if (waiting != null)
            {
                Hud.Select(waiting);
                Hud.ClearBanner();
                LookAt(World(waiting.Center), 22f, 50f, 20f);
                yield return Settle(4);
                yield return Shot("status_waiting_input");
                Hud.Select(null);
            }
            Debug.Log("[GuideShots] done");
            _gsStage = 0;
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        /// <summary>Like <see cref="Advance"/>, but stops as soon as the next age can be reached, before the auto-player
        /// gets to press Advance.</summary>
        IEnumerator AdvanceUntilReady(int maxFrames, float hoursPerFrame)
        {
            float hour = Data.Balance.dayLengthSeconds / 24f;
            for (int f = 0; f < maxFrames && !Sim.CanAdvanceAge(); f++)
            {
                for (float h = 0; h < hoursPerFrame; h += 0.25f)
                {
                    Sim.Tick(hour * 0.25f);
                    if (Sim.CanAdvanceAge()) break;
                    _autoClock += hour * 0.25f;
                    if (_autoClock >= hour) { _autoClock = 0; Auto?.Think(); }
                }
                yield return null;
            }
        }
    }
}
