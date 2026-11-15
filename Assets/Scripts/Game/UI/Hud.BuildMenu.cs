using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Bottom-left bar: build categories, demolish, builders; the list of buildings for the open category.
    public sealed partial class Hud
    {
        static readonly string[] CategoryOrder = { "Housing", "Food", "Materials", "Crafts", "Metal", "Storage", "Knowledge", "Services", "Paths" };
        string[] _cats;
        string[] Categories => _cats ??= CategoryOrder.Where(c => D.Buildings.Any(b => b.category == c && !b.townCenter))
            .Concat(D.Buildings.Where(b => !b.townCenter).Select(b => b.category).Distinct().Where(c => !CategoryOrder.Contains(c))).ToArray();

        /// <summary>Buildings shown in a category: those of the current age or earlier (locked ones greyed).</summary>
        public IEnumerable<BuildingDef> InCategory(string cat) =>
            D.Buildings.Where(b => b.category == cat && !b.townCenter && D.AgeOrder(b.age) <= S.Age.order);

        void DrawBuildBar()
        {
            float y = VH - 60;
            var cats = Categories;
            float bw = cats.Length > 7 ? 100 : 118;
            float w = 12 + cats.Length * bw + 130 + 220;
            var bar = Box(new Rect(10, y, w, 50), UiTheme.Header);
            float x = bar.x + 8;
            foreach (var c in cats)
            {
                var r = new Rect(x, y + 8, bw - 6, 34);
                int ready = InCategory(c).Count(S.IsUnlocked);
                if (GUI.Button(r, ready > 0 ? c : Col(c, Faint), BuildCategory == c ? ButtonOn : Button)) OpenCategory(c);
                x += bw;
            }
            var dr = new Rect(x + 4, y + 8, 120, 34);
            if (GUI.Button(dr, $"Demolish {Col("X", Faint)}", Mode == ToolMode.Demolish ? ButtonOn : Button))
            {
                if (Mode == ToolMode.Demolish) CancelTool(); else StartDemolish();
            }
            x += 136;
            // Builders: how many people are kept on construction while there are sites.
            GUI.Label(new Rect(x, y + 4, 100, 20), "Builders", Small);
            int sites = S.Sites.Count();
            GUI.Label(new Rect(x, y + 24, 110, 20), sites > 0 ? $"{sites} site{(sites == 1 ? "" : "s")} open" : Col("no sites", Dim), Small);
            if (GUI.Button(new Rect(x + 108, y + 11, 30, 28), "-", Button)) S.SetBuilders(S.BuildersWanted - 1);
            GUI.Label(new Rect(x + 140, y + 11, 36, 28), $"<b>{S.BuildersWanted}</b>", Center);
            if (GUI.Button(new Rect(x + 178, y + 11, 30, 28), "+", Button)) S.SetBuilders(S.BuildersWanted + 1);
            Tip(new Rect(x, y, 210, 50), "How many people build and carry materials to sites, taken before normal-priority jobs. They go back to other work when there is nothing to build.");

            if (BuildCategory != null) DrawCategory(BuildCategory, y);
        }

        GUIStyle _costStyle, _summaryStyle;

        void DrawCategory(string cat, float barY)
        {
            var list = InCategory(cat).ToList();
            const float rowH = 54, w = 400;
            float h = 40 + list.Count * rowH + 8;
            var r = Box(new Rect(10, barY - h - 6, w, h));
            GUI.Label(new Rect(r.x + 14, r.y + 8, w - 60, 24), cat, Title);
            if (GUI.Button(new Rect(r.xMax - 32, r.y + 8, 24, 22), "x", Button)) BuildCategory = null;
            float y = r.y + 38;
            foreach (var d in list)
            {
                bool ok = S.IsUnlocked(d);
                var row = new Rect(r.x + 8, y, w - 16, rowH - 4);
                bool on = Mode == ToolMode.Place && PlaceDef == d;
                if (GUI.Button(row, GUIContent.none, ok ? (on ? ButtonOn : Button) : ButtonDim) && ok) StartPlacing(d);
                // Locked rows in Dim, not Faint: "Needs Pottery" is what the player needs to read.
                GUI.Label(new Rect(row.x + 10, row.y + 4, 220, 22), ok ? $"<b>{d.name}</b>" : Col(d.name, Dim), Label);
                _costStyle ??= new GUIStyle(Small);
                _costStyle.fontSize = Px(13);
                GUI.Label(new Rect(row.x + 10, row.y + 26, 360, 20), ok ? Cost(d) : Col("Needs " + (D.ResearchById(d.unlockedBy)?.name ?? d.unlockedBy), Warn), _costStyle);
                _summaryStyle ??= new GUIStyle(Small) { alignment = TextAnchor.MiddleRight };
                _summaryStyle.fontSize = Small.fontSize;
                GUI.Label(new Rect(row.x + 200, row.y + 4, 176, 22), Col(Summary(d), Dim), _summaryStyle);
                Tip(row, Describe(d));
                y += rowH;
            }
        }

        /// <summary>One short line: what the building is for.</summary>
        string Summary(BuildingDef d)
        {
            if (d.path) return "drag to lay";
            if (d.crop) return $"{d.maxWorkers} farmers · " + string.Join(" or ", d.outputs.Select(o => ResName(o.id).ToLower()));
            if (d.waterRadius > 0) return $"water within {d.waterRadius}";
            if (d.housing > 0) return $"houses {d.housing}";
            if (d.storageCapacity > 0) return $"stores {d.storageCapacity}";
            if (d.knowledgePerWorker > 0) return $"{d.maxWorkers} workers · knowledge";
            if (d.outputs.Length > 0) return $"{d.maxWorkers} worker{(d.maxWorkers == 1 ? "" : "s")} · " + string.Join(", ", d.outputs.Select(o => ResName(o.id).ToLower()));
            return "";
        }

        /// <summary>Full tooltip for a building type.</summary>
        string Describe(BuildingDef d)
        {
            var lines = new List<string> { $"<b>{d.name}</b>", d.description, "" };
            if (d.cost.Length > 0) lines.Add("Cost: " + Cost(d) + $"   build work {d.buildWork:0.#}");
            if (d.maxWorkers > 0) lines.Add($"Workers: up to {d.maxWorkers}" + (d.requiresTools ? Col(", need tools", Warn) : d.usesTools ? ", faster with tools" : ""));
            if (d.crop) lines.Add("Full harvest per field: " + string.Join(" or ", d.outputs.Select(o => $"{o.amount:0} {ResName(o.id).ToLower()}")) + $"; sowing {d.sowWork:0.#} and harvest {d.harvestWork:0.#} worker-days");
            else if (d.outputs.Length > 0) lines.Add("Makes per worker-day: " + string.Join(", ", d.outputs.Select(o => $"{o.amount:0.#} {ResName(o.id).ToLower()}")));
            if (d.inputs.Length > 0) lines.Add("Uses per worker-day: " + string.Join(", ", d.inputs.Select(i => $"{i.amount:0.#} {string.Join(" or ", i.ids.Select(ResName)).ToLower()}")));
            if (d.knowledgePerWorker > 0) lines.Add($"Knowledge per worker-day: {d.knowledgePerWorker:0.#} (elders x{D.Balance.elderKnowledgeMult:0.#})");
            if (d.HasNearby) lines.Add($"Needs {d.nearby.feature} within {d.nearby.radius}: at least {d.nearby.min}, full yield at {d.nearby.full}");
            if (d.fieldWork) lines.Add("Workers walk out to work and carry their loads back.");
            if (d.seasonYield != null && d.seasonYield.Length == 4) lines.Add("By season: " + string.Join(" / ", d.seasonYield.Select(v => $"{v:0.##}")) + "  (spring / summer / autumn / winter)");
            if (d.housing > 0) lines.Add($"Houses {d.housing}, warmth {d.homeWarmth:0.##}");
            if (d.storageCapacity > 0) lines.Add($"Holds {d.storageCapacity} goods");
            if (d.warmthRadius > 0) lines.Add($"Warms homes within {d.warmthRadius}");
            if (d.healthRadius > 0) lines.Add($"Treats the sick within {d.healthRadius}");
            if (d.waterRadius > 0) lines.Add($"Clean water for homes within {d.waterRadius}");
            if (d.faithRadius > 0) lines.Add($"Faith for homes within {d.faithRadius}");
            if (d.orderPower > 0) lines.Add($"Calms unrest by {d.orderPower:P0} while staffed" + (d.enablesLaws ? "; passes laws" : ""));
            if (!string.IsNullOrEmpty(d.onDeposit)) lines.Add($"Must stand on a {d.onDeposit} deposit");
            if (!string.IsNullOrEmpty(d.upgradesTo)) lines.Add($"Can be rebuilt as a {D.Building(d.upgradesTo)?.name}");
            if (d.unique) lines.Add("Only one can be built.");
            if (!S.IsUnlocked(d)) lines.Add(Col("Needs research: " + (D.ResearchById(d.unlockedBy)?.name ?? d.unlockedBy), Warn));
            return string.Join("\n", lines).Replace("\n\n\n", "\n\n");
        }
    }
}
