using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Firstlight.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using Debug = UnityEngine.Debug;
using Random = Unity.Mathematics.Random;

namespace Firstlight.StressTest
{
    /// <summary>
    /// Stress test: N citizens as plain data walking between home and work on a 256x256 grid, using cached
    /// flow fields, Jobs + Burst, staggered think ticks and GPU-instanced drawing near the camera.
    /// Keys: 1/2/3 = 5k/10k/20k citizens, B = run the benchmark. Command line -benchmark runs it and quits.
    /// </summary>
    public sealed class StressTestController : MonoBehaviour
    {
        const int MapSize = 256;
        const int BlockSize = 16;          // homes are grouped in 16x16 blocks, one flow field per block
        const int WorkplaceCount = 64;
        const int ThinkStagger = 8;        // each citizen "thinks" once every 8 frames
        const float WalkSpeed = 1.4f;      // cells per second

        // Map
        NativeArray<byte> _cost;
        NativeArray<byte> _fields;
        int _fieldCount;
        readonly List<int> _workCells = new();
        readonly List<int> _blockCells = new();
        double _fieldBuildMs;

        // Citizens (structure of arrays)
        int _n;
        NativeArray<float2> _pos, _goal, _home, _work;
        NativeArray<float> _yaw, _speed, _timer, _hunger, _energy;
        NativeArray<int> _slot, _homeSlot, _workSlot;
        NativeArray<byte> _state, _arrived, _hidden;
        NativeArray<uint> _color;
        NativeArray<float4> _planes;
        NativeList<InstanceData> _visible;
        NativeArray<float> _noHeights;

        InstanceBatch _crowd, _blocks;
        OrbitCamera _cam;
        int _frame;

        // Stats
        readonly Stopwatch _sw = new();
        double _simMs, _renderPrepMs;
        float _fpsSmoothed;
        string _status = "";
        bool _benchmarkRunning;

        void Start()
        {
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            MaterialLibrary.SetDensity(64);
            _cam = WorldLighting.CreateCamera();
            _cam.BoundsMax = new Vector2(MapSize, MapSize);
            _cam.MaxDistance = 380f;
            WorldLighting.CreateSun();
            _cam.SetView(new Vector3(MapSize / 2f, 0, MapSize / 2f), 60f, 50f, 30f);

            BuildMap();
            Spawn(5000);

            if (Environment.GetCommandLineArgs().Contains("-benchmark"))
                StartCoroutine(RunBenchmark(true));
        }

        // ------------------------------------------------------------------ map

        void BuildMap()
        {
            var rng = new Random(12345);
            int cells = MapSize * MapSize;
            _cost = new NativeArray<byte>(cells, Allocator.Persistent);
            for (int i = 0; i < cells; i++) _cost[i] = 1;

            var blockInstances = new List<InstanceData>();
            // River with six bridges.
            for (int y = 0; y < MapSize; y++)
            {
                int cx = (int)(MapSize * 0.55f + math.sin(y * 0.035f) * 22f);
                bool bridge = y % 42 == 20;
                for (int x = cx - 2; x <= cx + 2; x++)
                {
                    if (bridge) { _cost[y * MapSize + x] = 2; continue; }
                    _cost[y * MapSize + x] = 0;
                    blockInstances.Add(new InstanceData(new float3(x + 0.5f, -0.45f, y + 0.5f), 0, new float3(1, 0.5f, 1), Pal.Pack(Pal.Hex("#5d7480"))));
                }
            }
            // Random obstacles (stand-ins for buildings / rocks).
            for (int k = 0; k < 900; k++)
            {
                int w = rng.NextInt(2, 6), h = rng.NextInt(2, 6);
                int x0 = rng.NextInt(2, MapSize - w - 2), y0 = rng.NextInt(2, MapSize - h - 2);
                bool clear = true;
                for (int y = y0 - 1; y < y0 + h + 1 && clear; y++)
                    for (int x = x0 - 1; x < x0 + w + 1; x++)
                        if (_cost[y * MapSize + x] != 1) { clear = false; break; }
                if (!clear) continue;
                for (int y = y0; y < y0 + h; y++)
                    for (int x = x0; x < x0 + w; x++)
                        _cost[y * MapSize + x] = 0;
                float height = rng.NextFloat(0.8f, 2.2f);
                blockInstances.Add(new InstanceData(new float3(x0 + w / 2f, 0, y0 + h / 2f), 0, new float3(w, height, h),
                    Pal.Pack(Color.Lerp(Pal.Hex("#8a7f70"), Pal.Hex("#6f6a62"), rng.NextFloat()))));
            }

            // Destinations: workplaces + one target per home block.
            for (int k = 0; k < WorkplaceCount; k++) _workCells.Add(RandomWalkable(ref rng));
            for (int by = 0; by < MapSize / BlockSize; by++)
                for (int bx = 0; bx < MapSize / BlockSize; bx++)
                    _blockCells.Add(NearestWalkable(bx * BlockSize + BlockSize / 2, by * BlockSize + BlockSize / 2));
            foreach (int c in _workCells)
                blockInstances.Add(new InstanceData(new float3(c % MapSize + 0.5f, 0, c / MapSize + 0.5f), 0, new float3(1.2f, 2.5f, 1.2f), Pal.Pack(Pal.Hex("#9a6b4f"))));

            _fieldCount = _workCells.Count + _blockCells.Count;
            _fields = new NativeArray<byte>(cells * _fieldCount, Allocator.Persistent);
            var targets = new NativeArray<int>(_fieldCount, Allocator.TempJob);
            var slots = new NativeArray<int>(_fieldCount, Allocator.TempJob);
            for (int i = 0; i < _fieldCount; i++)
            {
                targets[i] = i < _workCells.Count ? _workCells[i] : _blockCells[i - _workCells.Count];
                slots[i] = i;
            }
            var sw = Stopwatch.StartNew();
            new BuildFlowFieldsJob { cost = _cost, targets = targets, slots = slots, fields = _fields, width = MapSize, height = MapSize }
                .Schedule(_fieldCount, 1).Complete();
            _fieldBuildMs = sw.Elapsed.TotalMilliseconds;
            targets.Dispose(); slots.Dispose();
            Debug.Log($"[StressTest] Built {_fieldCount} flow fields on {MapSize}x{MapSize} in {_fieldBuildMs:F1} ms");

            _blocks = new InstanceBatch(Shapes.UnitBox, blockInstances.Count);
            _blocks.Upload(blockInstances.ToArray(), blockInstances.Count);

            // Ground plane.
            var mb = new MeshBuilder();
            mb.Box(MeshBuilder.TRS(new Vector3(MapSize / 2f, -1f, MapSize / 2f), 0, new Vector3(MapSize, 1f, MapSize)), Pal.Hex("#7d8a5e"));
            var ground = new GameObject("Ground");
            ground.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("ground");
            ground.AddComponent<MeshRenderer>().sharedMaterial = WorldLighting.FlatMaterial(Color.white);

            _crowd = new InstanceBatch(Shapes.Citizen, 1024);
            _planes = new NativeArray<float4>(6, Allocator.Persistent);
            _noHeights = new NativeArray<float>(0, Allocator.Persistent);
        }

        int RandomWalkable(ref Random rng)
        {
            while (true)
            {
                int c = rng.NextInt(0, MapSize * MapSize);
                if (_cost[c] == 1) return c;
            }
        }

        int NearestWalkable(int x, int y)
        {
            for (int r = 0; r < MapSize; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= MapSize || ny >= MapSize) continue;
                        if (_cost[ny * MapSize + nx] != 0) return ny * MapSize + nx;
                    }
            return 0;
        }

        // ------------------------------------------------------------------ citizens

        void DisposeCitizens()
        {
            if (!_pos.IsCreated) return;
            _pos.Dispose(); _goal.Dispose(); _home.Dispose(); _work.Dispose();
            _yaw.Dispose(); _speed.Dispose(); _timer.Dispose(); _hunger.Dispose(); _energy.Dispose();
            _slot.Dispose(); _homeSlot.Dispose(); _workSlot.Dispose();
            _state.Dispose(); _arrived.Dispose(); _hidden.Dispose(); _color.Dispose();
            _visible.Dispose();
        }

        void Spawn(int n)
        {
            DisposeCitizens();
            _n = n;
            var a = Allocator.Persistent;
            _pos = new NativeArray<float2>(n, a); _goal = new NativeArray<float2>(n, a);
            _home = new NativeArray<float2>(n, a); _work = new NativeArray<float2>(n, a);
            _yaw = new NativeArray<float>(n, a); _speed = new NativeArray<float>(n, a);
            _timer = new NativeArray<float>(n, a); _hunger = new NativeArray<float>(n, a); _energy = new NativeArray<float>(n, a);
            _slot = new NativeArray<int>(n, a); _homeSlot = new NativeArray<int>(n, a); _workSlot = new NativeArray<int>(n, a);
            _state = new NativeArray<byte>(n, a); _arrived = new NativeArray<byte>(n, a); _hidden = new NativeArray<byte>(n, a);
            _color = new NativeArray<uint>(n, a);
            _visible = new NativeList<InstanceData>(n, a);

            var rng = new Random(777);
            uint[] palette =
            {
                Pal.Pack(Pal.Hex("#7a6a55")), Pal.Pack(Pal.Hex("#5f6b4e")), Pal.Pack(Pal.Hex("#6b5a4a")),
                Pal.Pack(Pal.Hex("#837865")), Pal.Pack(Pal.Hex("#4f5a5e")), Pal.Pack(Pal.Hex("#8b6f52")),
            };
            for (int i = 0; i < n; i++)
            {
                int homeCell = RandomWalkable(ref rng);
                int hx = homeCell % MapSize, hy = homeCell / MapSize;
                int block = (hy / BlockSize) * (MapSize / BlockSize) + hx / BlockSize;
                int workIdx = rng.NextInt(0, WorkplaceCount);
                int wc = _workCells[workIdx];
                _home[i] = new float2(hx + 0.5f, hy + 0.5f);
                _work[i] = new float2(wc % MapSize + rng.NextFloat(0.1f, 0.9f), wc / MapSize + rng.NextFloat(0.1f, 0.9f));
                _homeSlot[i] = WorkplaceCount + block;
                _workSlot[i] = workIdx;
                _speed[i] = WalkSpeed * rng.NextFloat(0.85f, 1.15f);
                _color[i] = palette[rng.NextInt(0, palette.Length)];
                _hunger[i] = rng.NextFloat();
                _energy[i] = rng.NextFloat();
                // Start spread out: half walking to work, half walking home, from a random point on the route.
                bool toWork = rng.NextBool();
                float t = rng.NextFloat();
                _pos[i] = math.lerp(_home[i], _work[i], t);
                int pc = (int)_pos[i].y * MapSize + (int)_pos[i].x;
                if (_cost[pc] == 0) _pos[i] = _home[i];
                _goal[i] = toWork ? _work[i] : _home[i];
                _slot[i] = toWork ? _workSlot[i] : _homeSlot[i];
                _state[i] = (byte)(toWork ? CitizenState.WalkingToWork : CitizenState.WalkingHome);
            }
            _status = $"{n:N0} citizens";
        }

        enum CitizenState : byte { AtHome, WalkingToWork, AtWork, WalkingHome }

        /// <summary>Decisions and needs. Each frame only 1/ThinkStagger of the citizens run this.</summary>
        [BurstCompile]
        struct ThinkJob : IJobParallelFor
        {
            public int start, total;
            public float dt;
            public uint frameSeed;
            [NativeDisableParallelForRestriction] public NativeArray<byte> state, arrived, hidden;
            [NativeDisableParallelForRestriction] public NativeArray<float> timer, hunger, energy;
            [NativeDisableParallelForRestriction] public NativeArray<int> slot;
            [NativeDisableParallelForRestriction] public NativeArray<float2> goal;
            [ReadOnly] public NativeArray<float2> home, work;
            [ReadOnly] public NativeArray<int> homeSlot, workSlot;

            public void Execute(int k)
            {
                int i = (start + k) % total;
                var rng = new Random((uint)(i * 9781 + 1) ^ frameSeed | 1u);
                var s = (CitizenState)state[i];
                // Needs tick (stand-in for the real game's food / warmth / health work).
                hunger[i] = math.saturate(hunger[i] + dt * 0.01f);
                energy[i] = math.saturate(energy[i] - dt * (s == CitizenState.AtWork ? 0.02f : -0.015f));
                if (arrived[i] != 0)
                {
                    arrived[i] = 0;
                    slot[i] = -1;
                    s = s == CitizenState.WalkingToWork ? CitizenState.AtWork : CitizenState.AtHome;
                    timer[i] = rng.NextFloat(8f, 25f);
                    hidden[i] = (byte)(s == CitizenState.AtHome ? 1 : 0); // indoors at home
                    if (s == CitizenState.AtHome) hunger[i] = 0;
                }
                else if (s == CitizenState.AtHome || s == CitizenState.AtWork)
                {
                    timer[i] -= dt;
                    if (timer[i] <= 0)
                    {
                        bool toWork = s == CitizenState.AtHome;
                        s = toWork ? CitizenState.WalkingToWork : CitizenState.WalkingHome;
                        goal[i] = toWork ? work[i] : home[i];
                        slot[i] = toWork ? workSlot[i] : homeSlot[i];
                        hidden[i] = 0;
                    }
                }
                state[i] = (byte)s;
            }
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && !_benchmarkRunning)
            {
                if (kb.digit1Key.wasPressedThisFrame) Spawn(5000);
                if (kb.digit2Key.wasPressedThisFrame) Spawn(10000);
                if (kb.digit3Key.wasPressedThisFrame) Spawn(20000);
                if (kb.bKey.wasPressedThisFrame) StartCoroutine(RunBenchmark(false));
                if (kb.escapeKey.wasPressedThisFrame) Application.Quit();
            }

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _frame++;

            // --- simulation (timed: schedule + wait for completion)
            _sw.Restart();
            var move = new MoveAgentsJob
            {
                pos = _pos, yaw = _yaw, arrived = _arrived, goal = _goal, slot = _slot, speed = _speed,
                fields = _fields, cost = _cost, width = MapSize, height = MapSize, dt = dt, directRadius = 6f,
            }.Schedule(_n, 256);
            int batch = (_n + ThinkStagger - 1) / ThinkStagger;
            var think = new ThinkJob
            {
                start = (_frame % ThinkStagger) * batch, total = _n, dt = dt * ThinkStagger, frameSeed = (uint)_frame * 2654435761u,
                state = _state, arrived = _arrived, hidden = _hidden, timer = _timer, hunger = _hunger, energy = _energy,
                slot = _slot, goal = _goal, home = _home, work = _work, homeSlot = _homeSlot, workSlot = _workSlot,
            }.Schedule(Mathf.Min(batch, _n), 256, move);
            think.Complete();
            double sim = _sw.Elapsed.TotalMilliseconds;

            // --- render prep: cull to the camera and upload
            _sw.Restart();
            CullAgentsJob.FillPlanes(_cam.Cam, _planes);
            float radius = Mathf.Clamp(_cam.Distance * 2.2f, 60f, 400f);
            _visible.Clear();
            new CullAgentsJob
            {
                pos = _pos, yaw = _yaw, color = _color, hidden = _hidden, cornerHeights = _noHeights, planes = _planes,
                output = _visible.AsParallelWriter(), focus = new float2(_cam.Focus.x, _cam.Focus.z), radiusSq = radius * radius,
                scale = 1f, width = MapSize, height = MapSize,
            }.Schedule(_n, 512).Complete();
            _crowd.Upload(_visible.AsArray(), _visible.Length);
            double prep = _sw.Elapsed.TotalMilliseconds;

            _simMs = math.lerp(_simMs, sim, 0.1);
            _renderPrepMs = math.lerp(_renderPrepMs, prep, 0.1);
            _lastSim = sim; _lastPrep = prep;
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f), 0.05f);

            WorldLighting.UpdateForZoom(_cam.Distance);
            _blocks.Draw();
            _crowd.Draw();
        }

        double _lastSim, _lastPrep;

        void OnGUI()
        {
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 15 };
            style.normal.textColor = new Color(0.92f, 0.9f, 0.85f);
            GUI.Box(new Rect(10, 10, 430, 150),
                $"Firstlight stress test  —  {_status}\n" +
                $"FPS {_fpsSmoothed:F0}   sim {_simMs:F2} ms   cull+upload {_renderPrepMs:F2} ms\n" +
                $"drawn {_visible.Length:N0} of {_n:N0}   flow fields {_fieldCount} ({_fieldBuildMs:F0} ms to build)\n" +
                $"keys: 1/2/3 = 5k/10k/20k,  B = benchmark\n" +
                $"WASD pan, Q/E rotate, wheel zoom, right-drag orbit", style);
        }

        // ------------------------------------------------------------------ benchmark

        struct Result
        {
            public int n; public string view; public float avgFps, avgFrameMs, p99FrameMs, avgSimMs, maxSimMs, avgPrepMs; public int avgDrawn;
        }

        IEnumerator RunBenchmark(bool quitAfter)
        {
            _benchmarkRunning = true;
            var results = new List<Result>();
            string outDir = Path.Combine(Application.dataPath, "..", "runs");
            if (Application.isEditor) outDir = Path.Combine(Application.dataPath, "..", "runs");
            else outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "..", "runs");
            string dirArg = Arg("-outdir");
            if (dirArg != null) outDir = dirArg;
            Directory.CreateDirectory(outDir);

            foreach (int n in new[] { 5000, 10000, 20000 })
            {
                Spawn(n);
                foreach (var view in new[] { "street", "overview" })
                {
                    if (view == "street") _cam.SetView(new Vector3(MapSize * 0.45f, 0, MapSize * 0.5f), 45f, 48f, 30f);
                    else _cam.SetView(new Vector3(MapSize / 2f, 0, MapSize / 2f), 300f, 62f, 30f);
                    _status = $"benchmark: {n:N0} citizens, {view} view (warming up)";
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 3f) yield return null;

                    _status = $"benchmark: {n:N0} citizens, {view} view (measuring)";
                    var frames = new List<float>();
                    double simSum = 0, simMax = 0, prepSum = 0; long drawnSum = 0;
                    t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 8f)
                    {
                        yield return null;
                        frames.Add(Time.unscaledDeltaTime * 1000f);
                        simSum += _lastSim; simMax = Math.Max(simMax, _lastSim); prepSum += _lastPrep; drawnSum += _visible.Length;
                    }
                    frames.Sort();
                    float avg = 0; foreach (var f in frames) avg += f; avg /= frames.Count;
                    var r = new Result
                    {
                        n = n, view = view, avgFrameMs = avg, avgFps = 1000f / avg,
                        p99FrameMs = frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))],
                        avgSimMs = (float)(simSum / frames.Count), maxSimMs = (float)simMax,
                        avgPrepMs = (float)(prepSum / frames.Count), avgDrawn = (int)(drawnSum / frames.Count),
                    };
                    results.Add(r);
                    Debug.Log($"[StressTest] {n} {view}: {r.avgFps:F0} fps, frame {r.avgFrameMs:F2} ms (p99 {r.p99FrameMs:F2}), sim {r.avgSimMs:F2} ms (max {r.maxSimMs:F2}), cull+upload {r.avgPrepMs:F2} ms, drawn {r.avgDrawn}");
                    ScreenCapture.CaptureScreenshot(Path.Combine(outDir, $"stress_{n / 1000}k_{view}.png"));
                    yield return null; yield return null;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"# Stress test results ({DateTime.Now:yyyy-MM-dd HH:mm})");
            sb.AppendLine();
            sb.AppendLine($"- Machine: {SystemInfo.processorType} ({SystemInfo.processorCount} threads), {SystemInfo.graphicsDeviceName}, {SystemInfo.systemMemorySize / 1024} GB RAM");
            sb.AppendLine($"- Resolution {Screen.width}x{Screen.height}, vsync off, Windows player build (Mono + Burst), URP with shadows + 4x MSAA");
            sb.AppendLine($"- Map {MapSize}x{MapSize} cells, {_fieldCount} flow fields built in {_fieldBuildMs:F0} ms (parallel Burst Dijkstra)");
            sb.AppendLine($"- Movement every frame for all citizens; decisions/needs staggered over {ThinkStagger} frames");
            sb.AppendLine();
            sb.AppendLine("| Citizens | View | FPS | Frame ms (avg) | Frame ms (p99) | Sim ms (avg) | Sim ms (max) | Cull+upload ms | Drawn |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var r in results)
                sb.AppendLine($"| {r.n:N0} | {r.view} | {r.avgFps:F0} | {r.avgFrameMs:F2} | {r.p99FrameMs:F2} | {r.avgSimMs:F2} | {r.maxSimMs:F2} | {r.avgPrepMs:F2} | {r.avgDrawn:N0} |");
            File.WriteAllText(Path.Combine(outDir, "stress-results.md"), sb.ToString());
            Debug.Log(sb.ToString());
            _status = "benchmark done";
            _benchmarkRunning = false;
            if (quitAfter)
            {
                yield return new WaitForSecondsRealtime(1f);
                Application.Quit();
            }
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        void OnDestroy()
        {
            DisposeCitizens();
            if (_cost.IsCreated) _cost.Dispose();
            if (_fields.IsCreated) _fields.Dispose();
            if (_planes.IsCreated) _planes.Dispose();
            if (_noHeights.IsCreated) _noHeights.Dispose();
            _crowd?.Dispose();
            _blocks?.Dispose();
        }
    }

    static class ArgsExt
    {
        public static bool Contains(this string[] args, string a) => Array.IndexOf(args, a) >= 0;
    }
}
