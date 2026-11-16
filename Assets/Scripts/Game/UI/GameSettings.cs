using System;
using System.IO;
using Firstlight.Core;
using UnityEngine;

namespace Firstlight.Game.UI
{
    /// <summary>
    /// Player settings, kept in DataPaths.Root/settings.json (persistentDataPath for a plain launch) (plain JsonUtility, so a hand edit or a missing field
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
        public bool effects = true;           // post effects as a whole: soft shadowing in creases, glow, colour grading, vignette, soft focus (off = plainer, faster)
        // Strength of each part of the look: 0 = off, 1 = as designed, up to 1.5 (see Core/Look.cs Strength).
        public float fxOcclusion = 1f, fxBloom = 1f, fxGrade = 1f, fxVignette = 1f, fxFocus = 1f;
        public float fxHaze = 1f, fxMist = 1f, fxClouds = 1f, fxWind = 1f, fxSparkle = 1f;
        public float fxSmoke = 1f, fxSnowfall = 1f;
        public float groundDetail = 1f;       // share of the small ground cover drawn (reeds, ferns, pebbles, tufts, flowers, fallen wood)
        public bool birds = true;
        public bool cineBars = true;          // wide-screen bars in the cinematic view
        public bool cameraGlide = true;       // the view eases after zooms, turns and jumps
        public bool edgeScroll = true;
        public float cameraSpeed = 1f;
        public bool autosave = true;
        public bool guide = true;             // the first-game objectives card
        public bool hints = true;             // one-time contextual hints
        public float volMaster = 0.8f, volAmbience = 0.7f, volUi = 0.7f;
        public bool guideDone;                // the guide was finished or closed once (not shown again for new games)

        public const float MaxStrength = 1.5f;

        /// <summary>Every look setting back to how the game ships.</summary>
        public void ResetVisuals()
        {
            effects = true;
            fxOcclusion = fxBloom = fxGrade = fxVignette = fxFocus = fxHaze = fxMist = fxClouds = fxWind = fxSparkle = fxSmoke = fxSnowfall = 1f;
            groundDetail = 1f; birds = true; cineBars = true; cameraGlide = true;
        }

        /// <summary>Every strength to one value (test and screenshot modes: -fxscale X).</summary>
        public void ScaleVisuals(float v)
        {
            fxOcclusion = fxBloom = fxGrade = fxVignette = fxFocus = fxHaze = fxMist = fxClouds = fxWind = fxSparkle = fxSmoke = fxSnowfall = v;
            groundDetail = Mathf.Clamp01(v); birds = v > 0; cineBars = v > 0;
        }

        public static GameSettings Current { get; private set; } = new();
        static string FilePath => Path.Combine(DataPaths.Root, "settings.json");   // a scratch folder in test modes (DataPaths)

        public static void Load()
        {
            try
            {
                if (File.Exists(FilePath)) Current = JsonUtility.FromJson<GameSettings>(File.ReadAllText(FilePath)) ?? new GameSettings();
            }
            catch (Exception e) { Debug.LogWarning($"[Settings] could not read {FilePath}: {e.Message}; using defaults"); Current = new GameSettings(); }
            Current.uiScale = Mathf.Clamp(Current.uiScale, 0.7f, 1.6f);
            Current.cameraSpeed = Mathf.Clamp(Current.cameraSpeed, 0.3f, 3f);
            var c = Current;
            static void Fx(ref float v) => v = Mathf.Clamp(v, 0f, MaxStrength);
            Fx(ref c.fxOcclusion); Fx(ref c.fxBloom); Fx(ref c.fxGrade); Fx(ref c.fxVignette); Fx(ref c.fxFocus);
            Fx(ref c.fxHaze); Fx(ref c.fxMist); Fx(ref c.fxClouds); Fx(ref c.fxWind); Fx(ref c.fxSparkle);
            Fx(ref c.fxSmoke); Fx(ref c.fxSnowfall);
            c.groundDetail = Mathf.Clamp01(c.groundDetail);
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
            OrbitCamera.Glide = s.cameraGlide;
            Look.Strength.Occlusion = s.fxOcclusion; Look.Strength.Bloom = s.fxBloom; Look.Strength.Grade = s.fxGrade;
            Look.Strength.Vignette = s.fxVignette; Look.Strength.Focus = s.fxFocus; Look.Strength.Haze = s.fxHaze;
            Look.Strength.Mist = s.fxMist; Look.Strength.Clouds = s.fxClouds; Look.Strength.Wind = s.fxWind; Look.Strength.Sparkle = s.fxSparkle;
            SettlementView.SmokeAmount = s.fxSmoke;
            WorldView.GroundDetail = s.groundDetail; WorldView.BirdsOn = s.birds;
            Look.SetEffects(s.effects);
            Look.ApplyStrengths();
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
