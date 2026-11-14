using System.Collections;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // -uishots, Agricultural Age part: the auto-player plays on to the Bronze Age while the shots catch fields through
    // the year, a rebuild, a well and its reach, the laws, revealed ore and a mine, the smelter, the grown town and
    // the end-of-slice screen. Same machinery as GameController.UiShots (Advance, Shot, LookAt).
    public sealed partial class GameController
    {
        IEnumerator AgriShots()
        {
            if (Sim.Age.order < 1) yield return Advance(() => Sim.Age.order >= 1, 20000, 24);
            Debug.Log($"[UiShots] agricultural: day {Sim.Day} (year {Sim.Year + 1}), {Sim.Population} people");

            // Fields through one year: shoots in spring, growing in summer, ripe at harvest, stubble in winter.
            Building field = null;
            yield return Advance(() => Sim.Season == 0 && Sim.DayOfSeason >= 6 && (field = BestField()) != null && field.Sown > 0.6f, 20000, 12);
            if (field != null)
            {
                foreach (var (season, day, name) in new[] { (0, 6, "agri_fields_spring"), (1, 6, "agri_fields_summer"), (2, 2, "agri_fields_autumn"), (3, 4, "agri_fields_winter") })
                {
                    yield return Advance(() => Sim.Season == season && Sim.DayOfSeason >= day && Sim.DayFraction > 0.42f && Sim.DayFraction < 0.5f, 20000, 6);
                    if (!field.Alive) field = BestField();
                    if (field == null) break;
                    Hud.Select(field);
                    LookAt(World(field.Center + new float2(1.5f, -1f)), 21f, 48f, 25f);
                    yield return Settle(20);
                    yield return Shot(name);
                }
                Hud.Select(null);
            }

            // A citizen with the needs of the age (water, pottery, faith) in the panel.
            var person = Sim.Citizens.Where(c => c.Activity == Activity.Working && c.Age >= Data.Balance.adultAge)
                .OrderBy(c => Map.CountFeature("trees", c.Pos, 3)).FirstOrDefault();
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

            // A hut being rebuilt in place (in summer light, not melting snow).
            Building up = null;
            yield return Advance(() => (up = Sim.Buildings.FirstOrDefault(b => b.UpgradeTo != null && b.Progress > 0.45f)) != null && (Sim.Season == 1 || Sim.Season == 2) && Sim.IsWorkTime, 6000, 3);
            Hud.ClearBanner();
            if (up != null)
            {
                Hud.Select(up);
                LookAt(World(up.Center), 14f, 46f, 30f);
                yield return Settle(15);
                yield return Shot("agri_upgrade");
                Hud.Select(null);
            }

            // A well and the homes it reaches.
            yield return Advance(() => Sim.Season == 1 && Sim.DayFraction > 0.42f && Sim.DayFraction < 0.5f, 6000, 6);
            Hud.ClearBanner();
            var well = Sim.Buildings.Where(b => b.Active && b.Def.waterRadius > 0).OrderByDescending(b => Sim.Buildings.Count(h => h.Active && h.Def.housing > 0 && math.distance(h.Center, b.Center) < b.Def.waterRadius)).FirstOrDefault();
            if (well != null)
            {
                Hud.Select(well);
                LookAt(World(well.Center), 34f, 60f, 20f);
                yield return Settle(10);
                yield return Shot("agri_well");
                Hud.Select(null);
            }

            // The laws, once there is a council.
            yield return Advance(() => Sim.LawsInForce && Sim.Season >= 1, 6000, 12);
            Hud.ClearBanner();
            if (Sim.LawsInForce)
            {
                var council = Sim.Buildings.First(b => b.Active && b.Def.enablesLaws);
                Hud.Select(council);
                LookAt(World(council.Center), 18f, 48f, 25f);
                yield return Settle(10);
                Hud.ToggleLaws();
                yield return Shot("agri_laws");
                Hud.ToggleLaws();
                Hud.Select(null);
            }

            // Prospecting: the ore shows on the map; placing a mine marks the deposit; a mine at work.
            yield return Advance(() => Map.OreRevealed && Sim.Buildings.Any(b => b.Active && b.Def.id == "copper_mine" && b.Workers > 0) && Sim.IsWorkTime, 12000, 12);
            var mine = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "copper_mine");
            if (mine != null)
            {
                Hud.ClearBanner();
                Hud.Select(mine);
                LookAt(World(mine.Center + new float2(2f, 1f)), 22f, 50f, 30f);
                yield return Settle(20);
                yield return Shot("agri_ore_mine");
                Hud.Select(null);
                var tinDef = Data.Building("tin_mine");
                int tinCell = -1;
                for (int c = 0; c < Map.W * Map.H && tinCell < 0; c++) if (Map.Deposits[c] == Deposit.Tin && Map.BuildingAt[c] < 0) tinCell = c;
                if (tinCell >= 0 && Sim.IsUnlocked(tinDef))
                {
                    var tp = new float2(tinCell % Map.W + 0.5f, tinCell / Map.W + 0.5f);
                    LookAt(World(tp), 22f, 55f, 20f);
                    Hud.StartPlacing(tinDef);
                    yield return Settle(3);
                    Aim(tp + new float2(4f, 0));
                    yield return Shot("agri_mine_placing");
                    Hud.CancelTool();
                    Hud.PointerOverride = new Vector2(-100, -100);
                }
            }

            // The smelter at work.
            Building smelter = null;
            yield return Advance(() => (smelter = Sim.Buildings.FirstOrDefault(b => b.Active && b.Def.id == "smelter" && b.Workers > 0 && b.Status == "")) != null && Sim.IsWorkTime && Sim.DayFraction > 0.4f, 6000, 2);
            if (smelter != null)
            {
                Hud.ClearBanner();
                Hud.Select(smelter);
                LookAt(World(smelter.Center), 14f, 44f, 30f);
                yield return Settle(15);
                yield return Shot("agri_smelter");
                Hud.Select(null);
            }

            // The grown town, close and wide, at summer midday.
            yield return Advance(() => Sim.Population >= 80 && Sim.Season == 1 && Sim.DayFraction > 0.45f && Sim.DayFraction < 0.5f, 20000, 12);
            Hud.ClearBanner();
            var mid = TownMiddle();
            LookAt(World(mid), 26f, 46f, 25f);
            yield return Settle(30);
            yield return Shot("agri_town_close");
            LookAt(World(mid), 80f, 55f, 25f);
            yield return Settle(10);
            yield return Shot("agri_town_wide");
            Debug.Log($"[UiShots] town: day {Sim.Day} (year {Sim.Year + 1}), {Sim.Population} people, {Sim.Buildings.Count(b => b.Alive)} buildings, {Sim.PathCount} path tiles");

            // The Bronze Age: the end of this slice.
            int ages = Sim.AgeLog.Count;
            yield return Advance(() => Sim.Age.order >= 2, 20000, 24);
            if (Sim.Age.order >= 2)
            {
                LookAt(World(Sim.TownCenter.Center), 40f, 50f, 25f);
                yield return Settle(12);
                Hud.ClearBanner();
                Hud.EndShown = Hud.EndOpen = true;
                yield return Shot("agri_bronze_end");
                Hud.EndOpen = false;
            }
            Debug.Log($"[UiShots] agricultural shots done: day {Sim.Day} (year {Sim.Year + 1}), age {Sim.Age.name}, {Sim.Population} people");
        }

        /// <summary>The field with the most other fields around it (a farmed stretch makes the better picture).</summary>
        Building BestField() => Sim.Buildings.Where(b => b.Active && b.Def.crop)
            .OrderByDescending(b => Sim.Buildings.Count(o => o.Active && o.Def.crop && math.distance(o.Center, b.Center) < 9f) * 10 - Map.CountFeature("trees", b.Center, 4))
            .FirstOrDefault();
    }
}
