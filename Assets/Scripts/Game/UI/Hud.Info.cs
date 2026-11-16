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
            if (Selected != null && Selected.OnMap) DrawBuilding(Selected);
            else if (SelectedCitizen != null) DrawCitizen(SelectedCitizen);
        }

        // Simple vertical layout inside a panel.
        float _ly, _lx, _lw;
        void Row(string text, GUIStyle style = null, float h = 22) { GUI.Label(new Rect(_lx, _ly, _lw, h), text, style ?? Label); _ly += h; }
        void Para(string text, GUIStyle style = null)
        {
            style ??= SmallWrap;
            float h = style.CalcHeight(Temp(text), _lw);
            GUI.Label(new Rect(_lx, _ly, _lw, h), text, style);
            _ly += h + 4;
        }
        void Gap(float h = 8) => _ly += h;
        void BarRow(string label, float v, Color c, string right)
        {
            GUI.Label(new Rect(_lx, _ly, 120, 20), label, Small);
            Bar(new Rect(_lx + 120, _ly + 5, _lw - 200, 11), v, c);
            GUI.Label(new Rect(_lx + _lw - 72, _ly, 72, 20), right, SmallRight);
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
            if (b.IsPile) { DrawLeftGoods(b); EndInfo(r); return; }
            if (b.Dismantling) DrawDismantling(b);
            if (b.IsSite) DrawSite(b);
            else
            {
                string status = !string.IsNullOrEmpty(b.Status) ? b.Status : d.IsWorkplace ? (b.Workers > 0 ? "Working" : "No workers") : "";
                if (d.warmthRadius > 0) status = b.Fueled ? "The fire is burning" : "Out of firewood: the fire is out";
                if (b.Flooded) status = "Under flood water";
                if (status.Length > 0) Row(Col(StatusText(b, status), b.Flooded ? Bad : status == "Working" || b.Fueled && d.warmthRadius > 0 ? Good : Warn), Bold);
                if (b.Flooded) Para(Col("No work, no storing and a cold home here until the river goes down; goods stored here were partly spoiled.", Warn));
                else if (S.FloodCells != null && UnderFlood(b) && !S.Flooding) Para(Col("On the low ground the river will flood tomorrow.", Warn));
                Para(d.description);
                if (d.crop) DrawCrop(b);
                if (d.IsWorkplace) DrawWorkplace(b);
                if (d.housing > 0) DrawHome(b);
                if (b.Store != null) DrawStore(b);
                DrawCoverage(b);
                if (!b.Dismantling) DrawUpgrade(b);
            }
            Gap(6);
            if (b.Dismantling)
            {
                var br = new Rect(_lx, _ly, 200, 28);
                if (GUI.Button(br, "Cancel demolition", Button)) S.CancelDismantle(b);
                if (Over(br)) Tip(br, b.IsSite ? "Take the mark off: the site is built on from where it stands." : "Take the mark off: the building stays, whole, and goes back to work.");
                _ly += 32;
            }
            else if (b != S.TownCenter)
            {
                bool confirm = _confirmDemolish == b.Id;
                var br = new Rect(_lx, _ly, 200, 28);
                if (GUI.Button(br, confirm ? Col("Click again to demolish", Bad) : "Demolish", Button)) Demolish(b);
                if (Over(br)) Tip(br, b.IsSite ? $"Mark the site for demolition. Comes back: {GoodsText(S.DismantleReturn(b, false))} (all that was delivered)." : $"Mark it for demolition: {DemolishLoss(b)}. You can cancel until the work is done.");
                _ly += 32;
            }
            EndInfo(r);
        }

        /// <summary>A building marked for dismantling: how far the work is, who is on it, what comes back and what it means meanwhile.</summary>
        void DrawDismantling(Building b)
        {
            var d = b.Def;
            Row(Col("Marked for demolition", Warn), Bold);
            BarRow("Still standing", b.Work, Warn, $"{b.Work:P0}");
            int builders = S.Citizens.Count(c => c.TaskB2 == b && (c.Activity == Activity.Building || c.Activity == Activity.Walking));
            Row(builders > 0 ? $"{Many(builders, "builder", "builders")} on it, about {S.DismantleWorkLeft(b):0.#} builder-days of work left" : Col(SiteWait(b), Warn), Small, 20);
            Para($"Comes back when it is down: {Col(GoodsText(S.DismantleReturn(b, false)), Good)}" + (b.IsSite ? " (all that was delivered)." : $" ({D.Balance.dismantleRefund:P0} of the building materials, rounded down).") + " The goods lie on the plot until they are carried to a store.");
            if (b.Active && d.housing > 0 && b.Residents > 0) Para(Col($"{Many(b.Residents, "person lives", "people live")} here until it is down; then they need another home. Nobody new moves in.", Warn));
            if (b.Active && b.Store != null) Para(b.Stored >= 0.5f ? Col($"It takes no more goods. The {b.Stored:0} it holds are being moved to other stores; what is still here when it comes down stays on the plot to be carried off.", Warn) : "It takes no more goods.");
            if (b.Active && d.IsWorkplace) Para("Nobody works here while it is marked." + (b.OutTotal + b.In.Sum() >= 0.5f ? " Goods still here stay on the plot to be carried off." : ""));
            if (b.Active) { DrawBuildOrder(b); Gap(4); }
        }

        /// <summary>What a dismantled building left on its plot: the goods waiting to be carried to a store.</summary>
        void DrawLeftGoods(Building b)
        {
            Row(Col("Demolished", Dim), Bold);
            Para("These goods lie here until they are carried to a store. The ground is free again once they are gone.");
            bool anyRoom = false;
            for (int g = 0; g < b.Out.Length; g++)
            {
                if (b.Out[g] < 0.05f) continue;
                bool room = S.Buildings.Any(s => s.Active && s.Store != null && !s.Flooded && S.Accepts(s, g) && S.Capacity(s) - s.Stored >= 0.5f);
                anyRoom |= room;
                Row($"{D.Resources[g].name}: <b>{b.Out[g]:0.#}</b>" + (room ? "" : Col("   no store has room for it", Bad)), Small, 20);
            }
            if (!anyRoom) Para(Col("No store has room for any of this: build a store, free some room, or clear the goods away.", Warn));
            Gap(6);
            bool confirm = _confirmDemolish == b.Id;
            var br = new Rect(_lx, _ly, 200, 28);
            if (GUI.Button(br, confirm ? Col("Click again: goods are lost", Bad) : "Clear away", Button))
            {
                if (!confirm) _confirmDemolish = b.Id;
                else { S.ClearPile(b); _confirmDemolish = -1; Select(null); }
            }
            if (Over(br)) Tip(br, "Give these goods up: they are lost, and the ground is free at once.");
            _ly += 32;
        }

        /// <summary>
        /// A workplace status that names the cause: "Waiting for inputs" says which input and whether any is in store, and
        /// "Needs tools" says when none are in store. (The sim's own strings stay as they are: the auto-player reads them.)
        /// </summary>
        string StatusText(Building b, string status)
        {
            var d = b.Def;
            if (status == "Waiting for inputs" && d.inputs.Length > 0)
            {
                var empty = new List<string>();
                bool noneInStore = true;
                for (int i = 0; i < d.inputs.Length; i++)
                {
                    float have = 0, store = 0;
                    foreach (var id in d.inputs[i].ids) { int r = S.ResIndex(id); if (r >= 0) { have += b.In[r]; store += S.StockTotals[r]; } }
                    if (have > 0.001f) continue;
                    empty.Add(string.Join(" or ", d.inputs[i].ids.Select(ResName)).ToLower());
                    if (store >= 0.5f) noneInStore = false;
                }
                if (empty.Count > 0) return $"Waiting for {string.Join(" and ", empty)}" + (noneInStore ? " (none in store)" : " (on its way from storage)");
            }
            if (status == "Needs tools" && ToolsInStore() < 1f) return "Needs tools (none in store: a Toolmaker makes them)";
            return status;
        }

        void DrawSite(Building b)
        {
            Row(Col("Under construction", Warn), Bold);
            if (!string.IsNullOrEmpty(b.Def.onDeposit)) Row(Col($"On {Many(S.DepositCells(b.Def, b.Origin, b.Rot), "cell", "cells")} of the {b.Def.onDeposit} deposit", Dim), Small, 20);
            // "Overall": materials and build work together (it read as a second, different progress bar).
            BarRow("Overall", b.Progress, Accent, $"{b.Progress:P0}");
            for (int i = 0; i < b.Def.cost.Length; i++)
            {
                var c = b.Def.cost[i];
                float have = math.min(b.Delivered[i], c.amount);
                BarRow(ResName(c.id), have / math.max(0.01f, c.amount), have >= c.amount - 0.01f ? Good : Info, $"{have:0}/{c.amount:0}");
            }
            BarRow("Build work", b.Work, Accent, $"{b.Work:P0}");
            int builders = S.Citizens.Count(c => c.IsBuilder && c.TaskB2 == b);
            Row(builders > 0 ? $"{builders} builder{(builders == 1 ? "" : "s")} on it" : Col(SiteWait(b), Warn), Small, 20);
            var missing = b.Def.cost.Where((c, i) => b.Delivered[i] + b.InTransitOf(i) < c.amount - 0.01f && S.Stock(c.id) < 0.5f).Select(c => ResName(c.id).ToLower()).ToList();
            if (missing.Count > 0) Row(Col("No " + string.Join(", ", missing) + " in store", Bad), Small, 20);
            DrawBuildOrder(b);
            Gap(4);
            Para(b.Def.description);
        }

        /// <summary>
        /// Why nobody is building a site: no builders at all; outside work hours (they rest until the workday starts); or
        /// other sites come first (higher build order, or the same and older).
        /// </summary>
        string SiteWait(Building b)
        {
            if (S.BuildersWanted == 0) return "No builders assigned (bottom bar)";
            var bal = D.Balance;
            if (!S.IsWorkTime)
            {
                float h = bal.workdayStart * 24f;
                return $"Builders are resting; work starts at {(int)h:00}:{(int)((h - (int)h) * 60) / 5 * 5:00}";
            }
            int ahead = 0;
            foreach (var s in S.Buildings)
                if (s != b && s.UnderWork && (s.Priority > b.Priority || s.Priority == b.Priority && s.Id < b.Id)) ahead++;
            if (ahead == 0) return "Waiting for builders";
            return $"Waiting for builders: {ahead} site{(ahead == 1 ? " is" : "s are")} ahead of it" + (b.Priority != Priority.High ? " (Build order: First moves it up)" : "");
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
                    if (Over(fr)) Tip(fr, i < 0 ? "Make the usual mix." : $"Make only {n.ToLower()}: the time spent on the rest goes into it, at {D.Balance.focusShare:P0}.");
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
            Row(b.Residents == 0 ? Col("Empty", Dim) : b.Heated ? Col("Heated: firewood today", Good) : S.Data.Balance.firewoodPerHome[S.Season] > 0 ? Col("No firewood today: cold inside", Bad) : "No fire needed this season", Small, 20);
            bool warm = S.TownCenter != null && math.distance(S.TownCenter.Center, b.Center) <= S.TownCenter.Def.warmthRadius;
            Row(warm ? "Within the campfire's warmth" : b.Def.homeWarmth >= 0.6f ? Col("Outside the campfire's warmth (warm enough when heated)", Dim) : Col("Outside the campfire's warmth", Warn), Small, 20);
            var has = new List<string>();
            if (S.NeedActive("water")) has.Add(b.HasWater ? Col("clean water", Good) : Col("no clean water nearby", Bad));
            if (S.NeedActive("goods")) has.Add(b.GoodsLife > 0 ? Col($"pots in use ({Days(b.GoodsLife, "0")} left)", Good) : Col("no pots", Warn));
            if (S.NeedActive("faith")) has.Add(b.HasFaith ? Col("a shaman in reach", Good) : Col("no shaman in reach", Warn));
            if (has.Count > 0) Row("Household  " + string.Join(", ", has), Small, 20);
            var who = S.Citizens.Where(c => c.Home == b).Select(c => c.Age < D.Balance.adultAge ? $"{c.Name} ({(int)c.Age})" : c.Name).ToList();
            if (who.Count > 0) Para(string.Join(", ", who));
        }

        readonly List<int> _storeItems = new();

        void DrawStore(Building b)
        {
            Gap(4);
            float cap = math.max(1, S.Capacity(b));
            BarRow("Stored", b.Stored / cap, b.Stored > cap * 0.9f ? Warn : Info, $"{b.Stored:0} / {cap:0}");
            // Goods held, most first (as Enumerable.OrderByDescending: a stable sort, ties keep the resource order).
            var items = _storeItems; items.Clear();
            for (int i = 0; i < S.ResCount; i++) if (b.Store[i] >= 0.5f) items.Add(i);
            for (int k = 1; k < items.Count; k++)
            {
                int v = items[k], j = k - 1;
                while (j >= 0 && b.Store[items[j]] < b.Store[v]) { items[j + 1] = items[j]; j--; }
                items[j + 1] = v;
            }
            for (int k = 0; k < items.Count; k += 2)
            {
                GUI.Label(new Rect(_lx, _ly, _lw / 2, 20), StoreItem(b, items[k]), Small);
                if (k + 1 < items.Count) GUI.Label(new Rect(_lx + _lw / 2, _ly, _lw / 2, 20), StoreItem(b, items[k + 1]), Small);
                _ly += 20;
            }
            if (items.Count == 0) Row(Col("Empty", Dim), Small, 20);
            DrawStoreControls(b);          // Hud.Storage.cs
        }

        /// <summary>"Wood  40", dimmed with "moving out" when the store no longer takes it or holds more than its limit.</summary>
        string StoreItem(Building b, int r)
        {
            float amt = b.Store[r], cap = S.KeepMaxOf(b, r);
            if (!S.Accepts(b, r) || cap > 0 && amt > cap + 0.5f) return Col($"{D.Resources[r].name}  <b>{amt:0}</b> (moving out)", Dim);
            return $"{D.Resources[r].name}  <b>{amt:0}</b>";
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

        readonly List<Settlement.HappinessPart> _happyParts = new();

        void DrawCitizen(Citizen c)
        {
            var r = BeginInfo(c.Name);
            var bal = D.Balance;
            string stage = c.Age < bal.adultAge ? "child" : c.Age >= bal.elderAge ? "elder" : "adult";
            Row($"{(c.Female ? "Woman" : "Man")}, {(int)c.Age} ({stage})", Small, 20);
            Gap(4);
            string job = c.Job != null ? $"{c.Job.Def.workerTitle ?? "Worker"} at the {c.Job.Def.name}" : c.IsBuilder ? "Builder" : c.IsHauler ? "Hauler: carries goods to and between stores" : c.Age < bal.adultAge ? "Too young to work" : "No job: hauls goods and helps build";
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
            if (c.Home == null) needs.Add(Col("no home", Warn));
            if (c.Hungry) needs.Add(Col("hungry", Bad));
            if (c.Cold) needs.Add(Col("cold", Bad));
            if (c.Sick) needs.Add(Col($"sick for {c.SickDays} more {(c.SickDays == 1 ? "day" : "days")}" + (c.Treated ? ", treated" : ""), Warn));
            if (c.NoWater) needs.Add(Col("no clean water", Warn));
            if (c.NoGoods) needs.Add(Col("no pots at home", Warn));
            if (c.NoFaith) needs.Add(Col("no faith", Warn));
            if (c.DisputeDays > 0) needs.Add(Col($"in a dispute ({Many(c.DisputeDays, "day", "days")} off work)", Warn));
            if (c.UnhappyDays > 0) needs.Add(Col($"unhappy {Many(c.UnhappyDays, "day", "days")}, may leave", Bad));
            if (needs.Count > 0) Para(string.Join(", ", needs)); else Row(Col("Fed, warm and well" + (S.Age.order >= 1 ? ", with all it needs" : ""), Good), Small, 20);
            // What holds happiness down, from the sim's own weighing of the needs (shown while the bar is not green).
            if (c.Happiness < 0.6f)
            {
                float target = S.ExplainHappiness(c, _happyParts);
                string toward = $"heading for {target:P0}";
                if (_happyParts.Count == 0) Para(Col($"Every need is met: happiness is rising ({toward}).", Dim));
                else
                {
                    var held = new List<string>();
                    for (int i = 0; i < _happyParts.Count && i < 4; i++) held.Add(_happyParts[i].What);
                    Para("Held down by: " + Col(string.Join(", ", held), Warn) + Col($"  ({toward})", Dim));
                }
            }
            Gap(4);
            Row("Tool  " + (c.Tool >= 0 ? $"{D.Resources[c.Tool].name}, {c.ToolLife:0} work {(c.ToolLife.ToString("0") == "1" ? "day" : "days")} left" : Col("none", Warn)), Small, 20);
            Row("Clothing  " + (c.ClothingLife > 0 ? $"{c.ClothingLife:0} cold {(c.ClothingLife.ToString("0") == "1" ? "day" : "days")} left" : Col("none", Dim)), Small, 20);
            Gap(6);
            float bx = _lx;
            if (c.Job != null && GUI.Button(new Rect(bx, _ly, 130, 28), "Workplace", Button)) { var j = c.Job; Select(j); JumpTo(j.Center); }
            if (c.Job != null) bx += 136;
            if (c.Home != null && GUI.Button(new Rect(bx, _ly, 100, 28), "Home", Button)) { var h = c.Home; Select(h); JumpTo(h.Center); }
            var fr = new Rect(_lx + _lw - 110, _ly, 110, 28);
            bool following = Following == c;
            if (GUI.Button(fr, following ? "Following" : "Follow", following ? ButtonOn : Button)) { if (following) Following = null; else Follow(c); }
            Tip(fr, following ? "The camera follows them until you move it or close this panel. Click to stop." : "Keep the camera on them until you move it or close this panel.");
            _ly += 32;
            EndInfo(r);
        }
    }
}
