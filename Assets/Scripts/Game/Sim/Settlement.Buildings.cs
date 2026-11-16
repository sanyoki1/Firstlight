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

        // ------------------------------------------------------------------ dismantling
        // Removing a building is a job. The player marks it (Dismantle); builders come and take it apart, the same way
        // they built it (Work runs down from where it stood to 0); then the building materials it gives back, and any
        // goods it still held, lie on its footprint as a pile (BuildState.Pile) until the normal hauling has carried
        // them to a store. Until the work is done the player can cancel (CancelDismantle).

        /// <summary>
        /// Marks a building or a site for dismantling. A finished building gives back balance.dismantleRefund of each
        /// building material, rounded down; a site gives back everything delivered so far. A site nobody has built on
        /// yet (and anything that took no work to build) comes apart at once. The campfire can't be removed.
        /// </summary>
        public bool Dismantle(Building b, out string reason)
        {
            reason = "";
            if (b == null || !b.Alive) { reason = "Nothing to remove"; return false; }
            if (b == TownCenter) { reason = "The campfire can't be removed"; return false; }
            if (b.Dismantling) { reason = "Already marked for demolition"; return false; }
            if (b.UpgradeTo != null) CancelUpgrade(b);        // the upgrade's materials go back to storage first
            // Materials on their way to the site are carried back to a store.
            foreach (var c in Citizens)
                if (c.TaskB2 == b && (c.Task == Task.FetchMaterial || c.Task == Task.DeliverMaterial || c.Task == Task.GoBuild || c.Task == Task.Build)) CancelTask(c);
            b.Dismantling = true;
            _jobsDirty = true;
            if (b.Def.buildWork <= 0 || b.IsSite && b.Work <= 0) { FinishDismantle(b); Map.CommitCostChanges(); }
            return true;
        }
        public bool Dismantle(Building b) => Dismantle(b, out _);

        /// <summary>Takes the mark off again. A finished building is whole again; a site is built on from where it stands.</summary>
        public bool CancelDismantle(Building b)
        {
            if (b == null || !b.Alive || !b.Dismantling) return false;
            b.Dismantling = false;
            if (b.Active) b.Work = 1f;
            _jobsDirty = true;
            return true;
        }

        /// <summary>
        /// What dismantling a building gives back, per good: the share of its building materials (a site: all that was
        /// delivered) and, if <paramref name="withGoods"/>, the goods it holds now (stored, made, brought in as inputs).
        /// </summary>
        public float[] DismantleReturn(Building b, bool withGoods = true)
        {
            var back = new float[ResCount];
            if (b == null || !b.Alive) return back;
            if (b.IsSite)
                for (int i = 0; i < b.Def.cost.Length && i < b.Delivered.Length; i++) back[b.CostRes[i]] += b.Delivered[i];
            else
                for (int i = 0; i < b.Def.cost.Length; i++)
                {
                    int r = ResIndex(b.Def.cost[i].id);
                    if (r >= 0) back[r] += math.floor(b.Def.cost[i].amount * B.dismantleRefund + 1e-3f);
                }
            if (withGoods)
                for (int r = 0; r < ResCount; r++) back[r] += b.Out[r] + b.In[r] + (b.Store != null ? b.Store[r] : 0f);
            return back;
        }

        /// <summary>Builder-days (one builder at full speed) left to take a marked building apart.</summary>
        public float DismantleWorkLeft(Building b) => b == null || !b.Dismantling ? 0f : b.Work * b.Def.buildWork * B.dismantleWork / math.max(0.01f, B.builderWorkPerDay);

        /// <summary>The work is done: the materials and whatever the building still held become a pile on its footprint
        /// (or, with nothing to give back, the ground is free at once).</summary>
        void FinishDismantle(Building b)
        {
            var mats = DismantleReturn(b, false);
            var pile = DismantleReturn(b, true);
            for (int r = 0; r < ResCount; r++) Consumed[r] -= mats[r];          // built-in materials come back
            // Everyone on an errand to or from it lets go of it (their promises are released).
            foreach (var c in Citizens)
            {
                if (c.TaskB == b || c.TaskB2 == b) CancelTask(c);
                if (c.Home == b) c.Home = null;
                if (c.Job == b) c.Job = null;
            }
            bool wasSite = b.IsSite;
            b.Dismantling = false;
            b.Work = 0; b.Workers = 0; b.Residents = 0; b.Builders = 0; b.HaulReserved = 0;
            b.Store = null; b.Stored = 0; b.Allow = null; b.KeepMin = null; b.KeepMax = null; b.Incoming = null; b.Outgoing = null;
            Array.Clear(b.In, 0, ResCount);
            Array.Clear(b.Delivered, 0, b.Delivered.Length); Array.Clear(b.InTransit, 0, b.InTransit.Length);
            float total = 0;
            for (int r = 0; r < ResCount; r++)
            {
                if (pile[r] < 0.01f) { Lost[r] += pile[r]; pile[r] = 0; }
                b.Out[r] = pile[r]; total += pile[r];
            }
            b.OutTotal = total;
            b.Status = "";
            string what = wasSite ? $"{b.Def.name} site" : b.Def.name;
            if (total >= 0.01f)
            {
                b.State = BuildState.Pile;
                Log(Severity.Info, $"{what} taken apart: {total:0.#} goods lie there until they are carried to a store.", b.Center);
            }
            else
            {
                RemoveFromMap(b);
                Log(Severity.Info, $"{what} taken apart.", b.Center);
            }
            if (b.Def.enablesLaws) RebuildEffects();
            _jobsDirty = true;
            RefreshStock();
        }

        /// <summary>Gives up the goods of a dismantled building's pile (they are lost) and frees the ground.</summary>
        public bool ClearPile(Building b)
        {
            if (b == null || !b.IsPile) return false;
            foreach (var c in Citizens) if (c.TaskB == b || c.TaskB2 == b) CancelTask(c);
            float lost = b.OutTotal;
            for (int r = 0; r < ResCount; r++) { Lost[r] += b.Out[r]; b.Out[r] = 0; }
            b.OutTotal = 0;
            RemoveFromMap(b);
            Map.CommitCostChanges();
            if (lost >= 0.5f) Log(Severity.Info, $"{lost:0.#} goods left where the {b.Def.name} stood were cleared away.", b.Center);
            return true;
        }

        /// <summary>A pile with nothing left that anyone would pick up frees its ground.</summary>
        void PileTaken(Building b)
        {
            if (!b.IsPile) return;
            for (int r = 0; r < ResCount; r++) if (b.Out[r] >= 0.05f) return;
            foreach (var c in Citizens) if (!c.Gone && (c.TaskB == b || c.TaskB2 == b) && c.Task != Task.None) return;   // someone is still on the way
            for (int r = 0; r < ResCount; r++) { Lost[r] += b.Out[r]; b.Out[r] = 0; }
            b.OutTotal = 0;
            RemoveFromMap(b);
        }

        /// <summary>Hourly: piles whose goods spoiled away or were all carried off.</summary>
        void SweepPiles()
        {
            foreach (var b in Buildings) if (b.IsPile) PileTaken(b);
        }

        /// <summary>Takes a building, site or pile off the map: its cells and its entrance are free again.</summary>
        void RemoveFromMap(Building b)
        {
            b.State = BuildState.Removed;
            for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                {
                    int c = Map.Cell(x, y);
                    Map.BuildingAt[c] = -1;
                    Map.Open[c] = false;
                    Map.RefreshCost(c);
                }
            bool doorShared = false;
            foreach (var o in Buildings) if (o.OnMap && o.Door == b.Door) doorShared = true;
            if (!doorShared) Map.Reserved[b.Door] = false;
            // The freed cells' walking prices are committed by the caller when it is a player's command (as placing
            // does), else by the next hourly step with every other change: a commit drops every cached path, and in
            // the middle of a step each walker would then rebuild its own, one by one.
            _jobsDirty = true;
        }

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
                    // Reach and caps from CampfireReach (trees 11/45, rock 13/8, water 14/12, game 18/14; the HUD's tip judges by them).
                    var cr = CampfireReach;
                    float score = math.min(Map.CountFeature(cr[0].feature, p, cr[0].radius), cr[0].full) * 0.5f
                                + math.min(Map.CountFeature(cr[1].feature, p, cr[1].radius), cr[1].full) * 2f
                                + math.min(Map.CountFeature(cr[2].feature, p, cr[2].radius), cr[2].full) * 0.8f
                                + math.min(Map.CountFeature(cr[3].feature, p, cr[3].radius), cr[3].full) * 0.6f
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
            if (b.Dismantling) { reason = "Marked for demolition"; return false; }
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
            if (b.Focus >= 0) b.Focus = b.Focus < from.outputs.Length ? Array.FindIndex(d.outputs, o => o.id == from.outputs[b.Focus].id) : -1;   // same good, else the usual mix
            if (b.IsStorage && b.Store == null) b.Store = new float[ResCount];
            EnsureStoreSettings(b);
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
            EnsureStoreSettings(b);
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

        // ------------------------------------------------------------------ spoilage
        // (Storage itself, what stores take and the haulers between them, is in Settlement.Storage.cs.)

        void DailySpoilage()
        {
            float foodMult = Effect("spoil.food");
            foreach (var b in Buildings)
            {
                if (!b.OnMap) continue;
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
