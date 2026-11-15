using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Headless auto-play test (-autotest -seed N -years Y -outdir DIR): the auto-player runs the settlement as
    /// fast as it can and the result goes to autotest-report.md / .json. PASS needs: campfire placed, population
    /// never 0, the last age of the slice (the Bronze Age) reached, and no errors or exceptions logged.
    /// </summary>
    public static class AutoTest
    {
        [Serializable]
        public class SeasonRow
        {
            public int year, season, day, population, children, births, migrants, leavers, deaths;
            public string deathCauses = "";
            public float foodDays, food, wood, firewood, stone, tools, hides, clothing, preserved, herbs, happiness, knowledge;
            public float grain, pottery, clay, copperOre, copper, tinOre, bronze, unrest, water = -1, goods = -1, faith = -1;
            public int toolsStone, toolsCopper, toolsBronze, outbreaks, thefts, disputes, trades, fields, paths;
            public float growth, harvest;
            public string toolStock = "";
            public string laws = "";
            public int buildings, sites, housing, researched;
            public string age = "", newResearch = "";
            public string detail = "";      // per-building workers/goods/fullness and what people are doing
        }

        [Serializable]
        public class Report
        {
            public int seed, years, finalDay, finalPopulation, minPopulation, maxPopulation;
            public bool careless;
            public bool campfirePlaced, populationNeverZero, reachedNextAge, reachedFinalAge, noErrors, pass;
            public int ageUpDay = -1, finalAgeDay = -1;
            public string nextAge = "", finalAge = "";
            public int outbreaks, thefts, disputes, trades, upgrades, toolSwaps, paths;
            public List<SeasonRow> seasons = new();
            public List<string> milestones = new(), research = new(), ages = new(), warnings = new(), errors = new(), actions = new();
            public string deaths = "";
            public int births, migrants, leavers;
        }

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Perf probe (-autotest -crowd N): the auto-player's first season with N extra adults, timed per game day.
        /// Writes nothing; logs one line.
        /// </summary>
        public static void Crowd(GameData data, GameMap map, int seed, int extra)
        {
            var s = new Settlement(data, map, seed);
            var ap = new AutoPlayer(s);
            ap.Think();
            s.DebugSpawn(extra);
            float hour = data.Balance.dayLengthSeconds / 24f;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int days = 0;
            while (s.Day < data.Balance.daysPerSeason)   // one spring: before winter thins an unhoused crowd
            {
                s.Tick(hour);
                ap.Think();
                days = s.Day;
            }
            double ms = sw.Elapsed.TotalMilliseconds;
            Debug.Log($"[Perf] {s.Population} citizens at the end ({extra} added): {ms / Math.Max(1, days):F1} ms per game day, " +
                      $"{ms / Math.Max(1, days) / (data.Balance.dayLengthSeconds / Settlement.Step):F3} ms per sim step, {s.Buildings.Count} buildings");
        }

        /// <summary>
        /// Save/load check: <paramref name="saveAtDay"/> &gt;= 0 writes a save to <paramref name="savePath"/> once that day is
        /// reached (and goes on to the end); <paramref name="resume"/> continues a run from such a save. The report state
        /// so far (season rows, snapshot, extremes) travels in the save's test block, so both runs write the same report.
        /// </summary>
        public static bool Run(GameData data, GameMap map, int seed, int years, string outDir, bool careless = false,
                               int saveAtDay = -1, string savePath = null, SaveGame.Loaded resume = null, int difficulty = 1)
        {
            var errors = new List<string>();
            void OnLog(string msg, string stack, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add($"{type}: {msg}" + (type == LogType.Exception ? "\n" + stack : ""));
            }
            Application.logMessageReceived += OnLog;
            var rep = new Report { seed = seed, years = years, minPopulation = int.MaxValue, careless = careless };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Settlement s = null;
            AutoPlayer ap = null;
            try
            {
                float hour = data.Balance.dayLengthSeconds / 24f;
                int lastSeasonIdx = 0;
                Snap prev;
                if (resume != null)
                {
                    s = resume.Sim; ap = resume.Auto ?? new AutoPlayer(s);
                    ap.Profile = true; s.Profile = true;
                    var r = new SaveReader(new MemoryStream(resume.Test)) { Version = resume.Header.Version };
                    r.Fields(rep);
                    lastSeasonIdx = r.Int(); _noon = r.Str();
                    prev = ReadSnap(r);
                }
                else
                {
                    s = new Settlement(data, map, seed);
                    s.SetDifficulty(difficulty);
                    ap = new AutoPlayer(s) { Careless = careless, Profile = true };
                    s.Profile = true;
                    rep.nextAge = s.Age.next;
                    rep.finalAge = data.Ages[data.Ages.Count - 1].id;
                    ap.Think();
                    rep.campfirePlaced = !s.NeedsCampfire;
                    prev = Snapshot(s);
                }
                int totalDays = years * s.DaysPerYear;
                bool saved = false;
                while (rep.campfirePlaced && s.Day < totalDays)
                {
                    s.Tick(hour);
                    ap.Think();
                    int pop = s.Population;
                    // What people are doing at midday (the season rows are taken at midnight, when everyone sleeps).
                    if (s.DayFraction >= 0.5f && s.DayFraction < 0.5f + 1f / 24f) _noon = Detail(s);
                    rep.minPopulation = Math.Min(rep.minPopulation, pop);
                    rep.maxPopulation = Math.Max(rep.maxPopulation, pop);
                    if (pop == 0) break;
                    int seasonIdx = s.Day / data.Balance.daysPerSeason;
                    if (seasonIdx != lastSeasonIdx && seasonIdx % 4 == 0)
                    {
                        Debug.Log($"[AutoTest] year {seasonIdx / 4 + 1}: {pop} people, {sw.Elapsed.TotalSeconds:F0} s; sim citizens {s.ProfCitizens / 1000:F1} s, hourly {s.ProfHourly / 1000:F1} s, daily {s.ProfDaily / 1000:F1} s, jobs {s.ProfJobs / 1000:F1} s; auto-player "
                                  + string.Join(", ", ap.ProfMs.Select(kv => $"{kv.Key} {kv.Value / 1000:F1} s")));
                    }
                    if (seasonIdx != lastSeasonIdx)
                    {
                        rep.seasons.Add(Row(s, prev, lastSeasonIdx, data));
                        prev = Snapshot(s);
                        lastSeasonIdx = seasonIdx;
                    }
                    if (saveAtDay >= 0 && !saved && s.Day >= saveAtDay)
                    {
                        saved = true;
                        var ms = new MemoryStream();
                        var w = new SaveWriter(ms);
                        w.Fields(rep); w.Int(lastSeasonIdx); w.Str(_noon); WriteSnap(w, prev);
                        w.W.Flush();
                        SaveGame.Write(savePath, SaveGame.HeaderFor(s, $"auto-test seed {seed}", "test"), s, ap, null, null, ms.ToArray());
                        Debug.Log($"[SaveLoad] saved at day {s.Day} ({s.DayFraction:F3}) to {savePath}");
                    }
                }
                if (rep.campfirePlaced && (rep.seasons.Count == 0 || rep.seasons[rep.seasons.Count - 1].day != s.Day)) rep.seasons.Add(Row(s, prev, lastSeasonIdx, data));
            }
            catch (Exception e)
            {
                errors.Add("Exception: " + e);
                Debug.LogException(e);
            }
            Application.logMessageReceived -= OnLog;

            if (s != null)
            {
                rep.finalDay = s.Day;
                rep.finalPopulation = s.Population;
                rep.populationNeverZero = rep.campfirePlaced && rep.minPopulation > 0;
                var up = s.AgeLog.FirstOrDefault(a => a.age == rep.nextAge);
                rep.reachedNextAge = up.age == rep.nextAge && !string.IsNullOrEmpty(up.age);
                rep.ageUpDay = rep.reachedNextAge ? up.day : -1;
                var fin = s.AgeLog.FirstOrDefault(a => a.age == rep.finalAge);
                rep.reachedFinalAge = fin.age == rep.finalAge && !string.IsNullOrEmpty(fin.age);
                rep.finalAgeDay = rep.reachedFinalAge ? fin.day : -1;
                rep.outbreaks = s.Outbreaks; rep.thefts = s.Thefts; rep.disputes = s.Disputes; rep.trades = s.Trades;
                rep.upgrades = s.Upgrades; rep.toolSwaps = s.ToolSwaps; rep.paths = s.PathCount;
                foreach (var a in s.AgeLog) rep.ages.Add($"{data.Age(a.age)?.name} on {When(s, a.day)}");
                foreach (var r in s.ResearchLog) rep.research.Add($"{data.ResearchById(r.id)?.name} on {When(s, r.day)}");
                foreach (var e in s.Events)
                {
                    if (e.Text.StartsWith("Milestone reached")) rep.milestones.Add($"{e.Text.Substring(19).TrimEnd('.')} on {When(s, e.Day)}");
                    if (e.Severity != Severity.Info) rep.warnings.Add($"{When(s, e.Day)}: {e.Text}");
                }
                rep.deaths = string.Join(", ", s.Deaths.Select(kv => $"{kv.Key} {kv.Value}"));
                rep.births = s.Births; rep.migrants = s.Migrants; rep.leavers = s.Leavers;
                rep.actions.AddRange(ap.Actions);
            }
            rep.errors.AddRange(errors);
            rep.noErrors = errors.Count == 0;
            rep.pass = rep.campfirePlaced && rep.populationNeverZero && rep.reachedFinalAge && rep.noErrors;
            double msPerStep = s != null && s.Day > 0 ? sw.Elapsed.TotalMilliseconds / (s.Day * (data.Balance.dayLengthSeconds / Settlement.Step)) : 0;
            if (rep.minPopulation == int.MaxValue) rep.minPopulation = 0;

            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, "autotest-report.md"), Markdown(rep, s, data));
            File.WriteAllText(Path.Combine(outDir, "autotest-report.json"), JsonUtility.ToJson(rep, true));
            Debug.Log($"[AutoTest] seed {seed}: {(rep.pass ? "PASS" : "FAIL")} in {sw.Elapsed.TotalSeconds:F1} s real time, day {rep.finalDay}, population {rep.finalPopulation} (max {rep.maxPopulation}), {msPerStep:F3} ms per sim step with the auto-player");
            // The wall-clock line goes to the log only: the report files stay byte-identical for a seed.
            // Sim cost on its own (no auto-player) for two more game days of the final town, after the report is written.
            if (s != null && s.Population > 0)
            {
                var sw2 = System.Diagnostics.Stopwatch.StartNew();
                s.Profile = true;
                s.ProfCitizens = s.ProfHourly = s.ProfDaily = s.ProfJobs = 0;
                int built0 = map.Flow.FieldsBuilt;
                s.Tick(data.Balance.dayLengthSeconds * 2f);
                float steps = 2f * data.Balance.dayLengthSeconds / Settlement.Step;
                Debug.Log($"[Perf] sim alone, town of {s.Population} with {s.Buildings.Count(b => b.Alive)} buildings: {sw2.Elapsed.TotalMilliseconds / steps:F3} ms per sim step " +
                          $"(citizens {s.ProfCitizens / steps:F3}, hourly {s.ProfHourly / steps:F3}, daily {s.ProfDaily / steps:F3}, job fill {s.ProfJobs / steps:F3}; {map.Flow.FieldsBuilt - built0} path fields built)");
            }
            return rep.pass;
        }

        static string When(Settlement s, int day) =>
            $"day {day} (year {day / s.DaysPerYear + 1}, {s.Data.Balance.seasonNames[(day / s.Data.Balance.daysPerSeason) % 4]})";

        struct Snap { public int births, migrants, leavers, deaths, research, outbreaks, thefts, disputes, trades; public Dictionary<string, int> causes; public double[] produced, consumed; public double workerSec, prodSec; }

        static void WriteSnap(SaveWriter w, Snap p)
        {
            w.Int(p.births); w.Int(p.migrants); w.Int(p.leavers); w.Int(p.deaths); w.Int(p.research);
            w.Int(p.outbreaks); w.Int(p.thefts); w.Int(p.disputes); w.Int(p.trades);
            w.Int(p.causes.Count); foreach (var kv in p.causes) { w.Str(kv.Key); w.Int(kv.Value); }
            w.Doubles(p.produced); w.Doubles(p.consumed); w.Double(p.workerSec); w.Double(p.prodSec);   // doubles from save format 2
        }

        static Snap ReadSnap(SaveReader r)
        {
            var p = new Snap { births = r.Int(), migrants = r.Int(), leavers = r.Int(), deaths = r.Int(), research = r.Int(),
                               outbreaks = r.Int(), thefts = r.Int(), disputes = r.Int(), trades = r.Int(), causes = new Dictionary<string, int>() };
            for (int i = 0, n = r.Int(); i < n; i++) p.causes[r.Str()] = r.Int();
            if (r.Version >= 2) { p.produced = r.Doubles(); p.consumed = r.Doubles(); p.workerSec = r.Double(); p.prodSec = r.Double(); }
            else { p.produced = Array.ConvertAll(r.Floats(), v => (double)v); p.consumed = Array.ConvertAll(r.Floats(), v => (double)v); p.workerSec = r.Float(); p.prodSec = r.Float(); }
            return p;
        }

        static Snap Snapshot(Settlement s) => new Snap
        {
            births = s.Births, migrants = s.Migrants, leavers = s.Leavers, deaths = s.TotalDeaths,
            research = s.ResearchLog.Count, causes = new Dictionary<string, int>(s.Deaths),
            outbreaks = s.Outbreaks, thefts = s.Thefts, disputes = s.Disputes, trades = s.Trades,
            produced = (double[])s.Produced.Clone(), consumed = (double[])s.Consumed.Clone(),
            workerSec = s.WorkerSeconds, prodSec = s.ProductiveSeconds,
        };

        static string _noon = "";

        static SeasonRow Row(Settlement s, Snap prev, int seasonIdx, GameData data)
        {
            var b = data.Balance;
            var causes = s.Deaths.Where(kv => kv.Value - (prev.causes.TryGetValue(kv.Key, out int p) ? p : 0) > 0)
                .Select(kv => $"{kv.Key} {kv.Value - (prev.causes.TryGetValue(kv.Key, out int p2) ? p2 : 0)}");
            return new SeasonRow
            {
                year = seasonIdx / 4 + 1, season = seasonIdx % 4, day = s.Day,
                population = s.Population, children = s.Children,
                births = s.Births - prev.births, migrants = s.Migrants - prev.migrants, leavers = s.Leavers - prev.leavers,
                deaths = s.TotalDeaths - prev.deaths, deathCauses = string.Join(", ", causes),
                foodDays = s.FoodDays, food = s.FoodStock,
                wood = s.Stock("wood"), firewood = s.Stock("firewood"), stone = s.Stock("stone"), tools = s.ToolStock,
                hides = s.Stock("hides"), clothing = s.Stock("clothing"), preserved = s.Stock("preserved"), herbs = s.Stock("herbs"),
                happiness = s.AverageHappiness, knowledge = s.KnowledgeTotal,
                buildings = s.Buildings.Count(x => x.Active), sites = s.Buildings.Count(x => x.IsSite), housing = s.HousingCapacity,
                researched = s.ResearchLog.Count, age = s.Age.name,
                grain = s.Stock("grain"), pottery = s.Stock("pottery"), clay = s.Stock("clay"), copperOre = s.Stock("copper_ore"),
                copper = s.Stock("copper"), tinOre = s.Stock("tin_ore"), bronze = s.Stock("bronze"), unrest = s.Unrest,
                water = s.NeedMet("water"), goods = s.NeedMet("goods"), faith = s.NeedMet("faith"),
                toolsStone = s.Citizens.Count(c => c.Tool == s.ResIndex("tools")), toolsCopper = s.Citizens.Count(c => c.Tool == s.ResIndex("copper_tools")),
                toolsBronze = s.Citizens.Count(c => c.Tool == s.ResIndex("bronze_tools")),
                outbreaks = s.Outbreaks - prev.outbreaks, thefts = s.Thefts - prev.thefts, disputes = s.Disputes - prev.disputes, trades = s.Trades - prev.trades,
                fields = s.Buildings.Count(x => x.Active && x.Def.crop), paths = s.PathCount,
                toolStock = $"{s.Stock("tools"):F0}/{s.Stock("copper_tools"):F0}/{s.Stock("bronze_tools"):F0}",
                growth = s.Buildings.Where(x => x.Active && x.Def.crop).Select(x => x.Growth).DefaultIfEmpty(0).Average(),
                harvest = s.ResIndex("grain") >= 0 ? (float)(s.Produced[s.ResIndex("grain")] - prev.produced[s.ResIndex("grain")]) : 0, laws = string.Join(" ", s.ActiveLaws.OrderBy(x => x)),
                newResearch = string.Join(", ", s.ResearchLog.Skip(prev.research).Select(r => data.ResearchById(r.id)?.name)),
                detail = _noon + " | made " + string.Join(", ", Enumerable.Range(0, s.ResCount).Where(i => s.Produced[i] - prev.produced[i] > 0.05f)
                    .Select(i => $"{s.Data.Resources[i].id} {s.Produced[i] - prev.produced[i]:F0}")) + " | used " + string.Join(", ", Enumerable.Range(0, s.ResCount).Where(i => s.Consumed[i] - prev.consumed[i] > 0.05f)
                    .Select(i => $"{s.Data.Resources[i].id} {s.Consumed[i] - prev.consumed[i]:F0}")) + $" | at work {(s.ProductiveSeconds - prev.prodSec) / Math.Max(1.0, s.WorkerSeconds - prev.workerSec):P0}",
            };
        }

        static string Detail(Settlement s)
        {
            var parts = s.Buildings.Where(b => b.Alive && (b.Def.IsWorkplace || b.IsSite)).Select(b =>
                b.IsSite ? $"{b.Def.id}[site {b.MaterialsFraction:F2}/{b.Work:F2}]"
                         : $"{b.Def.id}[{b.Priority.ToString()[0]} {b.Workers}w out {b.OutTotal:F1} full {b.Fullness:F2}{(b.Status.Length > 0 ? " " + b.Status : "")}]");
            var acts = s.Citizens.GroupBy(c => c.Activity).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")
                .Concat(new[] { "builder tasks: " + string.Join(" ", s.Citizens.Where(c => c.IsBuilder).Select(c => $"{c.Task}{(c.TaskB2 != null ? "@" + c.TaskB2.Def.id : "")}{(c.Walking ? "~" : "")}")) })
                .Concat(new[] { "wood tasks: " + string.Join(" ", s.Citizens.Where(c => c.Job != null && c.Job.Def.id == "woodcutter").Select(c => $"{c.Task}{(c.AtField ? "F" : "")}{(c.Walking ? "~" : "")}{c.LoadTotal:F1}")) });
            int jobs = s.Citizens.Count(c => c.Job != null), builders = s.Citizens.Count(c => c.IsBuilder), sick = s.Citizens.Count(c => c.Sick);
            float hp = s.Citizens.Count > 0 ? s.Citizens.Average(c => c.Health) : 0;
            return $"{string.Join(" ", parts)} | jobs {jobs} builders {builders} sick {sick} health {hp:F2} tools held {s.Citizens.Count(c => c.Tool >= 0)} | {string.Join(", ", acts)}";
        }

        static string F(float v) => v.ToString(v >= 100 ? "F0" : "F1", Inv);

        static string Markdown(Report r, Settlement s, GameData data)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# Firstlight auto-test, seed {r.seed}{(r.careless ? " (careless player)" : "")}{(s != null && s.Difficulty != 1 ? $" ({s.Diff.name})" : "")}");
            sb.AppendLine();
            sb.AppendLine($"Ran {r.years} game years ({r.finalDay} days). Final population {r.finalPopulation} (min {r.minPopulation}, max {r.maxPopulation}).");
            sb.AppendLine($"Births {r.births}, migrants {r.migrants}, leavers {r.leavers}, deaths: {(r.deaths.Length > 0 ? r.deaths : "none")}.");
            sb.AppendLine(r.reachedNextAge ? $"Reached the {data.Age(r.nextAge)?.name} on {When(s, r.ageUpDay)}." : $"Did not reach the {data.Age(r.nextAge)?.name}.");
            sb.AppendLine(r.reachedFinalAge ? $"Reached the {data.Age(r.finalAge)?.name} on {When(s, r.finalAgeDay)}." : $"Did not reach the {data.Age(r.finalAge)?.name}.");
            sb.AppendLine($"Outbreaks {r.outbreaks}, thefts {r.thefts}, disputes {r.disputes}, trader visits {r.trades}, upgrades {r.upgrades}, tool swaps {r.toolSwaps}, path tiles {r.paths}.");
            if (s != null)
            {
                double food = 0, all = 0;
                for (int i = 0; i < s.ResCount; i++) { all += s.Lost[i]; if (data.Resources[i].isFood) food += s.Lost[i]; }
                sb.AppendLine($"Goods lost (spoiled, soaked, stolen, dropped, no room, taken by leavers and the dead): food {food.ToString("F0", Inv)}, all goods {all.ToString("F0", Inv)}.");
            }
            sb.AppendLine();
            sb.AppendLine("## Seasons");
            sb.AppendLine();
            sb.AppendLine("| Year | Season | Day | Pop (kids) | Births | Deaths | Migr. | Left | Food days | Food | Wood | Firewood | Stone | Tools | Hides | Cloth. | Herbs | Bldgs (sites) | Housing | Happy | Research done | Age |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var x in r.seasons)
            {
                string deaths = x.deaths > 0 ? $"{x.deaths} ({x.deathCauses})" : "0";
                string res = x.newResearch.Length > 0 ? $"{x.researched}: +{x.newResearch}" : x.researched.ToString();
                sb.AppendLine($"| {x.year} | {data.Balance.seasonNames[x.season]} | {x.day} | {x.population} ({x.children}) | {x.births} | {deaths} | {x.migrants} | {x.leavers} | {F(x.foodDays)} | {F(x.food)} | {F(x.wood)} | {F(x.firewood)} | {F(x.stone)} | {F(x.tools)} | {F(x.hides)} | {F(x.clothing)} | {F(x.herbs)} | {x.buildings} ({x.sites}) | {x.housing} | {x.happiness.ToString("F2", Inv)} | {res} | {x.age} |");
            }
            sb.AppendLine();
            sb.AppendLine("Each row is the state at the end of that season; births, deaths, migrants and leavers are counted over the season.");
            sb.AppendLine();
            sb.AppendLine("## Agricultural systems");
            sb.AppendLine();
            sb.AppendLine("| Year | Season | Pop | Fields (growth) | Harvest | Grain | Pottery | Clay | Cu ore | Copper | Tin ore | Bronze | Tools held s/c/b (stock) | Water | Goods | Faith | Unrest | Outbr. | Theft | Disp. | Trades | Paths | Laws |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            string Pct(float v) => v < 0 ? "-" : v.ToString("P0", Inv);
            foreach (var x in r.seasons)
                sb.AppendLine($"| {x.year} | {data.Balance.seasonNames[x.season]} | {x.population} | {x.fields} ({x.growth.ToString("F2", Inv)}) | {F(x.harvest)} | {F(x.grain)} | {F(x.pottery)} | {F(x.clay)} | {F(x.copperOre)} | {F(x.copper)} | {F(x.tinOre)} | {F(x.bronze)} | {x.toolsStone}/{x.toolsCopper}/{x.toolsBronze} ({x.toolStock}) | {Pct(x.water)} | {Pct(x.goods)} | {Pct(x.faith)} | {x.unrest.ToString("F2", Inv)} | {x.outbreaks} | {x.thefts} | {x.disputes} | {x.trades} | {x.paths} | {x.laws} |");
            sb.AppendLine();
            sb.AppendLine("## Season detail (workplaces and people at noon on the last day; goods made and used over the season)");
            sb.AppendLine();
            foreach (var x in r.seasons) sb.AppendLine($"- Y{x.year} {data.Balance.seasonNames[x.season]}: {x.detail}");
            sb.AppendLine();
            void Section(string title, List<string> items)
            {
                sb.AppendLine($"## {title}");
                sb.AppendLine();
                if (items.Count == 0) sb.AppendLine("(none)");
                foreach (var i in items) sb.AppendLine("- " + i.Replace("\n", "\n  "));
                sb.AppendLine();
            }
            Section("Ages", r.ages);
            Section("Milestones", r.milestones);
            Section("Research", r.research);
            Section("Warnings and bad events", r.warnings);
            Section("Errors and exceptions", r.errors);
            Section("Auto-player actions", r.actions);
            sb.AppendLine("## Result");
            sb.AppendLine();
            sb.AppendLine($"- Campfire placed: {(r.campfirePlaced ? "yes" : "NO")}");
            sb.AppendLine($"- Population never 0: {(r.populationNeverZero ? "yes" : "NO")}");
            sb.AppendLine($"- Reached the {data.Age(r.nextAge)?.name}: {(r.reachedNextAge ? "yes" : "NO")}");
            sb.AppendLine($"- Reached the {data.Age(r.finalAge)?.name}: {(r.reachedFinalAge ? "yes" : "NO")}");
            sb.AppendLine($"- No errors or exceptions: {(r.noErrors ? "yes" : "NO")}");
            sb.AppendLine();
            sb.AppendLine(r.pass ? "**PASS**" : "**FAIL**");
            return sb.ToString();
        }
    }
}
