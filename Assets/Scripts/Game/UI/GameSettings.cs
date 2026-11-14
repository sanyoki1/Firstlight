using System;
using System.IO;
using Firstlight.Core;
using UnityEngine;

namespace Firstlight.Game.UI
{
    /// <summary>
    /// Player settings, kept in persistentDataPath/settings.json (plain JsonUtility, so a hand edit or a missing field
    /// falls back to the default). Applied at start and whenever the settings screen changes something.
    /// </summary>
    [Serializable]
    public sealed class GameSettings
    {
        public float uiScale = 1f;            // on top of the 1080-high layout scale
        public bool fullscreen = true;
        public int width, height;             // 0 = the desktop's resolution
        public bool vsync = true;
        public int frameCap = 0;              // 0 = none (vsync decides), else frames per second
        public bool edgeScroll = true;
        public float cameraSpeed = 1f;
        public bool autosave = true;
        public bool guide = true;             // the first-game objectives card
        public bool hints = true;             // one-time contextual hints
        public float volMaster = 0.8f, volAmbience = 0.7f, volUi = 0.7f;
        public bool guideDone;                // the guide was finished or closed once (not shown again for new games)

        public static GameSettings Current { get; private set; } = new();
        static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Current = JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
            }
            catch (Exception e) { Debug.LogWarning($"[Settings] could not read {FilePath}: {e.Message}; using defaults"); Current = new GameSettings(); }
            Current.uiScale = Mathf.Clamp(Current.uiScale, 0.7f, 1.6f);
            Current.cameraSpeed = Mathf.Clamp(Current.cameraSpeed, 0.3f, 3f);
        }

        public static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
            catch (Exception e) { Debug.LogWarning($"[Settings] could not write {FilePath}: {e.Message}"); }
        }

        /// <summary>Puts the display, frame rate and camera settings into effect (not the window in batch / shot modes).</summary>
        public static void Apply(bool display)
        {
            var s = Current;
            UiTheme.UserScale = s.uiScale;
            OrbitCamera.SpeedScale = s.cameraSpeed;
            OrbitCamera.EdgeScrollEnabled = s.edgeScroll;
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            Application.targetFrameRate = s.frameCap > 0 ? s.frameCap : -1;
            if (!display || Application.isBatchMode) return;
            var mode = s.fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            int w = s.width > 0 ? s.width : Screen.currentResolution.width, h = s.height > 0 ? s.height : Screen.currentResolution.height;
            if (Screen.width != w || Screen.height != h || Screen.fullScreenMode != mode) Screen.SetResolution(w, h, mode);
        }

        /// <summary>Window sizes offered in the settings (the desktop's own first).</summary>
        public static readonly Vector2Int[] Resolutions = { new(0, 0), new(1280, 720), new(1600, 900), new(1920, 1080), new(2560, 1440), new(3840, 2160) };
    }
}
