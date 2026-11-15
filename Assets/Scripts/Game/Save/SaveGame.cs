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

        // ------------------------------------------------------------------ writing
        //
        // A save is made in two halves. Serialize: the whole game state into memory (the uncompressed header and body),
        // on the main thread, in one go, so the state is one consistent moment: nothing steps the sim in between. Commit:
        // compress the body and write it to "<save>.tmp", then swap it into place (File.Replace, keeping the old save until
        // the new one is there). Write does both on the calling thread (the tests, the save screen); WriteInBackground
        // does the commit on a worker thread (autosave, quicksave), so the frame only pays for the serialize. At most
        // one save is in flight: every other save operation (a second save, a load, the save list, the next autosave
        // slot, quitting) first waits for it (WaitPending), so nothing ever sees a half-written file. The bytes are the
        // same either way (the -savecheck compares them).

        /// <summary>A save being written on a worker thread. Read <see cref="Error"/> and <see cref="DiskMs"/> once <see cref="Done"/>.</summary>
        public sealed class PendingSave
        {
            public string Path, Kind;
            /// <summary>Main-thread ms spent on the request (serialize), and ms from the request until the file was in place.</summary>
            public double MainMs, DiskMs;
            /// <summary>Why the write failed (null: it is in place). Only meaningful once Done.</summary>
            public Exception Error;
            internal System.Threading.Tasks.Task Task;
            public bool Done => Task == null || Task.IsCompleted;
        }

        static PendingSave _pending;
        static readonly object _gate = new();
        // One header and one body buffer, reused by every save (only one save is ever being made or written).
        static readonly MemoryStream _head = new(256), _body = new(1024 * 1024);
        static SaveWriter _headW, _bodyW;
        static readonly byte[] MagicBytes = System.Text.Encoding.ASCII.GetBytes(Magic);

        /// <summary>
        /// Waits until the save in flight (if any) is on disk or has failed. Called by every save operation, and when the
        /// game quits (Application.quitting) so the last autosave or quicksave is never lost. Its result stays with the
        /// <see cref="PendingSave"/> for its owner to report.
        /// </summary>
        public static void WaitPending()
        {
            var p = _pending;
            if (p == null || p.Done) return;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            p.Task.Wait();
            Debug.Log($"[Save] waited {sw.Elapsed.TotalMilliseconds:F1} ms for the {p.Kind} save to {p.Path} to finish");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void HookQuit()
        {
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting()
        {
            WaitPending();
            var p = _pending;
            if (p != null && p.Error != null) Debug.LogWarning($"[Save] the {p.Kind} save to {p.Path} failed while quitting: {p.Error}");
        }

        // Measurement (docs/pass2/perf.md): where the last save's time went, per part, with the collections seen in each.
        // Parts 0-5 are the serialize (main thread), 6-9 the commit (the worker thread for a background save).
        static readonly System.Diagnostics.Stopwatch _partSw = new();
        static readonly string[] PartNames = { "header", "map", "sim", "auto", "water", "blocks", "open", "compress", "close", "replace" };
        const int FirstCommitPart = 6;
        static readonly double[] _partMs = new double[PartNames.Length];
        static readonly int[] _partGc = new int[PartNames.Length];
        static double _partLast; static int _partGcLast;
        static void PartsStart() { _partSw.Restart(); _partLast = 0; _partGcLast = GC.CollectionCount(0); Array.Clear(_partMs, 0, _partMs.Length); Array.Clear(_partGc, 0, _partGc.Length); }
        static void PartsResume() { _partLast = _partSw.Elapsed.TotalMilliseconds; _partGcLast = GC.CollectionCount(0); }   // skip the wait for a worker
        static void Part(int i)
        {
            double t = _partSw.Elapsed.TotalMilliseconds; int gc = GC.CollectionCount(0);
            _partMs[i] += t - _partLast; _partGc[i] += gc - _partGcLast; _partLast = t; _partGcLast = gc;
        }
        /// <summary>The last save's parts: "name ms (collections)", serialize and commit apart, for the log (measurement only).</summary>
        public static string LastParts
        {
            get
            {
                var sb = new System.Text.StringBuilder();
                double total = 0; int gcs = 0;
                for (int i = 0; i < PartNames.Length; i++)
                {
                    if (i == 0) sb.Append("serialize: ");
                    if (i == FirstCommitPart) sb.Append($"= {total:F1} ms; commit: ");
                    total += _partMs[i]; gcs += _partGc[i];
                    sb.Append($"{PartNames[i]} {_partMs[i]:F1}{(_partGc[i] > 0 ? $" ({_partGc[i]} gc)" : "")}, ");
                }
                sb.Append($"total {total:F1} ms, {gcs} gc");
                return sb.ToString();
            }
        }

        /// <summary>Writes a save on the calling thread (after any save in flight has finished). Throws if it cannot.</summary>
        public static void Write(string path, Header h, Settlement s, AutoPlayer auto, WaterSim water, byte[] game, byte[] test)
        {
            var (head, body) = Serialize(h, s, auto, water, game, test);
            Commit(path, head, body);
        }

        /// <summary>
        /// Serializes the game now (on this thread, the main one) and writes the file on a worker thread. Returns at once
        /// with the save in flight; a failure (disk full, file held) is in its <see cref="PendingSave.Error"/> when it is
        /// Done. A serialize failure throws here, as <see cref="Write"/> does.
        /// </summary>
        public static PendingSave WriteInBackground(string path, string kind, Header h, Settlement s, AutoPlayer auto, WaterSim water, byte[] game, byte[] test)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var (head, body) = Serialize(h, s, auto, water, game, test);
            var p = new PendingSave { Path = path, Kind = kind };
            p.Task = System.Threading.Tasks.Task.Run(() =>
            {
                try { Commit(path, head, body); }
                catch (Exception e) { p.Error = e; }
                p.DiskMs = sw.Elapsed.TotalMilliseconds;
            });
            _pending = p;
            p.MainMs = sw.Elapsed.TotalMilliseconds;
            return p;
        }

        /// <summary>
        /// Serializes the game into the save buffers and throws the result away, starts one empty worker task and looks
        /// at the autosave slots: no file is written. The first autosave of a run spent ~10 ms more in its frame than
        /// later saves (the save code compiled on first use, the buffer grown, the thread pool started, the slot lookup);
        /// the game calls this once, in a frame that is long anyway (the first frame after a load or the campfire's).
        /// </summary>
        public static void Warm(Settlement s, AutoPlayer auto, WaterSim water, byte[] game)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Serialize(HeaderFor(s, "", "warm"), s, auto, water, game, null);
            System.Threading.Tasks.Task.Run(() => { }).Wait();   // the thread pool's first task cost ~3 ms in the first autosave's frame
            NextAutosave();                                         // as did the first look at the autosave slots (~2.5 ms)
            Debug.Log($"[Save] save code warmed up in {sw.Elapsed.TotalMilliseconds:F1} ms (nothing written)");
        }

        /// <summary>The header and the uncompressed body, as the file will hold them (the body is gzipped by Commit).</summary>
        static (MemoryStream head, MemoryStream body) Serialize(Header h, Settlement s, AutoPlayer auto, WaterSim water, byte[] game, byte[] test)
        {
            WaitPending();                  // the buffers below belong to the save in flight until it is on disk
            PartsStart();
            var head = _head; head.SetLength(0);
            var w = _headW ??= new SaveWriter(head);
            w.W.Write(MagicBytes);
            w.Int(Version);
            w.Str(h.Name); w.Str(h.Kind); w.Int(h.Seed); w.Int(h.Day); w.Int(h.Year); w.Str(h.Season); w.Str(h.Age);
            w.Int(h.Population); w.Int(h.Difficulty); w.Long(h.SavedUtcTicks);
            w.W.Flush();
            // The body is written into memory first and compressed in one call: written straight into the GZipStream it
            // made ~200k tiny deflate writes (30-44 ms and ~10 collections a save). The bytes saved mean the same. The
            // buffer is kept from one save to the next (it grew 256 KB -> 512 KB -> 1 MB, new, in every save).
            var body = _body; body.SetLength(0);
            var b = _bodyW ??= new SaveWriter(body);
            b.Int(s.Map.W);
            b.Int(MapGenHash(s.Data.Balance));
            var goods = new List<string>(s.ResCount); foreach (var g in s.Data.Resources) goods.Add(g.id);
            b.Strs(goods);
            Part(0);
            s.Map.Write(b);
            Part(1);
            s.Write(b);
            Part(2);
            b.Bool(auto != null);
            auto?.Write(b);
            Part(3);
            if (water != null) b.Floats(water.Depth.AsReadOnlySpan());     // straight from the water sim (was a 64 KB copy)
            else b.Floats((float[])null);
            Part(4);
            b.Block(game);
            b.Block(test);
            b.Str("end");
            b.W.Flush();
            Part(5);
            return (head, body);
        }

        /// <summary>Compresses the body and puts the file in place. Touches only the two streams and the file system (any thread).</summary>
        static void Commit(string path, MemoryStream head, MemoryStream body)
        {
            lock (_gate)
            {
                PartsResume();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                using (var fs = File.Create(tmp))
                {
                    Part(6);
                    fs.Write(head.GetBuffer(), 0, (int)head.Length);
                    using var gz = new GZipStream(fs, System.IO.Compression.CompressionLevel.Fastest, true);
                    gz.Write(body.GetBuffer(), 0, (int)body.Length);
                    gz.Dispose();
                    Part(7);
                }
                Part(8);
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
                Part(9);
            }
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
            WaitPending();
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
            WaitPending();
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
            WaitPending();                      // never list (or tidy away as stale) a save still being written
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
            WaitPending();
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
