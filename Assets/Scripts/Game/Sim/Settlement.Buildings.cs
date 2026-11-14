using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Placement, construction sites, storage and production.
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ placement

        /// <summary>The walkable cell in front of a footprint (below its middle), where people go in and out.</summary>
        public static int2 DoorCell(BuildingDef d, int2 origin) => new int2(origin.x + d.sizeX / 2, origin.y - 1);
        public static float2 FootprintCenter(BuildingDef d, int2 origin) => new float2(origin.x + d.sizeX * 0.5f, origin.y + d.sizeY * 0.5f);

        /// <summary>
        /// Whether a building can go with its lower-left corner on <paramref name="origin"/>: footprint on dry free
        /// ground, a walkable door cell, research and age reached, nearby features present, unique respected.
        /// The first placement must be the town center.
        /// </summary>
        public bool CanPlace(BuildingDef d, int2 origin, out string reason)
        {
            reason = "";
            if (d == null) { reason = "Nothing selected"; return false; }
            if (NeedsCampfire && !d.townCenter) { reason = "Place the campfire first"; return false; }
            if (!NeedsCampfire && d.townCenter) { reason = "There is already a campfire"; return false; }
            if (!d.townCenter && !IsUnlocked(d))
            {
                reason = !string.IsNullOrEmpty(d.unlockedBy) && !IsResearched(d.unlockedBy)
                    ? $"Needs research: {Data.ResearchById(d.unlockedBy)?.name ?? d.unlockedBy}"
                    : $"Needs the {Data.Age(d.age)?.name ?? d.age}";
                return false;
            }
            if (d.unique && CountBuildings(d.id, true) > 0) { reason = "Only one can be built"; return false; }
            for (int y = origin.y; y < origin.y + d.sizeY; y++)
                for (int x = origin.x; x < origin.x + d.sizeX; x++)
                {
                    if (x < 1 || y < 1 || x >= Map.W - 1 || y >= Map.H - 1) { reason = "Too close to the map edge"; return false; }
                    int c = Map.Cell(x, y);
                    if (Map.Ground[c] != Ground.Grass || Map.WaterClass[c] > 0) { reason = "Can't build in water"; return false; }
                    if (Map.BuildingAt[c] >= 0) { reason = "Something is already there"; return false; }
                    if (Map.Reserved[c]) { reason = "Blocks another building's entrance"; return false; }
                    if (Map.Rock[c] > 0) { reason = "Rock in the way"; return false; }
                }
            var door = DoorCell(d, origin);
            if (!Map.InBounds(door.x, door.y)) { reason = "No room for an entrance"; return false; }
            int dc = Map.Cell(door.x, door.y);
            if (Map.WaterClass[dc] >= 2 || Map.BuildingAt[dc] >= 0 || Map.Rock[dc] > 0) { reason = "The entrance is blocked"; return false; }
            if (d.HasNearby && d.nearby.min > 0)
            {
                int n = Map.CountFeature(d.nearby.feature, FootprintCenter(d, origin), d.nearby.radius);
                if (n < d.nearby.min) { reason = $"Needs {d.nearby.feature} nearby ({n} of {d.nearby.min})"; return false; }
            }
            return true;
        }
        public bool CanPlace(BuildingDef d, int2 origin) => CanPlace(d, origin, out _);

        /// <summary>Places a construction site (or, for the campfire, the finished town center and the starting band).</summary>
        public Building Place(BuildingDef d, int2 origin) => Place(d, origin, out _);

        public Building Place(BuildingDef d, int2 origin, out string reason)
        {
            if (!CanPlace(d, origin, out reason)) return null;
            var b = new Building
            {
                Id = Buildings.Count, Def = d, Origin = origin, State = BuildState.Site,
                Delivered = new float[d.cost.Length], InTransit = new float[d.cost.Length],
                Out = new float[ResCount], In = new float[ResCount],
                Center = FootprintCenter(d, origin),
                CostRes = Array.ConvertAll(d.cost, a => ResIndex(a.id)),
                OutRes = Array.ConvertAll(d.outputs, a => ResIndex(a.id)),
                InRes = Array.ConvertAll(d.inputs, i => Array.ConvertAll(i.ids, ResIndex)),
                YieldKey = "yield." + d.yieldTag,
            };
            var door = DoorCell(d, origin);
            b.Door = Map.Cell(door.x, door.y);
            b.DoorPos = new float2(door.x + 0.5f, door.y + 0.5f);
            Buildings.Add(b);
            for (int y = origin.y; y < origin.y + d.sizeY; y++)
                for (int x = origin.x; x < origin.x + d.sizeX; x++)
                {
                    int c = Map.Cell(x, y);
                    Map.RemoveTree(c);
                    Map.BuildingAt[c] = b.Id;
                    Map.RefreshCost(c);
                }
            Map.Reserved[b.Door] = true;
            Map.CommitCostChanges();
            if (d.townCenter)
            {
                TownCenter = b;
                Complete(b);
                FoundSettlement(b);
            }
            else if (d.cost.Length == 0 && d.buildWork <= 0) Complete(b);
            _jobsDirty = true;
            return b;
        }

        /// <summary>
        /// Pulls a building down. Sites give back what was delivered; finished buildings a share of their cost.
        /// Stored goods move to other stores where there is room. The campfire can't be removed.
        /// </summary>
        public bool Demolish(Building b, out string reason)
        {
            reason = "";
            if (b == null || !b.Alive) { reason = "Nothing to remove"; return false; }
            if (b == TownCenter) { reason = "The campfire can't be removed"; return false; }
            var refund = new float[ResCount];
            for (int i = 0; i < b.Def.cost.Length; i++)
                refund[b.CostRes[i]] += b.IsSite ? b.Delivered[i] : b.Def.cost[i].amount * B.demolishRefund;
            for (int r = 0; r < ResCount; r++) refund[r] += b.Out[r] + b.In[r];
            if (b.Store != null) for (int r = 0; r < ResCount; r++) refund[r] += b.Store[r];
            b.State = BuildState.Removed;
            for (int r = 0; r < ResCount; r++) if (refund[r] > 0.01f) Deposit(r, refund[r], b.Center);
            foreach (var c in Citizens)
            {
                if (c.Home == b) c.Home = null;
                if (c.Job == b) c.Job = null;
            }
            for (int y = b.Origin.y; y < b.Origin.y + b.Def.sizeY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.Def.sizeX; x++)
                {
                    int c = Map.Cell(x, y);
                    Map.BuildingAt[c] = -1;
                    Map.RefreshCost(c);
                }
            bool doorShared = false;
            foreach (var o in Buildings) if (o.Alive && o.Door == b.Door) doorShared = true;
            if (!doorShared) Map.Reserved[b.Door] = false;
            Map.CommitCostChanges();
            Log(Severity.Info, $"{b.Def.name} removed.");
            _jobsDirty = true;
            RefreshStock();
            return true;
        }
        public bool Demolish(Building b) => Demolish(b, out _);

        /// <summary>
        /// A good campfire origin for a default/suggestion: open buildable ground with forest, loose rock, the river
        /// and game within reach (the HUD can offer it; the auto-player uses it).
        /// </summary>
        public int2 SuggestCampfireSite()
        {
            var d = Data.Buildings.Find(b => b.townCenter);
            int2 best = new int2(Map.W / 2, Map.H / 2); float bestScore = float.MinValue;
            for (int y = 16; y < Map.H - 16; y += 2)
                for (int x = 16; x < Map.W - 16; x += 2)
                {
                    var o = new int2(x, y);
                    if (!CanPlace(d, o)) continue;
                    var p = FootprintCenter(d, o);
                    int near = Map.CountFeature("trees", p, 4);
                    float score = math.min(Map.CountFeature("trees", p, 11), 45) * 0.5f
                                + math.min(Map.CountFeature("rocks", p, 13), 8) * 2f
                                + math.min(Map.CountFeature("water", p, 14), 12) * 0.8f
                                + math.min(Map.CountFeature("wildlife", p, 18), 14) * 0.6f
                                - near * 1.5f;
                    // Room to build: dry, clear cells close by.
                    int open = 0;
                    for (int dy = -7; dy <= 7; dy++)
                        for (int dx = -7; dx <= 7; dx++)
                        {
                            int cx = (int)p.x + dx, cy = (int)p.y + dy;
                            if (!Map.InBounds(cx, cy)) continue;
                            int c = Map.Cell(cx, cy);
                            if (Map.Ground[c] == Ground.Grass && Map.WaterClass[c] == 0 && !Map.Tree[c] && Map.Rock[c] <= 0) open++;
                        }
                    score += math.min(open, 150) * 0.15f;
                    if (score > bestScore) { bestScore = score; best = o; }
                }
            return best;
        }

        public Building BuildingAtCell(int2 cell)
        {
            if (!Map.InBounds(cell.x, cell.y)) return null;
            int id = Map.BuildingAt[Map.Cell(cell.x, cell.y)];
            return id >= 0 ? Buildings[id] : null;
        }

        void Complete(Building b)
        {
            b.State = BuildState.Active;
            b.Work = 1f;
            if (b.IsStorage && b.Store == null) b.Store = new float[ResCount];
            RefreshFullness(b);
            if (!b.Def.townCenter) Log(Severity.Info, $"{b.Def.name} finished.");
            _jobsDirty = true;
        }

        /// <summary>The campfire is lit: the starting band and their goods appear around it.</summary>
        void FoundSettlement(Building tc)
        {
            foreach (var a in B.startResources)
            {
                int r = ResIndex(a.id);
                if (r >= 0) { tc.Store[r] += a.amount; tc.Stored += a.amount; }
            }
            for (int i = 0; i < B.startPeople; i++)
            {
                bool child = i < B.startChildren, elder = !child && i < B.startChildren + B.startElders;
                float age = child ? R(4f, 11f) : elder ? R(B.elderAge, B.elderAge + 6f) : R(B.fertileMin, B.fertileMax - 6f);
                float a = i * math.PI * 2f / B.startPeople;
                var pos = tc.Center + new float2(math.sin(a), math.cos(a)) * R(2.2f, 3.2f);
                var c = NewCitizen(i % 2 == 0, age, pos);
                // The band brings no tools (Basic Tools is the first research) and no clothing.
                c.BirthCooldown = R(0f, B.birthCooldownDays);
            }
            Log(Severity.Info, $"The band lights its campfire: {Population} people.");
            RefreshStock();
            AssignHomes();
            AssignJobs();
            EvaluateMilestones();
            RefreshWarnings();
        }

        // ------------------------------------------------------------------ storage

        /// <summary>Recomputes stock totals from every store (the incremental totals are kept in step anyway).</summary>
        void RefreshStock()
        {
            Array.Clear(StockTotals, 0, ResCount);
            foreach (var b in Buildings)
            {
                if (!b.Active || b.Store == null) continue;
                float sum = 0;
                for (int r = 0; r < ResCount; r++) { StockTotals[r] += b.Store[r]; sum += b.Store[r]; }
                b.Stored = sum;
            }
        }

        float Free(Building s) => s.Def.storageCapacity - s.Stored;

        float PutInto(Building s, int r, float amt)
        {
            float put = math.min(amt, math.max(0, Free(s)));
            if (put <= 0) return 0;
            s.Store[r] += put; s.Stored += put; StockTotals[r] += put;
            return put;
        }

        float TakeFrom(Building s, int r, float amt)
        {
            float take = math.min(amt, s.Store[r]);
            if (take <= 0) return 0;
            s.Store[r] -= take; s.Stored -= take; StockTotals[r] -= take;
            if (s.Store[r] < 1e-4f) { s.Stored -= s.Store[r]; StockTotals[r] -= s.Store[r]; s.Store[r] = 0; }
            return take;
        }

        /// <summary>Takes up to amt of a resource from any stores (nearest the town center first). Used for eating and burning.</summary>
        float Withdraw(int r, float amt)
        {
            float got = 0;
            foreach (var s in Buildings)
            {
                if (got >= amt) break;
                if (s.Active && s.Store != null && s.Store[r] > 0) got += TakeFrom(s, r, amt - got);
            }
            Consumed[r] += got;
            return got;
        }

        /// <summary>Puts goods into the nearest stores with room; whatever does not fit is lost.</summary>
        float Deposit(int r, float amt, float2 near)
        {
            float left = amt;
            for (int k = 0; k < 8 && left > 0.001f; k++)
            {
                var s = NearestStorageWithSpace(near, 0.01f);
                if (s == null) break;
                left -= PutInto(s, r, left);
            }
            return amt - left;
        }

        Building NearestStorageWithSpace(float2 from, float min)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || Free(s) < min) continue;
                float d = math.distancesq(s.DoorPos, from);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        Building NearestStorageWith(float2 from, int r, float min = 0.01f)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Store[r] < min) continue;
                float d = math.distancesq(s.DoorPos, from);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        void DailySpoilage()
        {
            float foodMult = Effect("spoil.food");
            foreach (var b in Buildings)
            {
                if (!b.Alive) continue;
                for (int r = 0; r < ResCount; r++)
                {
                    var rd = Data.Resources[r];
                    if (rd.spoilPerDay <= 0) continue;
                    float rate = rd.spoilPerDay * (rd.isFood ? foodMult : 1f);
                    if (b.Store != null && b.Store[r] > 0)
                    {
                        float loss = b.Store[r] * math.saturate(rate * b.Def.spoilMultiplier);
                        b.Store[r] -= loss; b.Stored -= loss;
                    }
                    if (b.Out[r] > 0) { float loss = b.Out[r] * math.saturate(rate); b.Out[r] -= loss; b.OutTotal -= loss; }
                    if (b.In[r] > 0) b.In[r] -= b.In[r] * math.saturate(rate);
                }
            }
        }

        // ------------------------------------------------------------------ production

        /// <summary>Most finished goods a workplace holds before work stops (waiting to be carried off).</summary>
        float OutCap(Building b) => B.carryCapacity * math.max(2, b.Def.maxWorkers) * 2f;

        void RefreshFullness()
        {
            foreach (var b in Buildings) if (b.Active && b.Def.HasNearby) RefreshFullness(b);
        }

        void RefreshFullness(Building b)
        {
            if (!b.Def.HasNearby) { b.Fullness = 1f; return; }
            var n = b.Def.nearby;
            int count = Map.CountFeature(n.feature, b.Center, n.radius);
            b.FeatureLeft = count;
            b.Fullness = n.full <= 0 ? (count > 0 ? 1f : 0f) : math.saturate(count / (float)n.full);
        }

        /// <summary>
        /// One sim step of one worker at its workplace. Output per worker per (full) workday is the def's amount
        /// times season yield, nearby fullness, research yield effects and the worker's speed (tool, happiness).
        /// Returns false if nothing could be made (no inputs, nothing nearby, out of season, workplace full).
        /// </summary>
        bool WorkStep(Citizen c, Building b, float speed)
        {
            var d = b.Def;
            float frac = Step / WorkdaySeconds * speed;
            bool did = false;
            if (d.knowledgePerWorker > 0)
            {
                float mult = c.Age >= B.elderAge ? B.elderKnowledgeMult : 1f;
                AddKnowledge(d.knowledgePerWorker * frac * mult * Effect(b.YieldKey));
                did = true;
            }
            if (d.outputs.Length == 0) return did;
            if (b.OutTotal >= OutCap(b)) { b.Status = "Waiting for goods to be carried off"; return did; }
            float yield = d.SeasonYield(Season) * b.Fullness * (string.IsNullOrEmpty(d.yieldTag) ? 1f : Effect(b.YieldKey));
            if (yield <= 0)
            {
                b.Status = d.SeasonYield(Season) <= 0 ? "Nothing to do this season" : $"No {d.nearby?.feature} left nearby";
                return did;
            }
            float work = frac * yield;
            // Inputs: each input group can be met by any of its resources, in order.
            float scale = 1f;
            for (int i = 0; i < d.inputs.Length; i++)
            {
                float have = 0;
                foreach (int r in b.InRes[i]) have += b.In[r];
                scale = math.min(scale, have / (d.inputs[i].amount * work));
            }
            if (scale <= 0) { b.Status = "Waiting for inputs"; return did; }
            scale = math.min(1f, scale);
            for (int i = 0; i < d.inputs.Length; i++)
            {
                float need = d.inputs[i].amount * work * scale;
                foreach (int r in b.InRes[i])
                {
                    float t = math.min(need, b.In[r]);
                    b.In[r] -= t; need -= t;
                    if (need <= 0) break;
                }
            }
            // A focused workplace makes only one of its outputs, at a share of the combined amount.
            float total = 0;
            if (b.Focus >= 0) foreach (var o in d.outputs) total += o.amount;
            for (int i = 0; i < d.outputs.Length; i++)
            {
                float per = b.Focus < 0 ? d.outputs[i].amount : i == b.Focus ? total * B.focusShare : 0f;
                float amt = per * work * scale;
                if (amt <= 0) continue;
                b.Out[b.OutRes[i]] += amt;
                b.OutTotal += amt;
                Produced[b.OutRes[i]] += amt;
            }
            if (d.consumesFeaturePer > 0)
            {
                // The feature is used up by the work done, whatever the output mix.
                b.FeatureAcc += d.outputs[0].amount * work * scale;
                while (b.FeatureAcc >= d.consumesFeaturePer) { b.FeatureAcc -= d.consumesFeaturePer; ConsumeFeature(b); }
            }
            b.Status = "";
            return true;
        }

        /// <summary>Workers use up what they work: one tree felled, one stone off a rock, one animal from a herd.</summary>
        void ConsumeFeature(Building b)
        {
            var n = b.Def.nearby;
            if (n == null) return;
            switch (n.feature)
            {
                case "trees":
                {
                    int c = Map.NearestFeatureCell("trees", b.Center, n.radius);
                    if (c >= 0) Map.RemoveTree(c);
                    break;
                }
                case "rocks":
                {
                    int c = Map.NearestFeatureCell("rocks", b.Center, n.radius);
                    if (c >= 0) Map.TakeStone(c, 1f);
                    break;
                }
                case "wildlife":
                {
                    Herd best = null; float bd = float.MaxValue;
                    foreach (var h in Map.Herds)
                    {
                        float d = math.distance(h.Pos, b.Center);
                        if (h.Pop >= 1f && d <= n.radius && d < bd) { bd = d; best = h; }
                    }
                    if (best != null) best.Pop = math.max(0f, best.Pop - 1f);
                    break;
                }
            }
            RefreshFullness(b);
        }

        /// <summary>Which input a workplace is shortest of, for its workers to fetch (-1 = enough for now).</summary>
        int InputToFetch(Building b, out float amount)
        {
            amount = 0;
            var d = b.Def;
            for (int i = 0; i < d.inputs.Length; i++)
            {
                float have = 0;
                foreach (int r in b.InRes[i]) have += b.In[r];
                float want = d.inputs[i].amount * math.max(1, b.Workers);   // a day's worth for the crew
                if (have >= want * 0.35f) continue;
                foreach (int r in b.InRes[i])
                    if (StockTotals[r] > 0.05f) { amount = math.min(B.carryCapacity, want - have); return r; }
            }
            return -1;
        }
    }
}
