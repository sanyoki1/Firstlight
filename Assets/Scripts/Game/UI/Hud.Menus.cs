using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using static Firstlight.Game.UI.UiTheme;

namespace Firstlight.Game.UI
{
    // Menus: the title screen (Continue, New Game, Load, Settings, Quit), the new-game screen (seed and difficulty), the
    // save and load screens, settings, the in-game menu (Esc) and confirmations. Every button goes through a public
    // action method, which the smoke test and -p4shots call too.
    public sealed partial class Hud
    {
        public enum MenuScreen { None, Main, NewGame, Load, Save, Settings }
        MenuScreen _screen = MenuScreen.Main;
        /// <summary>The in-game menu is open (the settlement is paused meanwhile).</summary>
        public bool MenuOpen { get; private set; }
        public MenuScreen CurrentScreen => _screen;

        string _confirmText; Action _confirmYes;
        string _seedText = ""; int _difficulty = 1;
        string _saveName = "";
        List<(SaveGame.Header h, string file, string error)> _saves = new();
        int _saveSel = -1;
        Vector2 _saveScroll;
        string _menuMsg;
        // The title screen's Continue line: the newest save, read once when the screen opens (finding every save and
        // reading each header on every OnGUI call cost about 3 ms a frame) and again after a save or a delete.
        (SaveGame.Header h, string file)? _latest;
        bool _latestRead;
        float _settingsH = 760;

        // ------------------------------------------------------------------ actions

        public void OpenMenu() { MenuOpen = true; _screen = MenuScreen.None; _confirmText = null; }
        public void CloseMenu() { MenuOpen = false; _screen = MenuScreen.None; _confirmText = null; OrbitCamera.KeyboardCaptured = false; }

        public void ShowScreen(MenuScreen s)
        {
            _screen = s; _confirmText = null; _menuMsg = null; _latestRead = false;
            if (s == MenuScreen.Load || s == MenuScreen.Save) { _saves = SaveGame.List(); _saveSel = s == MenuScreen.Load && _saves.Count > 0 && _saves[0].h != null ? 0 : -1; _saveScroll = default; }
            if (s == MenuScreen.Save) _saveName = $"{S.Age?.name}, year {S.Year + 1} {S.SeasonName}";
            if (s == MenuScreen.NewGame) { _seedText = UnityEngine.Random.Range(1, 99999).ToString(); _difficulty = 1; }
        }

        public void MenuNewGame(int seed, int difficulty) => G.StartNewGame(seed, difficulty);

        /// <summary>Continue: the most recent save. False if there is none.</summary>
        public bool MenuContinue()
        {
            var latest = SaveGame.Latest();
            if (latest == null) return false;
            G.LoadGame(latest.Value.file);
            return true;
        }

        /// <summary>Saves to a named slot (from the save screen). Returns an error, or null.</summary>
        public string MenuSave(string name)
        {
            string err = G.SaveTo(SaveGame.PathFor(name), name, "manual");
            _latestRead = false;
            Toast(err ?? $"Saved: {name}");
            if (err == null) { CloseMenu(); }
            return err;
        }

        void Confirm(string text, Action yes) { _confirmText = text; _confirmYes = yes; }

        /// <summary>Main menu or quit from a game: ask first if there is unsaved progress.</summary>
        public void MenuLeave(bool quit)
        {
            Action go = quit ? G.QuitGame : G.ToMainMenu;
            if (!G.InMenu && G.Unsaved)
            {
                float min = G.SecondsSinceSave / 60f;
                Confirm($"Leave without saving? The last {(min < 1 ? "few moments" : $"{min:0} minute{(min < 1.5f ? "" : "s")}")} of play since the last save will be lost.", go);
            }
            else go();
        }

        /// <summary>Esc while a menu screen is up: back one step.</summary>
        void MenuBack()
        {
            if (_confirmText != null) { _confirmText = null; return; }
            if (G.InMenu) { _screen = MenuScreen.Main; _latestRead = false; return; }
            if (_screen != MenuScreen.None) { _screen = MenuScreen.None; return; }
            CloseMenu();
        }

        /// <summary>Title screen keys (Esc steps back).</summary>
        public void MenuKeys()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) MenuBack();
        }

        // ------------------------------------------------------------------ toast

        string _toast; float _toastTime = -1;
        public void Toast(string text) { _toast = text; _toastTime = 0; }

        void DrawToast()
        {
            if (_toastTime < 0 || string.IsNullOrEmpty(_toast)) return;
            if (Event.current.type == EventType.Repaint) _toastTime += math.min(Time.unscaledDeltaTime, 0.05f);
            if (_toastTime > 3.5f) { _toastTime = -1; return; }
            float a = math.saturate(math.min(_toastTime / 0.15f, (3.5f - _toastTime) / 0.6f));
            float w = Label.CalcSize(Temp(_toast)).x + 40;
            var r = new Rect(VW / 2 - w / 2, VH - 120, w, 34);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
            GUI.Box(r, GUIContent.none, UiTheme.Header);
            GUI.Label(r, _toast, Center);
            GUI.color = old;
        }

        // ------------------------------------------------------------------ drawing

        static GUIStyle _titleBig, _sub, _menuBtn, _field;

        void MenuStyles()
        {
            if (_titleBig != null) return;
            _titleBig = new GUIStyle(Title) { fontSize = 64, alignment = TextAnchor.MiddleLeft };
            _sub = new GUIStyle(Label) { fontSize = 18 };
            _sub.normal.textColor = Dim;
            _menuBtn = new GUIStyle(UiTheme.Button) { fontSize = 20, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(18, 10, 2, 2) };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 18, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(8, 8, 4, 4) };
            var fieldTex = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave };
            for (int fy = 0; fy < 3; fy++) for (int fx = 0; fx < 3; fx++) fieldTex.SetPixel(fx, fy, fx == 1 && fy == 1 ? new Color(0.07f, 0.075f, 0.07f, 1f) : new Color(0.62f, 0.55f, 0.4f, 1f));
            fieldTex.Apply();
            _field.border = new RectOffset(1, 1, 1, 1);
            _field.normal.background = _field.focused.background = _field.hover.background = fieldTex;
            _field.normal.textColor = _field.focused.textColor = _field.hover.textColor = Text;
        }

        /// <summary>The title screen (the world turns slowly behind it).</summary>
        public void OnMenuGUI()
        {
            Ensure(); MenuStyles();
            ClickSound();
            _rects.Clear();
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1));
            // A soft dark band down the left so the text reads over any scenery.
            Fill(new Rect(0, 0, 620, VH), new Color(0.06f, 0.065f, 0.06f, 0.78f));
            Fill(new Rect(620, 0, 2, VH), new Color(0.3f, 0.3f, 0.28f, 0.6f));
            if (_screen == MenuScreen.None) _screen = MenuScreen.Main;
            switch (_screen)
            {
                case MenuScreen.Main: DrawTitle(); break;
                case MenuScreen.NewGame: DrawNewGame(); break;
                case MenuScreen.Load: DrawLoadSave(false); break;
                case MenuScreen.Settings: DrawSettings(); break;
            }
            DrawConfirm();
            DrawToast();
            DrawTip();
            GUI.matrix = old;
            SwapRects();
        }

        void DrawTitle()
        {
            float x = 90, y = VH * 0.2f;
            GUI.Label(new Rect(x, y, 520, 80), "Firstlight", _titleBig);
            GUI.Label(new Rect(x + 4, y + 78, 520, 28), "From the first campfire to the Bronze Age", _sub);
            y += 150;
            if (!_latestRead) { _latest = SaveGame.Latest(); _latestRead = true; }
            var latest = _latest;
            bool can = latest != null;
            if (MenuButton(new Rect(x, y, 420, 50), "Continue", can) && can) G.LoadGame(latest.Value.file);
            if (can)
            {
                var h = latest.Value.h;
                GUI.Label(new Rect(x + 4, y + 52, 520, 22), Col($"{h.Name}  ·  {h.Age}, year {h.Year} {h.Season}  ·  {(h.Population == 1 ? "1 person" : $"{h.Population} people")}  ·  {h.SavedLocal:d MMM yyyy, HH:mm}", Dim), Small);
            }
            y += 84;
            if (MenuButton(new Rect(x, y, 420, 50), "New game")) ShowScreen(MenuScreen.NewGame);
            y += 60;
            if (MenuButton(new Rect(x, y, 420, 50), "Load")) ShowScreen(MenuScreen.Load);
            y += 60;
            if (MenuButton(new Rect(x, y, 420, 50), "Settings")) ShowScreen(MenuScreen.Settings);
            y += 60;
            if (MenuButton(new Rect(x, y, 420, 50), "Quit")) G.QuitGame();
            y += 70;
            if (!string.IsNullOrEmpty(GameController.MenuMessage))
            {
                var r = new Rect(x, y, 470, 70);
                GUI.Box(r, GUIContent.none, UiTheme.Chip);
                GUI.Label(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16), Col(GameController.MenuMessage, Warn), SmallWrap);
            }
            GUI.Label(new Rect(x, VH - 46, 520, 24), Col("Playable slice: the Stone and Agricultural Ages, to the edge of the Bronze Age.", Dim), Small);
        }

        bool MenuButton(Rect r, string text, bool enabled = true)
        {
            _rects.Add(r);
            if (!enabled) _menuBtnDim ??= new GUIStyle(_menuBtn) { normal = { background = ButtonDim.normal.background }, hover = { background = ButtonDim.normal.background } };
            return GUI.Button(r, enabled ? text : Col(text, Faint), enabled ? _menuBtn : _menuBtnDim);
        }

        static GUIStyle _menuBtnDim;     // a disabled menu button (made once, not on every call)

        // ------------------------------------------------------------------ new game

        void DrawNewGame()
        {
            float x = 90, y = VH * 0.2f;
            GUI.Label(new Rect(x, y, 520, 44), "New settlement", Title34);
            y += 64;
            GUI.Label(new Rect(x, y, 300, 24), "Map seed", Label);
            y += 28;
            GUI.SetNextControlName("seed");
            _seedText = new string(GUI.TextField(new Rect(x, y, 260, 36), _seedText ?? "", 9, _field).Where(char.IsDigit).ToArray());
            if (GUI.Button(new Rect(x + 270, y, 140, 36), "Random", Button)) _seedText = UnityEngine.Random.Range(1, 99999).ToString();
            y += 42;
            GUI.Label(new Rect(x, y, 470, 40), Col("The same seed always makes the same land: river, woods, rock, game and ore.", Dim), SmallWrap);
            y += 52;
            GUI.Label(new Rect(x, y, 300, 24), "Difficulty", Label);
            y += 30;
            var diffs = D.Balance.difficulties;
            for (int i = 0; i < diffs.Length; i++)
            {
                var r = new Rect(x + i * 142, y, 136, 40);
                if (GUI.Button(r, diffs[i].name, _difficulty == i ? ButtonOn : Button)) _difficulty = i;
            }
            y += 48;
            if (_difficulty >= 0 && _difficulty < diffs.Length)
            {
                var d = diffs[_difficulty];
                GUI.Label(new Rect(x, y, 470, 44), d.description, SmallWrap);
                y += 46;
                GUI.Label(new Rect(x, y, 470, 22), Col($"Starting goods x{d.startGoods:0.##}, yields x{d.yield:0.##}, firewood use x{d.firewood:0.##}. Winters: {(d.winterTemperature > 0.001f ? "milder" : d.winterTemperature < -0.001f ? "colder" : "normal")}", Dim), Small);
            }
            y += 50;
            if (MenuButton(new Rect(x, y, 420, 50), "Start")) MenuNewGame(int.TryParse(_seedText, out int seed) ? seed : 1, _difficulty);
            y += 60;
            if (MenuButton(new Rect(x, y, 420, 44), "Back")) _screen = MenuScreen.Main;
            OrbitCamera.KeyboardCaptured = true;
        }

        // ------------------------------------------------------------------ save and load screens

        void DrawLoadSave(bool saving)
        {
            float w = 820, h = 620;
            var r = Box(new Rect(G.InMenu ? 660 : VW / 2 - w / 2, VH / 2 - h / 2, w, h));
            GUI.Label(new Rect(r.x + 24, r.y + 14, 400, 32), saving ? "Save the settlement" : "Load a settlement", Title24);
            if (GUI.Button(new Rect(r.xMax - 40, r.y + 14, 26, 24), "x", Button)) MenuBack();
            float y = r.y + 58;
            if (saving)
            {
                GUI.Label(new Rect(r.x + 24, y + 6, 70, 24), "Name", Label);
                GUI.SetNextControlName("savename");
                _saveName = GUI.TextField(new Rect(r.x + 90, y, 470, 36), _saveName ?? "", 40, _field);
                bool exists = File.Exists(SaveGame.PathFor(_saveName));
                if (GUI.Button(new Rect(r.x + 574, y, 220, 36), exists ? "<b>Save over it</b>" : "<b>Save</b>", ButtonOn))
                {
                    if (exists) Confirm($"Replace the save \"{SaveGame.Sanitize(_saveName)}\"?", () => _menuMsg = MenuSave(_saveName));
                    else _menuMsg = MenuSave(_saveName);
                }
                OrbitCamera.KeyboardCaptured = true;
                y += 48;
            }
            // Header row.
            float cx = r.x + 24;
            GUI.Label(new Rect(cx, y, 300, 20), "Save", Small);
            GUI.Label(new Rect(cx + 300, y, 250, 20), "Settlement", Small);
            GUI.Label(new Rect(cx + 560, y, 220, 20), "Saved", Small);
            y += 24;
            float listH = r.yMax - 74 - y;
            var view = new Rect(0, 0, w - 64, math.max(listH, _saves.Count * 46));
            _saveScroll = GUI.BeginScrollView(new Rect(cx - 6, y, w - 36, listH), _saveScroll, view);
            for (int i = 0; i < _saves.Count; i++)
            {
                var (hd, file, err) = _saves[i];
                var row = new Rect(0, i * 46, view.width, 42);
                bool sel = i == _saveSel;
                if (sel) Fill(row, new Color(0.55f, 0.45f, 0.25f, 0.25f));
                else if (row.Contains(Event.current.mousePosition)) Fill(row, new Color(1, 1, 1, 0.05f));
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { _saveSel = i; if (saving && hd != null && hd.Kind == "manual") _saveName = hd.Name; }
                string name = hd != null ? hd.Name : Path.GetFileNameWithoutExtension(file);
                string kind = hd == null ? "" : hd.Kind == "auto" ? "autosave" : hd.Kind == "quick" ? "quicksave" : "";
                GUI.Label(new Rect(6, row.y + 2, 290, 22), $"<b>{name}</b>", Label);
                // Second line: autosave / quicksave; the file name only for a save whose header can't be read.
                string sub = kind.Length > 0 ? kind : hd == null ? Path.GetFileName(file) : "";
                if (sub.Length > 0) GUI.Label(new Rect(6, row.y + 22, 290, 18), Col(sub, Dim), Small);
                if (hd != null)
                {
                    GUI.Label(new Rect(306, row.y + 2, 250, 22), $"{hd.Age}, year {hd.Year} {hd.Season}", Label);
                    GUI.Label(new Rect(306, row.y + 22, 250, 18), Col($"{(hd.Population == 1 ? "1 person" : $"{hd.Population} people")} · {D.Balance.difficulties.ElementAtOrDefault(hd.Difficulty)?.name ?? ""}", Dim), Small);
                    GUI.Label(new Rect(566, row.y + 2, 200, 22), $"{hd.SavedLocal:d MMM yyyy}", Label);
                    GUI.Label(new Rect(566, row.y + 22, 200, 18), Col($"{hd.SavedLocal:HH:mm}", Dim), Small);
                }
                else GUI.Label(new Rect(306, row.y, 460, 42), Col(err ?? "Unreadable", Warn), SmallWrap);
            }
            if (_saves.Count == 0) GUI.Label(new Rect(6, 10, 600, 24), Col(saving ? "No saves yet." : "No saves yet. Start a new game; it saves itself at the start of each season.", Dim), Label);
            GUI.EndScrollView();
            // Buttons.
            float by = r.yMax - 56;
            bool has = _saveSel >= 0 && _saveSel < _saves.Count;
            bool readable = has && _saves[_saveSel].h != null;
            if (!saving && GUI.Button(new Rect(r.x + 24, by, 200, 38), readable ? "<b>Load</b>" : Col("Load", Faint), readable ? ButtonOn : ButtonDim) && readable)
            {
                string file = _saves[_saveSel].file;
                if (!G.InMenu && G.Unsaved) Confirm("Load this save? Progress since your last save will be lost.", () => G.LoadGame(file));
                else G.LoadGame(file);
            }
            if (GUI.Button(new Rect(r.x + (saving ? 24 : 236), by, 160, 38), has ? "Delete" : Col("Delete", Faint), has ? Button : ButtonDim) && has)
            {
                var (hd, file, _) = _saves[_saveSel];
                Confirm($"Delete \"{hd?.Name ?? Path.GetFileName(file)}\"? This can't be undone.", () =>
                {
                    try { File.Delete(file); } catch (Exception e) { _menuMsg = e.Message; }
                    _saves = SaveGame.List(); _saveSel = -1; _latestRead = false;
                });
            }
            if (GUI.Button(new Rect(r.xMax - 184, by, 160, 38), "Back", Button)) MenuBack();
            if (!string.IsNullOrEmpty(_menuMsg)) GUI.Label(new Rect(r.x + 420, by, 230, 38), Col(_menuMsg, Warn), SmallWrap);
            GUI.Label(new Rect(r.x + 24, r.yMax - 19, 770, 17), Col($"Saves are kept in {SaveGame.Dir.Replace('\\', '/')}", Dim), SmallFoot);
        }

        // ------------------------------------------------------------------ settings

        void DrawSettings()
        {
            var st = GameSettings.Current;
            float w = 640, h = _settingsH;
            var r = Box(new Rect(G.InMenu ? 660 : VW / 2 - w / 2, VH / 2 - h / 2, w, h));
            GUI.Label(new Rect(r.x + 24, r.y + 14, 400, 32), "Settings", Title24);
            if (GUI.Button(new Rect(r.xMax - 40, r.y + 14, 26, 24), "x", Button)) MenuBack();
            float y = r.y + 60, lx = r.x + 28, cx = r.x + 300;
            bool changed = false, display = false;

            void Head(string t) { GUI.Label(new Rect(lx, y, 400, 24), Col(t, Accent), Bold); y += 30; }
            void Toggle(string label, ref bool v, string on = "On", string off = "Off", string tip = null)
            {
                GUI.Label(new Rect(lx, y, 260, 30), label, Label);
                if (GUI.Button(new Rect(cx, y, 140, 30), on, v ? ButtonOn : Button) && !v) { v = true; changed = true; }
                if (GUI.Button(new Rect(cx + 146, y, 140, 30), off, !v ? ButtonOn : Button) && v) { v = false; changed = true; }
                if (tip != null) Tip(new Rect(lx, y, 260, 30), tip);
                y += 36;
            }
            void Step(string label, ref float v, float step, float min, float max, string text, string tip = null)
            {
                GUI.Label(new Rect(lx, y, 260, 30), label, Label);
                if (GUI.Button(new Rect(cx, y, 40, 30), "-", Button)) { v = math.max(min, (float)Math.Round(v - step, 2)); changed = true; }
                GUI.Label(new Rect(cx + 46, y, 194, 30), text, Center);
                if (GUI.Button(new Rect(cx + 246, y, 40, 30), "+", Button)) { v = math.min(max, (float)Math.Round(v + step, 2)); changed = true; }
                if (tip != null) Tip(new Rect(lx, y, 260, 30), tip);
                y += 36;
            }

            Head("Display");
            bool fs = st.fullscreen;
            Toggle("Window", ref fs, "Fullscreen", "Windowed");
            if (fs != st.fullscreen) { st.fullscreen = fs; display = true; }
            {
                GUI.Label(new Rect(lx, y, 260, 30), "Resolution", Label);
                var res = GameSettings.Resolutions;
                int idx = Array.FindIndex(res, v => v.x == st.width && v.y == st.height); if (idx < 0) idx = 0;
                if (GUI.Button(new Rect(cx, y, 40, 30), "<", Button)) { idx = (idx + res.Length - 1) % res.Length; st.width = res[idx].x; st.height = res[idx].y; changed = display = true; }
                GUI.Label(new Rect(cx + 46, y, 194, 30), idx == 0 ? $"Desktop ({UnityEngine.Screen.currentResolution.width} x {UnityEngine.Screen.currentResolution.height})" : $"{res[idx].x} x {res[idx].y}", Center);
                if (GUI.Button(new Rect(cx + 246, y, 40, 30), ">", Button)) { idx = (idx + 1) % res.Length; st.width = res[idx].x; st.height = res[idx].y; changed = display = true; }
                y += 36;
            }
            Toggle("Vertical sync", ref st.vsync, tip: "Matches frames to the screen's refresh: no tearing, steadier frame times.");
            {
                GUI.Label(new Rect(lx, y, 260, 30), "Frame rate limit", Label);
                int[] caps = { 0, 30, 60, 120, 144 };
                int idx = math.max(0, Array.IndexOf(caps, st.frameCap));
                if (GUI.Button(new Rect(cx, y, 40, 30), "<", Button)) { st.frameCap = caps[(idx + caps.Length - 1) % caps.Length]; changed = true; }
                GUI.Label(new Rect(cx + 46, y, 194, 30), st.frameCap == 0 ? "None" : $"{st.frameCap} fps", Center);
                if (GUI.Button(new Rect(cx + 246, y, 40, 30), ">", Button)) { st.frameCap = caps[(idx + 1) % caps.Length]; changed = true; }
                y += 36;
            }
            Step("Interface size", ref st.uiScale, 0.1f, 0.7f, 1.6f, $"{st.uiScale:P0}");
            y += 6;
            Head("Camera");
            Toggle("Scroll at the screen edge", ref st.edgeScroll);
            Step("Camera speed", ref st.cameraSpeed, 0.1f, 0.3f, 3f, $"{st.cameraSpeed:P0}");
            y += 6;
            Head("Game");
            Toggle("Autosave each season", ref st.autosave, tip: "Keeps the last three autosaves.");
            bool guide = st.guide;
            Toggle("First-game guide", ref guide, tip: "The objectives card that walks through the first steps.");
            if (guide != st.guide) { st.guide = guide; if (guide) st.guideDone = false; }
            Toggle("Hints", ref st.hints, tip: "One-time notes the first time something new happens (first winter, a first death, a new need...).");
            if (SoundScape.Available)
            {
                y += 6;
                Head("Sound");
                Step("Volume", ref st.volMaster, 0.1f, 0f, 1f, $"{st.volMaster:P0}");
                Step("Ambience", ref st.volAmbience, 0.1f, 0f, 1f, $"{st.volAmbience:P0}");
                Step("Interface sounds", ref st.volUi, 0.1f, 0f, 1f, $"{st.volUi:P0}");
            }
            if (changed || display) { GameSettings.Apply(display); GameSettings.Save(); }
            y += 14;
            if (GUI.Button(new Rect(r.xMax - 184, y, 160, 38), "Back", Button)) MenuBack();
            GUI.Label(new Rect(r.x + 28, y, 400, 38), Col("Changes are kept at once.", Dim), Small);
            _settingsH = y + 56 - r.y;
        }

        // ------------------------------------------------------------------ in-game menu (Esc)

        void DrawGameMenu()
        {
            if (!MenuOpen) return;
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.35f));
            _rects.Add(new Rect(0, 0, VW, VH));
            switch (_screen)
            {
                case MenuScreen.Save: DrawLoadSave(true); break;
                case MenuScreen.Load: DrawLoadSave(false); break;
                case MenuScreen.Settings: DrawSettings(); break;
                default:
                {
                    float w = 400, h = 440;
                    var r = Box(new Rect(VW / 2 - w / 2, VH / 2 - h / 2, w, h));
                    GUI.Label(new Rect(r.x, r.y + 18, w, 36), "Paused", Title28Center);
                    float y = r.y + 72, x = r.x + 40, bw = w - 80;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Resume", _menuBtn)) CloseMenu();
                    y += 52;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Save", _menuBtn)) ShowScreen(MenuScreen.Save);
                    y += 52;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Load", _menuBtn)) ShowScreen(MenuScreen.Load);
                    y += 52;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Settings", _menuBtn)) ShowScreen(MenuScreen.Settings);
                    y += 52;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Main menu", _menuBtn)) MenuLeave(false);
                    y += 52;
                    if (GUI.Button(new Rect(x, y, bw, 44), "Quit to desktop", _menuBtn)) MenuLeave(true);
                    y += 56;
                    float min = G.SecondsSinceSave / 60f;
                    string saved = !G.Unsaved ? "Saved just now." : $"Last saved {(min < 1 ? "under a minute" : $"{min:0} minute{(min < 1.5f ? "" : "s")}")} of play ago.";
                    var cs = SmallCenter;
                    GUI.Label(new Rect(r.x, y - 4, w, 20), Col(saved, Dim), cs);
                    GUI.Label(new Rect(r.x, y + 16, w, 20), Col("F5 quicksave, F8 quickload", Dim), cs);
                    break;
                }
            }
        }

        void DrawConfirm()
        {
            if (_confirmText == null) return;
            float w = 520, h = 190;
            Fill(new Rect(0, 0, VW, VH), new Color(0, 0, 0, 0.3f));
            var r = Box(new Rect(VW / 2 - w / 2, VH / 2 - h / 2, w, h));
            GUI.Label(new Rect(r.x + 28, r.y + 24, w - 56, 90), _confirmText, Wrap);
            if (GUI.Button(new Rect(r.x + 28, r.yMax - 60, 200, 40), "<b>Yes</b>", ButtonOn)) { var a = _confirmYes; _confirmText = null; a?.Invoke(); }
            if (GUI.Button(new Rect(r.xMax - 228, r.yMax - 60, 200, 40), "No", Button)) _confirmText = null;
        }
    }
}
