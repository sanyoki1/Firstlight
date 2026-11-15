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
            public BuildingDef Def; public Building First, Short; public int N, W, Max, Hi, Lo, Paused; public string Note;
            // The row's texts, kept while they come out the same (TextLine).
            public string NameTxt, CountTxt, PrioTxt, NoteTxt;
            public void Reset(BuildingDef d) { Def = d; First = Short = null; N = W = Max = Hi = Lo = Paused = 0; Note = null; }
        }
        readonly List<JobRow> _jobRowPool = new();
        readonly List<JobRow> _jobRowsSorted = new();
        readonly Dictionary<BuildingDef, JobRow> _jobRowOf = new();
        string _jobsHead, _jobsPlaces, _jobsNoHaul, _jobsFoot;
        // Rows in the order of category, then name (as OrderBy(category).ThenBy(name) sorted them; a row is one kind of building).
        static readonly System.Comparison<JobRow> JobOrder = (a, b) =>
        {
            int c = string.Compare(a.Def.category, b.Def.category);
            return c != 0 ? c : string.Compare(a.Def.name, b.Def.name);
        };
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
            int adults = _tAdults, jobs = 0, builders = 0, sick = 0;
            foreach (var c in S.Citizens)
            {
                if (c.Gone) continue;
                if (c.Job != null) jobs++;
                if (c.IsBuilder) builders++;
                if (c.Sick && c.Age >= D.Balance.adultAge) sick++;
            }
            int free = adults - jobs - builders - sick;
            var (onlyAdults, elders, kids) = PeopleSplit();
            _tl.Begin().I(onlyAdults).S(" adults and ").I(elders).S(" elders: ").I(jobs).S(" in jobs, ").I(builders).S(" building, ")
               .C(free > 0 ? Good : Dim).I(free).S(" free").E().S(" (they haul goods), ").I(sick).S(" sick. ").I(kids).S(" children.");
            Para(_tl.To(ref _jobsHead), Wrap);
            // Places that want workers now (not paused, flooded or out of season), how many are filled, and who carries.
            int places = 0, filled = 0, hiWorkers = 0, normalWorkers = 0;
            foreach (var b in S.Buildings)
            {
                if (!b.Active || !b.Def.IsWorkplace) continue;
                if (b.Priority == Priority.High) hiWorkers += b.Workers; else if (b.Priority == Priority.Normal) normalWorkers += b.Workers;
                if (JobIdle(b) != null) continue;
                places += b.Def.maxWorkers; filled += b.Workers;
            }
            _tl.Begin().S("Places: ").I(places).S(", filled ").I(filled);
            if (places > filled) _tl.C(Warn).S(" · short of ").I(places - filled).S(places - filled == 1 ? " worker" : " workers").E();
            else _tl.C(Good).S(" · all filled").E();
            Row(_tl.To(ref _jobsPlaces), Label);
            if (free <= 0) Row(_jobsNoHaul ??= Col("Nobody is free to haul: goods wait at workplaces", Warn), Label);
            Gap(2);
            GUI.Label(new Rect(_lx, _ly, 200, 20), "Workplace", Small);
            GUI.Label(new Rect(_lx + 200, _ly, 80, 20), "Workers", Small);
            GUI.Label(new Rect(_lx + 312, _ly, 130, 20), "Priority", Small);
            _ly += 22;
            // The rows (gathered first, so a list taller than the screen scrolls instead of running off the bottom).
            // One row per kind of workplace (no LINQ: the rows are pooled per kind and sorted in place).
            int nRows = 0;
            float content = 0;
            _jobRowOf.Clear(); _jobRowsSorted.Clear();
            foreach (var b in S.Buildings)
            {
                if (!b.Active || !b.Def.IsWorkplace) continue;
                if (!_jobRowOf.TryGetValue(b.Def, out var jr))
                {
                    if (nRows == _jobRowPool.Count) _jobRowPool.Add(new JobRow());
                    jr = _jobRowPool[nRows++]; jr.Reset(b.Def);
                    _jobRowOf[b.Def] = jr; _jobRowsSorted.Add(jr);
                }
                jr.First ??= b; jr.N++;
                jr.W += b.Workers; jr.Max += b.Def.maxWorkers;
                if (b.Priority == Priority.High) jr.Hi++; else if (b.Priority == Priority.Low) jr.Lo++;
                if (b.Paused) jr.Paused++;
                if (jr.Short == null && b.Workers < b.Def.maxWorkers) jr.Short = b;
            }
            _jobRowsSorted.Sort(JobOrder);
            foreach (var jr in _jobRowsSorted)
            {
                jr.Note = JobNote(jr.Def, jr.Short ?? jr.First, jr.W, jr.Max, free, hiWorkers, normalWorkers, builders, jr);
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
                var jr = _jobRowsSorted[k];
                var row = new Rect(_lx - 6, _ly, _lw + 12, jr.Note != null ? 42 : 24);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { Select(jr.First); JumpTo(jr.First.Center); }
                if (row.Contains(Event.current.mousePosition)) Fill(row, new Color(1, 1, 1, 0.05f));
                GUI.Label(new Rect(_lx, _ly, 200, 24), jr.N > 1 ? _tl.Begin().S(jr.Def.name).S(" x").I(jr.N).To(ref jr.NameTxt) : jr.Def.name, Label);
                // An empty bar draws no colour, so the count carries it: red at none, amber short, green full.
                var wc = jr.W >= jr.Max ? Good : jr.W == 0 ? Bad : Warn;
                if (jr.W == 0) Fill(new Rect(_lx + 200, _ly + 7, 50, 10), new Color(Bad.r, Bad.g, Bad.b, 0.28f));
                else Bar(new Rect(_lx + 200, _ly + 7, 50, 10), jr.W / (float)math.max(1, jr.Max), wc);
                GUI.Label(new Rect(_lx + 256, _ly, 52, 24), _tl.Begin().C(wc).I(jr.W).S("/").I(jr.Max).E().To(ref jr.CountTxt), Label);
                _tl.Begin();
                if (jr.Hi > 0) _tl.C(Accent).I(jr.Hi).S(" high ").E();
                if (jr.Lo > 0) _tl.C(Dim).I(jr.Lo).S(" low ").E();
                if (jr.Paused > 0) _tl.C(Warn).I(jr.Paused).S(" paused").E();
                if (jr.Hi == 0 && jr.Lo == 0 && jr.Paused == 0) _tl.C(Dim).S("normal").E();
                GUI.Label(new Rect(_lx + 312, _ly, _lw - 312, 24), _tl.To(ref jr.PrioTxt), Small);
                _ly += 24;
                if (jr.Note != null) { GUI.Label(new Rect(_lx + 12, _ly - 2, _lw - 12, 18), jr.Note, Small); _ly += 18; }
                _ly += 1;
            }
            if (scroll)
            {
                GUI.EndScrollView();
                _lx = lx0; _lw = lw0; _ly = ly0 + avail;
            }
            Gap(4);
            Para(_jobsFoot ??= Col("Click a row to select it. Priorities and pausing are set on each building.", Dim));
            _jobsH = _ly - r.y + 8;
        }

        /// <summary>Why a workplace wants nobody now (its status as the sim sets it), or null when it takes workers.</summary>
        static string JobIdle(Building b) => b.Paused ? "paused" : b.Flooded ? "flooded" : b.Status switch
        {
            "Nothing to do this season" => "nothing to do this season",
            "The harvest is in" => "the harvest is in",
            _ => null,
        };

        readonly List<string> _before = new(3);

        /// <summary>
        /// The faint line under a Jobs row: why it is short of workers (paused, out of season, or who came first: jobs fill
        /// High first, then builders, then Normal with fields first, then Low), or what stops the workers it has. Returned
        /// in Dim, kept in the row while it reads the same.
        /// </summary>
        string JobNote(BuildingDef d, Building b, int w, int max, int free, int hiWorkers, int normalWorkers, int builders, JobRow row)
        {
            if (b == null) return null;
            string idle = JobIdle(b);
            var t = _tl.Begin().C(Dim);
            if (idle != null)
            {
                if (w >= max) return null;
                t.Ch(char.ToUpper(idle[0])).S(idle, 1);          // Cap(idle)
            }
            else if (w < max)
            {
                if (free > 0) return null;                         // filled at the next shift change
                var before = _before; before.Clear();
                if (b.Priority < Priority.High && hiWorkers > 0) before.Add("High jobs");
                if (b.Priority < Priority.High && builders > 0) before.Add("builders");
                if (b.Priority == Priority.Low && normalWorkers > 0) before.Add("Normal jobs");
                if (before.Count == 0)
                {
                    if (w != 0) return null;
                    t.S("Nobody left to take it: more people, or raise its priority");
                }
                else if (before.Count == 1) t.S("Short: ").S(before[0] == "High jobs" ? "High-priority jobs" : before[0]).S(" take everyone first");
                else
                {
                    t.S("Short: taken first by ");
                    for (int i = 0; i < before.Count - 1; i++) { if (i > 0) t.S(", "); t.S(before[i]); }
                    t.S(" and ").S(before[before.Count - 1]);
                }
            }
            else
            {
                string s = b.Status switch
                {
                    "Needs tools" => "Its workers need tools",
                    "Waiting for inputs" => "Waiting for its inputs",
                    "Waiting for goods to be carried off" => "Full: waiting for haulers",
                    _ => null,
                };
                if (s == null) return null;
                t.S(s);
            }
            return t.E().To(ref row.NoteTxt);
        }

        // ------------------------------------------------------------------ stocks

        void DrawStocks()
        {
            var r = BeginSide("Goods in store", 400, _stocksH, () => StocksOpen = false);
            float used = S.StorageUsed; int capacity = S.StorageCapacity;
            BarRow("Storage", used / math.max(1, capacity), used > capacity * 0.9f ? Warn : Info, _tl.Begin().F(used, "0").S("/").I(capacity).To(ref _storageTxt));
            Gap(4);
            GUI.Label(new Rect(_lx, _ly, 180, 20), "Good", Small);
            GUI.Label(new Rect(_lx + 180, _ly, 80, 20), "In store", SmallRight);
            GUI.Label(new Rect(_lx + 270, _ly, 90, 20), "Last day", SmallRight);
            _ly += 22;
            if (_goodsName == null || _goodsName.Length != S.ResCount)
            {
                _goodsName = new string[S.ResCount]; _goodsAmt = new string[S.ResCount + 1]; _goodsTrend = new string[S.ResCount + 1];
                for (int i = 0; i < S.ResCount; i++) _goodsName[i] = D.Resources[i].name + (D.Resources[i].isFood ? Col("  food", Faint) : "");
            }
            for (int i = 0; i < S.ResCount; i++)
            {
                float amt = S.StockTotals[i];
                if (amt < 0.05f && S.Produced[i] < 0.05f) continue;
                GUI.Label(new Rect(_lx, _ly, 180, 22), _goodsName[i], Label);
                GUI.Label(new Rect(_lx + 180, _ly, 80, 22), _tl.Begin().S("<b>").F(amt, "0").S("</b>").To(ref _goodsAmt[i]), Right);
                GUI.Label(new Rect(_lx + 270, _ly, 90, 22), TrendText(Trend(i), ref _goodsTrend[i]), Right);
                _ly += 23;
            }
            // Tools of all kinds together: the top bar's "Tools" figure.
            float tools = ToolsInStore();
            if (tools >= 0.05f)
            {
                GUI.Label(new Rect(_lx, _ly, 180, 22), _goodsTools ??= Col("Tools (all kinds)", Dim), Label);
                GUI.Label(new Rect(_lx + 180, _ly, 80, 22), _tl.Begin().S("<b>").F(tools, "0").S("</b>").To(ref _goodsAmt[S.ResCount]), Right);
                GUI.Label(new Rect(_lx + 270, _ly, 90, 22), TrendText(ToolTrend(), ref _goodsTrend[S.ResCount]), Right);
                _ly += 23;
            }
            _stocksH = _ly - r.y + 10;
        }

        string _storageTxt, _goodsTools;
        string[] _goodsName, _goodsAmt, _goodsTrend;

        /// <summary>"-" (faint) for no change, else "+1.2" / "-0.4" in green / red.</summary>
        string TrendText(float t, ref string cache)
        {
            if (math.abs(t) < 0.05f) return _goodsFlat ??= Col("-", Faint);
            return _tl.Begin().C(t > 0 ? Good : Bad).F(t, "+0.#;-0.#").E().To(ref cache);
        }
        static string _goodsFlat;

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

        /// <summary>What a standing warning is about, by its wording as the sim words it (0: no place to go).</summary>
        static int ChipKind(string w)
        {
            if (w.Contains("have no tools") || w.Contains("has no tools")) return 1;
            if (w.Contains("no home")) return 2;
            if (w.Contains("are sick")) return 3;
            if (w.Contains("unhappy")) return 4;
            if (w.Contains("no clean water")) return 5;
            if (w.Contains("nearly full")) return 6;
            if (w.StartsWith("Not enough firewood")) return 7;
            if (w.StartsWith("Expected harvest")) return 8;
            if (w.StartsWith("Unrest")) return 9;
            if (w.StartsWith("Food") || w.StartsWith("There is no food")) return 10;
            return 0;
        }

        static readonly string[] ChipTargetNames = { "", "workplaces", "people without a home", "sick people", "unhappy people", "homes without water", "full stores", "woodcutters", "fields", "councils", "food workplaces" };

        bool ChipPerson(int kind, Citizen c) => kind == 2 ? c.Home == null : kind == 3 ? c.Sick : kind == 4 && c.UnhappyDays > 0;

        bool ChipPlace(int kind, Building b)
        {
            switch (kind)
            {
                case 5: return b.Def.housing > 0 && b.Residents > 0 && !b.HasWater;
                case 6: return b.IsStorage && b.Stored > S.Capacity(b) * 0.9f;
                case 7:
                    if (!b.Def.IsWorkplace) return false;
                    foreach (var o in b.Def.outputs) if (o.id == D.Balance.fuelResource) return true;
                    return false;
                case 8: return b.Def.crop;
                case 9: return b.Def.orderPower > 0;
                case 10:
                    if (!b.Def.IsWorkplace || b.Def.crop) return false;
                    foreach (var o in b.Def.outputs) { int r = S.ResIndex(o.id); if (r >= 0 && D.Resources[r].isFood) return true; }
                    return false;
            }
            return false;
        }

        /// <summary>
        /// The places a standing warning is about (by its wording, as the sim words it), for the chip's click. With firstOnly
        /// it stops at the first (the chip only needs to know whether it has a place). No delegates: it runs on every call.
        /// </summary>
        void ChipTargets(string w, System.Collections.Generic.List<(float2 at, Building b, Citizen c)> into, bool firstOnly = false)
        {
            into.Clear();
            int kind = ChipKind(w);
            _chipTargetName = ChipTargetNames[kind];
            if (kind == 1)
            {
                foreach (var c in S.Citizens)
                {
                    if (c.Gone || c.Job == null || !c.Job.Def.usesTools || c.Tool >= 0) continue;
                    bool listed = false;
                    foreach (var t in into) if (t.b == c.Job) { listed = true; break; }
                    if (listed) continue;
                    into.Add((c.Job.Center, c.Job, null));
                    if (firstOnly) return;
                }
            }
            else if (kind >= 2 && kind <= 4)
            {
                foreach (var c in S.Citizens) if (!c.Gone && ChipPerson(kind, c)) { into.Add((c.Pos, null, c)); if (firstOnly) return; }
            }
            else if (kind >= 5)
            {
                foreach (var b in S.Buildings) if (b.Active && ChipPlace(kind, b)) { into.Add((b.Center, b, null)); if (firstOnly) return; }
            }
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

        // The chips' and feed rows' text and sizes, made when a warning or event first shows (and again when the text size
        // changes), not on every call: each used to be a new coloured string, a new GUIContent and a text measurement.
        sealed class ChipLook { public string Text; public float One, Height; }
        readonly System.Collections.Generic.Dictionary<string, ChipLook> _chipLooks = new();
        int _chipLookFont = -1;
        string _readyText, _readyFor; float _readyW; int _readyFont = -1;
        sealed class FeedLook { public string Text, Src; public float W, H; public int Repeats; }
        readonly System.Collections.Generic.Dictionary<int, FeedLook> _feedLooks = new();
        int _feedBuiltCount = -1;
        readonly System.Collections.Generic.List<string> _feedBuiltWarnings = new();

        ChipLook LookOf(string w)
        {
            if (_chipLookFont != UiTheme.Chip.fontSize) { _chipLooks.Clear(); _chipLookFont = UiTheme.Chip.fontSize; }
            if (_chipLooks.TryGetValue(w, out var look)) return look;
            if (_chipLooks.Count > 64) _chipLooks.Clear();
            bool bad = w.StartsWith("There is no food") || w.Contains("run out in 1 ") || w.Contains("run out in 2 ");
            var c = new GUIContent(Col(w, bad ? Bad : Warn));
            float one = UiTheme.Chip.CalcSize(c).x + 4;
            float wd = math.min(ChipMaxW, one);
            look = new ChipLook { Text = c.text, One = one, Height = one <= ChipMaxW ? 26 : math.max(26, _chipWrap.CalcHeight(c, wd)) };
            _chipLooks[w] = look;
            return look;
        }

        /// <summary>Whether the feed's rows must be gathered again: a new event, or the standing warnings changed.</summary>
        bool FeedStale()
        {
            bool same = _feedBuiltCount == S.Events.Count && _feedBuiltWarnings.Count == S.Warnings.Count;
            for (int i = 0; same && i < S.Warnings.Count; i++) same = string.Equals(_feedBuiltWarnings[i], S.Warnings[i]);
            if (same) return false;
            _feedBuiltCount = S.Events.Count;
            _feedBuiltWarnings.Clear(); _feedBuiltWarnings.AddRange(S.Warnings);
            return true;
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
                if (_readyText == null || _readyFor != next?.name || _readyFont != UiTheme.ChipGood.fontSize)
                {
                    var c = new GUIContent(Col($"Ready to advance to the {next?.name} (G)", Good));
                    _readyText = c.text; _readyFor = next?.name; _readyFont = UiTheme.ChipGood.fontSize;
                    _readyW = math.min(ChipMaxW, UiTheme.ChipGood.CalcSize(c).x + 4);
                }
                var r = Box(new Rect(10, y, _readyW, 26), UiTheme.ChipGood);
                GUI.Label(r, _readyText, UiTheme.ChipGood);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { if (!GoalsOpen) ToggleGoals(); }
                Tip(r, "Every key research topic and milestone is done. Open Goals (G) and press Advance when you are ready.");
                y += 30;
            }
            // Long ones (a cause and a remedy) wrap onto a second line.
            _chipWrap ??= new GUIStyle(UiTheme.Chip) { wordWrap = true, padding = new RectOffset(10, 10, 4, 4) };
            foreach (var w in S.Warnings)
            {
                var look = LookOf(w);
                float wd = math.min(ChipMaxW, look.One);
                float hh = look.Height;
                var r = Box(new Rect(10, y, wd, hh), UiTheme.Chip);
                GUI.Label(r, look.Text, look.One <= ChipMaxW ? UiTheme.Chip : _chipWrap);
                // Click: the camera goes to the buildings or people concerned, the next one at each click.
                ChipTargets(w, _chipTargets, true);
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
            // the same line coming again is shown once with a count. Gathered again only when an event comes in or the
            // chips change.
            if (FeedStale())
            {
                _feedRows.Clear();
                for (int i = S.Events.Count - 1; i >= 0 && _feedRows.Count < 7 && i >= S.Events.Count - 60; i--)
                {
                    var e = S.Events[i];
                    if (e.Severity != Severity.Bad && ShownAsChip(e.Text)) continue;   // (some chips are logged as Info)
                    if (_feedRows.Count > 0 && S.Events[_feedRows[_feedRows.Count - 1].idx].Text == e.Text) { var last = _feedRows[_feedRows.Count - 1]; _feedRows[_feedRows.Count - 1] = (last.idx, last.n + 1); continue; }
                    _feedRows.Add((i, 1));
                }
            }
            if (_feedLooks.Count > 32) _feedLooks.Clear();
            for (int k = _feedRows.Count - 1; k >= 0; k--)
            {
                var (i, repeats) = _feedRows[k];
                var e = S.Events[i];
                float age = Time.unscaledTime - _feedTime[i];
                float a = math.saturate(1.4f - age / 40f);
                if (a <= 0.05f) continue;
                var col = e.Severity == Severity.Bad ? Bad : e.Severity == Severity.Warning ? Warn : Text;
                var style = FeedRow;
                if (!_feedLooks.TryGetValue(i, out var look) || look.Repeats != repeats || !ReferenceEquals(look.Src, e.Text))
                {
                    string line = repeats > 1 ? $"{e.Text}  {Col($"x{repeats}", Faint)}" : e.Text;
                    var content = new GUIContent(line);
                    float w0 = math.min(520, style.CalcSize(content).x + 26 + (e.HasPlace ? 14 : 0));
                    look = new FeedLook
                    {
                        Repeats = repeats, Src = e.Text, W = w0,
                        H = math.max(24, style.CalcHeight(content, w0 - 14 - (e.HasPlace ? 14 : 0)) + 6),
                        Text = repeats > 1 ? Col(e.Text, col) + "  " + Col($"x{repeats}", Faint) : Col(e.Text, col),
                    };
                    _feedLooks[i] = look;
                }
                float wd = look.W, hh = look.H;
                var r = new Rect(10, y, wd, hh);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                Box(r, UiTheme.Header);
                Fill(new Rect(r.x + 1, r.y + 1, 4, r.height - 2), col);
                GUI.Label(new Rect(r.x + 12, r.y + 3, wd - 14 - (e.HasPlace ? 14 : 0), hh - 6), look.Text, style);
                if (e.HasPlace) GUI.Label(new Rect(r.xMax - 16, r.y + 2, 12, hh - 4), _feedArrow ??= Col("›", Dim), style);   // clickable: goes there
                if (e.HasPlace && GUI.Button(r, GUIContent.none, GUIStyle.none)) JumpTo(e.Place);
                if (e.HasPlace && Over(r)) Tip(r, $"Day {e.Day + 1}. Click to go there.");
                GUI.color = old;
                y += hh + 3;
            }
        }

        static string _feedArrow;
    }
}
