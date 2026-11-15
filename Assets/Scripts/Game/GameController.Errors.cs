using System;
using System.Text;
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

        /// <summary>Where this run's log goes (-logFile, else the player log).</summary>
        static string LogPath => Arg("-logFile") ?? Application.consoleLogPath;
    }
}
