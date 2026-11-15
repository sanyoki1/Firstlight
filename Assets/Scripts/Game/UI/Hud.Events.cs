using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Events (V): the settlement's history, which the feed shows for only a minute (deaths vanished before anyone could
    // read why), newest first, with a "Problems only" filter; and Topics: every hint already seen, to read again.
    public sealed partial class Hud
    {
        public bool EventsOpen;
        bool _eventsProblems, _eventsTopics;
        Vector2 _eventsScroll;
        const int EventsShown = 200;

        public void ToggleEvents() { EventsOpen = !EventsOpen; if (EventsOpen) GoalsOpen = JobsOpen = StocksOpen = LawsOpen = false; }
        /// <summary>-p4shots: open the panel on a tab.</summary>
        public void ShowEvents(bool topics, bool problemsOnly) { if (!EventsOpen) ToggleEvents(); _eventsTopics = topics; _eventsProblems = problemsOnly; _eventsScroll = default; }

        // The rows on show and their heights (rebuilt only when an event comes in or the filter changes).
        readonly List<int> _evRows = new();
        readonly List<float> _evH = new();
        int _evCount = -1; bool _evProblemsBuilt; float _evWidthBuilt;
        GUIStyle _evStyle;

        static string SeasonShort(int s) => s switch { 0 => "Spr", 1 => "Sum", 2 => "Aut", _ => "Win" };
        string EventDate(int day) => $"Y{day / S.DaysPerYear + 1} {SeasonShort((day / D.Balance.daysPerSeason) % 4)} {day % D.Balance.daysPerSeason + 1}";

        void DrawEvents()
        {
            float w = 560, h = math.min(VH - TopBarHeight - 90, 760);
            var r = BeginSide(_eventsTopics ? "Topics" : "Events", w, h, () => EventsOpen = false);
            // Tabs and the filter.
            if (GUI.Button(new Rect(r.x + 150, r.y + 10, 90, 24), "Events", !_eventsTopics ? ButtonOn : Button)) { _eventsTopics = false; _eventsScroll = default; }
            if (GUI.Button(new Rect(r.x + 244, r.y + 10, 90, 24), "Topics", _eventsTopics ? ButtonOn : Button)) { _eventsTopics = true; _eventsScroll = default; }
            Tip(new Rect(r.x + 244, r.y + 10, 90, 24), "Every hint seen so far, to read again.");
            if (!_eventsTopics)
            {
                var fr = new Rect(r.x + 344, r.y + 10, 160, 24);
                if (GUI.Button(fr, GUIContent.none, GUIStyle.none)) { _eventsProblems = !_eventsProblems; _eventsScroll = default; }
                GUI.Label(fr, (_eventsProblems ? Col("[x]", Good) : Col("[  ]", Dim)) + "  Problems only", Small);
                Tip(fr, "Show only warnings and bad news: deaths, leavers, shortages, floods, sickness, disputes.");
            }
            var view = new Rect(r.x + 10, r.y + 44, w - 20, h - 54);
            if (_eventsTopics) DrawTopics(view); else DrawEventRows(view);
        }

        void DrawEventRows(Rect view)
        {
            _evStyle ??= new GUIStyle(Label) { fontSize = 14, wordWrap = true };
            float textW = view.width - 120;
            if (_evCount != S.Events.Count || _evProblemsBuilt != _eventsProblems || _evWidthBuilt != textW)
            {
                _evRows.Clear(); _evH.Clear();
                for (int i = S.Events.Count - 1; i >= 0 && _evRows.Count < EventsShown; i--)
                {
                    var e = S.Events[i];
                    if (_eventsProblems && e.Severity == Severity.Info) continue;
                    _evRows.Add(i);
                    _evH.Add(math.max(22, _evStyle.CalcHeight(new GUIContent(e.Text), textW) + 4));
                }
                _evCount = S.Events.Count; _evProblemsBuilt = _eventsProblems; _evWidthBuilt = textW;
            }
            if (_evRows.Count == 0) { GUI.Label(new Rect(view.x + 6, view.y + 4, view.width, 22), Col(_eventsProblems ? "No problems so far." : "Nothing has happened yet.", Dim), Label); return; }
            float total = 0; foreach (var hh in _evH) total += hh + 2;
            _eventsScroll = GUI.BeginScrollView(view, _eventsScroll, new Rect(0, 0, view.width - 18, total));
            float y = 0;
            for (int k = 0; k < _evRows.Count; k++)
            {
                float hh = _evH[k];
                // Only the rows in sight are drawn.
                if (y + hh >= _eventsScroll.y && y <= _eventsScroll.y + view.height)
                {
                    var e = S.Events[_evRows[k]];
                    var col = e.Severity == Severity.Bad ? Bad : e.Severity == Severity.Warning ? Warn : Text;
                    var row = new Rect(0, y, view.width - 18, hh);
                    if (e.HasPlace && row.Contains(Event.current.mousePosition)) Fill(row, new Color(1, 1, 1, 0.05f));
                    Fill(new Rect(0, y + 2, 3, hh - 4), e.Severity == Severity.Info ? Faint : col);
                    GUI.Label(new Rect(10, y + 2, 92, 20), Col(EventDate(e.Day), Dim), Small);   // Faint was nearly invisible here
                    GUI.Label(new Rect(104, y + 2, textW, hh - 4), Col(e.Text, e.Severity == Severity.Info ? Text : col), _evStyle);
                    if (e.HasPlace)
                    {
                        GUI.Label(new Rect(row.xMax - 16, y + 1, 14, 20), Col("›", Dim), Label);
                        if (GUI.Button(row, GUIContent.none, GUIStyle.none)) JumpTo(e.Place);
                    }
                }
                y += hh + 2;
            }
            GUI.EndScrollView();
        }

        void DrawTopics(Rect view)
        {
            var seen = new List<HintDef>();
            foreach (var hd in Hints) if (_hintsSeen.Contains(hd.Id)) seen.Add(hd);
            if (seen.Count == 0) { GUI.Label(new Rect(view.x + 6, view.y + 4, view.width, 44), Col("No hints seen yet. Each one is kept here once it has come up.", Dim), SmallWrap); return; }
            float tw = view.width - 30;
            float total = 0;
            var texts = new List<string>();
            foreach (var hd in seen) { string t = HintText(hd); texts.Add(t); total += 28 + SmallWrap.CalcHeight(new GUIContent(t), tw) + 12; }
            _eventsScroll = GUI.BeginScrollView(view, _eventsScroll, new Rect(0, 0, view.width - 18, total));
            float y = 0;
            for (int i = 0; i < seen.Count; i++)
            {
                float th = SmallWrap.CalcHeight(new GUIContent(texts[i]), tw);
                GUI.Label(new Rect(6, y + 2, tw, 24), $"<b>{seen[i].Title}</b>", Label);
                GUI.Label(new Rect(6, y + 28, tw, th), texts[i], SmallWrap);
                y += 28 + th + 12;
            }
            GUI.EndScrollView();
        }
    }
}
