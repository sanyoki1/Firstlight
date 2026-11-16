using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    // Test mode: is 1x really 1x?
    //   [-load SAVE] -speedcheck [-speedsecs N] [-speedcaps 0,30,60,144,8,2] [-speedlist 1,2,4] [-crowd N] [-outdir DIR]
    // For each speed and each frame-rate cap (0 = uncapped) the loaded town runs for N real seconds (default 45). Game
    // time passed (the settlement's clock) is compared with a wall clock that Unity's frame timing cannot touch
    // (a Stopwatch). Writes speedcheck.csv and a table in the log, then quits. Windowed (-popupwindow) or batch mode.
    public sealed partial class GameController
    {
        IEnumerator SpeedCheck()
        {
            string dir = Path.GetFullPath(Arg("-outdir") ?? Path.Combine(Application.dataPath, "..", "..", "runs", "speed"));
            Directory.CreateDirectory(dir);
            for (int i = 0; i < 60; i++) yield return null;
            // Without -load or -seed the game opens on its title menu: start a new game the way a player does (the scene
            // reloads and this starts again), and let the auto-player choose the campfire site.
            if (InMenu) { StartNewGame(ArgInt("-speedseed", 2026), 1); yield break; }
            if (Sim.NeedsCampfire)
            {
                Auto = new AutoPlayer(Sim);
                while (Sim.NeedsCampfire) yield return null;
            }
            Auto = null;
            int crowd = ArgInt("-crowd", 0);
            if (crowd > 0) Sim.DebugSpawn(crowd);
            float secs = ArgInt("-speedsecs", 45);
            int[] caps = ParseInts(Arg("-speedcaps") ?? "0,30,60,144,8,2");
            int[] speeds = ParseInts(Arg("-speedlist") ?? "1,2,4");
            // Batch mode draws nothing by itself: render each frame into a texture, so a frame costs what it does in play.
            RenderTexture rt = null;
            var req = new UnityEngine.Rendering.RenderPipeline.StandardRequest();
            if (Application.isBatchMode)
            {
                rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
                req.destination = rt;
            }
            var sb = new StringBuilder();
            sb.Append("speed,cap,wall_s,game_s,ratio,frames,fps,sum_dt,sum_unscaled,worst_frame_ms,frames_over_limit,calendar_s,population\n");
            QualitySettings.vSyncCount = 0;
            Debug.Log($"[Speed] start: population {Sim.Population}, buildings {Sim.Buildings.Count}, day length {Data.Balance.dayLengthSeconds} s, " +
                      $"maximumDeltaTime {Time.maximumDeltaTime:F3}, timeScale {Time.timeScale}, batch {Application.isBatchMode}, {Screen.width}x{Screen.height}");
            foreach (int sp in speeds)
                foreach (int cap in caps)
                {
                    Application.targetFrameRate = cap > 0 ? cap : -1;
                    SetSpeed(sp);
                    for (int i = 0; i < 5; i++) yield return null;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    double game0 = Sim.Time, cal0 = (Sim.Day + (double)Sim.DayFraction) * Data.Balance.dayLengthSeconds;
                    double sumDt = 0, sumUn = 0, worst = 0, last = 0;
                    int frames = 0, over = 0;
                    while (sw.Elapsed.TotalSeconds < secs)
                    {
                        if (rt != null && UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(Cam.Cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(Cam.Cam, req);
                        yield return null;
                        double now = sw.Elapsed.TotalMilliseconds;
                        worst = math.max(worst, now - last); last = now;
                        sumDt += Time.deltaTime; sumUn += Time.unscaledDeltaTime;
                        if (Time.unscaledDeltaTime > Time.deltaTime + 1e-4f) over++;
                        frames++;
                    }
                    double wall = sw.Elapsed.TotalSeconds;
                    double game = Sim.Time - game0, cal = (Sim.Day + (double)Sim.DayFraction) * Data.Balance.dayLengthSeconds - cal0;
                    string row = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F2},{3:F2},{4:F4},{5},{6:F1},{7:F2},{8:F2},{9:F1},{10},{11:F2},{12}",
                        sp, cap, wall, game, game / (wall * sp), frames, frames / wall, sumDt, sumUn, worst, over, cal, Sim.Population);
                    sb.Append(row).Append('\n');
                    Debug.Log("[Speed] " + row);
                    File.WriteAllText(Path.Combine(dir, "speedcheck.csv"), sb.ToString());
                }
            if (rt != null) rt.Release();
            Debug.Log("[Speed] done");
            Application.Quit();
        }

        static int[] ParseInts(string list)
        {
            var parts = list.Split(',');
            var r = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) int.TryParse(parts[i], out r[i]);
            return r;
        }
    }
}
