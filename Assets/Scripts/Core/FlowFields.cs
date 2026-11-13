using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Firstlight.Core
{
    /// <summary>Flow field direction codes: 0..7 = neighbour, 8 = this is the target, 255 = no path.</summary>
    public static class FlowDir
    {
        public const byte Target = 8;
        public const byte None = 255;

        public static int2 Offset(int d)
        {
            switch (d)
            {
                case 0: return new int2(1, 0);
                case 1: return new int2(1, 1);
                case 2: return new int2(0, 1);
                case 3: return new int2(-1, 1);
                case 4: return new int2(-1, 0);
                case 5: return new int2(-1, -1);
                case 6: return new int2(0, -1);
                default: return new int2(1, -1);
            }
        }
    }

    /// <summary>
    /// Builds one flow field per target with Dijkstra (8 neighbours, no corner cutting).
    /// cost: 0 = blocked, otherwise the price of entering the cell (1 = normal ground).
    /// Each batch item writes its own slice of <see cref="fields"/>, so items run in parallel.
    /// </summary>
    [BurstCompile]
    public struct BuildFlowFieldsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<byte> cost;
        [ReadOnly] public NativeArray<int> targets;
        [ReadOnly] public NativeArray<int> slots;
        [NativeDisableParallelForRestriction] public NativeArray<byte> fields;
        public int width, height;

        public void Execute(int item)
        {
            int cells = width * height;
            int target = targets[item];
            int baseIdx = slots[item] * cells;
            var dist = new NativeArray<int>(cells, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
            for (int c = 0; c < cells; c++) dist[c] = int.MaxValue;
            var heap = new NativeList<long>(1024, Allocator.Temp);

            dist[target] = 0;
            Push(ref heap, 0L << 32 | (uint)target);
            while (heap.Length > 0)
            {
                long top = Pop(ref heap);
                int d = (int)(top >> 32);
                int c = (int)(top & 0xffffffffL);
                if (d > dist[c]) continue;
                int x = c % width, y = c / width;
                for (int k = 0; k < 8; k++)
                {
                    int2 o = FlowDir.Offset(k);
                    int nx = x + o.x, ny = y + o.y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int nc = ny * width + nx;
                    byte cc = cost[nc];
                    if (cc == 0) continue;
                    bool diag = (k & 1) == 1;
                    if (diag && (cost[y * width + nx] == 0 || cost[ny * width + x] == 0)) continue;
                    int nd = d + (diag ? 14 : 10) * cc;
                    if (nd < dist[nc])
                    {
                        dist[nc] = nd;
                        Push(ref heap, (long)nd << 32 | (uint)nc);
                    }
                }
            }

            for (int c = 0; c < cells; c++)
            {
                if (c == target) { fields[baseIdx + c] = FlowDir.Target; continue; }
                bool blocked = cost[c] == 0;
                int best = blocked ? int.MaxValue : dist[c];
                int bestK = FlowDir.None;
                int x = c % width, y = c / width;
                for (int k = 0; k < 8; k++)
                {
                    int2 o = FlowDir.Offset(k);
                    int nx = x + o.x, ny = y + o.y;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    int nc = ny * width + nx;
                    if (cost[nc] == 0 && nc != target) continue;
                    if (!blocked && (k & 1) == 1 && (cost[y * width + nx] == 0 || cost[ny * width + x] == 0)) continue;
                    if (dist[nc] < best) { best = dist[nc]; bestK = k; }
                }
                fields[baseIdx + c] = (byte)bestK;
            }
            dist.Dispose();
            heap.Dispose();
        }

        static void Push(ref NativeList<long> heap, long v)
        {
            heap.Add(v);
            int i = heap.Length - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (heap[p] <= v) break;
                heap[i] = heap[p];
                i = p;
            }
            heap[i] = v;
        }

        static long Pop(ref NativeList<long> heap)
        {
            long top = heap[0];
            long last = heap[heap.Length - 1];
            heap.RemoveAt(heap.Length - 1);
            int n = heap.Length;
            if (n == 0) return top;
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1;
                if (l >= n) break;
                int r = l + 1;
                int m = (r < n && heap[r] < heap[l]) ? r : l;
                if (heap[m] >= last) break;
                heap[i] = heap[m];
                i = m;
            }
            heap[i] = last;
            return top;
        }
    }

    /// <summary>
    /// Least-recently-used cache of flow fields keyed by target cell. Fields are computed on demand
    /// (in parallel when prefetched in a batch) and all dropped when the walkable map changes.
    /// </summary>
    public sealed class FlowFieldCache : IDisposable
    {
        public readonly int Width, Height, Cells, Capacity;
        public NativeArray<byte> Cost;   // edit freely, then call Invalidate()
        public NativeArray<byte> Fields; // Capacity * Cells direction codes

        readonly Dictionary<int, int> _slotOfTarget = new();
        readonly int[] _targetOfSlot;
        readonly long[] _lastUse;
        long _clock;
        public int FieldsBuilt { get; private set; }
        public double LastBuildMs { get; private set; }

        public FlowFieldCache(int width, int height, int capacity)
        {
            Width = width; Height = height; Cells = width * height; Capacity = capacity;
            Cost = new NativeArray<byte>(Cells, Allocator.Persistent);
            Fields = new NativeArray<byte>(Cells * capacity, Allocator.Persistent);
            _targetOfSlot = new int[capacity];
            _lastUse = new long[capacity];
            for (int i = 0; i < capacity; i++) _targetOfSlot[i] = -1;
        }

        public void Invalidate()
        {
            _slotOfTarget.Clear();
            for (int i = 0; i < Capacity; i++) { _targetOfSlot[i] = -1; _lastUse[i] = 0; }
        }

        /// <summary>Slot index of the field leading to <paramref name="targetCell"/>, building it if needed.</summary>
        public int Resolve(int targetCell)
        {
            _clock++;
            if (_slotOfTarget.TryGetValue(targetCell, out int slot))
            {
                _lastUse[slot] = _clock;
                return slot;
            }
            Build(new List<int> { targetCell });
            return _slotOfTarget[targetCell];
        }

        /// <summary>Builds any missing fields for these targets in one parallel batch.</summary>
        public void Prefetch(IReadOnlyList<int> targetCells)
        {
            var missing = new List<int>();
            var seen = new HashSet<int>();
            foreach (int t in targetCells)
                if (!_slotOfTarget.ContainsKey(t) && seen.Add(t)) missing.Add(t);
            if (missing.Count > 0) Build(missing);
        }

        void Build(List<int> targets)
        {
            if (targets.Count > Capacity) targets = targets.GetRange(0, Capacity);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tArr = new NativeArray<int>(targets.Count, Allocator.TempJob);
            var sArr = new NativeArray<int>(targets.Count, Allocator.TempJob);
            for (int i = 0; i < targets.Count; i++)
            {
                int slot = PickSlot();
                if (_targetOfSlot[slot] >= 0) _slotOfTarget.Remove(_targetOfSlot[slot]);
                _targetOfSlot[slot] = targets[i];
                _slotOfTarget[targets[i]] = slot;
                _lastUse[slot] = ++_clock;
                tArr[i] = targets[i];
                sArr[i] = slot;
            }
            new BuildFlowFieldsJob
            {
                cost = Cost, targets = tArr, slots = sArr, fields = Fields, width = Width, height = Height,
            }.Schedule(targets.Count, 1).Complete();
            tArr.Dispose();
            sArr.Dispose();
            FieldsBuilt += targets.Count;
            LastBuildMs = sw.Elapsed.TotalMilliseconds;
        }

        int PickSlot()
        {
            int best = 0;
            long bestUse = long.MaxValue;
            for (int i = 0; i < Capacity; i++)
            {
                if (_targetOfSlot[i] < 0) return i;
                if (_lastUse[i] < bestUse) { bestUse = _lastUse[i]; best = i; }
            }
            return best;
        }

        public void Dispose()
        {
            if (Cost.IsCreated) Cost.Dispose();
            if (Fields.IsCreated) Fields.Dispose();
        }
    }
}
