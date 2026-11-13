using System;
using System.Collections.Generic;
using Firstlight.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    public enum Ground : byte { Grass, Water, Ford }

    /// <summary>A wildlife herd: hunters reduce it, it regrows towards its maximum.</summary>
    public sealed class Herd
    {
        public float2 Pos;
        public float Pop, Max;
        public float2[] Offsets = new float2[16];
        public float[] Yaw = new float[16];
    }

    /// <summary>
    /// The grid world: terrain heights, river, trees, rocks, herds, building occupancy, and the walk
    /// costs that drive flow-field paths. One cell = one world unit (about 2 m).
    /// </summary>
    public sealed class GameMap : IDisposable
    {
        public readonly int W, H;
        public readonly float WaterLevel = 0f;
        /// <summary>River surface height where it enters (y = 0) and leaves (y = H-1) the map; it runs downhill.</summary>
        public readonly float RiverTop = 0.35f, RiverBottom = -0.55f;
        public float RiverLevelAt(float y) => math.lerp(RiverTop, RiverBottom, math.saturate(y / (H - 1)));
        /// <summary>Live water state from the simulation: 0 dry, 1 shallow (wade, slow), 2 deep (blocks walking).</summary>
        public readonly byte[] WaterClass;
        public NativeArray<float> Corners;        // (W+1)*(H+1) heights
        public readonly Ground[] Ground;
        public readonly bool[] Tree;
        public readonly byte[] TreeKind;          // 0 conifer, 1 broadleaf
        public readonly float[] TreeScale, TreeYaw;
        public readonly float2[] TreeOffset;
        public readonly float[] Rock;             // stone left; 0 = no rock
        public readonly int[] BuildingAt;         // building id or -1
        public readonly bool[] Reserved;          // door cells: walkable, nothing may be built on them
        public readonly List<Herd> Herds = new();
        public readonly FlowFieldCache Flow;

        public bool TreesDirty = true, RocksDirty = true;
        bool _costDirty;

        public GameMap(int size)
        {
            W = H = size;
            int n = W * H;
            Corners = new NativeArray<float>((W + 1) * (H + 1), Allocator.Persistent);
            Ground = new Ground[n];
            Tree = new bool[n]; TreeKind = new byte[n]; TreeScale = new float[n]; TreeYaw = new float[n];
            TreeOffset = new float2[n];
            Rock = new float[n];
            WaterClass = new byte[n];
            BuildingAt = new int[n];
            Reserved = new bool[n];
            for (int i = 0; i < n; i++) BuildingAt[i] = -1;
            Flow = new FlowFieldCache(W, H, 192);
        }

        public void Dispose()
        {
            if (Corners.IsCreated) Corners.Dispose();
            Flow.Dispose();
        }

        // ------------------------------------------------------------------ queries

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public int Cell(int x, int y) => y * W + x;
        public int2 XY(int c) => new int2(c % W, c / W);
        public int CellAt(float2 p) => Cell(math.clamp((int)p.x, 0, W - 1), math.clamp((int)p.y, 0, H - 1));

        public float HeightAt(float x, float z) => CullAgentsJob.SampleHeight(Corners, W, H, new float2(x, z));

        public float CellHeight(int x, int y)
        {
            int w1 = W + 1;
            return (Corners[y * w1 + x] + Corners[y * w1 + x + 1] + Corners[(y + 1) * w1 + x] + Corners[(y + 1) * w1 + x + 1]) * 0.25f;
        }

        public bool IsWater(int c) => WaterClass[c] > 0;

        /// <summary>Price of walking into a cell (0 = blocked).</summary>
        public byte CostOf(int c)
        {
            if (WaterClass[c] >= 2) return 0;
            if (BuildingAt[c] >= 0) return 0;
            if (Rock[c] > 0) return 0;
            if (WaterClass[c] == 1) return 4;
            if (Tree[c]) return 2;
            return 1;
        }

        public void RefreshCost(int c)
        {
            byte v = CostOf(c);
            if (Flow.Cost[c] != v) { Flow.Cost[c] = v; _costDirty = true; }
        }

        /// <summary>Drops cached paths once per frame if anything blocking changed.</summary>
        public void CommitCostChanges()
        {
            if (!_costDirty) return;
            _costDirty = false;
            Flow.Invalidate();
        }

        /// <summary>Counts a feature within a circle around a point (cell units).</summary>
        public int CountFeature(string feature, float2 center, int radius, List<Herd> herdsOut = null)
        {
            if (feature == "wildlife")
            {
                int total = 0;
                foreach (var h in Herds)
                    if (math.distance(h.Pos, center) <= radius) { total += (int)math.floor(h.Pop); herdsOut?.Add(h); }
                return total;
            }
            int count = 0;
            int x0 = math.max(0, (int)(center.x - radius)), x1 = math.min(W - 1, (int)(center.x + radius));
            int y0 = math.max(0, (int)(center.y - radius)), y1 = math.min(H - 1, (int)(center.y + radius));
            float r2 = radius * radius;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - center.x, dy = y + 0.5f - center.y;
                    if (dx * dx + dy * dy > r2) continue;
                    int c = Cell(x, y);
                    switch (feature)
                    {
                        case "trees": if (Tree[c]) count++; break;
                        case "rocks": if (Rock[c] > 0) count++; break;
                        case "water": if (WaterClass[c] > 0) count++; break;
                    }
                }
            return count;
        }

        /// <summary>Nearest cell with the feature inside the radius, or -1.</summary>
        public int NearestFeatureCell(string feature, float2 center, int radius)
        {
            int best = -1; float bestD = float.MaxValue;
            int x0 = math.max(0, (int)(center.x - radius)), x1 = math.min(W - 1, (int)(center.x + radius));
            int y0 = math.max(0, (int)(center.y - radius)), y1 = math.min(H - 1, (int)(center.y + radius));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = Cell(x, y);
                    bool has = feature == "trees" ? Tree[c] : feature == "rocks" && Rock[c] > 0;
                    if (!has) continue;
                    float d = math.distancesq(new float2(x + 0.5f, y + 0.5f), center);
                    if (d < bestD && d <= radius * radius) { bestD = d; best = c; }
                }
            return best;
        }

        public void RemoveTree(int c)
        {
            if (!Tree[c]) return;
            Tree[c] = false;
            TreesDirty = true;
            RefreshCost(c);
        }

        public void TakeStone(int c, float amount)
        {
            Rock[c] = math.max(0, Rock[c] - amount);
            RocksDirty = true;
            if (Rock[c] <= 0) RefreshCost(c);
        }

        /// <summary>Nearest walkable cell on the map edge (for migrants arriving / people leaving).</summary>
        public int EdgeCellNear(float2 p, System.Random rng)
        {
            for (int attempt = 0; attempt < 400; attempt++)
            {
                int side = rng.Next(4);
                int t = rng.Next(W);
                int x = side == 0 ? 0 : side == 1 ? W - 1 : t;
                int y = side == 2 ? 0 : side == 3 ? H - 1 : t;
                int c = Cell(x, y);
                if (Flow.Cost[c] != 0) return c;
            }
            return Cell(0, 0);
        }

        // ------------------------------------------------------------------ generation

        public void Generate(int seed, Balance bal)
        {
            var rng = new System.Random(seed);
            float ox = rng.Next(0, 10000), oy = rng.Next(0, 10000);
            int n = W * H;

            // River: meanders from the bottom edge to the top edge, 2-3 cells wide, with three fords.
            float riverX = W * (0.3f + (float)rng.NextDouble() * 0.4f);
            float phase = (float)rng.NextDouble() * 10f;
            var fordRows = new HashSet<int>();
            for (int k = 1; k <= 3; k++)
            {
                int fy = (int)(H * k / 4f) + rng.Next(-6, 7);
                fordRows.Add(fy); fordRows.Add(fy + 1);
            }
            for (int y = 0; y < H; y++)
            {
                float cx = riverX + Mathf.Sin(y * 0.045f + phase) * 13f + (Mathf.PerlinNoise(ox + y * 0.07f, oy) - 0.5f) * 14f;
                float width = 2.2f + Mathf.PerlinNoise(ox, oy + y * 0.09f) * 1.6f;
                for (int x = (int)(cx - width / 2); x <= (int)(cx + width / 2); x++)
                {
                    if (!InBounds(x, y)) continue;
                    Ground[Cell(x, y)] = fordRows.Contains(y) ? Game.Ground.Ford : Game.Ground.Water;
                }
            }

            // Distance (in cells) from each cell to the river, for valley carving and bank colours.
            var dist = RiverDistance();

            // Heights: rolling hills, carved into a valley along the river.
            int w1 = W + 1;
            for (int y = 0; y <= H; y++)
                for (int x = 0; x <= W; x++)
                {
                    float f = Mathf.PerlinNoise(ox + x * 0.022f, oy + y * 0.022f) * 0.65f
                            + Mathf.PerlinNoise(ox + 300 + x * 0.06f, oy + y * 0.06f) * 0.25f
                            + Mathf.PerlinNoise(ox + 700 + x * 0.15f, oy + y * 0.15f) * 0.10f;
                    float h = 0.5f + f * 4.2f;
                    // Corner distance = min of the 4 cells around it.
                    float d = float.MaxValue; bool touchesWater = false;
                    for (int dy = -1; dy <= 0; dy++)
                        for (int dx = -1; dx <= 0; dx++)
                        {
                            int cx2 = x + dx, cy2 = y + dy;
                            if (!InBounds(cx2, cy2)) continue;
                            int c = Cell(cx2, cy2);
                            d = Mathf.Min(d, dist[c]);
                            if (Ground[c] == Game.Ground.Water) touchesWater = true;
                        }
                    float wl = RiverLevelAt(y);
                    if (touchesWater) h = wl - 0.45f;
                    // Low floodplain (about 8 m each side) that spring floods can spread over, then the valley side.
                    else h = Mathf.Min(h, wl + 0.14f + Mathf.Clamp(d - 1, 0, 4) * 0.07f + Mathf.Max(0, d - 5) * 0.32f);
                    Corners[y * w1 + x] = h;
                }
            // Fords sit just under the water surface.
            for (int c = 0; c < n; c++)
            {
                if (Ground[c] != Game.Ground.Ford) continue;
                int x = c % W, y = c / W;
                Corners[y * w1 + x] = Corners[y * w1 + x + 1] = Corners[(y + 1) * w1 + x] = Corners[(y + 1) * w1 + x + 1] = RiverLevelAt(y) - 0.2f;
            }

            // Forests: clustered by noise, denser in the middle of each patch.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int c = Cell(x, y);
                    if (Ground[c] != Game.Ground.Grass || dist[c] < 1.5f) continue;
                    float f = Mathf.PerlinNoise(ox + 1000 + x * 0.06f, oy + 1000 + y * 0.06f) * 0.75f
                            + Mathf.PerlinNoise(ox + 1500 + x * 0.18f, oy + 1500 + y * 0.18f) * 0.25f;
                    double r = rng.NextDouble();
                    bool tree = (f > 0.6f && r < 0.7) || (f > 0.5f && r < 0.22) || r < 0.008;
                    if (!tree) continue;
                    Tree[c] = true;
                    float conifer = Mathf.PerlinNoise(ox + 2000 + x * 0.04f, oy + 2000 + y * 0.04f) + CellHeight(x, y) * 0.08f;
                    TreeKind[c] = (byte)(conifer > 0.62f ? 0 : 1);
                    TreeScale[c] = 0.8f + (float)rng.NextDouble() * 0.6f;
                    TreeYaw[c] = (float)rng.NextDouble() * Mathf.PI * 2f;
                    TreeOffset[c] = new float2((float)rng.NextDouble() * 0.4f + 0.3f, (float)rng.NextDouble() * 0.4f + 0.3f);
                }

            // Rock outcrops: small clusters, more likely on higher ground.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int c = Cell(x, y);
                    if (Ground[c] != Game.Ground.Grass || dist[c] < 2f) continue;
                    float f = Mathf.PerlinNoise(ox + 3000 + x * 0.11f, oy + 3000 + y * 0.11f) + CellHeight(x, y) * 0.04f;
                    if (f > 0.8f && rng.NextDouble() < 0.55)
                    {
                        Rock[c] = bal.rockStone * (0.7f + (float)rng.NextDouble() * 0.6f);
                        Tree[c] = false;
                    }
                }

            // Wildlife herds on open ground, spread apart.
            int herdCount = 8;
            for (int attempt = 0; attempt < 2000 && Herds.Count < herdCount; attempt++)
            {
                int x = rng.Next(8, W - 8), y = rng.Next(8, H - 8);
                int c = Cell(x, y);
                if (Ground[c] != Game.Ground.Grass || Tree[c] || Rock[c] > 0 || dist[c] < 3) continue;
                var p = new float2(x + 0.5f, y + 0.5f);
                bool farEnough = true;
                foreach (var h in Herds) if (math.distance(h.Pos, p) < 22) { farEnough = false; break; }
                if (!farEnough) continue;
                var herd = new Herd { Pos = p, Max = 9 + rng.Next(0, 5) };
                herd.Pop = herd.Max;
                for (int i = 0; i < herd.Offsets.Length; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.8f + (float)rng.NextDouble() * 3.2f;
                    herd.Offsets[i] = new float2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
                    herd.Yaw[i] = (float)rng.NextDouble() * Mathf.PI * 2f;
                }
                Herds.Add(herd);
            }

            for (int c = 0; c < n; c++)
                WaterClass[c] = (byte)(Ground[c] == Game.Ground.Water ? 2 : Ground[c] == Game.Ground.Ford ? 1 : 0);
            for (int c = 0; c < n; c++) Flow.Cost[c] = CostOf(c);
            Flow.Invalidate();
            TreesDirty = RocksDirty = true;
        }

        public float[] RiverDistance()
        {
            int n = W * H;
            var dist = new float[n];
            var queue = new Queue<int>();
            for (int c = 0; c < n; c++)
            {
                if (Ground[c] != Game.Ground.Grass) { dist[c] = 0; queue.Enqueue(c); }
                else dist[c] = float.MaxValue;
            }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % W, y = c / W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!InBounds(nx, ny)) continue;
                    int nc = Cell(nx, ny);
                    if (dist[nc] > dist[c] + 1) { dist[nc] = dist[c] + 1; queue.Enqueue(nc); }
                }
            }
            return dist;
        }

        /// <summary>Daily regrowth: a few new trees sprout next to existing forest; herds recover.</summary>
        public void DailyRegrowth(System.Random rng, Balance bal)
        {
            for (int k = 0; k < bal.treeRegrowPerDay; k++)
            {
                for (int attempt = 0; attempt < 30; attempt++)
                {
                    int x = rng.Next(1, W - 1), y = rng.Next(1, H - 1);
                    int c = Cell(x, y);
                    if (Tree[c] || Ground[c] != Game.Ground.Grass || Rock[c] > 0 || BuildingAt[c] >= 0 || Reserved[c]) continue;
                    int neighbours = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (Tree[Cell(x + dx, y + dy)]) neighbours++;
                    if (neighbours < 3) continue;
                    // Not right next to buildings.
                    bool nearBuilding = false;
                    for (int dy = -2; dy <= 2 && !nearBuilding; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            if (InBounds(x + dx, y + dy) && BuildingAt[Cell(x + dx, y + dy)] >= 0) { nearBuilding = true; break; }
                    if (nearBuilding) continue;
                    Tree[c] = true;
                    TreeKind[c] = (byte)rng.Next(2);
                    TreeScale[c] = 0.6f + (float)rng.NextDouble() * 0.4f;
                    TreeYaw[c] = (float)rng.NextDouble() * Mathf.PI * 2f;
                    TreeOffset[c] = new float2(0.5f, 0.5f);
                    TreesDirty = true;
                    RefreshCost(c);
                    break;
                }
            }
            foreach (var h in Herds)
                h.Pop = math.min(h.Max, h.Pop + h.Pop * bal.herdRegrowPerDay + 0.05f);
        }
    }
}
