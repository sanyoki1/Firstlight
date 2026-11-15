using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // The right-hand panel for a selected building or citizen.
    public sealed partial class Hud
    {
        const float InfoW = 430;
        Rect InfoRect(float h) => new(VW - InfoW - 10, TopBarHeight + 8, InfoW, h);

        void DrawInfo()
        {
            if (Selected != null && Selected.Alive) DrawBuilding(Selected);
            else if (SelectedCitizen != null) DrawCitizen(SelectedCitizen);
        }

        // Simple vertical layout inside a panel.
        float _ly, _lx, _lw;
        void Row(string text, GUIStyle style = null, float h = 22) { GUI.Label(new Rect(_lx, _ly, _lw, h), text, style ?? Label); _ly += h; }
        void Para(string text, GUIStyle style = null)
        {
            style ??= SmallWrap;
            float h = style.CalcHeight(new GUIContent(text), _lw);
            GUI.Label(new Rect(_lx, _ly, _lw, h), text, style);
            _ly += h + 4;
        }
        void Gap(float h = 8) => _ly += h;
        void BarRow(string label, float v, Color c, string right)
        {
            GUI.Label(new Rect(_lx, _ly, 120, 20), label, Small);
            Bar(new Rect(_lx + 120, _ly + 5, _lw - 200, 11), v, c);
            GUI.Label(new Rect(_lx + _lw - 72, _ly, 72, 20), right, new GUIStyle(Small) { alignment = TextAnchor.MiddleRight });
            _ly += 22;
        }

        float _infoH = 300;

        Rect BeginInfo(string title)
        {
            var r = Box(InfoRect(_infoH));
            GUI.Label(new Rect(r.x + 16, r.y + 10, InfoW - 60, 26), title, Title);
            if (GUI.Button(new Rect(r.xMax - 34, r.y + 10, 24, 22), "x", Button)) { Select(null); SelectCitizen(null); }
            _lx = r.x + 16; _lw = InfoW - 32; _ly = r.y + 42;
            return r;
        }

        void EndInfo(Rect r) => _infoH = math.max(120, _ly - r.y + 12);

        // ------------------------------------------------------------------ buildings

        void DrawBuilding(Building b)
        {
            var d = b.Def;
            var r = BeginInfo(d.name);
            if (b.IsSite) DrawSite(b);
            else
            {
                string status = !string.IsNullOrEmpty(b.Status) ? b.Status : d.IsWorkplace ? (b.Workers > 0 ? "Working" : "No workers") : "";
                if (d.warmthRadius > 0) status = b.Fueled ? "The fire is burning" : "Out of firewood: the fire is out";
                if (b.Flooded) status = "Under flood water";
                if (status.Length > 0) Row(Col(status, b.Flooded ? Bad : status == "Working" || b.Fueled && d.warmthRadius > 0 ? Good : Warn), Bold);
                if (b.Flooded) Para(Col("No work, no storing and a cold home here until the river goes down; goods stored here were partly spoiled.", Warn));
                else if (S.FloodCells != null && UnderFlood(b) && !S.Flooding) Para(Col("On the low ground the river will flood tomorrow.", Warn));
                Para(d.description);
                if (d.crop) DrawCrop(b);
                if (d.IsWorkplace) DrawWorkplace(b);
                if (d.housing > 0) DrawHome(b);
                if (b.Store != null) DrawStore(b);
                DrawCoverage(b);
                DrawUpgrade(b);
            }
            Gap(6);
            if (b != S.TownCenter)
            {
                bool confirm = _confirmDemolish == b.Id;
                var br = new Rect(_lx, _ly, 200, 28);
                if (GUI.Button(br, confirm ? Col("Click again to remove", Bad) : "Demolish", Button)) Demolish(b);
                Tip(br, b.IsSite ? "Pull down the site. Delivered materials go back to storage." : $"Pull it down. {D.Balance.demolishRefund:P0} of the materials come back.");
                _ly += 32;
            }
            EndInfo(r);
        }

        void DrawSite(Building b)
        {
            Row(Col("Under construction", Warn), Bold);
            if (!string.IsNullOrEmpty(b.Def.onDeposit)) Row(Col($"On {S.DepositCells(b.Def, b.Origin, b.Rot)} cells of the {b.Def.onDeposit} deposit", Dim), Small, 20);
            BarRow("Progress", b.Progress, Accent, $"{b.Progress:P0}");
            for (int i = 0; i < b.Def.cost.Length; i++)
            {
                var c = b.Def.cost[i];
                float have = math.min(b.Delivered[i], c.amount);
                BarRow(ResName(c.id), have / math.max(0.01f, c.amount), have >= c.amount - 0.01f ? Good : Info, $"{have:0}/{c.amount:0}");
            }
            BarRow("Build work", b.Work, Accent, $"{b.Work:P0}");
            int builders = S.Citizens.Count(c => c.IsBuilder && c.TaskB2 == b);
            Row(builders > 0 ? $"{builders} builder{(builders == 1 ? "" : "s")} on it" : Col(S.BuildersWanted == 0 ? "No builders assigned (bottom bar)" : "Waiting for builders", Warn), Small, 20);
            var missing = b.Def.cost.Where((c, i) => b.Delivered[i] + b.InTransitOf(i) < c.amount - 0.01f && S.Stock(c.id) < 0.5f).Select(c => ResName(c.id).ToLower()).ToList();
            if (missing.Count > 0) Row(Col("No " + string.Join(", ", missing) + " in store", Bad), Small, 20);
            DrawBuildOrder(b);
            Gap(4);
            Para(b.Def.description);
        }

        /// <summary>Which sites the builders serve first (materials and work): High, then Normal, then Low.</summary>
        void DrawBuildOrder(Building b)
        {
            Gap(4);
            GUI.Label(new Rect(_lx, _ly + 4, 80, 22), "Build order", Small);
            string[] pn = { "Last", "Normal", "First" };
            for (int i = 0; i < 3; i++)
            {
                var pr = new Rect(_lx + 84 + i * 76, _ly, 72, 28);
                if (GUI.Button(pr, pn[i], (int)b.Priority == i ? ButtonOn : Button)) S.SetPriority(b, (Priority)i);
            }
            Tip(new Rect(_lx + 84, _ly, 226, 28), "Builders bring materials to, and build, the sites marked First before the others (oldest first within each). Use it when a material is short: the woodcutter before the huts.");
            _ly += 34;
        }

        /// <summary>Workers, priority, pause, focus, and output per day with the factors behind it.</summary>
        void DrawWorkplace(Building b)
        {
            var d = b.Def;
            Gap(4);
            BarRow("Workers", b.Workers / (float)math.max(1, d.maxWorkers), Good, $"{b.Workers} / {d.maxWorkers}");
            // Priority and pause.
            GUI.Label(new Rect(_lx, _ly + 4, 70, 22), "Priority", Small);
            string[] pn = { "Low", "Normal", "High" };
            for (int i = 0; i < 3; i++)
            {
                var pr = new Rect(_lx + 70 + i * 76, _ly, 72, 28);
                if (GUI.Button(pr, pn[i], (int)b.Priority == i ? ButtonOn : Button)) S.SetPriority(b, (Priority)i);
            }
            Tip(new Rect(_lx + 70, _ly, 226, 28), "High-priority jobs are filled first and take people from lower ones; builders come right after High.");
            var pa = new Rect(_lx + _lw - 92, _ly, 92, 28);
            if (GUI.Button(pa, b.Paused ? "Resume" : "Pause", b.Paused ? ButtonOn : Button)) S.SetPaused(b, !b.Paused);
            _ly += 34;
            if (d.outputs.Length >= 2 && !d.exclusiveOutputs)
            {
                GUI.Label(new Rect(_lx, _ly + 4, 70, 22), "Make", Small);
                float x = _lx + 72;
                for (int i = -1; i < d.outputs.Length; i++)
                {
                    string n = i < 0 ? "Both" : ResName(d.outputs[i].id);
                    var fr = new Rect(x, _ly, 100, 28);
                    if (GUI.Button(fr, n, b.Focus == i ? ButtonOn : Button)) S.SetFocus(b, i);
                    Tip(fr, i < 0 ? "Make the usual mix." : $"Make only {n.ToLower()}: the time spent on the rest goes into it, at {D.Balance.focusShare:P0}.");
                    x += 104;
                }
                _ly += 34;
            }

            // Rate: the settlement's own estimate (the sim's factors: season, nearby, research, difficulty, and each
            // worker's speed from mood, water and tools); the panel only formats it.
            var est = S.EstimateWorkplace(b);
            if (d.knowledgePerWorker > 0)
            {
                Row($"Knowledge  <b>about {est.Knowledge:0.#} a day</b>", Label);
                if (est.Elders > 0 && est.ElderKnowledge != D.Balance.elderKnowledgeMult)
                    Row($"Elders learn x{est.ElderKnowledge:0.##} (x{D.Balance.elderKnowledgeMult:0.##}, laws x{est.ElderKnowledge / D.Balance.elderKnowledgeMult:0.##})", Small, 20);
            }
            if (d.outputs.Length > 0 && !d.crop)
            {
                var outs = new List<string>();
                for (int i = 0; i < d.outputs.Length; i++)
                    if (S.OutputPerWorkday(b, i) > 0) outs.Add($"{est.Outputs[i]:0.#} {ResName(d.outputs[i].id).ToLower()}");
                Row("Makes  <b>about " + string.Join(", ", outs) + "</b> a day", Label);
                if (d.inputs.Length > 0)
                    Row("Uses  " + string.Join(", ", d.inputs.Select((g, i) => $"{est.Inputs[i]:0.#} {string.Join(" or ", g.ids.Select(ResName)).ToLower()}")) + " a day", Small, 20);
                var f = new List<string>();
                if (d.seasonYield != null && d.seasonYield.Length == 4) f.Add($"{S.SeasonName.ToLower()} x{est.Season:0.##}");
                if (d.HasNearby) f.Add($"{d.nearby.feature} {b.FeatureLeft:0} in reach x{est.Near:0.##}");
                if (d.usesTools) f.Add($"tools x{est.Tools:0.##}");
                f.Add($"mood x{est.Mood:0.##}");
                if (est.NoWater > 0) f.Add($"no clean water x{est.Water:0.##}");
                if (est.Research != 1f) f.Add($"research x{est.Research:0.##}");
                if (est.Difficulty != 1f) f.Add($"{S.Diff.name.ToLower()} x{est.Difficulty:0.##}");
                Para("At full workdays: " + string.Join(" · ", f) + (d.fieldWork ? ". Walking out and back takes some of the day." : "."));
                if (b.OutTotal > 0.5f) Row($"Waiting to be carried to storage: {b.OutTotal:0}", Small, 20);
            }
            var names = S.Citizens.Where(c => c.Job == b).Select(c => c.Name).ToList();
            if (names.Count > 0) Para("Workers: " + string.Join(", ", names));
        }

        void DrawHome(Building b)
        {
            Gap(4);
            BarRow("Residents", b.Residents / (float)math.max(1, b.Def.housing), Good, $"{b.Residents} / {b.Def.housing}");
            Row(b.Residents == 0 ? Col("Empty", Faint) : b.Heated ? Col("Heated: firewood today", Good) : S.Data.Balance.firewoodPerHome[S.Season] > 0 ? Col("No firewood today: cold inside", Bad) : "No fire needed this season", Small, 20);
            bool warm = S.TownCenter != null && math.distance(S.TownCenter.Center, b.Center) <= S.TownCenter.Def.warmthRadius;
            Row(warm ? "Within the campfire's warmth" : b.Def.homeWarmth >= 0.6f ? Col("Outside the campfire's warmth (warm enough when heated)", Dim) : Col("Outside the campfire's warmth", Warn), Small, 20);
            var has = new List<string>();
            if (S.NeedActive("water")) has.Add(b.HasWater ? Col("clean water", Good) : Col("no clean water nearby", Bad));
            if (S.NeedActive("goods")) has.Add(b.GoodsLife > 0 ? Col($"pottery ({b.GoodsLife:0} days)", Good) : Col("no pottery", Warn));
            if (S.NeedActive("faith")) has.Add(b.HasFaith ? Col("a shaman in reach", Good) : Col("no shaman in reach", Warn));
            if (has.Count > 0) Row("Household  " + string.Join(", ", has), Small, 20);
            var who = S.Citizens.Where(c => c.Home == b).Select(c => c.Age < D.Balance.adultAge ? $"{c.Name} ({(int)c.Age})" : c.Name).ToList();
            if (who.Count > 0) Para(string.Join(", ", who));
        }

        void DrawStore(Building b)
        {
            Gap(4);
            float cap = math.max(1, S.Capacity(b));
            BarRow("Stored", b.Stored / cap, b.Stored > cap * 0.9f ? Warn : Info, $"{b.Stored:0} / {cap:0}");
            var items = Enumerable.Range(0, S.ResCount).Where(i => b.Store[i] >= 0.5f).OrderByDescending(i => b.Store[i]).ToList();
            for (int k = 0; k < items.Count; k += 2)
            {
                GUI.Label(new Rect(_lx, _ly, _lw / 2, 20), $"{D.Resources[items[k]].name}  <b>{b.Store[items[k]]:0}</b>", Small);
                if (k + 1 < items.Count) GUI.Label(new Rect(_lx + _lw / 2, _ly, _lw / 2, 20), $"{D.Resources[items[k + 1]].name}  <b>{b.Store[items[k + 1]]:0}</b>", Small);
                _ly += 20;
            }
            if (items.Count == 0) Row(Col("Empty", Faint), Small, 20);
        }

        // ------------------------------------------------------------------ citizens

        string Doing(Citizen c)
        {
            switch (c.Activity)
            {
                case Activity.Working:
                    if (!c.AtField) return "Working";
                    if (c.Job != null && c.Job.Def.crop) return S.Season == 0 ? "Sowing the field" : S.Season == 1 ? "Tending the crop" : "Bringing in the harvest";
                    return c.Job?.Def.nearby?.feature switch { "wildlife" => "Out hunting", "rocks" => "Working at the rocks", "trees" => "Working in the woods", _ => "Working out" };
                case Activity.Building: return "Building";
                case Activity.Carrying: return "Carrying goods";
                case Activity.Fetching: return "Fetching materials";
                case Activity.Walking: return c.Job != null && c.TaskB == c.Job ? "Walking to work" : "Walking";
                case Activity.Resting: return c.Home != null ? "Resting at home" : "Resting by the fire";
                case Activity.Sick: return "Sick in bed";
                case Activity.Playing: return "Playing";
                case Activity.Leaving: return "Leaving the settlement";
                case Activity.Arriving: return "Arriving";
                default: return "Idle";
            }
        }

        void DrawCitizen(Citizen c)
        {
            var r = BeginInfo(c.Name);
            var bal = D.Balance;
            string stage = c.Age < bal.adultAge ? "child" : c.Age >= bal.elderAge ? "elder" : "adult";
            Row($"{(c.Female ? "Woman" : "Man")}, {(int)c.Age} ({stage})", Small, 20);
            Gap(4);
            string job = c.Job != null ? $"{c.Job.Def.workerTitle ?? "Worker"} at the {c.Job.Def.name}" : c.IsBuilder ? "Builder" : c.Age < bal.adultAge ? "Too young to work" : "No job: hauls goods and helps build";
            Row("Job  " + Col(job, Text), Label);
            Row("Home  " + (c.Home != null ? c.Home.Def.name : Col("none: sleeps by the fire", Warn)), Label);
            Row("Now  " + Doing(c), Label);
            int carry = c.Carrying ? c.CarryRes : c.LoadTotal > 0 ? c.LoadRes : -1;
            float amt = c.Carrying ? c.CarryAmt : c.LoadTotal;
            if (carry >= 0) Row((c.Carrying || c.Activity == Activity.Carrying ? "Carrying  " : "Gathered so far  ") + $"{amt:0.#} {D.Resources[carry].name.ToLower()}", Label);
            Gap(6);
            BarRow("Health", c.Health, c.Health > 0.6f ? Good : c.Health > 0.3f ? Warn : Bad, $"{c.Health:P0}");
            BarRow("Happiness", c.Happiness, c.Happiness > 0.6f ? Good : c.Happiness > bal.leaveHappiness ? Warn : Bad, $"{c.Happiness:P0}");
            BarRow("Warmth", math.saturate(c.Warmth / bal.warmthComfort), c.Cold ? Bad : Good, c.Cold ? "cold" : "warm");
            var needs = new List<string>();
            if (c.Hungry) needs.Add(Col("hungry", Bad));
            if (c.Cold) needs.Add(Col("cold", Bad));
            if (c.Sick) needs.Add(Col($"sick for {c.SickDays} more days" + (c.Treated ? ", treated" : ""), Warn));
            if (c.NoWater) needs.Add(Col("no clean water", Warn));
            if (c.NoGoods) needs.Add(Col("no pottery", Warn));
            if (c.NoFaith) needs.Add(Col("no faith", Warn));
            if (c.DisputeDays > 0) needs.Add(Col($"in a dispute ({c.DisputeDays} days off work)", Warn));
            if (c.UnhappyDays > 0) needs.Add(Col($"unhappy {c.UnhappyDays} days, may leave", Bad));
            if (needs.Count > 0) Para(string.Join(", ", needs)); else Row(Col("Fed, warm and well" + (S.Age.order >= 1 ? ", with all it needs" : ""), Good), Small, 20);
            Gap(4);
            Row("Tool  " + (c.Tool >= 0 ? $"{D.Resources[c.Tool].name}, {c.ToolLife:0} work days left" : Col("none", Faint)), Small, 20);
            Row("Clothing  " + (c.ClothingLife > 0 ? $"hide clothing, {c.ClothingLife:0} cold days left" : Col("none", Faint)), Small, 20);
            Gap(6);
            float bx = _lx;
            if (c.Job != null && GUI.Button(new Rect(bx, _ly, 130, 28), "Workplace", Button)) { var j = c.Job; Select(j); JumpTo(j.Center); }
            if (c.Job != null) bx += 136;
            if (c.Home != null && GUI.Button(new Rect(bx, _ly, 100, 28), "Home", Button)) { var h = c.Home; Select(h); JumpTo(h.Center); }
            if (GUI.Button(new Rect(_lx + _lw - 100, _ly, 100, 28), "Follow", Button)) JumpTo(c.Pos);
            _ly += 32;
            EndInfo(r);
        }
    }
}
