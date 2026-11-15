using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Goals (G), jobs (J), stocks (T), help (F1), and the left-hand feed of warnings and events.
    public sealed partial class Hud
    {
        /// <summary>Side panels sit left of the info panel when something is selected.</summary>
        float SideX(float w) => (Selected != null || SelectedCitizen != null) ? VW - InfoW - 20 - w : VW - 10 - w;

        Rect BeginSide(string title, float w, float h, System.Action close)
        {
            var r = Box(new Rect(SideX(w), TopBarHeight + 8, w, h));
            GUI.Label(new Rect(r.x + 16, r.y + 10, w - 60, 26), title, Title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 10, 24, 22), "x", Button)) close();
            _lx = r.x + 16; _lw = w - 32; _ly = r.y + 44;
            return r;
        }

        float _goalsH = 300, _jobsH = 300, _stocksH = 300;

        sealed class JobRow
        {
            public BuildingDef Def; public Building First; public int N, W, Max, Hi, Lo, Paused; public string Note;
            public void Reset(BuildingDef d) { Def = d; First = null; N = W = Max = Hi = Lo = Paused = 0; Note = null; }
        }
        readonly List<JobRow> _jobRowPool = new();
        Vector2 _jobsScroll;

        // ------------------------------------------------------------------ goals

        void DrawGoals()
        {
            var next = S.Age != null && !string.IsNullOrEmpty(S.Age.next) ? D.Age(S.Age.next) : null;
            var r = BeginSide(next != null ? $"Goals: toward the {next.name}" : "Goals", 440, _goalsH, () => GoalsOpen = false);
            if (next == null) { Para("This is the last age in this build."); _goalsH = _ly - r.y + 12; return; }
            Row("Key research", Bold);
            foreach (var k in S.Age.keyResearch)
            {
                var rd = D.ResearchById(k);
                bool done = S.IsResearched(k);
                int qi = S.ResearchQueue.IndexOf(k);
                string state = done ? Col("done", Good) : qi >= 0 ? Col($"queued #{qi + 1}, {S.ResearchProgress(k):P0}", Accent) : Col("not queued", Warn);
                Row($"{Tick(done)} {rd?.name}   {state}");
            }
            if (S.Age.keyResearch.Any(k => !S.IsResearched(k) && !S.ResearchQueue.Contains(k)))
            {
                if (GUI.Button(new Rect(_lx, _ly + 2, 240, 26), "Queue the key research", Button))
                    foreach (var k in S.Age.keyResearch) if (!S.IsResearched(k)) S.QueueResearch(k);
                _ly += 32;
            }
            Gap(6);
            Row("Milestones", Bold);
            foreach (var m in S.Milestones)
            {
                string cur = m.Done ? Col($"reached day {m.DoneDay + 1}", Good) : Col($"{math.min(m.Current, m.Def.value)} / {m.Def.value}", Dim);
                Row($"{Tick(m.Done)} {m.Label}   {cur}");
            }
            Gap(10);
            bool can = S.CanAdvanceAge(out string why);
            var br = new Rect(_lx, _ly, _lw, 38);
            if (GUI.Button(br, can ? $"<b>Advance to the {next.name}</b>" : Col($"Advance to the {next.name}  (not yet)", Dim), can ? ButtonOn : ButtonDim) && can) AdvanceAge();
            _ly += 44;
            if (!can) Para(why);
            else Para("Everything is in place. Advancing unlocks the next age's buildings and research.");
            _goalsH = _ly - r.y + 10;
        }

        public void AdvanceAge() { S.AdvanceAge(); AgeReadyRefresh(); }

        static string Tick(bool done) => done ? Col("[x]", Good) : Col("[  ]", Faint);

        // ------------------------------------------------------------------ jobs

        void DrawJobs()
        {
            var r = BeginSide("Jobs", 470, _jobsH, () => JobsOpen = false);
            int adults = S.Adults, jobs = S.Citizens.Count(c => !c.Gone && c.Job != null), builders = S.Citizens.Count(c => !c.Gone && c.IsBuilder);
            int sick = S.Citizens.Count(c => !c.Gone && c.Sick && S.Citizens.Count > 0 && c.Age >= D.Balance.adultAge);
            int free = adults - jobs - builders - sick;
            var (onlyAdults, elders, kids) = PeopleSplit();
            Para($"{onlyAdults} adults and {elders} elders: {jobs} in jobs, {builders} building, {Col($"{free} free", free > 0 ? Good : Dim)} (they haul goods), {sick} sick. {kids} children.", Wrap);
            // Places that want workers now (not paused, flooded or out of season), how many are filled, and who carries.
            int places = 0, filled = 0, hiWorkers = 0, normalWorkers = 0;
            foreach (var b in S.Buildings)
            {
                if (!b.Active || !b.Def.IsWorkplace) continue;
                if (b.Priority == Priority.High) hiWorkers += b.Workers; else if (b.Priority == Priority.Normal) normalWorkers += b.Workers;
                if (JobIdle(b) != null) continue;
                places += b.Def.maxWorkers; filled += b.Workers;
            }
            Row($"Places: {places}, filled {filled}" + (places > filled ? Col($" · short of {places - filled} worker{(places - filled == 1 ? "" : "s")}", Warn) : Col(" · all filled", Good)), Label);
            if (free <= 0) Row(Col("Nobody is free to haul: goods wait at workplaces", Warn), Label);
            Gap(2);
            GUI.Label(new Rect(_lx, _ly, 200, 20), "Workplace", Small);
            GUI.Label(new Rect(_lx + 200, _ly, 80, 20), "Workers", Small);
            GUI.Label(new Rect(_lx + 312, _ly, 130, 20), "Priority", Small);
            _ly += 22;
            // The rows (gathered first, so a list taller than the screen scrolls instead of running off the bottom).
            int nRows = 0;
            float content = 0;
            foreach (var g in S.Buildings.Where(b => b.Active && b.Def.IsWorkplace).GroupBy(b => b.Def).OrderBy(g => g.Key.category).ThenBy(g => g.Key.name))
            {
                if (nRows == _jobRowPool.Count) _jobRowPool.Add(new JobRow());
                var jr = _jobRowPool[nRows++]; jr.Reset(g.Key);
                Building shortB = null;
                foreach (var b in g)
                {
                    jr.First ??= b; jr.N++;
                    jr.W += b.Workers; jr.Max += b.Def.maxWorkers;
                    if (b.Priority == Priority.High) jr.Hi++; else if (b.Priority == Priority.Low) jr.Lo++;
                    if (b.Paused) jr.Paused++;
                    if (shortB == null && b.Workers < b.Def.maxWorkers) shortB = b;
                }
                jr.Note = JobNote(g.Key, shortB ?? jr.First, jr.W, jr.Max, free, hiWorkers, normalWorkers, builders);
                content += jr.Note != null ? 43 : 25;
            }
            float avail = VH - 70 - _ly - 40;
            bool scroll = content > avail;
            float lx0 = _lx, ly0 = _ly, lw0 = _lw;
            if (scroll)
            {
                _jobsScroll = GUI.BeginScrollView(new Rect(_lx - 6, _ly, _lw + 12, avail), _jobsScroll, new Rect(0, 0, _lw - 6, content));
                _lx = 6; _ly = 0; _lw -= 18;
            }
            for (int k = 0; k < nRows; k++)
            {
                var jr = _jobRowPool[k];
                var row = new Rect(_lx - 6, _ly, _lw + 12, jr.Note != null ? 42 : 24);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { Select(jr.First); JumpTo(jr.First.Center); }
                if (row.Contains(Event.current.mousePosition)) Fill(row, new Color(1, 1, 1, 0.05f));
                GUI.Label(new Rect(_lx, _ly, 200, 24), jr.N > 1 ? $"{jr.Def.name} x{jr.N}" : jr.Def.name, Label);
                // An empty bar draws no colour, so the count carries it: red at none, amber short, green full.
                var wc = jr.W >= jr.Max ? Good : jr.W == 0 ? Bad : Warn;
                if (jr.W == 0) Fill(new Rect(_lx + 200, _ly + 7, 50, 10), new Color(Bad.r, Bad.g, Bad.b, 0.28f));
                else Bar(new Rect(_lx + 200, _ly + 7, 50, 10), jr.W / (float)math.max(1, jr.Max), wc);
                GUI.Label(new Rect(_lx + 256, _ly, 52, 24), Col($"{jr.W}/{jr.Max}", wc), Label);
                string p = (jr.Hi > 0 ? Col($"{jr.Hi} high ", Accent) : "") + (jr.Lo > 0 ? Col($"{jr.Lo} low ", Dim) : "") + (jr.Paused > 0 ? Col($"{jr.Paused} paused", Warn) : "");
                GUI.Label(new Rect(_lx + 312, _ly, _lw - 312, 24), p.Length > 0 ? p : Col("normal", Dim), Small);
                _ly += 24;
                if (jr.Note != null) { GUI.Label(new Rect(_lx + 12, _ly - 2, _lw - 12, 18), Col(jr.Note, Dim), Small); _ly += 18; }
                _ly += 1;
            }
            if (scroll)
            {
                GUI.EndScrollView();
                _lx = lx0; _lw = lw0; _ly = ly0 + avail;
            }
            Gap(4);
            Para(Col("Click a row to select it. Priorities and pausing are set on each building.", Dim));
            _jobsH = _ly - r.y + 8;
        }

        /// <summary>Why a workplace wants nobody now (its status as the sim sets it), or null when it takes workers.</summary>
        static string JobIdle(Building b) => b.Paused ? "paused" : b.Flooded ? "flooded" : b.Status switch
        {
            "Nothing to do this season" => "nothing to do this season",
            "The harvest is in" => "the harvest is in",
            _ => null,
        };

        /// <summary>
        /// The faint line under a Jobs row: why it is short of workers (paused, out of season, or who came first: jobs fill
        /// High first, then builders, then Normal with fields first, then Low), or what stops the workers it has.
        /// </summary>
        string JobNote(BuildingDef d, Building b, int w, int max, int free, int hiWorkers, int normalWorkers, int builders)
        {
            if (b == null) return null;
            string idle = JobIdle(b);
            if (idle != null) return w < max ? Cap(idle) : null;
            if (w < max)
            {
                if (free > 0) return null;                         // filled at the next shift change
                var before = new List<string>(3);
                if (b.Priority < Priority.High && hiWorkers > 0) before.Add("High jobs");
                if (b.Priority < Priority.High && builders > 0) before.Add("builders");
                if (b.Priority == Priority.Low && normalWorkers > 0) before.Add("Normal jobs");
                if (before.Count == 0) return w == 0 ? "Nobody left to take it: more people, or raise its priority" : null;
                return before.Count == 1 ? $"Short: {(before[0] == "High jobs" ? "High-priority jobs" : before[0])} take everyone first" : $"Short: taken first by {string.Join(", ", before.GetRange(0, before.Count - 1))} and {before[before.Count - 1]}";
            }
            return b.Status switch
            {
                "Needs tools" => "Its workers need tools",
                "Waiting for inputs" => "Waiting for its inputs",
                "Waiting for goods to be carried off" => "Full: waiting for haulers",
                _ => null,
            };
        }

        // ------------------------------------------------------------------ stocks

        void DrawStocks()
        {
            var r = BeginSide("Goods in store", 400, _stocksH, () => StocksOpen = false);
            BarRow("Storage", S.StorageUsed / math.max(1, S.StorageCapacity), S.StorageUsed > S.StorageCapacity * 0.9f ? Warn : Info, $"{S.StorageUsed:0}/{S.StorageCapacity}");
            Gap(4);
            GUI.Label(new Rect(_lx, _ly, 180, 20), "Good", Small);
            GUI.Label(new Rect(_lx + 180, _ly, 80, 20), "In store", SmallRight);
            GUI.Label(new Rect(_lx + 270, _ly, 90, 20), "Last day", SmallRight);
            _ly += 22;
            for (int i = 0; i < S.ResCount; i++)
            {
                float amt = S.StockTotals[i];
                if (amt < 0.05f && S.Produced[i] < 0.05f) continue;
                var rd = D.Resources[i];
                GUI.Label(new Rect(_lx, _ly, 180, 22), rd.name + (rd.isFood ? Col("  food", Faint) : ""), Label);
                GUI.Label(new Rect(_lx + 180, _ly, 80, 22), $"<b>{amt:0}</b>", Right);
                float t = Trend(i);
                GUI.Label(new Rect(_lx + 270, _ly, 90, 22), math.abs(t) < 0.05f ? Col("-", Faint) : Col($"{t:+0.#;-0.#}", t > 0 ? Good : Bad), Right);
                _ly += 23;
            }
            // Tools of all kinds together: the top bar's "Tools" figure.
            float tools = ToolsInStore();
            if (tools >= 0.05f)
            {
                GUI.Label(new Rect(_lx, _ly, 180, 22), Col("Tools (all kinds)", Dim), Label);
                GUI.Label(new Rect(_lx + 180, _ly, 80, 22), $"<b>{tools:0}</b>", Right);
                float tt = ToolTrend();
                GUI.Label(new Rect(_lx + 270, _ly, 90, 22), math.abs(tt) < 0.05f ? Col("-", Faint) : Col($"{tt:+0.#;-0.#}", tt > 0 ? Good : Bad), Right);
                _ly += 23;
            }
            _stocksH = _ly - r.y + 10;
        }

        // ------------------------------------------------------------------ help

        void DrawHelp()
        {
            var r = Box(new Rect(VW / 2 - 410, VH / 2 - 390, 820, 780));
            GUI.Label(new Rect(r.x + 20, r.y + 12, 400, 28), "Controls", Title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 12, 24, 22), "x", Button)) HelpOpen = false;
            string[,] keys =
            {
                { "W A S D, arrows, middle-drag", "Move the camera (or push the pointer to the screen edge)" },
                { "Q / E, right-drag", "Turn and tilt the camera" },
                { "Wheel, PgUp / PgDn, + / -", "Zoom in and out" },
                { "Home or H", "Camera back to the campfire" },
                { "Left-click", "Select a building or person; place what you are building" },
                { "Tab", "Before the campfire is placed: show a good site for it" },
                { "Shift + left-click", "Place several of the same building" },
                { "Right-click, Esc", "Stop placing or removing; close a panel; clear the selection" },
                { ",  and  .", "Turn a building while placing it (its door turns with it)" },
                { "Esc (nothing open)", "Game menu: save, load, settings, quit" },
                { "F5 / F8", "Quicksave / quickload" },
                { "B", "Build menu" },
                { "X or Delete", "Demolish tool" },
                { "Space", "Pause / resume" },
                { "1, 2, 3", "Speed 1x, 2x, 4x" },
                { "R", "Research tree" },
                { "G", "Goals for the next age" },
                { "J", "Jobs overview" },
                { "V", "Events: what happened, newest first; Topics: hints seen" },
                { "L", "Laws (once there is a Tribal Council)" },
                { "T", "Goods in store" },
                { "F1", "This help" },
                { "F9", "Let the auto-player run the settlement" },
                { "F11", "Hide the interface" },
            };
            float y = r.y + 50;
            // Key names and their texts fit their columns (290 and 480 wide; the long ones used to clip at 210 and 400).
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                GUI.Label(new Rect(r.x + 24, y, 290, 26), $"<b>{keys[i, 0]}</b>", Label);
                GUI.Label(new Rect(r.x + 320, y, 480, 26), keys[i, 1], Label);
                y += 27;
            }
            GUI.Label(new Rect(r.x + 24, y + 2, 772, 22), Col("Hover any number for what moves it; click an event (›) to go there.", Dim), Small);
            if (GUI.Button(new Rect(r.x + 24, r.yMax - 46, 160, 30), "Game menu (Esc)", Button)) { HelpOpen = false; OpenMenu(); }
            GUI.Label(new Rect(r.x + 200, r.yMax - 46, 500, 30), Col("Saves itself at the start of every season.", Dim), Small);
        }

        // ------------------------------------------------------------------ warnings and events

        int _feedSeen;
        readonly System.Collections.Generic.List<(int idx, int n)> _feedRows = new();

        bool ShownAsChip(string text)
        {
            string key = text.Length > 14 ? text.Substring(0, 14) : text;
            foreach (var w in S.Warnings) if (w.StartsWith(key)) return true;
            return false;
        }
        float[] _feedTime = new float[0];
        GUIStyle _chipWrap;
        const float ChipMaxW = 470;

        // ------------------------------------------------------------------ clickable chips

        readonly System.Collections.Generic.List<(float2 at, Building b, Citizen c)> _chipTargets = new();
        readonly System.Collections.Generic.Dictionary<string, int> _chipCycle = new();
        string _chipTargetName = "";

        /// <summary>The places a standing warning is about (by its wording, as the sim words it), for the chip's click.</summary>
        void ChipTargets(string w, System.Collections.Generic.List<(float2 at, Building b, Citizen c)> into)
        {
            into.Clear(); _chipTargetName = "";
            void People(System.Func<Citizen, bool> f, string name) { _chipTargetName = name; foreach (var c in S.Citizens) if (!c.Gone && f(c)) into.Add((c.Pos, null, c)); }
            void Places(System.Func<Building, bool> f, string name) { _chipTargetName = name; foreach (var b in S.Buildings) if (b.Active && f(b)) into.Add((b.Center, b, null)); }
            if (w.Contains("have no tools") || w.Contains("has no tools"))
            {
                _chipTargetName = "workplaces";
                foreach (var c in S.Citizens)
                    if (!c.Gone && c.Job != null && c.Job.Def.usesTools && c.Tool < 0 && !into.Exists(t => t.b == c.Job)) into.Add((c.Job.Center, c.Job, null));
            }
            else if (w.Contains("no home")) People(c => c.Home == null, "people without a home");
            else if (w.Contains("are sick")) People(c => c.Sick, "sick people");
            else if (w.Contains("unhappy")) People(c => c.UnhappyDays > 0, "unhappy people");
            else if (w.Contains("no clean water")) Places(b => b.Def.housing > 0 && b.Residents > 0 && !b.HasWater, "homes without water");
            else if (w.Contains("nearly full")) Places(b => b.IsStorage && b.Stored > S.Capacity(b) * 0.9f, "full stores");
            else if (w.StartsWith("Not enough firewood")) Places(b => b.Def.IsWorkplace && System.Array.Exists(b.Def.outputs, o => o.id == D.Balance.fuelResource), "woodcutters");
            else if (w.StartsWith("Expected harvest")) Places(b => b.Def.crop, "fields");
            else if (w.StartsWith("Unrest")) Places(b => b.Def.orderPower > 0, "councils");
            else if (w.StartsWith("Food") || w.StartsWith("There is no food"))
                Places(b => b.Def.IsWorkplace && !b.Def.crop && System.Array.Exists(b.Def.outputs, o => S.ResIndex(o.id) >= 0 && D.Resources[S.ResIndex(o.id)].isFood), "food workplaces");
        }

        /// <summary>A chip's tooltip: where a click goes (null when it has no place).</summary>
        public string ChipTipText(string w)
        {
            ChipTargets(w, _chipTargets);
            if (_chipTargets.Count == 0) return null;
            return _chipTargets.Count == 1 ? "Click to go there." : $"Click to go to the {_chipTargets.Count} {_chipTargetName}, one at each click.";
        }

        public void CycleChip(string w)
        {
            string key = w.Length > 14 ? w.Substring(0, 14) : w;
            ChipTargets(w, _chipTargets);
            if (_chipTargets.Count == 0) return;
            int i = _chipCycle.TryGetValue(key, out int k) ? (k + 1) % _chipTargets.Count : 0;
            _chipCycle[key] = i;
            var t = _chipTargets[i];
            if (t.b != null) Select(t.b); else if (t.c != null) SelectCitizen(t.c);
            JumpTo(t.at);
        }

        // Whether the next age can be reached now (looked up twice a second, not every frame: it builds lists; and at once
        // when more than a game hour has passed since, as when the game runs fast).
        bool _ageReady; float _ageReadyAt = -1, _ageReadySim = -1;
        bool AgeReady
        {
            get
            {
                if (S.NeedsCampfire) return false;
                if (_ageReadyAt < 0 || Time.unscaledTime - _ageReadyAt > 0.5f || math.abs(S.Time - _ageReadySim) > D.Balance.dayLengthSeconds / 24f)
                {
                    _ageReadyAt = Time.unscaledTime; _ageReadySim = S.Time; _ageReady = S.CanAdvanceAge();
                }
                return _ageReady;
            }
        }
        void AgeReadyRefresh() => _ageReadyAt = -1;

        void DrawFeed()
        {
            float y = TopBarHeight + 8;
            // Standing problems, as chips.
            // Good news first: everything the next age needs is in place (the age-up is a manual step in Goals).
            if (AgeReady && !EndOpen)
            {
                var next = D.Age(S.Age.next);
                var c = new GUIContent(Col($"Ready to advance to the {next?.name} (G)", Good));
                float wd = math.min(ChipMaxW, UiTheme.ChipGood.CalcSize(c).x + 4);
                var r = Box(new Rect(10, y, wd, 26), UiTheme.ChipGood);
                GUI.Label(r, c, UiTheme.ChipGood);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (!GoalsOpen) ToggleGoals(); }
                Tip(r, "Every key research topic and milestone is done. Open Goals (G) and press Advance when you are ready.");
                y += 30;
            }
            // Long ones (a cause and a remedy) wrap onto a second line.
            _chipWrap ??= new GUIStyle(UiTheme.Chip) { wordWrap = true, padding = new RectOffset(10, 10, 4, 4) };
            foreach (var w in S.Warnings)
            {
                bool bad = w.StartsWith("There is no food") || w.Contains("run out in 1 ") || w.Contains("run out in 2 ");
                var c = new GUIContent(Col(w, bad ? Bad : Warn));
                float one = UiTheme.Chip.CalcSize(c).x + 4;
                float wd = math.min(ChipMaxW, one);
                float hh = one <= ChipMaxW ? 26 : math.max(26, _chipWrap.CalcHeight(c, wd));
                var r = Box(new Rect(10, y, wd, hh), UiTheme.Chip);
                GUI.Label(r, c, one <= ChipMaxW ? UiTheme.Chip : _chipWrap);
                // Click: the camera goes to the buildings or people concerned, the next one at each click.
                ChipTargets(w, _chipTargets);
                if (_chipTargets.Count > 0)
                {
                    if (r.Contains(Event.current.mousePosition)) Fill(r, new Color(1, 1, 1, 0.04f));
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) CycleChip(w);
                    if (Over(r)) Tip(r, ChipTipText(w));
                }
                y += hh + 4;
            }
            // Recent events: newest at the bottom of a short list; they fade after a while. Click one to go there.
            if (_feedTime.Length < S.Events.Count) System.Array.Resize(ref _feedTime, S.Events.Count + 256);
            for (; _feedSeen < S.Events.Count; _feedSeen++) _feedTime[_feedSeen] = Time.unscaledTime;
            y += 6;
            // The last few events, newest at the bottom: a warning already standing as a chip is not repeated here, and
            // the same line coming again is shown once with a count.
            _feedRows.Clear();
            for (int i = S.Events.Count - 1; i >= 0 && _feedRows.Count < 7 && i >= S.Events.Count - 60; i--)
            {
                var e = S.Events[i];
                if (e.Severity != Severity.Bad && ShownAsChip(e.Text)) continue;   // (some chips are logged as Info)
                if (_feedRows.Count > 0 && S.Events[_feedRows[_feedRows.Count - 1].idx].Text == e.Text) { var last = _feedRows[_feedRows.Count - 1]; _feedRows[_feedRows.Count - 1] = (last.idx, last.n + 1); continue; }
                _feedRows.Add((i, 1));
            }
            for (int k = _feedRows.Count - 1; k >= 0; k--)
            {
                var (i, repeats) = _feedRows[k];
                var e = S.Events[i];
                float age = Time.unscaledTime - _feedTime[i];
                float a = math.saturate(1.4f - age / 40f);
                if (a <= 0.05f) continue;
                var col = e.Severity == Severity.Bad ? Bad : e.Severity == Severity.Warning ? Warn : Text;
                string line = repeats > 1 ? $"{e.Text}  {Col($"x{repeats}", Faint)}" : e.Text;
                var content = new GUIContent(line);
                var style = FeedRow;
                float wd = math.min(520, style.CalcSize(content).x + 26 + (e.HasPlace ? 14 : 0));
                float hh = math.max(24, style.CalcHeight(content, wd - 14 - (e.HasPlace ? 14 : 0)) + 6);
                var r = new Rect(10, y, wd, hh);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                Box(r, UiTheme.Header);
                Fill(new Rect(r.x + 1, r.y + 1, 4, r.height - 2), col);
                GUI.Label(new Rect(r.x + 12, r.y + 3, wd - 14 - (e.HasPlace ? 14 : 0), hh - 6), repeats > 1 ? Col(e.Text, col) + "  " + Col($"x{repeats}", Faint) : Col(e.Text, col), style);
                if (e.HasPlace) GUI.Label(new Rect(r.xMax - 16, r.y + 2, 12, hh - 4), Col("›", Dim), style);   // clickable: goes there
                if (e.HasPlace && GUI.Button(r, GUIContent.none, GUIStyle.none)) JumpTo(e.Place);
                if (e.HasPlace && Over(r)) Tip(r, $"Day {e.Day + 1}. Click to go there.");
                GUI.color = old;
                y += hh + 3;
            }
        }
    }
}
