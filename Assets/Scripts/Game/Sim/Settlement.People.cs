using System;
using System.Collections.Generic;
using System.Linq;
using Firstlight.Core;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Citizens: walking, errands, jobs, needs and population.
    public sealed partial class Settlement
    {
        /// <summary>People walking off the map after deciding to leave (no longer counted; drawn until they reach the edge).</summary>
        public readonly List<Citizen> Departing = new();
        readonly List<Building> _demand = new();
        readonly List<Citizen> _free = new();
        readonly List<Citizen> _order = new();

        /// <summary>Debug/perf only: adds adults around the campfire, with food and room for them.</summary>
        public void DebugSpawn(int count)
        {
            if (TownCenter == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f, r = 2f + (i % 7);
                NewCitizen(_rng.Next(2) == 0, R(B.fertileMin, B.elderAge), TownCenter.Center + new float2(math.sin(a), math.cos(a)) * r);
            }
            TownCenter.Store[_foodByPerish[0]] += count * 10f;
            TownCenter.Stored += count * 10f;
            RefreshStock();
            _jobsDirty = true;
        }

        Citizen NewCitizen(bool female, float age, float2 pos)
        {
            var list = female ? B.namesFemale : B.namesMale;
            var c = new Citizen
            {
                Id = NewId(), Female = female, Age = age, Pos = pos,
                Name = list != null && list.Length > 0 ? list[_rng.Next(list.Length)] : (female ? "Woman " : "Man ") + _nextId,
                Happiness = 0.75f,
            };
            Citizens.Add(c);
            return c;
        }

        bool IsAdult(Citizen c) => c.Age >= B.adultAge;

        /// <summary>How much of a good one person carries in a trip (grain goes in sacks, so more of it).</summary>
        public float CarryCap(int r) => B.carryCapacity * (r >= 0 ? math.max(0.2f, Data.Resources[r].carry) : 1f);
        public bool IsElder(Citizen c) => c.Age >= B.elderAge;

        // ------------------------------------------------------------------ per-step update

        void UpdateCitizen(Citizen c, bool work)
        {
            if (c.Gone) return;
            // Anything the errand points at may have been pulled down.
            if ((c.TaskB != null && !c.TaskB.Alive) || (c.TaskB2 != null && !c.TaskB2.Alive)) { CancelTask(c); }
            if (c.Target >= 0)
            {
                if (!Move(c)) return;
                c.Target = -1;
                Arrive(c);
                return;
            }
            Act(c, work);
        }

        void UpdateDeparting()
        {
            for (int i = Departing.Count - 1; i >= 0; i--)
                if (Departing[i].Target < 0 || Move(Departing[i])) Departing.RemoveAt(i);
        }

        void Walk(Citizen c, int cell, Activity activity)
        {
            c.Target = cell;
            c.Stuck = 0;
            c.Activity = activity;
        }

        /// <summary>One step along the flow field towards the target cell. True on arrival.</summary>
        bool Move(Citizen c)
        {
            int cur = Map.CellAt(c.Pos);
            var tpos = new float2(c.Target % Map.W + 0.5f, c.Target / Map.W + 0.5f);
            float speed = B.walkSpeed * Step * (Map.WaterClass[cur] == 1 ? 0.5f : Map.Path[cur] ? B.pathSpeed : 1f) * (c.Sick ? 0.6f : 1f) * (c.Carrying ? 0.85f : 1f);
            float2 goal;
            if (cur == c.Target) goal = tpos;
            else
            {
                int slot = Map.Flow.Resolve(c.Target);
                byte code = Map.Flow.Fields[slot * Map.Flow.Cells + cur];
                if (code == FlowDir.None || code == FlowDir.Target)
                {
                    // No path (cut off by water or buildings): walk straight, and give up after a while.
                    goal = tpos;
                    c.Stuck += Step;
                    if (c.Stuck > 30f) { c.Pos = tpos; return true; }
                }
                else
                {
                    int2 xy = Map.XY(cur) + FlowDir.Offset(code);
                    goal = new float2(xy.x + 0.5f, xy.y + 0.5f);
                }
            }
            var delta = goal - c.Pos;
            float len = math.length(delta);
            if (len > 1e-4f) c.Facing = math.atan2(delta.x, delta.y);
            if (len <= speed) c.Pos = goal;
            else c.Pos += delta / len * speed;
            return cur == c.Target && math.distancesq(c.Pos, tpos) < 1e-4f;
        }

        // ------------------------------------------------------------------ errands

        void Arrive(Citizen c)
        {
            switch (c.Task)
            {
                case Task.GoHome:
                    c.Task = Task.Rest;
                    c.Activity = c.Sick ? Activity.Sick : Activity.Resting;
                    break;
                case Task.GoWork:
                    c.Task = Task.Work;
                    break;
                case Task.GoField:
                    c.Task = Task.Work;
                    c.TaskAmt = 0;
                    c.AtField = true;
                    if (c.Job != null && c.Job.Def.crop) StepIntoField(c, c.Job);
                    else c.Facing = FacingToFeature(c);
                    break;
                case Task.FieldReturn:
                {
                    // Into the store if it still has room, else (no room anywhere) onto the workplace's pile.
                    var s = c.TaskB;
                    if (s != null && c.TaskAmt > 0) s.HaulReserved = math.max(0, s.HaulReserved - c.TaskAmt);
                    c.TaskAmt = 0;
                    for (int r = 0; r < ResCount && c.Load != null; r++)
                    {
                        if (c.Load[r] <= 0) continue;
                        if (s != null && s.Active && s.Store != null) { if (Accepts(s, r)) c.Load[r] -= PutInto(s, r, math.min(c.Load[r], math.max(0, Room(s)))); }
                        else if (s == null && c.TaskB2 != null && c.TaskB2.Active) { c.TaskB2.Out[r] += c.Load[r]; c.TaskB2.OutTotal += c.Load[r]; c.Load[r] = 0; }
                    }
                    RecountLoad(c);
                    c.Task = Task.None;
                    c.TaskB = c.TaskB2 = null;
                    break;
                }
                case Task.GoBuild:
                    c.Task = Task.Build;
                    break;
                case Task.MoveStock:
                {
                    // At the open store: take the food and carry it on to the granary.
                    var to = c.TaskB2;
                    to.HaulReserved = math.max(0, to.HaulReserved - c.TaskAmt);
                    // Only what still fits: others may have filled the granary meanwhile. (Taking a full sack anyway
                    // sent haulers round in circles: granary full, back to the open pile, picked up again for the granary.)
                    float fit = to.Active ? Free(to) - to.HaulReserved : 0;
                    float got = c.TaskB.Active && fit >= 0.5f ? TakeFrom(c.TaskB, c.TaskIdx, math.min(c.TaskAmt, fit)) : 0;
                    c.Task = Task.None;
                    c.TaskAmt = 0;
                    if (got > 0)
                    {
                        c.CarryRes = c.TaskIdx; c.CarryAmt = got;
                        // The room stays promised on the way there.
                        if (to.Active) { c.Task = Task.DeliverOutput; c.TaskB = to; c.TaskB2 = null; c.TaskAmt = got; to.HaulReserved += got; Walk(c, to.Door, Activity.Carrying); }
                    }
                    break;
                }
                case Task.DeliverOutput:
                {
                    var s = c.TaskB;
                    if (s != null && c.TaskAmt > 0) s.HaulReserved = math.max(0, s.HaulReserved - c.TaskAmt);
                    c.TaskAmt = 0;
                    // Only into room nobody else is on the way to fill (taking more than was promised here sent the others
                    // round the stores for days).
                    if (s != null && s.Active && s.Store != null && Accepts(s, c.CarryRes)) c.CarryAmt -= PutInto(s, c.CarryRes, math.min(c.CarryAmt, math.max(0, Room(s))));
                    if (!c.Carrying) c.CarryRes = -1;
                    c.Task = Task.None;     // anything left over: Decide finds another store
                    c.TaskB = null;
                    break;
                }
                case Task.FetchInput:
                {
                    float got = c.TaskB != null && c.TaskB.Active ? TakeFrom(c.TaskB, c.TaskIdx, c.TaskAmt) : 0;
                    if (got > 0 && c.TaskB2 != null && c.TaskB2.Active)
                    {
                        c.CarryRes = c.TaskIdx; c.CarryAmt = got;
                        c.Task = Task.DeliverInput;
                        Walk(c, c.TaskB2.Door, Activity.Carrying);
                    }
                    else
                    {
                        if (got > 0) { c.CarryRes = c.TaskIdx; c.CarryAmt = got; }
                        c.Task = Task.None;
                    }
                    break;
                }
                case Task.DeliverInput:
                    if (c.TaskB2 != null && c.TaskB2.Active) { c.TaskB2.In[c.CarryRes] += c.CarryAmt; c.CarryAmt = 0; c.CarryRes = -1; }
                    c.Task = Task.None;
                    break;
                case Task.FetchMaterial:
                {
                    var site = c.TaskB2;
                    if (!site.UnderWork || c.TaskIdx >= site.InTransit.Length) { c.Task = Task.None; break; }
                    site.InTransit[c.TaskIdx] -= c.TaskAmt;
                    float still = site.BuildDef.cost[c.TaskIdx].amount - site.Delivered[c.TaskIdx] - site.InTransit[c.TaskIdx];
                    float got = c.TaskB.Active ? TakeAny(c.TaskB, site.CostRes[c.TaskIdx], math.min(c.TaskAmt, still)) : 0;
                    c.Task = Task.None;
                    if (got > 0)
                    {
                        c.CarryRes = site.CostRes[c.TaskIdx]; c.CarryAmt = got;
                        site.InTransit[c.TaskIdx] += got;
                        c.Task = Task.DeliverMaterial;
                        Walk(c, site.Door, Activity.Carrying);
                    }
                    break;
                }
                case Task.DeliverMaterial:
                {
                    var site = c.TaskB2;
                    if (site.UnderWork && c.TaskIdx < site.Delivered.Length && site.CostRes[c.TaskIdx] == c.CarryRes)
                    {
                        site.InTransit[c.TaskIdx] -= c.CarryAmt;
                        var bd = site.BuildDef;
                        site.Delivered[c.TaskIdx] += c.CarryAmt;
                        // Snap away float dust so a site is never left a hair short of its materials.
                        if (bd.cost[c.TaskIdx].amount - site.Delivered[c.TaskIdx] < MaterialTolerance) site.Delivered[c.TaskIdx] = bd.cost[c.TaskIdx].amount;
                        if (math.abs(site.InTransit[c.TaskIdx]) < 1e-3f) site.InTransit[c.TaskIdx] = 0;
                        c.CarryAmt = 0; c.CarryRes = -1;
                        if (site.MaterialsComplete && bd.buildWork <= 0) { if (site.IsSite) Complete(site); else CompleteUpgrade(site); }
                    }
                    c.Task = Task.None;
                    break;
                }
                case Task.HaulPickup:
                {
                    var b = c.TaskB;
                    b.HaulReserved = math.max(0, b.HaulReserved - c.TaskAmt);
                    c.Task = Task.None;
                    if (b.Active) TakeOutput(c, b);
                    break;
                }
                case Task.Leave:
                    c.Gone = true;
                    break;
                case Task.Arrive:
                    c.Task = Task.None;
                    _jobsDirty = true;
                    break;
                case Task.Idle:
                    c.Activity = IsAdult(c) ? Activity.Idle : Activity.Playing;
                    c.TaskAmt = 0;
                    break;
                default:
                    c.Task = Task.None;
                    break;
            }
        }

        /// <summary>Undoes any reservation of the current errand and stops walking.</summary>
        void CancelTask(Citizen c)
        {
            switch (c.Task)
            {
                case Task.FetchMaterial: if (c.TaskB2 != null && c.TaskIdx < c.TaskB2.InTransit.Length) c.TaskB2.InTransit[c.TaskIdx] -= c.TaskAmt; break;
                case Task.DeliverMaterial: if (c.TaskB2 != null && c.TaskIdx < c.TaskB2.InTransit.Length) c.TaskB2.InTransit[c.TaskIdx] -= c.CarryAmt; break;
                case Task.HaulPickup: if (c.TaskB != null) c.TaskB.HaulReserved = math.max(0, c.TaskB.HaulReserved - c.TaskAmt); break;
                case Task.MoveStock: if (c.TaskB2 != null) c.TaskB2.HaulReserved = math.max(0, c.TaskB2.HaulReserved - c.TaskAmt); break;
                case Task.FieldReturn:
                case Task.DeliverOutput: if (c.TaskB != null && c.TaskAmt > 0) { c.TaskB.HaulReserved = math.max(0, c.TaskB.HaulReserved - c.TaskAmt); c.TaskAmt = 0; } break;
            }
            if (c.Task == Task.Leave || c.Task == Task.Arrive) return;
            c.AtField = false;
            c.Task = Task.None;
            c.TaskB = c.TaskB2 = null;
            c.Target = -1;
        }

        /// <summary>The steady part of an errand (resting, working, building, idling); re-decides when it no longer fits.</summary>
        void Act(Citizen c, bool work)
        {
            if (work && c.Job != null && IsWorkTime) WorkerSeconds += Step;
            switch (c.Task)
            {
                case Task.Rest:
                    if (work && !c.Sick) Decide(c, work);
                    return;
                case Task.Work:
                    WorkAct(c, work);
                    return;
                case Task.Build:
                    BuildAct(c, work);
                    return;
                case Task.Idle:
                    c.TaskAmt += Step;
                    if (c.TaskAmt > 3f || !work) { c.TaskAmt = 0; Decide(c, work); }
                    return;
                default:
                    Decide(c, work);
                    return;
            }
        }

        /// <summary>Chooses the next errand.</summary>
        void Decide(Citizen c, bool work)
        {
            CancelTask(c);
            if (c.Carrying) { GoDeliver(c); return; }
            // A field worker carries a full load (or whatever it has at the end of the shift) back to storage.
            if (c.LoadTotal > 0.01f && (!work || c.Sick || c.Job == null || !c.Job.Def.fieldWork || c.Job.Paused || c.LoadTotal >= CarryCap(c.LoadRes) - 0.01f))
            {
                if (ReturnLoad(c)) return;
            }
            bool adult = IsAdult(c);
            if (c.Sick || !work || c.DisputeDays > 0) { GoHome(c); return; }
            if (!adult) { GoIdle(c, Activity.Playing); return; }
            if (c.Job != null) { WorkerDecide(c); return; }
            if (c.IsBuilder) { if (BuilderDecide(c) || HaulDecide(c) || MoveStockDecide(c)) return; }
            else if (HaulDecide(c) || BuilderDecide(c) || MoveStockDecide(c)) return;
            GoIdle(c, Activity.Idle);
        }

        void GoHome(Citizen c)
        {
            var home = c.Home ?? TownCenter;
            c.Task = Task.GoHome;
            c.TaskB = null;
            Walk(c, home.Door, Activity.Walking);
        }

        void GoIdle(Citizen c, Activity a)
        {
            c.Task = Task.Idle;
            c.TaskAmt = 0;
            // Wait around the campfire, each at a slightly different spot so they don't stack.
            var tc = TownCenter;
            int2 xy = Map.XY(tc.Door) + new int2((c.Id % 5) - 2, -(c.Id / 5 % 2));
            int cell = Map.InBounds(xy.x, xy.y) && Map.Flow.Cost[Map.Cell(xy.x, xy.y)] != 0 ? Map.Cell(xy.x, xy.y) : tc.Door;
            Walk(c, cell, Activity.Walking);
            c.Activity = a == Activity.Playing ? Activity.Playing : Activity.Walking;
        }

        void GoDeliver(Citizen c)
        {
            // Room others are already carrying goods to doesn't count: a crowd of carriers all heading for the same last
            // gap used to find it gone on arrival, again and again, for days.
            var s = NearestStorageFor(c.Pos, c.CarryRes, 0.5f, true);
            if (s == null && NearestStorageFor(c.Pos, c.CarryRes, 0.5f) != null) { c.Task = Task.Idle; c.TaskAmt = 0; c.Activity = Activity.Idle; return; }   // wait: it is all promised
            if (s == null)
            {
                // No store will take it: put it down on a workplace's pile (it can be fetched from there) rather than
                // stand holding it, unable to do anything else (builders holding goods could never build the store
                // that would end the jam). With no workplace to leave it at, it is lost.
                DropCarried(c);
                c.Task = Task.Idle; c.TaskAmt = 0; c.Activity = Activity.Idle;
                return;
            }
            c.Task = Task.DeliverOutput;
            c.TaskB = s;
            c.TaskAmt = math.min(c.CarryAmt, Free(s) - s.HaulReserved);     // the room is promised on the way
            s.HaulReserved += c.TaskAmt;
            Walk(c, s.Door, Activity.Carrying);
        }

        void DropCarried(Citizen c)
        {
            int r = c.CarryRes;
            Building best = null; float bd = float.MaxValue;
            foreach (var b in Buildings)
            {
                if (!b.Active || b.Store != null || !b.Def.IsWorkplace || Array.IndexOf(b.OutRes, r) < 0 && b != c.Job) continue;
                float d = math.distancesq(b.DoorPos, c.Pos);
                if (d < bd) { bd = d; best = b; }
            }
            if (best != null) { best.Out[r] += c.CarryAmt; best.OutTotal += c.CarryAmt; }
            else Consumed[r] += c.CarryAmt;
            c.CarryAmt = 0; c.CarryRes = -1;
        }

        bool HasStorageRoom() => NearestStorageFor(TownCenter.Center, -1, 1f) != null;

        bool[] _room; float _roomStamp = -1f;

        /// <summary>
        /// Whether some store would take this good (refreshed once per step). Room in a granary is no use for wood: a
        /// hauler who picked up a good no store takes stood holding it, doing nothing else, until a store had room.
        /// </summary>
        bool RoomFor(int r)
        {
            if (_room == null || _roomStamp != Time)
            {
                _room ??= new bool[ResCount];
                _roomStamp = Time;
                for (int k = 0; k < ResCount; k++) _room[k] = false;
                foreach (var s in Buildings)
                {
                    if (!s.Active || s.Store == null || s.Flooded || Free(s) - s.HaulReserved < 1f) continue;
                    for (int k = 0; k < ResCount; k++) if (!_room[k] && Accepts(s, k)) _room[k] = true;
                }
            }
            return r >= 0 && r < ResCount && _room[r];
        }

        /// <summary>A workplace has finished goods waiting that some store would take.</summary>
        bool HasRoomFor(Building b)
        {
            for (int r = 0; r < ResCount; r++) if (b.Out[r] >= 0.05f && RoomFor(r)) return true;
            return false;
        }

        void TakeOutput(Citizen c, Building b)
        {
            int best = -1; float most = 0;
            for (int r = 0; r < ResCount; r++) if (b.Out[r] > most && RoomFor(r)) { most = b.Out[r]; best = r; }
            if (best < 0 || most < 0.05f) return;
            float amt = math.min(CarryCap(best), most);
            b.Out[best] -= amt; b.OutTotal -= amt;
            c.CarryRes = best; c.CarryAmt = amt;
            GoDeliver(c);
        }

        void WorkerDecide(Citizen c)
        {
            var b = c.Job;
            EnsureTool(c, b.Def.usesTools);
            if (!WantsWorkers(b)) { c.Task = Task.GoWork; c.TaskB = b; Walk(c, b.Door, Activity.Walking); return; }
            int r = InputToFetch(b, out float amt);
            if (r >= 0)
            {
                var s = NearestStorageWith(b.DoorPos, r);
                if (s != null)
                {
                    c.Task = Task.FetchInput;
                    c.TaskB = s; c.TaskB2 = b; c.TaskIdx = r; c.TaskAmt = B.carryCapacity;
                    Walk(c, s.Door, Activity.Fetching);
                    return;
                }
            }
            if (b.Def.fieldWork)
            {
                int spot = FieldSpot(b, out int feature);
                if (spot >= 0)
                {
                    c.Task = Task.GoField;
                    c.TaskB = b;
                    c.FieldFeature = feature;
                    Walk(c, spot, Activity.Walking);
                    return;
                }
            }
            c.Task = Task.GoWork;
            c.TaskB = b;
            Walk(c, b.Door, Activity.Walking);
        }

        // ------------------------------------------------------------------ field work

        readonly int[] _spotCells = new int[6];
        readonly float[] _spotDist = new float[6];

        /// <summary>
        /// Where a field worker goes: a walkable cell next to one of the few trees / rocks nearest the workplace door
        /// (chosen at random so the crew spreads out), or a spot a few cells short of the nearest herd for hunters.
        /// -1 if there is nothing left in reach. <paramref name="feature"/> = the tree/rock cell or the herd index.
        /// </summary>
        int FieldSpot(Building b, out int feature)
        {
            feature = -1;
            var n = b.Def.nearby;
            if (b.Def.crop) return CropSpot(b, out feature);
            if (n == null) return -1;
            if (n.feature == "wildlife")
            {
                float bd = float.MaxValue;
                for (int h = 0; h < Map.Herds.Count; h++)
                {
                    float d = math.distance(Map.Herds[h].Pos, b.Center);
                    if (Map.Herds[h].Pop >= 1f && d <= n.radius && d < bd) { bd = d; feature = h; }
                }
                if (feature < 0) return -1;
                var hp = Map.Herds[feature].Pos;
                var at = hp + math.normalizesafe(b.DoorPos - hp) * 3f + new float2(R(-1.5f, 1.5f), R(-1.5f, 1.5f));
                return WalkableNear(Map.CellAt(at), 3);
            }
            if (n.feature != "trees" && n.feature != "rocks") return -1;
            bool trees = n.feature == "trees";
            int count = 0;
            int x0 = math.max(1, (int)(b.Center.x - n.radius)), x1 = math.min(Map.W - 2, (int)(b.Center.x + n.radius));
            int y0 = math.max(1, (int)(b.Center.y - n.radius)), y1 = math.min(Map.H - 2, (int)(b.Center.y + n.radius));
            float r2 = n.radius * n.radius;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = Map.Cell(x, y);
                    if (trees ? !Map.Tree[c] : Map.Rock[c] <= 0) continue;
                    var p = new float2(x + 0.5f, y + 0.5f);
                    if (math.distancesq(p, b.Center) > r2) continue;
                    // Keep the nearest few (by distance from the door: that is the walk).
                    float d = math.distancesq(p, b.DoorPos);
                    int k;
                    if (count < _spotCells.Length) k = count++;
                    else if (d >= _spotDist[count - 1]) continue;
                    else k = count - 1;
                    while (k > 0 && _spotDist[k - 1] > d) { _spotCells[k] = _spotCells[k - 1]; _spotDist[k] = _spotDist[k - 1]; k--; }
                    _spotCells[k] = c; _spotDist[k] = d;
                }
            if (count == 0) return -1;
            feature = _spotCells[_rng.Next(count)];
            // Stand beside it, on the side nearest the workplace.
            int2 fxy = Map.XY(feature);
            int best = -1; float bestD = float.MaxValue;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = fxy.x + dx, ny = fxy.y + dy;
                    if (!Map.InBounds(nx, ny)) continue;
                    int c = Map.Cell(nx, ny);
                    if (Map.Flow.Cost[c] == 0 || Map.Tree[c] || Map.Reserved[c]) continue;
                    float d = math.distancesq(new float2(nx + 0.5f, ny + 0.5f), b.DoorPos);
                    if (d < bestD) { bestD = d; best = c; }
                }
            // In thick forest every neighbour may be a tree: stand on the tree's own cell (trees are walkable).
            if (best < 0) best = trees ? feature : WalkableNear(feature, 2);
            return best;
        }

        /// <summary>Nearest walkable cell to a cell, searching outwards up to <paramref name="reach"/> rings (-1 if none).</summary>
        int WalkableNear(int cell, int reach)
        {
            int2 xy = Map.XY(cell);
            for (int r = 0; r <= reach; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dy)) != r) continue;
                        int x = xy.x + dx, y = xy.y + dy;
                        if (!Map.InBounds(x, y)) continue;
                        int c = Map.Cell(x, y);
                        if (Map.Flow.Cost[c] != 0 && !Map.Reserved[c]) return c;
                    }
            return -1;
        }

        /// <summary>Yaw from a field worker towards what it works on.</summary>
        float FacingToFeature(Citizen c)
        {
            float2 at;
            if (c.Job != null && c.Job.Def.nearby?.feature == "wildlife") at = c.FieldFeature >= 0 && c.FieldFeature < Map.Herds.Count ? Map.Herds[c.FieldFeature].Pos : c.Pos;
            else if (c.FieldFeature >= 0) at = new float2(c.FieldFeature % Map.W + 0.5f, c.FieldFeature / Map.W + 0.5f);
            else return c.Facing;
            var d = at - c.Pos;
            return math.lengthsq(d) > 1e-4f ? math.atan2(d.x, d.y) : c.Facing;
        }

        /// <summary>The tree or rock a field worker was at has gone (felled or emptied by someone else).</summary>
        bool FieldGone(Citizen c)
        {
            if (c.FieldFeature < 0 || c.Job == null) return false;
            string f = c.Job.Def.nearby?.feature;
            if (f == "trees") return !Map.Tree[c.FieldFeature];
            if (f == "rocks") return Map.Rock[c.FieldFeature] <= 0;
            if (f == "wildlife") return c.FieldFeature >= Map.Herds.Count || Map.Herds[c.FieldFeature].Pop < 1f;
            return false;
        }

        /// <summary>
        /// Sends a field worker with a load back: to its own workplace if that is nearer than any store with room
        /// (idle people haul it on from there, as with every workplace's goods), else to the store.
        /// </summary>
        bool ReturnLoad(Citizen c)
        {
            var s = NearestStorageFor(c.Pos, c.LoadRes, 0.5f, true);
            var job = c.Job != null && c.Job.Active ? c.Job : null;
            if (s == null && job == null) return false;
            // Farmers work inside their field, so its door is always nearest: they take the harvest to the store.
            if (s != null && job != null && !job.Def.crop && math.distancesq(job.DoorPos, c.Pos) < math.distancesq(s.DoorPos, c.Pos)) s = null;
            c.Task = Task.FieldReturn;
            c.TaskB = s; c.TaskB2 = s == null ? job : null;
            // Like every delivery, the room is promised on the way (field loads used to fill gaps others were walking to).
            c.TaskAmt = s != null ? math.max(0, math.min(c.LoadTotal, Free(s) - s.HaulReserved)) : 0;
            if (s != null) s.HaulReserved += c.TaskAmt;
            Walk(c, s != null ? s.Door : job.Door, Activity.Carrying);
            return true;
        }

        void RecountLoad(Citizen c)
        {
            float total = 0, most = 0; int main = -1;
            if (c.Load != null)
                for (int r = 0; r < ResCount; r++)
                {
                    if (c.Load[r] < 1e-4f) { c.Load[r] = 0; continue; }
                    total += c.Load[r];
                    if (c.Load[r] > most) { most = c.Load[r]; main = r; }
                }
            c.LoadTotal = total; c.LoadRes = main;
        }

        void WorkAct(Citizen c, bool work)
        {
            var b = c.Job;
            if (b == null || b != c.TaskB || !b.Active || b.Paused) { Decide(c, work); return; }
            if (!work)
            {
                // End of the shift: take finished goods along on the way home, once a day.
                if (c.ShiftDay != Day && b.OutTotal >= B.workHaulMin && HasRoomFor(b)) { c.ShiftDay = Day; TakeOutput(c, b); return; }
                Decide(c, work);
                return;
            }
            if (!IsWorkTime) { c.Activity = Activity.Idle; return; }      // came early: wait for the shift to start
            bool field = c.AtField && b.Def.fieldWork;
            if (field && FieldGone(c)) { Decide(c, work); return; }   // on to another tree / rock / herd
            // Farmers move along the rows now and then.
            if (field && b.Def.crop && (c.TaskAmt += Step) > 6f) { c.TaskAmt = 0; StepIntoField(c, b); }
            if (!field && b.OutTotal >= B.carryCapacity && HasRoomFor(b)) { TakeOutput(c, b); return; }
            ProductiveSeconds += Step;
            if (b.Def.usesTools && c.Tool < 0) EnsureTool(c, true);   // tools may have come in since the shift began
            float speed = WorkSpeed(c, b.Def.usesTools, b.Def.requiresTools);
            if (speed <= 0) { b.Status = "Needs tools"; c.Activity = Activity.Idle; return; }
            bool did = WorkStep(c, b, speed, field);
            c.Activity = did ? Activity.Working : Activity.Idle;
            if (did && b.Def.usesTools) WearTool(c);
            if (field && c.LoadTotal >= CarryCap(c.LoadRes) - 0.01f) { Decide(c, work); return; }
            if (!did && b.Def.inputs.Length > 0 && InputToFetch(b, out _) >= 0) Decide(c, work);
        }

        float WorkSpeed(Citizen c, bool usesTools, bool requiresTools)
        {
            float s = math.lerp(B.workSpeedMin, 1f, math.saturate(c.Happiness / 0.6f));
            if (c.NoWater) s *= B.noWaterWorkSpeed;          // time lost fetching water from afar
            if (!usesTools) return s;
            if (c.Tool >= 0) return s * Data.Resources[c.Tool].toolSpeed;
            return requiresTools ? 0f : s * B.noToolWorkSpeed;
        }

        /// <summary>
        /// A tool-using worker without a tool takes the best one in stock; one holding a worse tier swaps up when a
        /// better tier is in stock (the old tool goes back to store if it has more than half its life left).
        /// </summary>
        void EnsureTool(Citizen c, bool uses)
        {
            if (!uses) return;
            float have = c.Tool >= 0 ? Data.Resources[c.Tool].toolSpeed : 0f;
            int better = -1;
            Building s = null;
            // Best tier first; a tier whose stock is scattered in part-tools over several stores is skipped.
            foreach (int r in _toolsBest)
            {
                if (Data.Resources[r].toolSpeed <= have) break;
                if (StockTotals[r] < 1f) continue;
                s = NearestStorageWith(c.Pos, r, 1f);
                if (s != null) { better = r; break; }
            }
            if (better < 0 || TakeFrom(s, better, 1f) < 1f) return;
            if (c.Tool >= 0)
            {
                if (c.ToolLife > B.toolWearDays * 0.5f) Deposit(c.Tool, 1f, c.Pos);
                else Consumed[c.Tool] += 1f;
                ToolSwaps++;
            }
            c.Tool = better;
            c.ToolLife = B.toolWearDays;
        }

        public int ToolSwaps { get; private set; }

        void WearTool(Citizen c)
        {
            if (c.Tool < 0) return;
            c.ToolLife -= Step / WorkdaySeconds * Effect("wear.tools");
            if (c.ToolLife <= 0) { c.Tool = -1; c.ToolLife = 0; }
        }

        /// <summary>
        /// Rebuilds the reservations (materials on their way to sites, loads promised to haulers) from what people are
        /// actually doing, so a missed release or float drift can never leave a site waiting forever.
        /// </summary>
        void RecountReservations()
        {
            foreach (var b in Buildings) { if (b.InTransit != null) Array.Clear(b.InTransit, 0, b.InTransit.Length); b.HaulReserved = 0; }
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                if (c.Task == Task.FetchMaterial && c.TaskB2 != null) c.TaskB2.InTransit[c.TaskIdx] += c.TaskAmt;
                else if (c.Task == Task.DeliverMaterial && c.TaskB2 != null) c.TaskB2.InTransit[c.TaskIdx] += c.CarryAmt;
                else if (c.Task == Task.HaulPickup && c.TaskB != null) c.TaskB.HaulReserved += c.TaskAmt;
                else if (c.Task == Task.MoveStock && c.TaskB2 != null) c.TaskB2.HaulReserved += c.TaskAmt;
                else if ((c.Task == Task.DeliverOutput || c.Task == Task.FieldReturn) && c.TaskB != null && c.TaskAmt > 0) c.TaskB.HaulReserved += c.TaskAmt;
            }
        }

        /// <summary>Builders: carry missing materials to the oldest site that can use them, else build one whose materials are in.</summary>
        bool BuilderDecide(Citizen c)
        {
            foreach (var site in Buildings)
            {
                if (!site.UnderWork) continue;
                var bd = site.BuildDef;
                if (!site.MaterialsComplete)
                {
                    for (int i = 0; i < bd.cost.Length; i++)
                    {
                        float remaining = bd.cost[i].amount - site.Delivered[i] - site.InTransit[i];
                        if (remaining < MaterialTolerance) continue;
                        // From a store, or straight from where it is made when no store has any (so full stores can't
                        // leave a new storage pile waiting for the wood piled at the woodcutter's).
                        var s = NearestStorageWith(site.DoorPos, site.CostRes[i]) ?? NearestOutputOf(site.DoorPos, site.CostRes[i]);
                        if (s == null) continue;
                        float have = s.Store != null ? s.Store[site.CostRes[i]] : s.Out[site.CostRes[i]] - s.HaulReserved;
                        float amt = math.min(CarryCap(site.CostRes[i]), math.min(remaining, have));
                        site.InTransit[i] += amt;
                        c.Task = Task.FetchMaterial;
                        c.TaskB = s; c.TaskB2 = site; c.TaskIdx = i; c.TaskAmt = amt;
                        Walk(c, s.Door, Activity.Fetching);
                        return true;
                    }
                    continue;
                }
                if (B.buildersUseTools) EnsureTool(c, true);
                c.Task = Task.GoBuild;
                c.TaskB2 = site;
                Walk(c, site.Door, Activity.Walking);
                return true;
            }
            return false;
        }

        void BuildAct(Citizen c, bool work)
        {
            var site = c.TaskB2;
            if (!work || site == null || !site.UnderWork) { Decide(c, work); return; }
            float speed = WorkSpeed(c, B.buildersUseTools, false);
            site.Work += B.builderWorkPerDay * Step / WorkdaySeconds * speed * Effect("build.speed") / math.max(0.05f, site.BuildDef.buildWork);
            c.Activity = Activity.Building;
            if (B.buildersUseTools) WearTool(c);
            if (site.Work >= 1f) { if (site.IsSite) Complete(site); else CompleteUpgrade(site); Decide(c, work); }
        }

        /// <summary>A workplace with at least a little of a good waiting in its output pile (nearest first).</summary>
        Building NearestOutputOf(float2 from, int r)
        {
            Building best = null; float bd = float.MaxValue;
            foreach (var b in Buildings)
            {
                if (!b.Active || b.Store != null || b.Out[r] - b.HaulReserved < 0.5f) continue;
                float d = math.distancesq(b.DoorPos, from);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        /// <summary>Takes a good from a store, or from a workplace's output pile.</summary>
        float TakeAny(Building b, int r, float amt)
        {
            if (b.Store != null) return TakeFrom(b, r, amt);
            float take = math.min(amt, b.Out[r]);
            if (take <= 0) return 0;
            b.Out[r] -= take; b.OutTotal -= take;
            return take;
        }

        /// <summary>Unassigned people carry finished goods from workplaces to storage.</summary>
        bool HaulDecide(Citizen c)
        {
            if (!HasStorageRoom()) return false;
            Building best = null; float bestScore = 0;
            foreach (var b in Buildings)
            {
                if (!b.Active || b.IsStorage) continue;
                float avail = b.OutTotal - b.HaulReserved;
                if (avail <= 0 || !HasRoomFor(b)) continue;
                float min = b.Workers > 0 ? B.carryCapacity : B.workHaulMin;
                if (avail < min) continue;
                float score = avail / (1f + math.distance(b.DoorPos, c.Pos) * 0.05f);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            if (best == null) return false;
            float amt = math.min(B.carryCapacity, best.OutTotal - best.HaulReserved);
            for (int r = 0; r < ResCount; r++) if (best.Out[r] > amt) amt = math.min(CarryCap(r), best.Out[r]);
            best.HaulReserved += amt;
            c.Task = Task.HaulPickup;
            c.TaskB = best; c.TaskAmt = amt;
            Walk(c, best.Door, Activity.Walking);
            return true;
        }

        // ------------------------------------------------------------------ homes and jobs

        void AssignHomes()
        {
            foreach (var b in Buildings) b.Residents = 0;
            foreach (var c in Citizens)
            {
                if (c.Home != null && !c.Home.Active) c.Home = null;
                if (c.Home != null) c.Home.Residents++;
            }
            foreach (var c in Citizens)
            {
                if (c.Home != null || c.Gone) continue;
                Building best = null; float bd = float.MaxValue;
                foreach (var b in Buildings)
                {
                    if (!b.Active || b.Def.housing <= b.Residents) continue;
                    float d = math.distancesq(b.Center, c.Pos);
                    if (d < bd) { bd = d; best = b; }
                }
                if (best == null) break;
                c.Home = best;
                best.Residents++;
            }
            // Babies stay with their mothers, so a home can fill past its beds while others stand half empty: adults in
            // an overfull home move to a free bed elsewhere (crowding breeds sickness and unrest).
            foreach (var c in Citizens)
            {
                var h = c.Home;
                if (h == null || c.Gone || !IsAdult(c) || h.Residents <= h.Def.housing) continue;
                Building best = null; float bd = float.MaxValue;
                foreach (var b in Buildings)
                {
                    if (!b.Active || b == h || b.Def.housing <= b.Residents) continue;
                    float d = math.distancesq(b.Center, h.Center);
                    if (d < bd) { bd = d; best = b; }
                }
                if (best == null) break;
                h.Residents--;
                c.Home = best;
                best.Residents++;
            }
        }

        /// <summary>
        /// Fills jobs: high-priority workplaces first, then builders (only while there are sites), then normal and low
        /// priority. Short jobs take people from strictly lower priorities (high jobs never take builders). Elders are preferred for knowledge work.
        /// Anyone left over hauls and helps build.
        /// </summary>
        void AssignJobs()
        {
            _jobsDirty = false;
            if (TownCenter == null) return;
            foreach (var b in Buildings) { b.Workers = 0; b.Builders = 0; }
            bool sites = false;
            foreach (var b in Buildings) if (b.UnderWork) { sites = true; break; }
            int wantBuilders = sites ? BuildersWanted : 0, builders = 0;
            _free.Clear();
            foreach (var c in Citizens)
            {
                bool able = !c.Gone && IsAdult(c) && !c.Sick && c.DisputeDays == 0 && c.Task != Task.Arrive;
                if (!able) { if (c.Job != null || c.IsBuilder) Unassign(c); continue; }
                if (c.Job != null && (!c.Job.Active || c.Job.Paused || !c.Job.Def.IsWorkplace || c.Job.Workers >= c.Job.Def.maxWorkers || !WantsWorkers(c.Job))) Unassign(c);
                if (c.IsBuilder && builders >= wantBuilders) Unassign(c);
                if (c.Job != null) c.Job.Workers++;
                else if (c.IsBuilder) builders++;
                else _free.Add(c);
            }
            _demand.Clear();   // null = the builders
            for (int p = (int)Priority.High; p >= 0; p--)
            {
                // Within a priority, fields come first: a few days of sowing or harvest feed people for months.
                for (int pass = 0; pass < 2; pass++)
                    foreach (var b in Buildings)
                        if (b.Active && !b.Paused && b.Def.IsWorkplace && (int)b.Priority == p && WantsWorkers(b) && b.Def.crop == (pass == 0)) _demand.Add(b);
                if (p == (int)Priority.High) _demand.Add(null);
            }
            for (int k = 0; k < _demand.Count; k++)
            {
                var b = _demand[k];
                while (b == null ? builders < wantBuilders : b.Workers < b.Def.maxWorkers)
                {
                    var pick = PickFree(b) ?? Steal(k, ref builders);
                    if (pick == null) break;
                    if (b == null) { pick.IsBuilder = true; builders++; }
                    else { pick.Job = b; b.Workers++; }
                }
            }
            foreach (var b in Buildings)
                if (b.Active && b.Def.IsWorkplace) b.Status = b.Paused ? "Paused" : b.Flooded ? "Flooded" : !WantsWorkers(b) ? (Season == 3 ? "Nothing to do this season" : "The harvest is in") : b.Workers == 0 ? "No workers" : b.Status == "No workers" || b.Status == "Paused" ? "" : b.Status;
        }

        // Builders share the top tier with high-priority jobs: filled after them, but never taken by them.
        // A high-priority field in sowing or harvest time outranks everything else: it may take people from other
        // high-priority jobs (a few days there feed people for months).
        int Tier(Building b) => b == null ? 0 : b.Priority == Priority.High ? (b.Def.crop && (Season == 0 || Season == 2) ? -1 : 0) : b.Priority == Priority.Normal ? 2 : 3;

        Citizen PickFree(Building b)
        {
            if (_free.Count == 0) return null;
            bool wantElder = b != null && b.Def.knowledgePerWorker > 0;
            int best = 0;
            for (int i = 0; i < _free.Count; i++)
                if (IsElder(_free[i]) == wantElder) { best = i; break; }
            var c = _free[best];
            _free.RemoveAt(best);
            Unassign(c);
            return c;
        }

        Citizen Steal(int k, ref int builders)
        {
            int tier = Tier(_demand[k]);
            for (int j = _demand.Count - 1; j > k; j--)
            {
                var d = _demand[j];
                if (Tier(d) <= tier) break;
                if (d == null ? builders == 0 : d.Workers == 0) continue;
                foreach (var c in Citizens)
                {
                    if (d == null ? !c.IsBuilder : c.Job != d) continue;
                    if (d == null) builders--; else d.Workers--;
                    Unassign(c);
                    return c;
                }
            }
            return null;
        }

        void Unassign(Citizen c)
        {
            c.Job = null;
            c.IsBuilder = false;
            if (!c.Carrying) CancelTask(c);
        }

        // ------------------------------------------------------------------ daily needs

        void DailyNeeds()
        {
            float comfort = B.warmthComfort, temp = Temperature;
            int season = Season;

            // Fires burn fuel; homes burn firewood. Out of fuel = no warmth from it today.
            foreach (var b in Buildings)
            {
                if (!b.Active) continue;
                if (b.Def.warmthRadius > 0)
                {
                    float need = b.Def.fuelPerDay != null && b.Def.fuelPerDay.Length == 4 ? b.Def.fuelPerDay[season] : 0;
                    b.Fueled = need <= 0 || Withdraw(_fuel, need) >= need * 0.99f;
                }
                if (b.Def.housing > 0)
                {
                    float need = b.Residents > 0 ? B.firewoodPerHome[season] * Diff.firewood : 0;
                    b.Heated = need <= 0 || Withdraw(_fuel, need) >= need * 0.99f;
                    if (b.Flooded) b.Heated = false;      // a fire won't burn in a flooded hut
                }
            }

            // Food: children first, then everyone else in a random order; most perishable food is eaten first.
            _order.Clear();
            foreach (var c in Citizens) if (!IsAdult(c)) _order.Add(c);
            int firstAdult = _order.Count;
            foreach (var c in Citizens) if (IsAdult(c)) _order.Add(c);
            for (int i = _order.Count - 1; i > firstAdult; i--) { int j = firstAdult + _rng.Next(i - firstAdult + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
            // Food Sharing: when there is not enough, everyone gets the same share instead of some going without.
            float eaten = Effect("food.eaten"), share = 1f;
            if (Effect("law.foodsharing") > 1f)
            {
                float total = 0;
                foreach (var c in _order) total += (IsAdult(c) ? B.foodPerAdult : B.foodPerChild) * eaten;
                share = total <= 0 ? 1f : math.min(1f, FoodStock / total);
            }
            foreach (var c in _order)
            {
                float want = (IsAdult(c) ? B.foodPerAdult : B.foodPerChild) * eaten, got = 0, take = want * share;
                foreach (int r in _foodByPerish) { if (got >= take) break; got += Withdraw(r, take - got); }
                c.Hungry = got < want * 0.5f;
                c.Ate = want > 0 ? got / want : 1f;
                c.HungryDays = c.Hungry ? c.HungryDays + 1 : 0;
            }

            bool cold = temp < comfort;
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                // Clothing is put on when it gets cold and wears out over the cold days.
                if (cold)
                {
                    if (c.ClothingLife <= 0 && _clothing >= 0 && StockTotals[_clothing] >= 1f && Withdraw(_clothing, 1f) >= 1f) c.ClothingLife = B.clothingWearDays;
                    if (c.ClothingLife > 0) c.ClothingLife -= 1f;
                }
                var home = c.Home;
                var at = home != null ? home.Center : TownCenter.Center;
                float w = temp;
                if (home != null) w += home.Def.homeWarmth * (home.Heated ? 1f : B.unheatedHomeFactor);
                if (cold && c.ClothingLife > 0) w += B.clothingWarmth;
                if (NearFire(at)) w += B.campfireWarmth;
                c.Warmth = w;
                c.Cold = w < comfort;

                // Sickness: crowding, cold, hunger and bad water raise the chance; a healer in reach (with herbs) lowers
                // chance and harm, a healer's hut more than a herbalist.
                bool covered = HealthCover(at, out float healChance, out float healDamage);
                float sickDmg = 0;
                if (c.SickDays > 0)
                {
                    c.Treated = covered && _herbs >= 0 && Withdraw(_herbs, B.herbsPerSickDay) > 0;
                    sickDmg = B.sickDamage * (c.Treated ? healDamage : 1f);
                    c.SickDays--;
                    if (c.SickDays == 0) _jobsDirty = true;
                }
                else
                {
                    float crowd = home != null ? math.max(0, home.Residents - CrowdFree(home)) : 0;
                    float chance = B.sickBase + B.sickPerCrowding * crowd + (c.Cold ? B.sickCold : 0) + (c.Hungry ? B.sickStarving : 0) + (c.NoWater ? B.noWaterSick : 0);
                    chance *= Effect("sickness");
                    if (covered && _herbs >= 0 && StockTotals[_herbs] > 0) chance *= healChance;
                    if (_rng.NextDouble() < chance)
                    {
                        c.SickDays = _rng.Next(B.sickDaysMin, B.sickDaysMax + 1);
                        _jobsDirty = true;
                    }
                }

                // Hunger harms in proportion to how little was eaten (shared short rations harm less than none).
                float starve = c.Hungry ? B.starveDamage * math.saturate((1f - c.Ate) * 1.6f) : 0;
                float coldDmg = c.Cold ? B.coldDamage * (comfort - w) / comfort : 0;
                float total = starve + coldDmg + sickDmg;
                if (total > 0)
                {
                    c.LastHarm = starve >= coldDmg && starve >= sickDmg ? "starvation" : coldDmg >= sickDmg ? "cold" : "sickness";
                    c.Health -= total;
                    if (c.Health <= 0) { Die(c, c.LastHarm); continue; }
                }
                else c.Health = math.min(1f, c.Health + B.recoverPerDay);

                // Happiness follows the needs: a blend of their average and the worst of them.
                float food = c.Hungry ? 0f : 1f;
                float warm = math.saturate(w / comfort);
                float shelter = home != null ? 1f : B.homelessShelter;
                float health = c.Health * (c.Sick ? 0.6f : 1f);
                float sum = food + warm + shelter + health; int count = 4;
                float min = math.min(math.min(food, warm), math.min(shelter, health));
                ExtraNeeds(c, ref sum, ref count, ref min);     // water, goods, faith once they apply
                float avg = sum / count;
                float target = math.saturate((avg * (1f - B.happinessWorstWeight) + min * B.happinessWorstWeight) * Effect("happiness"));
                c.Happiness = math.lerp(c.Happiness, target, B.happinessSmoothing);
                c.UnhappyDays = c.Happiness < B.leaveHappiness ? c.UnhappyDays + 1 : 0;
            }
        }

        bool NearFire(float2 p)
        {
            foreach (var b in Buildings)
                if (b.Active && b.Fueled && b.Def.warmthRadius > 0 && math.distance(b.Center, p) <= b.Def.warmthRadius) return true;
            return false;
        }


        // ------------------------------------------------------------------ population

        void DailyPopulation()
        {
            float years = B.ageYearsPerGameYear / DaysPerYear;
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                if (c.Gone) continue;
                bool wasChild = !IsAdult(c);
                c.Age += years;
                if (c.BirthCooldown > 0) c.BirthCooldown -= 1f;
                if (wasChild && IsAdult(c)) _jobsDirty = true;
                if (c.Age > B.oldAgeStart && _rng.NextDouble() < B.oldAgeChancePerYearOver * (c.Age - B.oldAgeStart)) Die(c, "old age");
            }

            // Births: a fertile woman with a home that has room, enough food in store and a man in the settlement.
            bool man = false;
            foreach (var c in Citizens) if (!c.Gone && !c.Female && c.Age >= B.fertileMin) { man = true; break; }
            // The baby stays with its mother, so her home may end up crowded (more sickness) if others had the room.
            int room = HousingCapacity - Population;
            // The stores have to carry everyone until the next harvest: families and newcomers plan for the lean
            // months, not for the pile on the day (a full store just after the harvest has winter and spring to feed).
            // (A Stone Age band lives off what it gathers day by day; planning for the lean months comes with farming.)
            bool plan = Data.AgeOrder(B.planFoodAge) <= Age.order;
            float birthFood = math.max(B.birthMinFoodDays, plan ? LeanDays() * B.birthWinterShare : 0);
            if (man && FoodDays >= birthFood && room > 0)
            {
                int n = Citizens.Count;
                for (int i = 0; i < n && room > 0; i++)
                {
                    var m = Citizens[i];
                    if (m.Gone || !m.Female || m.Age < B.fertileMin || m.Age > B.fertileMax || m.Home == null || m.BirthCooldown > 0 || m.Hungry || m.Sick) continue;
                    if (_rng.NextDouble() >= B.birthChancePerDay * Effect("births")) continue;
                    var baby = NewCitizen(_rng.Next(2) == 0, 0f, m.Home.DoorPos);
                    baby.Home = m.Home;
                    m.Home.Residents++;
                    m.BirthCooldown = B.birthCooldownDays;
                    room--;
                    Births++;
                    Log(Severity.Info, $"{m.Name} gave birth to {baby.Name}.", m.Home.DoorPos);
                }
            }

            // Migrants come when there is room, food and a good mood.
            if (B.migrantCheckDays > 0 && Day % (int)math.max(1, B.migrantCheckDays) == 0)
            {
                int free = HousingCapacity - Population;
                float migrantFood = math.max(B.migrantMinFoodDays, plan ? LeanDays() * B.migrantWinterShare : 0);
                if (free >= B.migrantMinFreeHousing && FoodDays >= migrantFood && AverageHappiness >= B.migrantMinHappiness && _rng.NextDouble() < B.migrantChance * Effect("migrants"))
                {
                    int n = math.min(free, _rng.Next(B.migrantsMin, B.migrantsMax + 1));
                    // Only as many as the stores can feed for the same number of days with them counted in.
                    while (n > 0 && FoodStock / (DailyFoodNeed + n * B.foodPerAdult) < migrantFood) n--;
                    if (n > 0)
                    {
                        int edge = Map.EdgeCellNear(TownCenter.Center, _rng);
                        var from = new float2(edge % Map.W + 0.5f, edge / Map.W + 0.5f);
                        for (int i = 0; i < n; i++)
                        {
                            bool child = i > 0 && _rng.NextDouble() < 0.25;
                            var c = NewCitizen(_rng.Next(2) == 0, child ? R(3f, 12f) : R(B.fertileMin, B.fertileMax), from + new float2(R(-0.4f, 0.4f), R(-0.4f, 0.4f)));
                            c.Task = Task.Arrive;
                            Walk(c, TownCenter.Door, Activity.Arriving);
                        }
                        Migrants += n;
                        Log(Severity.Info, n == 1 ? "A migrant arrived." : $"{n} migrants arrived.", from);
                        AssignHomes();
                    }
                }
            }

            // People who have been unhappy too long walk away; in a famine, the hungry go looking for food elsewhere
            // before it kills them.
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                if (c.Gone || !IsAdult(c)) continue;
                bool famine = c.HungryDays >= B.famineLeaveDays && _rng.NextDouble() < B.famineLeaveChance;
                if (c.UnhappyDays < B.leaveAfterDays && !famine) continue;
                var home = c.Home;
                int edge = Map.EdgeCellNear(c.Pos, _rng);
                Depart(c, edge);
                Log(Severity.Bad, famine ? $"{c.Name} left the settlement to find food elsewhere." : $"{c.Name} left the settlement, unhappy.", c.Pos);
                // The last adult of a household takes its children along.
                if (home != null && !Citizens.Any(o => !o.Gone && o.Home == home && IsAdult(o)))
                    foreach (var k in Citizens) if (!k.Gone && k.Home == home && !IsAdult(k)) Depart(k, edge);
            }
        }

        void Depart(Citizen c, int edge)
        {
            CancelTask(c);
            c.Job = null; c.IsBuilder = false; c.Home = null;
            c.CarryAmt = 0; c.CarryRes = -1;
            c.Gone = true;
            Leavers++;
            var walker = new Citizen { Id = c.Id, Name = c.Name, Female = c.Female, Age = c.Age, Pos = c.Pos, Facing = c.Facing, Task = Task.Leave };
            Walk(walker, edge, Activity.Leaving);
            Departing.Add(walker);
            _jobsDirty = true;
        }

        void Die(Citizen c, string cause)
        {
            if (c.Gone) return;
            CancelTask(c);
            c.Gone = true;
            c.Job = null; c.IsBuilder = false; c.Home = null;
            Deaths[cause] = Deaths.TryGetValue(cause, out int n) ? n + 1 : 1;
            Log(Severity.Bad, cause == "old age" ? $"{c.Name} died of old age at {(int)c.Age}." : $"{c.Name} ({(int)c.Age}) died of {cause}.", c.Pos);
            _jobsDirty = true;
        }
    }
}
