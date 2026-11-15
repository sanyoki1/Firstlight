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
            Gap(2);
            GUI.Label(new Rect(_lx, _ly, 200, 20), "Workplace", Small);
            GUI.Label(new Rect(_lx + 200, _ly, 80, 20), "Workers", Small);
            GUI.Label(new Rect(_lx + 290, _ly, 150, 20), "Priority", Small);
            _ly += 22;
            foreach (var g in S.Buildings.Where(b => b.Active && b.Def.IsWorkplace).GroupBy(b => b.Def).OrderBy(g => g.Key.category).ThenBy(g => g.Key.name))
            {
                int w = g.Sum(b => b.Workers), max = g.Sum(b => b.Def.maxWorkers);
                int hi = g.Count(b => b.Priority == Priority.High), lo = g.Count(b => b.Priority == Priority.Low), paused = g.Count(b => b.Paused);
                var row = new Rect(_lx - 6, _ly, _lw + 12, 24);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { var first = g.First(); Select(first); JumpTo(first.Center); }
                if (row.Contains(Event.current.mousePosition)) Fill(row, new Color(1, 1, 1, 0.05f));
                GUI.Label(new Rect(_lx, _ly, 200, 24), g.Count() > 1 ? $"{g.Key.name} x{g.Count()}" : g.Key.name, Label);
                Bar(new Rect(_lx + 200, _ly + 7, 50, 10), w / (float)math.max(1, max), w == max ? Good : w == 0 ? Bad : Warn);
                GUI.Label(new Rect(_lx + 254, _ly, 40, 24), $"{w}/{max}", Small);
                string p = (hi > 0 ? Col($"{hi} high ", Accent) : "") + (lo > 0 ? Col($"{lo} low ", Dim) : "") + (paused > 0 ? Col($"{paused} paused", Warn) : "");
                GUI.Label(new Rect(_lx + 300, _ly, 150, 24), p.Length > 0 ? p : Col("normal", Faint), Small);
                _ly += 25;
            }
            Gap(4);
            Para(Col("Click a row to select it. Priorities and pausing are set on each building.", Dim));
            _jobsH = _ly - r.y + 8;
        }

        // ------------------------------------------------------------------ stocks

        void DrawStocks()
        {
            var r = BeginSide("Goods in store", 400, _stocksH, () => StocksOpen = false);
            BarRow("Storage", S.StorageUsed / math.max(1, S.StorageCapacity), S.StorageUsed > S.StorageCapacity * 0.9f ? Warn : Info, $"{S.StorageUsed:0}/{S.StorageCapacity}");
            Gap(4);
            GUI.Label(new Rect(_lx, _ly, 180, 20), "Good", Small);
            GUI.Label(new Rect(_lx + 180, _ly, 80, 20), "In store", new GUIStyle(Small) { alignment = TextAnchor.MiddleRight });
            GUI.Label(new Rect(_lx + 270, _ly, 90, 20), "Last day", new GUIStyle(Small) { alignment = TextAnchor.MiddleRight });
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
            var r = Box(new Rect(VW / 2 - 330, VH / 2 - 390, 660, 780));
            GUI.Label(new Rect(r.x + 20, r.y + 12, 400, 28), "Controls", Title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 12, 24, 22), "x", Button)) HelpOpen = false;
            string[,] keys =
            {
                { "W A S D, arrows", "Move the camera (or push the pointer to the screen edge)" },
                { "Q / E, right-drag", "Turn and tilt the camera" },
                { "Mouse wheel, middle-drag", "Zoom, pan" },
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
            float y = r.y + 52;
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                GUI.Label(new Rect(r.x + 24, y, 210, 26), $"<b>{keys[i, 0]}</b>", Label);
                GUI.Label(new Rect(r.x + 240, y, 400, 26), keys[i, 1], Label);
                y += 28;
            }
            GUI.Label(new Rect(r.x + 24, y + 2, 612, 22), Col("Hover any number for what moves it; click an event (›) to go there.", Dim), Small);
            if (GUI.Button(new Rect(r.x + 24, r.yMax - 46, 160, 30), "Game menu (Esc)", Button)) { HelpOpen = false; OpenMenu(); }
            GUI.Label(new Rect(r.x + 200, r.yMax - 46, 440, 30), Col("Saves itself at the start of every season.", Faint), Small);
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

        // Whether the next age can be reached now (looked up twice a second, not every frame: it builds lists).
        bool _ageReady; float _ageReadyAt = -1;
        bool AgeReady
        {
            get
            {
                if (S.NeedsCampfire) return false;
                if (_ageReadyAt < 0 || Time.unscaledTime - _ageReadyAt > 0.5f) { _ageReadyAt = Time.unscaledTime; _ageReady = S.CanAdvanceAge(); }
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
                if (e.Severity == Severity.Warning && ShownAsChip(e.Text)) continue;
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
                var style = new GUIStyle(Label) { fontSize = 14, wordWrap = true };
                float wd = math.min(520, style.CalcSize(content).x + 26);
                float hh = math.max(24, style.CalcHeight(content, wd - 14) + 6);
                var r = new Rect(10, y, wd, hh);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                Box(r, UiTheme.Header);
                Fill(new Rect(r.x + 1, r.y + 1, 4, r.height - 2), col);
                GUI.Label(new Rect(r.x + 12, r.y + 3, wd - 14, hh - 6), repeats > 1 ? Col(e.Text, col) + "  " + Col($"x{repeats}", Faint) : Col(e.Text, col), style);
                if (e.HasPlace && GUI.Button(r, GUIContent.none, GUIStyle.none)) JumpTo(e.Place);
                if (e.HasPlace) Tip(r, $"Day {e.Day + 1}. Click to go there.");
                GUI.color = old;
                y += hh + 3;
            }
        }
    }
}
