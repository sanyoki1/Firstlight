using System.Diagnostics;
using UnityEngine.Profiling;

namespace Firstlight.Game
{
    /// <summary>
    /// PERF PROBE (performance audit, pass 2; measurement only, see docs/pass2/audit-performance.md). Off unless the
    /// player is started with -perfprobe: every hook is one static bool test when off. When on, each marked section
    /// adds up its wall time (Stopwatch ticks) and the managed heap growth seen across it (Mono "used size" deltas;
    /// Boehm counts heap blocks, so single small allocations show up in ~4-8 KB steps: only sums over many frames mean
    /// anything). The driver is GameController.PerfProbe.cs.
    /// </summary>
    public static class PerfProbe
    {
        public static bool On;

        public enum S
        {
            HudUpdate, Water, WaterSync, FillFlood, Commit, Autosave, Sim, Auto, Wild, Atmo, ViewDraw, Trees, Rocks, MatMap,
            WaterMesh, SViewDraw, Sound, Render, OnGUI, GuiTopBar, GuiFeed, GuiBuildBar, GuiGuide, GuiInfo, GuiPanels,
            GuiResearch, GuiOther, Count,
        }

        public const int N = (int)S.Count;
        static readonly long[] _t0 = new long[N], _m0 = new long[N];
        /// <summary>This frame so far (cleared by the driver each frame): ticks, positive heap growth, calls.</summary>
        public static readonly long[] Ticks = new long[N], Bytes = new long[N];
        public static readonly int[] Calls = new int[N];

        public static void Begin(S s)
        {
            if (!On) return;
            int i = (int)s;
            _m0[i] = Profiler.GetMonoUsedSizeLong();
            _t0[i] = Stopwatch.GetTimestamp();
        }

        public static void End(S s)
        {
            if (!On) return;
            long t = Stopwatch.GetTimestamp();
            int i = (int)s;
            Ticks[i] += t - _t0[i];
            long d = Profiler.GetMonoUsedSizeLong() - _m0[i];
            if (d > 0) Bytes[i] += d;
            Calls[i]++;
        }

        public static void ClearFrame()
        {
            System.Array.Clear(Ticks, 0, N); System.Array.Clear(Bytes, 0, N); System.Array.Clear(Calls, 0, N);
        }

        public static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        /// <summary>Startup marks: time since the engine started, logged as they come (allocates; only when on).</summary>
        public static void Mark(string what)
        {
            if (!On) return;
            UnityEngine.Debug.Log($"[Probe] mark {UnityEngine.Time.realtimeSinceStartupAsDouble * 1000.0:F0} ms since engine start: {what}");
        }
    }
}
