using System;
using System.Collections.Generic;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Draws the settlement from the simulation every frame: buildings (one instanced batch per building type),
    /// construction sites rising with their material piles, citizens in their poses with what they carry, fires,
    /// the selection and the placement ghost. Nothing here is a GameObject, and nothing allocates per frame once
    /// the buffers have grown. Sim positions move in 0.25 s steps, so each citizen's drawn position follows its
    /// sim position smoothly.
    /// </summary>
    public sealed class SettlementView : IDisposable
    {
        readonly Settlement S;
        readonly GameMap M;
        readonly WorldView W;
        readonly Wildlife _wild;

        // ------------------------------------------------------------------ batches
        sealed class Batch
        {
            public InstanceBatch B;
            public InstanceData[] Buf = new InstanceData[16];
            public int N;
            public Batch(Mesh mesh, Material mat = null, bool shadows = true) { B = new InstanceBatch(mesh, 16, mat) { CastShadows = shadows }; }
            public void Add(in InstanceData d)
            {
                if (N == Buf.Length) Array.Resize(ref Buf, N * 2);
                Buf[N++] = d;
            }
            public void Flush() { B.Upload(Buf, N); B.Draw(); N = 0; }
        }

        readonly Dictionary<BuildingDef, (Batch body, Batch glow, float height)> _defs = new();
        readonly Dictionary<int, Batch> _siteMarks = new(), _scaffolds = new();
        readonly Dictionary<string, Batch> _piles = new();
        readonly Dictionary<(BuildingDef, int, int, int), Batch> _crops = new();
        readonly Dictionary<string, Batch> _ores = new();
        readonly List<Batch> _all = new();
        Batch[] _people;                  // [pose * 2 + woman]
        Batch _spearUp, _spearLevel, _rings, _markers;
        static Material _noSnow;

        // ------------------------------------------------------------------ per building / citizen view state
        float[] _base = new float[64];    // ground height per building id (NaN = not yet seen)
        byte[] _ground = new byte[64];    // 0 not yet, 1 footprint cleared to dirt, 2 restored after removal

        struct Vis { public float2 Pos; public float Yaw, Stride; public int Seen; }
        readonly Dictionary<int, Vis> _vis = new();
        readonly List<int> _stale = new();
        int _frame;
        float _anim;                      // game-time clock for work animations
        int _killsSeen;

        /// <summary>Where each drawn citizen is this frame (for clicking on people).</summary>
        public readonly List<(Citizen c, float2 pos)> Drawn = new();

        // ------------------------------------------------------------------ set by the HUD each frame
        public Building Selected;
        public Citizen SelectedCitizen;
        public BuildingDef GhostDef;
        public int2 GhostOrigin;
        public int GhostRot;
        public bool GhostValid;
        /// <summary>The person under the pointer: a faint ring.</summary>
        public Citizen HoverCitizen;
        public Building HoverBuilding;    // a faint ring under the building the pointer is on (its hover tag)
        /// <summary>Set each frame by the game: 0 by day, 1 at deep night (fires light the ground round them).</summary>
        public float Night;
        /// <summary>Set each frame by the game: the point trees are cut away round (the camera focus) and how strongly.</summary>
        public float2 CutCenter; public float CutRadius, CutStrength;
        public Building DemolishHover;
        public readonly List<(float2 center, float radius, Color color)> Radii = new();
        public bool HidePeople;           // perf probe only
        /// <summary>While placing a mine or pit: mark the cells of this deposit kind (None = no hint).</summary>
        public Deposit DepositHint;
        /// <summary>Path tiles about to be laid by a drag (drawn as markers).</summary>
        public readonly List<int2> PathPreview = new();
        public bool PathPreviewValid = true;

        static readonly uint White = Pal.Pack(Color.white), Flooded = Pal.Pack(new Color(0.62f, 0.66f, 0.72f));

        public SettlementView(Settlement s, WorldView w, Wildlife wild)
        {
            S = s; M = s.Map; W = w; _wild = wild;
            for (int i = 0; i < _base.Length; i++) _base[i] = float.NaN;
            if (_noSnow == null)
            {
                _noSnow = new Material(InstanceBatch.SharedMaterial) { name = "InstancedFlatNoSnow" };
                _noSnow.SetFloat("_Snowable", 0f);
            }
            int batches = PersonModel.PoseCount * 2;
            _people = new Batch[batches];
            for (int k = 0; k < batches; k++) _people[k] = Track(new Batch(PersonModel.Get(k / 2, (k & 1) == 1), _noSnow));
            _spearUp = Track(new Batch(PersonModel.Spear(false), _noSnow));
            _spearLevel = Track(new Batch(PersonModel.Spear(true), _noSnow));
            _rings = Track(new Batch(BuildingMeshes.Ring, _noSnow, false));
            _markers = Track(new Batch(BuildingMeshes.Marker, _noSnow, false));
            foreach (var k in BuildingMeshes.PileKinds) _piles[k] = Track(new Batch(BuildingMeshes.Pile(k)));
        }

        Batch Track(Batch b) { _all.Add(b); return b; }

        (Batch body, Batch glow, float height) Def(BuildingDef d)
        {
            if (_defs.TryGetValue(d, out var v)) return v;
            var m = BuildingMeshes.For(d);
            v = (Track(new Batch(m.body)), m.glow != null ? Track(new Batch(m.glow, _noSnow, false)) : null, m.height);
            _defs[d] = v;
            return v;
        }

        Batch SiteMarks(BuildingDef d)
        {
            int key = d.sizeX * 64 + d.sizeY;
            if (!_siteMarks.TryGetValue(key, out var b)) { b = Track(new Batch(BuildingMeshes.SiteMarks(d.sizeX, d.sizeY))); _siteMarks[key] = b; }
            return b;
        }

        Batch Scaffold(BuildingDef d)
        {
            int key = d.sizeX * 64 + d.sizeY;
            if (!_scaffolds.TryGetValue(key, out var b)) { b = Track(new Batch(BuildingMeshes.Scaffold(d.sizeX, d.sizeY))); _scaffolds[key] = b; }
            return b;
        }

        Batch PileFor(int res) => _piles[BuildingMeshes.PileKind(res >= 0 ? S.Data.Resources[res] : null)];

        Batch CropFor(BuildingDef d, int stage, int crop, int rowsFrom = 0)
        {
            var key = (d, stage, crop, rowsFrom);
            if (_crops.TryGetValue(key, out var b)) return b;
            var mesh = BuildingMeshes.Crop(d, stage, crop, rowsFrom);
            b = mesh != null ? Track(new Batch(mesh)) : null;
            _crops[key] = b;
            return b;
        }

        // ------------------------------------------------------------------ ground

        /// <summary>Ground height for a footprint: between its lowest and average corner, so little floats or sinks.</summary>
        public float BaseHeight(BuildingDef d, int2 o, int rot = 0)
        {
            var size = Settlement.Size(d, rot);
            float lo = float.MaxValue, sum = 0; int n = 0;
            for (int y = o.y; y <= o.y + size.y; y++)
                for (int x = o.x; x <= o.x + size.x; x++)
                {
                    if (x < 0 || y < 0 || x > M.W || y > M.H) continue;
                    float h = M.Corners[y * (M.W + 1) + x];
                    lo = math.min(lo, h); sum += h; n++;
                }
            return n == 0 ? 0 : math.lerp(lo, sum / n, 0.45f);
        }

        float BaseOf(Building b)
        {
            if (b.Id >= _base.Length)
            {
                int old = _base.Length;
                Array.Resize(ref _base, math.max(b.Id + 1, old * 2));
                Array.Resize(ref _ground, _base.Length);
                for (int i = old; i < _base.Length; i++) _base[i] = float.NaN;
            }
            if (float.IsNaN(_base[b.Id])) _base[b.Id] = BaseHeight(b.Def, b.Origin, b.Rot);
            return _base[b.Id];
        }

        /// <summary>Clears a footprint to trodden earth when it is placed; gives the ground back when it is removed.</summary>
        void Ground(Building b)
        {
            byte want = b.Alive ? (byte)1 : (byte)2;
            if (_ground[b.Id] == want || (!b.Alive && _ground[b.Id] == 0)) return;
            _ground[b.Id] = want;
            for (int y = b.Origin.y - 1; y <= b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x - 1; x <= b.Origin.x + b.SX; x++)
                {
                    if (!M.InBounds(x, y)) continue;
                    int c = M.Cell(x, y);
                    bool inside = x >= b.Origin.x && x < b.Origin.x + b.SX && y >= b.Origin.y && y < b.Origin.y + b.SY;
                    if (!inside && c != b.Door) continue;
                    if (b.Alive) W.SetCellMaterial(c, "dirt");
                    else if (M.BuildingAt[c] < 0) W.RestoreCellMaterial(c);
                }
        }

        // ------------------------------------------------------------------ per frame

        public void Draw(float dt)
        {
            _frame++;
            _anim += dt * S.Speed;
            SyncPaths();
            _fires.Clear();
            DrawBuildings();
            PushLights();
            DrawOres();
            DrawPeople(dt);
            DrawKills();
            DrawOverlays();
            foreach (var b in _all) b.Flush();
        }

        void DrawBuildings()
        {
            float flicker = 0.88f + 0.12f * math.sin(_anim * 9f) * math.sin(_anim * 5.3f + 1f);
            foreach (var b in S.Buildings)
            {
                if (b.Id >= _ground.Length) BaseOf(b);
                Ground(b);
                if (!b.Alive) continue;
                float y = BaseOf(b);
                var at = new float3(b.Center.x, y, b.Center.y);
                float yaw = Yaw(b.Rot);
                var (body, glow, height) = Def(b.Def);
                if (b.Active)
                {
                    body.Add(new InstanceData(at, yaw, new float3(1f), b.Flooded ? Flooded : White));
                    if (b.Def.crop)
                    {
                        // The crop: bare soil, shoots, growing, ripe, stubble; each field ripens a shade differently.
                        int kind = b.Sown > 0 ? b.CropOut : math.max(0, b.Focus);
                        float k = 0.92f + Hash(b.Id, 11) * 0.12f;
                        uint tint = Pal.Pack(new Color(k, k, k * 0.97f));
                        if (S.Season == 2 && b.Harvested > 0.01f && b.Harvested < 0.99f)
                        {
                            // Mid-harvest: stubble where it is cut, the ripe rows still standing beyond.
                            int rows = BuildingMeshes.CropRows(b.Def), cut = math.clamp((int)math.round(b.Harvested * rows), 1, rows - 1);
                            CropFor(b.Def, 4, kind)?.Add(new InstanceData(at, yaw, new float3(1f), tint));
                            CropFor(b.Def, 3, kind, cut)?.Add(new InstanceData(at, yaw, new float3(1f), tint));
                        }
                        else CropFor(b.Def, b.CropStage(S.Season), kind)?.Add(new InstanceData(at, yaw, new float3(1f), tint));
                    }
                    if (b.UpgradeTo != null) DrawRising(b, at, b.UpgradeTo, BuildingMeshes.For(b.UpgradeTo).height, false);
                    // Fires burn while there is fuel; a town fire with nothing to burn shows only ash.
                    bool lit = b.Def.warmthRadius > 0 ? b.Fueled : b.Def.knowledgePerWorker > 0 ? b.Workers > 0 : b.Workers > 0 || b.Def.maxWorkers == 0;
                    if (glow != null && lit)
                    {
                        glow.Add(new InstanceData(at, yaw, new float3(1f, flicker, 1f), White));
                        // A pool of firelight round it at night: the town fire reaches farthest.
                        float reach = b.Def.townCenter ? 5.5f : b.Def.warmthRadius > 0 || b.Def.knowledgePerWorker > 0 ? 3.6f : 2.6f;
                        _fires.Add(new Vector4(b.Center.x, y + 0.45f, b.Center.y, reach * (0.94f + 0.06f * flicker)));
                    }
                    DrawStore(b, at);
                    continue;
                }
                // A site: marked out, its materials piled up as they arrive, then the building rising inside scaffolding.
                SiteMarks(b.Def).Add(new InstanceData(at, yaw, new float3(1f), White));
                DrawRising(b, at, b.Def, height, true);
            }
        }

        /// <summary>
        /// A building going up: scaffolding and the material piles by the footprint; a new site also shows the body
        /// rising (an upgrade keeps the old building standing inside the scaffolding until it is done).
        /// </summary>
        void DrawRising(Building b, float3 at, BuildingDef d, float height, bool rises)
        {
            float rise = math.saturate((b.Progress - 0.3f) / 0.7f);
            float yaw = Yaw(b.Rot);
            if (rises && rise > 0.01f)
            {
                byte cut = (byte)math.clamp(math.round(height * rise / 4f * 255f), 1, 254);
                Def(d).body.Add(new InstanceData(at, yaw, new float3(1f), White & 0x00ffffffu | ((uint)cut << 24)));
            }
            if (rise > 0.01f || !rises && b.MaterialsFraction > 0.2f) Scaffold(d).Add(new InstanceData(at, yaw, new float3(1f), White));
            for (int i = 0; i < d.cost.Length && i < b.Delivered.Length; i++)
            {
                float need = d.cost[i].amount;
                float f = need <= 0 ? 0 : b.Delivered[i] / need * (1f - rise * 0.85f);   // used up as it rises
                if (f < 0.02f) continue;
                // Piles along the front (the door side), turned with the building.
                var local = new float2(-d.sizeX * 0.5f + 0.45f + i * 0.75f, -d.sizeY * 0.5f - 0.25f);
                if (i * 0.75f > d.sizeX - 0.6f) local = new float2(d.sizeX * 0.5f + 0.25f, -d.sizeY * 0.5f + 0.45f + (i - 2) * 0.75f);
                var p = b.Center + Turn(local, yaw);
                PileFor(S.ResIndex(d.cost[i].id)).Add(new InstanceData(new float3(p.x, M.HeightAt(p.x, p.y), p.y), i * 0.7f, new float3(0.45f + 0.55f * math.saturate(f)), White));
            }
        }

        static float Yaw(int rot) => (rot & 3) * math.PI * 0.5f;

        /// <summary>Turns a footprint-local offset like the models turn (the instance shader's yaw convention).</summary>
        static float2 Turn(float2 p, float yaw) { math.sincos(yaw, out float s, out float c); return new float2(c * p.x + s * p.y, -s * p.x + c * p.y); }

        readonly List<Vector4> _fires = new();
        readonly Vector4[] _fireArr = new Vector4[16];
        static readonly int FiresId = Shader.PropertyToID("_FL_Fires"), FireCountId = Shader.PropertyToID("_FL_FireCount"), FireStrengthId = Shader.PropertyToID("_FL_FireStrength");
        static readonly int CutId = Shader.PropertyToID("_FL_Cut"), CutStrengthId = Shader.PropertyToID("_FL_CutStrength");

        /// <summary>The 16 fires nearest the camera focus light the ground at night; the canopy cut-away point.</summary>
        void PushLights()
        {
            int n = 0;
            if (Night > 0.02f && _fires.Count > 0)
            {
                var c = CutCenter;
                _fires.Sort((a, b) => math.distancesq(new float2(a.x, a.z), c).CompareTo(math.distancesq(new float2(b.x, b.z), c)));
                for (; n < _fires.Count && n < 16; n++) _fireArr[n] = _fires[n];
            }
            Shader.SetGlobalVectorArray(FiresId, _fireArr);
            Shader.SetGlobalFloat(FireCountId, n);
            Shader.SetGlobalFloat(FireStrengthId, Night * 1.15f);
            Shader.SetGlobalVector(CutId, new Vector4(CutCenter.x, CutCenter.y, 0, CutRadius));
            Shader.SetGlobalFloat(CutStrengthId, CutStrength);
        }

        // ------------------------------------------------------------------ deposits and paths

        readonly List<(string mat, InstanceData data)> _oreList = new();
        bool _oreShown; int _oreBuildings = -1;

        /// <summary>Copper and tin outcrops once Prospecting has revealed them (not under buildings).</summary>
        void DrawOres()
        {
            if (!M.OreRevealed) return;
            if (!_oreShown || _oreBuildings != S.Buildings.Count)
            {
                _oreShown = true; _oreBuildings = S.Buildings.Count;
                _oreList.Clear();
                for (int c = 0; c < M.W * M.H; c++)
                {
                    var dep = M.Deposits[c];
                    if (dep != Deposit.Copper && dep != Deposit.Tin) continue;
                    // The stained ground shows the extent of the deposit; outcrops break through here and there.
                    if (M.BuildingAt[c] < 0 && !M.Path[c]) W.SetCellMaterial(c, dep == Deposit.Copper ? "ore_copper" : "ore_tin");
                    if (M.BuildingAt[c] >= 0 || Hash(c, 21) > (dep == Deposit.Tin ? 0.6f : 0.4f)) continue;
                    float x = c % M.W + 0.3f + Hash(c, 22) * 0.4f, y = c / M.W + 0.3f + Hash(c, 23) * 0.4f;
                    float s = 1.1f + Hash(c, 24) * 0.7f;
                    _oreList.Add((dep == Deposit.Copper ? "ore_copper" : "ore_tin", new InstanceData(new float3(x, M.HeightAt(x, y), y), Hash(c, 25) * 6.28f, new float3(s), White)));
                }
            }
            foreach (var (mat, data) in _oreList)
            {
                if (!_ores.TryGetValue(mat, out var batch)) { batch = Track(new Batch(BuildingMeshes.Ore(mat))); _ores[mat] = batch; }
                batch.Add(data);
            }
        }

        bool[] _pathShown;

        /// <summary>Path tiles show as trodden earth in the ground material map.</summary>
        void SyncPaths()
        {
            if (!M.PathsDirty) return;
            M.PathsDirty = false;
            _pathShown ??= new bool[M.W * M.H];
            for (int c = 0; c < _pathShown.Length; c++)
            {
                if (M.Path[c] == _pathShown[c]) continue;
                _pathShown[c] = M.Path[c];
                if (M.Path[c]) W.SetCellMaterial(c, "path");
                else if (M.BuildingAt[c] >= 0 || M.Reserved[c]) W.SetCellMaterial(c, "dirt");
                else W.RestoreCellMaterial(c);
            }
        }

        /// <summary>Stores show their biggest stocks as piles on the platform.</summary>
        void DrawStore(Building b, float3 at)
        {
            if (b.Store == null || b.Def.townCenter) return;
            float cap = math.max(1, S.Capacity(b));
            int shown = 0;
            for (int r = 0; r < b.Store.Length && shown < 6; r++)
            {
                float amt = b.Store[r];
                if (amt < 2f) continue;
                float f = math.saturate(amt / (cap * 0.25f));
                float a = shown * 1.047f + 0.3f;
                var p = at + new float3(math.sin(a) * 0.75f, 0.14f, math.cos(a) * 0.75f);
                PileFor(r).Add(new InstanceData(p, a, new float3(0.6f + 0.7f * f), White));
                shown++;
            }
        }

        // ------------------------------------------------------------------ people

        void DrawPeople(float dt)
        {
            Drawn.Clear();
            if (_wild != null) _wild.Threats.Clear();
            var tc = S.TownCenter;
            if (HidePeople) return;
            foreach (var c in S.Citizens) if (!c.Gone) DrawCitizen(c, tc, dt);
            foreach (var c in S.Departing) DrawCitizen(c, tc, dt);
            // Forget people who have gone.
            if (_frame % 120 == 0)
            {
                _stale.Clear();
                foreach (var kv in _vis) if (_frame - kv.Value.Seen > 60) _stale.Add(kv.Key);
                foreach (int id in _stale) _vis.Remove(id);
            }
        }

        static float Hash(int id, int salt) { uint h = (uint)(id * 73856093) ^ (uint)(salt * 19349663); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; }

        void DrawCitizen(Citizen c, Building tc, float dt)
        {
            var job = c.Job;
            bool resting = c.Activity is Activity.Resting or Activity.Sick && !c.Walking;
            if (resting && c.Home != null) return;                         // indoors
            float2 target = S.DrawPos(c);
            float faceTo = c.Facing;
            bool faceSet = false;
            int pose = (int)PersonModel.Pose.Stand;
            bool sitAround = false;

            if (!c.Walking && tc != null)
            {
                if (resting || c.Activity == Activity.Idle && !c.AtField && math.distancesq(c.Pos, tc.Center) < 36f)
                    sitAround = true;                                       // sleeping out / waiting round the fire
                else if (c.Activity == Activity.Working && job != null && !c.AtField) target = WorkSpot(c, job, out faceTo, out faceSet);
                else if (c.Activity == Activity.Building && c.TaskB2 is Building site && site.IsSite) target = BuildSpot(c, site, out faceTo, out faceSet);
                else if (c.Activity == Activity.Playing)
                {
                    float a = _anim * 1.3f + Hash(c.Id, 1) * 6.28f;
                    target = c.Pos + new float2(math.sin(a), math.cos(a)) * 0.7f;
                }
            }
            if (sitAround)
            {
                float a = Hash(c.Id, 2) * 6.283f;
                float r = 2.0f + Hash(c.Id, 3) * 0.6f;
                target = tc.Center + new float2(math.sin(a), math.cos(a)) * r;
                var d = tc.Center - target;
                faceTo = math.atan2(d.x, d.y); faceSet = true;
            }

            // Follow the sim position smoothly; snap if it jumped (arrived, teleported, new).
            if (!_vis.TryGetValue(c.Id, out var v)) v = new Vis { Pos = target, Yaw = c.Facing };
            float2 delta = target - v.Pos;
            float dist = math.length(delta);
            // At most a little faster than the fastest walk (on a path tile): a slower cap made walkers on paths trail
            // behind their sim position at 4x and slide on after stopping. DrawPos already moves smoothly between steps.
            var bal = S.Data.Balance;
            float maxStep = (bal.walkSpeed * math.max(1f, bal.pathSpeed) * math.max(S.Speed, 0.01f) * 1.25f + 0.5f) * dt;
            bool moving = false;
            if (dist > 4f) v.Pos = target;
            else if (dist > 1e-3f)
            {
                float step = math.min(dist, maxStep);
                v.Pos += delta / dist * step;
                v.Stride += step / 0.85f;
                moving = step > 0.15f * dt;
                if (moving) v.Yaw = TurnTo(v.Yaw, math.atan2(delta.x, delta.y), dt * 10f);
            }
            if (!moving) v.Yaw = TurnTo(v.Yaw, faceSet || c.AtField ? faceTo : c.Facing, dt * 6f);
            v.Seen = _frame;
            _vis[c.Id] = v;

            int carry = c.Carrying ? c.CarryRes : c.Activity == Activity.Carrying && c.LoadTotal > 0 ? c.LoadRes : -1;
            bool walking = moving || c.Walking && dist > 0.02f;
            int frame = (int)(v.Stride * PersonModel.Frames) % PersonModel.Frames;
            bool hunter = job != null && job.Def.nearby?.feature == "wildlife";
            if (walking) pose = (carry >= 0 ? (int)PersonModel.Pose.Carry0 : (int)PersonModel.Pose.Walk0) + frame;
            else if (sitAround) pose = (int)PersonModel.Pose.Sit;
            else if (c.Activity == Activity.Building) pose = Cycle(c, 0.8f, 0.45f, (int)PersonModel.Pose.Windup, (int)PersonModel.Pose.Release);
            else if (c.Activity == Activity.Working && job != null) pose = WorkPose(c, job.Def.workPose);
            else if (carry >= 0) pose = (int)PersonModel.Pose.Carry0;

            bool child = c.Age < S.Data.Balance.adultAge;
            // Drawn a touch larger than life (1.12) so people read at the usual zoom.
            float scale = (child ? math.lerp(0.55f, 0.92f, math.saturate(c.Age / S.Data.Balance.adultAge)) : 1f) * 1.12f;
            var at = new float3(v.Pos.x, M.HeightAt(v.Pos.x, v.Pos.y), v.Pos.y);
            uint tint = Pal.Pack(new Color(0.86f + Hash(c.Id, 4) * 0.18f, 0.86f + Hash(c.Id, 5) * 0.13f, 0.82f + Hash(c.Id, 6) * 0.15f));
            _people[pose * 2 + (c.Female ? 1 : 0)].Add(new InstanceData(at, v.Yaw, new float3(scale), tint));
            Drawn.Add((c, v.Pos));

            if (carry >= 0)
            {
                // The load rides on the shoulders, behind the head.
                float s = math.sin(v.Yaw), co = math.cos(v.Yaw);
                var back = new float3(-s * 0.06f, 0.69f * scale, -co * 0.06f);
                PileFor(carry).Add(new InstanceData(at + back, v.Yaw + 1.57f, new float3(0.55f * scale), White));
            }
            if (hunter && !child)
            {
                var hold = PersonModel.SpearHold(pose);
                if (hold == PersonModel.Hold.Upright) _spearUp.Add(new InstanceData(HandWorld(at, v.Yaw, pose), v.Yaw, new float3(1f), White));
                else if (hold == PersonModel.Hold.Level) _spearLevel.Add(new InstanceData(HandWorld(at, v.Yaw, pose), v.Yaw, new float3(1f), White));
                else if (hold == PersonModel.Hold.Back)
                {
                    float s = math.sin(v.Yaw), co = math.cos(v.Yaw);
                    _spearUp.Add(new InstanceData(at + new float3(co * 0.08f - s * 0.13f, 0.45f, -s * 0.08f - co * 0.13f), v.Yaw, new float3(1f), White));
                }
            }
            if (_wild != null)
            {
                bool crouched = c.AtField && hunter;
                _wild.Threats.Add((v.Pos, crouched ? 2.6f : 5f));
            }
        }

        static float3 HandWorld(float3 at, float yaw, int pose)
        {
            var h = PersonModel.Hand(pose);
            float s = math.sin(yaw), c = math.cos(yaw);
            return at + new float3(c * h.x + s * h.z, h.y, -s * h.x + c * h.z);
        }

        /// <summary>Two-pose work loop (chopping, digging, hammering), each person out of step with the others.</summary>
        int Cycle(Citizen c, float period, float firstShare, int a, int b)
        {
            float t = (_anim / period + Hash(c.Id, 7)) % 1f;
            return t < firstShare ? a : b;
        }

        int WorkPose(Citizen c, string kind)
        {
            switch (kind)
            {
                case "chop": return Cycle(c, 0.9f, 0.55f, (int)PersonModel.Pose.Windup, (int)PersonModel.Pose.Release);
                case "dig": return Cycle(c, 1.2f, 0.6f, (int)PersonModel.Pose.Bend, (int)PersonModel.Pose.Stand);
                case "bend": return Cycle(c, 5f, 0.85f, (int)PersonModel.Pose.Bend, (int)PersonModel.Pose.Stand);
                case "sit": return (int)PersonModel.Pose.Sit;
                case "stalk":
                {
                    float t = (_anim / 7f + Hash(c.Id, 8)) % 1f;
                    return t < 0.86f ? (int)PersonModel.Pose.Stalk0 : t < 0.94f ? (int)PersonModel.Pose.Windup : (int)PersonModel.Pose.Release;
                }
                default: return Cycle(c, 6f, 0.8f, (int)PersonModel.Pose.Stand, (int)PersonModel.Pose.Bend);
            }
        }

        /// <summary>Where a worker at its workplace stands (or sits): round a fire, or beside the door.</summary>
        float2 WorkSpot(Citizen c, Building b, out float face, out bool faceSet)
        {
            faceSet = true;
            float slot = (c.Id % 5) / 5f;
            if (b.Def.workPose == "sit")
            {
                float a = slot * 6.283f + 0.6f;
                float r = math.min(b.Def.sizeX, b.Def.sizeY) * 0.38f;
                var p = b.Center + new float2(math.sin(a), math.cos(a)) * r;
                var d = b.Center - p;
                face = math.atan2(d.x, d.y);
                return p;
            }
            var spot = b.DoorPos + Turn(new float2((slot - 0.4f) * 1.4f, 0.55f), Yaw(b.Rot));
            var to = b.Center - spot;
            face = math.atan2(to.x, to.y);
            return spot;
        }

        float2 BuildSpot(Citizen c, Building site, out float face, out bool faceSet)
        {
            faceSet = true;
            float a = Hash(c.Id, 9) * 6.283f;
            float r = math.max(site.Def.sizeX, site.Def.sizeY) * 0.5f + 0.35f;
            var p = site.Center + new float2(math.sin(a), math.cos(a)) * r;
            var d = site.Center - p;
            face = math.atan2(d.x, d.y);
            return p;
        }

        static float TurnTo(float yaw, float target, float maxStep)
        {
            float diff = math.atan2(math.sin(target - yaw), math.cos(target - yaw));
            return yaw + math.clamp(diff, -maxStep, maxStep);
        }

        /// <summary>
        /// An animal taken by the hunters lies in front of the hunter at that herd, and the herd bolts. Only the last
        /// few stay (scavengers take the rest), so fast-forwarding does not leave a heap.
        /// </summary>
        void DrawKills()
        {
            if (_wild == null) { _killsSeen = S.Kills.Count; return; }
            while (_killsSeen < S.Kills.Count)
            {
                var (h, _) = S.Kills[_killsSeen++];
                if (h < 0 || h >= _wild.Herds.Length) continue;
                float2 at = M.Herds[h].Pos; float yaw = 0; bool found = false;
                foreach (var (c, pos) in Drawn)
                    if (c.AtField && c.FieldFeature == h && c.Job != null && c.Job.Def.nearby?.feature == "wildlife")
                    {
                        float2 fwd = new(math.sin(c.Facing), math.cos(c.Facing));
                        at = pos + fwd * 0.9f; yaw = c.Facing + 1.4f; found = true;
                        break;
                    }
                if (!found) continue;                     // nobody there to see it (fast-forward): no carcass
                var d = _wild.Herds[h][0];
                var carcass = new Carcass { Pos = at, Yaw = yaw, Stag = false, Tint = d.Tint };
                _wild.Carcasses.Add(carcass);
                _ours.Add(carcass);
                if (_ours.Count > 3) { _wild.Carcasses.Remove(_ours[0]); _ours.RemoveAt(0); }
                _wild.Alarm(h, at);
            }
            if (S.Kills.Count > 256) { S.Kills.RemoveRange(0, 128); _killsSeen -= 128; }
        }

        readonly List<Carcass> _ours = new();

        // ------------------------------------------------------------------ selection, ghost, radii

        void DrawOverlays()
        {
            var sel = new Color(0.95f, 0.9f, 0.7f);
            if (Selected != null && Selected.Alive)
            {
                float y = BaseOf(Selected) + 0.02f;
                float r = math.max(Selected.Def.sizeX, Selected.Def.sizeY) * 0.75f;
                _rings.Add(new InstanceData(new float3(Selected.Center.x, y, Selected.Center.y), 0, new float3(r, 1, r), Pal.Pack(sel)));
            }
            if (HoverBuilding != null && HoverBuilding.Alive && HoverBuilding != Selected)
            {
                float y = BaseOf(HoverBuilding) + 0.02f;
                float r = math.max(HoverBuilding.Def.sizeX, HoverBuilding.Def.sizeY) * 0.75f;
                _rings.Add(new InstanceData(new float3(HoverBuilding.Center.x, y, HoverBuilding.Center.y), 0, new float3(r, 1, r), Pal.Pack(new Color(0.7f, 0.68f, 0.6f))));
            }
            if (SelectedCitizen != null || HoverCitizen != null)
            {
                foreach (var (c, p) in Drawn)
                {
                    if (c == SelectedCitizen) _rings.Add(new InstanceData(new float3(p.x, M.HeightAt(p.x, p.y), p.y), 0, new float3(0.45f, 1, 0.45f), Pal.Pack(sel)));
                    else if (c == HoverCitizen) _rings.Add(new InstanceData(new float3(p.x, M.HeightAt(p.x, p.y), p.y), 0, new float3(0.4f, 1, 0.4f), Pal.Pack(new Color(0.7f, 0.68f, 0.6f))));
                }
            }
            if (GhostDef != null)
            {
                var col = GhostValid ? new Color(0.45f, 0.85f, 0.45f) : new Color(0.9f, 0.35f, 0.3f);
                uint ghost = Pal.Pack(col) & 0x00ffffffu;                 // alpha 0 = ghost wash
                var center = Settlement.FootprintCenter(GhostDef, GhostOrigin, GhostRot);
                var at = new float3(center.x, BaseHeight(GhostDef, GhostOrigin, GhostRot), center.y);
                Def(GhostDef).body.Add(new InstanceData(at, Yaw(GhostRot), new float3(1f), ghost));
                SiteMarks(GhostDef).Add(new InstanceData(at, Yaw(GhostRot), new float3(1f), ghost));
                var door = Settlement.DoorCell(GhostDef, GhostOrigin, GhostRot);
                _markers.Add(new InstanceData(new float3(door.x + 0.5f, M.HeightAt(door.x + 0.5f, door.y + 0.5f), door.y + 0.5f), 0, new float3(2f, 1f, 2f), Pal.Pack(col)));
            }
            if (DemolishHover != null && DemolishHover.Alive)
            {
                var at = new float3(DemolishHover.Center.x, BaseOf(DemolishHover) + 0.01f, DemolishHover.Center.y);
                Def(DemolishHover.Def).body.Add(new InstanceData(at, Yaw(DemolishHover.Rot), new float3(1.02f), Pal.Pack(new Color(0.9f, 0.3f, 0.25f)) & 0x00ffffffu));
            }
            // Where the deposit is, while placing a mine or pit on it.
            if (DepositHint != Deposit.None && M.DepositVisible(DepositHint))
            {
                uint hint = Pal.Pack(DepositHint == Deposit.Copper ? new Color(0.5f, 0.95f, 0.75f) : DepositHint == Deposit.Tin ? new Color(0.8f, 0.8f, 0.95f) : new Color(0.95f, 0.7f, 0.5f));
                for (int c = 0; c < M.W * M.H; c++)
                {
                    if (M.Deposits[c] != DepositHint || M.BuildingAt[c] >= 0) continue;
                    float x = c % M.W + 0.5f, y = c / M.W + 0.5f;
                    _markers.Add(new InstanceData(new float3(x, M.HeightAt(x, y), y), 0, new float3(1.6f, 1f, 1.6f), hint));
                }
            }
            // A path being dragged out.
            if (PathPreview.Count > 0)
            {
                uint pc = Pal.Pack(PathPreviewValid ? new Color(0.95f, 0.85f, 0.6f) : new Color(0.9f, 0.35f, 0.3f));
                foreach (var p in PathPreview)
                    _markers.Add(new InstanceData(new float3(p.x + 0.5f, M.HeightAt(p.x + 0.5f, p.y + 0.5f), p.y + 0.5f), 0, new float3(3f, 1f, 3f), pc));
            }
            // Radius previews: studs on the ground every cell or so round the circle.
            foreach (var (center, radius, color) in Radii)
            {
                int n = math.clamp((int)(radius * 2f * math.PI / 1.1f), 12, 120);
                uint col = Pal.Pack(color);
                for (int i = 0; i < n; i++)
                {
                    float a = i * 2f * math.PI / n;
                    var p = center + new float2(math.sin(a), math.cos(a)) * radius;
                    if (p.x < 0 || p.y < 0 || p.x >= M.W || p.y >= M.H) continue;
                    _markers.Add(new InstanceData(new float3(p.x, M.HeightAt(p.x, p.y), p.y), 0, new float3(1.6f, 1f, 1.6f), col));
                }
            }
        }

        /// <summary>The drawn citizen nearest a ground point, within a radius (null if none).</summary>
        public Citizen CitizenNear(float2 p, float radius)
        {
            Citizen best = null; float bd = radius * radius;
            foreach (var (c, pos) in Drawn)
            {
                float d = math.distancesq(pos, p);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        public void Dispose()
        {
            foreach (var b in _all) b.B.Dispose();
        }
    }
}
