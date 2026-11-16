using System;
using System.Collections;
using System.IO;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // QA probes for long play and repeated starts (phase 5):
    //   -load SAVE -soak MINUTES [-crowd N] [-simspeed S]: batch mode, every frame rendered into a 1920x1080 texture, the
    //     loaded town (plus N extra people) played at speed S with the auto-player; once a minute a line with frame times,
    //     gen-0 collections, managed bytes allocated per frame, managed heap and Unity's total allocated memory; then quit
    //     (native leak warnings, if any, follow in the log).
    //   -reloadtest [-load SAVE]: menu -> new game -> load -> new game -> main menu -> quit through the real flow (each step
    //     reloads the scene), logging memory after each, so leaks across games show up.
    public sealed partial class GameController
    {
        static int _reloadStage = -1;

        IEnumerator Soak(float minutes)
        {
            for (int i = 0; i < 30; i++) yield return null;
            if (HasArg("-noauto")) Auto = null; else Auto ??= new AutoPlayer(Sim);
            int crowd = ArgInt("-crowd", 0);
            if (crowd > 0) Sim.DebugSpawn(crowd);
            SetSpeed(ArgInt("-simspeed", 3));
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt };
            var total = System.Diagnostics.Stopwatch.StartNew();
            Debug.Log($"[Soak] start: day {Sim.Day}, population {Sim.Population}, buildings {Sim.Buildings.Count}, managed heap {GC.GetTotalMemory(false) / 1048576.0:F1} MB, Unity allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB");
            int minute = 0;
            while (total.Elapsed.TotalMinutes < minutes)
            {
                minute++;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                long gc0 = GC.CollectionCount(0), alloc0 = Allocated();
                int frames = 0; double worst = 0, last = 0;
                while (sw.Elapsed.TotalSeconds < 60 && total.Elapsed.TotalMinutes < minutes)
                {
                    if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(Cam.Cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, req);
                    yield return null;
                    double now = sw.Elapsed.TotalMilliseconds;
                    worst = math.max(worst, now - last); last = now;
                    frames++;
                }
                Debug.Log($"[Soak] minute {minute}: {sw.Elapsed.TotalMilliseconds / math.max(1, frames):F2} ms per frame (worst {worst:F1}) over {frames} frames, gen0 {GC.CollectionCount(0) - gc0}, " +
                          $"{(Allocated() - alloc0) / (double)math.max(1, frames):F0} managed bytes per frame, heap {GC.GetTotalMemory(false) / 1048576.0:F1} MB, Unity allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB; " +
                          $"day {Sim.Day}, population {Sim.Population}, buildings {Sim.Buildings.Count}, events {Sim.Events.Count}");
            }
            rt.Release();
            Debug.Log("[Soak] done");
            Application.Quit();
        }

        /// <summary>
        /// -load SAVE -shot NAME [-outdir DIR] [-shotscale S]: a still of the loaded game with the HUD after a few seconds
        /// (windowed: -popupwindow -screen-width W -screen-height H), e.g. the empty-settlement screen from a dead save.
        /// </summary>
        IEnumerator OneShot(string name)
        {
            _shotDir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "shots"));
            Directory.CreateDirectory(_shotDir);
            if (float.TryParse(Arg("-shotscale"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float sc)) UI.UiTheme.UserScale = sc;
            if (HasArg("-cine")) { SetCinematic(true); _cineAmount = 1f; }   // the cinematic view (F10) for the still
            if (Arg("-settingstab") != null) { Hud.SettingsTab = ArgInt("-settingstab", 0); Hud.OpenMenu(); Hud.ShowScreen(Firstlight.Game.UI.Hud.MenuScreen.Settings); }   // the settings screen open on a tab
            for (int i = 0; i < 40; i++) yield return null;
            yield return Shot(name);
            Application.Quit();
        }

        static long Allocated()
        {
            try { return GC.GetAllocatedBytesForCurrentThread(); } catch { return 0; }
        }

        IEnumerator ReloadTest()
        {
            for (int i = 0; i < 30; i++) yield return null;
            yield return new WaitForSecondsRealtime(1f);
            GC.Collect();
            Debug.Log($"[Reload] stage {_reloadStage} ({(InMenu ? "menu" : "game")}): managed heap {GC.GetTotalMemory(true) / 1048576.0:F1} MB, Unity allocated {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0:F1} MB, objects {Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length}, audio clips {Resources.FindObjectsOfTypeAll<AudioClip>().Length}, textures {Resources.FindObjectsOfTypeAll<Texture>().Length}, meshes {Resources.FindObjectsOfTypeAll<Mesh>().Length}");
            string save = Arg("-load");
            _reloadStage++;
            switch (_reloadStage)
            {
                case 0: case 2: case 4: StartNewGame(2026 + _reloadStage, 1); break;
                case 1: case 3: if (save != null) LoadGame(save); else StartNewGame(99, 2); break;
                case 5: ToMainMenu(); break;
                default: Debug.Log("[Reload] done"); Application.Quit(); break;
            }
        }
    }
}
