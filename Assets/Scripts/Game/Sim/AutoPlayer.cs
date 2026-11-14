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
    public sealed class AutoPlayer
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
            "pottery", "healing", "shamanism", "tribal_law", "animal_husbandry", "irrigation", "masonry", "weaving",
            "prospecting", "barter", "copper_working", "writing", "bronze_working",
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
            S.SetBuilders(math.clamp(S.Adults / 4, 2, 6));
            if (!Careless)
            {
                SetPriorities();
                Relocate();
            }
            BuildSomething();
            if (S.CanAdvanceAge())
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
            if (id == "firewood") return S.FuelNeedUntilSpring() + 40f;
            if (r.toolSpeed > 0) return 6f + S.Adults / 2f;
            if (id == "wood") return 100f;
            if (id == "stone") return 60f;
            if (id == "clothing") return S.Population * 0.5f + 4f;
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
            var shortMat = new HashSet<string>();
            foreach (var site in S.Sites)
                for (int i = 0; i < site.Def.cost.Length; i++)
                    if (site.Def.cost[i].amount - site.Delivered[i] - site.InTransitOf(i) > S.Stock(site.Def.cost[i].id) + 0.01f) shortMat.Add(site.Def.cost[i].id);
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
                bool Plenty(string id) => S.Stock(id) >= Enough(id) && !shortMat.Contains(id);
                bool pause = b.Def.outputs.Length > 0 && !food && b.Def.outputs.All(o => Plenty(o.id));
                // With several outputs, concentrate on the one that is short.
                if (b.Def.outputs.Length > 1)
                {
                    int focus = -1;
                    for (int i = 0; i < b.Def.outputs.Length && !pause; i++)
                        if (!Plenty(b.Def.outputs[i].id) && b.Def.outputs.Where((o, j) => j != i).All(o => Plenty(o.id))) focus = i;
                    if (b.Focus != focus) S.SetFocus(b, focus);
                }
                if (feedsOnFood) pause = fd < 6f || S.Season == 3;
                if (b.Paused != pause) S.SetPaused(b, pause);
                var p = Priority.Normal;
                if (food && hungry && !feedsOnFood) p = Priority.High;
                else if (b.Def.outputs.Any(o => shortMat.Contains(o.id))) p = Priority.High;
                else if (b.Def.outputs.Any(o => o.id == "firewood") && fuelShort) p = Priority.High;
                else if (b.Def.outputs.Any(o => D.Resource(o.id)?.toolSpeed > 0) && toolsShort) p = Priority.High;
                else if (b.Def.outputs.Any(o => S.Stock(o.id) < LowStock(o.id))) p = Priority.High;
                else if (b.Def.knowledgePerWorker > 0) p = fd < 1f && S.Season == 3 ? Priority.Low : fd < 4f ? Priority.Normal : Priority.High;
                else if (food && plenty) p = Priority.Low;
                if (b.Priority != p) S.SetPriority(b, p);
            }
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

        void BuildSomething()
        {
            int sites = S.Sites.Count();
            if (sites >= 2) return;
            int pop = S.Population;
            int housing = S.Buildings.Where(b => b.Alive).Sum(b => b.Def.housing);
            bool hidesForTent = Unlocked("hide_tent") && S.Stock("hides") >= 8;
            var wants = new List<string>();
            void Want(bool cond, string id) { if (cond && Unlocked(id)) wants.Add(id); }

            Want(Count("gatherers_camp") < 1, "gatherers_camp");
            Want(Count("woodcutter") < 1, "woodcutter");
            Want(Count("elders_fire") < 1, "elders_fire");
            Want(housing < pop + 2, hidesForTent ? "hide_tent" : "lean_to");
            Want(Count("stone_gatherer") < 1, "stone_gatherer");
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
            bool foodShort = !Careless && S.FoodDays < 5f && S.Season < 3;
            Want(Count("hunters_camp") < 2 && (pop >= 20 || foodShort && pop >= 14), "hunters_camp");
            Want(Count("gatherers_camp") < 3 && foodShort && pop >= 16, "gatherers_camp");
            Want(Count("fishing_spot") < 2 && foodShort && pop >= 18, "fishing_spot");
            Want(Count("elders_fire") < 2 && pop >= 16, "elders_fire");
            Want(Count("field") < 2 + pop / 15, "field");
            Want(Count("gatherers_camp") < 3 && pop >= 26, "gatherers_camp");
            Want(Count("woodcutter") < 3 && pop >= 28, "woodcutter");
            Want(Count("mining_pit") < 1 && pop >= 22, "mining_pit");
            Want(Count("stone_gatherer") < 2 && pop >= 30, "stone_gatherer");

            foreach (var id in wants)
            {
                var d = D.Building(id);
                bool affordable = d.cost.All(c => S.Stock(c.id) >= c.amount);
                if (!affordable && sites > 0) continue;
                var at = FindSite(d);
                if (at == null) continue;
                var b = S.Place(d, at.Value, out string why);
                Note(b != null ? $"placed {id} at {at.Value.x},{at.Value.y}" : $"could not place {id}: {why}");
                return;
            }
        }

        /// <summary>Best spot near the campfire: features nearby for producers, short walks, a free ring around it.</summary>
        int2? FindSite(BuildingDef d)
        {
            var tc = S.TownCenter.Center;
            int R = d.HasNearby ? 30 : 22;
            int2? best = null; float bestScore = float.MinValue;
            for (int dy = -R; dy <= R; dy++)
                for (int dx = -R; dx <= R; dx++)
                {
                    float dist = math.length(new float2(dx, dy));
                    if (dist > R) continue;
                    var origin = new int2((int)tc.x + dx - d.sizeX / 2, (int)tc.y + dy - d.sizeY / 2);
                    if (!S.CanPlace(d, origin) || !ClearRing(d, origin)) continue;
                    float score = -dist * (d.housing > 0 ? 1.5f : 1f);
                    if (d.HasNearby)
                    {
                        int n = M.CountFeature(d.nearby.feature, Settlement.FootprintCenter(d, origin), d.nearby.radius);
                        score += math.saturate(n / (float)math.max(1, d.nearby.full)) * 40f;
                    }
                    if (d.housing > 0 && dist > S.TownCenter.Def.warmthRadius - 1) score -= 25f;
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
