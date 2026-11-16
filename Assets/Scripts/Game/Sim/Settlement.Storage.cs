using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Storage: what each store holds and takes, where goods go, what may be taken from where, and the haulers who move
    // goods between stores.
    //
    // The player's settings per store (Building.Allow / StorePriority / KeepMin / KeepMax):
    // - which goods it takes (within what the building can hold at all: a granary holds food only);
    // - its priority: new goods go to the highest-priority store that takes them, and idle haulers move goods up from
    //   lower-priority stores;
    // - per good, keep at least N (goods come here first, haulers fill it from other stores, and nobody takes it below N
    //   until every other store is out of that good) and keep at most N (no more comes in; the rest is moved out).
    // A store left as built (everything it can hold, Normal, no limits) behaves exactly as stores did before settings.
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ totals and room

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

        // ------------------------------------------------------------------ what a store takes

        /// <summary>The building can hold this good at all: a store with an accepts list (the granary) only takes those
        /// categories. The settings panel offers only these. r = -1: any good.</summary>
        public bool CanHold(Building s, int r) => r < 0 || s.Def.accepts.Length == 0 || Array.IndexOf(s.Def.accepts, Data.Resources[r].category) >= 0;

        /// <summary>The store takes this good now: the building can hold it and the player lets it in.</summary>
        public bool Accepts(Building s, int r) => CanHold(s, r) && (r < 0 || s.Allow == null || s.Allow[r]);

        /// <summary>Keep at least this much of the good here (0 = no reserve). Only while the store takes the good: one shut out
        /// is moved out whatever its reserve said.</summary>
        public float KeepMinOf(Building s, int r) => s.KeepMin != null && r >= 0 && s.KeepMin[r] > 0 && Accepts(s, r) ? s.KeepMin[r] : 0f;

        /// <summary>Keep at most this much of the good here (0 = no cap).</summary>
        public float KeepMaxOf(Building s, int r) => s.KeepMax != null && r >= 0 ? s.KeepMax[r] : 0f;

        /// <summary>Room left under the good's cap (no cap: endless), less what carriers are bringing if <paramref name="promised"/>.</summary>
        float CapRoom(Building s, int r, bool promised)
        {
            float cap = KeepMaxOf(s, r);
            if (cap <= 0) return float.MaxValue;
            return cap - s.Store[r] - (promised ? Incoming(s, r) : 0f);
        }

        /// <summary>Room for one good: room nobody is carrying goods to, and under its cap.</summary>
        float RoomIn(Building s, int r) => math.min(Free(s) - s.HaulReserved, CapRoom(s, r, true));

        /// <summary>How much of a good may leave a store for another store: all of it if the store no longer takes it, else
        /// what is above its keep-at-least.</summary>
        public float Movable(Building s, int r) => Accepts(s, r) ? math.max(0, s.Store[r] - KeepMinOf(s, r)) : s.Store[r];

        /// <summary>
        /// How eagerly a store wants a good: a store below its keep-at-least of it first (3), then by priority (High 2,
        /// Normal 1, Low 0).
        /// </summary>
        int StoreTier(Building s, int r)
        {
            if (r >= 0 && s.KeepMin != null)
            {
                float keep = KeepMinOf(s, r);
                if (keep > 0 && s.Store[r] + Incoming(s, r) < keep - 0.01f) return 3;
            }
            return (int)s.StorePriority;
        }

        // ------------------------------------------------------------------ putting in, taking out

        /// <summary>Puts up to amt of a good into a store: what fits, and (unless <paramref name="overCap"/>) no more than its cap.</summary>
        float PutInto(Building s, int r, float amt, bool overCap = false)
        {
            float put = math.min(amt, math.max(0, Free(s)));
            if (!overCap && s.KeepMax != null) put = math.min(put, math.max(0, CapRoom(s, r, false)));
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

        /// <summary>A good in store that no keep-at-least holds back: what anyone may take before the reserves.</summary>
        public float UsableStock(int r)
        {
            float n = 0;
            foreach (var s in Buildings) if (s.Active && s.Store != null) n += math.max(0, s.Store[r] - KeepMinOf(s, r));
            return n;
        }

        /// <summary>A good held back in reserves (keep-at-least) across all stores.</summary>
        public float ReservedStock(int r)
        {
            float n = 0;
            foreach (var s in Buildings) if (s.Active && s.Store != null && s.KeepMin != null) n += math.min(s.Store[r], KeepMinOf(s, r));
            return n;
        }

        /// <summary>What may be taken from this store now: what is above its keep-at-least, or, once no store has any of the
        /// good outside a reserve, all of it (nobody waits forever beside a full reserve).</summary>
        float Spare(Building s, int r)
        {
            float keep = KeepMinOf(s, r);
            if (keep <= 0) return s.Store[r];
            return UsableStock(r) < 0.01f ? s.Store[r] : math.max(0, s.Store[r] - keep);
        }

        /// <summary>Takes a good from a store for use (a workplace's input, a builder's material, a tool): never into its
        /// reserve while another store has some.</summary>
        float TakeUsable(Building s, int r, float amt) => TakeFrom(s, r, KeepMinOf(s, r) > 0 ? math.min(amt, Spare(s, r)) : amt);

        /// <summary>
        /// Takes up to amt of a resource from any stores: low-priority stores first and high-priority ones last (they are
        /// where goods are meant to be kept), in building order within each. Used for eating, burning, clothing, herbs,
        /// pottery, path tiles and traders. Reserves (keep-at-least) are used last of all, and not at all with
        /// <paramref name="reserves"/> false (traders never take them).
        /// </summary>
        float Withdraw(int r, float amt, bool reserves = true)
        {
            float got = 0;
            bool held = false;
            for (int p = (int)Priority.Low; p <= (int)Priority.High && got < amt; p++)
                foreach (var s in Buildings)
                {
                    if (got >= amt) break;
                    if (!s.Active || s.Store == null || (int)s.StorePriority != p || s.Store[r] <= 0) continue;
                    float keep = KeepMinOf(s, r);
                    if (keep <= 0) got += TakeFrom(s, r, amt - got);
                    else
                    {
                        held = true;
                        float spare = s.Store[r] - keep;
                        if (spare > 0) got += TakeFrom(s, r, math.min(amt - got, spare));
                    }
                }
            // Not enough outside the reserves: then the reserves (nobody starves or freezes beside a full one).
            if (held && reserves)
                for (int p = (int)Priority.Low; p <= (int)Priority.High && got < amt; p++)
                    foreach (var s in Buildings)
                    {
                        if (got >= amt) break;
                        if (s.Active && s.Store != null && (int)s.StorePriority == p && s.Store[r] > 0) got += TakeFrom(s, r, amt - got);
                    }
            Consumed[r] += got;
            return got;
        }

        /// <summary>Puts goods into the nearest stores with room that take them. Rather than lose them it then uses a store
        /// that could hold them though its settings shut them out (haulers move them on once a store takes them); whatever
        /// still does not fit is lost.</summary>
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
            for (int k = 0; k < 8 && left > 0.001f; k++)
            {
                var s = NearestHolder(near, r);
                if (s == null) break;
                left -= PutInto(s, r, left, overCap: true);
            }
            return amt - left;
        }

        // ------------------------------------------------------------------ choosing a store

        /// <summary>
        /// The store to take a good to: of the stores with room that take it, the most eager (below its keep-at-least of
        /// the good, then by priority), and among those the nearest; goods that spoil prefer stores that keep them (a granary
        /// up to about twice as far away wins over an open pile). r = -1: any good. <paramref name="aboveTier"/>: only stores
        /// more eager than that (for moving goods up the priorities).
        /// </summary>
        Building NearestStorageFor(float2 from, int r, float min, bool unreserved = false, int aboveTier = -1)
        {
            Building best = null; float bd = float.MaxValue; int bt = -1;
            bool spoils = r >= 0 && Data.Resources[r].spoilPerDay > 0;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Flooded || Free(s) - (unreserved ? s.HaulReserved : 0) < min || !Accepts(s, r)) continue;
                if (s.KeepMax != null && r >= 0 && CapRoom(s, r, unreserved) < min) continue;
                int tier = StoreTier(s, r);
                if (tier <= aboveTier) continue;
                float d = math.distance(s.DoorPos, from) + 4f;
                if (spoils) d *= 0.4f + s.Def.spoilMultiplier;
                if (tier > bt || tier == bt && d < bd) { bt = tier; bd = d; best = s; }
            }
            return best;
        }

        /// <summary>The nearest store that could hold a good at all, whatever its settings (a last place before losing it).</summary>
        Building NearestHolder(float2 from, int r)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Flooded || Free(s) < 0.01f || !CanHold(s, r)) continue;
                float d = math.distancesq(s.DoorPos, from);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// <summary>The nearest store with at least <paramref name="min"/> of a good outside its reserve; when only reserves
        /// have it, the nearest of those (the last resort).</summary>
        Building NearestStorageWith(float2 from, int r, float min = 0.01f)
        {
            Building best = null; float bd = float.MaxValue;
            bool held = false;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null) continue;
                float keep = KeepMinOf(s, r);
                if (keep > 0) held = true;
                if (s.Store[r] - keep < min) continue;
                float d = math.distancesq(s.DoorPos, from);
                if (d < bd) { bd = d; best = s; }
            }
            if (best != null || !held) return best;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Store[r] < min) continue;
                float d = math.distancesq(s.DoorPos, from);
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        // ------------------------------------------------------------------ goods on their way (per step)

        float _flowStamp = -1f;

        /// <summary>A good carriers are bringing to this store (worked out once a step from what people are doing).</summary>
        float Incoming(Building s, int r) { RefreshFlows(); return s.Incoming != null ? s.Incoming[r] : 0f; }

        /// <summary>A good haulers are on their way to take from this store to another.</summary>
        float Outgoing(Building s, int r) { RefreshFlows(); return s.Outgoing != null ? s.Outgoing[r] : 0f; }

        void RefreshFlows()
        {
            if (_flowStamp == Time) return;
            _flowStamp = Time;
            foreach (var b in Buildings)
            {
                if (b.Store == null) continue;
                if (b.Incoming == null) { b.Incoming = new float[ResCount]; b.Outgoing = new float[ResCount]; }
                else { Array.Clear(b.Incoming, 0, ResCount); Array.Clear(b.Outgoing, 0, ResCount); }
            }
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                switch (c.Task)
                {
                    case Task.DeliverOutput: AddFlow(c.TaskB, c.CarryRes, c.TaskAmt, true); break;
                    case Task.FieldReturn: AddFlow(c.TaskB, c.LoadRes, c.TaskAmt, true); break;
                    case Task.MoveStock: AddFlow(c.TaskB2, c.TaskIdx, c.TaskAmt, true); AddFlow(c.TaskB, c.TaskIdx, c.TaskAmt, false); break;
                }
            }
        }

        static void AddFlow(Building s, int r, float amt, bool into)
        {
            if (s?.Incoming == null || r < 0 || r >= s.Incoming.Length || amt <= 0) return;
            (into ? s.Incoming : s.Outgoing)[r] += amt;
        }

        /// <summary>A promise made or ended during this step (keeps the step's figures current without a recount; when they
        /// are not worked out yet this step, the next look counts it from the errand itself).</summary>
        void FlowChange(Building s, int r, float amt, bool into)
        {
            if (_flowStamp != Time || s?.Incoming == null || r < 0 || r >= s.Incoming.Length) return;
            var a = into ? s.Incoming : s.Outgoing;
            a[r] = math.max(0, a[r] + amt);
        }

        // ------------------------------------------------------------------ haulers between stores

        /// <summary>Idle haulers: the player's settings first (goods shut out or over a cap, keep-at-least, priority), then
        /// food that spoils into a granary.</summary>
        bool MoveStockDecide(Citizen c) => StoreOrderDecide(c) || KeepBetterDecide(c);

        float _ordersStamp = -1f; bool _orders;

        /// <summary>Whether the stores' settings ask for goods to be moved (the haulers are kept only then).</summary>
        public bool StoreWorkWanted => StoreOrders();

        /// <summary>Whether any store's settings could call for a move (a good shut out but still held, a limit, priorities
        /// that differ). Most settlements have none, and then idle haulers skip the search.</summary>
        bool StoreOrders()
        {
            if (_ordersStamp == Time) return _orders;
            _ordersStamp = Time;
            _orders = false;
            int pri = -1;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null) continue;
                if (s.KeepMin != null || s.KeepMax != null || pri >= 0 && (int)s.StorePriority != pri) return _orders = true;
                pri = (int)s.StorePriority;
                if (s.Allow != null) for (int r = 0; r < ResCount; r++) if (!s.Allow[r] && s.Store[r] > 0.01f) return _orders = true;
            }
            return false;
        }

        /// <summary>Room a store has for a move of a good into it: unpromised room, under the cap (none while flooded).</summary>
        float MoveRoom(Building t, int r) => t.Flooded ? 0 : RoomIn(t, r);

        /// <summary>
        /// The player's settings at work. Of each kind, the move worth most (amount over distance from the hauler), in this
        /// order: (1) goods a store no longer takes, or holds over its keep-at-most, out to a store that takes them; (2) a
        /// store below a keep-at-least filled from the nearest store with some to spare (whatever its priority); (3) goods
        /// in a store with a higher-priority store that takes them and has room, moved up. Nothing moves back: the
        /// destination always wants the good more than the store it left.
        /// </summary>
        bool StoreOrderDecide(Citizen c)
        {
            if (!StoreOrders()) return false;
            Building from = null, to = null; int res = -1; float amt = 0, best = 0;
            void Consider(Building f, Building t, int r, float a)
            {
                a = math.min(a, CarryCap(r));
                if (a < 0.5f) return;
                float score = a / (1f + math.distance(f.DoorPos, c.Pos) * 0.05f);
                if (score > best) { best = score; from = f; to = t; res = r; amt = a; }
            }

            // (1) Shut out or over the cap.
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s.Stored < 0.5f) continue;
                for (int r = 0; r < ResCount; r++)
                {
                    if (s.Store[r] < 0.5f) continue;
                    float cap = KeepMaxOf(s, r);
                    float excess = !Accepts(s, r) ? s.Store[r] : cap > 0 ? s.Store[r] - cap : 0;
                    if (excess < 0.5f) continue;
                    excess -= Outgoing(s, r);
                    if (excess < 0.5f) continue;
                    var t = NearestStorageFor(s.DoorPos, r, 0.5f, true);
                    if (t == null || t == s) continue;
                    Consider(s, t, r, math.min(excess, MoveRoom(t, r)));
                }
            }

            // (2) Fill a keep-at-least.
            if (from == null)
                foreach (var t in Buildings)
                {
                    if (!t.Active || t.Store == null || t.KeepMin == null || t.Flooded) continue;
                    for (int r = 0; r < ResCount; r++)
                    {
                        float need = KeepMinOf(t, r);
                        if (need <= 0) continue;
                        need -= t.Store[r] + Incoming(t, r);
                        if (need < 0.5f) continue;
                        float room = MoveRoom(t, r);
                        if (room < 0.5f) continue;
                        Building f = null; float fd = float.MaxValue, fs = 0;
                        foreach (var s in Buildings)
                        {
                            if (s == t || !s.Active || s.Store == null || s.Store[r] < 0.5f) continue;
                            float spare = Movable(s, r) - Outgoing(s, r);
                            if (spare < 0.5f) continue;
                            float d = math.distancesq(s.DoorPos, t.DoorPos);
                            if (d < fd) { fd = d; f = s; fs = spare; }
                        }
                        if (f != null) Consider(f, t, r, math.min(need, math.min(room, fs)));
                    }
                }

            // (3) Up the priorities.
            if (from == null)
            {
                int top = -1;
                foreach (var s in Buildings) if (s.Active && s.Store != null && !s.Flooded) top = math.max(top, (int)s.StorePriority);
                foreach (var s in Buildings)
                {
                    if (!s.Active || s.Store == null || s.Stored < 2f || (int)s.StorePriority >= top) continue;
                    for (int r = 0; r < ResCount; r++)
                    {
                        if (s.Store[r] < 2f || !Accepts(s, r)) continue;
                        float spare = Movable(s, r) - Outgoing(s, r);
                        if (spare < 2f) continue;
                        var t = NearestStorageFor(s.DoorPos, r, 0.5f, true, (int)s.StorePriority);
                        if (t == null) continue;
                        Consider(s, t, r, math.min(spare, MoveRoom(t, r)));
                    }
                }
            }

            if (from == null) return false;
            StartMove(c, from, to, res, amt);
            return true;
        }

        void StartMove(Citizen c, Building from, Building to, int r, float amt)
        {
            to.HaulReserved += amt;
            FlowChange(to, r, amt, true); FlowChange(from, r, amt, false);
            c.Task = Task.MoveStock;
            c.TaskB = from; c.TaskB2 = to; c.TaskIdx = r; c.TaskAmt = amt;
            Walk(c, from.Door, Activity.Walking);
        }

        /// <summary>
        /// Idle haulers move food that spoils from open stores into a granary with room (biggest pile first), so the harvest
        /// and the meat keep through winter: never down the priorities, never out of a reserve, never over a cap.
        /// </summary>
        bool KeepBetterDecide(Citizen c)
        {
            Building to = null; float room = 0;
            foreach (var s in Buildings)
                if (s.Active && s.Store != null && s.Def.accepts.Length > 0 && s.Def.spoilMultiplier < 1f && Free(s) - s.HaulReserved > room) { room = Free(s) - s.HaulReserved; to = s; }
            if (to == null || room < B.carryCapacity) return false;
            Building from = null; int res = -1; float most = 2f;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s == to || s.Def.spoilMultiplier <= to.Def.spoilMultiplier || s.StorePriority > to.StorePriority) continue;
                foreach (int r in _foodByPerish)
                {
                    if (!Accepts(to, r) || to.KeepMax != null && CapRoom(to, r, true) < B.carryCapacity) continue;
                    float have = s.KeepMin != null ? Movable(s, r) - Outgoing(s, r) : s.Store[r];
                    if (have > most) { most = have; from = s; res = r; }
                }
            }
            if (from == null) return false;
            float amt = math.min(math.min(CarryCap(res), most), room);
            if (to.KeepMax != null) amt = math.min(amt, CapRoom(to, res, true));
            StartMove(c, from, to, res, amt);
            return true;
        }

        /// <summary>A hauler moving a good between stores reaches the store it takes from: takes what may still leave and
        /// still fits (the settings or other haulers may have changed things on the way) and carries it on.</summary>
        void ArriveMoveStock(Citizen c)
        {
            var from = c.TaskB; var to = c.TaskB2; int r = c.TaskIdx;
            to.HaulReserved = math.max(0, to.HaulReserved - c.TaskAmt);
            FlowChange(to, r, -c.TaskAmt, true); FlowChange(from, r, -c.TaskAmt, false);
            // Only what still fits: others may have filled the store meanwhile. (Taking a full sack anyway sent haulers
            // round in circles: granary full, back to the open pile, picked up again for the granary.)
            float fit = to.Active ? Free(to) - to.HaulReserved : 0;
            if (to.KeepMax != null) fit = math.min(fit, CapRoom(to, r, true));
            float may = from.KeepMin != null ? Movable(from, r) : float.MaxValue;    // never out of a reserve
            float got = from.Active && to.Active && Accepts(to, r) && fit >= 0.5f ? TakeFrom(from, r, math.min(math.min(c.TaskAmt, fit), may)) : 0;
            c.Task = Task.None;
            c.TaskAmt = 0;
            if (got > 0)
            {
                c.CarryRes = r; c.CarryAmt = got;
                // The room stays promised on the way there.
                if (to.Active)
                {
                    c.Task = Task.DeliverOutput; c.TaskB = to; c.TaskB2 = null; c.TaskAmt = got; to.HaulReserved += got;
                    FlowChange(to, r, got, true);
                    Walk(c, to.Door, Activity.Carrying);
                }
            }
        }

        // ------------------------------------------------------------------ settings (player commands)

        /// <summary>Gives a store its settings when it is finished (everything it can hold, Normal priority, no limits). A
        /// store rebuilt as another kind keeps its settings, less limits on goods the new building cannot hold.</summary>
        void EnsureStoreSettings(Building b)
        {
            if (b.Store == null) return;
            if (b.Allow == null || b.Allow.Length != ResCount)
            {
                b.Allow = new bool[ResCount];
                for (int r = 0; r < ResCount; r++) b.Allow[r] = true;
            }
            for (int r = 0; r < ResCount; r++)
                if (!CanHold(b, r)) { if (b.KeepMin != null) b.KeepMin[r] = 0; if (b.KeepMax != null) b.KeepMax[r] = 0; }
            TrimLimits(b);
        }

        public bool IsStore(Building b) => b != null && b.Alive && b.Allow != null;

        /// <summary>Lets a good into a store or shuts it out (goods it already holds that it no longer takes are moved out).</summary>
        public void SetStoreAllow(Building b, int r, bool allow)
        {
            if (!IsStore(b) || r < 0 || r >= ResCount || !CanHold(b, r)) return;
            b.Allow[r] = allow;
            SettingsChanged();
        }

        /// <summary>Lets every good of a category (null: every good) into a store, or shuts them all out.</summary>
        public void SetStoreAllowAll(Building b, bool allow, string category = null)
        {
            if (!IsStore(b)) return;
            for (int r = 0; r < ResCount; r++)
                if (CanHold(b, r) && (category == null || Data.Resources[r].category == category)) b.Allow[r] = allow;
            SettingsChanged();
        }

        public void SetStorePriority(Building b, Priority p)
        {
            if (!IsStore(b) || p < Priority.Low || p > Priority.High) return;
            b.StorePriority = p;
            SettingsChanged();
        }

        /// <summary>Keep at least this much of a good here (0 clears it). A keep-at-most below it is raised to match.</summary>
        public void SetKeepMin(Building b, int r, float amount) => SetLimit(b, r, amount, true);

        /// <summary>Keep at most this much of a good here (0 clears it). A keep-at-least above it is lowered to match.</summary>
        public void SetKeepMax(Building b, int r, float amount) => SetLimit(b, r, amount, false);

        void SetLimit(Building b, int r, float amount, bool min)
        {
            if (!IsStore(b) || r < 0 || r >= ResCount || !CanHold(b, r) || !float.IsFinite(amount)) return;
            amount = math.clamp(math.round(amount), 0, 1e6f);
            b.KeepMin ??= new float[ResCount];
            b.KeepMax ??= new float[ResCount];
            if (min)
            {
                b.KeepMin[r] = amount;
                if (amount > 0 && b.KeepMax[r] > 0 && b.KeepMax[r] < amount) b.KeepMax[r] = amount;
            }
            else
            {
                b.KeepMax[r] = amount;
                if (amount > 0 && b.KeepMin[r] > amount) b.KeepMin[r] = amount;
            }
            TrimLimits(b);
            SettingsChanged();
        }

        /// <summary>A store's settings changed: the haulers are looked at again (kept only while settings ask for moves).</summary>
        void SettingsChanged() { _ordersStamp = -1f; _jobsDirty = true; }

        /// <summary>No limits set: no arrays (so a store without limits costs nothing and saves as before).</summary>
        static void TrimLimits(Building b)
        {
            static bool None(float[] a) { if (a == null) return true; foreach (var v in a) if (v > 0) return false; return true; }
            if (None(b.KeepMin)) b.KeepMin = null;
            if (None(b.KeepMax)) b.KeepMax = null;
        }

        /// <summary>A copy of a store's settings (for copy and paste). Null if it is not a store.</summary>
        public StoreSettings GetStoreSettings(Building b)
        {
            if (!IsStore(b)) return null;
            return new StoreSettings
            {
                Allow = (bool[])b.Allow.Clone(), Priority = b.StorePriority,
                KeepMin = (float[])b.KeepMin?.Clone(), KeepMax = (float[])b.KeepMax?.Clone(),
            };
        }

        /// <summary>Pastes settings onto a store: its priority, and for every good it can hold, whether it takes it and its
        /// limits (a granary pasted from an open pile takes the pile's choice of food and nothing else).</summary>
        public void ApplyStoreSettings(Building b, StoreSettings st)
        {
            if (!IsStore(b) || st?.Allow == null || st.Allow.Length != ResCount) return;
            b.StorePriority = st.Priority;
            b.KeepMin = null; b.KeepMax = null;
            for (int r = 0; r < ResCount; r++)
            {
                if (!CanHold(b, r)) continue;
                b.Allow[r] = st.Allow[r];
                if (st.KeepMin != null && st.KeepMin[r] > 0) SetKeepMin(b, r, st.KeepMin[r]);
                if (st.KeepMax != null && st.KeepMax[r] > 0) SetKeepMax(b, r, st.KeepMax[r]);
            }
            SettingsChanged();
        }

        /// <summary>How many of the goods a store can hold it takes, and how many it can hold.</summary>
        public (int takes, int holds) StoreTakes(Building b)
        {
            int t = 0, h = 0;
            if (b?.Store == null) return (0, 0);
            for (int r = 0; r < ResCount; r++) if (CanHold(b, r)) { h++; if (Accepts(b, r)) t++; }
            return (t, h);
        }

        /// <summary>Whether any working store takes this good (whatever its room).</summary>
        public bool AnyStoreTakes(int r)
        {
            foreach (var s in Buildings) if (s.Active && s.Store != null && Accepts(s, r)) return true;
            return false;
        }

        // ------------------------------------------------------------------ warning

        readonly List<string> _shutOut = new();

        /// <summary>Goods no store takes (the settings shut them out everywhere) pile up where they are made.</summary>
        void ShutOutWarning()
        {
            _shutOut.Clear();
            for (int r = 0; r < ResCount; r++)
            {
                float waiting = 0;
                foreach (var b in Buildings) if (b.Active && b.Store == null) waiting += b.Out[r];
                if (waiting >= 1f && !AnyStoreTakes(r)) _shutOut.Add(Data.Resources[r].name.ToLower());
            }
            if (_shutOut.Count == 0) return;
            Warnings.Add($"No store takes {string.Join(", ", _shutOut)}: it piles up where it is made. Select a store and let it in (Storage settings).");
        }
    }
}
