using System.Collections.Generic;
using Firstlight.Core;
using Unity.Mathematics;

namespace Firstlight.Game
{
    public enum DeerState : byte { Graze, Look, Wander, ToWater, Drink, Return }

    /// <summary>One deer. Pos is in cell units (x, map y); Yaw faces +z at 0, like every instance.</summary>
    public struct Deer
    {
        public DeerState State;
        public float2 Pos, Goal;
        public float Yaw, Timer, Thirst, ThirstRate, Head, HeadTarget, Stride;
        public bool Stag, Moving;
        public bool Near;              // past the shared field: on its own last stretch (to its spot, or home)
        public int RouteStep;
        public int DrinkCell;          // bank cell it drinks from (-1 = none)
        public float2 Face;            // direction to the water while drinking
        public uint Tint;
    }

    /// <summary>
    /// Deer herds. Each deer grazes and wanders near its herd's home spot and grows thirsty. Then it claims a
    /// free spot of its own on the near bank, walks the herd's shared flow field to the watering place, takes
    /// a short route of its own along the bank to its spot (the field's path traced back from the spot), drinks
    /// with its head down, and walks home the same way. Herd-mates that are fairly thirsty go along; when every
    /// spot is taken the rest wait. Spots use the simulated water depth, so floods and falling water move them.
    /// Hunters thin herds by lowering Herd.Pop.
    /// </summary>
    public sealed class Wildlife
    {
        public const float WalkSpeed = 0.6f;        // cells per second (about 1.2 m/s)
        public const float StrideCells = 0.55f;     // distance per walk cycle
        const float ThirstMinSeconds = 100f, ThirstMaxSeconds = 160f;
        const float DrinkSeconds = 8f;
        const int WaterSearchRadius = 48;
        const float RecheckSeconds = 4f;

        readonly GameMap _map;
        readonly WaterSim _water;
        readonly System.Random _rng;
        public readonly Deer[][] Herds;
        readonly int[] _waterCell;              // per herd: flow-field target on the bank (-1 = no water in reach)
        readonly float[] _recheck;
        readonly HashSet<int> _taken = new();
        readonly List<int>[][] _route;          // per deer: cells from the watering place to its own spot
        public int DrinksTaken { get; private set; }

        public Wildlife(GameMap map, WaterSim water, int seed)
        {
            _map = map;
            _water = water;
            _rng = new System.Random(seed ^ 0x5eed);
            Herds = new Deer[map.Herds.Count][];
            _waterCell = new int[map.Herds.Count];
            _recheck = new float[map.Herds.Count];
            _route = new List<int>[map.Herds.Count][];
            for (int h = 0; h < map.Herds.Count; h++)
            {
                var herd = map.Herds[h];
                var deer = new Deer[herd.Offsets.Length];
                _route[h] = new List<int>[deer.Length];
                for (int i = 0; i < deer.Length; i++) _route[h][i] = new List<int>();
                for (int i = 0; i < deer.Length; i++)
                {
                    deer[i] = new Deer
                    {
                        State = DeerState.Graze,
                        Pos = herd.Pos + herd.Offsets[i],
                        Yaw = herd.Yaw[i],
                        Timer = R(1f, 8f),
                        Thirst = R(0f, 0.85f),
                        ThirstRate = 1f / R(ThirstMinSeconds, ThirstMaxSeconds),
                        Stag = i % 4 == 0,
                        DrinkCell = -1,
                        Tint = Pal.Pack(new UnityEngine.Color(R(0.9f, 1.06f), R(0.92f, 1.02f), R(0.9f, 1f))),
                    };
                    deer[i].Head = deer[i].HeadTarget = 1f;
                }
                Herds[h] = deer;
                _waterCell[h] = FindWateringPlace(herd.Pos);
                _recheck[h] = R(0f, RecheckSeconds);
            }
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        /// <summary>The bank cell a herd currently drinks from (-1 = no water in reach).</summary>
        public int WateringPlace(int herd) => _waterCell[herd];

        /// <summary>Makes a herd thirsty now (used by the screenshot mode).</summary>
        public void MakeThirsty(int herd)
        {
            for (int i = 0; i < Herds[herd].Length; i++) Herds[herd][i].Thirst = math.max(Herds[herd][i].Thirst, 0.97f + i * 0.004f);
        }

        public static int ActiveCount(Herd herd, Deer[] deer) => math.min(deer.Length, (int)math.floor(herd.Pop));

        // ------------------------------------------------------------------ update

        public void Update(float dt)
        {
            if (dt <= 0) return;
            for (int h = 0; h < Herds.Length; h++)
            {
                var herd = _map.Herds[h];
                var deer = Herds[h];
                int n = ActiveCount(herd, deer);

                _recheck[h] -= dt;
                if (_recheck[h] <= 0)
                {
                    _recheck[h] = RecheckSeconds;
                    if (_waterCell[h] < 0 || !IsDrinkSpot(_waterCell[h])) _waterCell[h] = FindWateringPlace(herd.Pos);
                }

                for (int i = 0; i < n; i++) Step(h, i, dt);
                for (int i = n; i < deer.Length; i++)
                {
                    // Hunted out of the herd: free its spot; if the herd regrows it reappears at home.
                    if (deer[i].DrinkCell >= 0) Release(ref deer[i]);
                    deer[i].State = DeerState.Graze;
                    deer[i].Pos = herd.Pos + herd.Offsets[i];
                }
                Separate(deer, n, dt);
            }
        }

        void Step(int h, int i, float dt)
        {
            ref var d = ref Herds[h][i];
            var herd = _map.Herds[h];
            d.Thirst = math.min(1.2f, d.Thirst + d.ThirstRate * dt);
            d.Timer -= dt;
            d.Moving = false;

            switch (d.State)
            {
                case DeerState.Graze:
                case DeerState.Look:
                    d.HeadTarget = d.State == DeerState.Graze ? 1f : 0f;
                    if (d.Thirst >= 1f && _waterCell[h] >= 0) { GoDrink(h, i); break; }
                    if (d.Timer <= 0)
                    {
                        if (_rng.NextDouble() < 0.6) PickWander(ref d, herd);
                        else { d.State = _rng.NextDouble() < 0.7 ? DeerState.Graze : DeerState.Look; d.Timer = R(3f, 9f); }
                    }
                    break;

                case DeerState.Wander:
                    d.HeadTarget = 0f;
                    if (MoveStraight(ref d, d.Goal, dt)) { d.State = DeerState.Graze; d.Timer = R(4f, 12f); }
                    break;

                case DeerState.ToWater:
                    d.HeadTarget = 0f;
                    if (d.DrinkCell < 0 || !IsDrinkSpot(d.DrinkCell))
                    {
                        // The bank it was heading for flooded or dried out: aim again.
                        Release(ref d);
                        if (_waterCell[h] < 0 || !IsDrinkSpot(_waterCell[h])) _waterCell[h] = FindWateringPlace(herd.Pos);
                        if (_waterCell[h] < 0 || !ClaimSpot(ref d, _waterCell[h])) { d.State = DeerState.Return; d.Near = false; d.Goal = herd.Pos + herd.Offsets[i]; break; }
                    }
                    if (!d.Near)
                    {
                        // Leg 1: the shared field to the herd's watering place.
                        int wc = _waterCell[h];
                        if (math.distance(d.Pos, Centre(wc)) > 1.2f && math.distance(d.Pos, DrinkPos(d)) > 1.2f)
                        {
                            FollowField(ref d, wc, Centre(wc), dt);
                            break;
                        }
                        // Leg 2: its own short route along the bank to its spot.
                        BuildRoute(_route[h][i], d.DrinkCell, wc);
                        d.Near = true;
                        d.RouteStep = 0;
                    }
                    if (FollowRoute(ref d, _route[h][i], DrinkPos(d), dt))
                    {
                        d.State = DeerState.Drink;
                        d.Timer = R(DrinkSeconds * 0.75f, DrinkSeconds * 1.25f);
                    }
                    break;

                case DeerState.Drink:
                    d.HeadTarget = 1f;
                    d.Yaw = TurnTowards(d.Yaw, math.atan2(d.Face.x, d.Face.y), dt * 3f);
                    if (d.Head > 0.9f) d.Thirst = math.max(0f, d.Thirst - dt / DrinkSeconds);
                    if (!IsDrinkSpot(d.DrinkCell, true)) d.Timer = 0;        // water moved away or rose over it
                    if (d.Timer <= 0)
                    {
                        if (d.Thirst < 0.3f) DrinksTaken++;
                        Release(ref d);
                        d.State = DeerState.Return;
                        d.Near = false;
                        d.Goal = herd.Pos + herd.Offsets[i];
                    }
                    break;

                case DeerState.Return:
                    d.HeadTarget = 0f;
                    // The field leads to the herd's home cell; from there (or once close) it walks to its own place.
                    if (!d.Near && math.distance(d.Pos, herd.Pos) > 1.2f && math.distance(d.Pos, d.Goal) > 1.2f)
                        FollowField(ref d, _map.CellAt(herd.Pos), herd.Pos, dt);
                    else
                    {
                        d.Near = true;
                        if (MoveStraight(ref d, d.Goal, dt)) { d.State = DeerState.Graze; d.Timer = R(5f, 12f); }
                    }
                    break;
            }

            d.Head = MoveTowards(d.Head, d.HeadTarget, dt * 1.6f);
        }

        void GoDrink(int h, int i)
        {
            ref var d = ref Herds[h][i];
            if (!ClaimSpot(ref d, _waterCell[h]))
            {
                // Every spot on this stretch of bank is taken: wait a little and look around.
                d.State = DeerState.Look;
                d.Timer = R(4f, 8f);
                return;
            }
            d.State = DeerState.ToWater;
            // Deer go to water together: fairly thirsty herd-mates that are idle join in.
            var deer = Herds[h];
            int n = ActiveCount(_map.Herds[h], deer);
            for (int k = 0; k < n; k++)
            {
                if (k == i) continue;
                ref var o = ref deer[k];
                if (o.Thirst < 0.55f || (o.State != DeerState.Graze && o.State != DeerState.Look && o.State != DeerState.Wander)) continue;
                if (ClaimSpot(ref o, _waterCell[h])) o.State = DeerState.ToWater;
            }
        }

        void PickWander(ref Deer d, Herd herd)
        {
            for (int t = 0; t < 8; t++)
            {
                float a = R(0, math.PI * 2), r = R(0.5f, 4.5f);
                float2 g = herd.Pos + new float2(math.cos(a), math.sin(a)) * r;
                int c = _map.CellAt(g);
                if (!_map.InBounds((int)g.x, (int)g.y) || _map.Flow.Cost[c] == 0 || _map.WaterClass[c] > 0) continue;
                if (math.distance(g, d.Pos) < 1f) continue;
                d.Goal = g;
                d.State = DeerState.Wander;
                return;
            }
            d.Timer = R(2f, 5f);
        }

        // ------------------------------------------------------------------ movement

        /// <summary>Walks straight at a nearby point. True on arrival.</summary>
        bool MoveStraight(ref Deer d, float2 target, float dt)
        {
            float2 delta = target - d.Pos;
            float dist = math.length(delta);
            if (dist < 0.08f) return true;
            Walk(ref d, delta / dist, math.min(dist, Speed(d) * dt), dt);
            return false;
        }

        /// <summary>Follows the flow field towards a target cell (the field routes round deep water, rocks and buildings).</summary>
        void FollowField(ref Deer d, int targetCell, float2 finalPoint, float dt)
        {
            var flow = _map.Flow;
            int slot = flow.Resolve(targetCell);
            int cell = _map.CellAt(d.Pos);
            byte code = flow.Fields[slot * flow.Cells + cell];
            float2 aim;
            if (code == FlowDir.Target || code == FlowDir.None) aim = finalPoint;
            else
            {
                int2 xy = _map.XY(cell) + FlowDir.Offset(code);
                aim = new float2(xy.x + 0.5f, xy.y + 0.5f);
            }
            float2 delta = aim - d.Pos;
            float dist = math.length(delta);
            if (dist < 1e-4f) return;
            Walk(ref d, delta / dist, Speed(d) * dt, dt);
        }

        void Walk(ref Deer d, float2 dir, float step, float dt)
        {
            d.Yaw = TurnTowards(d.Yaw, math.atan2(dir.x, dir.y), dt * 4f);
            // Walk along the facing so turns curve instead of snapping.
            float2 fwd = new(math.sin(d.Yaw), math.cos(d.Yaw));
            float along = math.max(0.25f, math.dot(fwd, dir));
            float2 move = fwd * step * along;
            if (Blocked(d.Pos + move) && !Blocked(d.Pos))      // a deer caught by a flood may still wade out
            {
                // Slide along deep water, rock or a building instead of walking into it.
                if (!Blocked(d.Pos + new float2(move.x, 0))) move = new float2(move.x, 0);
                else if (!Blocked(d.Pos + new float2(0, move.y))) move = new float2(0, move.y);
                else move = 0;
            }
            d.Pos += move;
            d.Pos = math.clamp(d.Pos, 0.5f, new float2(_map.W - 0.5f, _map.H - 0.5f));
            d.Stride += step * along / StrideCells;
            d.Moving = true;
        }

        /// <summary>Deep water, rock or a building.</summary>
        bool Blocked(float2 p) => _map.Flow.Cost[_map.CellAt(p)] == 0;

        float Speed(in Deer d) => _map.WaterClass[_map.CellAt(d.Pos)] == 1 ? WalkSpeed * 0.5f : WalkSpeed;

        /// <summary>Keeps deer from standing inside each other (not while drinking: those have their own spots).</summary>
        static void Separate(Deer[] deer, int n, float dt)
        {
            const float minGap = 0.75f;           // a deer is about 0.85 cells (1.7 m) long
            for (int a = 0; a < n; a++)
            {
                if (deer[a].State == DeerState.Drink) continue;
                for (int b = 0; b < n; b++)
                {
                    if (a == b) continue;
                    float2 delta = deer[a].Pos - deer[b].Pos;
                    float dist = math.length(delta);
                    if (dist >= minGap || dist < 1e-4f) continue;
                    deer[a].Pos += delta / dist * (minGap - dist) * math.min(1f, dt * 4f);
                }
            }
        }

        static float TurnTowards(float yaw, float target, float maxStep)
        {
            float diff = math.atan2(math.sin(target - yaw), math.cos(target - yaw));
            return yaw + math.clamp(diff, -maxStep, maxStep);
        }

        static float MoveTowards(float v, float target, float step) =>
            v < target ? math.min(target, v + step) : math.max(target, v - step);

        // ------------------------------------------------------------------ watering places

        // Depths in world units (1 = 2 m). The water surface is drawn from 0.015, so a spot must be drier than
        // that to look like bank; while drinking a little slack stops ripples from chasing the deer off.
        const float DryDepth = 0.015f, DryDepthDrinking = 0.04f, DrinkableDepth = 0.06f;

        float DepthAt(int c) => _water != null ? _water.Depth[c] : _map.WaterClass[c] > 0 ? 1f : 0f;

        /// <summary>A dry, walkable cell beside river water (4-neighbour), where a deer can stand and drink.</summary>
        bool IsDrinkSpot(int c, bool drinking = false) =>
            c >= 0 && _map.Flow.Cost[c] != 0 && DepthAt(c) <= (drinking ? DryDepthDrinking : DryDepth) && WaterSide(c, out _);

        bool WaterSide(int c, out float2 face)
        {
            int2 xy = _map.XY(c);
            face = 0;
            int found = 0;
            for (int k = 0; k < 8; k += 2)                      // the four straight neighbours
            {
                int2 o = FlowDir.Offset(k);
                int nx = xy.x + o.x, ny = xy.y + o.y;
                if (!_map.InBounds(nx, ny) || DepthAt(_map.Cell(nx, ny)) < DrinkableDepth) continue;
                face += new float2(o.x, o.y);
                found++;
            }
            if (found == 0) return false;
            if (math.lengthsq(face) < 1e-4f) face = new float2(1, 0);   // water on both sides: pick one
            face = math.normalize(face);
            return true;
        }

        /// <summary>Nearest drinking spot to a herd's home, or -1 if there is no water within reach.</summary>
        int FindWateringPlace(float2 home)
        {
            int best = -1; float bestD = float.MaxValue;
            int x0 = math.max(0, (int)home.x - WaterSearchRadius), x1 = math.min(_map.W - 1, (int)home.x + WaterSearchRadius);
            int y0 = math.max(0, (int)home.y - WaterSearchRadius), y1 = math.min(_map.H - 1, (int)home.y + WaterSearchRadius);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = _map.Cell(x, y);
                    if (!IsDrinkSpot(c)) continue;
                    float dd = math.distancesq(new float2(x + 0.5f, y + 0.5f), home);
                    if (dd < bestD) { bestD = dd; best = c; }
                }
            return best;
        }

        /// <summary>
        /// Gives the deer its own free drinking spot along the bank near the herd's watering place. A spot only
        /// counts if it is a short walk from the watering place (so never on the far bank). False if none is free.
        /// </summary>
        bool ClaimSpot(ref Deer d, int around)
        {
            Release(ref d);
            if (around < 0) return false;
            int2 c0 = _map.XY(around);
            int slot = _map.Flow.Resolve(around);
            int best = -1; float bestD = float.MaxValue;
            for (int dy = -6; dy <= 6; dy++)
                for (int dx = -6; dx <= 6; dx++)
                {
                    int x = c0.x + dx, y = c0.y + dy;
                    if (!_map.InBounds(x, y)) continue;
                    int c = _map.Cell(x, y);
                    if (_taken.Contains(c) || !IsDrinkSpot(c) || !ShortWalk(slot, c, around, 10)) continue;
                    float dd = dx * dx + dy * dy + (float)_rng.NextDouble() * 2f;
                    if (dd < bestD) { bestD = dd; best = c; }
                }
            if (best < 0) return false;
            d.Near = false;                       // a new spot: start again from the shared field
            d.DrinkCell = best;
            _taken.Add(best);
            WaterSide(best, out d.Face);
            return true;
        }

        /// <summary>True if following the field from cell reaches target within maxSteps.</summary>
        bool ShortWalk(int slot, int cell, int target, int maxSteps)
        {
            var flow = _map.Flow;
            for (int s = 0; s <= maxSteps; s++)
            {
                if (cell == target) return true;
                byte code = flow.Fields[slot * flow.Cells + cell];
                if (code == FlowDir.None || code == FlowDir.Target) return code == FlowDir.Target;
                int2 xy = _map.XY(cell) + FlowDir.Offset(code);
                cell = _map.Cell(xy.x, xy.y);
            }
            return false;
        }

        void Release(ref Deer d)
        {
            if (d.DrinkCell >= 0) _taken.Remove(d.DrinkCell);
            d.DrinkCell = -1;
        }

        float2 Centre(int cell)
        {
            int2 xy = _map.XY(cell);
            return new float2(xy.x + 0.5f, xy.y + 0.5f);
        }

        /// <summary>
        /// The cells from the watering place to a deer's own spot: the field's path from the spot to the
        /// watering place, reversed. Spots are claimed only if that path is short (see ClaimSpot).
        /// </summary>
        void BuildRoute(List<int> route, int spot, int wateringPlace)
        {
            route.Clear();
            var flow = _map.Flow;
            int slot = flow.Resolve(wateringPlace);
            int cell = spot;
            for (int s = 0; s < 16 && cell != wateringPlace; s++)
            {
                route.Add(cell);
                byte code = flow.Fields[slot * flow.Cells + cell];
                if (code == FlowDir.None || code == FlowDir.Target) break;
                int2 xy = _map.XY(cell) + FlowDir.Offset(code);
                cell = _map.Cell(xy.x, xy.y);
            }
            route.Reverse();
        }

        /// <summary>Walks cell to cell along a route, then to the final point. True on arrival.</summary>
        bool FollowRoute(ref Deer d, List<int> route, float2 final, float dt)
        {
            while (d.RouteStep < route.Count && math.distance(d.Pos, Centre(route[d.RouteStep])) < 0.45f) d.RouteStep++;
            if (d.RouteStep < route.Count)
            {
                MoveStraight(ref d, Centre(route[d.RouteStep]), dt);
                return false;
            }
            return MoveStraight(ref d, final, dt);
        }

        /// <summary>Where the deer stands to drink: near the water edge of its bank cell, so its muzzle reaches over.</summary>
        float2 DrinkPos(in Deer d)
        {
            int2 xy = _map.XY(d.DrinkCell);
            return new float2(xy.x + 0.5f, xy.y + 0.5f) + d.Face * 0.22f;
        }
    }
}
