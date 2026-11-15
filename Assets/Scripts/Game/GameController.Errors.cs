using System;
using System.Text;
using Firstlight.Core;
using UnityEngine;

namespace Firstlight.Game
{
    // What the game does when something is wrong that the player cannot fix by playing: a data file that does not load
    // or has problems in it. Headless and test runs stop at once with exit code 2 and the reason in the log; the
    // windowed game shows a plain error screen instead of a black window.
    public sealed partial class GameController
    {
        string _dataError;          // the error screen's text (null = no error screen)
        bool _dataFatal;            // a file did not load: the game cannot start (no "Continue anyway")
        Vector2 _dataErrorScroll;

        /// <summary>No person is there to read a message: batch mode, the tests and the other command-line modes.</summary>
        static bool Unattended => Application.isBatchMode || HasArg("-autotest") || HasArg("-fuzztest") || HasArg("-smoketest") || CommandLineMode;

        /// <summary>Loads the data. False if the game must not go on (it is quitting with code 2, or the error screen is up).</summary>
        bool LoadData()
        {
            try { Data = GameData.Load(); }
            catch (Exception e)
            {
                string msg = e is GameData.LoadException ? e.Message : e.ToString();
                return DataFailed($"A data file could not be read.\n{msg}", fatal: true);
            }
            if (Data.Problems.Count == 0) return true;
            var sb = new StringBuilder($"The game data has {Data.Problems.Count} problem{(Data.Problems.Count == 1 ? "" : "s")}:");
            foreach (var p in Data.Problems) sb.Append("\n- ").Append(p);
            return DataFailed(sb.ToString(), fatal: false);
        }

        bool DataFailed(string text, bool fatal)
        {
            if (Unattended)
            {
                // LogError, not an exception: the message is the whole story. Exit 2 = the data, not the game, failed.
                Debug.LogError($"[Data] cannot start: {text}");
                Application.Quit(2);
                return false;
            }
            Debug.LogError($"[Data] {text}");
            _dataError = text;
            _dataFatal = fatal;
            return false;
        }

        /// <summary>The error screen: plain IMGUI (the HUD's styles and the world are not set up yet).</summary>
        void DataErrorGUI()
        {
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            var r = new Rect(w / 2 - 420, h / 2 - 260, 840, 520);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUI.Label(new Rect(r.x + 24, r.y + 18, r.width - 48, 34), "Firstlight could not start", title);
            string where = $"The data files are in {System.IO.Path.Combine(Application.streamingAssetsPath, "Data")}.\n" +
                           $"Details are also in the log file ({LogPath}).";
            float whereH = body.CalcHeight(new GUIContent(where), r.width - 48);
            GUI.Label(new Rect(r.x + 24, r.y + 56, r.width - 48, whereH), where, body);
            float top = r.y + 56 + whereH + 14;
            var view = new Rect(r.x + 24, top, r.width - 48, r.yMax - 64 - top);
            float textH = body.CalcHeight(new GUIContent(_dataError), view.width - 20);
            _dataErrorScroll = GUI.BeginScrollView(view, _dataErrorScroll, new Rect(0, 0, view.width - 20, Mathf.Max(textH, view.height - 4)));
            GUI.Label(new Rect(0, 0, view.width - 20, textH), _dataError, body);
            GUI.EndScrollView();
            if (GUI.Button(new Rect(r.xMax - 164, r.yMax - 50, 140, 34), "Quit")) QuitGame();
            if (!_dataFatal && GUI.Button(new Rect(r.xMax - 324, r.yMax - 50, 150, 34), "Continue anyway"))
            {
                _dataError = null;
                Begin();
            }
            GUI.matrix = keep;
        }

        // ------------------------------------------------------------------ an exception while playing

        bool _faulted;              // the settlement's step or the HUD's update threw: no more steps, no autosaves
        string _faultText;          // what the message box says went wrong
        string _faultSaveMsg;       // the result of "Save a copy"
        readonly bool[] _drawFaultLogged = new bool[6];
        static bool _openLoadScreen; // "Load a save": the title menu opens on its load screen

        /// <summary>
        /// An exception where the game cannot simply go on (the settlement's step, the HUD's update): logged once with the
        /// game day, the game pauses and stops stepping and autosaving (a half-done step must not overwrite good saves),
        /// and a message offers to load a save, go to the main menu, quit, or save a copy under a new name. Unattended
        /// runs (batch mode, tests, clips) quit with code 3 instead, so the failure is not missed.
        /// </summary>
        void Fault(string where, Exception e)
        {
            if (_faulted) return;
            _faulted = true;
            string day = Sim != null ? $"game day {Sim.Day} (year {Sim.Year + 1})" : "no game";
            Debug.LogError($"[Fault] exception in {where} on {day}; the settlement is paused and autosaves are off");
            Debug.LogException(e);
            _faultText = $"Something went wrong and the settlement was paused.\nDetails are in the log file ({LogPath}).";
            try { Sim?.SetSpeed(0); } catch (Exception) { }
            OrbitCamera.KeyboardCaptured = false;
            if (FaultQuits) { Debug.LogError("[Fault] unattended run: quitting with code 3"); Application.Quit(3); }
        }

        /// <summary>An exception while drawing (or in sound, wildlife): logged once per place; the game goes on.</summary>
        void DrawFault(int site, string where, Exception e)
        {
            if (FaultQuits) { Fault(where, e); return; }
            if (_drawFaultLogged[site]) return;
            _drawFaultLogged[site] = true;
            Debug.LogError($"[Fault] exception in {where} (day {Sim?.Day}); logged once, the game goes on");
            Debug.LogException(e);
        }

        /// <summary>Unattended runs fail loudly on an exception; -faulttest in a window shows the message instead.</summary>
        static bool FaultQuits => Application.isBatchMode || ((CommandLineMode || HasArg("-smoketest")) && !HasArg("-faulttest"));

        /// <summary>-faulttest N (debug): the settlement's step throws once game day N is reached.</summary>
        void FaultTestCheck()
        {
            int n = ArgInt("-faulttest", -1);
            if (n >= 0 && Sim.Day >= n && !Sim.NeedsCampfire)
            {
                if (Arg("-outdir") != null) StartCoroutine(FaultShot());
                throw new InvalidOperationException($"-faulttest: a deliberate exception on day {Sim.Day}");
            }
        }

        System.Collections.IEnumerator FaultShot()
        {
            yield return new WaitForSecondsRealtime(2f);
            string path = System.IO.Path.Combine(Arg("-outdir"), "fault-message.png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[Fault] screenshot {path}");
        }

        /// <summary>The fault message: drawn over the world (the HUD is not drawn any more).</summary>
        void FaultGUI()
        {
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var keep = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            float w = Screen.width / scale, h = Screen.height / scale;
            var r = new Rect(w / 2 - 360, h / 2 - 150, 720, 300);
            for (int i = 0; i < 3; i++) GUI.Box(r, GUIContent.none);
            var title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            var body = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
            GUI.Label(new Rect(r.x + 24, r.y + 18, r.width - 48, 34), "The game stopped", title);
            float th = body.CalcHeight(new GUIContent(_faultText), r.width - 48);
            GUI.Label(new Rect(r.x + 24, r.y + 60, r.width - 48, th), _faultText, body);
            if (_faultSaveMsg != null) GUI.Label(new Rect(r.x + 24, r.y + 70 + th, r.width - 48, 50), _faultSaveMsg, body);
            float y = r.yMax - 54, x = r.x + 24;
            if (GUI.Button(new Rect(x, y, 150, 34), "Load a save")) { _openLoadScreen = true; ToMainMenu(); }
            if (GUI.Button(new Rect(x + 160, y, 150, 34), "Main menu")) ToMainMenu();
            if (GUI.Button(new Rect(x + 320, y, 150, 34), "Save a copy")) SaveFaultCopy();
            if (GUI.Button(new Rect(r.xMax - 174, y, 150, 34), "Quit")) QuitGame();
            GUI.matrix = keep;
        }

        /// <summary>Saves the stopped game under a new name: never over an existing save or an autosave.</summary>
        void SaveFaultCopy()
        {
            if (Sim == null || Sim.NeedsCampfire) { _faultSaveMsg = "There is nothing to save yet."; return; }
            string baseName = $"Stopped game, day {Sim.Day}";
            string name = baseName, path = SaveGame.PathFor(name);
            for (int i = 2; System.IO.File.Exists(path) && i < 1000; i++) { name = $"{baseName} ({i})"; path = SaveGame.PathFor(name); }
            if (System.IO.File.Exists(path)) { _faultSaveMsg = "Could not find a free name for the copy."; return; }
            string err = SaveTo(path, name, "manual");
            _faultSaveMsg = err ?? $"Saved a copy as \"{name}\" (it may hold the problem; an earlier save is safer to load).";
        }

        /// <summary>Where this run's log goes (-logFile, else the player log).</summary>
        static string LogPath => System.IO.Path.GetFullPath(Arg("-logFile") ?? Application.consoleLogPath);
    }
}
