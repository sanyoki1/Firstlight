using System;
using System.Collections.Generic;
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
            float speed = B.walkSpeed * Step * (Map.WaterClass[cur] == 1 ? 0.5f : 1f) * (c.Sick ? 0.6f : 1f) * (c.Carrying ? 0.85f : 1f);
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
                case Task.GoBuild:
                    c.Task = Task.Build;
                    break;
                case Task.DeliverOutput:
                {
                    var s = c.TaskB;
                    if (s != null && s.Active && s.Store != null) c.CarryAmt -= PutInto(s, c.CarryRes, c.CarryAmt);
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
                    site.InTransit[c.TaskIdx] -= c.TaskAmt;
                    float still = site.Def.cost[c.TaskIdx].amount - site.Delivered[c.TaskIdx] - site.InTransit[c.TaskIdx];
                    float got = c.TaskB.Active && site.IsSite ? TakeFrom(c.TaskB, site.CostRes[c.TaskIdx], math.min(c.TaskAmt, still)) : 0;
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
                    site.InTransit[c.TaskIdx] -= c.CarryAmt;
                    if (site.IsSite)
                    {
                        site.Delivered[c.TaskIdx] += c.CarryAmt;
                        // Snap away float dust so a site is never left a hair short of its materials.
                        if (site.Def.cost[c.TaskIdx].amount - site.Delivered[c.TaskIdx] < MaterialTolerance) site.Delivered[c.TaskIdx] = site.Def.cost[c.TaskIdx].amount;
                        if (math.abs(site.InTransit[c.TaskIdx]) < 1e-3f) site.InTransit[c.TaskIdx] = 0;
                        c.CarryAmt = 0; c.CarryRes = -1;
                        if (site.MaterialsComplete && site.Def.buildWork <= 0) Complete(site);
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
                case Task.FetchMaterial: if (c.TaskB2 != null) c.TaskB2.InTransit[c.TaskIdx] -= c.TaskAmt; break;
                case Task.DeliverMaterial: if (c.TaskB2 != null) c.TaskB2.InTransit[c.TaskIdx] -= c.CarryAmt; break;
                case Task.HaulPickup: if (c.TaskB != null) c.TaskB.HaulReserved = math.max(0, c.TaskB.HaulReserved - c.TaskAmt); break;
            }
            if (c.Task == Task.Leave || c.Task == Task.Arrive) return;
            c.Task = Task.None;
            c.TaskB = c.TaskB2 = null;
            c.Target = -1;
        }

        /// <summary>The steady part of an errand (resting, working, building, idling); re-decides when it no longer fits.</summary>
        void Act(Citizen c, bool work)
        {
            if (work && c.Job != null) WorkerSeconds += Step;
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
            bool adult = IsAdult(c);
            if (c.Sick || !work) { GoHome(c); return; }
            if (!adult) { GoIdle(c, Activity.Playing); return; }
            if (c.Job != null) { WorkerDecide(c); return; }
            if (c.IsBuilder) { if (BuilderDecide(c) || HaulDecide(c)) return; }
            else if (HaulDecide(c) || BuilderDecide(c)) return;
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
            var s = NearestStorageWithSpace(c.Pos, 0.5f);
            if (s == null) { c.Task = Task.Idle; c.TaskAmt = 0; c.Activity = Activity.Idle; return; }   // hold it until there is room
            c.Task = Task.DeliverOutput;
            c.TaskB = s;
            Walk(c, s.Door, Activity.Carrying);
        }

        bool HasStorageRoom() => NearestStorageWithSpace(TownCenter.Center, 1f) != null;

        void TakeOutput(Citizen c, Building b)
        {
            int best = -1; float most = 0;
            for (int r = 0; r < ResCount; r++) if (b.Out[r] > most) { most = b.Out[r]; best = r; }
            if (best < 0 || most < 0.05f) return;
            float amt = math.min(B.carryCapacity, most);
            b.Out[best] -= amt; b.OutTotal -= amt;
            c.CarryRes = best; c.CarryAmt = amt;
            GoDeliver(c);
        }

        void WorkerDecide(Citizen c)
        {
            var b = c.Job;
            EnsureTool(c, b.Def.usesTools);
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
            c.Task = Task.GoWork;
            c.TaskB = b;
            Walk(c, b.Door, Activity.Walking);
        }

        void WorkAct(Citizen c, bool work)
        {
            var b = c.Job;
            if (b == null || b != c.TaskB || !b.Active || b.Paused) { Decide(c, work); return; }
            if (!work)
            {
                // End of the shift: take finished goods along on the way home, once a day.
                if (c.ShiftDay != Day && b.OutTotal >= B.workHaulMin && HasStorageRoom()) { c.ShiftDay = Day; TakeOutput(c, b); return; }
                Decide(c, work);
                return;
            }
            if (b.OutTotal >= B.carryCapacity && HasStorageRoom()) { TakeOutput(c, b); return; }
            ProductiveSeconds += Step;
            if (b.Def.usesTools && c.Tool < 0) EnsureTool(c, true);   // tools may have come in since the shift began
            float speed = WorkSpeed(c, b.Def.usesTools, b.Def.requiresTools);
            if (speed <= 0) { b.Status = "Needs tools"; c.Activity = Activity.Idle; return; }
            bool did = WorkStep(c, b, speed);
            c.Activity = did ? Activity.Working : Activity.Idle;
            if (did && b.Def.usesTools) WearTool(c);
            if (!did && b.Def.inputs.Length > 0 && InputToFetch(b, out _) >= 0) Decide(c, work);
        }

        float WorkSpeed(Citizen c, bool usesTools, bool requiresTools)
        {
            float s = math.lerp(B.workSpeedMin, 1f, math.saturate(c.Happiness / 0.6f));
            if (!usesTools) return s;
            if (c.Tool >= 0) return s * Data.Resources[c.Tool].toolSpeed;
            return requiresTools ? 0f : s * B.noToolWorkSpeed;
        }

        /// <summary>A tool-using worker without a tool takes the best one in stock.</summary>
        void EnsureTool(Citizen c, bool uses)
        {
            if (!uses || c.Tool >= 0) return;
            foreach (int r in _toolsBest)
            {
                if (StockTotals[r] < 1f) continue;
                var s = NearestStorageWith(c.Pos, r, 1f);
                if (s == null || TakeFrom(s, r, 1f) < 1f) continue;
                c.Tool = r;
                c.ToolLife = B.toolWearDays;
                return;
            }
        }

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
            }
        }

        /// <summary>Builders: carry missing materials to the oldest site that can use them, else build one whose materials are in.</summary>
        bool BuilderDecide(Citizen c)
        {
            foreach (var site in Buildings)
            {
                if (!site.IsSite) continue;
                if (!site.MaterialsComplete)
                {
                    for (int i = 0; i < site.Def.cost.Length; i++)
                    {
                        float remaining = site.Def.cost[i].amount - site.Delivered[i] - site.InTransit[i];
                        if (remaining < MaterialTolerance) continue;
                        var s = NearestStorageWith(site.DoorPos, site.CostRes[i]);
                        if (s == null) continue;
                        float amt = math.min(B.carryCapacity, math.min(remaining, s.Store[site.CostRes[i]]));
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
            if (!work || site == null || !site.IsSite) { Decide(c, work); return; }
            float speed = WorkSpeed(c, B.buildersUseTools, false);
            site.Work += B.builderWorkPerDay * Step / WorkdaySeconds * speed * Effect("build.speed") / math.max(0.05f, site.Def.buildWork);
            c.Activity = Activity.Building;
            if (B.buildersUseTools) WearTool(c);
            if (site.Work >= 1f) { Complete(site); Decide(c, work); }
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
                float min = b.Workers > 0 ? B.carryCapacity : B.workHaulMin;
                if (avail < min) continue;
                float score = avail / (1f + math.distance(b.DoorPos, c.Pos) * 0.05f);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            if (best == null) return false;
            float amt = math.min(B.carryCapacity, best.OutTotal - best.HaulReserved);
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
            foreach (var b in Buildings) if (b.IsSite) { sites = true; break; }
            int wantBuilders = sites ? BuildersWanted : 0, builders = 0;
            _free.Clear();
            foreach (var c in Citizens)
            {
                bool able = !c.Gone && IsAdult(c) && !c.Sick && c.Task != Task.Arrive;
                if (!able) { if (c.Job != null || c.IsBuilder) Unassign(c); continue; }
                if (c.Job != null && (!c.Job.Active || c.Job.Paused || !c.Job.Def.IsWorkplace || c.Job.Workers >= c.Job.Def.maxWorkers)) Unassign(c);
                if (c.IsBuilder && builders >= wantBuilders) Unassign(c);
                if (c.Job != null) c.Job.Workers++;
                else if (c.IsBuilder) builders++;
                else _free.Add(c);
            }
            _demand.Clear();   // null = the builders
            for (int p = (int)Priority.High; p >= 0; p--)
            {
                foreach (var b in Buildings)
                    if (b.Active && !b.Paused && b.Def.IsWorkplace && (int)b.Priority == p) _demand.Add(b);
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
                if (b.Active && b.Def.IsWorkplace) b.Status = b.Paused ? "Paused" : b.Workers == 0 ? "No workers" : b.Status == "No workers" || b.Status == "Paused" ? "" : b.Status;
        }

        // Builders share the top tier with high-priority jobs: filled after them, but never taken by them.
        int Tier(Building b) => b == null ? 0 : b.Priority == Priority.High ? 0 : b.Priority == Priority.Normal ? 2 : 3;

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
                    float need = b.Residents > 0 ? B.firewoodPerHome[season] : 0;
                    b.Heated = need <= 0 || Withdraw(_fuel, need) >= need * 0.99f;
                }
            }

            // Food: children first, then everyone else in a random order; most perishable food is eaten first.
            _order.Clear();
            foreach (var c in Citizens) if (!IsAdult(c)) _order.Add(c);
            int firstAdult = _order.Count;
            foreach (var c in Citizens) if (IsAdult(c)) _order.Add(c);
            for (int i = _order.Count - 1; i > firstAdult; i--) { int j = firstAdult + _rng.Next(i - firstAdult + 1); (_order[i], _order[j]) = (_order[j], _order[i]); }
            foreach (var c in _order)
            {
                float want = IsAdult(c) ? B.foodPerAdult : B.foodPerChild, got = 0;
                foreach (int r in _foodByPerish) { if (got >= want) break; got += Withdraw(r, want - got); }
                c.Hungry = got < want * 0.5f;
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
                float w = temp;
                if (home != null) w += home.Def.homeWarmth * (home.Heated ? 1f : B.unheatedHomeFactor);
                if (cold && c.ClothingLife > 0) w += B.clothingWarmth;
                if (NearFire(home != null ? home.Center : TownCenter.Center)) w += B.campfireWarmth;
                c.Warmth = w;
                c.Cold = w < comfort;

                // Sickness: crowding, cold and hunger raise the chance; a herbalist in reach (with herbs) lowers chance and harm.
                bool covered = Covered(home != null ? home.Center : TownCenter.Center);
                float sickDmg = 0;
                if (c.SickDays > 0)
                {
                    c.Treated = covered && _herbs >= 0 && Withdraw(_herbs, B.herbsPerSickDay) > 0;
                    sickDmg = B.sickDamage * (c.Treated ? B.herbalistDamageMult : 1f);
                    c.SickDays--;
                    if (c.SickDays == 0) _jobsDirty = true;
                }
                else
                {
                    int crowd = home != null ? math.max(0, home.Residents - B.crowdingFreeOccupants) : 0;
                    float chance = B.sickBase + B.sickPerCrowding * crowd + (c.Cold ? B.sickCold : 0) + (c.Hungry ? B.sickStarving : 0);
                    if (covered && _herbs >= 0 && StockTotals[_herbs] > 0) chance *= B.herbalistChanceMult;
                    if (_rng.NextDouble() < chance)
                    {
                        c.SickDays = _rng.Next(B.sickDaysMin, B.sickDaysMax + 1);
                        _jobsDirty = true;
                    }
                }

                float starve = c.Hungry ? B.starveDamage : 0;
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
                float avg = (food + warm + shelter + health) * 0.25f;
                float min = math.min(math.min(food, warm), math.min(shelter, health));
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

        bool Covered(float2 p)
        {
            foreach (var b in Buildings)
                if (b.Active && b.Workers > 0 && b.Def.healthRadius > 0 && math.distance(b.Center, p) <= b.Def.healthRadius) return true;
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
            if (man && FoodDays >= B.birthMinFoodDays && room > 0)
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
                    Log(Severity.Info, $"{m.Name} gave birth to {baby.Name}.");
                }
            }

            // Migrants come when there is room, food and a good mood.
            if (B.migrantCheckDays > 0 && Day % (int)math.max(1, B.migrantCheckDays) == 0)
            {
                int free = HousingCapacity - Population;
                if (free >= B.migrantMinFreeHousing && FoodDays >= B.migrantMinFoodDays && AverageHappiness >= B.migrantMinHappiness && _rng.NextDouble() < B.migrantChance)
                {
                    int n = math.min(free, _rng.Next(B.migrantsMin, B.migrantsMax + 1));
                    // Only as many as the stores can feed for the same number of days with them counted in.
                    while (n > 0 && FoodStock / (DailyFoodNeed + n * B.foodPerAdult) < B.migrantMinFoodDays) n--;
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
                        Log(Severity.Info, $"{n} migrants arrived.");
                        AssignHomes();
                    }
                }
            }

            // People who have been unhappy too long walk away.
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                if (c.Gone || !IsAdult(c) || c.UnhappyDays < B.leaveAfterDays) continue;
                CancelTask(c);
                c.Job = null; c.IsBuilder = false; c.Home = null;
                c.CarryAmt = 0; c.CarryRes = -1;
                c.Gone = true;
                Leavers++;
                Log(Severity.Bad, $"{c.Name} left the settlement, unhappy.");
                var walker = new Citizen { Id = c.Id, Name = c.Name, Female = c.Female, Age = c.Age, Pos = c.Pos, Facing = c.Facing, Task = Task.Leave };
                Walk(walker, Map.EdgeCellNear(c.Pos, _rng), Activity.Leaving);
                Departing.Add(walker);
                _jobsDirty = true;
            }
        }

        void Die(Citizen c, string cause)
        {
            if (c.Gone) return;
            CancelTask(c);
            c.Gone = true;
            c.Job = null; c.IsBuilder = false; c.Home = null;
            Deaths[cause] = Deaths.TryGetValue(cause, out int n) ? n + 1 : 1;
            Log(Severity.Bad, cause == "old age" ? $"{c.Name} died of old age at {(int)c.Age}." : $"{c.Name} ({(int)c.Age}) died of {cause}.");
            _jobsDirty = true;
        }
    }
}
