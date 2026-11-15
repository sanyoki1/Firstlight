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
            ["pottery"] = "Pottery: every household keeps pots in use, and they wear out. Potters make them.",
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

        static string Arrow(float d, float big)
        {
            if (d > big) return Col(" ↑", Good);
            if (d < -big) return Col(" ↓", Bad);
            return Col(" ·", Faint);
        }

        string Clock()
        {
            float h = S.DayFraction * 24f;
            return $"{(int)h:00}:{(int)((h - (int)h) * 60) / 10 * 10:00}";
        }

        void DrawTopBar()
        {
            Box(new Rect(0, 0, VW, 52), UiTheme.Header);
            Box(new Rect(0, 52, VW, TopBarHeight - 52), UiTheme.Panel);
            float x = 14;

            // Age and calendar.
            var ar = new Rect(x, 5, 250, 42);
            GUI.Label(new Rect(x, 5, 250, 22), S.Age?.name ?? "", Title);
            GUI.Label(new Rect(x, 27, 250, 20), $"Year {S.Year + 1}, {S.SeasonName} day {S.DayOfSeason + 1}  ·  {Clock()}", Small);
            Tip(ar, $"The age the settlement has reached, and the calendar: {D.Balance.daysPerSeason} days a season, a year of four seasons. Winter is cold and nothing grows; spring floods can come. Goals (G) show what the next age needs.");
            x += 252;

            // Speed.
            float[] speeds = { 0, 1, 2, 4 };
            string[] names = { "II", "1x", "2x", "4x" };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x + i * 42, 11, 38, 30);
                bool on = math.abs(S.Speed - speeds[i]) < 0.01f;
                if (GUI.Button(r, names[i], on ? ButtonOn : Button)) G.SetSpeed(speeds[i]);
                Tip(r, i == 0 ? "Pause (Space). You can still build and give orders while paused." : $"Game speed {names[i]} (key {i}).");
            }
            x += 4 * 42 + 16;

            // People.
            var (adults, elders, kids) = PeopleSplit();
            int free = S.HousingCapacity - S.Population;
            var pr = new Rect(x, 5, 236, 42);
            GUI.Label(new Rect(x, 5, 236, 22), $"<b>{S.Population}</b> people  " + (S.Homeless > 0 ? Col($"{S.Homeless} homeless", Warn) : free > 0 ? Col($"{free} beds free", Dim) : Col("no free beds", Dim)), Label);
            GUI.Label(new Rect(x, 27, 236, 20), $"{adults} adults · {kids} children · {elders} elders", Small);
            Tip(pr, $"People: {S.Population}, housing for {S.HousingCapacity}. Babies are born to families with a home, free beds and food in store; migrants come when there are free beds, food and good spirits. Unhappy or starving people leave.\n\n" +
                    $"Happiness {S.AverageHappiness:P0}: the average and the worst of food, warmth, shelter, health (and later water, pottery and faith). It sets how fast people work.\n\nBorn {S.Births}, arrived {S.Migrants}, left {S.Leavers}, died {S.TotalDeaths}." +
                    (S.TotalDeaths > 0 ? $"\nDeaths: {DeathsByCause()}. Events (V) says who." : ""));
            x += 240;

            // Food.
            float fd = S.FoodDays;
            var fcol = fd < S.Data.Balance.foodWarnDays ? Bad : fd < 8 ? Warn : Good;
            var fr = new Rect(x, 5, 150, 42);
            GUI.Label(new Rect(x, 5, 150, 22), "Food " + Col(FoodDaysText(fd), fcol), Label);
            GUI.Label(new Rect(x, 27, 150, 20), $"{S.FoodStock:0} in store" + Arrow(_foodDelta, 0.5f), Small);
            Tip(fr, FoodTip());
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
            bool allKnown = def == null && !S.AvailableResearch.Any();
            GUI.Label(new Rect(rx + 8, 7, rw - 14, 20), def != null ? $"Researching <b>{def.name}</b>" : allKnown ? Col("All research known", Good) : Col("Nothing being researched", Warn), new GUIStyle(Label) { fontSize = 14 });
            // Second line: what holds research up when nothing makes knowledge (it used to look like it was running, at
            // "+0/day"), else the bar, the rate and about how many days the current topic has left.
            string stalled = allKnown ? null : KnowledgeStalled();
            if (stalled != null) GUI.Label(new Rect(rx + 8, 25, rw - 12, 18), Col(stalled, Warn), Small);
            else
            {
                string eta = def != null && _knowPerDay > 0.05f ? $" · ~{math.max(1, math.ceil(def.cost * (1f - S.ResearchProgress(cur)) / _knowPerDay)):0} days" : "";
                float lw = eta.Length > 0 ? 128 : 80;
                Bar(new Rect(rx + 8, 30, rw - lw - 18, 9), def != null ? S.ResearchProgress(cur) : 0, Accent);
                GUI.Label(new Rect(rx + rw - lw - 4, 25, lw + 2, 18), $"+{_knowPerDay:0.#}/day{eta}", new GUIStyle(Small) { alignment = TextAnchor.MiddleRight });
            }
            Tip(rr, (def != null ? $"{def.name}: {S.ResearchProgress(cur):P0}. {S.ResearchQueue.Count} in the queue." : $"Nothing queued: knowledge is banked ({S.KnowledgeBank:0}) until you choose.") +
                    $"\n\nKnowledge {_knowPerDay:0.#} a day, from the elders at staffed Elders' Fires (old people count more). More fires and more elders on them research faster. Open the research tree (R)." +
                    (stalled != null ? $"\n\n{stalled}: nothing makes knowledge now." : ""));

            string[] labels = { "Research", "Goals", "Jobs", "Events", "Help" };
            string[] keys = { "R", "G", "J", "V", "F1" };
            bool[] open = { ResearchOpen, GoalsOpen, JobsOpen, EventsOpen, HelpOpen };
            string[] tips = { "Research tree: choose what to learn next.", "Goals: the key research and milestones for the next age.", "Jobs: how many work where; set priorities on each building.",
                              "Events: everything that has happened, newest first (who died and of what, who left, shortages), and Topics: the hints seen so far.", "Controls and keys." };
            for (int i = 0; i < 5; i++)
            {
                var r = new Rect(bx + i * bw, 9, bw - 4, 34);
                // Goals is lit while the next age is ready (nothing else says the age-up is a manual step).
                bool lit = open[i] || i == 1 && AgeReady;
                if (GUI.Button(r, narrow ? labels[i] : $"{labels[i]} {Col(keys[i], Dim)}", lit ? ButtonOn : Button))
                {
                    if (i == 0) ToggleResearch(); else if (i == 1) ToggleGoals(); else if (i == 2) ToggleJobs(); else if (i == 3) ToggleEvents(); else ToggleHelp();
                }
                Tip(r, tips[i] + (narrow ? $" ({keys[i]})" : "") + (i == 1 && AgeReady ? " Everything for the next age is in place: advance from here." : ""));
            }

            // Row two: key stocks, then the needs and unrest (they keep their order and drop from the right when narrow).
            x = 12;
            float y2 = 55, right = VW - 10;
            int shown = 0;
            foreach (var id in KeyStocks)
            {
                int r = S.ResIndex(id);
                if (r < 0) continue;
                bool tools = id == "tools";
                float amt = tools ? ToolsInStore() : S.StockTotals[r];
                if (shown >= AlwaysShown && amt < 0.5f && S.Produced[r] < 0.5f) continue;
                if (x + 116 > right - NeedsWidth()) break;
                var sr = new Rect(x, y2, 112, 26);
                float trend = tools ? ToolTrend() : Trend(r);
                GUI.Label(new Rect(x, y2 + 3, 64, 20), tools ? "Tools" : D.Resources[r].name, Small);
                GUI.Label(new Rect(x + 62, y2 + 2, 50, 22), $"<b>{amt:0}</b>" + Arrow(trend, 0.3f), Label);
                Tip(sr, (StockTips.TryGetValue(id, out var t) ? t : D.Resources[r].name) + $"\n\n{amt:0.#} in store, {trend:+0.#;-0.#;0} over the last day. Click for every good (T).");
                if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) ToggleStocks();
                x += 116; shown++;
            }
            DrawNeedsStrip(math.max(x + 10, right - NeedsWidth()), y2, right);
            if (G.Auto != null)
            {
                var ar2 = Box(new Rect(VW - 330, VH - 40, 320, 30), UiTheme.Header);
                GUI.Label(new Rect(ar2.x + 12, ar2.y, 300, 30), Col("Auto-player is running the settlement (F9)", Info), new GUIStyle(Label) { fontSize = 14 });
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
            int elders = 0;
            foreach (var c in S.Citizens) if (!c.Gone && S.IsElder(c)) elders++;
            return (S.Adults - elders, elders, S.Children);
        }

        /// <summary>"148  (86 adults, 19 elders, 43 children)".</summary>
        string PeopleLine()
        {
            var (a, e, c) = PeopleSplit();
            return $"{S.Population}  ({a} adults, {e} elders, {c} children)";
        }

        /// <summary>Tools of every kind in store (unrounded; shown with {:0} like every other good).</summary>
        float ToolsInStore()
        {
            float t = 0;
            for (int r = 0; r < S.ResCount; r++) if (D.Resources[r].toolSpeed > 0) t += S.StockTotals[r];
            return t;
        }

        static string FoodDaysText(float fd) => fd >= 99 ? "-" : $"{fd:0.#} days";

        /// <summary>The food figure's tooltip (also shown by -guideshots).</summary>
        public string FoodTip() =>
            $"Food in store lasts {S.FoodDays:0.#} days at {S.DailyFoodNeed:0.#} a day (adults eat 1, children less). Change over the last day: {_foodDelta:+0.#;-0.#;0}.\n\n" +
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
            foreach (var need in D.Balance.needs) if (S.NeedMet(need.id) >= 0) n++;
            if (S.Age.order >= 1) n++;
            return (n + 1) * 120;                  // + happiness, always shown
        }
    }
}
