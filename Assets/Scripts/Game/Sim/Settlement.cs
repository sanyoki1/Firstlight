using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Firstlight.Game
{
    /// <summary>
    /// The settlement simulation: calendar, buildings, goods, citizens, needs, population, research and ages.
    /// Plain data and logic, no GameObjects: the view reads it, the HUD and the auto-player call the same
    /// commands. Deterministic for a seed; advanced in fixed steps by <see cref="Tick"/>, so it can be
    /// fast-forwarded with nothing drawn. Content comes from GameData; only structural ids (the town center
    /// flag, and the fuel/clothing/herbs ids in balance.json) are known to the code.
    ///
    /// Split over partial files: this one (time, commands, read API, research, ages, events), Buildings
    /// (placement, construction, storage, production) and People (citizens, jobs, needs, population).
    /// </summary>
    public sealed partial class Settlement
    {
        /// <summary>Fixed sim step in game seconds.</summary>
        public const float Step = 0.25f;
        /// <summary>A site counts as supplied when less than this is missing of any material.</summary>
        public const float MaterialTolerance = 0.01f;

        public readonly GameData Data;
        public readonly GameMap Map;
        readonly Balance B;
        readonly SimRandom _rng;
        public readonly int Seed;

        public readonly List<Building> Buildings = new();
        public readonly List<Citizen> Citizens = new();
        public readonly List<SimEvent> Events = new();
        public readonly List<string> Warnings = new();
        readonly Dictionary<string, int> _warnSeen = new();   // warning key -> last day it was showing

        // ------------------------------------------------------------------ calendar
        public float Time { get; private set; }            // game seconds since the campfire was lit
        public int Day { get; private set; }
        public float DayFraction { get; private set; } = 0.3f;   // the band arrives in the morning
        public int DaysPerYear => B.daysPerSeason * 4;
        public int Season => (Day / B.daysPerSeason) % 4;
        public string SeasonName => B.seasonNames[Season];
        public int DayOfSeason => Day % B.daysPerSeason;
        public int Year => Day / DaysPerYear;
        public float Temperature => B.seasonTemperature[Season] + (Season == 3 ? Diff.winterTemperature : 0f);
        public int WintersSurvived { get; private set; }
        public bool IsWorkTime => DayFraction >= B.workdayStart && DayFraction < B.workdayEnd;
        public float WorkdaySeconds => (B.workdayEnd - B.workdayStart) * B.dayLengthSeconds;

        /// <summary>Game speed multiplier used by <see cref="Update"/> (0 = paused, 1, 2, 4).</summary>
        public float Speed { get; private set; } = 1f;

        public Building TownCenter { get; private set; }
        public bool NeedsCampfire => TownCenter == null;
        public int BuildersWanted { get; private set; }

        // ------------------------------------------------------------------ counters (for HUD and the auto-test)
        public int Births, Migrants, Leavers;
        public readonly SortedDictionary<string, int> Deaths = new();
        public int TotalDeaths => Deaths.Values.Sum();
        /// <summary>Knowledge earned since the start (a double: it is added to in small amounts for decades).</summary>
        public double KnowledgeEarned { get; private set; }
        /// <summary><see cref="KnowledgeEarned"/> as a float, for the HUD.</summary>
        public float KnowledgeTotal => (float)KnowledgeEarned;

        // ------------------------------------------------------------------ resources
        public readonly int ResCount;
        readonly Dictionary<string, int> _resIndex = new();
        readonly int[] _foodByPerish;        // food resources, most perishable first (eaten first)
        readonly int[] _toolsBest;           // tool resources, best first
        readonly int _fuel, _clothing, _herbs, _goods, _grain;
        /// <summary>
        /// Grain made since the start, added up in a float exactly as <see cref="Produced"/> was before it became a double.
        /// The auto-player sizes its fields from this harvest measure; keeping its float arithmetic keeps every auto-test
        /// game the same. Not for reports (use Produced).
        /// </summary>
        public float GrainProducedF { get; private set; }
        public readonly float[] StockTotals; // sum over all storage buildings, refreshed each step
        // Cumulative counters are doubles: they are fed tiny per-step amounts for decades (floats stopped adding them up
        // exactly past ~2^15). Reports only: no decision of the sim reads them.
        public readonly double[] Produced;   // made by workplaces since the start (per resource)
        public readonly double[] Consumed;   // used up since the start: eaten, burned, worn out, used by workplaces, built in
        public readonly double[] Lost;       // gone unused since the start: spoiled, soaked, stolen, dropped, no room, taken by leavers and the dead
        public readonly double[] Traded;     // net from traders since the start (received minus given)
        public double WorkerSeconds, ProductiveSeconds;  // workers' time on shift vs time actually producing

        // ------------------------------------------------------------------ research and ages
        readonly HashSet<string> _researched = new();
        readonly Dictionary<string, float> _progress = new();
        public readonly List<string> ResearchQueue = new();
        readonly Dictionary<string, float> _effects = new();
        public float KnowledgeBank { get; private set; }  // knowledge earned while nothing was queued
        public AgeDef Age { get; private set; }
        public readonly List<MilestoneState> Milestones = new();
        public readonly List<(string id, int day)> ResearchLog = new();
        public readonly List<(string age, int day)> AgeLog = new();

        float _acc, _hourAcc;
        int _nextId;
        bool _jobsDirty = true;

        public Settlement(GameData data, GameMap map, int seed)
        {
            Data = data; Map = map; B = data.Balance; Seed = seed;
            _rng = new SimRandom(seed ^ 0x5e771e);
            ResCount = data.Resources.Count;
            for (int i = 0; i < ResCount; i++) _resIndex[data.Resources[i].id] = i;
            StockTotals = new float[ResCount];
            Produced = new double[ResCount];
            Consumed = new double[ResCount];
            Lost = new double[ResCount];
            Traded = new double[ResCount];
            _foodByPerish = Enumerable.Range(0, ResCount).Where(i => data.Resources[i].isFood)
                .OrderByDescending(i => data.Resources[i].spoilPerDay).ThenBy(i => i).ToArray();
            _toolsBest = Enumerable.Range(0, ResCount).Where(i => data.Resources[i].toolSpeed > 0)
                .OrderByDescending(i => data.Resources[i].toolSpeed).ThenBy(i => i).ToArray();
            _fuel = ResIndex(B.fuelResource); _clothing = ResIndex(B.clothingResource); _herbs = ResIndex(B.herbsResource); _goods = ResIndex(B.goodsResource);
            _grain = ResIndex("grain");
            BuildersWanted = B.defaultBuilders;

            foreach (var r in data.Research) if (r.known) _researched.Add(r.id);
            RebuildEffects();
            SetAge(data.Ages.Count > 0 ? data.Ages[0] : null);
        }

        // ------------------------------------------------------------------ lookups
        public int ResIndex(string id) => id != null && _resIndex.TryGetValue(id, out int i) ? i : -1;
        public ResourceDef Res(int i) => Data.Resources[i];
        public float Stock(string id) { int i = ResIndex(id); return i < 0 ? 0 : StockTotals[i]; }
        public float FoodStock { get { float f = 0; foreach (int r in _foodByPerish) f += StockTotals[r]; return f; } }
        public int ToolStock { get { float t = 0; foreach (int r in _toolsBest) t += StockTotals[r]; return (int)t; } }
        public int Population { get { int n = 0; foreach (var c in Citizens) if (!c.Gone) n++; return n; } }
        public int Adults { get { int n = 0; foreach (var c in Citizens) if (!c.Gone && c.Age >= B.adultAge) n++; return n; } }
        public int Children => Population - Adults;
        public float DailyFoodNeed { get { float f = 0; foreach (var c in Citizens) if (!c.Gone) f += c.Age >= B.adultAge ? B.foodPerAdult : B.foodPerChild; return f; } }
        public float FoodDays => DailyFoodNeed <= 0 ? 99f : FoodStock / DailyFoodNeed;
        public float AverageHappiness { get { float h = 0; int n = 0; foreach (var c in Citizens) if (!c.Gone) { h += c.Happiness; n++; } return n == 0 ? 0 : h / n; } }
        public int HousingCapacity { get { int n = 0; foreach (var b in Buildings) if (b.Active) n += b.Def.housing; return n; } }
        // The dead and the leavers stay in Citizens (Gone, with no home) until the end of the step; they aren't homeless.
        public int Homeless { get { int n = 0; foreach (var c in Citizens) if (!c.Gone && c.Home == null) n++; return n; } }
        public int StorageCapacity { get { float n = 0; foreach (var b in Buildings) if (b.Active && b.IsStorage) n += Capacity(b); return (int)n; } }
        public float StorageUsed { get { float n = 0; foreach (var b in Buildings) if (b.Active && b.IsStorage) n += b.Stored; return n; } }
        public int CountBuildings(string id, bool includeSites = false)
        {
            int n = 0;
            foreach (var b in Buildings) if (b.Def.id == id && (b.Active || includeSites && b.IsSite) || includeSites && b.UpgradeTo != null && b.UpgradeTo.id == id) n++;
            return n;
        }
        /// <summary>Everything builders are working on: sites and upgrades.</summary>
        public IEnumerable<Building> Sites => Buildings.Where(b => b.UnderWork);

        /// <summary>Product of all researched effects on a stat ("yield.hunting", "spoil.food", ...); 1 if none.</summary>
        public float Effect(string stat) => _effects.TryGetValue(stat, out float v) ? v : 1f;

        // ------------------------------------------------------------------ time

        /// <summary>Advances by real seconds times the game speed (what a frame calls).</summary>
        public void Update(float realSeconds) => Tick(realSeconds * Speed);

        /// <summary>Advances the simulation by game seconds in fixed steps. Nothing happens until the campfire is placed.</summary>
        public void Tick(float gameSeconds)
        {
            if (NeedsCampfire || gameSeconds <= 0) return;
            _acc += gameSeconds;
            while (_acc >= Step)
            {
                _acc -= Step;
                foreach (var c in Citizens) c.PrevPos = c.Pos;
                foreach (var c in Departing) c.PrevPos = c.Pos;
                StepOnce();
            }
        }

        /// <summary>How far the clock is into the next fixed step (0..1). The view draws walkers between PrevPos and
        /// Pos by this, so they glide instead of hopping 4 times a game second (what made walking choppy at 1x).</summary>
        public float StepAlpha => math.saturate(_acc / Step);

        /// <summary>Where to draw a citizen this frame: between its last two step positions. A new person (no last
        /// position yet) or one that jumped further than a step's walk is drawn where it is.</summary>
        public float2 DrawPos(Citizen c)
        {
            if (math.distancesq(c.PrevPos, c.Pos) > 2.25f) return c.Pos;
            return math.lerp(c.PrevPos, c.Pos, StepAlpha);
        }

        /// <summary>Perf probe only: when set, time spent per part of the step is added up (ms).</summary>
        public bool Profile;
        public double ProfCitizens, ProfHourly, ProfDaily, ProfJobs;
        readonly System.Diagnostics.Stopwatch _prof = new();

        void StepOnce()
        {
            if (Profile) _prof.Restart();
            Time += Step;
            DayFraction += Step / B.dayLengthSeconds;
            if (DayFraction >= 1f)
            {
                DayFraction -= 1f;
                Day++;
                NewDay();
                if (Profile) { ProfDaily += _prof.Elapsed.TotalMilliseconds; _prof.Restart(); }
            }
            _hourAcc += Step;
            if (_hourAcc >= B.dayLengthSeconds / 24f)
            {
                _hourAcc -= B.dayLengthSeconds / 24f;
                Hourly();
                if (Profile) { ProfHourly += _prof.Elapsed.TotalMilliseconds; _prof.Restart(); }
            }
            if (_jobsDirty) { AssignJobs(); if (Profile) { ProfJobs += _prof.Elapsed.TotalMilliseconds; _prof.Restart(); } }
            bool work = IsWorkTime;
            // People with a job set off a little before the shift, so the walk out isn't all taken from the workday.
            bool early = !work && DayFraction >= B.workdayStart - B.commuteLead && DayFraction < B.workdayStart;
            for (int i = 0; i < Citizens.Count; i++)
            {
                var c = Citizens[i];
                UpdateCitizen(c, work || early && c.Job != null && !c.Sick);
            }
            UpdateDeparting();
            for (int i = Citizens.Count - 1; i >= 0; i--) if (Citizens[i].Gone) Citizens.RemoveAt(i);
            if (Profile) ProfCitizens += _prof.Elapsed.TotalMilliseconds;
        }

        void Hourly()
        {
            if (Map.CommitCostChanges()) PrefetchPaths();
            RecountReservations();
            RefreshFullness();
            AssignHomes();
            AssignJobs();
            RefreshStock();
            RefreshHomes();
            RefreshOutbreakRisk();
            EvaluateMilestones();
            RefreshWarnings();
        }

        void NewDay()
        {
            if (DayOfSeason == 0)
            {
                if (Season == 0 && Day > 0 && Population > 0)
                {
                    WintersSurvived++;
                    Log(Severity.Info, $"The settlement came through winter {WintersSurvived} with {Population} people.");
                }
                Log(Severity.Info, $"{SeasonName} of year {Year + 1} begins.");
            }
            RefreshStock();
            DailyCrops();
            DailyFlood();
            DailyGoods();
            DailyNeeds();
            DailySpoilage();
            DailyPopulation();
            DailyUnrest();
            DailyOutbreak();
            DailyTradeExpiry();
            TraderVisit();
            Map.DailyRegrowth(_rng, B);
            if (DayOfSeason == 0 && Map.CommitSoftCostChanges()) PrefetchPaths();
            RefreshStock();
            EvaluateMilestones();
            RefreshWarnings();
        }

        // ------------------------------------------------------------------ player commands
        // The HUD and the auto-player call exactly these.

        public void SetSpeed(float speed) => Speed = math.max(0f, speed);

        /// <summary>How many people are kept on construction (taken before normal-priority jobs).</summary>
        public void SetBuilders(int count) { BuildersWanted = math.clamp(count, 0, 50); _jobsDirty = true; }

        public void SetPriority(Building b, Priority p) { if (b != null && b.Alive) { b.Priority = p; _jobsDirty = true; } }

        public void SetPaused(Building b, bool paused) { if (b != null && b.Alive) { b.Paused = paused; _jobsDirty = true; } }

        /// <summary>For workplaces with several outputs: make only output <paramref name="outputIndex"/> (-1 = the normal mix).</summary>
        public void SetFocus(Building b, int outputIndex)
        {
            if (b == null || !b.Alive || b.Def.outputs.Length < 2) return;
            if (outputIndex >= 0 && outputIndex < b.Def.outputs.Length && !ResourceUnlocked(b.Def.outputs[outputIndex].id)) return;
            b.Focus = outputIndex >= 0 && outputIndex < b.Def.outputs.Length ? outputIndex : b.Def.exclusiveOutputs ? 0 : -1;
        }

        // Place / Demolish / CanPlace are in Settlement.Buildings.cs.

        /// <summary>Queues a research topic, queueing any missing prerequisites ahead of it.</summary>
        public bool QueueResearch(string id, out string reason)
        {
            var r = Data.ResearchById(id);
            if (r == null) { reason = "Unknown research"; return false; }
            if (_researched.Contains(id)) { reason = "Already known"; return false; }
            if (ResearchQueue.Contains(id)) { reason = "Already queued"; return false; }
            if (Data.AgeOrder(r.age) > Age.order) { reason = $"Needs the {Data.Age(r.age)?.name ?? r.age}"; return false; }
            foreach (var p in r.prereqs)
                if (!_researched.Contains(p) && !ResearchQueue.Contains(p) && !QueueResearch(p, out reason)) return false;
            ResearchQueue.Add(id);
            reason = "";
            SpendBank();
            return true;
        }
        public bool QueueResearch(string id) => QueueResearch(id, out _);

        /// <summary>Removes a topic from the queue, along with anything queued that depends on it. Progress is kept.</summary>
        public void DequeueResearch(string id)
        {
            if (!ResearchQueue.Remove(id)) return;
            for (int i = ResearchQueue.Count - 1; i >= 0; i--)
                if (DependsOn(ResearchQueue[i], id)) ResearchQueue.RemoveAt(i);
        }

        bool DependsOn(string id, string on)
        {
            var r = Data.ResearchById(id);
            if (r == null) return false;
            foreach (var p in r.prereqs) if (p == on || DependsOn(p, on)) return true;
            return false;
        }

        public bool CanAdvanceAge(out string reason)
        {
            reason = "";
            if (Age == null || string.IsNullOrEmpty(Age.next) || Data.Age(Age.next) == null) { reason = "No later age"; return false; }
            var missing = Age.keyResearch.Where(k => !_researched.Contains(k)).Select(k => Data.ResearchById(k)?.name ?? k).ToList();
            var todo = Milestones.Where(m => !m.Done).Select(m => m.Label).ToList();
            if (missing.Count == 0 && todo.Count == 0) return true;
            var parts = new List<string>();
            if (missing.Count > 0) parts.Add("research " + string.Join(", ", missing));
            if (todo.Count > 0) parts.Add(string.Join(", ", todo));
            reason = "Still needed: " + string.Join("; ", parts);
            return false;
        }
        public bool CanAdvanceAge() => CanAdvanceAge(out _);

        public bool AdvanceAge()
        {
            if (!CanAdvanceAge(out _)) return false;
            var next = Data.Age(Age.next);
            SetAge(next);
            AgeLog.Add((next.id, Day));
            Log(Severity.Info, $"The settlement enters the {next.name}.");
            return true;
        }

        // ------------------------------------------------------------------ research

        public bool IsResearched(string id) => _researched.Contains(id);

        /// <summary>A resource with an unlockedBy (a field crop) can only be made once that research is known.</summary>
        public bool ResourceUnlocked(string id) { var r = Data.Resource(id); return r == null || string.IsNullOrEmpty(r.unlockedBy) || _researched.Contains(r.unlockedBy); }
        public float ResearchProgress(string id) => _researched.Contains(id) ? 1f :
            _progress.TryGetValue(id, out float p) ? p / math.max(1f, Data.ResearchById(id)?.cost ?? 1f) : 0f;
        public string CurrentResearch => ResearchQueue.Count > 0 ? ResearchQueue[0] : null;

        /// <summary>Topics whose prerequisites are known and whose age has been reached (not yet researched).</summary>
        public IEnumerable<ResearchDef> AvailableResearch =>
            Data.Research.Where(r => !_researched.Contains(r.id) && Data.AgeOrder(r.age) <= Age.order && r.prereqs.All(_researched.Contains));

        /// <summary>A building can be placed once its research is known and its age has been reached.</summary>
        public bool IsUnlocked(BuildingDef d) =>
            (string.IsNullOrEmpty(d.unlockedBy) || _researched.Contains(d.unlockedBy)) && Data.AgeOrder(d.age) <= Age.order;

        public IEnumerable<BuildingDef> UnlockedBuildings => Data.Buildings.Where(d => !d.townCenter && IsUnlocked(d));

        void AddKnowledge(float k)
        {
            KnowledgeEarned += k;
            KnowledgeBank += k;
            SpendBank();
        }

        void SpendBank()
        {
            while (KnowledgeBank > 0 && ResearchQueue.Count > 0)
            {
                string id = ResearchQueue[0];
                var r = Data.ResearchById(id);
                _progress.TryGetValue(id, out float p);
                float need = r.cost - p;
                if (KnowledgeBank < need) { _progress[id] = p + KnowledgeBank; KnowledgeBank = 0; break; }
                KnowledgeBank -= need;
                ResearchQueue.RemoveAt(0);
                CompleteResearch(r);
            }
        }

        void CompleteResearch(ResearchDef r)
        {
            _researched.Add(r.id);
            _progress[r.id] = r.cost;
            ResearchLog.Add((r.id, Day));
            RebuildEffects();
            var unlocks = Data.Buildings.Where(b => b.unlockedBy == r.id).Select(b => b.name).ToList();
            Log(Severity.Info, $"Research finished: {r.name}." + (unlocks.Count > 0 ? $" Unlocks {string.Join(", ", unlocks)}." : ""));
            _jobsDirty = true;
        }

        void RebuildEffects()
        {
            _effects.Clear();
            foreach (var r in Data.Research)
            {
                if (!_researched.Contains(r.id)) continue;
                foreach (var e in r.effects)
                    _effects[e.stat] = (_effects.TryGetValue(e.stat, out float v) ? v : 1f) * e.value;
            }
            // Laws in force add their effects (suspended while there is no council).
            if (_laws.Count > 0 && LawsInForce)
                foreach (var l in Data.Laws)
                    if (_laws.Contains(l.id))
                        foreach (var e in l.effects)
                            _effects[e.stat] = (_effects.TryGetValue(e.stat, out float v) ? v : 1f) * e.value;
            // Structural effect: ore deposits show once anything reveals them.
            if (_effects.ContainsKey("reveal.ore")) Map.OreRevealed = true;
        }

        // ------------------------------------------------------------------ ages and milestones

        void SetAge(AgeDef age)
        {
            Age = age;
            Milestones.Clear();
            if (age == null) return;
            foreach (var m in age.milestones) Milestones.Add(new MilestoneState { Def = m });
            EvaluateMilestones();
        }

        void EvaluateMilestones()
        {
            foreach (var m in Milestones)
            {
                m.Current = m.Def.type switch
                {
                    "population" => Population,
                    "wintersSurvived" => WintersSurvived,
                    "building" => CountBuildings(m.Def.target),
                    _ => 0,
                };
                if (!m.Done && TownCenter != null && m.Current >= m.Def.value)
                {
                    m.Done = true; m.DoneDay = Day;
                    Log(Severity.Info, $"Milestone reached: {m.Label}.");
                }
            }
        }

        // ------------------------------------------------------------------ events and warnings

        void Log(Severity s, string text) => Log(s, text, false, default);
        void Log(Severity s, string text, float2 at) => Log(s, text, true, at);

        void Log(Severity s, string text, bool hasPlace, float2 at)
        {
            Events.Add(new SimEvent { Day = Day, Severity = s, Text = text, HasPlace = hasPlace, Place = at });
            if (Events.Count > 20000) Events.RemoveRange(0, 5000);
        }

        /// <summary>Current standing warnings (recomputed hourly). A warning is also logged once when it first appears.</summary>
        void RefreshWarnings()
        {
            Warnings.Clear();
            if (TownCenter == null) { Warnings.Add("Choose a site for the campfire."); return; }
            float fd = FoodDays;
            if (fd < B.foodWarnDays) { int n = math.max(1, (int)math.round(fd)); Warnings.Add(fd < 0.5f ? "There is no food left." : $"Food will run out in {n} day{(n == 1 ? "" : "s")}."); }
            float fuelNeed = FuelNeedUntilSpring();
            if (fuelNeed > 0 && StockTotals[_fuel] < fuelNeed * 0.7f && Season >= 2)
                Warnings.Add($"Not enough firewood for winter ({StockTotals[_fuel]:F0} of about {fuelNeed:F0}).");
            int homeless = Homeless;
            if (homeless > 0) Warnings.Add($"{homeless} {(homeless == 1 ? "person has" : "people have")} no home.");
            int cap = StorageCapacity;
            if (cap > 0 && StorageUsed > cap * 0.9f) Warnings.Add("Storage is nearly full.");
            int noTools = 0;
            foreach (var c in Citizens) if (c.Job != null && c.Job.Def.usesTools && c.Tool < 0) noTools++;
            if (noTools > 0) Warnings.Add(noTools == 1 ? "1 worker has no tools." : $"{noTools} workers have no tools.");
            int sick = 0;
            foreach (var c in Citizens) if (!c.Gone && c.Sick) sick++;
            if (sick >= math.max(3, Population / 6)) Warnings.Add($"{sick} people are sick.");
            int unhappy = 0;
            foreach (var c in Citizens) if (!c.Gone && c.UnhappyDays > 0) unhappy++;
            if (unhappy > 0 && unhappy >= Population / 4) Warnings.Add(unhappy == 1 ? "1 person is unhappy and may leave." : $"{unhappy} people are unhappy and may leave.");
            // Heading into winter without the food to get through it (and the lean spring after).
            if (Season == 3 || Season == 2 && DayOfSeason >= B.daysPerSeason / 2)
            {
                float need = DailyFoodNeed * DaysUntilSummer() * 0.7f;
                if (FoodStock < need && fd >= B.foodWarnDays) Warnings.Add($"Food stores won't last until summer ({FoodStock:0} of about {need:0}).");
            }
            if (Unrest >= B.unrestWarn) Warnings.Add($"Unrest is {(Unrest >= B.theftAt ? "high" : "rising")} ({Unrest:P0}): disputes and theft are likely.");
            if (OutbreakRisk >= B.outbreakWarn) Warnings.Add(OutbreakWarning());
            if (_needWater)
            {
                int dry = 0;
                foreach (var c in Citizens) if (!c.Gone && c.NoWater) dry++;
                if (dry > 0 && dry >= Population / 5) Warnings.Add(dry == 1 ? "1 person has no clean water nearby." : $"{dry} people have no clean water nearby.");
            }

            // Log a warning when it appears, but not again while it keeps flickering on and off within a few days
            // (keyed by its first words so changing numbers don't repeat it).
            foreach (var w in Warnings)
            {
                string key = w.Length > 14 ? w.Substring(0, 14) : w;
                if (!_warnSeen.TryGetValue(key, out int seen) || Day - seen > 3) Log(Severity.Warning, w);
                _warnSeen[key] = Day;
            }
        }

        /// <summary>
        /// The outbreak warning names what is actually driving the risk (it used to blame crowding and unclean water even
        /// in a town where every home had water: a big town carries risk by its size alone, until a healer covers it).
        /// </summary>
        string OutbreakWarning()
        {
            int pop = 0, noWater = 0, uncovered = 0, crowded = 0;
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                pop++;
                if (c.NoWater) noWater++;
                if (!HealthCover((c.Home ?? TownCenter).Center, out float ch, out _) || ch > 0.45f) uncovered++;
            }
            foreach (var h in Buildings) if (h.Active && h.Def.housing > 0 && h.Residents > CrowdFree(h)) crowded += h.Residents;
            var why = new List<string>();
            if (crowded * 5 >= pop) why.Add("crowded homes");
            if (noWater * 5 >= pop) why.Add("no clean water");
            if (uncovered * 3 >= pop) why.Add("no healer near");
            if (why.Count == 0) why.Add("a town this size");
            // (Short: it stands as a chip, which has room for about 60 letters.)
            return $"Sickness could spread: {string.Join(", ", why)}.";
        }

        /// <summary>Firewood homes and the campfire will burn from now until spring.</summary>
        public float FuelNeedUntilSpring()
        {
            int homes = 0;
            foreach (var b in Buildings) if (b.Active && b.Def.housing > 0 && b.Residents > 0) homes++;
            float need = 0;
            for (int s = Season; s < 4; s++)
            {
                int days = s == Season ? B.daysPerSeason - DayOfSeason : B.daysPerSeason;
                float perDay = homes * B.firewoodPerHome[s] * Diff.firewood;
                if (TownCenter != null && TownCenter.Def.fuelPerDay != null && TownCenter.Def.fuelPerDay.Length == 4) perDay += TownCenter.Def.fuelPerDay[s];
                need += perDay * days;
            }
            return need;
        }

        /// <summary>Days from now until the first day of summer (the lean seasons run until then).</summary>
        public int DaysUntilSummer() => DaysUntilSeason(1);

        /// <summary>
        /// Days the stores have to carry the settlement: until the next harvest season (autumn) begins. In autumn,
        /// while the harvest comes in, this counts to next year's autumn.
        /// </summary>
        public int LeanDays() => DaysUntilSeason(2);

        int DaysUntilSeason(int target)
        {
            int toNext = B.daysPerSeason - DayOfSeason;
            int s = (Season + 1) % 4, days = toNext;
            while (s != target) { days += B.daysPerSeason; s = (s + 1) % 4; }
            return days;
        }

        readonly List<int> _doors = new();

        /// <summary>
        /// After the walking map changed, rebuilds the paths to every building door in one parallel batch, rather than
        /// one at a time as people next set off (a big town would rebuild a few hundred one by one).
        /// </summary>
        void PrefetchPaths()
        {
            _doors.Clear();
            foreach (var b in Buildings) if (b.Alive && !_doors.Contains(b.Door)) _doors.Add(b.Door);
            if (_doors.Count > Map.Flow.Capacity / 2) _doors.RemoveRange(Map.Flow.Capacity / 2, _doors.Count - Map.Flow.Capacity / 2);
            Map.Flow.Prefetch(_doors);
        }

        /// <summary>Tests only (-sitecheck): every topic known and the last age reached, so every building can be tried.</summary>
        public void DebugUnlockAll()
        {
            foreach (var r in Data.Research) _researched.Add(r.id);
            ResearchQueue.Clear();
            RebuildEffects();
            SetAge(Data.Ages[Data.Ages.Count - 1]);
        }

        int NewId() => ++_nextId;
        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }
}
