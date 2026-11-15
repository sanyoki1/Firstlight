using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Placement, construction sites, storage and production.
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ placement

        /// <summary>Footprint size on the map for a turn (rot = quarter turns, 0..3): odd turns swap the sides.</summary>
        public static int2 Size(BuildingDef d, int rot) => (rot & 1) == 0 ? new int2(d.sizeX, d.sizeY) : new int2(d.sizeY, d.sizeX);
        public static float2 FootprintCenter(BuildingDef d, int2 origin, int rot = 0) => origin + (float2)Size(d, rot) * 0.5f;

        /// <summary>
        /// The walkable cell in front of a footprint, where people go in and out. Unturned the front faces -y (the door
        /// below the middle); each quarter turn carries the door round with the model (yaw = rot x 90 degrees: -x, +y, +x).
        /// </summary>
        public static int2 DoorCell(BuildingDef d, int2 origin, int rot = 0)
        {
            // The unturned door's offset from the footprint middle, turned like the model (the view's yaw convention).
            float dx = d.sizeX / 2 + 0.5f - d.sizeX * 0.5f, dy = -d.sizeY * 0.5f - 0.5f;
            int r = rot & 3;
            float s = r == 1 ? 1 : r == 3 ? -1 : 0, c = r == 0 ? 1 : r == 2 ? -1 : 0;
            var p = FootprintCenter(d, origin, rot) + new float2(c * dx + s * dy, -s * dx + c * dy);
            return new int2((int)math.floor(p.x), (int)math.floor(p.y));
        }

        /// <summary>
        /// Whether a building can go with its lower-left corner on <paramref name="origin"/>: footprint on dry free
        /// ground, a walkable door cell, research and age reached, nearby features present, unique respected.
        /// The first placement must be the town center.
        /// </summary>
        public bool CanPlace(BuildingDef d, int2 origin, out string reason) => CanPlace(d, origin, 0, out reason);

        public bool CanPlace(BuildingDef d, int2 origin, int rot, out string reason)
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
            if (d.path) return CanPlacePath(origin, out reason);
            var size = Size(d, rot);
            for (int y = origin.y; y < origin.y + size.y; y++)
                for (int x = origin.x; x < origin.x + size.x; x++)
                {
                    if (x < 1 || y < 1 || x >= Map.W - 1 || y >= Map.H - 1) { reason = "Too close to the map edge"; return false; }
                    int c = Map.Cell(x, y);
                    if (Map.Ground[c] != Ground.Grass || Map.WaterClass[c] > 0) { reason = "Can't build in water"; return false; }
                    if (Map.BuildingAt[c] >= 0) { reason = "Something is already there"; return false; }
                    if (Map.Reserved[c]) { reason = "Blocks another building's entrance"; return false; }
                    if (Map.Rock[c] > 0) { reason = "Rock in the way"; return false; }
                }
            var door = DoorCell(d, origin, rot);
            if (!Map.InBounds(door.x, door.y)) { reason = "No room for an entrance"; return false; }
            int dc = Map.Cell(door.x, door.y);
            if (Map.WaterClass[dc] >= 2 || Map.BuildingAt[dc] >= 0 || Map.Rock[dc] > 0) { reason = "The entrance is blocked"; return false; }
            if (!string.IsNullOrEmpty(d.onDeposit))
            {
                var kind = GameMap.DepositOf(d.onDeposit);
                if (!Map.DepositVisible(kind)) { reason = $"{Cap(d.onDeposit)} deposits are hidden until Prospecting"; return false; }
                int on = DepositCells(d, origin, rot);
                if (on < d.depositMin) { reason = on == 0 ? $"Must be built on a {d.onDeposit} deposit" : $"Must stand more on the {d.onDeposit} deposit ({on} of {d.depositMin} cells)"; return false; }
            }
            if (d.HasNearby && d.nearby.min > 0)
            {
                int n = Map.CountFeature(d.nearby.feature, FootprintCenter(d, origin, rot), d.nearby.radius);
                if (n < d.nearby.min) { reason = $"Needs {(d.nearby.feature == "open" ? "open grass" : d.nearby.feature)} nearby ({n} of {d.nearby.min})"; return false; }
            }
            return true;
        }

        /// <summary>Footprint cells over a building's deposit kind.</summary>
        public int DepositCells(BuildingDef d, int2 origin, int rot = 0)
        {
            var kind = GameMap.DepositOf(d.onDeposit);
            int on = 0;
            var size = Size(d, rot);
            for (int y = origin.y; y < origin.y + size.y; y++)
                for (int x = origin.x; x < origin.x + size.x; x++)
                    if (Map.InBounds(x, y) && Map.Deposits[Map.Cell(x, y)] == kind) on++;
            return on;
        }

        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------ paths

        /// <summary>A path tile can go on any dry, free ground cell, including entrances.</summary>
        public bool CanPlacePath(int2 cell, out string reason)
        {
            reason = "";
            if (cell.x < 1 || cell.y < 1 || cell.x >= Map.W - 1 || cell.y >= Map.H - 1) { reason = "Too close to the map edge"; return false; }
            int c = Map.Cell(cell.x, cell.y);
            if (Map.Ground[c] != Ground.Grass || Map.WaterClass[c] > 0) { reason = "Can't build in water"; return false; }
            if (Map.BuildingAt[c] >= 0) { reason = "Something is already there"; return false; }
            if (Map.Rock[c] > 0) { reason = "Rock in the way"; return false; }
            if (Map.Path[c]) { reason = "Already a path"; return false; }
            var d = PathDef;
            if (d != null)
                foreach (var a in d.cost)
                    if (Stock(a.id) < a.amount - 1e-4f) { reason = $"Needs {a.amount:0.#} {Data.Resource(a.id)?.name.ToLower() ?? a.id} per tile"; return false; }
            return true;
        }

        BuildingDef _pathDef; bool _pathDefLooked;
        public BuildingDef PathDef { get { if (!_pathDefLooked) { _pathDefLooked = true; _pathDef = Data.Buildings.Find(b => b.path); } return _pathDef; } }

        /// <summary>Lays one path tile (free and immediate: people tread it in). Paths are not buildings.</summary>
        public bool PlacePath(int2 cell, out string reason)
        {
            var d = PathDef;
            if (d == null) { reason = "No paths in this build"; return false; }
            if (NeedsCampfire) { reason = "Place the campfire first"; return false; }
            if (!IsUnlocked(d)) { reason = $"Needs the {Data.Age(d.age)?.name ?? d.age}"; return false; }
            if (!CanPlacePath(cell, out reason)) return false;
            // A little material per tile, taken from store at once (tiles are laid on the spot).
            foreach (var a in d.cost) { int r = ResIndex(a.id); if (r >= 0) Withdraw(r, a.amount); }
            Map.SetPath(Map.Cell(cell.x, cell.y), true);
            PathCount++;
            Map.CommitCostChanges();
            return true;
        }
        public bool PlacePath(int2 cell) => PlacePath(cell, out _);

        public bool RemovePath(int2 cell)
        {
            if (!Map.InBounds(cell.x, cell.y) || !Map.Path[Map.Cell(cell.x, cell.y)]) return false;
            Map.SetPath(Map.Cell(cell.x, cell.y), false);
            PathCount--;
            Map.CommitCostChanges();
            return true;
        }

        public int PathCount { get; private set; }
        public bool CanPlace(BuildingDef d, int2 origin) => CanPlace(d, origin, out _);

        /// <summary>Places a construction site (or, for the campfire, the finished town center and the starting band).</summary>
        public Building Place(BuildingDef d, int2 origin) => Place(d, origin, out _);

        public Building Place(BuildingDef d, int2 origin, out string reason) => Place(d, origin, 0, out reason);

        /// <summary>Places a building turned <paramref name="rot"/> quarter turns (its door turns with it).</summary>
        public Building Place(BuildingDef d, int2 origin, int rot, out string reason)
        {
            if (d != null && d.path) { PlacePath(origin, out reason); return null; }
            rot &= 3;
            if (!CanPlace(d, origin, rot, out reason)) return null;
            var size = Size(d, rot);
            var b = new Building
            {
                Id = Buildings.Count, Def = d, Origin = origin, Rot = rot, State = BuildState.Site,
                Delivered = new float[d.cost.Length], InTransit = new float[d.cost.Length],
                Out = new float[ResCount], In = new float[ResCount],
                Center = FootprintCenter(d, origin, rot),
                CostRes = Array.ConvertAll(d.cost, a => ResIndex(a.id)),
                OutRes = Array.ConvertAll(d.outputs, a => ResIndex(a.id)),
                InRes = Array.ConvertAll(d.inputs, i => Array.ConvertAll(i.ids, ResIndex)),
                YieldKey = "yield." + d.yieldTag,
            };
            var door = DoorCell(d, origin, rot);
            b.Door = Map.Cell(door.x, door.y);
            b.DoorPos = new float2(door.x + 0.5f, door.y + 0.5f);
            Buildings.Add(b);
            for (int y = origin.y; y < origin.y + size.y; y++)
                for (int x = origin.x; x < origin.x + size.x; x++)
                {
                    int c = Map.Cell(x, y);
                    Map.RemoveTree(c);
                    if (Map.Path[c]) { Map.SetPath(c, false); PathCount--; }
                    Map.BuildingAt[c] = b.Id;
                    Map.Open[c] = d.crop;           // people walk through fields
                    Map.RefreshCost(c);
                }
            Map.Reserved[b.Door] = true;
            // The ground round it is cleared too: a yard, and room to walk round.
            for (int y = origin.y - 1; y <= origin.y + size.y; y++)
                for (int x = origin.x - 1; x <= origin.x + size.x; x++)
                    if (Map.InBounds(x, y)) Map.RemoveTree(Map.Cell(x, y));
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
            {
                // (The refund line is kept exactly as it was: the JIT may keep the product in higher precision, and a
                // float local in between would round it differently and change the game.)
                refund[ResIndex(b.Def.cost[i].id)] += b.IsSite ? b.Delivered[i] : b.Def.cost[i].amount * B.demolishRefund;
                Consumed[ResIndex(b.Def.cost[i].id)] -= b.IsSite ? b.Delivered[i] : b.Def.cost[i].amount * B.demolishRefund;   // built-in materials come back
            }
            if (b.UpgradeTo != null)
                for (int i = 0; i < b.UpgradeTo.cost.Length; i++) { refund[b.CostRes[i]] += b.Delivered[i]; Consumed[b.CostRes[i]] -= b.Delivered[i]; }
            for (int r = 0; r < ResCount; r++) refund[r] += b.Out[r] + b.In[r];
            if (b.Store != null) for (int r = 0; r < ResCount; r++) refund[r] += b.Store[r];
            b.State = BuildState.Removed;
            // What it held is moved out (or lost) below: nothing stays behind in the removed building.
            if (b.Store != null) { Array.Clear(b.Store, 0, ResCount); b.Stored = 0; }
            Array.Clear(b.Out, 0, ResCount); Array.Clear(b.In, 0, ResCount); b.OutTotal = 0;
            float lost = 0;
            for (int r = 0; r < ResCount; r++)
            {
                if (refund[r] > 0.01f) { float put = Deposit(r, refund[r], b.Center); lost += refund[r] - put; Lost[r] += refund[r] - put; }
                else Lost[r] += refund[r];
            }
            foreach (var c in Citizens)
            {
                if (c.Home == b) c.Home = null;
                if (c.Job == b) c.Job = null;
            }
            for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                {
                    int c = Map.Cell(x, y);
                    Map.BuildingAt[c] = -1;
                    Map.Open[c] = false;
                    Map.RefreshCost(c);
                }
            bool doorShared = false;
            foreach (var o in Buildings) if (o.Alive && o.Door == b.Door) doorShared = true;
            if (!doorShared) Map.Reserved[b.Door] = false;
            Map.CommitCostChanges();
            Log(lost >= 1f ? Severity.Warning : Severity.Info, lost >= 1f ? $"{b.Def.name} removed. {lost:0} goods were lost: no store had room for them." : $"{b.Def.name} removed.", b.Center);
            if (b.Def.enablesLaws) RebuildEffects();
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

        // ------------------------------------------------------------------ upgrades

        /// <summary>The building this one can be rebuilt as (null if none).</summary>
        public BuildingDef UpgradeOption(Building b)
        {
            if (b == null || !b.Active || string.IsNullOrEmpty(b.Def.upgradesTo)) return null;
            return Data.Building(b.Def.upgradesTo);
        }

        public bool CanUpgrade(Building b, out string reason)
        {
            reason = "";
            var t = UpgradeOption(b);
            if (t == null) { reason = "Can't be upgraded"; return false; }
            if (b.UpgradeTo != null) { reason = "Already being upgraded"; return false; }
            if (!IsUnlocked(t)) { reason = !string.IsNullOrEmpty(t.unlockedBy) && !IsResearched(t.unlockedBy) ? $"Needs research: {Data.ResearchById(t.unlockedBy)?.name}" : $"Needs the {Data.Age(t.age)?.name}"; return false; }
            return true;
        }
        public bool CanUpgrade(Building b) => CanUpgrade(b, out _);

        /// <summary>
        /// Rebuilds a building in place as its upgrade: builders bring the new building's materials and build it while
        /// the old one keeps working (residents stay). Cancel with <see cref="CancelUpgrade"/>.
        /// </summary>
        public bool Upgrade(Building b, out string reason)
        {
            if (!CanUpgrade(b, out reason)) return false;
            var t = UpgradeOption(b);
            b.UpgradeTo = t;
            b.Delivered = new float[t.cost.Length];
            b.InTransit = new float[t.cost.Length];
            b.CostRes = Array.ConvertAll(t.cost, a => ResIndex(a.id));
            b.Work = 0;
            if (t.cost.Length == 0 && t.buildWork <= 0) CompleteUpgrade(b);
            _jobsDirty = true;
            return true;
        }
        public bool Upgrade(Building b) => Upgrade(b, out _);

        /// <summary>Stops an upgrade: delivered materials go back to storage.</summary>
        public void CancelUpgrade(Building b)
        {
            if (b == null || b.UpgradeTo == null) return;
            var refund = new float[ResCount];
            for (int i = 0; i < b.UpgradeTo.cost.Length; i++) { refund[b.CostRes[i]] += b.Delivered[i]; Consumed[b.CostRes[i]] -= b.Delivered[i]; }
            foreach (var c in Citizens) if (c.TaskB2 == b && (c.Task == Task.FetchMaterial || c.Task == Task.DeliverMaterial || c.Task == Task.GoBuild || c.Task == Task.Build)) CancelTask(c);
            b.UpgradeTo = null;
            b.Delivered = new float[b.Def.cost.Length];
            b.InTransit = new float[b.Def.cost.Length];
            b.CostRes = Array.ConvertAll(b.Def.cost, a => ResIndex(a.id));
            b.Work = 1f;
            for (int r = 0; r < ResCount; r++) Lost[r] += refund[r] > 0.01f ? refund[r] - Deposit(r, refund[r], b.Center) : refund[r];
            _jobsDirty = true;
        }

        void CompleteUpgrade(Building b)
        {
            var from = b.Def;
            var d = b.UpgradeTo;
            b.Def = d;
            b.UpgradeTo = null;
            b.Work = 1f;
            b.Delivered = new float[d.cost.Length];
            b.InTransit = new float[d.cost.Length];
            for (int i = 0; i < d.cost.Length; i++) b.Delivered[i] = d.cost[i].amount;
            b.CostRes = Array.ConvertAll(d.cost, a => ResIndex(a.id));
            b.OutRes = Array.ConvertAll(d.outputs, a => ResIndex(a.id));
            b.InRes = Array.ConvertAll(d.inputs, i => Array.ConvertAll(i.ids, ResIndex));
            b.YieldKey = "yield." + d.yieldTag;
            if (b.IsStorage && b.Store == null) b.Store = new float[ResCount];
            RefreshFullness(b);
            Upgrades++;
            Log(Severity.Info, $"{from.name} rebuilt as a {d.name}.", b.Center);
            _jobsDirty = true;
        }

        public int Upgrades { get; private set; }

        void Complete(Building b)
        {
            b.State = BuildState.Active;
            b.Work = 1f;
            if (b.IsStorage && b.Store == null) b.Store = new float[ResCount];
            RefreshFullness(b);
            if (!b.Def.townCenter) Log(Severity.Info, $"{b.Def.name} finished.", b.Center);
            if (b.Def.enablesLaws) RebuildEffects();
            _jobsDirty = true;
        }

        /// <summary>The campfire is lit: the starting band and their goods appear around it.</summary>
        void FoundSettlement(Building tc)
        {
            foreach (var a in B.startResources)
            {
                int r = ResIndex(a.id);
                if (r >= 0) { tc.Store[r] += a.amount * Diff.startGoods; tc.Stored += a.amount * Diff.startGoods; }
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
            Log(Severity.Info, $"The band lights its campfire: {Population} people.", tc.Center);
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

        /// <summary>A store's capacity with research (Pottery's jars) applied. The campfire's store grows with a gentle
        /// start's extra goods, so they fit (they used to start it overfull).</summary>
        public float Capacity(Building s) => s.Def.storageCapacity * Effect("storage") * (s.Def.townCenter ? math.max(1f, Diff.startGoods) : 1f);

        float Free(Building s) => Capacity(s) - s.Stored;

        /// <summary>Room not already promised to someone carrying goods here.</summary>
        float Room(Building s) => Free(s) - s.HaulReserved;

        /// <summary>A store with an accepts list (the granary) only takes those categories.</summary>
        public bool Accepts(Building s, int r) => r < 0 || s.Def.accepts.Length == 0 || Array.IndexOf(s.Def.accepts, Data.Resources[r].category) >= 0;

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
            if (s.Store[r] < 1e-4f) { s.Stored -= s.Store[r]; StockTotals[r] -= s.Store[r]; Lost[r] += s.Store[r]; s.Store[r] = 0; }
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
            // Room nobody is carrying goods to first; then, rather than lose it, any room.
            for (int k = 0; k < 8 && left > 0.001f; k++)
            {
                var s = NearestStorageFor(near, r, 0.01f, true);
                if (s == null) break;
                left -= PutInto(s, r, math.min(left, Room(s)));
            }
            for (int k = 0; k < 8 && left > 0.001f; k++)
            {
                var s = NearestStorageFor(near, r, 0.01f);
                if (s == null) break;
                left -= PutInto(s, r, left);
            }
            return amt - left;
        }

        /// <summary>
        /// The store to take a good to: the nearest with room that accepts it; goods that spoil prefer stores that keep
        /// them (a granary up to about twice as far away wins over an open pile). r = -1: any good.
        /// </summary>
        Building NearestStorageFor(float2 from, int r, float min, bool unreserved = false)
        {
            Building best = null; float bd = float.MaxValue;
            bool spoils = r >= 0 && Data.Resources[r].spoilPerDay > 0;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Flooded || Free(s) - (unreserved ? s.HaulReserved : 0) < min || !Accepts(s, r)) continue;
                float d = math.distance(s.DoorPos, from) + 4f;
                if (spoils) d *= 0.4f + s.Def.spoilMultiplier;
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
                        b.Store[r] -= loss; b.Stored -= loss; Lost[r] += loss;
                    }
                    if (b.Out[r] > 0) { float loss = b.Out[r] * math.saturate(rate); b.Out[r] -= loss; b.OutTotal -= loss; Lost[r] += loss; }
                    if (b.In[r] > 0) { float before = b.In[r]; b.In[r] -= b.In[r] * math.saturate(rate); Lost[r] += before - b.In[r]; }
                }
            }
        }

        // ------------------------------------------------------------------ production

        /// <summary>Most finished goods a workplace holds before work stops (waiting to be carried off).</summary>
        float OutCap(Building b) => B.carryCapacity * math.max(2, b.Def.maxWorkers) * 2f;

        void RefreshFullness()
        {
            foreach (var b in Buildings) if (b.Active && (b.Def.HasNearby || b.Def.crop)) RefreshFullness(b);
        }

        void RefreshFullness(Building b)
        {
            if (b.Def.crop && b.Def.irrigationRadius > 0) b.Irrigated = Map.CountFeature("water", b.Center, b.Def.irrigationRadius) > 0;
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
        bool WorkStep(Citizen c, Building b, float speed, bool field = false)
        {
            var d = b.Def;
            float frac = Step / WorkdaySeconds * speed;
            bool did = false;
            if (d.knowledgePerWorker > 0)
            {
                float mult = c.Age >= B.elderAge ? B.elderKnowledgeMult * Effect("knowledge.elders") : 1f;
                float learned = d.knowledgePerWorker * frac * mult * Effect(b.YieldKey);
                AddKnowledge(learned); b.Learned += learned;
                did = true;
            }
            if (d.outputs.Length == 0) return did;
            if (d.crop) return CropWork(c, b, frac, field);
            if (!field && b.OutTotal >= OutCap(b)) { b.Status = "Waiting for goods to be carried off"; return did; }
            float yield = d.SeasonYield(Season) * b.Fullness * (string.IsNullOrEmpty(d.yieldTag) ? 1f : Effect(b.YieldKey)) * Diff.yield;
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
                    Consumed[r] += t;
                    if (need <= 0) break;
                }
            }
            for (int i = 0; i < d.outputs.Length; i++)
            {
                float per = OutputPerWorkday(b, i);
                float amt = per * work * scale;
                if (amt <= 0) continue;
                if (field)
                {
                    // Out at the tree / rock / herd: into the worker's own load.
                    c.Load ??= new float[ResCount];
                    c.Load[b.OutRes[i]] += amt;
                }
                else
                {
                    b.Out[b.OutRes[i]] += amt;
                    b.OutTotal += amt;
                }
                Produced[b.OutRes[i]] += amt;
                if (b.OutRes[i] == _grain) GrainProducedF += amt;
                b.Made += amt;
            }
            if (field) RecountLoad(c);
            if (d.consumesFeaturePer > 0)
            {
                // The feature is used up by the work done, whatever the output mix.
                b.FeatureAcc += d.outputs[0].amount * work * scale;
                while (b.FeatureAcc >= d.consumesFeaturePer) { b.FeatureAcc -= d.consumesFeaturePer; ConsumeFeature(b, field ? c.FieldFeature : -1); }
            }
            b.Status = "";
            return true;
        }

        /// <summary>Workers use up what they work: one tree felled, one stone off a rock, one animal from a herd.
        /// <paramref name="at"/> = the tree / rock cell or herd a field worker is at (-1 = the nearest).</summary>
        void ConsumeFeature(Building b, int at = -1)
        {
            var n = b.Def.nearby;
            if (n == null) return;
            switch (n.feature)
            {
                case "trees":
                {
                    int c = at >= 0 && Map.Tree[at] ? at : Map.NearestFeatureCell("trees", b.Center, n.radius);
                    if (c >= 0) Map.RemoveTree(c);
                    break;
                }
                case "rocks":
                {
                    int c = at >= 0 && Map.Rock[at] > 0 ? at : Map.NearestFeatureCell("rocks", b.Center, n.radius);
                    if (c >= 0) Map.TakeStone(c, 1f);
                    break;
                }
                case "wildlife":
                {
                    if (at >= 0 && at < Map.Herds.Count && Map.Herds[at].Pop >= 1f) { Map.Herds[at].Pop -= 1f; Kills.Add((at, Time)); break; }
                    Herd best = null; float bd = float.MaxValue;
                    foreach (var h in Map.Herds)
                    {
                        float d = math.distance(h.Pos, b.Center);
                        if (h.Pop >= 1f && d <= n.radius && d < bd) { bd = d; best = h; }
                    }
                    if (best != null) { best.Pop = math.max(0f, best.Pop - 1f); Kills.Add((Map.Herds.IndexOf(best), Time)); }
                    break;
                }
            }
            RefreshFullness(b);
        }

        /// <summary>Animals taken by hunters (herd index, game time), so the view can show a carcass. Trimmed by the view.</summary>
        public readonly List<(int herd, float time)> Kills = new();

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

        /// <summary>Base amount of output i one worker makes in a workday, before season, tools, mood etc. A focused
        /// workplace makes only its chosen output: its own amount plus the time saved on the others, at focusShare
        /// (so focusing never makes less of the chosen good than the usual mix, even when it is the main one).</summary>
        public float OutputPerWorkday(Building b, int i)
        {
            var d = b.Def;
            if (d.exclusiveOutputs) return i == math.max(0, b.Focus) ? d.outputs[i].amount : 0f;
            if (b.Focus < 0) return d.outputs[i].amount;
            if (i != b.Focus) return 0f;
            float others = 0;
            for (int j = 0; j < d.outputs.Length; j++) if (j != i) others += d.outputs[j].amount;
            return d.outputs[i].amount + others * B.focusShare;
        }
    }
}
