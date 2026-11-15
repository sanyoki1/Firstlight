using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Headless fuzz test (-fuzztest -seed N -years Y -outdir DIR): a clumsy, hostile player drives the settlement through
    /// the same public commands the HUD uses (random valid and invalid placements, demolishing mid-construction and
    /// mid-upgrade, homes with people in them and full stores, random priorities, pauses, focus, laws, research queue
    /// churn, builders 0 or huge, path spam, trader answers, age-ups the moment they are allowed, saving and loading at
    /// random and awkward moments). On most seeds the auto-player plays alongside so the run reaches the later systems;
    /// on every third seed the hostile player is alone (the band usually dies out, which is a state to survive too).
    /// After every game day the invariants are checked; every violation is written to fuzz-report.md. Exit code 0 = no
    /// violations. Seeded and reproducible: the same seed twice makes the same report.
    /// </summary>
    public sealed class FuzzTest
    {
        readonly GameData D;
        GameMap M;
        Settlement S;
        AutoPlayer A;
        readonly SimRandom R;
        readonly int _seed;
        readonly bool _hostileOnly;
        readonly double _chaosRate;
        readonly string _outDir, _savePath;
        readonly SortedDictionary<string, int> _actions = new();
        readonly SortedDictionary<string, int> _violationCounts = new();
        readonly List<string> _violations = new();
        readonly List<string> _log = new();
        int _startPeople;
        bool _extinctNoted;
        int _saves, _loads, _roundTripsChecked;

        // Stuck detection: per citizen id, the day its task/position was last seen to change.
        readonly Dictionary<int, (int task, float2 pos, float since)> _still = new();
        readonly Dictionary<int, (int res, float amt, float since)> _carry = new();
        readonly Dictionary<int, List<string>> _trail = new();

        /// <summary>The map in use at the end (a load replaces it; the caller disposes this one).</summary>
        public GameMap Map => M;

        public FuzzTest(GameData data, GameMap map, int seed, string outDir)
        {
            D = data; M = map; _seed = seed; _outDir = outDir;
            R = new SimRandom(seed * 7919 + 13);
            _hostileOnly = seed % 3 == 0;
            // How often the hostile player acts (per game hour): alone it plays all the time; beside the auto-player it
            // is a rare nuisance on seeds 1, 4, 7... (so the run gets deep into the Agricultural Age) and a frequent one
            // on seeds 2, 5, 8...
            _chaosRate = _hostileOnly ? 0.5 : seed % 3 == 1 ? 0.025 : 0.08;
            _savePath = Path.Combine(outDir, "fuzz" + SaveGame.Extension);
        }

        void Act(string name) => _actions[name] = _actions.TryGetValue(name, out int n) ? n + 1 : 1;

        void Violation(string kind, string detail)
        {
            _violationCounts[kind] = _violationCounts.TryGetValue(kind, out int n) ? n + 1 : 1;
            if (_violationCounts[kind] <= 5) _violations.Add($"day {S?.Day} ({S?.DayFraction:F2}): [{kind}] {detail}");
        }

        public bool Run(int years)
        {
            var errors = new List<string>();
            void OnLog(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add($"{type}: {msg}" + (type == LogType.Exception ? "\n" + stack : ""));
            }
            Application.logMessageReceived += OnLog;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Directory.CreateDirectory(_outDir);
            float hour = D.Balance.dayLengthSeconds / 24f;
            try
            {
                S = new Settlement(D, M, _seed);
                A = new AutoPlayer(S);
                S.SetDifficulty(_hostileOnly ? R.Next(-1, 5) : R.Next(0, 2));   // out of range on purpose: clamped
                Act("difficulty");
                // Before the campfire: everything else must be refused; saving and loading must survive the empty state.
                for (int i = 0; i < 40; i++) RandomPlace(anywhere: true, campfire: false);
                SaveLoad("before the campfire");
                S.Tick(100f);                           // nothing runs without a campfire
                if (S.Day != 0 || S.Population != 0) Violation("pre-campfire", $"time moved or people appeared: day {S.Day}, pop {S.Population}");
                PlaceCampfire();
                _startPeople = S.Population;
                int totalDays = years * S.DaysPerYear, lastDay = S.Day, deadDays = 0;
                while (S.Day < totalDays)
                {
                    // A burst of chaos, sometimes, every game hour; the auto-player thinks most hours on mixed seeds.
                    if (R.NextDouble() < _chaosRate) Chaos(1 + R.Next(3));
                    if (!_hostileOnly && R.NextDouble() < 0.85) Guard("autoplayer", () => A.Think());
                    float dt = R.NextDouble() < 0.05 ? hour * (float)R.NextDouble() * 6f : hour;   // odd tick sizes too
                    S.Tick(dt);
                    Guard("stuck check", SampleStuck);
                    if (S.Day != lastDay)
                    {
                        lastDay = S.Day;
                        CheckInvariants();
                        if (S.Population == 0)
                        {
                            if (!_extinctNoted) { _extinctNoted = true; _log.Add($"day {S.Day}: everyone is gone (births {S.Births}, migrants {S.Migrants}, leavers {S.Leavers}, deaths {S.TotalDeaths})"); SaveLoad("after extinction"); }
                            if (++deadDays > 40) break;      // a while in the empty settlement is enough
                        }
                        AwkwardSaves();
                    }
                }
            }
            catch (Exception e)
            {
                Violation("exception", e.ToString());
                Debug.LogException(e);
            }
            Application.logMessageReceived -= OnLog;
            foreach (var e in errors) Violation("logged error", e);
            WriteReport(years, sw.Elapsed.TotalSeconds);
            Debug.Log($"[Fuzz] seed {_seed}: {(_violations.Count == 0 ? "PASS" : "FAIL")} ({_violationCounts.Values.Sum()} violations) in {sw.Elapsed.TotalSeconds:F1} s, day {S?.Day}, population {S?.Population}, saves {_saves}, loads {_loads}");
            return _violations.Count == 0;
        }

        void Guard(string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Violation("exception", $"{what}: {e}"); }
        }

        // ------------------------------------------------------------------ the hostile player

        void PlaceCampfire()
        {
            var tc = D.Buildings.First(b => b.townCenter);
            // A clumsy player clicks around before finding a spot (water, edges, rock); then somewhere valid.
            for (int i = 0; i < 300 && S.NeedsCampfire; i++)
            {
                var o = new int2(R.Next(-3, M.W + 3), R.Next(-3, M.H + 3));
                if (R.NextDouble() < 0.7 && !S.CanPlace(tc, o)) { Act("campfire refused"); continue; }
                if (S.Place(tc, o, R.Next(-2, 7), out _) != null) Act("campfire placed at random");
            }
            if (S.NeedsCampfire) { S.Place(tc, S.SuggestCampfireSite(), out _); Act("campfire placed at the suggestion"); }
            if (S.Place(tc, S.SuggestCampfireSite(), out _) != null) Violation("two campfires", "a second campfire was placed");
            if (S.Demolish(S.TownCenter)) Violation("campfire removed", "the campfire could be demolished");
        }

        void Chaos(int n)
        {
            for (int i = 0; i < n; i++)
            {
                int k = R.Next(24);
                Guard($"action {k}", () => Action(k));
            }
        }

        Building RandomBuilding(Func<Building, bool> filter)
        {
            var list = S.Buildings.Where(b => b.Alive && filter(b)).ToList();
            return list.Count == 0 ? null : list[R.Next(list.Count)];
        }

        void Action(int k)
        {
            switch (k)
            {
                case 0: case 1: case 2: RandomPlace(anywhere: k == 0); break;
                case 3:
                {
                    // Demolish something awkward: a site, an upgrade, an occupied home, a full store, the only store.
                    int kind = R.Next(6);
                    Building b = kind switch
                    {
                        0 => RandomBuilding(x => x.IsSite),
                        1 => RandomBuilding(x => x.UpgradeTo != null),
                        2 => RandomBuilding(x => x.Active && x.Def.housing > 0 && x.Residents > 0),
                        3 => RandomBuilding(x => x.Active && x.IsStorage && x.Stored > 1 && x != S.TownCenter),
                        4 => RandomBuilding(x => x.Active && x.Workers > 0),
                        _ => RandomBuilding(x => true),
                    };
                    if (b != null && S.Demolish(b)) Act($"demolish ({(kind == 0 ? "site" : kind == 1 ? "upgrade" : kind == 2 ? "occupied home" : kind == 3 ? "store with goods" : kind == 4 ? "staffed workplace" : "any")})");
                    if (S.Demolish(S.TownCenter)) Violation("campfire removed", "the campfire could be demolished");
                    if (b != null && !b.Alive && S.Demolish(b)) Violation("double demolish", "a removed building was demolished again");
                    break;
                }
                case 4: { var b = RandomBuilding(x => true); if (b != null) { S.SetPriority(b, (Priority)R.Next(3)); Act("priority"); } break; }
                case 5: { var b = RandomBuilding(x => true); if (b != null) { S.SetPaused(b, R.NextDouble() < 0.5); Act("pause"); } break; }
                case 6: { var b = RandomBuilding(x => true); if (b != null) { S.SetFocus(b, R.Next(-3, 6)); Act("focus"); } break; }
                case 7: { int[] opts = { 0, 0, 1, 3, 50, 1000, -5 }; S.SetBuilders(opts[R.Next(opts.Length)]); Act("builders"); break; }
                case 8:
                {
                    var r = D.Research[R.Next(D.Research.Count)];
                    if (R.NextDouble() < 0.6) { S.QueueResearch(R.NextDouble() < 0.05 ? "no_such_topic" : r.id, out _); Act("queue research"); }
                    else { S.DequeueResearch(R.NextDouble() < 0.5 && S.ResearchQueue.Count > 0 ? S.ResearchQueue[R.Next(S.ResearchQueue.Count)] : r.id); Act("dequeue research"); }
                    break;
                }
                case 9:
                {
                    var l = D.Laws.Count > 0 ? D.Laws[R.Next(D.Laws.Count)] : null;
                    if (l != null && S.SetLaw(l.id, R.NextDouble() < 0.6)) Act("law changed"); else Act("law refused");
                    break;
                }
                case 10:
                    if (S.PendingTrade != null) { if (R.NextDouble() < 0.5) S.AcceptTrade(); else S.DeclineTrade(); Act("trade answered"); }
                    if (R.NextDouble() < 0.1) { S.AutoTrade = !S.AutoTrade; Act("auto trade toggled"); }
                    S.AcceptTrade(); S.DeclineTrade();          // with nothing pending: no-ops
                    break;
                case 11: if (S.AdvanceAge()) Act("age advanced"); break;
                case 12: case 13: PathSpam(); break;
                case 14:
                {
                    var b = RandomBuilding(x => x.Active);
                    if (b == null) break;
                    if (b.UpgradeTo != null) { S.CancelUpgrade(b); Act("upgrade cancelled"); }
                    else if (S.Upgrade(b, out _)) Act("upgrade started");
                    break;
                }
                case 15: { float[] sp = { 0, 1, 2, 4, -1, 100 }; S.SetSpeed(sp[R.Next(sp.Length)]); Act("speed"); break; }
                case 16: if (R.NextDouble() < 0.25) SaveLoad("at random"); break;
                case 17: if (R.NextDouble() < 0.15) { S.DebugFloodTomorrow(0.3f + (float)R.NextDouble() * 0.3f); Act("flood forced"); } break;
                case 18: { var b = RandomBuilding(x => x.Def.crop); if (b != null) { S.SetFocus(b, R.Next(-1, b.Def.outputs.Length + 1)); Act("crop choice"); } break; }
                case 19: S.Upgrade(null, out _); S.CancelUpgrade(null); S.SetPriority(null, Priority.High); S.SetPaused(null, true); S.SetFocus(null, 1); S.Demolish(null); Act("null commands"); break;
                case 20:
                {
                    // Place the same thing twice on the same spot, and on a spot just taken.
                    var d = RandomDef();
                    var o = NearTown(12);
                    var first = S.Place(d, o, R.Next(4), out _);
                    if (first != null && S.Place(d, o, R.Next(4), out _) != null) Violation("overlap", $"{d.id} placed twice at {o}");
                    Act("double place");
                    break;
                }
                case 21: { var b = RandomBuilding(x => x.Active && x.Def.housing > 0); if (b != null && S.Demolish(b)) Act("demolish home"); break; }
                case 22:
                {
                    // Demolish every store but the campfire (zero storage apart from the fire's own).
                    if (_hostileOnly && R.NextDouble() < 0.1) foreach (var b in S.Buildings.Where(x => x.Alive && x.IsStorage && x != S.TownCenter).ToList()) { S.Demolish(b); Act("demolish all stores"); }
                    break;
                }
                case 23: { var b = RandomBuilding(x => x.Active); if (b != null && S.UpgradeOption(b) != null && S.Upgrade(b, out _)) { Act("upgrade then demolish"); if (R.NextDouble() < 0.5) S.Demolish(b); } break; }
            }
        }

        BuildingDef RandomDef() => D.Buildings[R.Next(D.Buildings.Count)];

        int2 NearTown(int reach)
        {
            var c = S.TownCenter != null ? S.TownCenter.Center : new float2(M.W / 2f, M.H / 2f);
            return new int2((int)c.x + R.Next(-reach, reach + 1), (int)c.y + R.Next(-reach, reach + 1));
        }

        void RandomPlace(bool anywhere, bool campfire = true)
        {
            var d = RandomDef();
            if (!campfire && d.townCenter) d = D.Buildings.First(b => !b.townCenter);
            int2 o = anywhere ? new int2(R.Next(-4, M.W + 4), R.Next(-4, M.H + 4)) : NearTown(R.NextDouble() < 0.5 ? 10 : 30);
            int rot = R.Next(-3, 9);
            bool can = S.CanPlace(d, o, rot, out string why);
            var b = S.Place(d, o, rot, out string why2);
            if (d.path) { Act(can ? "path via Place" : "path via Place refused"); return; }
            if (can != (b != null)) Violation("CanPlace/Place disagree", $"{d.id} at {o} rot {rot}: CanPlace {can} ({why}), Place {(b != null)} ({why2})");
            Act(b != null ? "placed" : "placement refused");
            if (b == null && string.IsNullOrEmpty(why2)) Violation("no reason", $"{d.id} at {o} refused without a reason");
        }

        void PathSpam()
        {
            var a = NearTown(15);
            int len = R.Next(1, 25), dx = R.Next(2) == 0 ? 1 : 0;
            for (int i = 0; i < len; i++)
            {
                var c = a + (dx == 1 ? new int2(i, 0) : new int2(0, i));
                if (R.NextDouble() < 0.15) { S.RemovePath(c); Act("path removed"); }
                else if (S.PlacePath(c)) Act("path laid"); else Act("path refused");
            }
            S.RemovePath(new int2(-5, -5)); S.PlacePath(new int2(M.W + 3, 2));
        }

        /// <summary>Saves and loads at the moments most likely to lose something: floods, upgrades, offers, extinction.</summary>
        void AwkwardSaves()
        {
            if (S.Flooding && R.NextDouble() < 0.3) SaveLoad("during a flood");
            else if (S.FloodWarned && R.NextDouble() < 0.5) SaveLoad("the day before a flood");
            else if (S.PendingTrade != null && R.NextDouble() < 0.5) SaveLoad("with a trader's offer open");
            else if (S.Buildings.Any(b => b.UpgradeTo != null) && R.NextDouble() < 0.05) SaveLoad("during an upgrade");
            else if (R.NextDouble() < 0.03) SaveLoad("on a plain day");
        }

        // ------------------------------------------------------------------ save / load round trip

        byte[] Digest()
        {
            var ms = new MemoryStream();
            var w = new SaveWriter(ms);
            M.Write(w); S.Write(w); w.Bool(A != null); A?.Write(w);
            w.W.Flush();
            return ms.ToArray();
        }

        void SaveLoad(string when)
        {
            Act("save+load " + when);
            byte[] before = Digest();
            SaveGame.Write(_savePath, SaveGame.HeaderFor(S, "fuzz", "test"), S, A, null, null, null);
            _saves++;
            var loaded = SaveGame.Read(_savePath, D, out string err);
            if (loaded == null) { Violation("load failed", $"{when}: {err}"); return; }
            _loads++;
            var oldMap = M;
            M = loaded.Map; S = loaded.Sim; A = loaded.Auto ?? new AutoPlayer(S);
            oldMap.Dispose();
            byte[] after = Digest();
            _roundTripsChecked++;
            if (!before.SequenceEqual(after))
            {
                int first = 0; while (first < Math.Min(before.Length, after.Length) && before[first] == after[first]) first++;
                Violation("save round trip", $"{when}: state differs after load at byte {first} of {before.Length} (now {after.Length} bytes)");
            }
        }

        // ------------------------------------------------------------------ invariants

        void CheckInvariants()
        {
            const float eps = 1e-3f;
            // Goods: finite, not negative, totals equal the stores.
            var sum = new double[S.ResCount];
            foreach (var b in S.Buildings)
            {
                if (b.Store != null)
                {
                    double st = 0;
                    for (int r = 0; r < S.ResCount; r++)
                    {
                        float v = b.Store[r];
                        if (!float.IsFinite(v) || v < -eps) Violation("bad stock", $"{b.Def.id}#{b.Id} store {D.Resources[r].id} = {v}");
                        st += v;
                        if (b.Active) sum[r] += v;
                    }
                    if (b.Active && Math.Abs(st - b.Stored) > 0.05 + st * 1e-4) Violation("store total", $"{b.Def.id}#{b.Id} Stored {b.Stored} but holds {st:F3}");
                    if (b.Active && b.Stored > S.Capacity(b) + 0.5f) Violation("over capacity", $"{b.Def.id}#{b.Id} holds {b.Stored:F1} of {S.Capacity(b):F0}");
                    if (!b.Active && b.State == BuildState.Removed && st > 0.01) Violation("goods in a removed store", $"{b.Def.id}#{b.Id} still holds {st:F1}");
                }
                double ot = 0;
                for (int r = 0; r < S.ResCount; r++)
                {
                    if (!float.IsFinite(b.Out[r]) || b.Out[r] < -eps) Violation("bad stock", $"{b.Def.id}#{b.Id} out {D.Resources[r].id} = {b.Out[r]}");
                    if (!float.IsFinite(b.In[r]) || b.In[r] < -eps) Violation("bad stock", $"{b.Def.id}#{b.Id} in {D.Resources[r].id} = {b.In[r]}");
                    ot += b.Out[r];
                }
                if (b.Alive && Math.Abs(ot - b.OutTotal) > 0.05 + ot * 1e-4) Violation("out total", $"{b.Def.id}#{b.Id} OutTotal {b.OutTotal} but holds {ot:F3}");
                if (!float.IsFinite(b.Work) || b.Work < -eps || b.Work > 1.001f) Violation("bad work", $"{b.Def.id}#{b.Id} work {b.Work}");
                if (b.Delivered != null) foreach (var v in b.Delivered) if (!float.IsFinite(v) || v < -eps) Violation("bad delivered", $"{b.Def.id}#{b.Id} delivered {v}");
                if (b.HaulReserved < -eps || !float.IsFinite(b.HaulReserved)) Violation("bad reservation", $"{b.Def.id}#{b.Id} haul reserved {b.HaulReserved}");
            }
            for (int r = 0; r < S.ResCount; r++)
                if (!float.IsFinite(S.StockTotals[r]) || Math.Abs(S.StockTotals[r] - sum[r]) > 0.05 + sum[r] * 1e-4)
                    Violation("stock totals", $"{D.Resources[r].id}: StockTotals {S.StockTotals[r]} but stores hold {sum[r]:F3}");

            // People.
            var jobCount = new Dictionary<Building, int>();
            var homeCount = new Dictionary<Building, int>();
            foreach (var c in S.Citizens)
            {
                if (c.Gone) { Violation("gone citizen listed", $"{c.Name} is gone but still listed"); continue; }
                string who = $"{c.Name}#{c.Id}";
                if (!float.IsFinite(c.Pos.x) || !float.IsFinite(c.Pos.y) || c.Pos.x < -1 || c.Pos.y < -1 || c.Pos.x > M.W + 1 || c.Pos.y > M.H + 1) Violation("bad position", $"{who} at {c.Pos}");
                if (!float.IsFinite(c.Health) || !float.IsFinite(c.Happiness) || !float.IsFinite(c.Warmth) || c.Health > 1.001f || c.Happiness < -eps || c.Happiness > 1.001f) Violation("bad needs", $"{who} health {c.Health} happy {c.Happiness} warm {c.Warmth}");
                if (c.CarryAmt < -eps || !float.IsFinite(c.CarryAmt) || c.Carrying && c.CarryRes < 0) Violation("bad carry", $"{who} carries {c.CarryAmt} of {c.CarryRes}");
                if (c.Load != null) foreach (var v in c.Load) if (v < -eps || !float.IsFinite(v)) Violation("bad load", $"{who} load {v}");
                if (c.Home != null && !c.Home.Active) Violation("dead reference", $"{who} lives in {c.Home.Def.id}#{c.Home.Id} ({c.Home.State})");
                if (c.Home != null && c.Home.Def.housing <= 0) Violation("dead reference", $"{who} lives in non-home {c.Home.Def.id}");
                if (c.Job != null && (!c.Job.Active || !c.Job.Def.IsWorkplace)) Violation("dead reference", $"{who} works at {c.Job.Def.id}#{c.Job.Id} ({c.Job.State})");
                if (c.Job != null && c.IsBuilder) Violation("two jobs", $"{who} has a job and is a builder");
                if (c.TaskB != null && !c.TaskB.Alive || c.TaskB2 != null && !c.TaskB2.Alive) Violation("dead reference", $"{who} errand {c.Task} points at a removed building");
                if (c.Job != null) jobCount[c.Job] = jobCount.TryGetValue(c.Job, out int j) ? j + 1 : 1;
                if (c.Home != null) homeCount[c.Home] = homeCount.TryGetValue(c.Home, out int h) ? h + 1 : 1;
                if (c.Age < S.Data.Balance.adultAge && (c.Job != null || c.IsBuilder)) Violation("child labour", $"{who} ({c.Age:F1}) has a job");
            }
            foreach (var kv in jobCount) if (kv.Value > kv.Key.Def.maxWorkers) Violation("too many workers", $"{kv.Key.Def.id}#{kv.Key.Id}: {kv.Value} of {kv.Key.Def.maxWorkers}");
            foreach (var kv in homeCount) if (kv.Value > kv.Key.Def.housing * 2 + 2) Violation("overcrowded", $"{kv.Key.Def.id}#{kv.Key.Id}: {kv.Value} in {kv.Key.Def.housing} beds");
            int expected = _startPeople + S.Births + S.Migrants - S.Leavers - S.TotalDeaths;
            if (S.Population != expected) Violation("population count", $"population {S.Population}, expected {expected} (start {_startPeople} + births {S.Births} + migrants {S.Migrants} - leavers {S.Leavers} - deaths {S.TotalDeaths})");

            // Map occupancy and walking prices agree with the buildings.
            var owner = new int[M.W * M.H];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            foreach (var b in S.Buildings)
            {
                if (!b.Alive) continue;
                for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                    for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                    {
                        if (!M.InBounds(x, y)) { Violation("footprint", $"{b.Def.id}#{b.Id} off the map at {x},{y}"); continue; }
                        int c = M.Cell(x, y);
                        if (owner[c] >= 0) Violation("footprint", $"{b.Def.id}#{b.Id} overlaps #{owner[c]} at {x},{y}");
                        owner[c] = b.Id;
                    }
                if (!M.Reserved[b.Door]) Violation("door", $"{b.Def.id}#{b.Id} door {M.XY(b.Door)} not reserved");
                if (owner[b.Door] >= 0 && owner[b.Door] != b.Id) Violation("door", $"{b.Def.id}#{b.Id} door is under #{owner[b.Door]}");
            }
            int paths = 0;
            for (int c = 0; c < owner.Length; c++)
            {
                if (M.BuildingAt[c] != owner[c]) Violation("occupancy", $"cell {M.XY(c)} BuildingAt {M.BuildingAt[c]} but footprint says {owner[c]}");
                if (M.LiveCost[c] != M.CostOf(c)) Violation("walk cost", $"cell {M.XY(c)} live cost {M.LiveCost[c]} but should be {M.CostOf(c)}");
                if (M.Path[c]) { paths++; if (owner[c] >= 0) Violation("path under building", $"cell {M.XY(c)}"); }
                if (M.Reserved[c] && !S.Buildings.Any(b => b.Alive && b.Door == c)) Violation("stale reservation", $"cell {M.XY(c)} reserved for no door");
            }
            if (paths != S.PathCount) Violation("path count", $"PathCount {S.PathCount} but {paths} tiles");

            // Research.
            var seen = new HashSet<string>();
            foreach (var id in S.ResearchQueue)
            {
                var r = D.ResearchById(id);
                if (r == null) { Violation("research queue", $"unknown topic {id} queued"); continue; }
                if (!seen.Add(id)) Violation("research queue", $"{id} queued twice");
                if (S.IsResearched(id)) Violation("research queue", $"{id} queued but already known");
                foreach (var p in r.prereqs) if (!S.IsResearched(p) && !seen.Contains(p)) Violation("research queue", $"{id} queued before its prerequisite {p}");
            }
            if (S.KnowledgeBank < -eps || S.KnowledgeBank > 0.01f && S.ResearchQueue.Count > 0) Violation("knowledge bank", $"{S.KnowledgeBank} banked with {S.ResearchQueue.Count} queued");
            if (S.BuildersWanted < 0) Violation("builders", $"{S.BuildersWanted}");
        }
        int _homelessWithRoom;

        /// <summary>
        /// Every game hour: someone on the same errand (not resting, working, building or idling) at the same spot, or
        /// holding goods, at every sample for 3 days is stuck. (Sampled hourly, not daily: a daily walk home looks the
        /// same at the same hour every day.)
        /// </summary>
        void SampleStuck()
        {
            float now = S.Day + S.DayFraction;
            // Homeless while beds stand free, for a whole day of hourly samples (homes are handed out hourly, and deaths
            // at the turn of the day free beds that the next hour fills).
            int homeless = S.Citizens.Count(c => !c.Gone && c.Home == null);
            int free = 0;
            foreach (var b in S.Buildings) if (b.Active && b.Def.housing > 0) free += Math.Max(0, b.Def.housing - S.Citizens.Count(c => c.Home == b));
            if (homeless > 0 && free > 0) { if (++_homelessWithRoom == 24) Violation("homeless with free beds", $"{homeless} homeless, {free} beds free, for a day"); }
            else _homelessWithRoom = 0;
            foreach (var c in S.Citizens)
            {
                if (c.Gone) continue;
                int task = (int)c.Task;
                bool steady = c.Task == Task.Rest || c.Task == Task.Work || c.Task == Task.Build || c.Task == Task.Idle;
                if (!_still.TryGetValue(c.Id, out var st) || st.task != task || math.distancesq(st.pos, c.Pos) > 0.01f || steady) _still[c.Id] = (task, c.Pos, now);
                else if (now - st.since >= 3) { Violation("stuck citizen", $"{c.Name}#{c.Id} {c.Task} ({c.Activity}) at {c.Pos} since day {st.since:F2}, {WalkInfo(c)}, carrying {c.CarryAmt:F1}, home {c.Home?.Def.id}#{c.Home?.Id} job {c.Job?.Def.id} age {c.Age:F0} sick {c.SickDays}"); _still[c.Id] = (task, c.Pos, now); }
                // Holding goods all the time for days (no store will take them, so the person can do nothing else).
                if (!c.Carrying) { _carry.Remove(c.Id); continue; }
                // (The same good and amount at every sample: a hauler kept busy topping up a granary carries all day too.)
                if (!_carry.TryGetValue(c.Id, out var held) || held.res != c.CarryRes || Math.Abs(held.amt - c.CarryAmt) > 1e-3f) { _carry[c.Id] = (c.CarryRes, c.CarryAmt, now); _trail[c.Id] = new List<string>(); }
                if (_trail.TryGetValue(c.Id, out var tr) && tr.Count < 80) tr.Add($"{now:F2}:{c.Task}@{(int)c.Pos.x},{(int)c.Pos.y}->{(c.Target >= 0 ? M.XY(c.Target).ToString() : "-")}");
                else if (now - held.since >= 3) { float since = held.since; Violation("stuck carrying", $"{c.Name}#{c.Id} has held goods since day {since:F2}, now {c.CarryAmt:F1} {D.Resources[c.CarryRes].id} ({c.Task}, {c.Activity}, at {c.Pos}, {WalkInfo(c)}, to {c.TaskB?.Def.id}#{c.TaskB?.Id} free {(c.TaskB != null && c.TaskB.Store != null ? S.Capacity(c.TaskB) - c.TaskB.Stored : -1):F1} door {(c.TaskB != null ? M.XY(c.TaskB.Door) : default)} flooded {c.TaskB?.Flooded} job {c.Job?.Def.id} builder {c.IsBuilder}); stores with room for it: {S.Buildings.Count(b => b.Active && b.Store != null && !b.Flooded && S.Capacity(b) - b.Stored >= 0.5f && S.Accepts(b, c.CarryRes))}; trail {string.Join(" ", _trail.TryGetValue(c.Id, out var t2) ? t2.Where((x, i) => i % 6 == 0) : Enumerable.Empty<string>())}"); _carry[c.Id] = (c.CarryRes, c.CarryAmt, now); }
            }
        }

        string WalkInfo(Citizen c)
        {
            if (c.Target < 0) return "not walking";
            int cur = M.CellAt(c.Pos);
            int slot = M.Flow.Resolve(c.Target);
            byte code = M.Flow.Fields[slot * M.Flow.Cells + cur];
            return $"target {M.XY(c.Target)} (cost {M.Flow.Cost[c.Target]}/{M.LiveCost[c.Target]}, building {M.BuildingAt[c.Target]}), cell {M.XY(cur)} (cost {M.Flow.Cost[cur]}/{M.LiveCost[cur]}, building {M.BuildingAt[cur]}), flow code {code}, stuck {c.Stuck:F1}";
        }

        // ------------------------------------------------------------------ report

        void WriteReport(int years, double seconds)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Firstlight fuzz test, seed {_seed} ({(_hostileOnly ? "hostile player alone" : $"hostile player ({_chaosRate:P1} of game hours) with the auto-player")})");
            sb.AppendLine();
            sb.AppendLine($"Asked for {years} years; stopped on day {S?.Day} (year {(S?.Year ?? 0) + 1}), population {S?.Population}, age {S?.Age?.name}.");
            sb.AppendLine($"Births {S?.Births}, migrants {S?.Migrants}, leavers {S?.Leavers}, deaths {S?.TotalDeaths}. Saves {_saves}, loads {_loads}, round trips checked {_roundTripsChecked}.");
            sb.AppendLine();
            sb.AppendLine("## Violations");
            sb.AppendLine();
            if (_violationCounts.Count == 0) sb.AppendLine("(none)");
            foreach (var kv in _violationCounts) sb.AppendLine($"- {kv.Key}: {kv.Value}");
            sb.AppendLine();
            foreach (var v in _violations) sb.AppendLine("- " + v.Replace("\n", "\n  "));
            sb.AppendLine();
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (var l in _log) sb.AppendLine("- " + l);
            sb.AppendLine();
            sb.AppendLine("## Actions");
            sb.AppendLine();
            foreach (var kv in _actions) sb.AppendLine($"- {kv.Key}: {kv.Value}");
            sb.AppendLine();
            sb.AppendLine(_violations.Count == 0 ? "**PASS**" : "**FAIL**");
            File.WriteAllText(Path.Combine(_outDir, "fuzz-report.md"), sb.ToString());
        }

        /// <summary>
        /// -autotest -savecheck: damaged saves must give a message, never an exception: an empty file, a foreign file, a
        /// header only, a body cut short at several points, a flipped byte in the body, and a newer format version.
        /// Returns false (and logs why) if any of them throws or loads as if nothing were wrong.
        /// </summary>
        public static bool SaveRobustness(GameData data, GameMap map, int seed, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var s = new Settlement(data, map, seed);
            var ap = new AutoPlayer(s);
            float hour = data.Balance.dayLengthSeconds / 24f;
            while (s.Day < 160) { s.Tick(hour); ap.Think(); }   // into the Agricultural Age: tools held, grain, loads
            string good = Path.Combine(outDir, "good" + SaveGame.Extension);
            SaveGame.Write(good, SaveGame.HeaderFor(s, "good", "test"), s, ap, null, null, null);
            var bytes = File.ReadAllBytes(good);
            int headerLen = 0;
            // The gzip body starts after the header: find it by its magic bytes (1f 8b).
            for (int i = 4; i < bytes.Length - 1; i++) if (bytes[i] == 0x1f && bytes[i + 1] == 0x8b) { headerLen = i; break; }
            var cases = new List<(string name, byte[] data, bool shouldLoad)>
            {
                ("good", bytes, true),
                ("empty", new byte[0], false),
                ("foreign", System.Text.Encoding.ASCII.GetBytes("this is not a save file at all"), false),
                ("header only", bytes.Take(headerLen).ToArray(), false),
                ("cut at 10%", bytes.Take(headerLen + (bytes.Length - headerLen) / 10).ToArray(), false),
                ("cut at 50%", bytes.Take(headerLen + (bytes.Length - headerLen) / 2).ToArray(), false),
                ("cut 40 bytes short", bytes.Take(bytes.Length - 40).ToArray(), false),
                // Only gzip's trailer (CRC and length) missing: every byte of the save is there, so loading it is right.
                ("trailer missing", bytes.Take(bytes.Length - 8).ToArray(), true),
            };
            var flipped = (byte[])bytes.Clone(); flipped[headerLen + (bytes.Length - headerLen) / 3] ^= 0x5a; cases.Add(("flipped byte", flipped, false));
            var newer = (byte[])bytes.Clone(); BitConverter.GetBytes(SaveGame.Version + 98).CopyTo(newer, 4); cases.Add(("newer version", newer, false));
            bool ok = true;
            foreach (var (name, d, shouldLoad) in cases)
            {
                string path = Path.Combine(outDir, name.Replace(' ', '_').Replace("%", "pc") + SaveGame.Extension);
                File.WriteAllBytes(path, d);
                string line;
                try
                {
                    var h = SaveGame.ReadHeader(path, out string herr);
                    var loaded = SaveGame.Read(path, data, out string err);
                    bool loadedOk = loaded != null;
                    loaded?.Map.Dispose();
                    bool right = loadedOk == shouldLoad && (loadedOk || !string.IsNullOrEmpty(err));
                    // A flipped byte inside gzip data is usually caught by the CRC; if it is not, the load must at least not crash.
                    if (name == "flipped byte" && loadedOk) right = true;
                    ok &= right;
                    line = $"{name}: header {(h != null ? "read" : "refused (" + herr + ")")}, load {(loadedOk ? "OK" : "refused (" + err + ")")}{(right ? "" : "  <-- WRONG")}";
                }
                catch (Exception e) { ok = false; line = $"{name}: EXCEPTION {e}"; }
                Debug.Log("[SaveCheck] " + line);
            }
            var list = SaveGame.List();     // the load screen's list over the real save folder must not throw either

            // Other map settings: the land would be generated differently, so the save must be refused with a message.
            int copper = data.Balance.copperDeposits;
            try
            {
                data.Balance.copperDeposits = copper + 1;
                var other = SaveGame.Read(good, data, out string merr);
                bool right = other == null && !string.IsNullOrEmpty(merr);
                other?.Map.Dispose();
                ok &= right;
                Debug.Log($"[SaveCheck] other map settings: load {(other != null ? "OK" : "refused (" + merr + ")")}{(right ? "" : "  <-- WRONG")}");
            }
            catch (Exception e) { ok = false; Debug.Log($"[SaveCheck] other map settings: EXCEPTION {e}"); }
            finally { data.Balance.copperDeposits = copper; }

            ok &= OverwriteCheck(data, s, ap, outDir);
            ok &= GoodsByIdCheck(data, good, s);
            ok &= UnknownIdsCheck(data, good, s, outDir);
            foreach (var file in ArgsAfter("-compatsave")) ok &= CompatCheck(data, file, int.TryParse(ArgsAfter("-compatdays").FirstOrDefault(), out int cd) ? cd : 30, outDir);
            Debug.Log($"[SaveCheck] {(ok ? "PASS" : "FAIL")}");
            return ok;
        }

        /// <summary>
        /// A save naming research and a law this build does not have (the ids are renamed inside the gzip body, every
        /// place they are stored): it must load, drop them, and run on for a day without an exception.
        /// </summary>
        static bool UnknownIdsCheck(GameData data, string file, Settlement orig, string outDir)
        {
            // A topic whose id is not also a good's or a building's (those strings are saved too).
            bool Plain(string id) => data.Resources.All(x => x.id != id) && data.Buildings.All(x => x.id != id);
            string topic = orig.ResearchQueue.FirstOrDefault(Plain) ?? data.Research.First(x => !x.known && orig.IsResearched(x.id) && Plain(x.id)).id;
            string law = data.Laws.Count > 0 ? data.Laws[0].id : null;
            var bytes = File.ReadAllBytes(file);
            int headerLen = 0;
            for (int i = 4; i < bytes.Length - 1; i++) if (bytes[i] == 0x1f && bytes[i + 1] == 0x8b) { headerLen = i; break; }
            byte[] body;
            using (var gz = new System.IO.Compression.GZipStream(new MemoryStream(bytes, headerLen, bytes.Length - headerLen), System.IO.Compression.CompressionMode.Decompress))
            using (var ms = new MemoryStream()) { gz.CopyTo(ms); body = ms.ToArray(); }
            int renamed = 0;
            void Rename(string id)
            {
                // A saved string is a 'present' flag (1), a length byte and the UTF-8 bytes: match all of it, so ids that
                // merely contain this one are left alone.
                var pat = new byte[] { 1, (byte)id.Length }.Concat(System.Text.Encoding.UTF8.GetBytes(id)).ToArray();
                for (int i = 0; i + pat.Length <= body.Length; i++)
                {
                    int k = 0; while (k < pat.Length && body[i + k] == pat[k]) k++;
                    if (k < pat.Length) continue;
                    for (int j = 2; j < pat.Length; j++) body[i + j] = (byte)'z';
                    renamed++;
                }
            }
            Rename(topic);
            if (law != null) Rename(law);
            string path = Path.Combine(outDir, "unknown_ids" + SaveGame.Extension);
            using (var fs = File.Create(path))
            {
                fs.Write(bytes, 0, headerLen);
                using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest);
                gz.Write(body, 0, body.Length);
            }
            SaveGame.Loaded l = null;
            try
            {
                l = SaveGame.Read(path, data, out string err);
                bool ok = l != null;
                string detail = l == null ? "load refused (" + err + ")" : "";
                if (l != null)
                {
                    var s = l.Sim; var ap = l.Auto ?? new AutoPlayer(s);
                    bool clean = s.ResearchQueue.All(x => data.ResearchById(x) != null) && s.ActiveLaws.All(x => data.Laws.Any(d => d.id == x));
                    float hour = data.Balance.dayLengthSeconds / 24f;
                    int day0 = s.Day;
                    while (s.Day < day0 + 2) { s.Tick(hour); ap.Think(); }
                    ok = clean;
                    detail = $"loaded, ids dropped: {(clean ? "yes" : "NO")}, ran to day {s.Day}";
                }
                Debug.Log($"[SaveCheck] unknown research '{topic}' and law '{law}' ({renamed} places renamed): {detail}{(ok ? "" : "  <-- WRONG")}");
                return ok && renamed > 0;
            }
            catch (Exception e) { Debug.Log($"[SaveCheck] unknown research/law: EXCEPTION {e}"); return false; }
            finally { l?.Map.Dispose(); }
        }

        /// <summary>
        /// Overwriting a save: the new one replaces the old; if the old file is held open (a scanner or sync client) the
        /// save fails with an exception (the caller's toast) and the old save is still there and loads; listing the folder
        /// then puts a lone backup back under its name and deletes leftover temporary files.
        /// </summary>
        static bool OverwriteCheck(GameData data, Settlement s, AutoPlayer ap, string outDir)
        {
            string dir = Path.Combine(outDir, "overwrite");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "slot" + SaveGame.Extension);
            var notes = new List<string>();
            bool ok = true;
            try
            {
                SaveGame.Write(path, SaveGame.HeaderFor(s, "first", "test"), s, ap, null, null, null);
                SaveGame.Write(path, SaveGame.HeaderFor(s, "second", "test"), s, ap, null, null, null);
                var h = SaveGame.ReadHeader(path, out _);
                bool replaced = h?.Name == "second" && !File.Exists(path + ".tmp") && !File.Exists(path + ".bak");
                ok &= replaced; notes.Add($"overwrite {(replaced ? "replaced, no leftovers" : "WRONG")}");
                bool threw = false;
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))   // held open: no delete, no rename
                {
                    try { SaveGame.Write(path, SaveGame.HeaderFor(s, "third", "test"), s, ap, null, null, null); }
                    catch (Exception e) { threw = true; notes.Add($"held file: save failed ({e.GetType().Name})"); }
                }
                SaveGame.CleanStale(dir);
                var kept = SaveGame.ReadHeader(path, out _);
                var l = SaveGame.Read(path, data, out string err);
                bool intact = threw && kept?.Name == "second" && l != null && !File.Exists(path + ".tmp");
                l?.Map.Dispose();
                ok &= intact; notes.Add($"old save {(intact ? "intact and loads, .tmp cleaned" : "WRONG (" + (kept?.Name ?? err) + ")")}");
                // A replace that stopped half way: only the backup is left.
                File.Move(path, path + ".bak");
                File.WriteAllText(path + ".tmp", "partial");
                SaveGame.CleanStale(dir);
                bool restored = SaveGame.ReadHeader(path, out _)?.Name == "second" && !File.Exists(path + ".bak") && !File.Exists(path + ".tmp");
                ok &= restored; notes.Add($"lone backup {(restored ? "put back" : "WRONG")}");
            }
            catch (Exception e) { ok = false; notes.Add("EXCEPTION " + e); }
            Debug.Log($"[SaveCheck] overwrite: {string.Join("; ", notes)}{(ok ? "" : "  <-- WRONG")}");
            return ok;
        }

        static IEnumerable<string> ArgsAfter(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) yield return args[i + 1];
        }

        /// <summary>
        /// Goods are matched by id: loads <paramref name="file"/> (written from <paramref name="orig"/>) as if the save's goods
        /// list were in reverse order, held a good this build does not have ("obsolete_good", in the place of the good with the
        /// most stock) and lacked that good. Every per-good amount and every saved good index must land on the right good by
        /// id; the missing good must come back as 0 and the unknown good be dropped.
        /// </summary>
        static bool GoodsByIdCheck(GameData data, string file, Settlement orig)
        {
            var cur = data.Resources.Select(x => x.id).ToList();
            var claimed = Enumerable.Reverse(cur).ToList();
            int most = 0; for (int i = 1; i < cur.Count; i++) if (orig.StockTotals[i] > orig.StockTotals[most]) most = i;
            string gone = cur[most];
            claimed[claimed.IndexOf(gone)] = "obsolete_good";
            // What the save calls good i is the build's good at index Map(i) (-1 = dropped).
            int To(int i) => i < 0 ? -1 : claimed[i] == "obsolete_good" ? -1 : cur.IndexOf(claimed[i]);
            // Position i of the file holds the original good cur[i]; the loader puts it on claimed[i].
            SaveGame.Loaded l = null;
            var bad = new List<string>();
            try
            {
                SaveGame.TestSavedGoods = _ => claimed;
                l = SaveGame.Read(file, data, out string err);
                if (l == null) { Debug.Log($"[SaveCheck] goods by id: load refused ({err})  <-- WRONG"); return false; }
                var s = l.Sim;
                bool Near(float a, float b) => Math.Abs(a - b) <= 1e-3f + Math.Abs(b) * 1e-5f;
                for (int i = 0; i < cur.Count; i++)
                {
                    int j = To(i);
                    if (j < 0) continue;
                    if (!Near(s.StockTotals[j], orig.StockTotals[i])) bad.Add($"stock {cur[j]} {s.StockTotals[j]} (want {orig.StockTotals[i]})");
                    if (!Near(s.Produced[j], orig.Produced[i])) bad.Add($"produced {cur[j]} {s.Produced[j]} (want {orig.Produced[i]})");
                    for (int b = 0; b < s.Buildings.Count; b++)
                    {
                        var nb = s.Buildings[b]; var ob = orig.Buildings[b];
                        if (ob.Store != null && !Near(nb.Store[j], ob.Store[i])) bad.Add($"{ob.Def.id}#{b} store {cur[j]}");
                        if (!Near(nb.Out[j], ob.Out[i]) || !Near(nb.In[j], ob.In[i])) bad.Add($"{ob.Def.id}#{b} out/in {cur[j]}");
                    }
                }
                if (s.StockTotals[most] != 0 || s.Buildings.Any(b => b.Store != null && b.Store[most] != 0)) bad.Add($"{gone} (not in the save) is {s.StockTotals[most]}, not 0");
                for (int k = 0; k < s.Buildings.Count; k++)
                    if (s.Buildings[k].Store != null && Math.Abs(s.Buildings[k].Stored - s.Buildings[k].Store.Sum()) > 1e-2f) bad.Add($"#{k} Stored not recounted");
                int tools = 0;
                for (int c = 0; c < s.Citizens.Count; c++)
                {
                    var nc = s.Citizens[c]; var oc = orig.Citizens[c];
                    if (nc.Tool != To(oc.Tool) || nc.CarryRes != To(oc.CarryRes)) bad.Add($"{oc.Name}: tool {nc.Tool}/{To(oc.Tool)} carry {nc.CarryRes}/{To(oc.CarryRes)}");
                    if (oc.Load != null) for (int i = 0; i < cur.Count; i++) if (To(i) >= 0 && !Near(nc.Load[To(i)], oc.Load[i])) bad.Add($"{oc.Name}: load {cur[To(i)]}");
                    if (oc.Tool >= 0) tools++;
                }
                Debug.Log($"[SaveCheck] goods by id (reversed order, an unknown good dropped, {gone} missing): {cur.Count} goods, {s.Buildings.Count} buildings, {s.Citizens.Count} people ({tools} with tools, "
                          + $"{orig.Citizens.Count(c => c.Load != null && c.LoadTotal > 0)} with a field load, {orig.Citizens.Count(c => c.Carrying)} carrying), {gone} in the original {orig.StockTotals[most]:F1}: {(bad.Count == 0 ? "right" : "WRONG: " + string.Join("; ", bad.Take(8)))}");
                return bad.Count == 0;
            }
            catch (Exception e) { Debug.Log($"[SaveCheck] goods by id: EXCEPTION {e}"); return false; }
            finally { SaveGame.TestSavedGoods = null; l?.Map.Dispose(); }
        }

        /// <summary>
        /// -savecheck -compatsave FILE [-compatdays N]: a save from an older build (format 1) loads, plays on for N days
        /// with the auto-player without an error, and the game saved again in today's format loads back the same.
        /// </summary>
        static bool CompatCheck(GameData data, string file, int days, string outDir)
        {
            var errors = new List<string>();
            void OnLog(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add($"{type}: {msg}");
            }
            SaveGame.Loaded l = null, again = null;
            string line;
            Application.logMessageReceived += OnLog;
            try
            {
                var h = SaveGame.ReadHeader(file, out string herr);
                l = SaveGame.Read(file, data, out string err);
                if (l == null) line = $"load refused ({err ?? herr})";
                else
                {
                    var s = l.Sim; var ap = l.Auto ?? new AutoPlayer(s);
                    int day0 = s.Day, pop0 = s.Population;
                    float hour = data.Balance.dayLengthSeconds / 24f;
                    while (s.Day < day0 + days && s.Population > 0) { s.Tick(hour); ap.Think(); }
                    string resaved = Path.Combine(outDir, "compat-" + Path.GetFileNameWithoutExtension(file) + SaveGame.Extension);
                    // Timed: five writes of this game (the save's cost on the main thread, for docs/pass2/perf.md).
                    var times = new List<double>(); int gc0 = GC.CollectionCount(0);
                    for (int i = 0; i < 5; i++)
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        SaveGame.Write(resaved, SaveGame.HeaderFor(s, "compat", "test"), s, ap, null, null, null);
                        times.Add(sw.Elapsed.TotalMilliseconds);
                    }
                    Debug.Log($"[SaveTime] {Path.GetFileName(file)} (day {s.Day}, {s.Population} people, {s.Buildings.Count} buildings): save {times.Min():F1} ms min, {times.Average():F1} ms mean of 5 "
                              + $"({string.Join(" ", times.Select(t => t.ToString("F1")))}), {new FileInfo(resaved).Length / 1024.0:F0} KB, {(GC.CollectionCount(0) - gc0) / 5.0:F1} collections per save");
                    again = SaveGame.Read(resaved, data, out string err2);
                    bool same = again != null && Digest(again.Map, again.Sim, again.Auto).SequenceEqual(Digest(l.Map, s, ap));
                    line = $"format {h?.Version}, day {day0} pop {pop0} -> day {s.Day} pop {s.Population}, {s.Buildings.Count(b => b.Active)} buildings, {s.ResearchLog.Count} topics, age {s.Age.name}; "
                         + $"saved again as format {SaveGame.Version}: {(again == null ? "load refused (" + err2 + ")" : same ? "loads the same" : "DIFFERENT after load")}";
                    if (!same) errors.Add("re-saved game did not load back the same");
                }
            }
            catch (Exception e) { line = "EXCEPTION " + e; errors.Add(e.Message); }
            finally { Application.logMessageReceived -= OnLog; l?.Map.Dispose(); again?.Map.Dispose(); }
            bool ok = l != null && errors.Count == 0;
            Debug.Log($"[SaveCheck] compat {Path.GetFileName(file)}: {(ok ? "PASS" : "FAIL")}: {line}{(errors.Count > 0 ? " | " + string.Join(" | ", errors.Take(5)) : "")}");
            return ok;
        }

        static byte[] Digest(GameMap m, Settlement s, AutoPlayer a)
        {
            var ms = new MemoryStream();
            var w = new SaveWriter(ms);
            m.Write(w); s.Write(w); w.Bool(a != null); a?.Write(w);
            w.W.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// -autotest -sitecheck -seed N: on this seed's fresh map (campfire at the suggested site, everything unlocked),
        /// how many places each building could go (any turn), and how many of them within 30 cells of the campfire.
        /// A building with nowhere to go on a map is a dead end for that map.
        /// </summary>
        public static bool SiteCheck(GameData data, GameMap map, int seed)
        {
            var s = new Settlement(data, map, seed);
            var tc = data.Buildings.First(b => b.townCenter);
            s.Place(tc, s.SuggestCampfireSite(), out _);
            s.DebugUnlockAll();
            bool ok = true;
            var sb = new StringBuilder();
            foreach (var d in data.Buildings)
            {
                if (d.townCenter || d.path) continue;
                int any = 0, near = 0;
                for (int y = 0; y < map.H; y++)
                    for (int x = 0; x < map.W; x++)
                        for (int rot = 0; rot < 4; rot++)
                        {
                            if (!s.CanPlace(d, new int2(x, y), rot, out _)) continue;
                            any++;
                            if (math.distance(Settlement.FootprintCenter(d, new int2(x, y), rot), s.TownCenter.Center) <= 30) near++;
                            break;
                        }
                if (any == 0) ok = false;
                sb.Append($"{d.id} {any}/{near}{(any == 0 ? " <-- NOWHERE" : near == 0 ? " <-- none near" : "")}, ");
            }
            Debug.Log($"[SiteCheck] seed {seed}: {(ok ? "PASS" : "FAIL")}: {sb}");
            return ok;
        }
    }
}
