using System;
using System.Collections;
using System.IO;
using System.Linq;
using Firstlight.Core;
using Firstlight.Game.UI;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Firstlight.Game
{
    // The game's flow: title menu, new game, saving and loading (quick, auto, named), returning to the menu. Starting a
    // game or loading one reloads the scene with the request in a static, so every view is built fresh from the map
    // and settlement exactly as at a normal start. Command-line modes (-autotest, -uishots, -seed, the clip modes...)
    // skip the menu.
    public sealed partial class GameController
    {
        public enum LaunchMode { Menu, New, Load }

        public sealed class Launch
        {
            public LaunchMode Mode; public int Seed, Difficulty = 1; public string Path;
            public static Launch Menu() => new() { Mode = LaunchMode.Menu };
            public static Launch NewGame(int seed, int difficulty) => new() { Mode = LaunchMode.New, Seed = seed, Difficulty = difficulty };
            public static Launch LoadFile(string path) => new() { Mode = LaunchMode.Load, Path = path };
        }

        static Launch _next;
        /// <summary>The title screen is up (the world behind it is only a backdrop).</summary>
        public bool InMenu { get; private set; }
        /// <summary>A message for the title screen (a load that failed).</summary>
        public static string MenuMessage;
        const int MenuSeed = 7;
        Vector3 _menuFocus;
        public SoundScape Sound { get; private set; }

        /// <summary>Started for a test or a clip (no menu, no autosaves, no settings applied to the window).</summary>
        static bool CommandLineMode => new[] { "-seed", "-autoplay", "-uishots", "-p4shots", "-shots", "-watershots", "-deershots", "-deerpair", "-huntshots", "-load", "-benchmark" }.Any(HasArg);

        /// <summary>The in-game menu is open: the settlement waits.</summary>
        public bool Paused => Hud != null && Hud.MenuOpen;

        void MenuUpdate()
        {
            // The backdrop turns slowly round the most interesting spot on the map.
            Cam.Yaw += Time.unscaledDeltaTime * 2.2f;
            Cam.Focus = _menuFocus;
            Hud.MenuKeys();
        }

        // ------------------------------------------------------------------ menu actions (the menu's buttons call these)

        public void StartNewGame(int seed, int difficulty) { _next = Launch.NewGame(seed, difficulty); Reload(); }
        public void LoadGame(string path) { _next = Launch.LoadFile(path); Reload(); }
        public void ToMainMenu() { _next = Launch.Menu(); Reload(); }
        public void QuitGame()
        {
            Debug.Log("[Game] quit");
            Application.Quit();
        }

        void Reload()
        {
            OrbitCamera.InputLocked = OrbitCamera.KeyboardCaptured = false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        // ------------------------------------------------------------------ saving

        float _savedAt = -1f;               // sim time of the last save or load (-1 = never)
        int _autosaveSeason = -1;

        /// <summary>Game time played since the last save (0 if nothing has happened yet).</summary>
        public bool Unsaved => !Sim.NeedsCampfire && (_savedAt < 0 || Sim.Time - _savedAt > 1f);
        public float SecondsSinceSave => _savedAt < 0 ? Sim.Time : Sim.Time - _savedAt;

        /// <summary>Writes the game to a file. Returns an error message, or null.</summary>
        public string SaveTo(string path, string name, string kind)
        {
            if (Sim.NeedsCampfire) return "Nothing to save until the campfire is lit.";
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                SaveGame.Write(path, SaveGame.HeaderFor(Sim, name, kind), Sim, Auto, Water, WriteGameBlock(), null);
                _savedAt = Sim.Time;
                Debug.Log($"[Save] {kind} save to {path} in {sw.Elapsed.TotalMilliseconds:F0} ms: day {Sim.Day}, population {Sim.Population}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] could not save to {path}: {e}");
                return $"Could not save: {e.Message}";
            }
        }

        public void QuickSave()
        {
            string err = SaveTo(SaveGame.PathFor("quicksave"), "Quicksave", "quick");
            Hud.Toast(err ?? "Quicksaved (F8 to load it)");
        }

        public void QuickLoad()
        {
            var p = SaveGame.PathFor("quicksave");
            if (!File.Exists(p)) { Hud.Toast("No quicksave yet (F5 saves one)"); return; }
            var h = SaveGame.ReadHeader(p, out string err);
            if (h == null) { Hud.Toast(err); return; }
            LoadGame(p);
        }

        float _autosaveDue = -1f;           // real time this season's autosave is due (-1 = none waiting)

        /// <summary>
        /// At the start of each season, if autosaves are on: the oldest of three autosave slots. Called after the sim's
        /// step. The save waits a second after the season begins, and never shares a frame with a day tick, so the season
        /// start (the daily step and its path rebuild) and the save are two short hitches instead of one long one.
        /// </summary>
        void Autosave(bool dayTicked)
        {
            if (Sim.NeedsCampfire || Sim.Population == 0 || HasArg("-uishots") || HasArg("-p4shots") || HasArg("-autoplay")) return;
            int season = Sim.Day / Data.Balance.daysPerSeason;
            if (_autosaveSeason < 0) { _autosaveSeason = season; return; }
            if (season != _autosaveSeason)
            {
                _autosaveSeason = season;
                if (GameSettings.Current.autosave) _autosaveDue = Time.realtimeSinceStartup + 1f;
            }
            if (_autosaveDue < 0 || dayTicked || Time.realtimeSinceStartup < _autosaveDue) return;
            _autosaveDue = -1f;
            if (!GameSettings.Current.autosave) return;
            string err = SaveTo(SaveGame.NextAutosave(), $"Autosave: {Sim.SeasonName}, year {Sim.Year + 1}", "auto");
            Hud.Toast(err ?? "Autosaved");
        }

        // ------------------------------------------------------------------ the game's own block in a save

        const int GameBlockVersion = 1;

        byte[] WriteGameBlock()
        {
            var ms = new MemoryStream();
            var w = new SaveWriter(ms);
            w.Int(GameBlockVersion);
            w.Float(Cam.Focus.x); w.Float(Cam.Focus.y); w.Float(Cam.Focus.z); w.Float(Cam.Distance); w.Float(Cam.Pitch); w.Float(Cam.Yaw);
            w.Float(_lastSpeed);
            Hud.WriteState(w);
            w.W.Flush();
            return ms.ToArray();
        }

        void ReadGameBlock(byte[] block)
        {
            if (block == null || block.Length < 4) return;
            try
            {
                var r = new SaveReader(new MemoryStream(block));
                if (r.Int() != GameBlockVersion) return;
                var focus = new Vector3(r.Float(), r.Float(), r.Float());
                float dist = r.Float(), pitch = r.Float(), yaw = r.Float();
                Cam.SetView(focus, dist, pitch, yaw);
                _lastSpeed = r.Float();
                Hud.ReadState(r);
            }
            catch (Exception e) { Debug.LogWarning($"[Save] view state not restored: {e.Message}"); }
        }

        /// <summary>A fingerprint of the saved state (map changes, settlement, auto-player): equal before saving and after loading.</summary>
        public string StateDigest()
        {
            var ms = new MemoryStream();
            var w = new SaveWriter(ms);
            Map.Write(w); Sim.Write(w); w.Bool(Auto != null); Auto?.Write(w);
            w.W.Flush();
            using var sha = System.Security.Cryptography.SHA1.Create();
            return BitConverter.ToString(sha.ComputeHash(ms.ToArray())).Replace("-", "").Substring(0, 16);
        }

        // ------------------------------------------------------------------ smoke test of the default launch path

        static int _smoke;                  // 0 off; 1 menu -> new game; 2 in the new game; 3 menu -> continue; 4 in the loaded game
        static string _smokeDigestLoaded;

        /// <summary>
        /// -smoketest: the default launch path as a player would take it, through the same buttons: title menu, New Game,
        /// campfire placed by a click, a minute of play, saved from the in-game menu, quit. -smoketest -smokecontinue: title
        /// menu, Continue, and a check that the loaded state is exactly the one saved (state fingerprints), then quit.
        /// </summary>
        IEnumerator SmokeTest()
        {
            string dir = Path.Combine(Application.persistentDataPath, "smoke");
            Directory.CreateDirectory(dir);
            string digestFile = Path.Combine(dir, "digest.txt");
            if (_smoke == 0) _smoke = HasArg("-smokecontinue") ? 3 : 1;
            Debug.Log($"[Smoke] stage {_smoke}, in menu {InMenu}");
            for (int i = 0; i < 20; i++) yield return null;
            yield return new WaitForSecondsRealtime(2f);
            switch (_smoke)
            {
                case 1:
                    _smoke = 2;
                    Hud.MenuNewGame(2026, 1);
                    yield break;
                case 2:
                {
                    // Place the campfire as a click would: at the suggested site.
                    var tc = Data.Buildings.Find(b => b.townCenter);
                    var site = Sim.SuggestCampfireSite();
                    var c = Settlement.FootprintCenter(tc, site);
                    Hud.StopCamera();
                    Cam.SetView(new Vector3(c.x, Map.HeightAt(c.x, c.y), c.y), 30f, 52f, 30f);
                    for (int i = 0; i < 5; i++) yield return null;
                    Hud.PointerOverride = (Vector2)Cam.Cam.WorldToScreenPoint(new Vector3(c.x, Map.HeightAt(c.x, c.y), c.y));
                    yield return null;
                    Hud.ClickWorld();
                    Hud.PointerOverride = null;
                    Debug.Log($"[Smoke] campfire {(Sim.NeedsCampfire ? "NOT placed" : "placed")}; playing a minute at 4x");
                    SetSpeed(4);
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 60f) yield return null;
                    Hud.OpenMenu();
                    yield return null;
                    string err = Hud.MenuSave("Smoke test");
                    string digest = StateDigest();
                    File.WriteAllText(digestFile, $"{digest}\n{Sim.Day}\n{Sim.Population}\n");
                    Debug.Log($"[Smoke] saved: {err ?? "ok"}; day {Sim.Day} {Sim.DayFraction:F2}, population {Sim.Population}, buildings {Sim.Buildings.Count}, digest {digest}");
                    _smoke = 0;
                    QuitGame();
                    yield break;
                }
                case 3:
                    _smoke = 4;
                    if (!Hud.MenuContinue()) { Debug.LogError("[Smoke] no save to continue"); QuitGame(); }
                    yield break;
                case 4:
                {
                    var want = File.Exists(digestFile) ? File.ReadAllLines(digestFile) : new string[0];
                    bool match = want.Length > 0 && want[0] == _smokeDigestLoaded;
                    Debug.Log($"[Smoke] continued: day {Sim.Day}, population {Sim.Population}; digest at load {_smokeDigestLoaded}, at save {(want.Length > 0 ? want[0] : "-")}: {(match ? "MATCH" : "MISMATCH")}");
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 15f) yield return null;
                    Debug.Log($"[Smoke] after 15 s more: day {Sim.Day}, population {Sim.Population}");
                    _smoke = 0;
                    QuitGame();
                    yield break;
                }
            }
        }

        /// <summary>Called right after a load in smoke stage 4, before the settlement has moved on.</summary>
        void SmokeAfterLoad() { if (_smoke == 4) _smokeDigestLoaded = StateDigest(); }
    }
}
