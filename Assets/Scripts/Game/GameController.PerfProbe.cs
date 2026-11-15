using System;
using System.Collections;
using System.IO;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // PERF PROBE (performance audit, pass 2; measurement only, nothing here changes how the game plays). Started by
    // -perfprobe; see docs/pass2/audit-performance.md for the commands. With -load SAVE it plays the loaded town through
    // a plan of phases (sim speeds, an added crowd, a flood, re-pathing after a walk-cost change, save/load, a scene
    // reload) and logs, per phase: frame-time percentiles, per-section time and heap growth, sim step parts, flow-field
    // builds, GC collections and the worst frames with what they spent their time on. Without -load and with
    // -perfstartup SEED it times the start to the title menu and a new game. Batch mode renders every frame into a
    // 1920x1080 texture (as -soak does); windowed (-popupwindow) the cameras and OnGUI run normally.
    // Autosaves are switched off in memory while it runs (so the player's own autosave slots are not touched).
    public sealed partial class GameController
    {
        static int _probeStage;              // 0 first start; 1 startup: new game requested; 2 reload requested
        static double _probeT0;              // realtime when a scene reload / new game was asked for

        const int MaxFrames = 400000, MaxSpikes = 12;
        string _phase;
        bool _phaseOn;
        int _pf;                             // frames in the phase
        float[] _frameMs;
        readonly double[] _secMs = new double[PerfProbe.N], _secMax = new double[PerfProbe.N];
        readonly long[] _secBytes = new long[PerfProbe.N], _secCalls = new long[PerfProbe.N];
        long _lastTick; bool _haveLast;
        int _gc0Start, _gcLast, _gcFrames; double _gcFrameMs, _gcFrameMax;
        double _simCit, _simHour, _simDay, _simJobs, _hourMsSum, _hourMsMax, _dayMsSum, _dayMsMax; int _hourFrames, _dayFrames;
        double _simTime0, _flowMs0; int _flow0, _flowBuilds0;
        long _heap0;
        float _lastSimTime; int _lastDay;
        double _lastCit, _lastHour, _lastDayMs, _lastJobs; double _lastFlowMs; int _lastFlow;

        struct Spike { public float Ms; public int Frame, Day, Gc, Flow; public float Hour, FlowMs, SimHour, SimDay, SimCit; public int Top1, Top2; public float Top1Ms, Top2Ms; }
        readonly Spike[] _spikes = new Spike[MaxSpikes];
        int _nSpikes;

        static readonly string[] SecNames = Enum.GetNames(typeof(PerfProbe.S));

        // ------------------------------------------------------------------ per frame (from Update)

        void PerfFrame()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_probeT0 > 0 && _probeStage == 2)
            {
                Debug.Log($"[Probe] scene reload + load to the first frame of the loaded game: {(Time.realtimeSinceStartupAsDouble - _probeT0) * 1000:F0} ms");
                _probeT0 = 0;
                Application.Quit();
                return;
            }
            if (_phaseOn && _haveLast && Sim != null)
            {
                float ms = (float)PerfProbe.Ms(now - _lastTick);
                if (_pf < MaxFrames) _frameMs[_pf] = ms;
                _pf++;
                for (int i = 0; i < PerfProbe.N; i++)
                {
                    double m = PerfProbe.Ms(PerfProbe.Ticks[i]);
                    _secMs[i] += m; if (m > _secMax[i]) _secMax[i] = m;
                    _secBytes[i] += PerfProbe.Bytes[i]; _secCalls[i] += PerfProbe.Calls[i];
                }
                int gc = GC.CollectionCount(0);
                bool gcHere = gc != _gcLast; _gcLast = gc;
                if (gcHere) { _gcFrames++; _gcFrameMs += ms; _gcFrameMax = math.max(_gcFrameMax, ms); }
                double cit = Sim.ProfCitizens - _lastCit, hr = Sim.ProfHourly - _lastHour, dy = Sim.ProfDaily - _lastDayMs, jb = Sim.ProfJobs - _lastJobs;
                _lastCit = Sim.ProfCitizens; _lastHour = Sim.ProfHourly; _lastDayMs = Sim.ProfDaily; _lastJobs = Sim.ProfJobs;
                _simCit += cit; _simHour += hr; _simDay += dy; _simJobs += jb;
                if (hr > 0) { _hourFrames++; _hourMsSum += ms; _hourMsMax = math.max(_hourMsMax, ms); }
                if (Sim.Day != _lastDay) { _dayFrames++; _dayMsSum += ms; _dayMsMax = math.max(_dayMsMax, ms); }
                _lastDay = Sim.Day;
                int flow = Map.Flow.FieldsBuilt - _lastFlow; double flowMs = Map.Flow.TotalBuildMs - _lastFlowMs;
                _lastFlow = Map.Flow.FieldsBuilt; _lastFlowMs = Map.Flow.TotalBuildMs;
                // Keep the worst frames (no allocation: a small array kept sorted by time).
                if (_nSpikes < MaxSpikes || ms > _spikes[_nSpikes - 1].Ms)
                {
                    var sp = new Spike { Ms = ms, Frame = _pf, Day = Sim.Day, Hour = Sim.DayFraction * 24f, Gc = gcHere ? 1 : 0, Flow = flow, FlowMs = (float)flowMs,
                                         SimHour = (float)hr, SimDay = (float)dy, SimCit = (float)cit, Top1 = -1, Top2 = -1 };
                    for (int i = 0; i < PerfProbe.N; i++)
                    {
                        float m = (float)PerfProbe.Ms(PerfProbe.Ticks[i]);
                        if (sp.Top1 < 0 || m > sp.Top1Ms) { sp.Top2 = sp.Top1; sp.Top2Ms = sp.Top1Ms; sp.Top1 = i; sp.Top1Ms = m; }
                        else if (sp.Top2 < 0 || m > sp.Top2Ms) { sp.Top2 = i; sp.Top2Ms = m; }
                    }
                    int k = _nSpikes < MaxSpikes ? _nSpikes++ : MaxSpikes - 1;
                    while (k > 0 && _spikes[k - 1].Ms < ms) { _spikes[k] = _spikes[k - 1]; k--; }
                    _spikes[k] = sp;
                }
            }
            _lastTick = now; _haveLast = true;
            PerfProbe.ClearFrame();
        }

        void BeginPhase(string name)
        {
            _phase = name; _phaseOn = true; _pf = 0; _haveLast = false;
            _frameMs ??= new float[MaxFrames];
            Array.Clear(_secMs, 0, PerfProbe.N); Array.Clear(_secMax, 0, PerfProbe.N); Array.Clear(_secBytes, 0, PerfProbe.N); Array.Clear(_secCalls, 0, PerfProbe.N);
            _gc0Start = _gcLast = GC.CollectionCount(0); _gcFrames = 0; _gcFrameMs = _gcFrameMax = 0;
            _simCit = _simHour = _simDay = _simJobs = 0; _hourFrames = _dayFrames = 0; _hourMsSum = _hourMsMax = _dayMsSum = _dayMsMax = 0;
            Sim.Profile = true;
            _lastCit = Sim.ProfCitizens; _lastHour = Sim.ProfHourly; _lastDayMs = Sim.ProfDaily; _lastJobs = Sim.ProfJobs;
            _simTime0 = Sim.Time; _lastDay = Sim.Day;
            _flow0 = _lastFlow = Map.Flow.FieldsBuilt; _flowMs0 = _lastFlowMs = Map.Flow.TotalBuildMs; _flowBuilds0 = Map.Flow.Builds;
            _heap0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            _nSpikes = 0;
            PerfProbe.ClearFrame();
        }

        void EndPhase()
        {
            _phaseOn = false;
            int n = math.min(_pf, MaxFrames);
            if (n == 0) { Debug.Log($"[Probe] phase {_phase}: no frames"); return; }
            var sorted = new float[n]; Array.Copy(_frameMs, sorted, n); Array.Sort(sorted);
            double sum = 0; int over16 = 0, over33 = 0, over50 = 0;
            for (int i = 0; i < n; i++) { sum += sorted[i]; if (sorted[i] > 16.7f) over16++; if (sorted[i] > 33.3f) over33++; if (sorted[i] > 50f) over50++; }
            float P(double q) => sorted[math.min(n - 1, (int)(q * n))];
            double secs = sum / 1000.0;
            double steps = (Sim.Time - _simTime0) / Settlement.Step;
            int gcs = GC.CollectionCount(0) - _gc0Start;
            var sb = new System.Text.StringBuilder();
            sb.Append($"[Probe] phase {_phase}: {n} frames in {secs:F1} s, speed {Sim.Speed}x, population {Sim.Population}, buildings {Sim.Buildings.Count}, day {Sim.Day}\n");
            sb.Append($"  frame ms: avg {sum / n:F2}, p50 {P(0.5):F2}, p90 {P(0.9):F2}, p99 {P(0.99):F2}, p99.9 {P(0.999):F2}, max {sorted[n - 1]:F1}; frames >16.7 ms {over16}, >33 ms {over33}, >50 ms {over50}\n");
            sb.Append($"  GC gen0: {gcs} ({gcs / math.max(secs, 0.001) * 60:F1}/min); frames with a GC: {_gcFrames}, avg {(_gcFrames > 0 ? _gcFrameMs / _gcFrames : 0):F2} ms, max {_gcFrameMax:F1} ms; heap now {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1048576.0:F1} MB (start {_heap0 / 1048576.0:F1})\n");
            sb.Append($"  sim: {steps:F0} steps ({steps / n:F2} per frame); ms per step: total {_secMs[(int)PerfProbe.S.Sim] / math.max(1, steps):F3} = citizens {_simCit / math.max(1, steps):F3} + hourly {_simHour / math.max(1, steps):F3} + daily {_simDay / math.max(1, steps):F3} + jobs {_simJobs / math.max(1, steps):F3}\n");
            sb.Append($"  hour-tick frames {_hourFrames}: avg {(_hourFrames > 0 ? _hourMsSum / _hourFrames : 0):F2} ms, max {_hourMsMax:F1}; day-tick frames {_dayFrames}: avg {(_dayFrames > 0 ? _dayMsSum / _dayFrames : 0):F2} ms, max {_dayMsMax:F1}\n");
            sb.Append($"  flow fields built {Map.Flow.FieldsBuilt - _flow0} in {Map.Flow.Builds - _flowBuilds0} batches, {Map.Flow.TotalBuildMs - _flowMs0:F1} ms total\n");
            sb.Append("  section: avg ms/frame | max ms | calls/frame | heap growth bytes/frame (block-granular)\n");
            for (int i = 0; i < PerfProbe.N; i++)
            {
                if (_secCalls[i] == 0) continue;
                sb.Append($"    {SecNames[i],-12} {_secMs[i] / n,8:F3} | {_secMax[i],7:F2} | {_secCalls[i] / (double)n,5:F2} | {_secBytes[i] / (double)n,9:F0}\n");
            }
            sb.Append("  worst frames: ms (frame, day hh) top sections; gc; flow fields built; sim hourly/daily/citizens ms\n");
            for (int i = 0; i < _nSpikes; i++)
            {
                var s = _spikes[i];
                sb.Append($"    {s.Ms,7:F1} (#{s.Frame}, d{s.Day} {s.Hour:00.0}h) {(s.Top1 >= 0 ? SecNames[s.Top1] : "-")} {s.Top1Ms:F1}, {(s.Top2 >= 0 ? SecNames[s.Top2] : "-")} {s.Top2Ms:F1}; gc {s.Gc}; flow {s.Flow} ({s.FlowMs:F1} ms); sim h {s.SimHour:F1} d {s.SimDay:F1} c {s.SimCit:F1}\n");
            }
            Debug.Log(sb.ToString());
        }

        // ------------------------------------------------------------------ the plan

        static float[] ParseList(string s, float[] fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            var parts = s.Split(',');
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = float.Parse(parts[i], System.Globalization.CultureInfo.InvariantCulture);
            return r;
        }

        RenderTexture _probeRT;
        UnityEngine.Rendering.RenderPipeline.StandardRequest _probeReq;

        /// <summary>Batch mode renders nothing by itself: one frame into the texture, timed as its own section.</summary>
        void ProbeRender()
        {
            if (!Application.isBatchMode) return;
            if (_probeRT == null)
            {
                _probeRT = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                _probeReq = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = _probeRT };
            }
            PerfProbe.Begin(PerfProbe.S.Render);
            UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, _probeReq);
            PerfProbe.End(PerfProbe.S.Render);
        }

        IEnumerator Run(string name, float secs, Action every = null)
        {
            BeginPhase(name);
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (Time.realtimeSinceStartupAsDouble - t0 < secs)
            {
                ProbeRender();
                every?.Invoke();
                yield return null;
            }
            EndPhase();
        }

        IEnumerator PerfRun()
        {
            string outDir = Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "perfprobe");
            Directory.CreateDirectory(outDir);
            if (_probeStage == 2) yield break;                     // the reloaded scene only reports (PerfFrame)
            if (InMenu || _probeStage == 1)
            {
                yield return PerfStartup();
                yield break;
            }
            // In memory only: keep the player's autosave slots (-perfautosave keeps autosaves on, to time a season start with its save).
            if (!HasArg("-perfautosave")) UI.GameSettings.Current.autosave = false;
            for (int i = 0; i < 20; i++) { ProbeRender(); yield return null; }
            Calibrate();
            if (HasArg("-perfauto")) Auto ??= new AutoPlayer(Sim); else Auto = null;
            float secs = ArgInt("-perfsecs", 60);
            var speeds = ParseList(Arg("-perfspeeds"), new[] { 1f, 2f, 4f });
            Cam.SetView(Cam.Focus, 30f, 50f, 25f);
            if (Sim.TownCenter != null) { Hud.StopCamera(); Cam.SetView(new Vector3(Sim.TownCenter.Center.x, Map.HeightAt(Sim.TownCenter.Center.x, Sim.TownCenter.Center.y), Sim.TownCenter.Center.y), 30f, 50f, 25f); }

            if (!HasArg("-perfnobase"))
                foreach (var sp in speeds) { SetSpeed(sp); yield return Run($"town {sp}x", secs); }

            if (HasArg("-perfpanels")) yield return Panels(secs);
            if (HasArg("-perfplace")) yield return Repath(secs);
            if (HasArg("-perfsave")) SaveLoadTimes(outDir);
            if (HasArg("-perfflood")) yield return Flood();

            foreach (var crowd in ParseList(Arg("-perfcrowds"), new float[0]))
            {
                int add = (int)crowd - Sim.Population;
                if (add > 0) Sim.DebugSpawn(add);
                for (int i = 0; i < 30; i++) { ProbeRender(); yield return null; }
                foreach (var sp in speeds) { SetSpeed(sp); yield return Run($"crowd {Sim.Population} {sp}x", math.max(20, secs / 2)); }
                if (HasArg("-perfsave")) SaveLoadTimes(outDir);
            }

            if (HasArg("-perfreload"))
            {
                string path = Path.Combine(outDir, "probe-reload" + SaveGame.Extension);
                SaveTo(path, "probe", "probe");
                _probeStage = 2; _probeT0 = Time.realtimeSinceStartupAsDouble;
                LoadGame(path);
                yield break;
            }
            Debug.Log("[Probe] done");
            Application.Quit();
        }

        /// <summary>Checks the heap-growth reading against known allocations, and what reading the command line costs.</summary>
        void Calibrate()
        {
            long m0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), g0 = GC.GetTotalMemory(false); int c0 = GC.CollectionCount(0);
            object keep = null;
            for (int i = 0; i < 1000; i++) keep = new byte[1000];
            long m1 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(), g1 = GC.GetTotalMemory(false);
            for (int i = 0; i < 1000; i++) keep = Environment.GetCommandLineArgs();
            long m2 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) keep = HasArg("-uishots");
            double us = sw.Elapsed.TotalMilliseconds;
            long m3 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
            Debug.Log($"[Probe] calibration: 1000 x byte[1000] read as {m1 - m0} bytes (Profiler.GetMonoUsedSizeLong) / {g1 - g0} (GC.GetTotalMemory); " +
                      $"1000 x Environment.GetCommandLineArgs() ({Environment.GetCommandLineArgs().Length} args) {m2 - m1} bytes; 1000 x HasArg {m3 - m2} bytes, {us * 1000 / 1000:F2} us each; " +
                      $"collections during calibration {GC.CollectionCount(0) - c0} (a collection makes these readings low); {keep != null}");
        }

        IEnumerator Panels(float secs)
        {
            float s = math.max(15, secs / 2);
            SetSpeed(1);
            Hud.ToggleJobs(); yield return Run("panel jobs (1x)", s); Hud.ToggleJobs();
            Hud.ToggleStocks(); yield return Run("panel goods (1x)", s); Hud.ToggleStocks();
            Hud.ToggleGoals(); yield return Run("panel goals (1x)", s); Hud.ToggleGoals();
            Hud.ToggleResearch(); yield return Run("research screen (1x)", s); Hud.ToggleResearch();
            if (Sim.TownCenter != null) { Hud.Select(Sim.TownCenter); yield return Run("campfire selected (1x)", s); Hud.Select(null); }
            Citizen c = null; foreach (var x in Sim.Citizens) if (!x.Gone) { c = x; break; }
            if (c != null) { Hud.SelectCitizen(c); yield return Run("citizen selected (1x)", s); Hud.SelectCitizen(null); }
            var def = Data.Building("lean_to") ?? Data.Buildings.Find(b => !b.townCenter && !b.path);
            Hud.PointerOverride = new Vector2(Screen.width / 2f, Screen.height / 2f);
            Hud.StartPlacing(def); yield return Run($"placing {def.id} (1x)", s); Hud.CancelTool();
            Hud.PointerOverride = null;
        }

        /// <summary>A path tile laid and lifted every few seconds near the campfire: each is a hard walk-cost change that drops every cached path.</summary>
        IEnumerator Repath(float secs)
        {
            int cell = -1;
            var tc = Sim.TownCenter.Center;
            for (int r = 3; r < 20 && cell < 0; r++)
                for (int dy = -r; dy <= r && cell < 0; dy++)
                    for (int dx = -r; dx <= r && cell < 0; dx++)
                    {
                        int x = (int)tc.x + dx, y = (int)tc.y + dy;
                        if (!Map.InBounds(x, y)) continue;
                        int c = Map.Cell(x, y);
                        if (Map.BuildingAt[c] < 0 && !Map.Path[c] && !Map.Reserved[c] && Map.LiveCost[c] == GameMap.CostGround) cell = c;
                    }
            if (cell < 0) { Debug.Log("[Probe] repath: no free cell"); yield break; }
            foreach (float sp in new[] { 1f, 4f })
            {
                SetSpeed(sp);
                double last = Time.realtimeSinceStartupAsDouble; bool on = false;
                yield return Run($"path tile toggled every 5 s ({sp}x)", math.max(30, secs / 2), () =>
                {
                    if (Time.realtimeSinceStartupAsDouble - last < 5) return;
                    last = Time.realtimeSinceStartupAsDouble; on = !on;
                    Map.SetPath(cell, on);
                    Map.CommitCostChanges();            // as the path commands do (Settlement.PlacePath / RemovePath)
                });
                if (on) { Map.SetPath(cell, false); Map.CommitCostChanges(); }
            }
        }

        IEnumerator Flood()
        {
            SetSpeed(4);
            Sim.DebugFloodTomorrow(Data.Balance.floodStrengthMax);
            // Wait for the flood by real time, not frames (a fast build runs 2000 frames in 3 s, before tomorrow comes).
            double w0 = Time.realtimeSinceStartupAsDouble;
            while (!Sim.Flooding && Time.realtimeSinceStartupAsDouble - w0 < 60) { ProbeRender(); yield return null; }
            BeginPhase("sim flood at strength max (4x)");
            double t0 = Time.realtimeSinceStartupAsDouble;
            while (Sim.Flooding && Time.realtimeSinceStartupAsDouble - t0 < 120) { ProbeRender(); yield return null; }
            for (int i = 0; i < 120; i++) { ProbeRender(); yield return null; }   // the water draining back
            EndPhase();
            SetSpeed(1);
            StartFlood();                                       // the F6 debug surge: 80 s of water only
            yield return Run("F6 surge (1x)", 85);
        }

        void SaveLoadTimes(string outDir)
        {
            string path = Path.Combine(outDir, "probe-save" + SaveGame.Extension);
            for (int i = 0; i < 3; i++)
            {
                long m0 = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong(); int g0 = GC.CollectionCount(0);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SaveTo(path, "probe", "probe");
                double ms = sw.Elapsed.TotalMilliseconds;
                Debug.Log($"[Probe] save {i + 1}: {ms:F1} ms on the main thread, {new FileInfo(path).Length / 1024.0:F0} KB, heap growth {(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() - m0) / 1024.0:F0} KB, collections {GC.CollectionCount(0) - g0}; population {Sim.Population}, buildings {Sim.Buildings.Count}");
            }
            for (int i = 0; i < 3; i++)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var l = SaveGame.Read(path, Data, out string err);
                double ms = sw.Elapsed.TotalMilliseconds;
                Debug.Log($"[Probe] load (SaveGame.Read: map regenerated + state read, no scene) {i + 1}: {ms:F1} ms {(l == null ? err : "")}");
                l?.Map.Dispose();
            }
            var w = System.Diagnostics.Stopwatch.StartNew();
            StateDigest();
            Debug.Log($"[Probe] StateDigest {w.Elapsed.TotalMilliseconds:F1} ms");
        }

        /// <summary>-perfstartup SEED (no -load): the title menu's first frame, then New game through the real flow.</summary>
        IEnumerator PerfStartup()
        {
            if (_probeStage == 1)
            {
                yield return null;
                Debug.Log($"[Probe] new game: {(Time.realtimeSinceStartupAsDouble - _probeT0) * 1000:F0} ms from the New game request to the first frame (campfire placement)");
                for (int i = 0; i < 30; i++) { ProbeRender(); yield return null; }
                yield return Run("new game, campfire prompt", ArgInt("-perfsecs", 10));
                Application.Quit();
                yield break;
            }
            yield return null;
            PerfProbe.Mark("first title-menu frame");
            for (int i = 0; i < 10; i++) { ProbeRender(); yield return null; }
            yield return Run("title menu", ArgInt("-perfsecs", 10));
            _probeStage = 1; _probeT0 = Time.realtimeSinceStartupAsDouble;
            StartNewGame(ArgInt("-perfstartup", 2026), 1);
        }
    }
}
