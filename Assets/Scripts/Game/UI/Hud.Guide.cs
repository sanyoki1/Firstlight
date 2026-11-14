using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Onboarding: a small objectives card for a first game (each step ticks itself off by watching the settlement, with
    // one sentence of why), one-time hints when something new happens, and the traders' offer. None of it blocks input.
    public sealed partial class Hud
    {
        // ------------------------------------------------------------------ the guide

        sealed class GuideStep
        {
            public string Title, Why;
            public Func<Hud, bool> Done;
            public Func<Hud, string> Progress;      // optional "12 of 40" line
            public Func<Hud, BuildingDef> Build;    // optional: the building this step asks for (a button opens it)
        }

        static BuildingDef Def(Hud h, string id) => h.D.Building(id);
        static bool Has(Hud h, string id) => h.S.CountBuildings(id, true) > 0;

        static readonly GuideStep[] Steps =
        {
            new() { Title = "Place the campfire", Why = "The band settles around it: it warms the homes near it and holds the first goods.", Done = h => !h.S.NeedsCampfire },
            new() { Title = "Build a lean-to", Why = "People without a roof lose health in the cold nights. Open Housing (B) and place it near the fire.", Done = h => Has(h, "lean_to") || Has(h, "hide_tent"), Build = h => Def(h, "lean_to") },
            new() { Title = "Build a gatherers' camp", Why = "Roots, nuts and berries from the woods around it: the band's first steady food.", Done = h => Has(h, "gatherers_camp"), Build = h => Def(h, "gatherers_camp") },
            new() { Title = "Build a woodcutter", Why = "Wood for every building, and firewood: without it homes and the campfire go cold in winter.", Done = h => Has(h, "woodcutter"), Build = h => Def(h, "woodcutter") },
            new() { Title = "Build a storage pile", Why = "The campfire holds little; goods with nowhere to go pile up at the workplaces and spoil.", Done = h => Has(h, "storage_pile"), Build = h => Def(h, "storage_pile") },
            new() { Title = "Build the Elders' Fire and queue Basic Tools", Why = "Elders turn stories into knowledge; Basic Tools comes first because it unlocks tools, spears, mining and farming. Research is R.",
                    Done = h => Has(h, "elders_fire") && (h.S.IsResearched("basic_tools") || h.S.ResearchQueue.Contains("basic_tools")), Build = h => Has(h, "elders_fire") ? null : Def(h, "elders_fire"),
                    Progress = h => (Has(h, "elders_fire") ? Col("Elders' Fire placed", Good) : "Elders' Fire not placed") + "  ·  " + (h.S.IsResearched("basic_tools") || h.S.ResearchQueue.Contains("basic_tools") ? Col("Basic Tools queued", Good) : "Basic Tools not queued") },
            new() { Title = "Prepare for winter", Why = "Nothing grows in winter. Stock firewood for the homes and fire, and food for the cold days; raise a building's priority to push it.",
                    Done = h => h.S.WintersSurvived >= 1 || h.WinterReady(out _), Progress = h => { h.WinterReady(out string p); return p; } },
            new() { Title = "Build a toolmaker", Why = "Once Basic Tools is known: workers with tools work faster, and fields and mines need them.", Done = h => Has(h, "toolmaker"), Build = h => Def(h, "toolmaker"),
                    Progress = h => h.S.IsResearched("basic_tools") ? Col("Basic Tools is known", Good) : "Waiting for Basic Tools" },
            new() { Title = "Look at the goals (G)", Why = "Goals list the key research and milestones that lead to the Agricultural Age. From here the settlement is yours.", Done = h => h._goalsSeen },
        };

        int _guideStep;                 // first step not yet done
        bool _guideFrozen;              // -p4shots: a step shown on purpose stays
        public bool GuideHidden;        // closed for this game
        bool _goalsSeen;

        bool GuideActive => GameSettings.Current.guide && !GuideHidden && _guideStep < Steps.Length;

        /// <summary>Firewood for the days to spring and food for 10 days (the targets the guide's winter step watches).</summary>
        bool WinterReady(out string progress)
        {
            float fuel = math.max(20f, S.FuelNeedUntilSpring()), food = math.max(1f, S.DailyFoodNeed) * 10f;
            float haveFuel = S.Stock(D.Balance.fuelResource), haveFood = S.FoodStock;
            progress = $"Firewood {Col($"{haveFuel:0} / {fuel:0}", haveFuel >= fuel ? Good : Warn)}   Food {Col($"{haveFood:0} / {food:0}", haveFood >= food ? Good : Warn)}";
            return haveFuel >= fuel && haveFood >= food;
        }

        void UpdateGuide()
        {
            if (!GameSettings.Current.guide || GuideHidden) return;
            if (GoalsOpen) _goalsSeen = true;
            while (!_guideFrozen && _guideStep < Steps.Length && Steps[_guideStep].Done(this)) _guideStep++;
            if (_guideStep >= Steps.Length && !GameSettings.Current.guideDone) { GameSettings.Current.guideDone = true; GameSettings.Save(); }
        }

        /// <summary>-p4shots: show the guide at a given step.</summary>
        public void GuideShowStep(int step) { _guideStep = math.clamp(step, 0, Steps.Length - 1); GuideHidden = false; _guideFrozen = step >= 0; }
        public void GuideRelease() => _guideFrozen = false;
        public int GuideStepIndex => _guideStep;

        void DrawGuide()
        {
            if (!GuideActive || EndOpen) return;
            var st = Steps[_guideStep];
            string prog = st.Progress?.Invoke(this);
            var build = st.Build?.Invoke(this);
            float w = 400;
            var why = new GUIContent(st.Why);
            float h = 44 + SmallWrap.CalcHeight(why, w - 28) + (prog != null ? 24 : 0) + (build != null && S.IsUnlocked(build) ? 36 : 0) + 12;
            float x = BuildCategory != null ? 420 : 10;
            var r = Box(new Rect(x, VH - 60 - h - 8, w, h));
            Fill(new Rect(r.x + 1, r.y + 1, 3, r.height - 2), Accent);
            GUI.Label(new Rect(r.x + 14, r.y + 8, w - 90, 20), Col($"FIRST STEPS  {_guideStep + 1} / {Steps.Length}", Faint), new GUIStyle(Small) { fontSize = 12 });
            if (GUI.Button(new Rect(r.xMax - 64, r.y + 6, 56, 20), Col("hide", Dim), new GUIStyle(Button) { fontSize = 12 })) { GuideHidden = true; GameSettings.Current.guideDone = true; GameSettings.Save(); }
            Tip(new Rect(r.xMax - 64, r.y + 6, 56, 20), "Hide the guide for this game (Settings can turn it back on).");
            GUI.Label(new Rect(r.x + 14, r.y + 24, w - 28, 22), $"<b>{st.Title}</b>", Label);
            float y = r.y + 48;
            float wh = SmallWrap.CalcHeight(why, w - 28);
            GUI.Label(new Rect(r.x + 14, y, w - 28, wh), st.Why, SmallWrap);
            y += wh + 2;
            if (prog != null) { GUI.Label(new Rect(r.x + 14, y, w - 28, 22), prog, Small); y += 24; }
            if (build != null && S.IsUnlocked(build))
            {
                var br = new Rect(r.x + 14, y + 2, 200, 28);
                if (GUI.Button(br, $"Place a {build.name}", Mode == ToolMode.Place && PlaceDef == build ? ButtonOn : Button)) { BuildCategory = build.category; StartPlacing(build); }
            }
        }

        // ------------------------------------------------------------------ one-time hints

        sealed class HintDef { public string Id, Title, Text; public Func<Hud, bool> When; public Func<Hud, float2?> Where; }

        static readonly HintDef[] Hints =
        {
            new() { Id = "winter", Title = "Winter has come", Text = "Nothing grows and little is gathered until spring. Homes burn firewood every day; people without warmth, food or a roof lose health. Keep the woodcutters on firewood and the food stores up.", When = h => h.S.Season == 3 },
            new() { Id = "death", Title = "Someone has died", Text = "The event list says why. Cold means not enough firewood, homes or clothing; hunger means too little food; sickness spreads in crowded homes. Old age is natural.", When = h => h.S.TotalDeaths > 0 },
            new() { Id = "need_water", Title = "A new need: clean water", Text = "Farming towns need clean water. Homes near the river have it; others need a well within reach. Without it people are less happy and fall sick more.", When = h => h.S.NeedActive("water") },
            new() { Id = "need_goods", Title = "A new need: pottery", Text = "Now that pottery is known every household wants pots. A clay pit and a potter keep them supplied; pots wear out over time.", When = h => h.S.NeedActive("goods") },
            new() { Id = "need_faith", Title = "A new need: faith", Text = "With Shamanism known, people want a staffed Shaman's Hut within reach of their homes. Unmet faith lowers happiness and adds to unrest.", When = h => h.S.NeedActive("faith") },
            new() { Id = "outbreak", Title = "Sickness is spreading", Text = "Outbreaks start in crowded homes without clean water. A staffed Healer's Hut with herbs in store treats the sick and keeps outbreaks from starting.", When = h => h.S.Outbreaks > 0 },
            new() { Id = "laws", Title = "Laws can be passed", Text = "Tribal Law is known. Build a Tribal Council and open Laws (L): each law has a cost and a benefit, and can be changed every few days.", When = h => h.S.IsResearched("tribal_law") },
            new() { Id = "ore", Title = "Ore deposits found", Text = "Prospecting shows copper and tin as stained ground with outcrops. Mines must stand on their deposit; the placement shows where it is.", When = h => h.M.OreRevealed },
            new() { Id = "flood", Title = "The river is rising", Text = "In spring the river can flood the low ground beside it. Buildings under water stop working and stored goods spoil, but flooded fields grow more that year.", When = h => h.S.FloodWarned || h.S.Flooding, Where = h => h.FloodSpot() },
            new() { Id = "traders", Title = "Traders", Text = "Traders call in spring, summer and autumn and offer what the settlement is short of for its biggest surplus. Accept or decline; the offer stands until the end of the next day.", When = h => h.S.PendingTrade != null },
        };

        readonly HashSet<string> _hintsSeen = new();
        HintDef _hint;

        float2? FloodSpot()
        {
            if (S.FloodCells == null) return null;
            foreach (var b in S.Buildings) if (b.Alive && b.Def.housing >= 0 && UnderFlood(b)) return b.Center;
            return null;
        }

        /// <summary>Whether any of a building's cells lie in the coming or current flood.</summary>
        public bool UnderFlood(Building b)
        {
            var cells = S.FloodCells;
            if (cells == null) return false;
            for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                    if (M.InBounds(x, y) && cells[M.Cell(x, y)]) return true;
            return false;
        }

        void UpdateHints()
        {
            // A hint whose moment has passed goes by itself (an undismissed "Winter has come" stood into the summer of
            // year 12 in the screenshot run, and held back every later hint).
            if (_hint != null && !_hintForced && !_hint.When(this)) _hint = null;
            if (!GameSettings.Current.hints || _hint != null || S.NeedsCampfire) return;
            foreach (var h in Hints)
                if (!_hintsSeen.Contains(h.Id) && h.When(this)) { _hint = h; _hintForced = false; _hintsSeen.Add(h.Id); return; }
        }

        /// <summary>-p4shots: show a hint as if it had just come up.</summary>
        public void ShowHint(string id) { _hint = Hints.FirstOrDefault(h => h.Id == id); _hintForced = _hint != null; if (_hint != null) _hintsSeen.Add(id); }
        bool _hintForced;

        void DrawHint()
        {
            if (_hint == null || ResearchOpen || EndOpen) return;
            float w = 520;
            var text = new GUIContent(_hint.Text);
            float th = SmallWrap.CalcHeight(text, w - 32);
            float h = 44 + th + 46;
            var r = Box(new Rect(VW / 2 - w / 2, TopBarHeight + 12, w, h));
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, 3), Info);
            GUI.Label(new Rect(r.x + 16, r.y + 12, w - 32, 24), $"<b>{_hint.Title}</b>", Label);
            GUI.Label(new Rect(r.x + 16, r.y + 40, w - 32, th), _hint.Text, SmallWrap);
            if (GUI.Button(new Rect(r.x + 16, r.yMax - 40, 120, 28), "Got it", Button)) _hint = null;
            var where = _hint?.Where?.Invoke(this);
            if (where != null && GUI.Button(new Rect(r.x + 146, r.yMax - 40, 120, 28), "Show me", Button)) JumpTo(where.Value);
            if (GUI.Button(new Rect(r.xMax - 176, r.yMax - 40, 160, 28), Col("No more hints", Dim), Button)) { _hint = null; GameSettings.Current.hints = false; GameSettings.Save(); }
        }

        // ------------------------------------------------------------------ traders' offer

        void DrawTrade()
        {
            var o = S.PendingTrade;
            if (o == null || ResearchOpen || EndOpen) return;
            float w = 440, h = 196;
            bool side = Selected != null || SelectedCitizen != null;
            var r = Box(new Rect(side ? VW - InfoW - w - 20 : VW - w - 10, TopBarHeight + 8, w, h));
            Fill(new Rect(r.x + 1, r.y + 1, r.width - 2, 3), Accent);
            var give = D.Resources[o.Give]; var want = D.Resources[o.Want];
            GUI.Label(new Rect(r.x + 16, r.y + 12, w - 32, 24), "<b>Traders have come</b>", Label);
            GUI.Label(new Rect(r.x + 16, r.y + 38, w - 32, 22), $"They offer <b>{o.GetAmount:0} {want.name.ToLower()}</b> for <b>{o.GiveAmount:0} {give.name.ToLower()}</b>.", Label);
            GUI.Label(new Rect(r.x + 16, r.y + 62, w - 32, 40), Col($"In store: {S.StockTotals[o.Want]:0} {want.name.ToLower()}, {S.StockTotals[o.Give]:0} {give.name.ToLower()}. The offer stands until the end of {(S.Day > o.Day ? "today" : "tomorrow")}.", Dim), SmallWrap);
            if (GUI.Button(new Rect(r.x + 16, r.y + 110, 130, 32), "<b>Accept</b>", ButtonOn)) AcceptTrade();
            if (GUI.Button(new Rect(r.x + 156, r.y + 110, 130, 32), "Decline", Button)) S.DeclineTrade();
            var tr = new Rect(r.x + 16, r.y + 154, w - 32, 26);
            bool auto = S.AutoTrade;
            if (GUI.Button(tr, GUIContent.none, GUIStyle.none)) S.AutoTrade = !auto;
            GUI.Label(tr, (auto ? Col("[x]", Good) : Col("[  ]", Dim)) + "  Always accept traders' offers", Small);
            Tip(tr, "Take every offer without asking (the auto-player does this).");
        }

        public void AcceptTrade() { if (S.AcceptTrade()) G.Sound?.Click(); }

        // ------------------------------------------------------------------ save / load of the HUD's state

        public void WriteState(SaveWriter w)
        {
            w.Bool(EndShown);
            w.Int(_guideStep); w.Bool(GuideHidden); w.Bool(_goalsSeen);
            w.Strs(_hintsSeen.OrderBy(x => x).ToList());
        }

        public void ReadState(SaveReader r)
        {
            EndShown = r.Bool();
            _guideStep = r.Int(); GuideHidden = r.Bool(); _goalsSeen = r.Bool();
            _hintsSeen.Clear(); foreach (var id in r.Strs()) _hintsSeen.Add(id);
            _lastAgeLogs = S.AgeLog.Count;          // no age-up banner for an age reached before the save
        }

        /// <summary>
        /// A new game: the guide only if the player has not finished or closed it before. A settlement that is already
        /// running (a save without the HUD's state) starts with the guide closed and its past moments' hints seen.
        /// </summary>
        void InitGuide()
        {
            GuideHidden = GameSettings.Current.guideDone;
            _lastAgeLogs = S.AgeLog.Count;
            if (S.NeedsCampfire) return;
            GuideHidden = true;
            EndShown = D.Ages.Count > 0 && S.Age?.id == D.Ages[D.Ages.Count - 1].id;
            foreach (var h in Hints) if (h.When(this)) _hintsSeen.Add(h.Id);
        }
    }
}
