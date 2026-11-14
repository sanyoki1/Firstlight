using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Top bar: age, calendar, speed, population, food, key stocks with daily trends, research, panel buttons.
    public sealed partial class Hud
    {
        float[] _daySnap, _dayDelta;
        int _snapDay = -1;
        float _knowSnap, _knowPerDay, _foodSnap, _foodDelta;

        static readonly string[] KeyStocks = { "wood", "firewood", "stone", "tools", "hides", "clothing", "herbs" };

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
            var bar = Box(new Rect(0, 0, VW, 52), UiTheme.Header);
            float x = 14;

            // Age and calendar.
            GUI.Label(new Rect(x, 5, 250, 22), S.Age?.name ?? "", Title);
            GUI.Label(new Rect(x, 27, 270, 20), $"Year {S.Year + 1}, {S.SeasonName} day {S.DayOfSeason + 1}  ·  {Clock()}", Small);
            x += 262;

            // Speed.
            float[] speeds = { 0, 1, 2, 4 };
            string[] names = { "II", "1x", "2x", "4x" };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(x + i * 42, 11, 38, 30);
                bool on = math.abs(S.Speed - speeds[i]) < 0.01f;
                if (GUI.Button(r, names[i], on ? ButtonOn : Button)) G.SetSpeed(speeds[i]);
                Tip(r, i == 0 ? "Pause (Space)" : $"Speed {names[i]} (key {i})");
            }
            x += 4 * 42 + 18;

            // People.
            int elders = 0;
            foreach (var c in S.Citizens) if (!c.Gone && S.IsElder(c)) elders++;
            int adults = S.Adults - elders, kids = S.Children;
            int free = S.HousingCapacity - S.Population;
            var pr = new Rect(x, 5, 250, 42);
            GUI.Label(new Rect(x, 5, 250, 22), $"<b>{S.Population}</b> people  " + (S.Homeless > 0 ? Col($"{S.Homeless} homeless", Warn) : free > 0 ? Col($"{free} beds free", Dim) : Col("no free beds", Dim)), Label);
            GUI.Label(new Rect(x, 27, 250, 20), $"{adults} adults · {kids} children · {elders} elders", Small);
            Tip(pr, $"Housing for {S.HousingCapacity}. Average happiness {S.AverageHappiness:P0}. Births {S.Births}, migrants {S.Migrants}, left {S.Leavers}, died {S.TotalDeaths}.");
            x += 254;

            // Food.
            float fd = S.FoodDays;
            var fcol = fd < S.Data.Balance.foodWarnDays ? Bad : fd < 8 ? Warn : Good;
            var fr = new Rect(x, 5, 150, 42);
            GUI.Label(new Rect(x, 5, 150, 22), "Food " + Col(fd >= 99 ? "-" : $"{fd:0.#} days", fcol), Label);
            GUI.Label(new Rect(x, 27, 150, 20), $"{S.FoodStock:0} in store" + Arrow(_foodDelta, 0.5f), Small);
            Tip(fr, $"Food for {fd:0.#} days at {S.DailyFoodNeed:0.#} a day. Change over the last day: {_foodDelta:+0.#;-0.#;0}.");
            if (GUI.Button(fr, GUIContent.none, GUIStyle.none)) ToggleStocks();
            x += 150;

            // The needs that have arrived (Agricultural Age), then key stocks (click: the full list).
            float right = VW - 4 * 96 - 240;
            x = DrawNeedsStrip(x, right) + 4;
            foreach (var id in KeyStocks)
            {
                int r = S.ResIndex(id);
                if (r < 0) continue;
                if (x + 80 > right) break;
                float amt = S.StockTotals[r];
                var sr = new Rect(x, 5, 78, 42);
                GUI.Label(new Rect(x, 5, 78, 20), D.Resources[r].name, Small);
                GUI.Label(new Rect(x, 24, 78, 22), $"{amt:0}" + Arrow(Trend(r), 0.3f), Bold);
                Tip(sr, $"{D.Resources[r].name}: {amt:0.#} in store, {Trend(r):+0.#;-0.#;0} over the last day. Click for every good (T).");
                if (GUI.Button(sr, GUIContent.none, GUIStyle.none)) ToggleStocks();
                x += 80;
            }

            // Research progress.
            float rx = VW - 4 * 96 - 234;
            var rr = new Rect(rx, 6, 226, 40);
            string cur = S.CurrentResearch;
            var def = cur != null ? D.ResearchById(cur) : null;
            if (GUI.Button(rr, GUIContent.none, Button)) ToggleResearch();
            GUI.Label(new Rect(rx + 8, 7, 214, 20), def != null ? $"Researching <b>{def.name}</b>" : Col("Nothing being researched", Warn), new GUIStyle(Label) { fontSize = 14 });
            Bar(new Rect(rx + 8, 30, 130, 9), def != null ? S.ResearchProgress(cur) : 0, Accent);
            GUI.Label(new Rect(rx + 144, 25, 80, 18), $"+{_knowPerDay:0.#}/day", Small);
            Tip(rr, def != null ? $"{def.name}: {S.ResearchProgress(cur):P0}. {S.ResearchQueue.Count} in the queue. Knowledge {_knowPerDay:0.#} a day. Open the research tree (R)." :
                $"Knowledge is being banked ({S.KnowledgeBank:0}). Open the research tree (R) and choose what to learn.");

            // Panel buttons.
            float bx = VW - 4 * 96 - 4;
            string[] labels = { "Research", "Goals", "Jobs", "Help" };
            string[] keys = { "R", "G", "J", "F1" };
            bool[] open = { ResearchOpen, GoalsOpen, JobsOpen, HelpOpen };
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(bx + i * 96, 9, 92, 34);
                if (GUI.Button(r, $"{labels[i]} {Col(keys[i], Dim)}", open[i] ? ButtonOn : Button))
                {
                    if (i == 0) ToggleResearch(); else if (i == 1) ToggleGoals(); else if (i == 2) ToggleJobs(); else ToggleHelp();
                }
            }
            if (G.Auto != null)
            {
                var ar = Box(new Rect(VW - 330, VH - 40, 320, 30), UiTheme.Header);
                GUI.Label(new Rect(ar.x + 12, ar.y, 300, 30), Col("Auto-player is running the settlement (F9)", Info), new GUIStyle(Label) { fontSize = 14 });
            }
        }
    }
}
