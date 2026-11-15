using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Save files. Layout: "FLSV", format version, a small uncompressed header (what the load screen lists: name, kind,
    /// age, year, season, population, real date), then a gzip body: the map's changes, the settlement, the auto-player
    /// (if one runs), and two length-prefixed blocks for the game (camera, HUD, water) and the auto-test. The map itself
    /// is regenerated from its seed on load. Files are written to a temporary name and swapped into place (File.Replace
    /// over an existing save), so a crash or a failed move mid-save never leaves a half-written save or an empty slot.
    /// </summary>
    public static class SaveGame
    {
        /// <summary>
        /// Save format. 1: the first format (goods by position, milestones by position, no map check). 2: the body starts
        /// with a hash of the map-generation settings and the goods list by id; goods and milestones are matched by id.
        /// Readers take a field added in version N as <c>r.Version &gt;= N ? r.X() : default</c>.
        /// </summary>
        public const int Version = 2;

        /// <summary>The goods of format 1, in the order its per-good arrays and indices use (resources.json at the time).</summary>
        static readonly string[] V1Goods =
        {
            "wild_food", "meat", "fish", "preserved", "wood", "firewood", "stone", "hides", "clothing", "herbs", "tools",
            "copper_tools", "bronze_tools", "grain", "fibre", "pottery", "clay", "copper_ore", "tin_ore", "copper", "bronze",
        };

        /// <summary>
        /// A hash of everything <see cref="GameMap.Generate"/> reads from the balance (and a version for the generator
        /// itself: bump it when the generation code changes what a seed makes). A load regenerates the map from the seed,
        /// so a save is only valid with the same inputs.
        /// </summary>
        public const int MapGenVersion = 1;
        public static int MapGenHash(Balance b)
        {
            unchecked
            {
                uint h = 2166136261;
                void Mix(int v) { for (int i = 0; i < 4; i++) { h ^= (byte)(v >> (8 * i)); h *= 16777619; } }
                Mix(MapGenVersion); Mix(b.mapSize); Mix(BitConverter.SingleToInt32Bits(b.rockStone)); Mix(b.copperDeposits); Mix(b.tinDeposits);
                return (int)h;
            }
        }

        /// <summary>Test hook (-savecheck): rewrites the goods list a save says it has, to load it as a build with other goods would.</summary>
        internal static Func<List<string>, List<string>> TestSavedGoods;
        const string Magic = "FLSV";
        public const string Extension = ".flsave";

        public static string Dir => Path.Combine(DataPaths.Root, "saves");   // the real folder only for a plain launch (DataPaths)

        public sealed class Header
        {
            public int Version, Seed, Day, Year, Population, Difficulty;
            public string Name = "", Kind = "manual", Age = "", Season = "";
            public long SavedUtcTicks;
            public string File;                         // where it was read from (not stored)
            public DateTime SavedLocal => new DateTime(SavedUtcTicks, DateTimeKind.Utc).ToLocalTime();
        }

        /// <summary>What a load brings back: the map and settlement ready to run, plus the game's and the test's blocks.</summary>
        public sealed class Loaded
        {
            public Header Header;
            public GameMap Map;
            public Settlement Sim;
            public AutoPlayer Auto;
            public byte[] Game, Test;
            public float[] WaterDepth;
        }

        public static Header HeaderFor(Settlement s, string name, string kind) => new Header
        {
            Version = Version, Seed = s.Seed, Day = s.Day, Year = s.Year + 1, Population = s.Population, Difficulty = s.Difficulty,
            Name = name, Kind = kind, Age = s.Age?.name ?? "", Season = s.NeedsCampfire ? "" : s.SeasonName, SavedUtcTicks = DateTime.UtcNow.Ticks,
        };

        public static void Write(string path, Header h, Settlement s, AutoPlayer auto, WaterSim water, byte[] game, byte[] test)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            {
                var w = new SaveWriter(fs);
                w.W.Write(System.Text.Encoding.ASCII.GetBytes(Magic));
                w.Int(Version);
                w.Str(h.Name); w.Str(h.Kind); w.Int(h.Seed); w.Int(h.Day); w.Int(h.Year); w.Str(h.Season); w.Str(h.Age);
                w.Int(h.Population); w.Int(h.Difficulty); w.Long(h.SavedUtcTicks);
                w.W.Flush();
                // The body is written into memory first and compressed in one call: written straight into the GZipStream it
                // made ~200k tiny deflate writes (30-44 ms and ~10 collections a save). The bytes saved mean the same.
                var body = new MemoryStream(256 * 1024);
                var b = new SaveWriter(body);
                b.Int(s.Map.W);
                b.Int(MapGenHash(s.Data.Balance));
                var goods = new List<string>(s.ResCount); foreach (var g in s.Data.Resources) goods.Add(g.id);
                b.Strs(goods);
                s.Map.Write(b);
                s.Write(b);
                b.Bool(auto != null);
                auto?.Write(b);
                float[] depth = null;
                if (water != null) { depth = new float[water.Depth.Length]; water.Depth.CopyTo(depth); }
                b.Floats(depth);
                b.Block(game);
                b.Block(test);
                b.Str("end");
                b.W.Flush();
                using var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest, true);
                gz.Write(body.GetBuffer(), 0, (int)body.Length);
            }
            // Overwriting: swap the new file in with File.Replace, which keeps the old save (as .bak) until the new one is in
            // place, so a failed move (a scanner or sync client holding the file) or a crash never leaves the slot empty.
            // If it throws, the old save is untouched and the caller shows the reason.
            if (File.Exists(path))
            {
                string bak = path + ".bak";
                File.Replace(tmp, path, bak);
                try { File.Delete(bak); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            else File.Move(tmp, path);
        }

        /// <summary>
        /// Tidies what an unfinished save can leave (on listing): a backup whose save is missing (a replace that failed
        /// half way) is put back under its name; leftover .bak and .tmp files are deleted.
        /// </summary>
        internal static void CleanStale(string dir)
        {
            foreach (var bak in Directory.GetFiles(dir, "*" + Extension + ".bak"))
            {
                string save = bak.Substring(0, bak.Length - 4);
                try { if (File.Exists(save)) File.Delete(bak); else File.Move(bak, save); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            foreach (var tmp in Directory.GetFiles(dir, "*" + Extension + ".tmp"))
            {
                try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        /// <summary>Reads only the header (for the load screen). Null with a reason for anything that is not a save this build can read.</summary>
        public static Header ReadHeader(string path, out string error)
        {
            try
            {
                using var fs = File.OpenRead(path);
                return ReadHeader(new SaveReader(fs), path, out error);
            }
            catch (Exception e) { error = $"Could not read the save ({e.GetType().Name}: {e.Message})."; return null; }
        }

        static Header ReadHeader(SaveReader r, string path, out string error)
        {
            error = null;
            var magic = System.Text.Encoding.ASCII.GetString(r.R.ReadBytes(4));   // bytes, not chars: ReadChars may read ahead
            if (magic != Magic) { error = "This is not a Firstlight save file."; return null; }
            int v = r.Int();
            if (v > Version) { error = $"This save was made by a newer build (save format {v}); this build reads format {Version} and older."; return null; }
            if (v < 1) { error = $"Unknown save format {v}."; return null; }
            var h = new Header { Version = v, File = path };
            h.Name = r.Str() ?? ""; h.Kind = r.Str() ?? ""; h.Seed = r.Int(); h.Day = r.Int(); h.Year = r.Int(); h.Season = r.Str() ?? ""; h.Age = r.Str() ?? "";
            h.Population = r.Int(); h.Difficulty = r.Int(); h.SavedUtcTicks = r.Long();
            return h;
        }

        /// <summary>
        /// Loads a save: regenerates the map from the seed (and settles the river), puts the map's changes back and restores
        /// the settlement. Null with a player-facing reason if the file is unknown, newer, damaged or for other game data.
        /// </summary>
        public static Loaded Read(string path, GameData data, out string error)
        {
            GameMap map = null;
            try
            {
                using var fs = File.OpenRead(path);
                var r0 = new SaveReader(fs);
                var h = ReadHeader(r0, path, out error);
                if (h == null) return null;
                using var gz = new GZipStream(fs, CompressionMode.Decompress, true);
                var r = new SaveReader(gz) { Version = h.Version };
                int size = r.Int();
                if (size != data.Balance.mapSize) { error = $"The save's map size ({size}) does not match this build's ({data.Balance.mapSize})."; return null; }
                var current = new List<string>(data.Resources.Count); foreach (var g in data.Resources) current.Add(g.id);
                List<string> saved;
                if (r.Version >= 2)
                {
                    int hash = r.Int();
                    if (hash != MapGenHash(data.Balance))
                    {
                        error = "This save's land was made with other map settings than this build uses (rivers, rock and ore would move under the buildings), so it cannot be loaded.";
                        return null;
                    }
                    saved = r.Strs() ?? throw new InvalidDataException("missing goods list");
                    if (TestSavedGoods != null) saved = TestSavedGoods(saved);
                }
                else saved = new List<string>(V1Goods);
                r.SetGoods(saved, current);
                if (r.DroppedGoods.Count > 0)
                    Debug.LogWarning($"[Save] {Path.GetFileName(path)}: goods this build does not have were dropped: {string.Join(", ", r.DroppedGoods)}");
                map = new GameMap(size);
                map.Generate(h.Seed, data.Balance);
                map.Read(r);
                var s = new Settlement(data, map, h.Seed);
                s.Read(r);
                AutoPlayer auto = null;
                if (r.Bool()) { auto = new AutoPlayer(s); auto.Read(r); }
                var res = new Loaded { Header = h, Map = map, Sim = s, Auto = auto, WaterDepth = r.Floats(), Game = r.Block(), Test = r.Block() };
                if (r.Str() != "end") throw new InvalidDataException("missing end marker");
                return res;
            }
            catch (Exception e)
            {
                map?.Dispose();
                error = $"The save could not be loaded: it is damaged or from an incompatible build ({e.GetType().Name}: {e.Message}).";
                Debug.LogWarning($"[Save] {path}: {e}");
                return null;
            }
        }

        // ------------------------------------------------------------------ slots

        public static string PathFor(string slot) => Path.Combine(Dir, Sanitize(slot) + Extension);

        public static string Sanitize(string name)
        {
            var chars = (name ?? "").Trim().Select(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_' ? ch : '_').ToArray();
            var s = new string(chars).Trim();
            return s.Length == 0 ? "save" : s.Length > 40 ? s.Substring(0, 40) : s;
        }

        /// <summary>Every readable save, newest first (unreadable files are listed with their error).</summary>
        public static List<(Header h, string file, string error)> List()
        {
            var list = new List<(Header, string, string)>();
            if (!Directory.Exists(Dir)) return list;
            CleanStale(Dir);
            foreach (var f in Directory.GetFiles(Dir, "*" + Extension))
            {
                var h = ReadHeader(f, out string err);
                list.Add((h, f, err));
            }
            return list.OrderByDescending(x => x.Item1?.SavedUtcTicks ?? File.GetLastWriteTimeUtc(x.Item2).Ticks).ToList();
        }

        public static (Header h, string file)? Latest()
        {
            foreach (var (h, f, err) in List()) if (h != null) return (h, f);
            return null;
        }

        /// <summary>The autosave slot to write next: the oldest of the three (or a free one).</summary>
        public static string NextAutosave()
        {
            string best = null; DateTime oldest = DateTime.MaxValue;
            for (int i = 1; i <= 3; i++)
            {
                var p = PathFor($"autosave-{i}");
                if (!File.Exists(p)) return p;
                var t = File.GetLastWriteTimeUtc(p);
                if (t < oldest) { oldest = t; best = p; }
            }
            return best;
        }
    }
}
