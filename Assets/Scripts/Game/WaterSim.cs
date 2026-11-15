using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Firstlight.Game
{
    /// <summary>
    /// Shallow-water simulation on the map grid ("virtual pipes" heightfield method). Each cell holds a water
    /// depth; virtual pipes to the four neighbours carry flow driven by the difference in water surface height.
    /// The river is fed at its upstream edge and drains at the downstream edge, so it flows downhill, pools in
    /// hollows and overtops its banks when the inflow rises (spring floods). Runs in Burst jobs.
    /// Units: heights in world units (1 unit = 2 m), time in seconds of game time.
    /// </summary>
    public sealed class WaterSim : IDisposable
    {
        public const float StepSeconds = 0.05f;
        const float Gravity = 4.9f;      // 9.81 m/s² in units of 2 m
        const float Damping = 0.992f;    // per step: stands in for bed friction
        const float Evaporation = 0.0004f; // units per second, dries stray puddles

        readonly GameMap _map;
        public readonly int W, H;
        public NativeArray<float> Bed;      // cell bed height (average of its corners)
        public NativeArray<float> Depth;    // water depth
        public NativeArray<float4> Flux;    // outflow per pipe: x left, y right, z down (-y), w up (+y)
        public NativeArray<float2> Velocity;
        NativeArray<int> _sourceCells, _sinkCells;

        /// <summary>Extra height added to the upstream water level (raise for a flood surge).</summary>
        public float SourceBoost;
        public double LastStepMs { get; private set; }
        /// <summary>Goes up with each <see cref="Step"/> call, so the view rebuilds the surface only after the water moved
        /// (the flood fill's direct depth edits show with the next step, at most 0.05 s later).</summary>
        public int Version { get; private set; }
        public float TotalVolume { get; private set; }

        public WaterSim(GameMap map)
        {
            _map = map;
            W = map.W; H = map.H;
            int n = W * H;
            Bed = new NativeArray<float>(n, Allocator.Persistent);
            Depth = new NativeArray<float>(n, Allocator.Persistent);
            Flux = new NativeArray<float4>(n, Allocator.Persistent);
            Velocity = new NativeArray<float2>(n, Allocator.Persistent);
            RefreshBed();

            // Source = river cells on the upstream edge, sink = river cells on the downstream edge.
            var src = new System.Collections.Generic.List<int>();
            var snk = new System.Collections.Generic.List<int>();
            for (int x = 0; x < W; x++)
            {
                if (map.Ground[map.Cell(x, 0)] != Ground.Grass) src.Add(map.Cell(x, 0));
                if (map.Ground[map.Cell(x, H - 1)] != Ground.Grass) snk.Add(map.Cell(x, H - 1));
            }
            _sourceCells = new NativeArray<int>(src.ToArray(), Allocator.Persistent);
            _sinkCells = new NativeArray<int>(snk.ToArray(), Allocator.Persistent);

            // Start from a still river at its nominal level, then let it settle into a flowing state.
            for (int c = 0; c < n; c++)
                if (map.Ground[c] != Ground.Grass)
                    Depth[c] = math.max(0, map.RiverLevelAt(c / W) - Bed[c]);
            Step(StepSeconds, 4000);
        }

        /// <summary>Re-reads bed heights from the terrain (call after the terrain is edited).</summary>
        public void RefreshBed()
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                    Bed[y * W + x] = _map.CellHeight(x, y);
        }

        /// <summary>Advances the simulation by <paramref name="steps"/> fixed steps.</summary>
        public void Step(float dt, int steps)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            JobHandle h = default;
            for (int i = 0; i < steps; i++)
            {
                h = new BoundaryJob
                {
                    bed = Bed, depth = Depth, cells = _sourceCells, level = _map.RiverTop + SourceBoost, flux = Flux, zeroFlux = false,
                }.Schedule(h);
                h = new BoundaryJob
                {
                    bed = Bed, depth = Depth, cells = _sinkCells, level = _map.RiverBottom + SourceBoost * 0.8f, flux = Flux, zeroFlux = false,
                }.Schedule(h);
                h = new FluxJob { bed = Bed, depth = Depth, flux = Flux, w = W, h = H, dt = dt, g = Gravity, damping = Damping }
                    .Schedule(W * H, 256, h);
                h = new DepthJob { flux = Flux, depth = Depth, velocity = Velocity, w = W, h = H, dt = dt, evaporation = Evaporation }
                    .Schedule(W * H, 256, h);
            }
            h.Complete();
            Version++;
            LastStepMs = sw.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Updates the map's walkable water classes from depth (with hysteresis so the edge of the water does not
        /// flicker). Returns true if any cell changed, which means cached paths must be rebuilt.
        /// </summary>
        public bool SyncWaterClasses()
        {
            bool changed = false;
            float vol = 0;
            for (int c = 0; c < W * H; c++)
            {
                float d = Depth[c];
                vol += d;
                byte cur = _map.WaterClass[c], next = cur;
                // 1 unit = 2 m: wading up to ~0.6 m, deeper blocks.
                if (cur == 0 && d > 0.08f) next = d > 0.32f ? (byte)2 : (byte)1;
                else if (cur == 1) { if (d < 0.04f) next = 0; else if (d > 0.32f) next = 2; }
                else if (cur == 2 && d < 0.26f) next = d < 0.04f ? (byte)0 : (byte)1;
                if (next != cur)
                {
                    _map.WaterClass[c] = next;
                    _map.RefreshCost(c);
                    changed = true;
                }
            }
            TotalVolume = vol;
            return changed;
        }

        public void Dispose()
        {
            if (Bed.IsCreated) Bed.Dispose();
            if (Depth.IsCreated) Depth.Dispose();
            if (Flux.IsCreated) Flux.Dispose();
            if (Velocity.IsCreated) Velocity.Dispose();
            if (_sourceCells.IsCreated) _sourceCells.Dispose();
            if (_sinkCells.IsCreated) _sinkCells.Dispose();
        }

        // ------------------------------------------------------------------ jobs

        /// <summary>Holds edge cells at a fixed water surface level (inflow upstream, outflow downstream).</summary>
        [BurstCompile]
        struct BoundaryJob : IJob
        {
            [ReadOnly] public NativeArray<float> bed;
            public NativeArray<float> depth;
            public NativeArray<float4> flux;
            [ReadOnly] public NativeArray<int> cells;
            public float level;
            public bool zeroFlux;

            public void Execute()
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    int c = cells[i];
                    depth[c] = math.max(0, level - bed[c]);
                    if (zeroFlux) flux[c] = float4.zero;
                }
            }
        }

        /// <summary>Pipe flow: each outflow grows with the surface drop to that neighbour, scaled so a cell never
        /// sends out more water than it holds.</summary>
        [BurstCompile]
        struct FluxJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> bed, depth;
            public NativeArray<float4> flux;
            public int w, h;
            public float dt, g, damping;

            public void Execute(int c)
            {
                int x = c % w, y = c / w;
                float d = depth[c];
                if (d <= 0f) { flux[c] = float4.zero; return; }
                float s = bed[c] + d;
                float4 f = flux[c] * damping;
                float k = dt * g;
                f.x = x > 0 ? math.max(0, f.x + k * (s - bed[c - 1] - depth[c - 1])) : 0;
                f.y = x < w - 1 ? math.max(0, f.y + k * (s - bed[c + 1] - depth[c + 1])) : 0;
                f.z = y > 0 ? math.max(0, f.z + k * (s - bed[c - w] - depth[c - w])) : 0;
                f.w = y < h - 1 ? math.max(0, f.w + k * (s - bed[c + w] - depth[c + w])) : 0;
                float sum = math.csum(f);
                if (sum > 0)
                {
                    float scale = math.min(1f, d / (sum * dt));
                    f *= scale;
                }
                flux[c] = f;
            }
        }

        /// <summary>New depth = old depth + (inflow - outflow) * dt; also derives the flow velocity.</summary>
        [BurstCompile]
        struct DepthJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float4> flux;
            public NativeArray<float> depth;
            public NativeArray<float2> velocity;
            public int w, h;
            public float dt, evaporation;

            public void Execute(int c)
            {
                int x = c % w, y = c / w;
                float4 f = flux[c];
                float fromLeft = x > 0 ? flux[c - 1].y : 0;
                float fromRight = x < w - 1 ? flux[c + 1].x : 0;
                float fromDown = y > 0 ? flux[c - w].w : 0;
                float fromUp = y < h - 1 ? flux[c + w].z : 0;
                float d0 = depth[c];
                float d1 = math.max(0, d0 + dt * (fromLeft + fromRight + fromDown + fromUp - math.csum(f)));
                d1 = math.max(0, d1 - evaporation * dt);
                depth[c] = d1;
                float avg = (d0 + d1) * 0.5f;
                float vx = (fromLeft - f.x + f.y - fromRight) * 0.5f;
                float vy = (fromDown - f.z + f.w - fromUp) * 0.5f;
                velocity[c] = avg > 0.01f ? new float2(vx, vy) / avg : float2.zero;
            }
        }
    }
}
