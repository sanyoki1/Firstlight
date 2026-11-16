using System;
using System.Collections.Generic;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Draws the map: terrain (one mesh, materials from a per-cell material map), river water, and the
    /// instanced nature (trees, rocks, herds, crossed-quad plants). Nothing here is a GameObject per object.
    /// </summary>
    public sealed class WorldView : IDisposable
    {
        readonly GameMap _map;
        GameObject _terrainGo, _waterGo;
        Mesh _terrainMesh; Vector3[] _terrainVerts;    // the map's cells come first, 4 vertices each, row by row
        Texture2D _matMap;
        byte[] _matBytes, _matOrig;
        bool _matDirty;
        InstanceBatch _conifers, _broadleaves, _rocks;

        /// <summary>
        /// One kind of ground cover (tufts, flowers, bushes, reeds, ferns, pebbles, fallen wood): every plant with the
        /// cell it stands on; only those on ground still in its natural state are drawn (not on trodden earth, paths,
        /// under buildings or fields).
        /// </summary>
        sealed class Cover
        {
            public InstanceBatch Batch; public System.Func<Mesh> Mesh; public bool UnderSnow;
            public readonly List<InstanceData> All = new(); public readonly List<int> Cell = new();
            public InstanceData[] Buf = new InstanceData[0];
            public void Add(int cell, InstanceData d) { All.Add(d); Cell.Add(cell); }
        }
        Cover _bushes, _tufts, _flowers, _reeds, _ferns, _pebbles, _logs, _stumps;
        Cover[] _covers;
        bool _coverDirty; float _coverClock;
        /// <summary>Player settings: the share of the ground cover drawn (0..1), and whether birds fly.</summary>
        public static float GroundDetail = 1f;
        public static bool BirdsOn = true;
        float _coverShare = -1f;
        readonly List<InstanceData> _tmp = new();
        Wildlife _wild;
        InstanceBatch[] _deer;                 // [pose * 2 + (stag ? 1 : 0)]
        InstanceData[][] _deerBuf;
        int[] _deerCount;
        Band _band;
        InstanceBatch[] _people;               // [pose * 2 + (woman ? 1 : 0)]
        InstanceData[][] _peopleBuf;
        int[] _peopleCount;
        InstanceBatch _spearUp, _spearLevel, _tent, _fire;
        readonly List<InstanceData> _spearUpBuf = new(), _spearLevelBuf = new();

        /// <summary>Extra deer drawn as-is (model line-ups for screenshots): pose, stag, instance.</summary>
        public readonly List<(int pose, bool stag, InstanceData data)> ExtraDeer = new();

        public float[] RiverDist;

        public WorldView(GameMap map)
        {
            _map = map;
            MaterialLibrary.DensityChanged += OnDensityChanged;
        }

        public void Build()
        {
            RiverDist = _map.RiverDistance();
            BuildMaterialMap();
            BuildTerrain();
            _conifers = new InstanceBatch(Shapes.Conifer, 4096);
            _broadleaves = new InstanceBatch(Shapes.Broadleaf, 4096);
            _rocks = new InstanceBatch(Shapes.Rock, 1024);
            int batches = DeerModel.PoseCount * 2;
            _deer = new InstanceBatch[batches]; _deerBuf = new InstanceData[batches][]; _deerCount = new int[batches];
            for (int k = 0; k < batches; k++)
            {
                _deer[k] = new InstanceBatch(DeerModel.Get(k / 2, (k & 1) == 1), 32);
                _deerBuf[k] = new InstanceData[32];
            }
            BuildDecor();
            BuildOutskirtTrees();
            RefreshTrees();
            RefreshRocks();
        }

        // ------------------------------------------------------------------ terrain

        byte LayerForCell(int c, System.Random rng)
        {
            int x = c % _map.W, y = c / _map.W;
            if (_map.Ground[c] == Ground.Water) return (byte)MaterialLibrary.Layer("bed");
            if (_map.Ground[c] == Ground.Ford) return (byte)MaterialLibrary.Layer("bank");
            if (RiverDist[c] <= 1.01f) return (byte)MaterialLibrary.Layer("bank");
            if (_map.Rock[c] > 0) return (byte)MaterialLibrary.Layer("rock_ground");
            if (_map.Deposits[c] == Deposit.Clay && !_map.Tree[c]) return (byte)MaterialLibrary.Layer("clay");   // clay beds show from the start
            int trees = 0, rocks = 0;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (!_map.InBounds(x + dx, y + dy)) continue;
                    int n = _map.Cell(x + dx, y + dy);
                    if (_map.Tree[n]) trees++;
                    if (_map.Rock[n] > 0) rocks++;
                }
            if (rocks > 0 && rng.NextDouble() < 0.5) return (byte)MaterialLibrary.Layer("rock_ground");
            if (_map.Tree[c] || trees >= 3) return (byte)MaterialLibrary.Layer("litter");
            return (byte)MaterialLibrary.Layer("grass");
        }

        void BuildMaterialMap()
        {
            var rng = new System.Random(99);
            _matBytes = new byte[_map.W * _map.H];
            for (int c = 0; c < _matBytes.Length; c++) _matBytes[c] = LayerForCell(c, rng);
            _matOrig = (byte[])_matBytes.Clone();
            _matMap = new Texture2D(_map.W, _map.H, TextureFormat.R8, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "MaterialMap",
            };
            _matMap.SetPixelData(_matBytes, 0);
            _matMap.Apply(false, false);
            Shader.SetGlobalTexture("_FL_MatMap", _matMap);
            Shader.SetGlobalVector("_FL_MatMapSize", new Vector4(_map.W, _map.H, 0, 0));
        }

        /// <summary>Changes one cell's ground material (e.g. a building clears it to dirt); uploaded once per frame.</summary>
        public void SetCellMaterial(int c, string layer)
        {
            byte v = (byte)MaterialLibrary.Layer(layer);
            if (_matBytes[c] != v) { _matBytes[c] = v; _matDirty = true; }
        }

        /// <summary>Puts a cell's generated ground material back (a building was pulled down).</summary>
        public void RestoreCellMaterial(int c)
        {
            if (_matBytes[c] != _matOrig[c]) { _matBytes[c] = _matOrig[c]; _matDirty = true; }
        }

        /// <summary>
        /// The drawn heights (<see cref="GameMap.Shown"/>) changed at corners x0..x1, y0..y1 (the ground levelled under a
        /// building, or given back): moves the terrain there, and the trees, rocks and ground cover standing on it.
        /// <paramref name="before"/> holds the heights as they were.
        /// </summary>
        public void ShownHeightsChanged(int x0, int y0, int x1, int y1, float[] before)
        {
            int w = _map.W, h = _map.H, w1 = w + 1;
            int cx0 = math.max(0, x0 - 1), cy0 = math.max(0, y0 - 1), cx1 = math.min(w - 1, x1), cy1 = math.min(h - 1, y1);
            for (int y = cy0; y <= cy1; y++)
                for (int x = cx0; x <= cx1; x++)
                {
                    int v = (y * w + x) * 4;
                    _terrainVerts[v].y = _map.Shown[y * w1 + x];
                    _terrainVerts[v + 1].y = _map.Shown[(y + 1) * w1 + x];
                    _terrainVerts[v + 2].y = _map.Shown[(y + 1) * w1 + x + 1];
                    _terrainVerts[v + 3].y = _map.Shown[y * w1 + x + 1];
                }
            _terrainMesh.SetVertices(_terrainVerts, 0, _terrainVerts.Length, UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds);
            if (_covers != null)
                foreach (var cv in _covers)
                    for (int i = 0; i < cv.All.Count; i++)
                    {
                        int c = cv.Cell[i], x = c % w, y = c / w;
                        if (x < cx0 || x > cx1 || y < cy0 || y > cy1) continue;
                        var d = cv.All[i];
                        d.pos.y += _map.HeightAt(d.pos.x, d.pos.z) - Before(d.pos.x, d.pos.z);
                        cv.All[i] = d;
                    }
            _coverDirty = true;
            _map.TreesDirty = _map.RocksDirty = true;

            float Before(float px, float pz)
            {
                float fx0 = math.clamp(px, 0, w - 0.001f), fy0 = math.clamp(pz, 0, h - 0.001f);
                int ix = (int)fx0, iy = (int)fy0;
                float fx = fx0 - ix, fy = fy0 - iy;
                return math.lerp(math.lerp(before[iy * w1 + ix], before[iy * w1 + ix + 1], fx), math.lerp(before[(iy + 1) * w1 + ix], before[(iy + 1) * w1 + ix + 1], fx), fy);
            }
        }

        // Distances from the border of the strips the outskirts are drawn in (finer near the map).
        static readonly float[] Skirt = { 0f, 2f, 5f, 10f, 18f, 30f, 46f, 64f, Look.Outskirts };

        // Per border (south, north along x; west, east along y), per corner along it: the border's height, that height
        // smoothed along the border, and how much the river is there (1 at the water, falling off over a few cells).
        float[][] _edgeH, _edgeAvg, _edgeRiver;

        void BuildOutskirtProfiles()
        {
            int w = _map.W, h = _map.H, w1 = w + 1;
            _edgeH = new float[4][]; _edgeAvg = new float[4][]; _edgeRiver = new float[4][];
            for (int side = 0; side < 4; side++)
            {
                int n = (side < 2 ? w : h) + 1;
                var e = new float[n]; var avg = new float[n]; var river = new float[n];
                for (int i = 0; i < n; i++)
                {
                    int cx = side < 2 ? i : side == 2 ? 0 : w, cy = side < 2 ? (side == 0 ? 0 : h) : i;
                    e[i] = _map.Corners[cy * w1 + cx];
                    int cellX = math.clamp(cx, 0, w - 1), cellY = math.clamp(cy, 0, h - 1);
                    river[i] = _map.Ground[cellY * w + cellX] != Ground.Grass ? 1f : 0f;
                }
                for (int pass = 0; pass < 7; pass++)       // the river's hold spreads out a few cells each side, fading
                {
                    var next = (float[])river.Clone();
                    for (int i = 0; i < n; i++) next[i] = math.max(river[i], math.max(i > 0 ? river[i - 1] : 0, i < n - 1 ? river[i + 1] : 0) - 0.15f);
                    river = next;
                }
                for (int i = 0; i < n; i++)
                {
                    float sum = 0; int cnt = 0;
                    for (int k = -14; k <= 14; k++) { int j = i + k; if (j < 0 || j >= n) continue; sum += e[j]; cnt++; }
                    avg[i] = sum / cnt;
                }
                _edgeH[side] = e; _edgeAvg[side] = avg; _edgeRiver[side] = river;
            }
        }

        static float Along(float[] a, float t)
        {
            t = math.clamp(t, 0, a.Length - 1);
            int i = math.min((int)t, a.Length - 2);
            return math.lerp(a[i], a[i + 1], t - i);
        }

        /// <summary>
        /// Ground height beyond the border: the border's own height at the edge, easing over some 25 units into gentle
        /// rolling land at the border's average level; where the river crosses the border its channel carries straight on.
        /// </summary>
        public float OutskirtHeight(float x, float z)
        {
            int w = _map.W, h = _map.H;
            float dx = x < 0 ? -x : x > w ? x - w : 0, dz = z < 0 ? -z : z > h ? z - h : 0;
            if (dx <= 0 && dz <= 0) return _map.HeightAt(x, z);
            int side = dz > 0 && dz >= dx ? (z < 0 ? 0 : 1) : (x < 0 ? 2 : 3);
            float t = side < 2 ? x : z;
            float d = math.sqrt(dx * dx + dz * dz);
            float e = Along(_edgeH[side], t), avg = Along(_edgeAvg[side], t), river = Along(_edgeRiver[side], t);
            float s = math.smoothstep(0f, 26f, d);
            float roll = (Mathf.PerlinNoise(x * 0.028f + 31.7f, z * 0.028f + 12.3f) - 0.45f) * 2.6f + (Mathf.PerlinNoise(x * 0.09f + 5.1f, z * 0.09f + 77.9f) - 0.5f) * 0.7f;
            return math.lerp(math.lerp(e, avg + roll, s), e, river);
        }

        /// <summary>The forests carry on beyond the border too (no shadows, never felled): what stands at the border, easing into its own woods.</summary>
        void BuildOutskirtTrees()
        {
            int w = _map.W, h = _map.H;
            var rng = new System.Random(777);
            var con = new List<InstanceData>(); var brd = new List<InstanceData>();
            int R = (int)(Look.Outskirts * 0.8f);
            for (int z = -R; z < h + R; z++)
                for (int x = -R; x < w + R; x++)
                {
                    if (x >= 0 && x < w && z >= 0 && z < h) continue;
                    int bx = math.clamp(x, 0, w - 1), bz = math.clamp(z, 0, h - 1);
                    float d = math.length(new float2(x - bx, z - bz));
                    if (d > R) continue;
                    // Woods at the border: the share of tree cells near the closest border cell.
                    int near = 0, cells = 0;
                    for (int oy = -2; oy <= 2; oy++)
                        for (int ox = -2; ox <= 2; ox++)
                        {
                            if (!_map.InBounds(bx + ox, bz + oy)) continue;
                            cells++; if (_map.Tree[_map.Cell(bx + ox, bz + oy)]) near++;
                        }
                    float atBorder = cells > 0 ? math.saturate(near / (cells * 0.55f)) : 0;
                    float woods = math.saturate((Mathf.PerlinNoise(x * 0.043f + 9.2f, z * 0.043f + 3.4f) - 0.47f) * 6f);
                    float p = math.lerp(atBorder, woods, math.smoothstep(2f, 22f, d)) * 0.62f;
                    if (rng.NextDouble() >= p) continue;
                    int side = z < 0 ? 0 : z >= h ? 1 : x < 0 ? 2 : 3;
                    if (Along(_edgeRiver[side], side < 2 ? x : z) > 0.35f) continue;      // not in the river's channel
                    float px = x + (float)rng.NextDouble(), pz = z + (float)rng.NextDouble();
                    float sc = 0.8f + (float)rng.NextDouble() * 0.5f;
                    bool conifer = Mathf.PerlinNoise(x * 0.06f + 55.5f, z * 0.06f + 21.1f) > 0.45f ? rng.NextDouble() < 0.85 : rng.NextDouble() < 0.2;
                    float k = 0.84f + (float)rng.NextDouble() * 0.14f;
                    uint tint = Pal.Pack(new Color(k + 0.02f, k, k - 0.02f));
                    var at = new float3(px, OutskirtHeight(px, pz) - 0.05f, pz);
                    if (conifer) con.Add(new InstanceData(at, (float)rng.NextDouble() * 6.28f, new float3(1.9f * sc, 2.3f * sc, 1.9f * sc), tint));
                    else brd.Add(new InstanceData(at, (float)rng.NextDouble() * 6.28f, new float3(2.2f * sc, 2.0f * sc, 2.2f * sc), tint));
                }
            _farConifers = new InstanceBatch(Shapes.Conifer, con.Count) { CastShadows = false };
            _farBroadleaves = new InstanceBatch(Shapes.Broadleaf, brd.Count) { CastShadows = false };
            _farConifers.Upload(con.ToArray(), con.Count);
            _farBroadleaves.Upload(brd.ToArray(), brd.Count);
        }

        InstanceBatch _farConifers, _farBroadleaves;

        void BuildTerrain()
        {
            Look.SetMap(_map.W, _map.H);
            int w = _map.W, h = _map.H, w1 = w + 1;
            int inner = w * h;
            int quads = inner + (2 * w + 2 * h) * (Skirt.Length - 1) + 4 * (Skirt.Length - 1) * (Skirt.Length - 1);
            var verts = new Vector3[quads * 4];
            var tris = new int[quads * 6];
            var cols = new Color[quads * 4];
            var mats = new Vector2[quads * 4];
            var normals = new Vector3[quads * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int q = y * w + x, v = q * 4;
                    verts[v] = new Vector3(x, _map.Shown[y * w1 + x], y);
                    verts[v + 1] = new Vector3(x, _map.Shown[(y + 1) * w1 + x], y + 1);
                    verts[v + 2] = new Vector3(x + 1, _map.Shown[(y + 1) * w1 + x + 1], y + 1);
                    verts[v + 3] = new Vector3(x + 1, _map.Shown[y * w1 + x + 1], y);
                    for (int k = 0; k < 4; k++) { cols[v + k] = Color.white; normals[v + k] = Vector3.up; }
                    int t = q * 6;
                    // Alternate the diagonal for a less regular facet pattern.
                    if (((x + y) & 1) == 0) { tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3; }
                    else { tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 3; tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3; }
                }
            // The outskirts: the land carries on past the map's border until it is lost in the haze (FL_Fog). Never walked on.
            BuildOutskirtProfiles();
            int qn = inner;
            void Quad(float x0, float z0, float x1, float z1)
            {
                int v = qn * 4, t = qn * 6;
                verts[v] = new Vector3(x0, OutskirtHeight(x0, z0), z0); verts[v + 1] = new Vector3(x0, OutskirtHeight(x0, z1), z1);
                verts[v + 2] = new Vector3(x1, OutskirtHeight(x1, z1), z1); verts[v + 3] = new Vector3(x1, OutskirtHeight(x1, z0), z0);
                for (int k = 0; k < 4; k++) { cols[v + k] = Color.white; normals[v + k] = Vector3.up; }
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                qn++;
            }
            for (int k = 0; k < Skirt.Length - 1; k++)
            {
                float d0 = Skirt[k], d1 = Skirt[k + 1];
                for (int x = 0; x < w; x++) { Quad(x, -d1, x + 1, -d0); Quad(x, h + d0, x + 1, h + d1); }      // south, north
                for (int y = 0; y < h; y++) { Quad(-d1, y, -d0, y + 1); Quad(w + d0, y, w + d1, y + 1); }      // west, east
                for (int j = 0; j < Skirt.Length - 1; j++)
                {
                    float e0 = Skirt[j], e1 = Skirt[j + 1];
                    Quad(-d1, -e1, -d0, -e0); Quad(w + d0, -e1, w + d1, -e0); Quad(-d1, h + e0, -d0, h + e1); Quad(w + d0, h + e0, w + d1, h + e1);
                }
            }
            var mesh = new Mesh { name = "Terrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.colors = cols; mesh.normals = normals;
            mesh.SetUVs(0, new Vector2[verts.Length]);
            mesh.SetUVs(1, mats);
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _terrainMesh = mesh; _terrainVerts = verts;
            _terrainGo = new GameObject("Terrain");
            _terrainGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mat = WorldLighting.FlatMaterial(Color.white);
            mat.SetFloat("_UseMatMap", 1);
            _terrainGo.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ------------------------------------------------------------------ water

        WaterSim _water;
        Mesh _waterMesh;
        Vector3[] _wv; Color[] _wc; Vector2[] _wuv, _wmat; int[] _wt;
        float[] _cs, _cd; Vector2[] _cvel;
        public float WaterFlowScale = 1f;
        // The surface is rebuilt only when the water changed (a water step, or the flood fill), and only over the cells
        // that can be wet: the river, every cell low enough for the biggest flood, and any cell ever seen wet, plus a
        // margin. A cell found wet outside that mask grows it at once, so nothing wet is ever left undrawn.
        int _waterVersion = -1; float _builtFlowScale;
        int _waterLayer;
        float[] _wDepth, _wBed; float2[] _wVel;  // managed copies, read many times per rebuild
        bool[] _canBeWet;                        // the mask
        readonly List<int> _quadCells = new();   // cells within 1 of the mask, ascending: the only ones that can get a quad
        readonly List<int> _maskCorners = new(); // the corners of those cells, ascending
        bool _maskDirty = true;

        /// <summary>The water surface is rebuilt from the simulation whenever it changes (only wet cells get quads).</summary>
        public void AttachWater(WaterSim water)
        {
            _water = water;
            int n = _map.W * _map.H, nc = (_map.W + 1) * (_map.H + 1);
            int room = n + 2 * (_map.W + _map.H);   // every cell, and a strip beyond each border cell (the river carries on)
            _wv = new Vector3[room * 4]; _wc = new Color[room * 4]; _wuv = new Vector2[room * 4]; _wmat = new Vector2[room * 4];
            _wt = new int[room * 6];
            _cs = new float[nc]; _cd = new float[nc]; _cvel = new Vector2[nc];
            _wDepth = new float[n]; _wBed = new float[n]; _wVel = new float2[n];
            _water.Bed.CopyTo(_wBed);
            _waterLayer = MaterialLibrary.Layer("water");
            // The mask starts as the river and every cell whose bed lies below the river's level plus more than the
            // biggest rise (a full-strength spring flood, or the F6 surge); the depth scan adds anything else.
            const float maxRise = 0.8f;   // > GameController.FloodPeak (0.6) and > floodStrengthMax (0.55) * 0.85
            _canBeWet = new bool[n];
            for (int c = 0; c < n; c++)
                _canBeWet[c] = _map.Ground[c] != Ground.Grass || _wBed[c] < _map.RiverLevelAt(c / _map.W) + maxRise;
            _waterMesh = new Mesh { name = "Water", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            _waterMesh.MarkDynamic();
            _waterGo = new GameObject("Water");
            _waterGo.AddComponent<MeshFilter>().sharedMesh = _waterMesh;
            var mr = _waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = WorldLighting.FlatMaterial(Color.white);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            UpdateWaterMesh();
        }

        const float WetDepth = 0.015f;

        /// <summary>Recomputes the cells that can get a quad (the mask grown by one cell) and their corners.</summary>
        void RebuildWaterMask()
        {
            _maskDirty = false;
            int w = _map.W, h = _map.H, w1 = w + 1;
            _quadCells.Clear(); _maskCorners.Clear();
            var corner = new bool[w1 * (h + 1)];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool near = false;
                    for (int dy = -1; dy <= 1 && !near; dy++)
                        for (int dx = -1; dx <= 1 && !near; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx >= 0 && ny >= 0 && nx < w && ny < h && _canBeWet[ny * w + nx]) near = true;
                        }
                    if (!near) continue;
                    _quadCells.Add(y * w + x);
                    corner[y * w1 + x] = corner[y * w1 + x + 1] = corner[(y + 1) * w1 + x] = corner[(y + 1) * w1 + x + 1] = true;
                }
            for (int ci = 0; ci < corner.Length; ci++) if (corner[ci]) _maskCorners.Add(ci);
        }

        bool WetNeighbour(int x, int y, int w, int h)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (_wDepth[ny * w + nx] > WetDepth) return true;
                }
            return false;
        }

        void UpdateWaterMesh()
        {
            _waterVersion = _water.Version; _builtFlowScale = WaterFlowScale;
            int w = _map.W, h = _map.H, w1 = w + 1, n = w * h;
            _water.Depth.CopyTo(_wDepth);
            _water.Velocity.CopyTo(_wVel);
            var depth = _wDepth; var bed = _wBed; var vel = _wVel;
            // Anything wet outside the mask grows it (a bigger flood than expected, water pooling somewhere new).
            int grew = 0;
            for (int c = 0; c < n; c++)
                if (depth[c] > WetDepth && !_canBeWet[c]) { _canBeWet[c] = true; _maskDirty = true; grew++; }
            if (_maskDirty)
            {
                RebuildWaterMask();
                Debug.Log($"[Water] surface mask: {_quadCells.Count} of {n} cells{(grew > 0 ? $" (grew by {grew} cells found wet outside it)" : "")}");
            }
            // Corner surface heights (only corners of cells that can get a quad are ever read).
            foreach (int ci in _maskCorners)
            {
                int cx = ci % w1, cy = ci / w1;
                float s = 0, d = 0; float2 v = 0; int k = 0;
                for (int dy = -1; dy <= 0; dy++)
                    for (int dx = -1; dx <= 0; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        int c = y * w + x;
                        float dc = depth[c];
                        if (dc <= WetDepth) continue;
                        s += bed[c] + dc; d += dc; v += vel[c]; k++;
                    }
                if (k == 0)
                {
                    // Skirt corner: take the surface of wet cells a little further out, so the water plane
                    // continues under the bank and the shoreline is where terrain and water meet.
                    for (int dy = -2; dy <= 1; dy++)
                        for (int dx = -2; dx <= 1; dx++)
                        {
                            int x = cx + dx, y = cy + dy;
                            if (x < 0 || y < 0 || x >= w || y >= h) continue;
                            int c = y * w + x;
                            if (depth[c] <= WetDepth) continue;
                            s += bed[c] + depth[c]; v += vel[c]; k++;
                        }
                }
                if (k > 0) { _cs[ci] = s / k; _cd[ci] = d / k; _cvel[ci] = new Vector2(v.x, v.y) / k; }
                else { _cs[ci] = -99; _cd[ci] = 0; _cvel[ci] = Vector2.zero; }
            }
            var mat = new Vector2(_waterLayer, 2);
            int nv = 0, nt = 0;
            foreach (int c in _quadCells)
            {
                int x = c % w, y = c / w;
                if (depth[c] <= WetDepth && !WetNeighbour(x, y, w, h)) continue;
                int v0 = nv;
                for (int q = 0; q < 4; q++)
                {
                    int cxx = x + (q >= 2 ? 1 : 0), cyy = y + (q == 1 || q == 2 ? 1 : 0);
                    int ci = cyy * w1 + cxx;
                    _wv[nv] = new Vector3(cxx, _cs[ci] - 0.01f, cyy);
                    _wc[nv] = new Color(1, 1, 1, Mathf.Clamp01(_cd[ci] / 0.45f));
                    _wuv[nv] = _cvel[ci] * WaterFlowScale;
                    _wmat[nv] = mat;
                    nv++;
                }
                _wt[nt++] = v0; _wt[nt++] = v0 + 1; _wt[nt++] = v0 + 2;
                _wt[nt++] = v0; _wt[nt++] = v0 + 2; _wt[nt++] = v0 + 3;
                // Water at the border carries on over the outskirts at the same level and flow, into the haze.
                const float R = Look.Outskirts;
                if (depth[c] > WetDepth)
                {
                    if (y == 0) Strip(x, -R, x, 0, x + 1, 0, x + 1, -R, y * w1 + x, y * w1 + x, y * w1 + x + 1, y * w1 + x + 1, mat, ref nv, ref nt);
                    if (y == h - 1) Strip(x, h, x, h + R, x + 1, h + R, x + 1, h, h * w1 + x, h * w1 + x, h * w1 + x + 1, h * w1 + x + 1, mat, ref nv, ref nt);
                    if (x == 0) Strip(-R, y, -R, y + 1, 0, y + 1, 0, y, y * w1, (y + 1) * w1, (y + 1) * w1, y * w1, mat, ref nv, ref nt);
                    if (x == w - 1) Strip(w, y, w, y + 1, w + R, y + 1, w + R, y, y * w1 + w, (y + 1) * w1 + w, (y + 1) * w1 + w, y * w1 + w, mat, ref nv, ref nt);
                }
            }
            _waterMesh.Clear(false);
            _waterMesh.SetVertices(_wv, 0, nv);
            _waterMesh.SetColors(_wc, 0, nv);
            _waterMesh.SetUVs(0, _wuv, 0, nv);
            _waterMesh.SetUVs(1, _wmat, 0, nv);
            _waterMesh.SetTriangles(_wt, 0, nt, 0, false);
            _waterMesh.bounds = new Bounds(new Vector3(w / 2f, 0, h / 2f), new Vector3(w + 2 * Look.Outskirts, 20, h + 2 * Look.Outskirts));
        }

        /// <summary>One water quad beyond the border; each of its four points takes the level, depth and flow of a border corner.</summary>
        void Strip(float ax, float az, float bx, float bz, float cx, float cz, float dx, float dz, int ca, int cb, int cc, int cd, Vector2 mat, ref int nv, ref int nt)
        {
            int v0 = nv;
            Put(ax, az, ca, mat, ref nv); Put(bx, bz, cb, mat, ref nv); Put(cx, cz, cc, mat, ref nv); Put(dx, dz, cd, mat, ref nv);
            _wt[nt++] = v0; _wt[nt++] = v0 + 1; _wt[nt++] = v0 + 2;
            _wt[nt++] = v0; _wt[nt++] = v0 + 2; _wt[nt++] = v0 + 3;
        }

        void Put(float x, float z, int corner, Vector2 mat, ref int nv)
        {
            _wv[nv] = new Vector3(x, _cs[corner] - 0.01f, z);
            _wc[nv] = new Color(1, 1, 1, Mathf.Clamp01(_cd[corner] / 0.45f));
            _wuv[nv] = _cvel[corner] * WaterFlowScale;
            _wmat[nv] = mat;
            nv++;
        }

        // ------------------------------------------------------------------ nature

        readonly List<InstanceData> _con = new(), _brd = new();
        InstanceData[] _treeArr = new InstanceData[1024];

        public void RefreshTrees()
        {
            var con = _con; var brd = _brd;
            con.Clear(); brd.Clear();
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (!_map.Tree[c]) continue;
                int x = c % _map.W, y = c / _map.W;
                float px = x + _map.TreeOffset[c].x, pz = y + _map.TreeOffset[c].y;
                float s = _map.TreeScale[c];
                uint tint = Tint(c, 0.86f, 1f);
                if (_map.TreeKind[c] == 0)
                    con.Add(new InstanceData(new float3(px, _map.HeightAt(px, pz) - 0.05f, pz), _map.TreeYaw[c], new float3(1.9f * s, 2.3f * s, 1.9f * s), tint));
                else
                    brd.Add(new InstanceData(new float3(px, _map.HeightAt(px, pz) - 0.05f, pz), _map.TreeYaw[c], new float3(2.2f * s, 2.0f * s, 2.2f * s), tint));
            }
            UploadList(_conifers, con);
            UploadList(_broadleaves, brd);
            _map.TreesDirty = false;
        }

        public void RefreshRocks()
        {
            _tmp.Clear();
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (_map.Rock[c] <= 0) continue;
                int x = c % _map.W, y = c / _map.W;
                float k = Mathf.Clamp(_map.Rock[c] / 30f, 0.35f, 1.2f);
                float px = x + 0.5f, pz = y + 0.5f;
                _tmp.Add(new InstanceData(new float3(px, _map.HeightAt(px, pz), pz), Hash01(c) * 6.28f,
                    new float3(1.2f, 1.0f, 1.2f) * (0.7f + 0.5f * k), Tint(c, 0.85f, 1f)));
            }
            UploadList(_rocks, _tmp);
            _map.RocksDirty = false;
        }

        void UploadList(InstanceBatch batch, List<InstanceData> list)
        {
            if (_treeArr.Length < list.Count) _treeArr = new InstanceData[Mathf.NextPowerOfTwo(list.Count)];
            list.CopyTo(_treeArr);
            batch.Upload(_treeArr, list.Count);
        }

        void BuildDecor()
        {
            var rng = new System.Random(4242);
            Cover Make(System.Func<Mesh> mesh, bool sprite, bool shadows, bool underSnow) => new Cover { Mesh = mesh, UnderSnow = underSnow, Batch = new InstanceBatch(mesh(), 1, sprite ? InstanceBatch.DoubleSidedMaterial : null) { CastShadows = shadows } };
            _bushes = Make(() => Shapes.Bush, true, true, true);
            _tufts = Make(() => Shapes.Tuft, true, false, true);
            _flowers = Make(() => Shapes.Flowers, true, false, true);
            _reeds = Make(() => Shapes.Reeds, true, false, false);
            _ferns = Make(() => Shapes.Fern, true, false, true);
            _pebbles = Make(() => Shapes.Rock, false, false, false);
            _logs = Make(() => Shapes.Log, false, true, false);
            _stumps = Make(() => Shapes.Stump, false, true, false);
            _covers = new[] { _bushes, _tufts, _flowers, _reeds, _ferns, _pebbles, _logs, _stumps };
            int grass = MaterialLibrary.Layer("grass"), litter = MaterialLibrary.Layer("litter");
            bool reeds = MaterialLibrary.HasLayer("reeds"), ferns = MaterialLibrary.HasLayer("fern");
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (_map.Tree[c] && _matBytes[c] != litter) continue;
                if (_map.Rock[c] > 0 || _map.Ground[c] != Ground.Grass) continue;
                int x = c % _map.W, y = c / _map.W;
                int trees = 0, rocks = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!_map.InBounds(x + dx, y + dy)) continue;
                        int n = _map.Cell(x + dx, y + dy);
                        if (_map.Tree[n]) trees++;
                        if (_map.Rock[n] > 0) rocks++;
                    }
                // The river's bank: reeds at the water's edge, pebbles on the shingle.
                if (RiverDist[c] <= 2.01f)
                {
                    if (_map.Tree[c]) continue;
                    if (reeds && RiverDist[c] <= 1.01f && rng.NextDouble() < 0.5)
                        for (int k = rng.Next(1, 4); k > 0; k--) _reeds.Add(c, Plant(x, y, rng));
                    if (rng.NextDouble() < 0.55)
                        for (int k = rng.Next(1, 4); k > 0; k--) _pebbles.Add(c, Pebble(x, y, rng, c * 31 + k));
                    continue;
                }
                if (rocks > 0 && rng.NextDouble() < 0.6)
                    for (int k = rng.Next(1, 3); k > 0; k--) _pebbles.Add(c, Pebble(x, y, rng, c * 31 + k));
                if (_map.Tree[c])
                {
                    // Under the trees: ferns between the trunks.
                    if (ferns && rng.NextDouble() < 0.22) _ferns.Add(c, Plant(x, y, rng));
                    continue;
                }
                bool edge = trees >= 1 && trees <= 5;
                if (edge && rng.NextDouble() < 0.35) _bushes.Add(c, Plant(x, y, rng));
                if (_matBytes[c] == litter)
                {
                    // Forest floor in the gaps: ferns, now and then a fallen trunk or a stump.
                    if (ferns && rng.NextDouble() < 0.45)
                        for (int k = rng.Next(1, 3); k > 0; k--) _ferns.Add(c, Plant(x, y, rng));
                    double roll = rng.NextDouble();
                    if (roll < 0.03) _logs.Add(c, Wood(x, y, rng, 0.8f, 1.15f));
                    else if (roll < 0.055) _stumps.Add(c, Wood(x, y, rng, 0.8f, 1.3f));
                }
                if (_matBytes[c] == grass || _matBytes[c] == litter)
                {
                    int tufts = rng.NextDouble() < 0.7 ? rng.Next(1, 5) : 0;
                    for (int k = 0; k < tufts; k++) _tufts.Add(c, Plant(x, y, rng));
                    // Flowers come in drifts: a cell with flowers has several, and its neighbours often do too.
                    if (_matBytes[c] == grass && Mathf.PerlinNoise(x * 0.11f + 40f, y * 0.11f + 90f) > 0.62f && rng.NextDouble() < 0.5)
                        for (int k = rng.Next(1, 4); k > 0; k--) _flowers.Add(c, Plant(x, y, rng));
                    else if (rng.NextDouble() < 0.03) _flowers.Add(c, Plant(x, y, rng));
                }
            }
            RefreshCover();
        }

        /// <summary>Uploads the ground cover that stands on ground still in its natural state.</summary>
        void RefreshCover()
        {
            _coverDirty = false;
            _coverShare = GroundDetail;
            foreach (var cv in _covers)
            {
                if (cv.Buf.Length < cv.All.Count) cv.Buf = new InstanceData[cv.All.Count];
                int n = 0;
                for (int i = 0; i < cv.All.Count; i++)
                {
                    int c = cv.Cell[i];
                    // The share to draw is picked plant by plant (the same ones every time), not cell by cell.
                    if (_matBytes[c] == _matOrig[c] && (GroundDetail >= 0.999f || Hash01(i * 2 + 17) < GroundDetail)) cv.Buf[n++] = cv.All[i];
                }
                cv.Batch.Upload(cv.Buf, n);
            }
        }

        InstanceData Plant(int x, int y, System.Random rng)
        {
            float px = x + (float)rng.NextDouble(), pz = y + (float)rng.NextDouble();
            // Plants are drawn at their true pixel density (scale 1), so their pixels match the ground's.
            return new InstanceData(new float3(px, _map.HeightAt(px, pz) - 0.02f, pz), (float)rng.NextDouble() * 6.28f, new float3(1f), Pal.Pack(new Color(0.92f + (float)rng.NextDouble() * 0.08f, 0.95f, 0.9f)));
        }

        InstanceData Pebble(int x, int y, System.Random rng, int seed)
        {
            float px = x + (float)rng.NextDouble(), pz = y + (float)rng.NextDouble();
            float s = 0.1f + (float)rng.NextDouble() * (float)rng.NextDouble() * 0.26f;
            return new InstanceData(new float3(px, _map.HeightAt(px, pz) - 0.01f, pz), (float)rng.NextDouble() * 6.28f, new float3(s, s * 0.8f, s), Tint(seed, 0.8f, 1f));
        }

        InstanceData Wood(int x, int y, System.Random rng, float sMin, float sMax)
        {
            float px = x + 0.3f + (float)rng.NextDouble() * 0.4f, pz = y + 0.3f + (float)rng.NextDouble() * 0.4f;
            float s = sMin + (float)rng.NextDouble() * (sMax - sMin);
            float k = 0.8f + (float)rng.NextDouble() * 0.2f;
            return new InstanceData(new float3(px, _map.HeightAt(px, pz), pz), (float)rng.NextDouble() * 6.28f, new float3(s), Pal.Pack(new Color(k, k, k * 0.97f)));
        }

        void OnDensityChanged()
        {
            if (_covers == null) return;
            foreach (var cv in _covers) cv.Batch.Mesh = cv.Mesh();
        }

        static float Hash01(int c) { uint h = (uint)c * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xffffff) / 16777216f; }

        static uint Tint(int seed, float lo, float hi)
        {
            // Hash, not System.Random: trees are re-uploaded whenever one is felled or grows (no garbage).
            float k = lo + Hash01(seed * 7919) * (hi - lo);
            float warm = Hash01(seed * 7919 + 1) * 0.05f;
            return Pal.Pack(new Color(k + warm, k, k - warm));
        }

        // ------------------------------------------------------------------ per frame

        public void Draw(float time)
        {
            // PERF PROBE: Begin/End marks (no-ops unless -perfprobe)
            PerfProbe.Begin(PerfProbe.S.Trees);
            if (_map.TreesDirty) RefreshTrees();
            PerfProbe.End(PerfProbe.S.Trees);
            PerfProbe.Begin(PerfProbe.S.Rocks);
            if (_map.RocksDirty) RefreshRocks();
            PerfProbe.End(PerfProbe.S.Rocks);
            PerfProbe.Begin(PerfProbe.S.MatMap);
            if (_matDirty)
            {
                _matDirty = false;
                _coverDirty = true;
                _matMap.SetPixelData(_matBytes, 0);
                _matMap.Apply(false, false);
            }
            PerfProbe.End(PerfProbe.S.MatMap);
            UpdateDeer();
            UpdatePeople();
            PerfProbe.Begin(PerfProbe.S.WaterMesh);
            if (_water != null && (_water.Version != _waterVersion || WaterFlowScale != _builtFlowScale)) UpdateWaterMesh();
            PerfProbe.End(PerfProbe.S.WaterMesh);
            _conifers.Draw(); _broadleaves.Draw(); _rocks.Draw();
            _farConifers.Draw(); _farBroadleaves.Draw();
            foreach (var batch in _deer) batch.Draw();
            if (_people != null)
            {
                foreach (var batch in _people) batch.Draw();
                _spearUp.Draw(); _spearLevel.Draw(); _tent.Draw(); _fire.Draw();
            }
            // Ground cover follows the ground: gone where it has been trodden, built on or tilled (checked a few times a second).
            _coverClock += Time.unscaledDeltaTime;
            if (_coverShare != GroundDetail) _coverDirty = true;
            if (_coverDirty && _coverClock > 0.3f) { _coverClock = 0; RefreshCover(); }
            foreach (var cv in _covers) if (!(HideGroundCover && cv.UnderSnow)) cv.Batch.Draw();
            DrawBirds();
            DrawSnowfall();
        }

        // ------------------------------------------------------------------ birds

        // A few flocks that circle high over the land by day: a little life in the air, seen from the usual view as
        // small dark shapes crossing the ground. Purely for show (not in the simulation).
        struct Flock { public float2 Center; public float Radius, Speed, Phase, Height; public int Count; }
        Flock[] _flocks;
        InstanceBatch _birds0, _birds1;
        InstanceData[] _birdBuf0 = new InstanceData[64], _birdBuf1 = new InstanceData[64];
        float _birdTime;
        /// <summary>0 = none in the air (night), 1 = all of them.</summary>
        public float BirdsOut = 1f;

        void DrawBirds()
        {
            if (_flocks == null)
            {
                var rng = new System.Random(_map.W * 31 + 5);
                _flocks = new Flock[5];
                for (int i = 0; i < _flocks.Length; i++)
                    _flocks[i] = new Flock
                    {
                        Center = new float2(_map.W * (0.15f + 0.7f * (float)rng.NextDouble()), _map.H * (0.15f + 0.7f * (float)rng.NextDouble())),
                        Radius = 14f + 22f * (float)rng.NextDouble(), Speed = (rng.NextDouble() < 0.5 ? -1f : 1f) * (0.09f + 0.07f * (float)rng.NextDouble()),
                        Phase = (float)rng.NextDouble() * 6.28f, Height = 7f + 4f * (float)rng.NextDouble(), Count = 5 + rng.Next(0, 8),
                    };
                _birds0 = new InstanceBatch(Shapes.Bird(0), 64);
                _birds1 = new InstanceBatch(Shapes.Bird(1), 64);
            }
            _birdTime += Time.deltaTime;
            int n0 = 0, n1 = 0;
            if (BirdsOut > 0.05f && BirdsOn)
                for (int f = 0; f < _flocks.Length; f++)
                {
                    var fl = _flocks[f];
                    int count = (int)math.round(fl.Count * BirdsOut);
                    for (int i = 0; i < count; i++)
                    {
                        // Each bird trails the flock's lead round the circle, weaving a little, and flaps out of step.
                        float h1 = Hash01(f * 97 + i * 13), h2 = Hash01(f * 53 + i * 7 + 1);
                        float a = fl.Phase + _birdTime * fl.Speed - math.sign(fl.Speed) * i * 0.035f + (h1 - 0.5f) * 0.12f;
                        float r = fl.Radius + (h2 - 0.5f) * 4f + math.sin(_birdTime * 0.7f + i) * 0.6f;
                        var p = fl.Center + new float2(math.cos(a), math.sin(a)) * r;
                        float yaw = math.atan2(-math.sin(a), math.cos(a)) + (fl.Speed > 0 ? 0 : math.PI);
                        float ground = _map.HeightAt(math.clamp(p.x, 0, _map.W - 0.01f), math.clamp(p.y, 0, _map.H - 0.01f));
                        var inst = new InstanceData(new float3(p.x, ground + fl.Height + (h1 - 0.5f) * 1.5f, p.y), yaw, new float3(1.6f), Pal.Pack(Color.white));
                        bool up = ((_birdTime * 2.6f + h2 * 5f) % 1f) < 0.5f;
                        if (up) { if (n0 < _birdBuf0.Length) _birdBuf0[n0++] = inst; }
                        else if (n1 < _birdBuf1.Length) _birdBuf1[n1++] = inst;
                    }
                }
            _birds0.Upload(_birdBuf0, n0); _birds1.Upload(_birdBuf1, n1);
            _birds0.Draw(); _birds1.Draw();
        }

        // ------------------------------------------------------------------ falling snow

        // Flakes fall through a box round the point the camera looks at, wrapping at its sides so the same flakes serve
        // wherever the camera goes; the box and the flakes grow with the zoom so snowfall shows near and far.
        /// <summary>0 = none, 1 = thick snowfall.</summary>
        public float Snowfall;
        public Vector3 SnowFocus; public float SnowDistance = 40f;
        InstanceBatch _flakes;
        readonly InstanceData[] _flakeBuf = new InstanceData[1600];
        static Material _flakeMat;
        float _snowTime;

        static float Wrap(float v, float size) => v - math.floor(v / size) * size;

        void DrawSnowfall()
        {
            if (Snowfall <= 0.02f) return;
            if (_flakes == null)
            {
                if (_flakeMat == null)
                {
                    // The smoke material's soft all-round lighting, fully solid: flakes are small pale dots.
                    _flakeMat = new Material(InstanceBatch.SharedMaterial) { name = "InstancedFlatSnowflake" };
                    _flakeMat.SetFloat("_Snowable", 0f);
                    _flakeMat.SetFloat("_Smoke", 1f);
                }
                _flakes = new InstanceBatch(Shapes.Puff, _flakeBuf.Length, _flakeMat) { CastShadows = false };
            }
            _snowTime += Time.deltaTime;
            float r = math.clamp(SnowDistance * 0.95f, 9f, 75f), box = r * 2f, high = r * 0.9f;
            float size = 0.028f + SnowDistance * 0.0017f;
            float ground = SnowFocus.y - 0.5f;
            int n = (int)(_flakeBuf.Length * math.saturate(Snowfall));
            uint white = Pal.Pack(new Color(1f, 1f, 1f, 1f));
            for (int i = 0; i < n; i++)
            {
                float h1 = Hash01(i * 3 + 1), h2 = Hash01(i * 3 + 2), h3 = Hash01(i * 3 + 3), h4 = Hash01(i * 7 + 5);
                float fall = (0.9f + h4 * 0.7f) * (0.5f + r / 22f);
                float sway = math.sin(_snowTime * (0.7f + h3) + h1 * 40f) * 0.35f;
                float wx = h1 * box + _snowTime * 0.6f + sway, wz = h2 * box + _snowTime * 0.35f + sway * 0.6f;
                float x = SnowFocus.x - r + Wrap(wx - SnowFocus.x, box), z = SnowFocus.z - r + Wrap(wz - SnowFocus.z, box);
                float y = ground + Wrap(h3 * high - _snowTime * fall, high);
                float s = size * (0.7f + h4 * 0.6f);
                _flakeBuf[i] = new InstanceData(new float3(x, y, z), h1 * 6.28f, new float3(s), white);
            }
            _flakes.Upload(_flakeBuf, n);
            _flakes.Draw();
        }

        /// <summary>Bushes, grass tufts and flowers are not drawn (snow lies over them).</summary>
        public bool HideGroundCover;

        public void AttachWildlife(Wildlife wild) => _wild = wild;

        /// <summary>Draws a band: its people (mesh per pose), spears, tent and hearth.</summary>
        public void AttachBand(Band band)
        {
            _band = band;
            if (_people == null)
            {
                int batches = PersonModel.PoseCount * 2;
                _people = new InstanceBatch[batches]; _peopleBuf = new InstanceData[batches][]; _peopleCount = new int[batches];
                for (int k = 0; k < batches; k++)
                {
                    _people[k] = new InstanceBatch(PersonModel.Get(k / 2, (k & 1) == 1), 8);
                    _peopleBuf[k] = new InstanceData[8];
                }
                _spearUp = new InstanceBatch(PersonModel.Spear(false), 8);
                _spearLevel = new InstanceBatch(PersonModel.Spear(true), 8);
                _tent = new InstanceBatch(PersonModel.Tent, 1);
                _fire = new InstanceBatch(PersonModel.Fire, 1);
            }
            var tp = band.TentPos; var fp = band.FirePos;
            _tent.Upload(new[] { new InstanceData(new float3(tp.x, _map.HeightAt(tp.x, tp.y) - 0.03f, tp.y), 2.4f, new float3(1f), Pal.Pack(new Color(0.95f, 0.92f, 0.88f))) }, 1);
            _fire.Upload(new[] { new InstanceData(new float3(fp.x, _map.HeightAt(fp.x, fp.y), fp.y), 0f, new float3(1f), Pal.Pack(Color.white)) }, 1);
        }

        void UpdatePeople()
        {
            if (_band == null) return;
            System.Array.Clear(_peopleCount, 0, _peopleCount.Length);
            _spearUpBuf.Clear(); _spearLevelBuf.Clear();
            uint plain = Pal.Pack(Color.white);
            foreach (var p in _band.People)
            {
                int pose = Band.PoseOf(p);
                int k = pose * 2 + (p.Woman ? 1 : 0);
                if (_peopleCount[k] == _peopleBuf[k].Length) System.Array.Resize(ref _peopleBuf[k], _peopleBuf[k].Length * 2);
                var at = new float3(p.Pos.x, _map.HeightAt(p.Pos.x, p.Pos.y), p.Pos.y);
                _peopleBuf[k][_peopleCount[k]++] = new InstanceData(at, p.Yaw, new float3(1f), p.Tint);
                if (!p.HasSpear) continue;
                switch (PersonModel.SpearHold(pose))
                {
                    case PersonModel.Hold.Upright: _spearUpBuf.Add(new InstanceData(_band.HandWorld(p, pose), p.Yaw, new float3(1f), plain)); break;
                    case PersonModel.Hold.Level: _spearLevelBuf.Add(new InstanceData(_band.HandWorld(p, pose), p.Yaw, new float3(1f), plain)); break;
                    case PersonModel.Hold.Back:
                    {
                        float s = math.sin(p.Yaw), c = math.cos(p.Yaw);
                        var back = new float3(0.08f, 0.45f, -0.13f);
                        _spearUpBuf.Add(new InstanceData(at + new float3(c * back.x + s * back.z, back.y, -s * back.x + c * back.z), p.Yaw, new float3(1f), plain));
                        break;
                    }
                }
            }
            foreach (var sp in _band.Spears)
                if (!sp.Gone) _spearLevelBuf.Add(new InstanceData(sp.Pos, sp.Yaw, new float3(1f), plain));
            if (_band.Prey != null && _band.Prey.Carried)
            {
                // The carrying pole (a spear) over both carriers' shoulders; the deer hangs from its middle.
                var c = _band.Prey;
                var fwd = new float3(math.sin(c.CarryYaw), 0, math.cos(c.CarryYaw));
                _spearLevelBuf.Add(new InstanceData(c.CarryPos - fwd * 0.095f, c.CarryYaw, new float3(1f), plain));
            }
            for (int k = 0; k < _people.Length; k++) _people[k].Upload(_peopleBuf[k], _peopleCount[k]);
            _spearUp.Upload(_spearUpBuf.ToArray(), _spearUpBuf.Count);
            _spearLevel.Upload(_spearLevelBuf.ToArray(), _spearLevelBuf.Count);
        }

        /// <summary>Sorts every visible deer into the batch for its pose (walk frame from stride, or head height).</summary>
        void UpdateDeer()
        {
            System.Array.Clear(_deerCount, 0, _deerCount.Length);
            if (_wild != null)
                for (int h = 0; h < _wild.Herds.Length; h++)
                {
                    var deer = _wild.Herds[h];
                    int n = Wildlife.ActiveCount(_map.Herds[h], deer);
                    for (int i = 0; i < n; i++)
                    {
                        ref var d = ref deer[i];
                        int pose = d.Moving
                            ? (int)DeerModel.Pose.Walk0 + ((int)(d.Stride * DeerModel.WalkFrames) % DeerModel.WalkFrames)
                            : d.Head < 0.33f ? (int)DeerModel.Pose.Stand : d.Head < 0.75f ? (int)DeerModel.Pose.HeadMid : (int)DeerModel.Pose.HeadDown;
                        AddDeer(pose, d.Stag, new InstanceData(new float3(d.Pos.x, _map.HeightAt(d.Pos.x, d.Pos.y), d.Pos.y), d.Yaw, new float3(1f), d.Tint));
                    }
                }
            foreach (var (pose, stag, data) in ExtraDeer) AddDeer(pose, stag, data);
            if (_wild != null)
                foreach (var c in _wild.Carcasses)
                {
                    var at = c.Carried ? c.CarryPos : new float3(c.Pos.x, _map.HeightAt(c.Pos.x, c.Pos.y), c.Pos.y);
                    AddDeer(c.Carried ? (int)DeerModel.Pose.Hung : (int)DeerModel.Pose.Dead, c.Stag, new InstanceData(at, c.Carried ? c.CarryYaw : c.Yaw, new float3(1f), c.Tint));
                }
            for (int k = 0; k < _deer.Length; k++) _deer[k].Upload(_deerBuf[k], _deerCount[k]);
        }

        void AddDeer(int pose, bool stag, InstanceData data)
        {
            int k = pose * 2 + (stag ? 1 : 0);
            if (_deerCount[k] == _deerBuf[k].Length) System.Array.Resize(ref _deerBuf[k], _deerBuf[k].Length * 2);
            _deerBuf[k][_deerCount[k]++] = data;
        }

        public void Dispose()
        {
            MaterialLibrary.DensityChanged -= OnDensityChanged;
            _conifers?.Dispose(); _broadleaves?.Dispose(); _rocks?.Dispose();
            _farConifers?.Dispose(); _farBroadleaves?.Dispose();
            if (_deer != null) foreach (var batch in _deer) batch.Dispose();
            if (_people != null)
            {
                foreach (var batch in _people) batch.Dispose();
                _spearUp.Dispose(); _spearLevel.Dispose(); _tent.Dispose(); _fire.Dispose();
            }
            if (_covers != null) foreach (var cv in _covers) cv.Batch.Dispose();
            _birds0?.Dispose(); _birds1?.Dispose(); _flakes?.Dispose();
        }
    }
}
