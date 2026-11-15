using System;
using System.IO;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Where the game keeps the player's files: saves (named, quick, auto), settings.json and the smoke test's digest.
    /// A plain launch by a person uses Unity's persistentDataPath (the owner's real folder). Every test or capture mode
    /// uses a scratch folder instead, so test runs never overwrite the owner's autosaves or settings:
    /// <c>-savedir DIR</c> if given, else <c>&lt;-outdir&gt;/userdata</c>, else a fixed folder under the system temp
    /// folder (fixed, so <c>-smoketest</c> and <c>-smoketest -smokecontinue</c> find each other's save).
    /// </summary>
    public static class DataPaths
    {
        // Modes nobody is playing: tests, screenshot drivers, probes. A plain "-seed N" or "-load FILE" is a person.
        static readonly string[] TestModes =
        {
            "-autotest", "-fuzztest", "-uishots", "-p4shots", "-guideshots", "-shots", "-shot", "-watershots", "-deershots", "-deerpair",
            "-huntshots", "-smoketest", "-smokecontinue", "-soak", "-perfprobe", "-faulttest", "-autoplay", "-reloadtest",
            "-benchmark", "-quitafter", "-batchmode", "-nographics",
        };

        static string _root;

        /// <summary>The folder the player's files go in (created on first use).</summary>
        public static string Root
        {
            get
            {
                if (_root != null) return _root;
                _root = Resolve(Environment.GetCommandLineArgs(), out string why);
                try { Directory.CreateDirectory(_root); } catch (Exception e) { Debug.LogWarning($"[Paths] could not create {_root}: {e.Message}"); }
                if (why != null) Debug.Log($"[Paths] player files (saves, settings) go to {_root} ({why})");
                return _root;
            }
        }

        /// <summary>Whether this run uses a scratch folder rather than the real one.</summary>
        public static bool Scratch => Root != Application.persistentDataPath;

        static string Resolve(string[] args, out string why)
        {
            string dir = ArgValue(args, "-savedir");
            if (!string.IsNullOrEmpty(dir)) { why = "-savedir"; return Path.GetFullPath(dir); }
            string mode = null;
            foreach (var m in TestModes) if (Array.IndexOf(args, m) >= 0) { mode = m; break; }
            if (mode == null) { why = null; return Application.persistentDataPath; }
            string outdir = ArgValue(args, "-outdir");
            why = $"{mode}: a scratch folder, not the real one; -savedir DIR to choose";
            return !string.IsNullOrEmpty(outdir) ? Path.Combine(Path.GetFullPath(outdir), "userdata") : Path.Combine(Path.GetTempPath(), "Firstlight-scratch");
        }

        static string ArgValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
