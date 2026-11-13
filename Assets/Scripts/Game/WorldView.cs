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
        byte[] _matBytes;
        InstanceBatch _conifers, _broadleaves, _rocks, _animals, _bushes, _tufts, _flowers;
        readonly List<InstanceData> _bushData = new(), _tuftData = new(), _flowerData = new();
        readonly List<InstanceData> _tmp = new();
        InstanceData[] _animalBuf = new InstanceData[256];

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
            BuildWater();
            _conifers = new InstanceBatch(Shapes.Conifer, 4096);
            _broadleaves = new InstanceBatch(Shapes.Broadleaf, 4096);
            _rocks = new InstanceBatch(Shapes.Rock, 1024);
            _animals = new InstanceBatch(Shapes.Animal, 256);
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
            _matMap = new Texture2D(_map.W, _map.H, TextureFormat.R8, false, true)
            {
                filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "MaterialMap",
            };
            _matMap.SetPixelData(_matBytes, 0);
            _matMap.Apply(false, false);
            Shader.SetGlobalTexture("_FL_MatMap", _matMap);
            Shader.SetGlobalVector("_FL_MatMapSize", new Vector4(_map.W, _map.H, 0, 0));
        }

        /// <summary>Changes one cell's ground material (e.g. a building clears it to dirt).</summary>
        public void SetCellMaterial(int c, string layer)
        {
            _matBytes[c] = (byte)MaterialLibrary.Layer(layer);
            _matMap.SetPixelData(_matBytes, 0);
            _matMap.Apply(false, false);
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

        void BuildWater()
        {
            var b = new MeshBuilder();
            int water = MaterialLibrary.Layer("water"), shallow = MaterialLibrary.Layer("shallow");
            float y = _map.WaterLevel - 0.1f;
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (RiverDist[c] > 2.01f) continue;
                int x = c % _map.W, z = c / _map.W;
                b.Layer = _map.Ground[c] == Ground.Ford ? shallow : water;
                b.Tri(new Vector3(x, y, z), new Vector3(x, y, z + 1), new Vector3(x + 1, y, z + 1), Color.white);
                b.Tri(new Vector3(x, y, z), new Vector3(x + 1, y, z + 1), new Vector3(x + 1, y, z), Color.white);
            }
            _waterGo = new GameObject("Water");
            _waterGo.AddComponent<MeshFilter>().sharedMesh = b.ToMesh("Water");
            var mat = WorldLighting.FlatMaterial(Color.white);
            mat.SetVector("_UVScroll", new Vector4(1.5f, 0.4f, 0, 0));
            var mr = _waterGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ nature

        public void RefreshTrees()
        {
            var con = new List<InstanceData>();
            var brd = new List<InstanceData>();
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
            _conifers.Upload(con.ToArray(), con.Count);
            _broadleaves.Upload(brd.ToArray(), brd.Count);
            _map.TreesDirty = false;
        }

        public void RefreshRocks()
        {
            _tmp.Clear();
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (_map.Rock[c] <= 0) continue;
                int x = c % _map.W, y = c / _map.W;
                var rng = new System.Random(c);
                float k = Mathf.Clamp(_map.Rock[c] / 30f, 0.35f, 1.2f);
                float px = x + 0.5f, pz = y + 0.5f;
                _tmp.Add(new InstanceData(new float3(px, _map.HeightAt(px, pz), pz), (float)rng.NextDouble() * 6.28f,
                    new float3(1.2f, 1.0f, 1.2f) * (0.7f + 0.5f * k), Tint(c, 0.85f, 1f)));
            }
            _rocks.Upload(_tmp.ToArray(), _tmp.Count);
            _map.RocksDirty = false;
        }

        void BuildDecor()
        {
            var rng = new System.Random(4242);
            _bushData.Clear(); _tuftData.Clear(); _flowerData.Clear();
            int grass = MaterialLibrary.Layer("grass"), litter = MaterialLibrary.Layer("litter");
            for (int c = 0; c < _map.W * _map.H; c++)
            {
                if (_map.Tree[c] || _map.Rock[c] > 0 || _map.Ground[c] != Ground.Grass || RiverDist[c] <= 1.01f) continue;
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

        static uint Tint(int seed, float lo, float hi)
        {
            var rng = new System.Random(seed * 7919);
            float k = lo + (float)rng.NextDouble() * (hi - lo);
            float warm = (float)rng.NextDouble() * 0.05f;
            return Pal.Pack(new Color(k + warm, k, k - warm));
        }

        // ------------------------------------------------------------------ per frame

        public void Draw(float time)
        {
            if (_map.TreesDirty) RefreshTrees();
            if (_map.RocksDirty) RefreshRocks();
            UpdateAnimals(time);
            _conifers.Draw(); _broadleaves.Draw(); _rocks.Draw(); _animals.Draw();
            _bushes.Draw(); _tufts.Draw(); _flowers.Draw();
        }

        void UpdateAnimals(float time)
        {
            int n = 0;
            for (int hi = 0; hi < _map.Herds.Count; hi++)
            {
                var herd = _map.Herds[hi];
                int count = Mathf.Min(herd.Offsets.Length, Mathf.FloorToInt(herd.Pop));
                for (int i = 0; i < count && n < _animalBuf.Length; i++)
                {
                    // Slow grazing drift around the herd's spot.
                    float t = time * 0.05f + i * 1.7f + hi * 3.1f;
                    float2 p = herd.Pos + herd.Offsets[i] + new float2(Mathf.Sin(t), Mathf.Cos(t * 0.8f)) * 0.8f;
                    float yaw = herd.Yaw[i] + Mathf.Sin(t * 0.5f) * 0.6f;
                    _animalBuf[n++] = new InstanceData(new float3(p.x, _map.HeightAt(p.x, p.y), p.y), yaw, new float3(1.5f), Pal.Pack(new Color(1f, 0.97f, 0.92f)));
                }
            }
            _animals.Upload(_animalBuf, n);
        }

        public void Dispose()
        {
            MaterialLibrary.DensityChanged -= OnDensityChanged;
            _conifers?.Dispose(); _broadleaves?.Dispose(); _rocks?.Dispose(); _animals?.Dispose();
            _bushes?.Dispose(); _tufts?.Dispose(); _flowers?.Dispose();
        }
    }
}
