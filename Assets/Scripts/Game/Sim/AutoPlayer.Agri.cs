using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // The auto-player's Agricultural Age: fields sized to the mouths to feed, a granary, huts and upgrades, wells where
    // homes have no water, services, the council and its laws, crafts, the metal chain out to the tin, and paths.
    // Still only public commands and public state.
    public sealed partial class AutoPlayer
    {
        int Homes() => S.Buildings.Count(b => b.Active && b.Def.housing > 0 && b.Residents > 0);
        int FirewoodUsers() => S.Buildings.Count(b => b.Active && b.Workers > 0 && b.Def.inputs.Any(i => i.ids.Contains("firewood")));

        /// <summary>A field's harvest in a normal year (most of its full yield; irrigated ones more).</summary>
        float FieldYield()
        {
            var f = D.Building("field");
            float planned = f == null ? 1 : f.outputs[0].amount * 0.7f * S.Effect("yield.farming") * (S.IsResearched("irrigation") ? 1.15f : 1f);
            // After a harvest, go by what the fields actually gave.
            return _harvestPerField > 0 ? math.clamp(_harvestPerField * 0.95f, planned * 0.6f, planned) : planned;
        }

        float _harvestPerField = -1, _grainAtAutumn;

        /// <summary>Measures the last harvest per grain field (from the public production counter).</summary>
        void WatchHarvest()
        {
            int g = S.ResIndex("grain");
            if (g < 0) return;
            if (S.Season == 2 && S.DayOfSeason == 0) _grainAtAutumn = S.GrainProducedF;
            if (S.Season == 3 && S.DayOfSeason == 0 && _grainAtAutumn >= 0)
            {
                int fields = S.Buildings.Count(b => b.Active && b.Def.crop && b.Focus <= 0);
                if (fields > 0) _harvestPerField = math.max(40f, (S.GrainProducedF - _grainAtAutumn) / fields);
                _grainAtAutumn = -1;
            }
        }

        /// <summary>Fields to feed most of the settlement (grain is the winter food); more after a hungry year.</summary>
        int FieldsWanted(int pop)
        {
            float need = (pop + 6) * S.DaysPerYear * D.Balance.foodPerAdult * _grainShare;
            int grain = (int)math.ceil(need / FieldYield());
            // No more fields than about two farmers each from 40% of the adults: the rest of the work needs hands too.
            grain = math.min(grain, math.max(2, (int)(S.Adults * 0.4f / 2f)));
            return math.max(2, grain + FibreFields());
        }

        float _grainShare = 0.6f;
        int _lastHungryYear = -1, _lastFullYear = -1;

        int FibreFields() => S.IsResearched("weaving") && S.Population >= 40 ? 1 + S.Population / 100 : 0;

        void AgriculturalWants(Action<bool, string> Want, int pop, int housing, string home)
        {
            bool known(string id) => S.IsResearched(id);
            Want(Count("field") < FieldsWanted(pop), "field");
            Want(Count("granary") < 1 + pop / 50 || GranaryFull() && Count("granary") < 2 + pop / 30, "granary");
            Want(housing < pop + 8 && pop >= 30 && GrowthOk(), home);
            Want(S.NeedActive("water") && S.Buildings.Count(b => b.Active && b.Def.housing > 0 && b.Residents > 0 && !b.HasWater) >= 2
                 && !S.Sites.Any(b => b.Def.waterRadius > 0), "well");
            Want(Count("healers_hut") < 1, "healers_hut");
            Want(Count("tribal_council") < 1 && pop >= 35, "tribal_council");
            Want(Count("shamans_hut") < 1 + (pop >= 90 ? 1 : 0), "shamans_hut");
            Want(Count("clay_pit") < 1, "clay_pit");
            Want(Count("potter") < 1 + pop / 90, "potter");
            Want(Count("elders_fire") < 3 && pop >= 45, "elders_fire");
            Want(Count("mining_pit") < 1, "mining_pit");
            Want(Count("woodcutter") < 3 + pop / 30 && S.Stock("firewood") < Enough("firewood"), "woodcutter");
            Want(Count("copper_mine") < 1, "copper_mine");
            Want(Count("smelter") < 1, "smelter");
            Want(Count("coppersmith") < 1 && Count("smelter") > 0, "coppersmith");
            Want(Count("tin_mine") < 1, "tin_mine");
            Want(Count("bronze_foundry") < 1 && Count("smelter") > 0, "bronze_foundry");
            Want(Count("bronze_smith") < 1 && Count("bronze_foundry") > 0, "bronze_smith");
            Want(Count("animal_pen") < 1 + pop / 70, "animal_pen");
            Want(Count("weaver") < 1 && FibreFields() > 0, "weaver");
            Want(Count("hunters_camp") < 2 + pop / 50, "hunters_camp");
            Want(Count("fishing_spot") < 2 + pop / 60, "fishing_spot");
            Want(Count("toolmaker") < 1 + S.Citizens.Count(c => c.Job != null && c.Job.Def.usesTools) / 35, "toolmaker");
            Want(Count("herbalist") < 1, "herbalist");
            Want(Count("hide_worker") < 1 + pop / 80, "hide_worker");
            Want(Count("stone_gatherer") < 2 + pop / 70, "stone_gatherer");
            Want(Count("storage_pile") < 2 + pop / 40, "storage_pile");
        }

        bool GranaryFull()
        {
            float cap = 0, used = 0;
            foreach (var b in S.Buildings) if (b.Active && b.Def.accepts.Length > 0) { cap += S.Capacity(b); used += b.Stored; }
            return cap > 0 && used > cap * 0.85f;
        }

        /// <summary>Agricultural-age decisions that are not new buildings: laws, upgrades, crops, wells, paths.</summary>
        void Agricultural()
        {
            if (Careless) return;
            WatchHarvest();
            // A year that ran short of food: grow more of it next year.
            if (S.Season <= 1 && S.FoodDays < 1.5f && _lastHungryYear != S.Year) { _lastHungryYear = S.Year; _grainShare = math.min(0.9f, _grainShare + 0.1f); Note($"food ran short: grain share now {_grainShare:F1}"); }
            // Still plenty at the end of summer: a smaller share will do.
            if (S.Season == 1 && S.DayOfSeason == 7 && S.FoodDays > 15f && _lastFullYear != S.Year) { _lastFullYear = S.Year; _grainShare = math.max(0.5f, _grainShare - 0.1f); Note($"food to spare: grain share now {_grainShare:F1}"); }
            Laws();
            Crops();
            Upgrades();
            Paths();
        }

        void Laws()
        {
            if (!S.LawsInForce) return;
            void Set(string id, bool on) { if (S.LawActive(id) != on && S.SetLaw(id, on, out _)) Note($"{(on ? "passed" : "ended")} law {id}"); }
            float outlook = S.FoodStock / math.max(1f, S.DailyFoodNeed * S.DaysUntilSummer());
            bool lean = S.Season == 3 || S.Season == 0;                 // after the harvest is in, before the summer food
            Set("elders_settle", S.Unrest >= 0.12f || S.LawActive("elders_settle") && S.Unrest >= 0.06f);
            // Sharing spreads a moderate shortage; in a famine (less than half the food) it would starve everyone together.
            Set("food_sharing", (lean && outlook < 0.75f || S.FoodDays < 3f) && outlook > 0.45f);
            Set("rationing", lean && outlook < 0.6f);
            Set("shared_labour", S.Sites.Count() >= 3 && S.AverageHappiness > 0.72f);
        }

        /// <summary>Keeps a field or two on flax once weaving is known (cloth for winter), the rest on grain.</summary>
        void Crops()
        {
            var fields = S.Buildings.Where(b => b.Active && b.Def.crop).ToList();
            int fibreIdx = -1;
            if (fields.Count > 0) fibreIdx = Array.FindIndex(fields[0].Def.outputs, o => o.id == "fibre");
            int wantFibre = fibreIdx >= 0 && S.ResourceUnlocked("fibre") && fields.Count >= 4 ? FibreFields() : 0;
            if (S.Stock("clothing") > S.Population * 0.8f) wantFibre = 0;
            int have = 0;
            foreach (var f in fields)
            {
                int want = have < wantFibre ? fibreIdx : 0;
                if (f.Focus != want) S.SetFocus(f, want);
                if (want == fibreIdx) have++;
            }
        }

        /// <summary>One upgrade at a time when materials allow: lean-tos and tents to huts, then huts to stone.</summary>
        void Upgrades()
        {
            if (S.Buildings.Any(b => b.UpgradeTo != null) || S.Sites.Count() >= MaxSites) return;
            if (!GrowthOk() && S.HousingCapacity >= S.Population) return;      // bigger homes mean more people
            foreach (var b in S.Buildings.Where(b => b.Active && b.Def.housing > 0).OrderBy(b => b.Def.homeWarmth).ThenBy(b => b.Id))
            {
                var t = S.UpgradeOption(b);
                if (t == null || !S.CanUpgrade(b)) continue;
                if (!t.cost.All(c => S.Stock(c.id) >= c.amount + LowStock(c.id) + 10f)) continue;
                if (S.Upgrade(b)) { Note($"upgrading {b.Def.id} {b.Id} to {t.id}"); return; }
            }
        }

        // ------------------------------------------------------------------ paths

        readonly HashSet<int> _pathed = new();

        /// <summary>
        /// Lays a path from each busy building's door towards the campfire, following the way people already walk
        /// (the flow field), stopping where it meets an existing path.
        /// </summary>
        void Paths()
        {
            var pathDef = D.Buildings.Find(b => b.path);
            if (pathDef == null || !S.IsUnlocked(pathDef) || (int)(S.DayFraction * 24f) % 6 != 0) return;
            foreach (var b in S.Buildings)
            {
                if (!b.Active || _pathed.Contains(b.Id) || b == S.TownCenter) continue;
                if (!(b.IsStorage || b.Def.maxWorkers >= 2 && !b.Def.crop || b.Def.housing > 0)) continue;
                if (math.distance(b.Center, S.TownCenter.Center) > 30) { _pathed.Add(b.Id); continue; }
                _pathed.Add(b.Id);
                int laid = LayPath(b.Door, S.TownCenter.Door, 40);
                if (laid > 0) { Note($"laid {laid} path tiles from {b.Def.id}"); return; }
            }
        }

        int LayPath(int from, int to, int max)
        {
            var cells = new List<int>();
            int slot = M.Flow.Resolve(to), cur = from;
            for (int k = 0; k < max && cur != to; k++)
            {
                if (M.Path[cur]) break;                      // joined the network
                cells.Add(cur);
                byte code = M.Flow.Fields[slot * M.Flow.Cells + cur];
                if (code == Firstlight.Core.FlowDir.None || code == Firstlight.Core.FlowDir.Target) break;
                var xy = M.XY(cur) + Firstlight.Core.FlowDir.Offset(code);
                cur = M.Cell(xy.x, xy.y);
            }
            int n = 0;
            foreach (int c in cells) if (S.PlacePath(M.XY(c))) n++;
            return n;
        }

        // ------------------------------------------------------------------ sites for deposits and wells

        /// <summary>A mine or pit on its deposit: the most deposit cells under the footprint, then the shortest walk.</summary>
        int2? FindDepositSite(BuildingDef d)
        {
            var kind = GameMap.DepositOf(d.onDeposit);
            if (!M.DepositVisible(kind)) return null;
            var tc = S.TownCenter.Center;
            int2? best = null; float bestScore = float.MinValue;
            for (int c = 0; c < M.W * M.H; c++)
            {
                if (M.Deposits[c] != kind) continue;
                var xy = M.XY(c);
                for (int oy = -d.sizeY + 1; oy <= 0; oy++)
                    for (int ox = -d.sizeX + 1; ox <= 0; ox++)
                    {
                        var o = new int2(xy.x + ox, xy.y + oy);
                        if (!S.CanPlace(d, o) || !ClearRing(d, o)) continue;
                        float score = S.DepositCells(d, o) * 6f - math.distance(Settlement.FootprintCenter(d, o), tc);
                        if (score > bestScore) { bestScore = score; best = o; }
                    }
            }
            return best;
        }

        /// <summary>A well among the homes that have no clean water (null if every home has water).</summary>
        int2? FindWellSite(BuildingDef d)
        {
            var dry = S.Buildings.Where(b => b.Active && b.Def.housing > 0 && b.Residents > 0 && !b.HasWater).ToList();
            if (dry.Count == 0) return null;
            // The dry home with the most dry neighbours within a well's reach.
            float reach = d.waterRadius * S.Effect("water.radius");
            var hub = dry.OrderByDescending(h => dry.Count(o => math.distance(o.Center, h.Center) <= reach * 0.8f)).ThenBy(h => h.Id).First();
            int2? best = null; float bestScore = float.MinValue;
            for (int dy = -8; dy <= 8; dy++)
                for (int dx = -8; dx <= 8; dx++)
                {
                    var o = new int2((int)hub.Center.x + dx, (int)hub.Center.y + dy);
                    if (!S.CanPlace(d, o) || !ClearRing(d, o)) continue;
                    var c = Settlement.FootprintCenter(d, o);
                    float score = dry.Count(h => math.distance(h.Center, c) <= reach) * 10f - math.length(new float2(dx, dy));
                    if (score > bestScore) { bestScore = score; best = o; }
                }
            return best;
        }
    }
}
