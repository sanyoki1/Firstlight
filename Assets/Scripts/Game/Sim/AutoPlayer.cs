using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Firstlight.Game
{
    /// <summary>
    /// A scripted but honest player: it reads the same state the HUD shows and only uses the public player
    /// commands (place, priorities, builders, research queue, age-up). Used by -autotest and, in the normal game,
    /// by F9 to watch it play. The build order and research plan are its strategy, not game rules.
    /// </summary>
    public sealed partial class AutoPlayer
    {
        readonly Settlement S;
        readonly GameData D;
        readonly GameMap M;
        public readonly List<string> Actions = new();

        static readonly string[] StonePlan =
        {
            "basic_tools", "storytelling", "spears", "fishing", "herbal_knowledge", "seed_gathering", "kinship",
            "farming", "tribes", "food_preservation", "hide_working", "tracking", "basic_mining", "flint_tools",
        };
        static readonly string[] AgriculturalPlan =
        {
            "pottery", "healing", "tribal_law", "shamanism", "irrigation", "prospecting", "barter", "copper_working",
            "writing", "masonry", "bronze_working", "animal_husbandry", "weaving",
        };

        /// <summary>
        /// A careless player builds the same things but never reacts: no priorities, pausing or focus, no extra food
        /// or firewood buildings when short, no moving worn-out producers. Used to check that neglect is punished.
        /// </summary>
        public bool Careless;

        public AutoPlayer(Settlement s) { S = s; D = s.Data; M = s.Map; }

        void Note(string text) => Actions.Add($"day {S.Day} {text}");

        /// <summary>One decision round; call it every game hour or so.</summary>
        public void Think()
        {
            if (S.NeedsCampfire)
            {
                var site = S.SuggestCampfireSite();
                var tc = S.Place(D.Buildings.First(b => b.townCenter), site, out string why);
                Note(tc != null ? $"campfire placed at {site.x},{site.y}" : $"campfire failed: {why}");
                return;
            }
            QueueResearch();
            S.SetBuilders(math.clamp(S.Adults / 4, 2, 6 + S.Adults / 20));
            if (!Careless)
            {
                SetPriorities();
                Relocate();
            }
            if (S.Age.order >= 1) Agricultural();
            BuildSomething();
            // A new age brings new needs: not on the edge of a hungry winter.
            if (S.CanAdvanceAge() && (Careless || S.FoodDays >= 3f && (S.Season <= 1 || S.FoodStock >= S.DailyFoodNeed * S.LeanDays() * 0.4f)))
            {
                string from = S.Age.name;
                if (S.AdvanceAge()) { Note($"advanced from the {from} to the {S.Age.name}"); QueueResearch(); }
            }
        }

        void QueueResearch()
        {
            if (S.ResearchQueue.Count >= 3) return;
            foreach (var id in StonePlan.Concat(AgriculturalPlan))
            {
                if (S.IsResearched(id) || S.ResearchQueue.Contains(id)) continue;
                if (S.QueueResearch(id)) Note($"queued research {id}");
                if (S.ResearchQueue.Count >= 3) return;
            }
        }

        int Count(string id) => S.CountBuildings(id, true);
        bool Unlocked(string id) { var d = D.Building(id); return d != null && S.IsUnlocked(d); }

        /// <summary>Stock above which the auto-player stops making a good (pausing the workplace frees its workers).</summary>
        float Enough(string id)
        {
            var r = D.Resource(id);
            if (r == null) return float.MaxValue;
            if (r.isFood) return float.MaxValue;
            if (id == "firewood") return S.FuelNeedUntilSpring() + 40f + FirewoodUsers() * 12f;
            if (r.toolSpeed > 0) return 6f + S.Adults / 2f;
            if (id == "wood") return 60f + S.Population * 0.4f;
            if (id == "stone") return 60f + S.Population * 0.3f;
            if (id == "clothing") return S.Population * 0.5f + 4f;
            if (id == "pottery") return Homes() + 8f;
            if (id == "copper") return 15f;
            return 40f;
        }

        /// <summary>
        /// Raises producers of whatever is short (food, firewood, tools, or a material a site is waiting for), pauses
        /// producers of goods in surplus and lowers food producers while the stores are comfortably full.
        /// </summary>
        void SetPriorities()
        {
            float fd = S.FoodDays;
            bool hungry = fd < 3f || (S.Season == 2 && S.FoodStock < S.DailyFoodNeed * 10f), plenty = fd > 20f && S.Season < 2;
            bool fuelShort = S.Stock("firewood") < math.max(10f, S.FuelNeedUntilSpring() * 0.8f);
            bool toolsShort = S.ToolStock < 3;
            // Sowing (spring) and harvest (autumn): a field's few days of work feed people for months, so they come first.
            bool cropRush = S.Buildings.Any(b => b.Active && b.Def.crop && S.Season != 1 && CropWorkLeft(b)) && (S.ToolStock > 0 || S.Citizens.Any(c => c.Tool >= 0 && c.Job != null && c.Job.Def.crop));
            var shortMat = new HashSet<string>();
            foreach (var site in S.Sites)
                for (int i = 0; i < site.BuildDef.cost.Length; i++)
                    if (site.BuildDef.cost[i].amount - site.Delivered[i] - site.InTransitOf(i) > S.Stock(site.BuildDef.cost[i].id) + 0.01f) shortMat.Add(site.BuildDef.cost[i].id);
            // Inputs a staffed workplace is waiting for count as short too (food inputs excepted).
            foreach (var b in S.Buildings)
                if (b.Active && !b.Paused && b.Workers > 0)
                    foreach (var inp in b.Def.inputs)
                        if (inp.ids.All(id => S.Stock(id) < 1f) && !inp.ids.Any(id => D.Resource(id)?.isFood == true)) shortMat.Add(inp.ids[0]);
            foreach (var b in S.Buildings)
            {
                if (!b.Active || !b.Def.IsWorkplace) continue;
                bool food = b.Def.outputs.Any(o => D.Resource(o.id)?.isFood == true);
                bool feedsOnFood = b.Def.inputs.Any(i => i.ids.Any(id => D.Resource(id)?.isFood == true));
                // Pause makers of goods we have plenty of; turning food into preserved food only pays with food to spare.
                bool Plenty(string id) => (id == "tools" ? S.ToolStock : S.Stock(id)) >= Enough(id) && !shortMat.Contains(id);
                bool pause = b.Def.outputs.Length > 0 && !food && b.Def.outputs.All(o => Plenty(o.id));
                // With several outputs, concentrate on the one that is short (a field's crop is chosen elsewhere).
                if (b.Def.outputs.Length > 1 && !b.Def.exclusiveOutputs)
                {
                    int focus = -1;
                    for (int i = 0; i < b.Def.outputs.Length && !pause; i++)
                        if (!Plenty(b.Def.outputs[i].id) && b.Def.outputs.Where((o, j) => j != i).All(o => Plenty(o.id))) focus = i;
                    // Before winter, firewood comes first unless wood itself is scarce.
                    int fw = Array.FindIndex(b.Def.outputs, o => o.id == "firewood");
                    if (fw >= 0 && (S.Season == 1 || S.Season == 2) && S.Stock("firewood") < S.FuelNeedUntilSpring() * 1.2f
                        && S.Stock("wood") >= LowStock("wood") && !shortMat.Contains("wood")) focus = fw;
                    // Short of firewood for the winter: that comes before any building.
                    if (fw >= 0 && (S.Season >= 2) && S.Stock("firewood") < S.FuelNeedUntilSpring() * 0.9f) focus = fw;
                    if (b.Focus != focus) S.SetFocus(b, focus);
                }
                if (feedsOnFood) pause = fd < 6f || S.Season == 3;
                // More than a year and a half of food in store: leave some fields fallow and free their farmers.
                if (b.Def.crop) pause = b.Paused ? fd > 30f : fd > 45f;
                // Don't eat into an input that a site is waiting for (the potter's clay while the smelter waits).
                if (!food && b.Def.outputs.All(o => D.Resource(o.id)?.category == "goods" && S.Stock(o.id) >= Enough(o.id) * 0.4f)
                    && b.Def.inputs.Any(i => i.ids.Any(shortMat.Contains))) pause = true;
                if (b.Paused != pause) S.SetPaused(b, pause);
                var p = Priority.Normal;
                if (b.Def.crop) p = b.Status == "Needs tools" && S.ToolStock < 1 ? Priority.Low : CropWorkLeft(b) && S.Season != 1 ? Priority.High : Priority.Normal;
                else if (cropRush && (food || b.Def.knowledgePerWorker > 0) && !(food && fd < 1.5f)) p = Priority.Normal;   // sowing and harvest come first
                else if (food && hungry && !feedsOnFood) p = Priority.High;
                else if (b.Def.outputs.Any(o => shortMat.Contains(o.id))) p = Priority.High;
                else if (b.Def.outputs.Any(o => o.id == "firewood") && fuelShort) p = Priority.High;
                else if (b.Def.outputs.Any(o => D.Resource(o.id)?.toolSpeed > 0) && toolsShort) p = Priority.High;
                else if (b.Def.outputs.Any(o => S.Stock(o.id) < LowStock(o.id))) p = Priority.High;
                // Services the town is short of now: the council when unrest builds, the shaman while faith is unmet.
                else if (b.Def.orderPower > 0 && S.Unrest >= 0.12f) p = Priority.High;
                else if (b.Def.faithRadius > 0 && S.NeedActive("faith") && S.NeedMet("faith") < 0.8f) p = Priority.High;
                else if (b.Def.healthRadius > 0 && S.OutbreakRisk >= D.Balance.outbreakWarn) p = Priority.High;
                else if (b.Def.knowledgePerWorker > 0) p = fd < 1f && S.Season == 3 ? Priority.Low : fd < 3f ? Priority.Normal : Priority.High;
                else if (food && plenty) p = Priority.Low;
                if (b.Priority != p) S.SetPriority(b, p);
            }
        }

        bool CropWorkLeft(Building b) => S.Season == 0 || S.Season == 1 ? b.Sown < 0.999f || S.Season == 1 : S.Season == 2 && b.Harvested < 0.999f && b.Growth > 0.01f;

        /// <summary>Rough food made per day by the food workplaces (full crews, this season, about 60% of nominal after walking).</summary>
        float FoodRate()
        {
            float f = 0;
            foreach (var b in S.Buildings)
                if (b.Alive && !b.Def.crop && b.Def.inputs.Length == 0)
                    foreach (var o in b.Def.outputs)
                        if (D.Resource(o.id)?.isFood == true)
                            f += o.amount * b.Def.maxWorkers * (b.Def.SeasonYield(0) + b.Def.SeasonYield(1) + b.Def.SeasonYield(2) + b.Def.SeasonYield(3)) * 0.25f * (b.Active ? b.Fullness : 0.8f) * 0.6f;
            return f;
        }

        /// <summary>
        /// More homes (so more people) only while the food keeps up: fields to match, and the stores holding enough for
        /// the lean months. The auto-player also stops growing the town at about 150 (its plan, not a game rule).
        /// </summary>
        bool GrowthOk()
        {
            if (Careless) return true;
            if (S.Population >= 150) return false;
            if (S.Age.order < 1) return true;
            bool fields = Count("field") >= FieldsWanted(S.Population) - 1;
            bool stores = S.FoodStock >= S.DailyFoodNeed * S.LeanDays() * 0.45f;
            return fields && stores;
        }

        /// <summary>
        /// A site waiting for a material nobody makes (no stone gatherer while the Elders' Fire wants stone) would
        /// block building for good: place a maker of it, even over the site limit.
        /// </summary>
        bool UnblockSites()
        {
            foreach (var site in S.Sites)
            {
                var bd = site.BuildDef;
                for (int i = 0; i < bd.cost.Length; i++)
                {
                    string id = bd.cost[i].id;
                    if (site.Delivered[i] + site.InTransitOf(i) >= bd.cost[i].amount - 0.01f || S.Stock(id) >= 1f) continue;
                    if (S.Buildings.Any(b => b.Alive && b.Def.outputs.Any(o => o.id == id))) continue;
                    var maker = D.Buildings.Where(d => S.IsUnlocked(d) && d.outputs.Any(o => o.id == id) && !d.crop)
                        .OrderBy(d => d.inputs.Length).ThenBy(d => d.cost.Sum(c => c.amount)).FirstOrDefault();
                    if (maker == null) continue;
                    var at = !string.IsNullOrEmpty(maker.onDeposit) ? FindDepositSite(maker) : FindSite(maker);
                    if (at == null) continue;
                    if (S.Place(maker, at.Value) != null) { Note($"placed {maker.id} at {at.Value.x},{at.Value.y}: {bd.id} needs {id} and nobody makes it"); return true; }
                }
            }
            return false;
        }

        float LowStock(string id) => id == "wood" ? 15f : id == "stone" ? 10f : 0f;

        readonly Dictionary<Building, Building> _replacing = new();   // new site -> the worn-out building it replaces

        /// <summary>
        /// Producers whose nearby trees, rock or game are nearly used up get a replacement on fresh ground; the old
        /// one is pulled down once the new one works.
        /// </summary>
        void Relocate()
        {
            foreach (var kv in _replacing.ToList())
            {
                if (!kv.Key.Alive) { _replacing.Remove(kv.Key); continue; }
                if (!kv.Key.Active) continue;
                if (kv.Value.Alive) { S.Demolish(kv.Value); Note($"removed worn-out {kv.Value.Def.id}"); }
                _replacing.Remove(kv.Key);
            }
            if (S.Sites.Count() >= 2) return;
            foreach (var b in S.Buildings)
            {
                if (!b.Active || !b.Def.HasNearby || b.Def.nearby.feature == "water" || b.Fullness >= 0.3f || _replacing.ContainsValue(b)) continue;
                var at = FindSite(b.Def);
                // The woods near town are gone: look farther out for fresh ones.
                if (at != null && math.saturate(M.CountFeature(b.Def.nearby.feature, Settlement.FootprintCenter(b.Def, at.Value), b.Def.nearby.radius) / (float)math.max(1, b.Def.nearby.full)) < 0.6f)
                    at = FindSite(b.Def, 18);
                if (at == null) continue;
                var center = Settlement.FootprintCenter(b.Def, at.Value);
                float fresh = math.saturate(M.CountFeature(b.Def.nearby.feature, center, b.Def.nearby.radius) / (float)math.max(1, b.Def.nearby.full));
                if (fresh < 0.6f) continue;
                var nb = S.Place(b.Def, at.Value);
                if (nb == null) continue;
                _replacing[nb] = b;
                Note($"placed {b.Def.id} at {at.Value.x},{at.Value.y} to replace a worn-out one (fullness {b.Fullness:F2})");
                return;
            }
        }

        int MaxSites => 2 + S.Adults / 20;

        void BuildSomething()
        {
            int sites = S.Sites.Count();
            if (!Careless && UnblockSites()) return;
            if (sites >= MaxSites) return;
            int pop = S.Population;
            int housing = S.Buildings.Where(b => b.Alive).Sum(b => b.Def.housing);
            bool hidesForTent = Unlocked("hide_tent") && S.Stock("hides") >= 8;
            var wants = new List<string>();
            void Want(bool cond, string id) { if (cond && Unlocked(id)) wants.Add(id); }

            Want(Count("gatherers_camp") < 1, "gatherers_camp");
            Want(Count("woodcutter") < 1, "woodcutter");
            // Nobody sleeps out in the cold: from summer on, roofs for everyone come before anything else.
            bool homeEarly = S.Season >= 1 && housing < pop;
            Want(homeEarly, D.Building("hide_tent") != null && Unlocked("hide_tent") && S.Stock("hides") >= 8 ? "hide_tent" : S.Age.order >= 1 && Unlocked("thatched_hut") ? "thatched_hut" : "lean_to");
            // Somewhere to put things before the campfire's small store fills up.
            Want(Count("storage_pile") < 1 && S.StorageUsed > S.StorageCapacity * 0.8f, "storage_pile");
            // A first camp in thin woods can't feed the band: a second one before anything else.
            Want(!Careless && Count("gatherers_camp") < 3 && Count("hunters_camp") == 0 && S.FoodDays < 10f && FoodRate() < S.DailyFoodNeed * 1.15f, "gatherers_camp");
            Want(Count("elders_fire") < 1, "elders_fire");
            bool agri = S.Age.order >= 1;
            string home = agri && Unlocked("thatched_hut") ? "thatched_hut" : hidesForTent ? "hide_tent" : "lean_to";
            Want(housing < pop + 4 && GrowthOk(), home);
            if (agri) Want(Count("granary") < 1, "granary");
            if (agri) Want(Count("field") < 2, "field");
            Want(Count("stone_gatherer") < 1, "stone_gatherer");
            // Idle hands and nowhere to put them: another woodcutter while wood is what holds things up, else food.
            if (!Careless)
            {
                int idle = S.Citizens.Count(c => !c.Gone && c.Age >= D.Balance.adultAge && c.Job == null && !c.IsBuilder && !c.Sick);
                int open = S.Buildings.Where(b => b.Active && !b.Paused && b.Def.IsWorkplace && !b.Def.crop).Sum(b => b.Def.maxWorkers - b.Workers);
                if (idle >= 3 && open < idle)
                {
                    bool woodShort = S.Stock("wood") < 25f;
                    Want(woodShort && Count("woodcutter") < 3 + pop / 25, "woodcutter");
                    Want(Count("gatherers_camp") < 2 + pop / 12, "gatherers_camp");
                    Want(Count("hunters_camp") < 1 + pop / 15, "hunters_camp");
                }
            }
            Want(Count("storage_pile") < 1 || (S.StorageUsed > S.StorageCapacity * 0.7f && S.Sites.All(b => !b.IsStorage)), "storage_pile");
            Want(Count("gatherers_camp") < 2 && (pop >= 12 || !Careless && S.FoodDays < 8f), "gatherers_camp");
            Want(Count("toolmaker") < 1, "toolmaker");
            Want(!Careless && Count("woodcutter") < 2 && S.Season >= 1 && S.Stock("firewood") < S.FuelNeedUntilSpring(), "woodcutter");
            Want(Count("hunters_camp") < 1, "hunters_camp");
            Want(Count("herbalist") < 1, "herbalist");
            Want(Count("drying_rack") < 1 && Count("hunters_camp") > 0, "drying_rack");
            Want(Count("woodcutter") < 2 && pop >= 15, "woodcutter");
            Want(Count("fishing_spot") < 1, "fishing_spot");
            Want(Count("hide_worker") < 1 && Count("hunters_camp") > 0, "hide_worker");
            // Short of food now, or (in summer and autumn) heading into winter without a winter's worth.
            bool foodShort = !Careless && (S.FoodDays < 5f && S.Season < 3
                || (S.Season == 1 || S.Season == 2) && S.FoodStock < S.DailyFoodNeed * D.Balance.daysPerSeason * 1.2f && S.FoodDays < 10f);
            Want(Count("hunters_camp") < 2 && (pop >= 20 || foodShort && pop >= 14), "hunters_camp");
            Want(Count("gatherers_camp") < 3 && foodShort && pop >= 16, "gatherers_camp");
            // Keep food work ahead of the mouths to feed: about one food workplace per six people, one more while the
            // stores won't carry the settlement to the next harvest.
            int foodPlaces = S.Buildings.Count(b => b.Alive && b.Def.outputs.Any(o => D.Resource(o.id)?.isFood == true) && b.Def.inputs.Length == 0 && !b.Def.crop);
            bool behind = !Careless && S.FoodStock < S.DailyFoodNeed * S.LeanDays() * 0.5f && S.Season != 3;
            if (foodPlaces < 1 + pop / 6 + (behind ? 1 : 0) && (!agri || foodPlaces < 3 + pop / 12))
            {
                Want(Count("hunters_camp") < 1 + pop / 20, "hunters_camp");
                Want(Count("fishing_spot") < 1 + pop / 25, "fishing_spot");
                Want(true, "gatherers_camp");
            }
            Want(Count("fishing_spot") < 2 && foodShort && pop >= 18, "fishing_spot");
            Want(Count("elders_fire") < 2 && pop >= 20, "elders_fire");
            if (agri) AgriculturalWants(Want, pop, housing, home);
            else Want(Count("field") < 2 + pop / 15, "field");
            Want(Count("gatherers_camp") < 3 && pop >= 26, "gatherers_camp");
            Want(Count("woodcutter") < 3 && pop >= 28, "woodcutter");
            Want(Count("mining_pit") < 1 && pop >= 22, "mining_pit");
            Want(Count("stone_gatherer") < 2 && pop >= 30, "stone_gatherer");

            foreach (var id in wants)
            {
                var d = D.Building(id);
                bool affordable = d.cost.All(c => S.Stock(c.id) >= c.amount);
                // Milestone buildings go down even before their materials are in (the site raises their producers).
                bool milestone = S.Milestones.Any(m => !m.Done && m.Def.type == "building" && m.Def.target == id);
                if (!affordable && sites > 0 && !milestone) continue;
                var at = !string.IsNullOrEmpty(d.onDeposit) ? FindDepositSite(d) : d.waterRadius > 0 ? FindWellSite(d) : FindSite(d);
                if (at == null) continue;
                var b = S.Place(d, at.Value, out string why);
                Note(b != null ? $"placed {id} at {at.Value.x},{at.Value.y}" : $"could not place {id}: {why}");
                return;
            }
        }

        /// <summary>Best spot near the campfire: features nearby for producers, short walks, a free ring around it.</summary>
        public int2? FindSite(BuildingDef d) => FindSite(d, 0);

        /// <summary>Same, searching <paramref name="extra"/> cells farther out (fresh woods once the near ones are cut).</summary>
        public int2? FindSite(BuildingDef d, int extra)
        {
            var tc = S.TownCenter.Center;
            int R = (d.crop ? 30 : d.HasNearby ? 30 : 22 + S.Population / 12) + extra;
            int2? best = null; float bestScore = float.MinValue;
            for (int dy = -R; dy <= R; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    float dist = math.length(new float2(dx, dy));
                    if (dist > R) continue;
                    var origin = new int2((int)tc.x + dx - d.sizeX / 2, (int)tc.y + dy - d.sizeY / 2);
                    if (!S.CanPlace(d, origin) || !ClearRing(d, origin)) continue;
                    float score = -dist * (d.housing > 0 ? 1.5f : 1f);
                    if (d.crop)
                    {
                        // Fields: open ground (each tree in the footprint is lost woodland), by the river for irrigation.
                        var fc = Settlement.FootprintCenter(d, origin);
                        score -= M.CountFeature("trees", fc, 3) * 2.5f;
                        if (d.irrigationRadius > 0 && M.CountFeature("water", fc, d.irrigationRadius) > 0) score += 18f;
                        score += dist < 7 ? -20f : 0f;          // leave the middle for homes and work
                    }
                    if (d.HasNearby)
                    {
                        int n = M.CountFeature(d.nearby.feature, Settlement.FootprintCenter(d, origin), d.nearby.radius);
                        score += math.saturate(n / (float)math.max(1, d.nearby.full)) * 40f;
                    }
                    if (d.housing > 0 && d.homeWarmth < 0.6f && dist > S.TownCenter.Def.warmthRadius - 1) score -= 25f;
                    // Woodcutters fell the trees gatherers live off, and producers sharing a feature split it: keep them apart.
                    var here = Settlement.FootprintCenter(d, origin);
                    foreach (var o in S.Buildings)
                    {
                        if (!o.Alive || !d.HasNearby || !o.Def.HasNearby || o.Def.nearby.feature != d.nearby.feature) continue;
                        bool clash = (d.consumesFeaturePer > 0) != (o.Def.consumesFeaturePer > 0);
                        float gap = math.distance(o.Center, here), reach = d.nearby.radius + o.Def.nearby.radius;
                        if (gap < reach) score -= (clash ? 40f : 15f) * (1f - gap / reach);
                    }
                    if (score > bestScore) { bestScore = score; best = origin; }
                }
            return best;
        }

        /// <summary>Keeps one free cell around every building so paths never get walled off.</summary>
        bool ClearRing(BuildingDef d, int2 o)
        {
            for (int y = o.y - 1; y <= o.y + d.sizeY; y++)
                for (int x = o.x - 1; x <= o.x + d.sizeX; x++)
                {
                    if (!M.InBounds(x, y)) return false;
                    int c = M.Cell(x, y);
                    if (M.BuildingAt[c] >= 0) return false;
                    bool inside = x >= o.x && x < o.x + d.sizeX && y >= o.y && y < o.y + d.sizeY;
                    if (!inside && M.Reserved[c] && y != o.y - 1) return false;
                }
            return true;
        }
    }
}
