using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Research screen (R): the tree in columns by age, prerequisite lines, queue by clicking.
    public sealed partial class Hud
    {
        sealed class Node { public ResearchDef Def; public int Col, Row; public Rect R; }
        List<Node> _nodes;
        Dictionary<string, Node> _byId;
        int _cols, _rows;
        List<(string age, int from, int to)> _ageCols;
        string _researchMsg = ""; float _researchMsgTime;

        /// <summary>
        /// Lays the tree out once: each age gets its own columns; within an age a topic sits one column right of its
        /// deepest same-age prerequisite. Rows are ordered by where their prerequisites are (fewer crossing lines).
        /// </summary>
        void LayoutResearch()
        {
            _nodes = new List<Node>(); _byId = new Dictionary<string, Node>();
            _ageCols = new List<(string, int, int)>();
            var local = new Dictionary<string, int>();
            int LocalDepth(ResearchDef r)
            {
                if (local.TryGetValue(r.id, out int v)) return v;
                int d = 0;
                foreach (var p in r.prereqs)
                {
                    var pd = D.ResearchById(p);
                    if (pd != null && pd.age == r.age) d = math.max(d, LocalDepth(pd) + 1);
                }
                local[r.id] = d;
                return d;
            }
            int offset = 0;
            foreach (var age in D.Ages)
            {
                var list = D.Research.Where(r => r.age == age.id).ToList();
                if (list.Count == 0) continue;
                int depth = list.Max(LocalDepth) + 1;
                foreach (var r in list) { var n = new Node { Def = r, Col = offset + LocalDepth(r) }; _nodes.Add(n); _byId[r.id] = n; }
                _ageCols.Add((age.id, offset, offset + depth - 1));
                offset += depth;
            }
            _cols = offset;
            _rows = 0;
            for (int c = 0; c < _cols; c++)
            {
                var col = _nodes.Where(n => n.Col == c).ToList();
                float Key(Node n)
                {
                    var rows = n.Def.prereqs.Where(_byId.ContainsKey).Select(p => (float)_byId[p].Row).ToList();
                    return rows.Count > 0 ? rows.Average() : -1;
                }
                col = col.OrderBy(Key).ThenBy(n => D.Research.IndexOf(n.Def)).ToList();
                for (int i = 0; i < col.Count; i++) col[i].Row = i;
                _rows = math.max(_rows, col.Count);
            }
            // Centre short columns.
            for (int c = 0; c < _cols; c++)
            {
                var col = _nodes.Where(n => n.Col == c).ToList();
                int pad = (_rows - col.Count) / 2;
                foreach (var n in col) n.Row += pad;
            }
        }

        enum RState { Known, Queued, Current, Available, Locked, AgeLocked }

        RState StateOf(ResearchDef r)
        {
            if (S.IsResearched(r.id)) return RState.Known;
            if (S.CurrentResearch == r.id) return RState.Current;
            if (S.ResearchQueue.Contains(r.id)) return RState.Queued;
            if (D.AgeOrder(r.age) > S.Age.order) return RState.AgeLocked;
            return r.prereqs.All(S.IsResearched) ? RState.Available : RState.Locked;
        }

        public void ClickResearch(string id, bool right = false)
        {
            if (right) { S.DequeueResearch(id); _researchMsg = ""; return; }
            if (S.ResearchQueue.Contains(id)) { _researchMsg = "Already in the queue (right-click to remove it)."; _researchMsgTime = Time.unscaledTime; return; }
            if (!S.QueueResearch(id, out string why)) { _researchMsg = why; _researchMsgTime = Time.unscaledTime; }
            else _researchMsg = "";
        }

        void DrawResearch()
        {
            if (_nodes == null) LayoutResearch();
            var r = Box(new Rect(14, TopBarHeight + 4, VW - 28, VH - TopBarHeight - 18));
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.095f, 0.1f, 0.1f, 1f));   // opaque: a screen of its own
            GUI.Label(new Rect(r.x + 20, r.y + 12, 300, 30), "Research", new GUIStyle(Title) { fontSize = 24 });
            if (GUI.Button(new Rect(r.xMax - 44, r.y + 12, 30, 28), "x", Button)) ResearchOpen = false;
            // Queue and knowledge.
            var q = S.ResearchQueue.Select((id, i) => $"{i + 1}. {D.ResearchById(id)?.name}" + (i == 0 ? $" {S.ResearchProgress(id):P0}" : "")).ToList();
            int elders = S.Buildings.Where(b => b.Active && b.Def.knowledgePerWorker > 0).Sum(b => b.Workers);
            string stalled = KnowledgeStalled();
            GUI.Label(new Rect(r.x + 180, r.y + 10, r.width - 260, 22),
                $"Knowledge {Col($"+{_knowPerDay:0.#} a day", Accent)} from {elders} elder{(elders == 1 ? "" : "s")}" +
                (stalled != null ? "   " + Col(stalled, Warn) : "") +
                (S.KnowledgeBank > 0.5f ? $"   ·   {S.KnowledgeBank:0} banked" : ""), Label);
            GUI.Label(new Rect(r.x + 180, r.y + 32, r.width - 260, 20), q.Count > 0 ? "Queue: " + string.Join("   ", q) : Col("The queue is empty: click a topic to research it. Knowledge is banked meanwhile.", Warn), Small);
            if (_researchMsg.Length > 0 && Time.unscaledTime - _researchMsgTime < 5f) GUI.Label(new Rect(r.x + 180, r.y + 52, r.width - 260, 20), Col(_researchMsg, Warn), Small);
            GUI.Label(new Rect(r.x + 20, r.yMax - 28, r.width - 40, 22),
                Col("Click: queue it (missing prerequisites are queued first)   ·   Right-click: remove from the queue   ·   Esc or R: close", Dim), Small);

            // Grid.
            float top = r.y + 110, bottom = r.yMax - 36, left = r.x + 20, width = r.width - 40;
            float colW = width / _cols, nodeW = math.min(236, colW - 30), rowH = math.min(88, (bottom - top) / _rows), nodeH = math.min(70, rowH - 10);
            foreach (var (age, from, to) in _ageCols)
            {
                var a = D.Age(age);
                bool reached = a.order <= S.Age.order;
                var hr = new Rect(left + from * colW + 4, top - 40, (to - from + 1) * colW - 8, 28);
                Fill(hr, reached ? new Color(0.2f, 0.19f, 0.15f, 1) : new Color(0.13f, 0.13f, 0.13f, 1));
                GUI.Label(hr, (reached ? Col(a.name, Accent) : Col(a.name, Dim)) + (a.id == S.Age.id ? Col("   (now)", Dim) : reached ? "" : Col("   (reach it to research these)", Dim)), Center);
                if (from > 0) Fill(new Rect(left + from * colW - 1, top - 40, 2, bottom - top + 40), new Color(0.3f, 0.3f, 0.28f, 0.6f));
            }
            foreach (var n in _nodes) n.R = new Rect(left + n.Col * colW + (colW - nodeW) / 2, top + n.Row * rowH + (rowH - nodeH) / 2, nodeW, nodeH);

            // Prerequisite lines (elbows), drawn under the nodes.
            foreach (var n in _nodes)
                foreach (var p in n.Def.prereqs)
                {
                    if (!_byId.TryGetValue(p, out var pn)) continue;
                    bool known = S.IsResearched(p);
                    var c = known && S.IsResearched(n.Def.id) ? new Color(0.45f, 0.62f, 0.4f, 0.9f) : known ? new Color(0.75f, 0.68f, 0.5f, 0.9f) : new Color(0.38f, 0.38f, 0.36f, 0.9f);
                    float x0 = pn.R.xMax, y0 = pn.R.center.y, x1 = n.R.x, y1 = n.R.center.y;
                    float mx = x1 - (colW - nodeW) / 2 + 6;
                    Fill(new Rect(x0, y0 - 1, mx - x0, 2), c);
                    Fill(new Rect(mx - 1, math.min(y0, y1) - 1, 2, math.abs(y1 - y0) + 2), c);
                    Fill(new Rect(mx, y1 - 1, x1 - mx, 2), c);
                }

            foreach (var n in _nodes) DrawNode(n);
        }

        void DrawNode(Node n)
        {
            var d = n.Def; var st = StateOf(d); var r = n.R;
            Color bg, border, title;
            switch (st)
            {
                case RState.Known: bg = new Color(0.15f, 0.21f, 0.14f); border = new Color(0.38f, 0.55f, 0.34f); title = new Color(0.78f, 0.9f, 0.72f); break;
                case RState.Current: bg = new Color(0.27f, 0.22f, 0.13f); border = Accent; title = new Color(1f, 0.93f, 0.76f); break;
                case RState.Queued: bg = new Color(0.22f, 0.19f, 0.13f); border = new Color(0.62f, 0.52f, 0.32f); title = new Color(0.95f, 0.88f, 0.72f); break;
                case RState.Available: bg = new Color(0.19f, 0.195f, 0.19f); border = new Color(0.62f, 0.62f, 0.58f); title = Text; break;
                default: bg = new Color(0.12f, 0.12f, 0.12f); border = new Color(0.26f, 0.26f, 0.25f); title = Dim; break;
            }
            Fill(r, border);
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), bg);
            if (d.key) Fill(new Rect(r.x + 1, r.y + 1, 4, r.height - 2), Accent);
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && r.Contains(ev.mousePosition))
            {
                ClickResearch(d.id, ev.button == 1);
                ev.Use();
            }
            GUI.Label(new Rect(r.x + 12, r.y + 6, r.width - 50, 22), $"<b>{Col(d.name, title)}</b>", Label);
            int qi = S.ResearchQueue.IndexOf(d.id);
            if (qi >= 0)
            {
                var qr = new Rect(r.xMax - 30, r.y + 6, 22, 22);
                Fill(qr, Accent);
                GUI.Label(qr, $"<b>{Col((qi + 1).ToString(), new Color(0.15f, 0.12f, 0.08f))}</b>", Center);
            }
            else if (st == RState.Known) GUI.Label(new Rect(r.xMax - 30, r.y + 6, 22, 22), Col("✓", Good), Center);
            string sub = st switch
            {
                RState.Known => Col(d.cost > 0 ? "Known" : "Known from the start", new Color(0.55f, 0.7f, 0.5f)),
                RState.AgeLocked => Col($"{D.Age(d.age)?.name}", Faint),
                RState.Locked => Col("Needs " + string.Join(", ", d.prereqs.Where(p => !S.IsResearched(p)).Select(p => D.ResearchById(p)?.name)), Dim),
                _ => $"{d.cost:0} knowledge" + (d.key ? Col("  ·  key", Accent) : ""),
            };
            GUI.Label(new Rect(r.x + 12, r.y + 28, r.width - 20, 18), sub, new GUIStyle(Small) { fontSize = 12 });
            float prog = S.ResearchProgress(d.id);
            if (st != RState.Known && prog > 0) Bar(new Rect(r.x + 12, r.yMax - 14, r.width - 24, 7), prog, Accent);
            else
            {
                var unlocks = D.Buildings.Where(b => b.unlockedBy == d.id).Select(b => b.name).ToList();
                if (unlocks.Count > 0 && r.height > 56) GUI.Label(new Rect(r.x + 12, r.y + 46, r.width - 20, 16), Col("Unlocks " + string.Join(", ", unlocks), st == RState.Known ? new Color(0.5f, 0.6f, 0.48f) : Dim), new GUIStyle(Small) { fontSize = 11 });
            }
            Tip(r, ResearchTip(d, st));
        }

        /// <summary>-uishots: show a topic's tooltip as hovering it would.</summary>
        public void ShowResearchTip(string id)
        {
            if (_byId == null || !_byId.TryGetValue(id, out var n)) return;
            ForceTooltip(ResearchTip(n.Def, StateOf(n.Def)), new Vector2(n.R.center.x, n.R.center.y));
        }

        string ResearchTip(ResearchDef d, RState st)
        {
            var lines = new List<string> { $"<b>{d.name}</b>" + (d.key ? Col("   key research for the next age", Accent) : ""), d.description, "" };
            if (d.cost > 0) lines.Add($"Cost {d.cost:0} knowledge" + (st != RState.Known && S.ResearchProgress(d.id) > 0 ? $", {S.ResearchProgress(d.id):P0} done" : ""));
            if (d.prereqs.Length > 0) lines.Add("Needs " + string.Join(", ", d.prereqs.Select(p => S.IsResearched(p) ? Col(D.ResearchById(p)?.name, Good) : D.ResearchById(p)?.name)));
            var unlocks = D.Buildings.Where(b => b.unlockedBy == d.id).Select(b => b.name).ToList();
            if (unlocks.Count > 0) lines.Add("Unlocks " + string.Join(", ", unlocks));
            foreach (var e in d.effects) lines.Add(EffectText(e));
            if (st == RState.AgeLocked) lines.Add(Col($"Can be researched once the settlement reaches the {D.Age(d.age)?.name}.", Warn));
            return string.Join("\n", lines);
        }

        static string EffectText(Effect e)
        {
            string pct(float v) => $"{math.abs(v - 1f):P0}";
            var parts = e.stat.Split('.');
            switch (parts[0])
            {
                case "yield": return parts.Length > 1 && parts[1] == "irrigated" ? $"Irrigated fields +{pct(e.value)}" : $"{Cap(parts.Length > 1 ? parts[1] : "")} output +{pct(e.value)}";
                case "wear": return $"{Cap(parts.Length > 1 ? parts[1] : "")} last {(1f / e.value - 1f):P0} longer";
                case "spoil": return $"{Cap(parts.Length > 1 ? parts[1] : "")} spoils {pct(e.value)} slower";
                case "births": return $"Births +{pct(e.value)}";
                case "happiness": return $"Happiness {(e.value >= 1 ? "+" : "-")}{pct(e.value)}";
                case "build": return $"Building +{pct(e.value)}";
                case "reveal": return "Reveals the copper and tin deposits on the map";
                case "storage": return $"Every store holds +{pct(e.value)}";
                case "water": return $"Wells reach +{pct(e.value)} farther";
                case "traders": return "Traders call in spring, summer and autumn";
                case "migrants": return $"Migrants come +{pct(e.value)} more readily";
                case "knowledge": return $"Elders' knowledge {(e.value >= 1 ? "+" : "-")}{pct(e.value)}";
                case "unrest": return $"Unrest {(e.value >= 1 ? "+" : "-")}{pct(e.value)}";
                case "food": return $"Food eaten {(e.value >= 1 ? "+" : "-")}{pct(e.value)}";
                case "sickness": return $"Sickness {(e.value >= 1 ? "+" : "-")}{pct(e.value)} likely";
                case "law": return "Food is shared out evenly when short";
            }
            return $"{e.stat} x{e.value:0.##}";
        }
    }
}
