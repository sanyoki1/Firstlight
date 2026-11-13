using System.Collections.Generic;
using Firstlight.Core;
using Unity.Mathematics;

namespace Firstlight.Game
{
    public enum PersonState : byte { Idle, Mill, ToHunt, Stalk, Throw, Recover, Fetch, Pickup, ToCarcass, Lift, Carry, Home }

    /// <summary>One member of the band. Pos is in cell units (x, map y); Yaw faces +z at 0.</summary>
    public struct Person
    {
        public PersonState State;
        public float2 Pos, Goal, Home;
        public float Yaw, Timer, Stride;
        public bool Woman, Hunter, HasSpear, Moving, SpearInCarcass;
        public int Spear;              // index of its thrown spear in Band.Spears (-1 = none)
        public int TargetCell;         // flow-field target while walking out to the herd
        public float Retarget;
        public uint Tint;
    }

    /// <summary>A thrown spear: flies in an arc, then lies where it landed until its owner picks it up.</summary>
    public sealed class Spear
    {
        public float3 Pos, Vel;
        public float Yaw;
        public bool Flying, Gone;
        public int Owner, Herd;
    }

    /// <summary>
    /// A small Stone Age band at a camp (hide tent and hearth): hunters with spears and people who stay at camp.
    /// A hunt: the hunters walk out to a deer herd along the flow field, crouch and creep in when close, and
    /// throw from about 8 m. A spear that hits kills the deer; two hunters tie it under a pole (a spear) and
    /// carry it home between them while the third walks ahead. A miss scatters the herd; the thrower fetches his spear
    /// and they try again. Walking people are noticed from far off, crouching hunters only up close.
    /// </summary>
    public sealed class Band
    {
        const float WalkSpeed = 0.65f, StalkSpeed = 0.3f, CarrySpeed = 0.45f;   // cells per second
        const float WalkStride = 0.7f, StalkStride = 0.45f, CarryStride = 0.6f;  // cells per walk cycle
        const float StalkFrom = 9f, ThrowRange = 4.2f, GiveUpRange = 30f;
        const float NoticeWalking = 7f, NoticeCrouched = 2.6f, NoticeCamp = 6f;
        const float SpearSpeed = 9f, Gravity = 4.9f;                             // cells/s, cells/s² (9.8 m/s²)

        readonly GameMap _map;
        readonly Wildlife _wild;
        readonly System.Random _rng;
        public readonly float2 Camp;
        public readonly float2 TentPos, FirePos, DropPos;
        public readonly Person[] People;
        public readonly List<Spear> Spears = new();
        public bool AutoHunt = true;
        public int Hunts, Throws, Kills;
        public int HuntHerd { get; private set; } = -1;
        public Carcass Prey { get; private set; }
        public int Carrier { get; private set; } = -1;     // front of the carrying pole
        public int Porter { get; private set; } = -1;      // back of the pole
        const float PoleHalf = 0.55f;                       // carriers stand this far either side of the deer
        float _nextHunt;

        public Band(GameMap map, Wildlife wild, float2 camp, int seed, int hunters = 3, int others = 2)
        {
            _map = map; _wild = wild; Camp = camp;
            _rng = new System.Random(seed ^ 0x6a7d);
            FirePos = camp;
            TentPos = camp + new float2(-1.6f, 1.1f);
            DropPos = camp + new float2(1.3f, -0.6f);
            People = new Person[hunters + others];
            for (int k = 0; k < People.Length; k++)
            {
                float a = k * math.PI * 2f / People.Length + 0.3f;
                var home = camp + new float2(math.sin(a), math.cos(a)) * R(1.1f, 1.5f);
                bool hunter = k < hunters;
                People[k] = new Person
                {
                    State = PersonState.Idle, Pos = home, Home = home, Goal = home,
                    Yaw = math.atan2(camp.x - home.x, camp.y - home.y),            // facing the fire
                    Hunter = hunter, Woman = !hunter && k % 2 == 1, HasSpear = hunter, Spear = -1,
                    Timer = R(2f, 6f),
                    Tint = Pal.Pack(new UnityEngine.Color(R(0.85f, 1.05f), R(0.85f, 1f), R(0.82f, 0.98f))),
                };
            }
            _nextHunt = 20f;
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ------------------------------------------------------------------ hunts

        /// <summary>Sends the hunters after a herd (default: the nearest with at least two deer). False if none.</summary>
        public bool StartHunt(int herd = -1)
        {
            if (HuntHerd >= 0) return false;
            if (herd < 0)
            {
                float best = float.MaxValue;
                for (int h = 0; h < _wild.Herds.Length; h++)
                {
                    if (Wildlife.ActiveCount(_map.Herds[h], _wild.Herds[h]) < 2) continue;
                    float d = math.distance(_map.Herds[h].Pos, Camp);
                    if (d < best && d < 48f) { best = d; herd = h; }
                }
            }
            if (herd < 0) return false;
            HuntHerd = herd; Prey = null; Carrier = -1; Porter = -1; Hunts++;
            for (int k = 0; k < People.Length; k++)
            {
                ref var p = ref People[k];
                if (!p.Hunter) continue;
                p.State = PersonState.ToHunt;
                p.Retarget = 0;
            }
            return true;
        }

        void EndHunt()
        {
            HuntHerd = -1;
            for (int k = 0; k < People.Length; k++)
                if (People[k].Hunter && People[k].State != PersonState.Carry && People[k].State != PersonState.Idle)
                    People[k].State = People[k].HasSpear || People[k].Spear < 0 ? PersonState.Home : People[k].State;
        }

        bool NearestDeer(float2 from, out int index, out float dist)
        {
            index = -1; dist = float.MaxValue;
            if (HuntHerd < 0) return false;
            var deer = _wild.Herds[HuntHerd];
            int n = Wildlife.ActiveCount(_map.Herds[HuntHerd], deer);
            for (int i = 0; i < n; i++)
            {
                float d = math.distance(deer[i].Pos, from);
                if (d < dist) { dist = d; index = i; }
            }
            return index >= 0;
        }

        // ------------------------------------------------------------------ update

        /// <summary>Moves everyone and the spears, and tells the deer where people are. Call before Wildlife.Update.</summary>
        public void Update(float dt)
        {
            if (dt <= 0) return;
            if (AutoHunt && HuntHerd < 0)
            {
                _nextHunt -= dt;
                if (_nextHunt <= 0) { _nextHunt = R(120f, 180f); StartHunt(); }
            }
            if (HuntHerd >= 0 && Prey == null && Wildlife.ActiveCount(_map.Herds[HuntHerd], _wild.Herds[HuntHerd]) == 0) EndHunt();

            for (int k = 0; k < People.Length; k++) Step(k, dt);
            UpdateSpears(dt);
            Separate(dt);

            _wild.Threats.Clear();
            foreach (var p in People)
            {
                bool crouched = p.State is PersonState.Stalk or PersonState.Throw;
                float notice = !p.Hunter ? NoticeCamp : crouched ? NoticeCrouched : NoticeWalking;
                _wild.Threats.Add((p.Pos, notice));
            }
        }

        void Step(int k, float dt)
        {
            ref var p = ref People[k];
            p.Timer -= dt;
            p.Moving = false;
            switch (p.State)
            {
                case PersonState.Idle:
                    // Hunters wait by the fire; the others potter about the camp.
                    if (!p.Hunter && p.Timer <= 0)
                    {
                        float a = R(0, math.PI * 2), r = R(0.9f, 2.6f);
                        var g = Camp + new float2(math.sin(a), math.cos(a)) * r;
                        if (!Blocked(g) && math.distance(g, TentPos) > 1.2f && math.distance(g, DropPos) > 0.7f) { p.Goal = g; p.State = PersonState.Mill; }
                        else p.Timer = 1f;
                    }
                    else if (p.Hunter) TurnTo(ref p, FirePos, dt);
                    break;

                case PersonState.Mill:
                    if (MoveStraight(ref p, p.Goal, WalkSpeed * 0.7f, WalkStride, dt)) { p.State = PersonState.Idle; p.Timer = R(3f, 9f); }
                    break;

                case PersonState.ToHunt:
                {
                    if (!NearestDeer(p.Pos, out int di, out float dd) || dd > GiveUpRange * 2) { p.State = PersonState.Home; break; }
                    if (dd < StalkFrom && p.HasSpear) { p.State = PersonState.Stalk; break; }
                    var deer = _wild.Herds[HuntHerd][di];
                    p.Retarget -= dt;
                    if (p.Retarget <= 0) { p.TargetCell = _map.CellAt(deer.Pos); p.Retarget = 2f; }  // fields are per target cell: re-aim now and then
                    FollowField(ref p, p.TargetCell, deer.Pos, WalkSpeed, WalkStride, dt);
                    break;
                }

                case PersonState.Stalk:
                {
                    if (!NearestDeer(p.Pos, out int di, out float dd)) { p.State = PersonState.Home; break; }
                    if (dd > StalkFrom * 1.6f) { p.State = PersonState.ToHunt; p.Retarget = 0; break; }   // the herd got away: walk after it
                    var deer = _wild.Herds[HuntHerd][di];
                    if (dd <= ThrowRange)
                    {
                        p.State = PersonState.Throw;
                        p.Timer = 0.7f;
                        break;
                    }
                    // Creep in, each hunter a little to the side so they come at the herd from several angles.
                    float2 to = deer.Pos - p.Pos;
                    float2 side = math.normalizesafe(new float2(to.y, -to.x));
                    float spread = (k % 3 - 1) * math.min(1.5f, dd * 0.25f);
                    MoveStraight(ref p, deer.Pos + side * spread, StalkSpeed, StalkStride, dt);
                    break;
                }

                case PersonState.Throw:
                {
                    // Wind-up, facing the deer; then let fly.
                    if (NearestDeer(p.Pos, out int di, out _)) TurnTo(ref p, _wild.Herds[HuntHerd][di].Pos, dt * 2f);
                    if (p.Timer > 0) break;
                    if (di >= 0) Launch(k, _wild.Herds[HuntHerd][di]);
                    p.State = PersonState.Recover;
                    p.Timer = 0.6f;
                    break;
                }

                case PersonState.Recover:
                    if (p.Timer > 0) break;
                    if (p.SpearInCarcass || p.Spear < 0) p.State = Prey != null ? PersonState.ToCarcass : PersonState.Home;
                    else if (!Spears[p.Spear].Flying) p.State = PersonState.Fetch;
                    break;

                case PersonState.Fetch:
                {
                    if (p.Spear < 0) { p.State = PersonState.Home; break; }
                    var sp = Spears[p.Spear];
                    float2 at = sp.Pos.xz;
                    float2 dir = new(math.sin(sp.Yaw), math.cos(sp.Yaw));
                    float2 grip = at - dir * 0.2f;
                    if (math.distance(p.Pos, grip) > 3f) FollowField(ref p, _map.CellAt(grip), grip, WalkSpeed, WalkStride, dt);
                    else if (MoveStraight(ref p, grip, WalkSpeed, WalkStride, dt)) { p.State = PersonState.Pickup; p.Timer = 0.7f; }
                    break;
                }

                case PersonState.Pickup:
                    if (p.Timer > 0) break;
                    if (p.Spear >= 0) Spears[p.Spear].Gone = true;
                    p.Spear = -1; p.HasSpear = true;
                    p.State = Prey != null ? PersonState.ToCarcass : HuntHerd >= 0 ? PersonState.ToHunt : PersonState.Home;
                    p.Retarget = 0;
                    break;

                case PersonState.ToCarcass:
                {
                    if (Prey == null) { p.State = PersonState.Home; break; }
                    // Two carry it, slung under a pole: the first to arrive takes the front, the second the back.
                    bool front = Carrier < 0 || Carrier == k;
                    if (!front && Porter >= 0 && Porter != k)
                    {
                        if (p.SpearInCarcass) { p.SpearInCarcass = false; p.HasSpear = true; }
                        p.State = PersonState.Home;          // the third walks home ahead of them
                        break;
                    }
                    float2 along = new(math.sin(Prey.Yaw), math.cos(Prey.Yaw));
                    float2 slot = Prey.Pos + along * (front ? PoleHalf : -PoleHalf);
                    if (math.distance(p.Pos, slot) > 3f) FollowField(ref p, _map.CellAt(slot), slot, WalkSpeed, WalkStride, dt);
                    else if (MoveStraight(ref p, slot, WalkSpeed, WalkStride, dt))
                    {
                        if (front) Carrier = k; else Porter = k;
                        p.State = PersonState.Lift;
                        p.Timer = 0.9f;
                    }
                    break;
                }

                case PersonState.Lift:
                    // Bend to tie the legs to the pole, then stand with it on the shoulder. Both face along the deer.
                    p.Yaw = Turn(p.Yaw, Prey.Yaw, dt * 3f);
                    if (p.Timer > 0) break;
                    if (p.SpearInCarcass) { p.SpearInCarcass = false; p.HasSpear = true; }
                    p.State = PersonState.Carry;
                    break;

                case PersonState.Carry:
                {
                    if (k == Porter)
                    {
                        // The back carrier keeps a pole's length behind the front one.
                        ref var lead = ref People[Carrier];
                        float2 fwd = new(math.sin(lead.Yaw), math.cos(lead.Yaw));
                        float2 slot = lead.Pos - fwd * PoleHalf * 2f;
                        float2 before = p.Pos;
                        p.Pos = math.lerp(p.Pos, slot, math.min(1f, dt * 8f));
                        float2 slotDir = lead.Pos - p.Pos;
                        if (math.lengthsq(slotDir) > 1e-4f) p.Yaw = Turn(p.Yaw, math.atan2(slotDir.x, slotDir.y), dt * 6f);
                        p.Moving = lead.Moving;
                        p.Stride += math.length(p.Pos - before) / CarryStride;
                        break;
                    }
                    // The front carrier waits for the porter, then leads the way home.
                    if (Porter < 0 || People[Porter].State != PersonState.Carry) break;
                    if (!Prey.Carried) { Prey.Carried = true; }
                    if (math.distance(p.Pos, DropPos) > 2.5f) FollowField(ref p, _map.CellAt(Camp), DropPos, CarrySpeed, CarryStride, dt);
                    else if (MoveStraight(ref p, DropPos, CarrySpeed, CarryStride, dt))
                    {
                        // Home with the kill: lay it down by the fire.
                        Prey.Carried = false;
                        Prey.Pos = (p.Pos + People[Porter].Pos) * 0.5f;
                        Prey.Yaw = p.Yaw;
                        Prey.Age = 0;
                        Kills++;
                        People[Porter].State = PersonState.Home;
                        Prey = null; Carrier = -1; Porter = -1; HuntHerd = -1;
                        p.State = PersonState.Home;
                        break;
                    }
                    var mid = (p.Pos + People[Porter].Pos) * 0.5f;
                    Prey.CarryYaw = p.Yaw;
                    Prey.CarryPos = new float3(mid.x, _map.HeightAt(mid.x, mid.y) + 0.66f, mid.y);
                    Prey.Pos = mid;
                    break;
                }

                case PersonState.Home:
                    if (math.distance(p.Pos, p.Home) > 3f) FollowField(ref p, _map.CellAt(Camp), p.Home, WalkSpeed, WalkStride, dt);
                    else if (MoveStraight(ref p, p.Home, WalkSpeed, WalkStride, dt)) { p.State = PersonState.Idle; p.Timer = R(2f, 5f); }
                    break;
            }
        }

        // ------------------------------------------------------------------ spears

        void Launch(int k, in Deer target)
        {
            ref var p = ref People[k];
            float3 hand = HandWorld(p, (int)PersonModel.Pose.Windup);
            // Aim where the deer will be, with an aiming error that grows when it is running.
            float2 aim = target.Pos + (target.Moving ? Wildlife.RunSpeed * 0.45f * new float2(math.sin(target.Yaw), math.cos(target.Yaw)) : 0);
            float err = target.Moving ? 0.22f : 0.07f;
            float2 to = aim - hand.xz;
            float ang = math.atan2(to.x, to.y) + R(-err, err);
            float dist = math.length(to) * R(1f - err, 1f + err) + 0.2f;
            float t = dist / SpearSpeed;
            float groundAt = _map.HeightAt(aim.x, aim.y) + 0.25f;          // aim at the deer's chest
            var sp = new Spear
            {
                Pos = hand,
                Yaw = ang,
                Vel = new float3(math.sin(ang) * SpearSpeed, (groundAt - hand.y) / t + Gravity * t * 0.5f, math.cos(ang) * SpearSpeed),
                Flying = true, Owner = k, Herd = HuntHerd,
            };
            Spears.Add(sp);
            p.Spear = Spears.Count - 1;
            p.HasSpear = false;
            Throws++;
        }

        void UpdateSpears(float dt)
        {
            for (int s = 0; s < Spears.Count; s++)
            {
                var sp = Spears[s];
                if (!sp.Flying || sp.Gone) continue;
                // Small sub-steps, so a fast spear can't jump past a deer between frames.
                int steps = math.max(1, (int)math.ceil(math.length(sp.Vel) * dt / 0.1f));
                float h = dt / steps;
                for (int st = 0; st < steps && sp.Flying; st++) StepSpear(sp, h);
            }
        }

        void StepSpear(Spear sp, float dt)
        {
            {
                sp.Vel.y -= Gravity * dt;
                sp.Pos += sp.Vel * dt;
                float ground = _map.HeightAt(sp.Pos.x, sp.Pos.z);
                // Hit: the point passes through a deer's body.
                if (sp.Herd >= 0)
                {
                    var deer = _wild.Herds[sp.Herd];
                    int n = Wildlife.ActiveCount(_map.Herds[sp.Herd], deer);
                    float2 tip = sp.Pos.xz + new float2(math.sin(sp.Yaw), math.cos(sp.Yaw)) * 0.55f;
                    for (int i = 0; i < n; i++)
                    {
                        if (math.distance(deer[i].Pos, tip) > 0.38f || sp.Pos.y - ground > 0.6f) continue;
                        var c = _wild.Kill(sp.Herd, i);
                        sp.Flying = false; sp.Gone = true;
                        _wild.Alarm(sp.Herd, People[sp.Owner].Pos);       // the rest of the herd bolts
                        ref var owner = ref People[sp.Owner];
                        owner.SpearInCarcass = true; owner.Spear = -1;
                        if (Prey == null)
                        {
                            Prey = c;
                            for (int k = 0; k < People.Length; k++)
                                if (People[k].Hunter && People[k].State is PersonState.ToHunt or PersonState.Stalk or PersonState.Throw)
                                    People[k].State = PersonState.ToCarcass;
                        }
                        break;
                    }
                }
                if (sp.Flying && sp.Pos.y <= ground + 0.02f)
                {
                    sp.Pos.y = ground + 0.02f;          // a miss: it lies where it fell, and the herd bolts
                    sp.Flying = false;
                    if (sp.Herd >= 0) _wild.Alarm(sp.Herd, People[sp.Owner].Pos);
                }
            }
        }

        /// <summary>World position of a person's right hand in a pose (for spears).</summary>
        public float3 HandWorld(in Person p, int pose)
        {
            var h = PersonModel.Hand(pose);
            float s = math.sin(p.Yaw), c = math.cos(p.Yaw);
            var local = new float3(c * h.x + s * h.z, h.y, -s * h.x + c * h.z);       // same rotation as the shader
            return new float3(p.Pos.x, _map.HeightAt(p.Pos.x, p.Pos.y), p.Pos.y) + local;
        }

        /// <summary>Which mesh pose a person shows now.</summary>
        public static int PoseOf(in Person p)
        {
            int frame = (int)(p.Stride * PersonModel.Frames) % PersonModel.Frames;
            switch (p.State)
            {
                case PersonState.Throw: return (int)PersonModel.Pose.Windup;
                case PersonState.Recover: return p.Timer > 0.25f ? (int)PersonModel.Pose.Release : (int)PersonModel.Pose.Stand;
                case PersonState.Pickup:
                case PersonState.Lift: return (int)PersonModel.Pose.Bend;
                case PersonState.Carry: return (int)PersonModel.Pose.Carry0 + (p.Moving ? frame : 0);
                case PersonState.Stalk: return (int)PersonModel.Pose.Stalk0 + (p.Moving ? frame : 0);
            }
            return p.Moving ? (int)PersonModel.Pose.Walk0 + frame : (int)PersonModel.Pose.Stand;
        }

        // ------------------------------------------------------------------ movement

        bool MoveStraight(ref Person p, float2 target, float speed, float stride, float dt)
        {
            float2 delta = target - p.Pos;
            float dist = math.length(delta);
            if (dist < 0.08f) return true;
            Walk(ref p, delta / dist, math.min(dist, speed * dt), stride, dt);
            return false;
        }

        void FollowField(ref Person p, int targetCell, float2 finalPoint, float speed, float stride, float dt)
        {
            var flow = _map.Flow;
            int slot = flow.Resolve(targetCell);
            int cell = _map.CellAt(p.Pos);
            byte code = flow.Fields[slot * flow.Cells + cell];
            float2 aim;
            if (code == FlowDir.Target || code == FlowDir.None) aim = finalPoint;
            else
            {
                int2 xy = _map.XY(cell) + FlowDir.Offset(code);
                aim = new float2(xy.x + 0.5f, xy.y + 0.5f);
            }
            float2 delta = aim - p.Pos;
            float dist = math.length(delta);
            if (dist < 1e-4f) return;
            Walk(ref p, delta / dist, speed * dt, stride, dt);
        }

        void Walk(ref Person p, float2 dir, float step, float stride, float dt)
        {
            p.Yaw = Turn(p.Yaw, math.atan2(dir.x, dir.y), dt * 5f);
            float2 fwd = new(math.sin(p.Yaw), math.cos(p.Yaw));
            float along = math.max(0.25f, math.dot(fwd, dir));
            float2 move = fwd * step * along;
            if (Blocked(p.Pos + move) && !Blocked(p.Pos))
            {
                if (!Blocked(p.Pos + new float2(move.x, 0))) move = new float2(move.x, 0);
                else if (!Blocked(p.Pos + new float2(0, move.y))) move = new float2(0, move.y);
                else move = 0;
            }
            if (_map.WaterClass[_map.CellAt(p.Pos)] == 1) move *= 0.5f;     // wading
            p.Pos = math.clamp(p.Pos + move, 0.5f, new float2(_map.W - 0.5f, _map.H - 0.5f));
            p.Stride += math.length(move) / stride;
            p.Moving = true;
        }

        void TurnTo(ref Person p, float2 at, float dt)
        {
            float2 d = at - p.Pos;
            if (math.lengthsq(d) > 1e-4f) p.Yaw = Turn(p.Yaw, math.atan2(d.x, d.y), dt * 3f);
        }

        bool Blocked(float2 at) => _map.Flow.Cost[_map.CellAt(at)] == 0;

        void Separate(float dt)
        {
            const float gap = 0.4f;
            for (int a = 0; a < People.Length; a++)
            {
                if (People[a].State is PersonState.Carry or PersonState.Lift or PersonState.Pickup) continue;
                for (int b = 0; b < People.Length; b++)
                {
                    if (a == b) continue;
                    float2 d = People[a].Pos - People[b].Pos;
                    float len = math.length(d);
                    if (len >= gap || len < 1e-4f) continue;
                    People[a].Pos += d / len * (gap - len) * math.min(1f, dt * 4f);
                }
            }
        }

        static float Turn(float yaw, float target, float maxStep)
        {
            float diff = math.atan2(math.sin(target - yaw), math.cos(target - yaw));
            return yaw + math.clamp(diff, -maxStep, maxStep);
        }
    }
}
