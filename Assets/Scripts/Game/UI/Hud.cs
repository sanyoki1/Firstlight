using System.Collections.Generic;
using System.Linq;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    /// <summary>
    /// The in-game HUD (IMGUI) and the mouse/keyboard input that acts on the world. Split over partial files:
    /// this one (state, the actions every click goes through, world input, drawing order, tooltips), TopBar,
    /// BuildMenu, Info (building and citizen panels), Research, Goals, Feed (events and warnings) and Panels
    /// (jobs, stocks, help). The -uishots mode calls the same action methods a click calls.
    ///
    /// Click-through: every panel drawn registers its rectangle; world clicks, wheel zoom and hover are ignored
    /// while the pointer is over one (using last frame's rectangles, as OnGUI runs after Update). While the
    /// research screen is open the world gets no input at all and WASD is released.
    /// </summary>
    public sealed partial class Hud
    {
        readonly GameController G;
        Settlement S => G.Sim;
        GameData D => G.Data;
        GameMap M => G.Map;

        public enum ToolMode { None, Place, Demolish }
        public ToolMode Mode { get; private set; }
        public BuildingDef PlaceDef { get; private set; }
        public Building Selected { get; private set; }
        public Citizen SelectedCitizen { get; private set; }
        public bool ResearchOpen, GoalsOpen, JobsOpen, HelpOpen, StocksOpen;
        public string BuildCategory;              // open build-menu category (null = closed)
        public bool Hidden;                       // F11 hides the HUD (clean screenshots)

        /// <summary>Screen pixel position used instead of the mouse (set by -uishots to aim the placement ghost).</summary>
        public Vector2? PointerOverride;
        public Vector2 PointerScreen => PointerOverride ?? (Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width / 2f, Screen.height / 2f));

        // Last placement check (for the ghost text and colouring).
        public int2 HoverCell { get; private set; }
        public bool HoverValid { get; private set; }
        public bool PlaceOk { get; private set; }
        public string PlaceReason { get; private set; } = "";
        public Building HoverBuilding { get; private set; }

        readonly List<Rect> _rects = new(), _prevRects = new();
        string _tip;
        float _bannerTime = -1; string _banner = "";
        int _lastAgeLogs;
        Vector2 _rightDown; bool _rightHeld;
        int _confirmDemolish = -1;

        public Hud(GameController g) { G = g; InitGuide(); }

        // ================================================================== actions (mouse and -uishots both call these)

        public void StartPlacing(BuildingDef d)
        {
            if (d == null) return;
            if (PlaceDef != d) PlaceRot = 0;
            _pathStart = null; _pathCells.Clear();      // a drag cut off by Esc or a right-click must not linger
            Mode = ToolMode.Place; PlaceDef = d;
            Select(null); SelectCitizen(null);
        }

        public void StartDemolish() { Mode = ToolMode.Demolish; PlaceDef = null; BuildCategory = null; }

        public void CancelTool()
        {
            if (S.NeedsCampfire) return;          // the campfire has to be placed first
            Mode = ToolMode.None; PlaceDef = null;
            _pathStart = null; _pathCells.Clear();
        }

        public void Select(Building b)
        {
            Selected = b; _confirmDemolish = -1;
            if (b != null) SelectedCitizen = null;
        }

        public void SelectCitizen(Citizen c) { SelectedCitizen = c; if (c != null) Selected = null; }

        public void ToggleResearch() { ResearchOpen = !ResearchOpen; if (ResearchOpen) { GoalsOpen = JobsOpen = StocksOpen = HelpOpen = EventsOpen = false; } }
        public void ToggleGoals() { GoalsOpen = !GoalsOpen; if (GoalsOpen) JobsOpen = StocksOpen = LawsOpen = EventsOpen = false; }
        public void ToggleJobs() { JobsOpen = !JobsOpen; if (JobsOpen) GoalsOpen = StocksOpen = LawsOpen = EventsOpen = false; }
        public void ToggleStocks() { StocksOpen = !StocksOpen; if (StocksOpen) GoalsOpen = JobsOpen = LawsOpen = EventsOpen = false; }
        public void ToggleHelp() => HelpOpen = !HelpOpen;
        public void OpenCategory(string cat) { BuildCategory = BuildCategory == cat ? null : cat; }

        /// <summary>Esc: closes the topmost thing open, else drops the tool, else the selection.</summary>
        public void Back()
        {
            if (MenuOpen) { MenuBack(); return; }
            if (GoneOpen) GoneOpen = false;
            else if (EndOpen) EndOpen = false;
            else if (ResearchOpen) ResearchOpen = false;
            else if (HelpOpen) HelpOpen = false;
            else if (Mode != ToolMode.None && !S.NeedsCampfire) CancelTool();
            else if (BuildCategory != null) BuildCategory = null;
            else if (GoalsOpen || JobsOpen || StocksOpen || LawsOpen || EventsOpen) GoalsOpen = JobsOpen = StocksOpen = LawsOpen = EventsOpen = false;
            else if (Selected != null || SelectedCitizen != null) { Select(null); SelectCitizen(null); }
            else OpenMenu();                       // nothing left to close: the game menu
        }

        /// <summary>A left click on the world at the pointer: place, demolish or select.</summary>
        public void ClickWorld(bool keepTool = false)
        {
            UpdateHover();
            if (!HoverValid) return;
            if (Mode == ToolMode.Place && PlaceDef != null && PlaceDef.path)
            {
                if (!S.PlacePath(HoverCell, out string pwhy)) PlaceReason = pwhy;
                return;
            }
            if (Mode == ToolMode.Place && PlaceDef != null)
            {
                var b = S.Place(PlaceDef, PlaceOrigin(PlaceDef, HoverCell, PlaceRot), PlaceRot, out string why);
                if (b == null) { PlaceReason = why; return; }
                if (b.Def.townCenter) OnCampfirePlaced(b);
                if (!keepTool || b.Def.unique) { Mode = ToolMode.None; PlaceDef = null; }
                return;
            }
            if (Mode == ToolMode.Demolish)
            {
                // A finished building takes a second click on it within 2 s (the tip says what goes with it); sites and
                // path tiles go at once (a site's delivered materials go back to storage).
                var hb = HoverBuilding;
                if (hb != null && !hb.IsSite && hb != S.TownCenter && !ToolConfirmPending(hb))
                {
                    _toolConfirm = hb.Id; _toolConfirmAt = Time.unscaledTime;
                    return;
                }
                _toolConfirm = -1;
                if (hb != null && S.Demolish(hb, out _) && !keepTool) Mode = ToolMode.None;
                else if (HoverBuilding == null && S.RemovePath(HoverCell) && !keepTool) Mode = ToolMode.None;
                return;
            }
            var person = G.SView.CitizenNear(HoverPoint, 0.55f);
            if (person != null) { SelectCitizen(person); return; }
            Select(HoverBuilding);
            if (HoverBuilding == null) SelectCitizen(null);
        }

        // The demolish tool's pending confirm: which building was clicked once, and when.
        int _toolConfirm = -1; float _toolConfirmAt;
        bool ToolConfirmPending(Building b) => b != null && _toolConfirm == b.Id && Time.unscaledTime - _toolConfirmAt <= 2f;

        /// <summary>What goes with a building pulled down: "8 live here; half the materials come back".</summary>
        string DemolishLoss(Building b)
        {
            var parts = new List<string>(3);
            if (b.Def.housing > 0 && b.Residents > 0) parts.Add($"{b.Residents} live{(b.Residents == 1 ? "s" : "")} here");
            if (b.Def.IsWorkplace && b.Workers > 0) parts.Add($"{b.Workers} work{(b.Workers == 1 ? "s" : "")} here");
            if (b.Store != null && b.Stored >= 1f) parts.Add($"{b.Stored:0} goods move to other stores if there is room");
            float refund = D.Balance.demolishRefund;
            parts.Add(math.abs(refund - 0.5f) < 0.01f ? "half the materials come back" : $"{refund:P0} of the materials come back");
            return string.Join("; ", parts);
        }

        public void Demolish(Building b)
        {
            if (b == null) return;
            if (_confirmDemolish != b.Id) { _confirmDemolish = b.Id; return; }     // second click confirms
            if (S.Demolish(b, out _)) Select(null);
            _confirmDemolish = -1;
        }

        /// <summary>Moves the camera to a point (keeps zoom unless given).</summary>
        public void JumpTo(float2 p, float distance = -1)
        {
            Following = null;
            _camFrom = G.Cam.Focus; _camTo = new Vector3(p.x, M.HeightAt(p.x, p.y), p.y);
            _distFrom = G.Cam.Distance; _distTo = distance > 0 ? distance : G.Cam.Distance;
            _camT = 0;
        }

        /// <summary>The person "Follow" keeps the camera on, or null.</summary>
        public Citizen Following { get; private set; }

        public void Follow(Citizen c)
        {
            JumpTo(c.Pos);                         // clears Following
            Following = c; OrbitCamera.PlayerPanned = false;
        }

        /// <summary>Keeps the camera on the followed person (where the view draws them) until the player pans the camera, the
        /// person's panel closes or they are gone.</summary>
        void UpdateFollow()
        {
            if (Following == null) return;
            if (OrbitCamera.PlayerPanned || SelectedCitizen != Following || Following.Gone) { Following = null; return; }
            if (_camT >= 0) return;               // the first jump runs its course
            float2 p = Following.Pos;
            foreach (var (dc, dp) in G.SView.Drawn) if (dc == Following) { p = dp; break; }
            var to = new Vector3(p.x, M.HeightAt(p.x, p.y), p.y);
            G.Cam.Focus = Vector3.Lerp(G.Cam.Focus, to, 1f - math.exp(-6f * math.min(Time.unscaledDeltaTime, 0.1f)));
        }

        Vector3 _camFrom, _camTo; float _distFrom, _distTo, _camT = -1;

        /// <summary>Stops a camera move in progress (the -uishots camera is set by hand).</summary>
        public void StopCamera() => _camT = -1;

        void OnCampfirePlaced(Building b)
        {
            Mode = ToolMode.None; PlaceDef = null;
            JumpTo(b.Center, 26f);
        }

        // ================================================================== per frame (Update)

        public float2 HoverPoint { get; private set; }

        /// <summary>Pointer over a panel (last frame's panel rectangles), or a full-screen panel is open.</summary>
        public bool PointerOverUI
        {
            get
            {
                if (Hidden) return false;
                if (ResearchOpen) return true;
                var p = PointerScreen;
                var v = new Vector2(p.x / Scale, (Screen.height - p.y) / Scale);
                foreach (var r in _prevRects) if (r.Contains(v)) return true;
                return false;
            }
        }

        public void Update(float dt)
        {
            if (S == null) return;
            if (MenuOpen)
            {
                // The game menu holds every key but Esc (and the world gets no input).
                OrbitCamera.PointerOverUI = true;
                OrbitCamera.KeyboardCaptured = true;
                var k = Keyboard.current;
                if (k != null && k.escapeKey.wasPressedThisFrame) MenuBack();
                if (!MenuOpen) OrbitCamera.KeyboardCaptured = false;
                return;
            }
            if (S.NeedsCampfire && Mode != ToolMode.Place) StartPlacing(D.Buildings.Find(b => b.townCenter));
            bool overUI = PointerOverUI;
            OrbitCamera.PointerOverUI = overUI;
            OrbitCamera.KeyboardCaptured = ResearchOpen && !Hidden;
            Keys();
            UpdateHover();
            MouseInput(overUI);
            UpdateFollow();
            if (_camT >= 0)
            {
                _camT = math.min(1f, _camT + math.min(Time.unscaledDeltaTime, 0.05f) / 1.6f);
                float k = _camT * _camT * (3 - 2 * _camT);
                G.Cam.Focus = Vector3.Lerp(_camFrom, _camTo, k);
                G.Cam.Distance = math.lerp(_distFrom, _distTo, k);
                if (_camT >= 1f) _camT = -1;
            }
            // Age-up moment: a banner and the camera going home to the campfire.
            if (S.AgeLog.Count != _lastAgeLogs)
            {
                _lastAgeLogs = S.AgeLog.Count;
                if (S.AgeLog.Count > 0) ShowBanner($"The settlement enters the {S.Age.name}");
                if (S.TownCenter != null) JumpTo(S.TownCenter.Center, 34f);
            }
            CheckEnd();
            CheckGone();
            UpdateGuide();
            UpdateHints();
            if (Selected != null && !Selected.Alive) Select(null);
            if (SelectedCitizen != null && SelectedCitizen.Gone) SelectCitizen(null);
            PushToView();
            SampleTrends();
        }

        public void ShowBanner(string text) { _banner = text; _bannerTime = 0; }
        public void ClearBanner() => _bannerTime = -1;

        void Keys()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.f11Key.wasPressedThisFrame) Hidden = !Hidden;
            if (kb.escapeKey.wasPressedThisFrame) Back();
            if (kb.f1Key.wasPressedThisFrame) ToggleHelp();
            if (kb.rKey.wasPressedThisFrame && !S.NeedsCampfire) ToggleResearch();
            if (kb.lKey.wasPressedThisFrame && !S.NeedsCampfire && !ResearchOpen) ToggleLaws();
            if (kb.f9Key.wasPressedThisFrame) G.ToggleAuto();
            if (kb.f5Key.wasPressedThisFrame) G.QuickSave();
            if (kb.f8Key.wasPressedThisFrame) { G.QuickLoad(); return; }
            if (kb.spaceKey.wasPressedThisFrame) G.TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) G.SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) G.SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) G.SetSpeed(4);
            if (ResearchOpen) return;              // world keys wait while the research screen has the keyboard
            if (kb.tabKey.wasPressedThisFrame && S.NeedsCampfire) PointAtSuggestedSite();   // before the return below, which is for the same state
            if (S.NeedsCampfire) return;
            if (kb.gKey.wasPressedThisFrame) ToggleGoals();
            if (kb.jKey.wasPressedThisFrame) ToggleJobs();
            if (kb.vKey.wasPressedThisFrame) ToggleEvents();
            if (kb.tKey.wasPressedThisFrame) ToggleStocks();
            if (kb.bKey.wasPressedThisFrame) OpenCategory(BuildCategory == null ? Categories[0] : BuildCategory);
            if ((kb.homeKey.wasPressedThisFrame || kb.hKey.wasPressedThisFrame) && S.TownCenter != null) JumpTo(S.TownCenter.Center, 34f);
            if (Mode == ToolMode.Place && PlaceDef != null && !PlaceDef.path)
            {
                if (kb.periodKey.wasPressedThisFrame) RotatePlacement(1);
                if (kb.commaKey.wasPressedThisFrame) RotatePlacement(-1);
            }
            if (kb.xKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame) { if (Mode == ToolMode.Demolish) CancelTool(); else StartDemolish(); }
        }

        void MouseInput(bool overUI)
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null || PointerOverride != null) return;
            // Right button: a click (not a camera drag) cancels the tool / clears the selection.
            if (mouse.rightButton.wasPressedThisFrame) { _rightDown = mouse.position.ReadValue(); _rightHeld = true; }
            if (mouse.rightButton.wasReleasedThisFrame && _rightHeld)
            {
                _rightHeld = false;
                if ((mouse.position.ReadValue() - _rightDown).magnitude < 6f && !overUI)
                {
                    if (Mode != ToolMode.None) CancelTool(); else { Select(null); SelectCitizen(null); }
                }
            }
            if (PlacingPath) { PathInput(overUI); return; }
            if (overUI) return;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var kb = Keyboard.current;
                ClickWorld(kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed));
            }
        }

        /// <summary>Points the pointer-free placement at the suggested campfire site and centres the camera on it.</summary>
        public void PointAtSuggestedSite()
        {
            var o = S.SuggestCampfireSite();
            var d = D.Buildings.Find(b => b.townCenter);
            var c = Settlement.FootprintCenter(d, o);
            SuggestedSite = c;
            JumpTo(c);
        }

        /// <summary>The site "Show a good site" pointed at: marked on the ground until the campfire is placed.</summary>
        public float2? SuggestedSite { get; private set; }

        /// <summary>"trees 51 (plenty)": a count within the campfire's reach judged against the suggester's own cap.</summary>
        static string Judged(string label, int n, int full)
        {
            if (n >= full) return $"{label} {Col($"{n} (plenty)", Good)}";
            if (n * 3 >= full) return $"{label} {Col($"{n} (some)", Warn)}";
            return $"{label} {Col(n == 0 ? "none" : $"{n} (few)", Bad)}";
        }

        public static int2 PlaceOrigin(BuildingDef d, int2 cell, int rot = 0)
        {
            var size = Settlement.Size(d, rot);
            return new int2(cell.x - (size.x - 1) / 2, cell.y - (size.y - 1) / 2);
        }

        /// <summary>Quarter turns of the building being placed (, and . while placing); its door turns with it.</summary>
        public int PlaceRot { get; private set; }
        public void RotatePlacement(int dir) { PlaceRot = (PlaceRot + dir + 4) & 3; }

        /// <summary>Ground point under the pointer (marching the ray over the height field) and what is there.</summary>
        void UpdateHover()
        {
            HoverValid = false; HoverBuilding = null; HoverCitizen = null;
            var cam = G.Cam.Cam;
            if (cam == null) return;
            var ray = cam.ScreenPointToRay(PointerScreen);
            float t = 0, step = 0.25f;
            Vector3 p = ray.origin;
            for (int i = 0; i < 2400; i++)
            {
                p = ray.origin + ray.direction * t;
                if (p.x >= 0 && p.z >= 0 && p.x < M.W && p.z < M.H && p.y <= M.HeightAt(p.x, p.z))
                {
                    // Refine between the last two samples.
                    float a = t - step, bnd = t;
                    for (int k = 0; k < 8; k++)
                    {
                        float m = (a + bnd) * 0.5f; var q = ray.origin + ray.direction * m;
                        if (q.y <= M.HeightAt(q.x, q.z)) bnd = m; else a = m;
                    }
                    p = ray.origin + ray.direction * bnd;
                    HoverValid = true;
                    break;
                }
                t += step;
                if (t > 2000f) break;
            }
            if (!HoverValid) return;
            HoverPoint = new float2(p.x, p.z);
            HoverCell = new int2((int)p.x, (int)p.z);
            HoverBuilding = S.BuildingAtCell(HoverCell);
            HoverCitizen = G.SView.CitizenNear(HoverPoint, 0.6f);
            if (HoverCitizen != null) foreach (var (dc, dp) in G.SView.Drawn) if (dc == HoverCitizen) { HoverCitizenPos = dp; break; }
            if (Mode == ToolMode.Place && PlaceDef != null)
            {
                PlaceOk = S.CanPlace(PlaceDef, PlaceOrigin(PlaceDef, HoverCell, PlaceRot), PlaceRot, out string why);
                PlaceReason = why;
            }
        }

        /// <summary>Tells the settlement view what to draw for the HUD: selection, ghost, radius previews.</summary>
        void PushToView()
        {
            var v = G.SView;
            v.Selected = Selected; v.SelectedCitizen = SelectedCitizen;
            v.GhostDef = Mode == ToolMode.Place && HoverValid && !PointerOverUI && !PlacingPath ? PlaceDef : null;
            v.Radii.Clear();
            v.DepositHint = Mode == ToolMode.Place && PlaceDef != null && !string.IsNullOrEmpty(PlaceDef.onDeposit) ? GameMap.DepositOf(PlaceDef.onDeposit) : Deposit.None;
            v.PathPreview.Clear();
            if (PlacingPath)
            {
                if (_pathStart != null) v.PathPreview.AddRange(_pathCells);
                else if (HoverValid && !PointerOverUI) v.PathPreview.Add(HoverCell);
                v.PathPreviewValid = v.PathPreview.Count == 0 || S.CanPlacePath(v.PathPreview[v.PathPreview.Count - 1], out _);
            }
            v.DemolishHover = Mode == ToolMode.Demolish && !PointerOverUI ? HoverBuilding : null;
            v.HoverCitizen = Mode == ToolMode.None && !PointerOverUI ? HoverCitizen : null;
            v.HoverBuilding = Mode == ToolMode.None && !PointerOverUI && HoverCitizen == null ? HoverBuilding : null;
            if (v.GhostDef != null)
            {
                var o = PlaceOrigin(PlaceDef, HoverCell, PlaceRot);
                v.GhostOrigin = o; v.GhostValid = PlaceOk; v.GhostRot = PlaceRot;
                AddRadii(v, PlaceDef, Settlement.FootprintCenter(PlaceDef, o, PlaceRot));
            }
            else if (Selected != null && Selected.Alive) AddRadii(v, Selected.Def, Selected.Center);
            // The suggested campfire site, after "Show a good site": a tight double ring of studs where the fire would
            // stand and its warmth's reach, in the accent colour, until the campfire is placed.
            if (!S.NeedsCampfire) SuggestedSite = null;
            else if (SuggestedSite != null)
            {
                var sc = SuggestedSite.Value;
                v.Radii.Add((sc, 1.2f, Accent));
                v.Radii.Add((sc, 2.4f, Accent));
                var tc = D.Buildings.Find(b => b.townCenter);
                if (tc != null && tc.warmthRadius > 0) v.Radii.Add((sc, tc.warmthRadius, Accent));
            }
        }

        static readonly Color RadiusWork = new(0.95f, 0.88f, 0.6f), RadiusWarm = new(1f, 0.62f, 0.35f), RadiusHealth = new(0.6f, 0.85f, 0.7f),
            RadiusWater = new(0.55f, 0.75f, 0.95f), RadiusFaith = new(0.8f, 0.7f, 0.95f);

        void AddRadii(SettlementView v, BuildingDef d, float2 c)
        {
            if (d.HasNearby) v.Radii.Add((c, d.nearby.radius, RadiusWork));
            if (d.warmthRadius > 0) v.Radii.Add((c, d.warmthRadius, RadiusWarm));
            if (d.healthRadius > 0) v.Radii.Add((c, d.healthRadius, RadiusHealth));
            if (d.waterRadius > 0) v.Radii.Add((c, d.waterRadius * S.Effect("water.radius"), RadiusWater));
            if (d.faithRadius > 0) v.Radii.Add((c, d.faithRadius, RadiusFaith));
            if (d.crop && d.irrigationRadius > 0 && S.IsResearched("irrigation")) v.Radii.Add((c, d.irrigationRadius, RadiusWater));
        }

        // ================================================================== drawing

        public void OnGUI()
        {
            if (S == null) return;
            Ensure();
            ClickSound();
            _rects.Clear();
            _tip = null;
            if (Hidden) { SwapRects(); return; }
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
            if (S.NeedsCampfire)
            {
                DrawCampfirePrompt();
                DrawPlacementTip();
                DrawGuide();
            }
            else
            {
                // PERF PROBE: Begin/End marks (no-ops unless -perfprobe)
                PerfProbe.Begin(PerfProbe.S.GuiFeed); DrawFeed(); PerfProbe.End(PerfProbe.S.GuiFeed);
                PerfProbe.Begin(PerfProbe.S.GuiBuildBar); DrawBuildBar(); PerfProbe.End(PerfProbe.S.GuiBuildBar);
                PerfProbe.Begin(PerfProbe.S.GuiGuide); DrawGuide(); PerfProbe.End(PerfProbe.S.GuiGuide);
                PerfProbe.Begin(PerfProbe.S.GuiInfo); DrawInfo(); PerfProbe.End(PerfProbe.S.GuiInfo);
                PerfProbe.Begin(PerfProbe.S.GuiOther);
                DrawTrade();
                DrawHint();
                DrawHoverTag();
                PerfProbe.End(PerfProbe.S.GuiOther);
                PerfProbe.Begin(PerfProbe.S.GuiPanels);
                if (GoalsOpen) DrawGoals();
                if (JobsOpen) DrawJobs();
                if (StocksOpen) DrawStocks();
                if (LawsOpen) DrawLaws();
                if (EventsOpen) DrawEvents();
                PerfProbe.End(PerfProbe.S.GuiPanels);
                DrawPlacementTip();
                PerfProbe.Begin(PerfProbe.S.GuiTopBar); DrawTopBar(); PerfProbe.End(PerfProbe.S.GuiTopBar);
                DrawPausedTag();
                PerfProbe.Begin(PerfProbe.S.GuiResearch);
                if (ResearchOpen) DrawResearch();
                PerfProbe.End(PerfProbe.S.GuiResearch);
                if (EndOpen) DrawEndScreen();
                if (GoneOpen) DrawGoneScreen();
            }
            if (HelpOpen) DrawHelp();
            DrawBanner();
            DrawGameMenu();
            DrawConfirm();
            DrawToast();
            DrawTip();
            GUI.matrix = old;
            SwapRects();
        }

        /// <summary>"Paused (Space)" under the top bar's centre while the game is paused and no menu or screen covers it.</summary>
        void DrawPausedTag()
        {
            if (S.Speed > 0 || MenuOpen || ResearchOpen || EndOpen || GoneOpen) return;
            var r = new Rect(VW / 2 - 90, TopBarHeight + 6, 180, 28);
            GUI.Box(r, GUIContent.none, UiTheme.Header);
            GUI.Label(r, _pausedText ??= $"<b>{Col("Paused", Accent)}</b>  {Col("(Space)", Dim)}", Center);
        }
        string _pausedText;

        void SwapRects() { _prevRects.Clear(); _prevRects.AddRange(_rects); }

        /// <summary>A soft click when a press lands on a panel (any button: they all sit on registered panels).</summary>
        void ClickSound()
        {
            var e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0) return;
            var v = e.mousePosition / Scale;
            foreach (var r in _prevRects) if (r.Contains(v)) { G.Sound?.Click(); return; }
        }

        /// <summary>Draws a panel background and registers it as UI (blocks world clicks under it).</summary>
        Rect Box(Rect r, GUIStyle style = null)
        {
            GUI.Box(r, GUIContent.none, style ?? UiTheme.Panel);
            _rects.Add(r);
            return r;
        }

        /// <summary>Whether the pointer is over a rectangle. A tooltip's text is built only when it is (built for every figure
        /// on every call, the tooltips were most of the HUD's garbage).</summary>
        static bool Over(Rect r) => r.Contains(Event.current.mousePosition);

        void Tip(Rect r, string text)
        {
            if (!string.IsNullOrEmpty(text) && r.Contains(Event.current.mousePosition)) _tip = text;
        }

        void DrawTip()
        {
            if (_forcedTip != null) _tip = _forcedTip;
            if (_tip == null) return;
            var content = new GUIContent(_tip);
            float w = 360;
            float h = UiTheme.Tip.CalcHeight(content, w);
            var m = _forcedTip != null ? _forcedTipAt : Event.current.mousePosition;
            var r = new Rect(m.x + 18, m.y + 14, w, h);
            if (r.xMax > VW - 8) r.x = m.x - w - 12;
            if (r.yMax > VH - 8) r.y = VH - 8 - h;
            GUI.Box(r, content, UiTheme.Tip);
        }

        // -uishots: show the tooltip a hover would show (screenshots have no mouse).
        string _forcedTip; Vector2 _forcedTipAt;
        public void ForceTooltip(string text, Vector2 at) { _forcedTip = text; _forcedTipAt = at; }

        void DrawBanner()
        {
            if (_bannerTime < 0) return;
            // Banner clock: real time, but at most 1/20 s a frame, so a slow frame rate still shows it for 6 s of frames.
            if (Event.current.type == EventType.Repaint) _bannerTime += math.min(Time.unscaledDeltaTime, 0.05f);
            float t = _bannerTime;
            if (t > 6f) { _bannerTime = -1; return; }
            float a = math.saturate(math.min(t / 0.15f, (6f - t) / 1f));
            var r = new Rect(VW / 2 - 480, VH * 0.24f, 960, 96);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
            GUI.Box(r, GUIContent.none, UiTheme.Header);
            GUI.Label(r, _banner, Banner);
            GUI.color = old;
        }

        void DrawCampfirePrompt()
        {
            var r = Box(new Rect(VW / 2 - 330, 18, 660, 112));
            GUI.Label(new Rect(r.x + 20, r.y + 12, 620, 30), "Choose a site for the campfire", Title);
            GUI.Label(new Rect(r.x + 20, r.y + 44, 620, 40), "The band will settle around it. Look for open ground with woods, loose rock, water and game within reach.", SmallWrap);
            if (GUI.Button(new Rect(r.x + 20, r.y + 78, 190, 26), "Show a good site (Tab)", Button)) PointAtSuggestedSite();
            GUI.Label(new Rect(r.x + 226, r.y + 78, 420, 26), SuggestedSite != null ? "The gold ring marks it. Left-click to place the fire." : "Left-click to place.   Wheel, WASD, Q/E, right-drag: camera", Small);
        }

        /// <summary>Next to the ghost: what is being placed, why it can't go there, and how good the spot is.</summary>
        void DrawPlacementTip()
        {
            if (Mode == ToolMode.Demolish)
            {
                var hb = HoverBuilding;
                string what, sub;
                if (hb == null) { what = "Click a building or path to remove it"; sub = null; }
                else if (hb == S.TownCenter) { what = Col("The campfire can't be removed", Bad); sub = null; }
                else if (hb.IsSite) { what = $"Remove the {hb.Def.name} site"; sub = "Delivered materials go back to storage."; }
                else if (ToolConfirmPending(hb)) { what = Col($"Click again to remove the {hb.Def.name}", Bad); sub = Cap(DemolishLoss(hb)) + "."; }
                else { what = $"Remove the {hb.Def.name}: click twice"; sub = Cap(DemolishLoss(hb)) + "."; }
                float h0 = sub != null ? 56 : 34;
                var r0 = Box(new Rect(VW / 2 - 320, VH - 128 - h0, 640, h0), UiTheme.Header);
                GUI.Label(new Rect(r0.x + 12, r0.y + (sub != null ? 4 : 0), 616, 28), what + Col("   (right-click or Esc to stop)", Dim), Label);
                if (sub != null) GUI.Label(new Rect(r0.x + 12, r0.y + 30, 616, 22), sub, Small);
                return;
            }
            if (Mode != ToolMode.Place || PlaceDef == null || !HoverValid || PointerOverUI) return;
            var p = PointerScreen;
            var at = new Vector2(p.x / Scale + 26, (Screen.height - p.y) / Scale + 18);
            var d = PlaceDef;
            var lines = new List<string> { $"<b>{d.name}</b>" };
            if (d.path)
            {
                int n = _pathStart != null ? _pathCells.Count(c => S.CanPlacePath(c, out _)) : 0;
                bool ok = S.CanPlacePath(HoverCell, out string why);
                if (_pathStart != null && n == 0 && _pathCells.Count > 0) S.CanPlacePath(_pathCells[_pathCells.Count - 1], out why);
                lines.Add(_pathStart != null ? (n > 0 ? $"{n} tile{(n == 1 ? "" : "s")} to lay" : Col(why, Bad)) : ok ? "Press and drag to lay a line of path" : Col(why, Bad));
                var pc = S.PathDef?.cost;
                lines.Add(Col("People walk faster on paths, and their routes bend onto them." + (pc != null && pc.Length > 0 ? $" Each tile: {string.Join(", ", pc.Select(a => $"{a.amount:0.#} {ResName(a.id).ToLower()}"))}." : ""), Dim));
            }
            else if (!PlaceOk)
            {
                lines.Add(Col(PlaceReason, Bad));
                if (!string.IsNullOrEmpty(d.onDeposit) && S.Map.DepositVisible(GameMap.DepositOf(d.onDeposit))) lines.Add(Col($"The {d.onDeposit} deposit is marked on the ground", Dim));
            }
            else
            {
                var c = Settlement.FootprintCenter(d, PlaceOrigin(d, HoverCell, PlaceRot), PlaceRot);
                if (d.HasNearby)
                {
                    int n = M.CountFeature(d.nearby.feature, c, d.nearby.radius);
                    float full = d.nearby.full <= 0 ? (n > 0 ? 1 : 0) : math.saturate(n / (float)d.nearby.full);
                    lines.Add($"{Cap(d.nearby.feature)} in reach: {n}  ->  yield {Col($"{full:P0}", full >= 0.75f ? Good : full >= 0.4f ? Warn : Bad)}");
                }
                if (d.warmthRadius > 0 || d.housing > 0)
                {
                    bool warm = S.TownCenter != null && math.distance(S.TownCenter.Center, c) <= S.TownCenter.Def.warmthRadius;
                    if (d.housing > 0) lines.Add(warm ? Col("Within the campfire's warmth", Good) : Col("Outside the campfire's warmth", Warn));
                }
                if (d.townCenter)
                {
                    // What a campfire here would have within reach (what the band will live off), judged against the counts
                    // the site suggester stops adding at.
                    if (SuggestedSite != null && math.distance(SuggestedSite.Value, c) < 1.5f) lines.Add(Col("The suggested site", Accent));
                    var cr = Settlement.CampfireReach;
                    lines.Add("Within reach: " + Judged(cr[0].label, M.CountFeature(cr[0].feature, c, cr[0].radius), cr[0].full) + ", " +
                              Judged(cr[1].label, M.CountFeature(cr[1].feature, c, cr[1].radius), cr[1].full) + ",\n" +
                              Judged(cr[2].label, M.CountFeature(cr[2].feature, c, cr[2].radius), cr[2].full) + ", " +
                              Judged(cr[3].label, M.CountFeature(cr[3].feature, c, cr[3].radius), cr[3].full));
                    lines.Add(Col($"Warms homes within {d.warmthRadius}", Dim));
                }
                if (!string.IsNullOrEmpty(d.onDeposit)) lines.Add(Col($"On the {d.onDeposit} deposit: {S.DepositCells(d, PlaceOrigin(d, HoverCell, PlaceRot), PlaceRot)} cells (marked)", Good));
                if (d.waterRadius > 0) { var c2 = Settlement.FootprintCenter(d, PlaceOrigin(d, HoverCell, PlaceRot), PlaceRot); float wr = d.waterRadius * S.Effect("water.radius"); int dry = S.Buildings.Count(h => h.Active && h.Def.housing > 0 && h.Residents > 0 && !h.HasWater && math.distance(h.Center, c2) <= wr); lines.Add(dry > 0 ? Col($"Brings water to {dry} home{(dry == 1 ? "" : "s")} without it", Good) : Col("No homes without water in reach", Dim)); }
                if (d.crop && d.irrigationRadius > 0) { var c2 = Settlement.FootprintCenter(d, PlaceOrigin(d, HoverCell, PlaceRot), PlaceRot); lines.Add(M.CountFeature("water", c2, d.irrigationRadius) > 0 ? Col("Water in reach: can be irrigated", Good) : Col("No water in reach to irrigate", Dim)); }
                if (!d.townCenter && S.FloodRiskCells(d, PlaceOrigin(d, HoverCell, PlaceRot), PlaceRot) > 0)
                    lines.Add(d.crop ? Col("On the floodplain: a spring flood leaves silt (a better harvest that year)", Good) : Col("On the floodplain: spring floods may reach it", Warn));
                if (d.cost.Length > 0)
                {
                    lines.Add("Cost: " + Cost(d));
                    // Placing it anyway is allowed: the site waits for what is missing.
                    var shortOf = d.cost.Where(c => S.Stock(c.id) < c.amount).Select(c => ResName(c.id).ToLower()).ToList();
                    if (shortOf.Count > 0) lines.Add(Col($"Not enough {string.Join(" or ", shortOf)} in store: builders will wait for it.", Warn));
                }
            }
            if (!d.path) lines.Add(Col(d.townCenter ? "Left-click to place" : "Left-click to place, Shift for several, comma and period turn it, right-click to stop", Dim));
            string text = string.Join("\n", lines);
            var style = PlaceTip;
            float h = style.CalcHeight(new GUIContent(text), 360) + 14;
            var r = new Rect(at.x, at.y, 380, h);
            if (r.xMax > VW - 10) r.x = at.x - 420;
            if (r.yMax > VH - 10) r.y = VH - 10 - h;
            GUI.Box(r, GUIContent.none, UiTheme.Tip);
            GUI.Label(new Rect(r.x + 10, r.y + 7, 360, h - 12), text, style);
        }

        /// <summary>The name and doing of the person under the pointer (with a faint ring in the view).</summary>
        void DrawHoverTag()
        {
            if (Mode != ToolMode.None || PointerOverUI) return;
            var c = HoverCitizen;
            var hb = c == null ? HoverBuilding : null;
            if (c == null && (hb == null || !hb.Alive)) return;
            Vector3 world = c != null
                ? new Vector3(HoverCitizenPos.x, M.HeightAt(HoverCitizenPos.x, HoverCitizenPos.y) + 1.15f, HoverCitizenPos.y)
                : new Vector3(hb.Center.x, M.HeightAt(hb.Center.x, hb.Center.y) + 1.6f, hb.Center.y);
            var at = G.Cam.Cam.WorldToScreenPoint(world);
            if (at.z <= 0) return;
            string text;
            if (c != null)
            {
                string doing = c.Job != null ? c.Job.Def.workerTitle ?? c.Job.Def.name : c.IsBuilder ? "Builder" : c.Age < D.Balance.adultAge ? "Child" : "Hauler";
                text = $"<b>{c.Name}</b>  {Col(doing, Dim)}";
            }
            else
            {
                // A building's tag: rebuilt when the building changes and a few times a second, not every frame.
                if (hb.Id != _tagBuilding || Time.unscaledTime - _tagAt > 0.25f) { _tagText = BuildingTag(hb); _tagBuilding = hb.Id; _tagAt = Time.unscaledTime; }
                text = _tagText;
            }
            var size = Label.CalcSize(new GUIContent(text));
            var r = new Rect(at.x / Scale - size.x / 2 - 8, (UnityEngine.Screen.height - at.y) / Scale - 30, size.x + 16, 24);
            GUI.Box(r, GUIContent.none, UiTheme.Tip);
            GUI.Label(r, text, Center);
        }

        int _tagBuilding = -1; float _tagAt; string _tagText = "";

        /// <summary>
        /// "Woodcutter · Working · 2/2", "Storage Pile · 212 / 250", "Thatched Hut · 6 / 8 live here", "Site: Lean-to · 40 %":
        /// what a building is and how it is doing, for the hover tag (workplaces look alike at the default zoom).
        /// </summary>
        string BuildingTag(Building b)
        {
            var d = b.Def;
            string sep = Col("  ·  ", Dim);
            if (b.IsSite) return $"{Col("Site:", Dim)} <b>{d.name}</b>{sep}{b.Progress:P0}";
            string text = $"<b>{d.name}</b>";
            if (b.UpgradeTo != null) text += sep + Col($"being rebuilt ({b.Progress:P0})", Warn);
            if (b.Flooded) return text + sep + Col("under flood water", Bad);
            if (d.warmthRadius > 0) text += sep + (b.Fueled ? Col("burning", Good) : Col("out of firewood", Bad));
            if (d.IsWorkplace)
            {
                string status = !string.IsNullOrEmpty(b.Status) ? b.Status : b.Workers > 0 ? "Working" : "No workers";
                bool ok = status == "Working" || status == "Tending the crop" || status == "Harvesting" || status == "Tended for today";
                text += sep + Col(status, ok ? Good : Warn);
                text += sep + Col($"{b.Workers}/{d.maxWorkers}", b.Workers >= d.maxWorkers ? Good : b.Workers == 0 ? Bad : Warn);
            }
            if (d.housing > 0) text += sep + $"{b.Residents} / {d.housing} live here";
            if (b.Store != null && !d.IsWorkplace) { float cap = math.max(1, S.Capacity(b)); text += sep + Col($"{b.Stored:0} / {cap:0}", b.Stored > cap * 0.9f ? Warn : Text); }
            return text;
        }

        public Citizen HoverCitizen { get; private set; }
        public float2 HoverCitizenPos { get; private set; }

        // ================================================================== small helpers

        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);

        string ResName(string id) => D.Resource(id)?.name ?? id;

        string Cost(BuildingDef d)
        {
            if (d.cost.Length == 0) return Col("No cost", Dim);
            var parts = new List<string>();
            foreach (var c in d.cost)
            {
                bool have = S.Stock(c.id) >= c.amount;
                parts.Add(Col($"{c.amount:0} {ResName(c.id).ToLower()}", have ? Text : Warn));
            }
            return string.Join(", ", parts);
        }

        static string Num(float v) => v >= 100 ? v.ToString("0") : v.ToString("0.#");
    }
}
