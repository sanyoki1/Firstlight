using System;
using System.Collections;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -uishots, Agricultural Age part: the auto-player plays on to the Bronze Age while the shots catch fields through
    // the year, a citizen with the new needs, a well and its reach, a rebuild, the laws, revealed ore and a mine, the
    // smelter, the grown town and the end-of-slice screen. Same machinery as GameController.UiShots (Advance, Shot,
    // LookAt). Every wait is bounded, so a run that never sees something skips that shot instead of playing on for years.
    public sealed partial class GameController
    {
        const int Year = 32 * 24;    // game hours in a year (Advance moves the sim on by hours)

        IEnumerator AgriShots()
        {
            if (Sim.Age.order < 1) yield return Advance(() => Sim.Age.order >= 1, 20000, 24);
            Debug.Log($"[UiShots] agricultural: day {Sim.Day} (year {Sim.Year + 1}), {Sim.Population} people");

            // Fields through one year: shoots in spring, green in summer, ripe and being cut in autumn, stubble in winter.
            Building field = null;
            yield return Advance(() => Sim.Season == 0 && Sim.DayOfSeason >= 6 && (field = BestField()) != null && field.Sown > 0.6f, 2 * Year / 12, 12);
            if (field != null)
            {
                foreach (var (season, day, name) in new[] { (0, 6, "agri_fields_spring"), (1, 6, "agri_fields_summer"), (2, 1, "agri_fields_autumn"), (3, 4, "agri_fields_winter") })
                {
                    yield return AdvanceToMidday(season, day);
                    if (!field.Alive) field = BestField();
                    if (field == null) break;
                    Hud.Select(field);
                    LookAt(World(field.Center + new float2(1.5f, -1f)), 21f, 48f, 25f);
                    yield return Settle(20);
                    yield return Shot(name);
                    if (season == 1) yield return SummerShots();
                }
                Hud.Select(null);
            }

            // A hut being rebuilt in place (the auto-player rebuilds when materials allow).
            Building up = null;
            yield return Advance(() => (up = Sim.Buildings.FirstOrDefault(b => b.UpgradeTo != null && b.Progress > 0.15f)) != null && Sim.Season != 3 && Sim.IsWorkTime, Year / 3, 3);
            if (up == null)
            {
                // Nothing being rebuilt just now: click "Rebuild" on a lean-to or hut, as a player would.
                yield return AdvanceToMidday(1, 0);
                var home = Sim.Buildings.Where(b => b.Active && b.Def.housing > 0 && Sim.CanUpgrade(b) && Sim.UpgradeOption(b).cost.All(c => Sim.Stock(c.id) >= c.amount))
                    .OrderBy(b => Map.CountFeature("trees", b.Center, 3)).FirstOrDefault();
                if (home != null)
                {
                    Hud.Select(home);
                    Hud.UpgradeSelected();
                    yield return Advance(() => home.UpgradeTo == null || home.Progress > 0.45f && Sim.IsWorkTime, Year / 6, 1);
                    if (home.UpgradeTo != null) up = home;
                }
            }
            if (up != null)
            {
                Hud.ClearBanner();
                Hud.Select(up);
                LookAt(World(up.Center), 14f, 46f, 30f);
                yield return Settle(15);
                yield return Shot("agri_upgrade");
                Hud.Select(null);
            }

            // The laws, once there is a council (not in winter).
            yield return Advance(() => Sim.LawsInForce && Sim.Season >= 1 && Sim.Season <= 2 && Sim.IsWorkTime, 2 * Year / 12, 12);
            if (Sim.LawsInForce)
            {
                Hud.ClearBanner();
                var council = Sim.Buildings.First(b => b.Active && b.Def.enablesLaws);
                Hud.Select(council);
                LookAt(World(council.Center), 18f, 48f, 25f);
                yield return Settle(10);
                Hud.ToggleLaws();
                yield return Shot("agri_laws");
                Hud.ToggleLaws();
                Hud.Select(null);
            }

            // Prospecting: the ore shows on the map; a mine stands on it.
            yield return Advance(() => Map.OreRevealed && Sim.Buildings.Any(b => b.Active && b.Def.id == "copper_mine" && b.Workers > 0) && (Sim.Season == 1 || Sim.Season == 2) && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.6f, 3 * Year / 2, 2);
            var mine = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "copper_mine");
            if (mine != null)
            {
                Hud.ClearBanner();
                Hud.EndShown = true; Hud.EndOpen = false;
                Hud.Select(mine);
                // The stretch of copper ground with the fewest trees over it (deposits are often wooded), near the mine.
                int bestCell = -1; float bestScore = float.MaxValue;
                for (int c = 0; c < Map.W * Map.H; c++)
                {
                    if (Map.Deposits[c] != Deposit.Copper) continue;
                    var p = new float2(c % Map.W + 0.5f, c / Map.W + 0.5f);
                    float score = Map.CountFeature("trees", p, 4) * 3f + math.distance(p, mine.Center) * 0.2f;
                    if (score < bestScore) { bestScore = score; bestCell = c; }
                }
                var at = bestCell >= 0 ? new float2(bestCell % Map.W + 0.5f, bestCell / Map.W + 0.5f) : mine.Center;
                if (math.distance(at, mine.Center) > 12f) Hud.Select(null);
                LookAt(World(at), 22f, 58f, 30f);
                yield return Settle(20);
                yield return Shot("agri_ore_mine");
                Hud.Select(null);
            }

            // The smelter at work.
            Building smelter = null;
            yield return Advance(() => (smelter = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "smelter" && b.Workers > 0)) != null
                                       && (Sim.Season == 1 || Sim.Season == 2) && Sim.DayFraction > 0.4f && Sim.DayFraction < 0.6f, 2 * Year / 2, 2);
            if (smelter != null)
            {
                Hud.ClearBanner();
                Hud.EndShown = true; Hud.EndOpen = false;
                Hud.Select(smelter);
                LookAt(World(smelter.Center), 14f, 44f, 30f);
                yield return Settle(15);
                yield return Shot("agri_smelter");
                Hud.Select(null);
            }

            // The Bronze Age: the end of this slice (the end screen opens by itself when the age is reached).
            yield return Advance(() => Sim.Age.order >= 2, 6 * Year / 24, 24);
            if (Sim.Age.order >= 2)
            {
                LookAt(World(Sim.TownCenter.Center), 40f, 50f, 25f);
                yield return Settle(12);
                Hud.ClearBanner();
                Hud.EndShown = Hud.EndOpen = true;
                yield return Shot("agri_bronze_end");
                Hud.EndOpen = false;
            }

            // The grown town, close and wide, at summer midday.
            yield return AdvanceToMidday(1, 3);
            Hud.ClearBanner();
            Hud.EndShown = true; Hud.EndOpen = false;
            var mid = TownMiddle();
            LookAt(World(mid), 26f, 46f, 25f);
            yield return Settle(30);
            yield return Shot("agri_town_close");
            LookAt(World(mid), 80f, 55f, 25f);
            yield return Settle(10);
            yield return Shot("agri_town_wide");
            Debug.Log($"[UiShots] agricultural shots done: day {Sim.Day} (year {Sim.Year + 1}), age {Sim.Age.name}, {Sim.Population} people, {Sim.Buildings.Count(b => b.Alive)} buildings, {Sim.PathCount} path tiles");
        }

        /// <summary>Summer midday, while the fields are green: a citizen with the age's needs, and a well's reach.</summary>
        IEnumerator SummerShots()
        {
            Hud.Select(null);
            var person = Sim.Citizens.Where(c => c.Activity == Activity.Working && c.Age >= Data.Balance.adultAge && c.Home != null)
                .OrderBy(c => Map.CountFeature("trees", c.Pos, 3) * 10 + (c.AtField ? 0 : 5)).FirstOrDefault();
            if (person != null)
            {
                Hud.SelectCitizen(person);
                LookAt(World(person.Pos), 13f, 50f, 35f);
                yield return Settle(12);
                LookAt(World(SView.Drawn.FirstOrDefault(d => d.c == person).pos), 13f, 50f, 35f);
                yield return Settle(4);
                yield return Shot("agri_citizen");
                Hud.SelectCitizen(null);
            }
            var well = Sim.Buildings.Where(b => b.Active && b.Def.waterRadius > 0)
                .OrderBy(b => Map.CountFeature("trees", b.Center, 3) * 4 - Sim.Buildings.Count(h => h.Active && h.Def.housing > 0 && math.distance(h.Center, b.Center) < b.Def.waterRadius)).FirstOrDefault();
            if (well != null)
            {
                Hud.Select(well);
                LookAt(World(well.Center), 30f, 58f, 20f);
                yield return Settle(10);
                yield return Shot("agri_well");
                Hud.Select(null);
            }
        }

        /// <summary>On to a given season and day at midday (within a year).</summary>
        IEnumerator AdvanceToMidday(int season, int day) =>
            Advance(() => Sim.Season == season && Sim.DayOfSeason >= day && Sim.DayFraction > 0.42f && Sim.DayFraction < 0.5f, Year / 2, 2);

        /// <summary>The field with the most other fields around it (a farmed stretch makes the better picture).</summary>
        Building BestField() => Sim.Buildings.Where(b => b.Active && b.Def.crop)
            .OrderByDescending(b => Sim.Buildings.Count(o => o.Active && o.Def.crop && math.distance(o.Center, b.Center) < 9f) * 10 - Map.CountFeature("trees", b.Center, 4))
            .FirstOrDefault();
    }
}
