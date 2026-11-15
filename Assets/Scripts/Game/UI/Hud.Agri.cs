using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Agricultural Age HUD: field, upgrade, coverage and deposit sections of the building panel; the laws screen (L);
    // path drag-placement; the end-of-slice screen when the settlement reaches the Bronze Age.
    public sealed partial class Hud
    {
        public bool LawsOpen;
        public void ToggleLaws() { LawsOpen = !LawsOpen; if (LawsOpen) GoalsOpen = JobsOpen = StocksOpen = EventsOpen = false; }

        // ------------------------------------------------------------------ building panel sections

        static readonly string[] SeasonVerb = { "sowing", "growing", "harvest", "fallow" };

        /// <summary>A field: what is sown, how far sowing / growth / harvest are, the crop choice, the expected harvest.</summary>
        void DrawCrop(Building b)
        {
            var d = b.Def;
            Gap(4);
            int stage = b.CropStage(S.Season);
            string stageName = d.cropStages != null && stage < d.cropStages.Length ? d.cropStages[stage].name : "";
            int crop = b.Sown > 0 ? b.CropOut : math.max(0, b.Focus);
            string cropName = ResName(d.outputs[crop].id).ToLower();
            Row($"{stageName}  {Col($"· {cropName} · {SeasonVerb[S.Season]} season", Dim)}", Bold);
            BarRow("Sown", b.Sown, Good, $"{b.Sown:P0}");
            BarRow("Grown", b.Growth, Accent, $"{b.Growth:P0}");
            if (S.Season >= 2 || b.Harvested > 0) BarRow("Harvested", b.Harvested, Info, $"{b.Harvested:P0}");
            float expect = S.ExpectedHarvest(b);
            if (S.Season <= 2 && expect > 1) Row($"Expected harvest  <b>about {expect:0} {cropName}</b>", Label);
            var notes = new List<string>();
            if (d.irrigationRadius > 0)
                notes.Add(b.Irrigated ? (S.Effect("yield.irrigated") > 1f ? Col($"Irrigated: +{S.Effect("yield.irrigated") - 1f:P0}", Good) : Col("Water in reach: Irrigation would raise the harvest", Dim)) : Col("No water in reach to irrigate", Faint));
            if (S.Season == 1) notes.Add(Col("Tended crops grow faster", Dim));
            if (S.Season == 3) notes.Add(Col("Nothing grows in winter; the farmers do other work", Dim));
            if (notes.Count > 0) Para(string.Join("   ", notes));
            if (d.exclusiveOutputs && d.outputs.Length > 1)
            {
                GUI.Label(new Rect(_lx, _ly + 4, 70, 22), "Crop", Small);
                float x = _lx + 72;
                for (int i = 0; i < d.outputs.Length; i++)
                {
                    bool ok = S.ResourceUnlocked(d.outputs[i].id);
                    var r = new Rect(x, _ly, 110, 28);
                    string n = ResName(d.outputs[i].id);
                    if (GUI.Button(r, ok ? n : Col(n, Faint), math.max(0, b.Focus) == i ? ButtonOn : ok ? Button : ButtonDim) && ok) S.SetFocus(b, i);
                    var unlock = D.Resource(d.outputs[i].id)?.unlockedBy;
                    Tip(r, ok ? $"Sow {n.ToLower()}: about {d.outputs[i].amount:0} at a full harvest." + (b.Sown > 0 && i != b.CropOut ? " Takes effect at next spring's sowing." : "")
                              : $"Needs research: {D.ResearchById(unlock)?.name ?? unlock}");
                    x += 114;
                }
                _ly += 34;
            }
        }

        /// <summary>Rebuild in place: the next building up, its cost, and progress once started.</summary>
        void DrawUpgrade(Building b)
        {
            var t = S.UpgradeOption(b);
            if (t == null) return;
            Gap(6);
            if (b.UpgradeTo != null)
            {
                Row(Col($"Being rebuilt as a {t.name}", Warn), Bold);
                BarRow("Progress", b.Progress, Accent, $"{b.Progress:P0}");
                for (int i = 0; i < t.cost.Length && i < b.Delivered.Length; i++)
                {
                    var c = t.cost[i];
                    float have = math.min(b.Delivered[i], c.amount);
                    BarRow(ResName(c.id), have / math.max(0.01f, c.amount), have >= c.amount - 0.01f ? Good : Info, $"{have:0}/{c.amount:0}");
                }
                var cr = new Rect(_lx, _ly, 200, 28);
                if (GUI.Button(cr, "Stop the rebuild", Button)) S.CancelUpgrade(b);
                Tip(cr, "Delivered materials go back to storage. The old building stays as it is.");
                _ly += 32;
                return;
            }
            bool can = S.CanUpgrade(b, out string why);
            var r = new Rect(_lx, _ly, _lw, 30);
            string label = can ? $"Rebuild as a {t.name}  " + Col(Cost(t), Dim) : Col($"Rebuild as a {t.name}  ({why})", Faint);
            if (GUI.Button(r, label, can ? Button : ButtonDim) && can) UpgradeSelected();
            string gain = t.housing > 0 ? $"Houses {t.housing} (now {b.Def.housing}), warmth {t.homeWarmth:0.##} (now {b.Def.homeWarmth:0.##}). " : "";
            Tip(r, $"{gain}Builders bring the materials and rebuild it where it stands; people keep living here meanwhile.\n\n{t.description}");
            _ly += 34;
        }

        public void UpgradeSelected() { if (Selected != null) S.Upgrade(Selected, out _); }

        /// <summary>Wells, healers, shamans: the reach and how many homes it covers; councils: unrest and the laws.</summary>
        void DrawCoverage(Building b)
        {
            var d = b.Def;
            var homes = S.Buildings.Where(h => h.Active && h.Def.housing > 0 && h.Residents > 0).ToList();
            int Count(float radius) => homes.Count(h => math.distance(h.Center, b.Center) <= radius);
            if (d.waterRadius > 0)
            {
                float r = d.waterRadius * S.Effect("water.radius");
                Gap(4);
                Row($"Clean water for homes within {r:0}  " + Col($"· {Count(r)} homes in reach", Good), Label);
                if (!S.NeedActive("water")) Para(Col("Water becomes a need in the Agricultural Age.", Dim));
            }
            if (d.healthRadius > 0)
            {
                Gap(4);
                Row($"Treats the sick within {d.healthRadius}  " + Col($"· {Count(d.healthRadius)} homes in reach", b.Workers > 0 ? Good : Warn), Label);
                Para(Col($"In reach: sickness {1f - d.healChance:P0} less likely, {1f - d.healDamage:P0} less harmful (with herbs in store)." + (b.Workers == 0 ? " Needs a worker." : ""), Dim));
            }
            if (d.faithRadius > 0)
            {
                Gap(4);
                Row($"Faith for homes within {d.faithRadius}  " + Col($"· {Count(d.faithRadius)} homes in reach", b.Workers > 0 ? Good : Warn), Label);
                if (!S.NeedActive("faith")) Para(Col("Faith becomes a need once Shamanism is known.", Dim));
                else if (b.Workers == 0) Para(Col("Needs a shaman to keep the rites.", Warn));
            }
            if (d.orderPower > 0)
            {
                Gap(4);
                Row($"Unrest  <b>{S.Unrest:P0}</b>" + (b.Workers > 0 ? Col($"  · councillors calm it by {d.orderPower:P0}", Good) : Col("  · no councillors: no effect", Warn)), Label);
            }
            if (d.enablesLaws)
            {
                var lr = new Rect(_lx, _ly + 2, 200, 28);
                if (GUI.Button(lr, $"Laws {Col("L", Dim)}", LawsOpen ? ButtonOn : Button)) ToggleLaws();
                int n = S.ActiveLaws.Count();
                GUI.Label(new Rect(_lx + 210, _ly + 2, _lw - 210, 28), Col(n == 0 ? "No laws in force" : $"{n} law{(n == 1 ? "" : "s")} in force", Dim), Small);
                _ly += 34;
                if (n > 0) Para(Col(string.Join(", ", S.ActiveLaws.Select(id => D.Laws.Find(l => l.id == id)?.name)), Dim));
            }
            if (d.accepts.Length > 0)
            {
                Gap(2);
                Para(Col($"Holds {string.Join(", ", d.accepts)} only. Food here spoils at {d.spoilMultiplier:P0} of the open rate.", Dim));
            }
            if (!string.IsNullOrEmpty(d.onDeposit))
            {
                Gap(2);
                Para(Col($"Stands on {S.DepositCells(d, b.Origin)} cells of the {d.onDeposit} deposit; {b.FeatureLeft:0} in working reach.", Dim));
            }
        }

        // ------------------------------------------------------------------ laws (L)

        float _lawsH = 300;
        string _lawMsg = ""; float _lawMsgTime;

        void DrawLaws()
        {
            var r = BeginSide("Laws", 520, _lawsH, () => LawsOpen = false);
            var council = D.Buildings.Find(b => b.enablesLaws);
            var research = D.Laws.Count > 0 ? D.ResearchById(D.Laws[0].research) : null;
            if (research != null && !S.IsResearched(research.id)) Para(Col($"Laws need {research.name} and a {council?.name ?? "council"}.", Warn), Wrap);
            else if (!S.LawsInForce) Para(Col($"Build a {council?.name ?? "council"} to pass laws. Laws already passed are suspended without one.", Warn), Wrap);
            else Para($"The council can change each law once every {D.Balance.lawCooldownDays} days. Unrest is {Col($"{S.Unrest * 100:0}%", S.Unrest >= D.Balance.disputeAt ? Bad : S.Unrest >= D.Balance.unrestWarn ? Warn : Good)}.", Wrap);
            Gap(4);
            foreach (var l in D.Laws)
            {
                bool on = S.LawActive(l.id);
                bool can = S.CanSetLaw(l.id, !on, out string why);
                var br = new Rect(_lx, _ly, 112, 30);
                if (GUI.Button(br, on ? "<b>In force</b>" : "Pass", on ? ButtonOn : can ? Button : ButtonDim))
                {
                    if (!S.SetLaw(l.id, !on, out string msg)) { _lawMsg = msg; _lawMsgTime = Time.unscaledTime; } else _lawMsg = "";
                }
                Tip(br, on ? (can ? "Click to end the law." : why) : can ? "Click to pass the law." : why);
                GUI.Label(new Rect(_lx + 124, _ly, _lw - 124, 24), $"<b>{l.name}</b>", Label);
                _ly += 26;
                var old = _lx; _lx += 124; _lw -= 124;
                Para(l.description, SmallWrap);
                _lx = old; _lw += 124;
                Gap(6);
            }
            if (_lawMsg.Length > 0 && Time.unscaledTime - _lawMsgTime < 5f) Para(Col(_lawMsg, Warn));
            _lawsH = _ly - r.y + 8;
        }

        /// <summary>-uishots: pass or end a law as a click would.</summary>
        public bool ClickLaw(string id) => S.SetLaw(id, !S.LawActive(id), out _);

        // ------------------------------------------------------------------ paths (drag to lay a line of tiles)

        int2? _pathStart;
        readonly List<int2> _pathCells = new();

        /// <summary>Cells of an L-shaped run from a to b (along x, then along y), as a path is usually laid.</summary>
        public static void PathLine(int2 a, int2 b, List<int2> into)
        {
            into.Clear();
            int sx = b.x >= a.x ? 1 : -1, sy = b.y >= a.y ? 1 : -1;
            for (int x = a.x; x != b.x + sx; x += sx) into.Add(new int2(x, a.y));
            for (int y = a.y + sy; y != b.y + sy; y += sy) into.Add(new int2(b.x, y));
        }

        bool PlacingPath => Mode == ToolMode.Place && PlaceDef != null && PlaceDef.path;

        /// <summary>Path tool: press to start, drag for the line, release to lay it (tiles that can't go are skipped).</summary>
        void PathInput(bool overUI)
        {
            var mouse = Mouse.current;
            if (mouse == null || PointerOverride != null) return;
            PathDrag(!overUI && mouse.leftButton.wasPressedThisFrame, mouse.leftButton.wasReleasedThisFrame);
        }

        /// <summary>
        /// The drag handler itself (the mouse, and -p4shots with an aimed pointer, both come through here): a press at the
        /// pointer starts the line, every frame the line follows the pointer, a release lays it.
        /// </summary>
        public void PathDrag(bool pressed, bool released)
        {
            UpdateHover();
            if (pressed && HoverValid) _pathStart = HoverCell;
            if (_pathStart != null && HoverValid) PathLine(_pathStart.Value, HoverCell, _pathCells);
            if (_pathStart != null && released)
            {
                int n = LayPath(_pathCells);
                if (n > 0) G.Sound?.Click();
                _pathStart = null;
                _pathCells.Clear();
            }
        }

        /// <summary>Lays path tiles on the given cells (the -uishots mode calls this with a line). Returns how many went down.</summary>
        public int LayPath(List<int2> cells)
        {
            int n = 0;
            foreach (var c in cells) if (S.PlacePath(c)) n++;
            return n;
        }

        // ------------------------------------------------------------------ end of the slice

        public bool EndShown, EndOpen;

        /// <summary>
        /// Shown once when the settlement reaches the last age in this build: what it took, and that the sandbox goes on.
        /// </summary>
        void DrawEndScreen()
        {
            var last = D.Ages[D.Ages.Count - 1];
            var w = 760f; var h = 470f;
            var r = Box(new Rect(VW / 2 - w / 2, VH / 2 - h / 2 - 20, w, h));
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.1f, 0.105f, 0.1f, 0.97f));
            GUI.Label(new Rect(r.x, r.y + 22, w, 40), $"You have reached the {last.name}", new GUIStyle(Title) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            var up = S.AgeLog.FirstOrDefault(a => a.age == last.id);
            int days = up.age == last.id ? up.day : S.Day;
            float years = days / (float)S.DaysPerYear;
            GUI.Label(new Rect(r.x + 40, r.y + 70, w - 80, 26), Col($"From a campfire to the edge of the {last.name} in {years:0.#} years.", Dim), new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter });
            float y = r.y + 112, cx = r.x + 70, cw = (w - 140) / 2;
            int buildings = S.Buildings.Count(b => b.Active && !b.Def.path);
            var lines = new (string, string)[]
            {
                ("People", PeopleLine()),
                ("Born / arrived", $"{S.Births} born, {S.Migrants} came to stay"),
                ("Left / died", $"{S.Leavers} left, {S.TotalDeaths} died" + (S.Deaths.Count > 0 ? $" ({string.Join(", ", S.Deaths.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}"))})" : "")),
                ("Buildings", $"{buildings}, {S.PathCount} path tiles"),
                ("Research", $"{S.ResearchLog.Count} topics learned"),
                ("Winters", $"{S.WintersSurvived} survived"),
                ("Food in store", $"{S.FoodStock:0} ({FoodDaysText(S.FoodDays)})"),
            };
            foreach (var (k, v) in lines)
            {
                GUI.Label(new Rect(cx, y, 170, 24), Col(k, Dim), Label);
                GUI.Label(new Rect(cx + 170, y, w - 140 - 170, 24), $"<b>{v}</b>", Label);
                y += 28;
            }
            y += 10;
            var note = new GUIStyle(SmallWrap) { alignment = TextAnchor.UpperCenter, fontSize = 15 };
            GUI.Label(new Rect(r.x + 50, y, w - 100, 50), $"This build ends here: the buildings and research of the {last.name} are not in it yet. You can keep playing the settlement as a sandbox for as long as you like.", note);
            var br = new Rect(r.x + w / 2 - 110, r.yMax - 60, 220, 38);
            if (GUI.Button(br, "<b>Keep playing</b>", ButtonOn)) EndOpen = false;
        }

        /// <summary>Opens the end screen once, when the last age is reached (and the age-up banner has had its moment).</summary>
        void CheckEnd()
        {
            if (EndShown || D.Ages.Count == 0) return;
            var last = D.Ages[D.Ages.Count - 1];
            if (S.Age?.id != last.id) return;
            if (_bannerTime >= 0 && _bannerTime < 4f) return;
            EndShown = EndOpen = true;
        }

        // ------------------------------------------------------------------ when everyone is gone

        public bool GoneShown, GoneOpen;

        /// <summary>Opens once when the last person has died or left (and the leavers have walked off): the game is over.</summary>
        void CheckGone()
        {
            if (S.NeedsCampfire || S.Population > 0) { GoneShown = false; return; }
            if (GoneShown || S.Departing.Count > 0) return;
            GoneShown = GoneOpen = true;
            EndOpen = false;
            G.SetSpeed(0);
        }

        /// <summary>The settlement has no people left: what happened, and the ways on (a save, a new game, the menu).</summary>
        void DrawGoneScreen()
        {
            var w = 700f; var h = 380f;
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.35f));
            var r = Box(new Rect(VW / 2 - w / 2, VH / 2 - h / 2 - 20, w, h));
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.1f, 0.105f, 0.1f, 0.97f));
            GUI.Label(new Rect(r.x, r.y + 22, w, 40), "The settlement is empty", new GUIStyle(Title) { fontSize = 30, alignment = TextAnchor.MiddleCenter });
            float years = S.Day / (float)S.DaysPerYear;
            GUI.Label(new Rect(r.x + 40, r.y + 70, w - 80, 26), Col($"The last of its people are gone after {years:0.#} years, in the {S.Age.name}.", Dim), new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter });
            float y = r.y + 112, cx = r.x + 70;
            var lines = new (string, string)[]
            {
                ("Born / arrived", $"{S.Births} born, {S.Migrants} came to stay"),
                ("Left / died", $"{S.Leavers} left, {S.TotalDeaths} died" + (S.Deaths.Count > 0 ? $" ({string.Join(", ", S.Deaths.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} {kv.Value}"))})" : "")),
                ("Winters", $"{S.WintersSurvived} survived"),
                ("Research", $"{S.ResearchLog.Count} topics learned"),
            };
            foreach (var (k, v) in lines)
            {
                GUI.Label(new Rect(cx, y, 170, 24), Col(k, Dim), Label);
                GUI.Label(new Rect(cx + 170, y, w - 140 - 170, 24), $"<b>{v}</b>", Label);
                y += 28;
            }
            GUI.Label(new Rect(r.x + 50, y + 8, w - 100, 44), "Nobody new will come to an empty camp. Load a save to try again from an earlier point, or start a new settlement.", new GUIStyle(SmallWrap) { alignment = TextAnchor.UpperCenter, fontSize = 15 });
            float by = r.yMax - 60, bw = 190;
            if (GUI.Button(new Rect(r.x + w / 2 - bw * 1.5f - 12, by, bw, 38), "<b>Load a save</b>", ButtonOn)) { GoneOpen = false; OpenMenu(); ShowScreen(MenuScreen.Load); }
            if (GUI.Button(new Rect(r.x + w / 2 - bw / 2, by, bw, 38), "Main menu", Button)) { GoneOpen = false; G.ToMainMenu(); }
            if (GUI.Button(new Rect(r.x + w / 2 + bw / 2 + 12, by, bw, 38), "Look around", Button)) GoneOpen = false;
        }

        // ------------------------------------------------------------------ needs in the top bar

        /// <summary>The needs that have arrived, as "Water 92%" (only once they apply), and unrest; row two of the top bar.</summary>
        void DrawNeedsStrip(float x, float y, float right)
        {
            {
                float hp = S.AverageHappiness;
                var r = new Rect(x, y, 116, 26);
                GUI.Label(new Rect(x, y + 3, 56, 20), "Happy", Small);
                GUI.Label(new Rect(x + 56, y + 2, 56, 22), Col($"<b>{hp:P0}</b>", hp >= 0.6f ? Good : hp >= D.Balance.leaveHappiness + 0.05f ? Warn : Bad), Label);
                Tip(r, $"Average happiness {hp:P0}: a blend of the average and the worst of each person's needs (food, warmth, shelter, health, and later water, pottery and faith). Happy people work faster; below {D.Balance.leaveHappiness:P0} for {D.Balance.leaveAfterDays:0} days they leave.");
                x += 120;
            }
            foreach (var n in D.Balance.needs)
            {
                float met = S.NeedMet(n.id);
                if (met < 0) continue;
                if (x + 116 > right) return;
                var r = new Rect(x, y, 116, 26);
                GUI.Label(new Rect(x, y + 3, 56, 20), n.id == "water" ? "Water" : n.name, Small);
                GUI.Label(new Rect(x + 56, y + 2, 56, 22), Col($"<b>{met:P0}</b>", met >= 0.9f ? Good : met >= 0.6f ? Warn : Bad), Label);
                Tip(r, $"{n.name}: {met:P0} of people have it. Unmet, it holds their happiness down to about {n.unmet:P0}." +
                       (n.id == "water" ? " Homes near the river or within a well's reach have clean water; without it people also fall sick more." : n.id == "goods" ? " Each household keeps pots in use and they wear out; a clay pit and potters keep them coming." : n.id == "faith" ? " Homes within reach of a staffed Shaman's Hut have it; without it unrest grows." : ""));
                x += 120;
            }
            if (S.Age.order >= 1 && x + 116 <= right)
            {
                var r = new Rect(x, y, 116, 26);
                float u = S.Unrest;
                GUI.Label(new Rect(x, y + 3, 56, 20), "Unrest", Small);
                GUI.Label(new Rect(x + 56, y + 2, 56, 22), Col($"<b>{u:P0}</b>", u >= D.Balance.disputeAt ? Bad : u >= D.Balance.unrestWarn ? Warn : Good), Label);
                Tip(r, $"Unrest {u:P0} (heading for {S.UnrestTarget:P0}). It grows with unhappiness, crowding, people without faith and sheer numbers. Above {D.Balance.disputeAt:P0} disputes cost work days; above {D.Balance.theftAt:P0} goods go missing. A staffed council and laws calm it.");
            }
        }
    }
}
