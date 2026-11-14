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
        Texture2D _matMap;
        byte[] _matBytes, _matOrig;
        bool _matDirty;
        InstanceBatch _conifers, _broadleaves, _rocks, _bushes, _tufts, _flowers;
        readonly List<InstanceData> _bushData = new(), _tuftData = new(), _flowerData = new();
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

        void BuildTerrain()
        {
            int w = _map.W, h = _map.H, w1 = w + 1;
            int quads = w * h;
            var verts = new Vector3[quads * 4];
            var tris = new int[quads * 6];
            var cols = new Color[quads * 4];
            var mats = new Vector2[quads * 4];
            var normals = new Vector3[quads * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int q = y * w + x, v = q * 4;
                    verts[v] = new Vector3(x, _map.Corners[y * w1 + x], y);
                    verts[v + 1] = new Vector3(x, _map.Corners[(y + 1) * w1 + x], y + 1);
                    verts[v + 2] = new Vector3(x + 1, _map.Corners[(y + 1) * w1 + x + 1], y + 1);
                    verts[v + 3] = new Vector3(x + 1, _map.Corners[y * w1 + x + 1], y);
                    for (int k = 0; k < 4; k++) { cols[v + k] = Color.white; normals[v + k] = Vector3.up; }
                    int t = q * 6;
                    // Alternate the diagonal for a less regular facet pattern.
                    if (((x + y) & 1) == 0) { tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3; }
                    else { tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 3; tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3; }
                }
            var mesh = new Mesh { name = "Terrain", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.vertices = verts; mesh.colors = cols; mesh.normals = normals;
            mesh.SetUVs(0, new Vector2[verts.Length]);
            mesh.SetUVs(1, mats);
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
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

        /// <summary>The water surface is rebuilt from the simulation every frame (only wet cells get quads).</summary>
        public void AttachWater(WaterSim water)
        {
            _water = water;
            int n = _map.W * _map.H, nc = (_map.W + 1) * (_map.H + 1);
            _wv = new Vector3[n * 4]; _wc = new Color[n * 4]; _wuv = new Vector2[n * 4]; _wmat = new Vector2[n * 4];
            _wt = new int[n * 6];
            _cs = new float[nc]; _cd = new float[nc]; _cvel = new Vector2[nc];
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

        static bool WetNeighbour(Unity.Collections.NativeArray<float> depth, int x, int y, int w, int h)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (depth[ny * w + nx] > WetDepth) return true;
                }
            return false;
        }

        void UpdateWaterMesh()
        {
            int w = _map.W, h = _map.H, w1 = w + 1;
            var depth = _water.Depth; var bed = _water.Bed; var vel = _water.Velocity;
            for (int cy = 0; cy <= h; cy++)
                for (int cx = 0; cx <= w; cx++)
                {
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
                    int ci = cy * w1 + cx;
                    if (k > 0) { _cs[ci] = s / k; _cd[ci] = d / k; _cvel[ci] = new Vector2(v.x, v.y) / k; }
                    else { _cs[ci] = -99; _cd[ci] = 0; _cvel[ci] = Vector2.zero; }
                }
            int layer = MaterialLibrary.Layer("water");
            var mat = new Vector2(layer, 2);
            int nv = 0, nt = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int c = y * w + x;
                    if (depth[c] <= WetDepth && !WetNeighbour(depth, x, y, w, h)) continue;
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
                }
            _waterMesh.Clear(false);
            _waterMesh.SetVertices(_wv, 0, nv);
            _waterMesh.SetColors(_wc, 0, nv);
            _waterMesh.SetUVs(0, _wuv, 0, nv);
            _waterMesh.SetUVs(1, _wmat, 0, nv);
            _waterMesh.SetTriangles(_wt, 0, nt, 0, false);
            _waterMesh.bounds = new Bounds(new Vector3(w / 2f, 0, h / 2f), new Vector3(w, 20, h));
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
            _bushData.Clear(); _tuftData.Clear(); _flowerData.Clear();
            int grass = MaterialLibrary.Layer("grass"), litter = MaterialLibrary.Layer("litter");
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (_map.Tree[c] || _map.Rock[c] > 0 || _map.Ground[c] != Ground.Grass || RiverDist[c] <= 2.01f) continue;
                int x = c % _map.W, y = c / _map.W;
                int trees = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (_map.InBounds(x + dx, y + dy) && _map.Tree[_map.Cell(x + dx, y + dy)]) trees++;
                bool edge = trees >= 1 && trees <= 5;
                if (edge && rng.NextDouble() < 0.35) _bushData.Add(Plant(x, y, rng, 1.6f, 2.2f));
                if (_matBytes[c] == grass || _matBytes[c] == litter)
                {
                    int tufts = rng.NextDouble() < 0.55 ? rng.Next(1, 4) : 0;
                    for (int k = 0; k < tufts; k++) _tuftData.Add(Plant(x, y, rng, 1.6f, 2.4f));
                    if (rng.NextDouble() < 0.05) _flowerData.Add(Plant(x, y, rng, 1.6f, 2.2f));
                }
            }
            _bushes = new InstanceBatch(Shapes.Bush, _bushData.Count, InstanceBatch.DoubleSidedMaterial);
            _tufts = new InstanceBatch(Shapes.Tuft, _tuftData.Count, InstanceBatch.DoubleSidedMaterial) { CastShadows = false };
            _flowers = new InstanceBatch(Shapes.Flowers, _flowerData.Count, InstanceBatch.DoubleSidedMaterial) { CastShadows = false };
            _bushes.Upload(_bushData.ToArray(), _bushData.Count);
            _tufts.Upload(_tuftData.ToArray(), _tuftData.Count);
            _flowers.Upload(_flowerData.ToArray(), _flowerData.Count);
        }

        InstanceData Plant(int x, int y, System.Random rng, float sMin, float sMax)
        {
            float px = x + (float)rng.NextDouble(), pz = y + (float)rng.NextDouble();
            float s = sMin + (float)rng.NextDouble() * (sMax - sMin);
            // Plants are sized at their true pixel density; scale stays an integer-ish factor so pixels stay square.
            s = 1f;
            return new InstanceData(new float3(px, _map.HeightAt(px, pz) - 0.02f, pz), (float)rng.NextDouble() * 6.28f, new float3(s), Pal.Pack(new Color(0.92f + (float)rng.NextDouble() * 0.08f, 0.95f, 0.9f)));
        }

        void OnDensityChanged()
        {
            if (_bushes == null) return;
            _bushes.Mesh = Shapes.Bush;
            _tufts.Mesh = Shapes.Tuft;
            _flowers.Mesh = Shapes.Flowers;
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
            if (_map.TreesDirty) RefreshTrees();
            if (_map.RocksDirty) RefreshRocks();
            if (_matDirty)
            {
                _matDirty = false;
                _matMap.SetPixelData(_matBytes, 0);
                _matMap.Apply(false, false);
            }
            UpdateDeer();
            UpdatePeople();
            if (_water != null) UpdateWaterMesh();
            _conifers.Draw(); _broadleaves.Draw(); _rocks.Draw();
            foreach (var batch in _deer) batch.Draw();
            if (_people != null)
            {
                foreach (var batch in _people) batch.Draw();
                _spearUp.Draw(); _spearLevel.Draw(); _tent.Draw(); _fire.Draw();
            }
            if (!HideGroundCover) { _bushes.Draw(); _tufts.Draw(); _flowers.Draw(); }
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
            if (_deer != null) foreach (var batch in _deer) batch.Dispose();
            if (_people != null)
            {
                foreach (var batch in _people) batch.Dispose();
                _spearUp.Dispose(); _spearLevel.Dispose(); _tent.Dispose(); _fire.Dispose();
            }
            _bushes?.Dispose(); _tufts?.Dispose(); _flowers?.Dispose();
        }
    }
}
