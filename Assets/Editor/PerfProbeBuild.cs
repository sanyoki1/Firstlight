using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Firstlight.EditorTools
{
    /// <summary>
    /// PERF PROBE (performance audit, pass 2): builds the player into runs/pass2/perf/build-probe instead of Build/, so a
    /// measuring build never overwrites (or half-overwrites, when a running player holds a file) the shared Build/.
    ///   tools/unity.sh Firstlight.EditorTools.PerfProbeBuild.BuildWindows LOGNAME
    /// </summary>
    public static class PerfProbeBuild
    {
        public static void BuildWindows()
        {
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Game.unity", "Assets/Scenes/StressTest.unity" },
                locationPathName = "runs/pass2/perf/build-probe/Firstlight.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Build] probe build {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
