using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Top bar, two rows. Row one: age and calendar, speed, people, food, research, panel buttons. Row two: the key
    // stocks with daily trends, then the needs that have arrived and unrest. Every figure has a tooltip saying what it
    // is and what moves it. Row two keeps its order and drops from the right when the screen is narrow.
    public sealed partial class Hud
    {
        float[] _daySnap, _dayDelta;
        int _snapDay = -1;
        float _knowSnap, _knowPerDay, _foodSnap, _foodDelta;

        /// <summary>Height of both rows: panels and the event feed start below it.</summary>
        public const float TopBarHeight = 84;

        // Stocks in the second row, most important first (tools = every tier together). Later goods appear once made.
        static readonly string[] KeyStocks = { "wood", "firewood", "stone", "tools", "hides", "clothing", "herbs", "grain", "clay", "pottery", "copper", "bronze" };
        static readonly int AlwaysShown = 4;

        static readonly System.Collections.Generic.Dictionary<string, string> StockTips = new()
        {
            ["wood"] = "Wood: for building and rebuilding. Woodcutters make it (and firewood); every site takes it.",
            ["firewood"] = "Firewood: burned by every lived-in home and the campfire each day, most in winter. Woodcutters split it; a woodcutter can be set to firewood only.",
            ["stone"] = "Stone: for sturdier buildings and paths. Stone gatherers and mining pits take it from rock.",
            ["tools"] = "Tools of every kind (stone, copper, bronze). Workers wear them out; a toolmaker or smiths make more. Without one, tool users work slower, and fields and mines not at all.",
            ["hides"] = "Hides: from hunters and pens; hide workers make clothing from them, and hide tents need them.",
            ["clothing"] = "Clothing: put on in the cold seasons and worn out over the winter; people without it are colder.",
            ["herbs"] = "Herbs: gathered by the herbalist; healers use them to treat the sick.",
            ["grain"] = "Grain: the harvest from the fields, eaten through winter and spring. Keeps best in a granary.",
            ["clay"] = "Clay: dug at a clay pit on the river's clay beds; potters fire it into pottery.",
            ["pottery"] = "Pottery: pots in store, not yet handed out. Every household keeps pots in use and they wear out; potters make more. (Pots on the right: how many people have pots at home.)",
            ["copper"] = "Copper: smelted from copper ore; coppersmiths make copper tools from it.",
            ["bronze"] = "Bronze: copper with a little tin, cast at a foundry; bronze smiths make the best tools.",
        };

        /// <summary>Change over the last full day per resource (for the trend arrows and the stock list).</summary>
        void SampleTrends()
        {
            if (S.NeedsCampfire) return;
            int n = S.ResCount;
            if (_daySnap == null) { _daySnap = new float[n]; _dayDelta = new float[n]; }
            if (S.Day == _snapDay) return;
            if (_snapDay >= 0)
            {
                for (int r = 0; r < n; r++) _dayDelta[r] = (S.StockTotals[r] - _daySnap[r]) / math.max(1, S.Day - _snapDay);
                _knowPerDay = (S.KnowledgeTotal - _knowSnap) / math.max(1, S.Day - _snapDay);
                _foodDelta = (S.FoodStock - _foodSnap) / math.max(1, S.Day - _snapDay);
            }
            System.Array.Copy(S.StockTotals, _daySnap, n);
            _knowSnap = S.KnowledgeTotal; _foodSnap = S.FoodStock;
            _snapDay = S.Day;
        }

        public float Trend(int r) => _dayDelta == null || r < 0 ? 0 : _dayDelta[r];
        public float KnowledgePerDay => _knowPerDay;

        static string _arrowUp, _arrowDown, _arrowFlat;
        static string Arrow(float d, float big)
        {
            if (d > big) return _arrowUp ??= Col(" ↑", Good);
            if (d < -big) return _arrowDown ??= Col(" ↓", Bad);
            return _arrowFlat ??= Col(" ·", Faint);
        }

        // Labels are built in one reused buffer and the string from last time is kept while the text is the same (TextLine):
        // the top bar used to make a few dozen new strings on every call. One cache field per label.
        readonly TextLine _tl = new();
        string _calTxt, _popTxt, _popTxt2, _foodTxt, _foodTxt2, _resTxt, _resTxt2, _resTxt3, _autoTxt;
        readonly string[] _stockTxt = new string[KeyStocks.Length];
        static readonly float[] Speeds = { 0, 1, 2, 4 };
        static readonly string[] SpeedNames = { "II", "1x", "2x", "4x" };
        static readonly string[] PanelLabels = { "Research", "Goals", "Jobs", "Events", "Help" };
        static readonly string[] PanelKeys = { "R", "G", "J", "V", "F1" };
        static readonly string[] PanelTips = { "Research tree: choose what to learn next.", "Goals: the key research and milestones for the next age.", "Jobs: how many work where; set priorities on each building.",
                                               "Events: everything that has happened, newest first (who died and of what, who left, shortages), and Topics: the hints seen so far.", "Controls and keys." };
        static string[] _panelLabelsKeyed;

        void AppendClock(TextLine t)
        {
            float h = S.DayFraction * 24f;
            t.I((int)h, "00").S(":").I((int)((h - (int)h) * 60) / 10 * 10, "00");
        }

        void DrawTopBar()
        {
            Box(new Rect(0, 0, VW, 52), UiTheme.Header);
            Box(new Rect(0, 52, VW, TopBarHeight - 52), UiTheme.Panel);
            float x = 14;

            // Age and calendar.
            var ar = new Rect(x, 5, 250, 42);
            GUI.Label(new Rect(x, 5, 250, 22), S.Age?.name ?? "", Title);
            _tl.Begin().S("Year ").I(S.Year + 1).S(", ").S(S.SeasonName).S(" day ").I(S.DayOfSeason + 1).S("  ·  "); AppendClock(_tl);
            GUI.Label(new Rect(x, 27, 250, 20), _tl.To(ref _calTxt), Small);
            if (Over(ar)) Tip(ar, $"The age the settlement has reached, and the calendar: {D.Balance.daysPerSeason} days a season, a year of four seasons. Winter is cold and nothing grows; spring floods can come. Goals (G) show what the next age needs.");
            x += 252;

            // Speed.
            var speeds = Speeds; var names = SpeedNames;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x + i * 42, 11, 38, 30);
                bool on = math.abs(S.Speed - speeds[i]) < 0.01f;
                if (GUI.Button(r, names[i], on ? ButtonOn : Button)) G.SetSpeed(speeds[i]);
                if (Over(r)) Tip(r, i == 0 ? "Pause (Space). You can still build and give orders while paused." : $"Game speed {names[i]} (key {i}).");
            }
            x += 4 * 42 + 16;

            // People.
            var (adults, elders, kids) = PeopleSplit();
            int free = _tHousing - _tPop;
            var pr = new Rect(x, 5, 236, 42);
            _tl.Begin().S("<b>").I(_tPop).S("</b> people  ");
            if (_tHomeless > 0) _tl.C(Warn).I(_tHomeless).S(" homeless").E();
            else if (free > 0) _tl.C(Dim).I(free).S(free == 1 ? " bed free" : " beds free").E();
            else _tl.C(Dim).S("no free beds").E();
            GUI.Label(new Rect(x, 5, 236, 22), _tl.To(ref _popTxt), Label);
            GUI.Label(new Rect(x, 27, 236, 20), _tl.Begin().I(adults).S(adults == 1 ? " adult · " : " adults · ").I(kids).S(kids == 1 ? " child · " : " children · ").I(elders).S(elders == 1 ? " elder" : " elders").To(ref _popTxt2), Small);
            if (Over(pr)) Tip(pr, $"People: {S.Population}, housing for {S.HousingCapacity}. Babies are born to families with a home, free beds and food in store; migrants come when there are free beds, food and good spirits. Unhappy or starving people leave.\n\n" +
                    $"Happiness {S.AverageHappiness:P0}: the average and the worst of food, warmth, shelter, health (and later water, pots at home and faith). It sets how fast people work.\n\nBorn {S.Births}, arrived {S.Migrants}, left {S.Leavers}, died {S.TotalDeaths}." +
                    (S.TotalDeaths > 0 ? $"\nDeaths: {DeathsByCause()}. Events (V) says who." : ""));
            x += 240;

            // Food.
            float fd = TFoodDays;
            var fcol = fd < S.Data.Balance.foodWarnDays ? Bad : fd < 8 ? Warn : Good;
            var fr = new Rect(x, 5, 150, 42);
            _tl.Begin().S("Food ").C(fcol);
            if (fd >= 99) _tl.S("-");
            else { int at = _tl.Mark; _tl.F(fd, "0.#"); _tl.S(_tl.Is(at, "1") ? " day" : " days"); }      // FoodDaysText
            GUI.Label(new Rect(x, 5, 150, 22), _tl.E().To(ref _foodTxt), Label);
            GUI.Label(new Rect(x, 27, 150, 20), _tl.Begin().F(_tFoodStock, "0").S(" in store").S(Arrow(_foodDelta, 0.5f)).To(ref _foodTxt2), Small);
            if (Over(fr)) Tip(fr, FoodTip());
            if (GUI.Button(fr, GUIContent.none, GUIStyle.none)) ToggleStocks();

            // Research progress (right side), then the panel buttons.
            // Narrow (a large interface size): smaller panel buttons without their keys and a shorter research box, so
            // they don't run over the food figure.
            bool narrow = VW - 5 * 100 - 6 - 236 < x + 156;
            float bw = narrow ? 80 : 100, rw = narrow ? 180 : 228;
            float bx = VW - 5 * bw - 6;
            float rx = bx - rw - 8;
            var rr = new Rect(rx, 6, rw, 40);
            string cur = S.CurrentResearch;
            var def = cur != null ? D.ResearchById(cur) : null;
            if (GUI.Button(rr, GUIContent.none, Button)) ToggleResearch();
            bool allKnown = def == null && _tAllKnown;
            if (def != null) _tl.Begin().S("Researching <b>").S(def.name).S("</b>");
            else if (allKnown) _tl.Begin().C(Good).S("All research known").E();
            else _tl.Begin().C(Warn).S("Nothing being researched").E();
            GUI.Label(new Rect(rx + 8, 7, rw - 14, 20), _tl.To(ref _resTxt), Label14);
            // Second line: what holds research up when nothing makes knowledge (it used to look like it was running, at
            // "+0/day"), else the bar, the rate and about how many days the current topic has left.
            string stalled = allKnown ? null : KnowledgeStalled();
            if (stalled != null) GUI.Label(new Rect(rx + 8, 25, rw - 12, 18), _tl.Begin().C(Warn).S(stalled).E().To(ref _resTxt2), Small);
            else
            {
                bool eta = def != null && _knowPerDay > 0.05f;
                float lw = eta ? 128 : 80;
                Bar(new Rect(rx + 8, 30, rw - lw - 18, 9), def != null ? S.ResearchProgress(cur) : 0, Accent);
                _tl.Begin().S("+").F(_knowPerDay, "0.#").S("/day");
                if (eta)
                {
                    float days = math.max(1, math.ceil(def.cost * (1f - S.ResearchProgress(cur)) / _knowPerDay));
                    _tl.S(" · ~").F(days, "0").S(days == 1 ? " day" : " days");
                }
                GUI.Label(new Rect(rx + rw - lw - 4, 25, lw + 2, 18), _tl.To(ref _resTxt3), SmallRight);
            }
            if (Over(rr)) Tip(rr, (def != null ? $"{def.name}: {S.ResearchProgress(cur):P0}. {S.ResearchQueue.Count} in the queue." : $"Nothing queued: knowledge is banked ({S.KnowledgeBank:0}) until you choose.") +
                    $"\n\nKnowledge {_knowPerDay:0.#} a day, from the elders at staffed Elders' Fires (old people count more). More fires and more elders on them research faster. Open the research tree (R)." +
                    (stalled != null ? $"\n\n{stalled}: nothing makes knowledge now." : ""));

            var labels = PanelLabels; var keys = PanelKeys; var tips = PanelTips;
            if (_panelLabelsKeyed == null)
            {
                _panelLabelsKeyed = new string[5];
                for (int i = 0; i < 5; i++) _panelLabelsKeyed[i] = $"{labels[i]} {Col(keys[i], Dim)}";
            }
            for (int i = 0; i < 5; i++)
            {
                var r = new Rect(bx + i * bw, 9, bw - 4, 34);
                // Goals is lit while the next age is ready (nothing else says the age-up is a manual step).
                bool open = i == 0 ? ResearchOpen : i == 1 ? GoalsOpen : i == 2 ? JobsOpen : i == 3 ? EventsOpen : HelpOpen;
                bool lit = open || i == 1 && AgeReady;
                if (GUI.Button(r, narrow ? labels[i] : _panelLabelsKeyed[i], lit ? ButtonOn : Button))
                {
                    if (i == 0) ToggleResearch(); else if (i == 1) ToggleGoals(); else if (i == 2) ToggleJobs(); else if (i == 3) ToggleEvents(); else ToggleHelp();
                }
                if (Over(r)) Tip(r, tips[i] + (narrow ? $" ({keys[i]})" : "") + (i == 1 && AgeReady ? " Everything for the next age is in place: advance from here." : ""));
            }

            // Row two: key stocks, then the needs and unrest (they keep their order and drop from the right when narrow).
            x = 12;
            float y2 = 55, right = VW - 10;
            int shown = 0;
            for (int k = 0; k < KeyStocks.Length; k++)
            {
                string id = KeyStocks[k];
                int r = S.ResIndex(id);
                if (r < 0) continue;
                bool tools = id == "tools";
                float amt = tools ? ToolsInStore() : S.StockTotals[r];
                if (shown >= AlwaysShown && amt < 0.5f && S.Produced[r] < 0.5f) continue;
                if (x + 120 > right - NeedsWidth()) break;
                var sr = new Rect(x, y2, 116, 26);
                float trend = tools ? ToolTrend() : Trend(r);
                // Name 68 wide (the small text grows when the layout is scaled down: "Firewood" ran into its figure at 720p).
                GUI.Label(new Rect(x, y2 + 3, 68, 20), tools ? "Tools" : D.Resources[r].name, Small);
                GUI.Label(new Rect(x + 68, y2 + 2, 50, 22), _tl.Begin().S("<b>").F(amt, "0").S("</b>").S(Arrow(trend, 0.3f)).To(ref _stockTxt[k]), Label);
                if (Over(sr)) Tip(sr, (StockTips.TryGetValue(id, out var t) ? t : D.Resources[r].name) + $"\n\n{amt:0.#} in store, {trend:+0.#;-0.#;0} over the last day. Click for every good (T).");
                if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) ToggleStocks();
                x += 120; shown++;
            }
            DrawNeedsStrip(math.max(x + 10, right - NeedsWidth()), y2, right);
            if (G.Auto != null)
            {
                var ar2 = Box(new Rect(VW - 330, VH - (VW - 330 < 10 + BuildBarW + 10 ? 98 : 40), 320, 30), UiTheme.Header);   // above the build bar when the screen is narrow
                GUI.Label(new Rect(ar2.x + 12, ar2.y, 300, 30), _autoTxt ??= Col("Auto-player is running the settlement (F9)", Info), Label14);
            }
        }

        // One source for the numbers more than one screen prints (top bar, goods panel, jobs, end screen), so they agree.

        /// <summary>
        /// Why no knowledge is being made, or null when some is: no Elders' Fire yet (or only its site), or no elders at
        /// the ones there are. Read from the buildings, not the day's rate, so it shows at once.
        /// </summary>
        string KnowledgeStalled()
        {
            int fires = 0, workers = 0; bool site = false;
            foreach (var b in S.Buildings)
            {
                if (b.Def.knowledgePerWorker <= 0) continue;
                if (b.Active) { fires++; workers += b.Flooded || b.Paused ? 0 : b.Workers; }
                else if (b.UnderWork) site = true;
            }
            if (fires == 0) return site ? "No elders: Elders' Fire unfinished" : "No elders: build an Elders' Fire";
            if (workers == 0) return fires == 1 ? "The Elders' Fire has no elders" : "The Elders' Fires have no elders";
            return null;
        }

        /// <summary>"cold 3, starvation 1, old age 2" (most first).</summary>
        string DeathsByCause() => string.Join(", ", S.Deaths.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));

        /// <summary>People as the top bar splits them: adults (not counting elders), elders and children.</summary>
        (int adults, int elders, int children) PeopleSplit()
        {
            return (_tAdults - _tElders, _tElders, _tPop - _tAdults);
        }

        // The settlement's totals the HUD prints, counted once per OnGUI call (CountTotals): each of Settlement's properties
        // is a loop over every citizen or building, and the top bar alone read them nine times a call. The same loops and
        // sums in the same order as those properties, so the figures are identical. HUD only: nothing in the sim reads them.
        int _tPop, _tAdults, _tElders, _tHomeless, _tHousing;
        float _tHappy, _tFoodNeed, _tFoodStock;
        bool _tAllKnown;
        float[] _tNeedMet = new float[0];
        float TFoodDays => _tFoodNeed <= 0 ? 99f : _tFoodStock / _tFoodNeed;

        void CountTotals()
        {
            var b = S.Data.Balance;
            int pop = 0, adults = 0, elders = 0, homeless = 0, housing = 0;
            float happy = 0, need = 0;
            foreach (var c in S.Citizens)
            {
                if (c.Gone) continue;
                pop++;
                bool adult = c.Age >= b.adultAge;
                if (adult) adults++;
                if (S.IsElder(c)) elders++;
                if (c.Home == null) homeless++;
                happy += c.Happiness;
                need += adult ? b.foodPerAdult : b.foodPerChild;
            }
            foreach (var x in S.Buildings) if (x.Active) housing += x.Def.housing;
            _tPop = pop; _tAdults = adults; _tElders = elders; _tHomeless = homeless; _tHousing = housing;
            _tHappy = pop == 0 ? 0 : happy / pop;
            _tFoodNeed = need; _tFoodStock = S.FoodStock;
            // Every topic known (Settlement.AvailableResearch is empty), without its LINQ and delegates.
            bool any = false;
            foreach (var r in S.Data.Research)
            {
                if (S.IsResearched(r.id) || S.Data.AgeOrder(r.age) > S.Age.order) continue;
                bool ready = true;
                foreach (var p in r.prereqs) if (!S.IsResearched(p)) { ready = false; break; }
                if (ready) { any = true; break; }
            }
            _tAllKnown = !any;
            // How many people each need reaches (a loop over every citizen per need): the needs strip and its width read it
            // up to 14 times a call (NeedsWidth ran once per stock shown).
            var needs = S.Data.Balance.needs;
            if (_tNeedMet.Length != needs.Length) _tNeedMet = new float[needs.Length];
            for (int k = 0; k < needs.Length; k++) _tNeedMet[k] = S.NeedMet(needs[k].id);
        }

        /// <summary>"148  (86 adults, 19 elders, 43 children)".</summary>
        string PeopleLine()
        {
            var (a, e, c) = PeopleSplit();
            return $"{_tPop}  ({a} {(a == 1 ? "adult" : "adults")}, {e} {(e == 1 ? "elder" : "elders")}, {c} {(c == 1 ? "child" : "children")})";
        }

        /// <summary>Tools of every kind in store (unrounded; shown with {:0} like every other good).</summary>
        float ToolsInStore()
        {
            float t = 0;
            for (int r = 0; r < S.ResCount; r++) if (D.Resources[r].toolSpeed > 0) t += S.StockTotals[r];
            return t;
        }

        static string FoodDaysText(float fd) => fd >= 99 ? "-" : Days(fd, "0.#");

        /// <summary>"1 day", "2.5 days": a figure with "day" or "days" as it reads once formatted.</summary>
        static string Days(float v, string format) { string n = v.ToString(format); return n == "1" ? n + " day" : n + " days"; }

        /// <summary>"1 cell" / "3 cells": the word in the singular for a count of one.</summary>
        static string Many(int n, string one, string many) => n == 1 ? $"1 {one}" : $"{n} {many}";

        /// <summary>The food figure's tooltip (also shown by -guideshots).</summary>
        public string FoodTip() =>
            $"Food in store lasts {Days(S.FoodDays, "0.#")} at {S.DailyFoodNeed:0.#} a day (adults eat 1, children less). Change over the last day: {_foodDelta:+0.#;-0.#;0}.\n\n" +
            "Gatherers, hunters, fishers and, later, fields make it; it spoils in open piles (a granary keeps it). Winter makes little: stock up before it. Click for every good (T)." +
            HarvestOutlookLine();

        /// <summary>Spring and summer, once there are fields: this year's expected harvest against the year's need (the same
        /// figures as the spring warning).</summary>
        string HarvestOutlookLine()
        {
            if (S.Season > 1 || !S.HarvestOutlook(out float exp, out int fields, out int flax, out float need)) return "";
            float have = exp + S.FoodStock;
            return $"\n\nHarvest outlook: ~{exp:0} expected from {fields} field{(fields == 1 ? "" : "s")}" + (flax > 0 ? $" ({flax} on flax)" : "") +
                   $", with {S.FoodStock:0} in store: " + Col($"{have:0} of ~{need:0}", have >= need ? Good : Warn) + " needed until autumn next year" +
                   (have >= need ? "." : (S.Season == 0 ? ": sow more fields now." : ": too late to sow this year; more gatherers, hunters or fishers."));
        }

        float ToolTrend()
        {
            float t = 0;
            for (int r = 0; r < S.ResCount; r++) if (D.Resources[r].toolSpeed > 0) t += Trend(r);
            return t;
        }

        /// <summary>Width the needs strip takes (0 while no need applies yet).</summary>
        float NeedsWidth()
        {
            int n = 0;
            for (int k = 0; k < D.Balance.needs.Length; k++) if (_tNeedMet[k] >= 0) n++;
            if (S.Age.order >= 1) n++;
            return (n + 1) * 120;                  // + happiness, always shown
        }
    }
}
