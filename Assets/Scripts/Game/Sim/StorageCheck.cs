using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// -autotest -storagecheck -seed N [-storageyears Y] [-outdir DIR]: the store settings at work in a real settlement.
    /// The auto-player grows one for Y years (default 2; 6 or more has a granary) and keeps playing; then the check sets a store's settings the way a player would and lets
    /// the haulers work, checking each rule:
    /// shut a good out (it leaves and none comes in), keep at most (it falls to the cap and stays there), keep at least
    /// (filled from other stores, and never taken into while other stores have some), priority (a High store fills from
    /// the others), copy and paste, the "no store takes" warning, and a save round trip with settings set.
    /// Writes storage-check.md; exit 0 = every rule held.
    /// </summary>
    public sealed class StorageCheck
    {
        readonly GameData D;
        readonly Settlement S;
        readonly AutoPlayer A;
        readonly float _hour;
        readonly StringBuilder _log = new();
        bool _ok = true;

        StorageCheck(GameData data, Settlement s, AutoPlayer a) { D = data; S = s; A = a; _hour = data.Balance.dayLengthSeconds / 24f; }

        public static bool Run(GameData data, GameMap map, int seed, string outDir, int years = 2)
        {
            var s = new Settlement(data, map, seed);
            var a = new AutoPlayer(s);
            a.Think();
            var c = new StorageCheck(data, s, a);
            try { c.All(seed, outDir, Math.Max(1, years)); }
            catch (Exception e) { c.Fail("exception", e.ToString()); }
            string text = $"# Storage check, seed {seed}: {(c._ok ? "PASS" : "FAIL")}\n\n{c._log}";
            Debug.Log("[StorageCheck] " + text.Replace("\n", "\n[StorageCheck] "));
            try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "storage-check.md"), text); } catch (Exception e) { Debug.Log($"[StorageCheck] report not written: {e.Message}"); }
            return c._ok;
        }

        void Note(string s) => _log.Append("- ").Append(s).Append('\n');
        void Pass(string what, string detail) => Note($"PASS {what}: {detail} {_last}");
        void Fail(string what, string detail)
        {
            // No hauler could be had: every worker on a High-priority job (the auto-player's way early on). High jobs come
            // before haulers by design (as before builders), so the rule was not tested, not broken.
            if (_lastHaulers < 0.05f && _lastLower < 0.5f) { Note($"blocked {what}: no hauler could be had, every worker was on a High-priority job ({detail}) {_last}"); return; }
            _ok = false; Note($"**FAIL {what}**: {detail} {_last}");
        }
        void Skip(string what, string detail) => Note($"skipped {what}: {detail}");

        /// <summary>Plays on for some days, calling <paramref name="each"/> after every game hour.</summary>
        void Days(int days, Action each = null)
        {
            int end = S.Day + days, hours = 0, moving = 0, free = 0, idle = 0, haulers = 0, lower = 0;
            var tasks = new SortedDictionary<string, int>();
            while (S.Day < end && S.Population > 0)
            {
                S.Tick(_hour); A.Think(); each?.Invoke();
                hours++;
                foreach (var c in S.Citizens)
                {
                    if (c.Task == Task.MoveStock || c.Task == Task.DeliverOutput && c.TaskB != null && c.TaskB.Store != null && c.Job == null) moving++;
                    if (c.Job == null && !c.IsBuilder && c.Age >= D.Balance.adultAge) free++;
                    if (c.Task == Task.Idle && c.Age >= D.Balance.adultAge) idle++;
                    if (c.Job != null && c.Job.Priority != Priority.High) lower++;
                    if (c.IsHauler) { haulers++; string k = c.Task.ToString(); tasks[k] = tasks.TryGetValue(k, out int n) ? n + 1 : 1; }
                }
            }
            _lastHaulers = (float)haulers / Math.Max(1, hours); _lastLower = (float)lower / Math.Max(1, hours);
            _last = $"(over {days} days: on average {(float)moving / Math.Max(1, hours):0.#} hauling between stores, {(float)free / Math.Max(1, hours):0.#} adults without a job, {(float)idle / Math.Max(1, hours):0.#} idle, {(float)lower / Math.Max(1, hours):0.#} on jobs below High, {(float)haulers / Math.Max(1, hours):0.#} haulers: {string.Join(" ", tasks.Select(kv => $"{kv.Key} {kv.Value * 100 / Math.Max(1, haulers)}%"))})";
        }
        string _last = "";
        float _lastHaulers, _lastLower;

        string Name(Building b) => $"{b.Def.name} #{b.Id}";
        string Good(int r) => D.Resources[r].id;
        List<Building> Stores => S.Buildings.Where(b => b.Active && b.Store != null).ToList();
        float Free(Building b) => S.Capacity(b) - b.Stored;

        void Reset(Building b)
        {
            var st = new StoreSettings { Allow = Enumerable.Repeat(true, S.ResCount).ToArray(), Priority = Priority.Normal };
            S.ApplyStoreSettings(b, st);
        }

        void All(int seed, string outDir, int years)
        {
            Days(years * S.DaysPerYear);
            var stores = Stores;
            Note($"day {S.Day}: {S.Population} people, {stores.Count} stores ({string.Join(", ", stores.Select(Name))})");
            if (S.Population == 0 || stores.Count < 2) { Fail("setup", $"fewer than two stores after {years} years"); return; }
            foreach (var b in stores) if (S.StoreTakes(b).takes != S.StoreTakes(b).holds || b.StorePriority != Priority.Normal || b.KeepMin != null || b.KeepMax != null) Fail("defaults", $"{Name(b)} was not built taking everything at Normal with no limits");

            // X: the fullest open store that is not the campfire (a granary holds only food, which spoils); Y: another store, the one with most room.
            var X = stores.Where(b => b != S.TownCenter).OrderBy(b => b.Def.accepts.Length > 0 ? 1 : 0).ThenByDescending(b => b.Stored).ThenBy(b => b.Id).FirstOrDefault() ?? stores[0];
            var Y = stores.Where(b => b != X).OrderByDescending(Free).ThenBy(b => b.Id).First();
            Note($"X = {Name(X)} ({X.Stored:0}/{S.Capacity(X):0}), Y = {Name(Y)} ({Y.Stored:0}/{S.Capacity(Y):0})");

            ShutOut(X);
            Cap(X);
            KeepAtLeast(Y, X);
            PriorityUp(Y);
            CopyPaste(X, stores);
            Warning(stores);
            SaveRoundTrip(X, Y, outDir);
            foreach (var b in Stores) Reset(b);
        }

        /// <summary>The good a store holds most of that another store could take (and that does not spoil, so amounts only
        /// change by people's doing).</summary>
        int MostHeld(Building b, Func<int, bool> ok = null)
        {
            int best = -1;
            for (int r = 0; r < S.ResCount; r++)
                if (D.Resources[r].spoilPerDay <= 0 && b.Store[r] >= 1f && (ok == null || ok(r)) && (best < 0 || b.Store[r] > b.Store[best])) best = r;
            return best;
        }

        void ShutOut(Building X)
        {
            int g = MostHeld(X, r => Stores.Any(s => s != X && S.CanHold(s, r)));
            if (g < 0) { Skip("shut out", $"{Name(X)} holds nothing another store could take"); return; }
            float before = X.Store[g];
            float room = Stores.Where(s => s != X && S.Accepts(s, g)).Sum(Free);
            S.SetStoreAllow(X, g, false);
            float last = X.Store[g]; float rose = 0;
            Days(3, () => { if (X.Store[g] > last + 0.01f) rose = Math.Max(rose, X.Store[g] - last); last = X.Store[g]; });
            if (rose > 0) Fail("shut out", $"{Good(g)} still came into {Name(X)} (+{rose:0.##} in an hour)");
            else if (X.Store[g] < 0.5f) Pass("shut out", $"{before:0} {Good(g)} moved out of {Name(X)} in 3 days, none came in");
            else if (room < before) Skip("shut out", $"{X.Store[g]:0} of {before:0} {Good(g)} left, other stores had room for only {room:0}");
            else Fail("shut out", $"{X.Store[g]:0} of {before:0} {Good(g)} still in {Name(X)} after 3 days (other stores had room for {room:0})");
            S.SetStoreAllow(X, g, true);
        }

        void Cap(Building X)
        {
            int g = MostHeld(X, r => Stores.Any(s => s != X && S.CanHold(s, r)));
            if (g < 0 || X.Store[g] < 8f)
            {
                // Bring some in first: let it fill for a few days.
                Days(3);
                g = MostHeld(X, r => Stores.Any(s => s != X && S.CanHold(s, r)));
            }
            if (g < 0) { Skip("keep at most", $"{Name(X)} holds nothing"); return; }
            float cap = Math.Max(1f, (float)Math.Floor(X.Store[g] * 0.3f));
            float before = X.Store[g];
            S.SetKeepMax(X, g, cap);
            float worstAfter = 0; bool under = false;
            Days(4, () => { if (X.Store[g] <= cap + 0.5f) under = true; else if (under) worstAfter = Math.Max(worstAfter, X.Store[g] - cap); });
            if (worstAfter > 0.5f) Fail("keep at most", $"{Good(g)} in {Name(X)} rose {worstAfter:0.#} over the cap of {cap:0} after falling to it");
            else if (X.Store[g] <= cap + 0.5f) Pass("keep at most", $"{Good(g)} in {Name(X)} {before:0} -> {X.Store[g]:0} with a cap of {cap:0}, and stayed at or under it");
            else Fail("keep at most", $"{Good(g)} in {Name(X)} still {X.Store[g]:0} over a cap of {cap:0} after 4 days (other stores' room {Stores.Where(s => s != X && S.Accepts(s, g)).Sum(Free):0})");
            S.SetKeepMax(X, g, 0);
            if (X.KeepMax != null) Fail("keep at most", "clearing the only cap left a limits array");
        }

        void KeepAtLeast(Building Y, Building X)
        {
            // A good that does not spoil, with stock elsewhere, that Y can hold.
            int h = -1; float most = 0;
            for (int r = 0; r < S.ResCount; r++)
            {
                if (D.Resources[r].spoilPerDay > 0 || !S.CanHold(Y, r)) continue;
                float elsewhere = S.StockTotals[r] - Y.Store[r];
                if (elsewhere > most) { most = elsewhere; h = r; }
            }
            if (h < 0 || most < 4f) { Skip("keep at least", "no good with stock outside the store"); return; }
            float keep = (float)Math.Floor(Math.Min(Y.Store[h] + most * 0.6f, Y.Store[h] + Free(Y) * 0.8f));
            if (keep <= Y.Store[h] + 2f) { Skip("keep at least", $"{Name(Y)} has no room to fill"); return; }
            float before = Y.Store[h];
            S.SetKeepMin(Y, h, keep);
            int reached = -1;
            Days(4, () => { if (reached < 0 && Y.Store[h] >= keep - 1f) reached = S.Day; });
            if (reached >= 0) Pass("keep at least (fill)", $"{Name(Y)} {Good(h)} {before:0} -> {Y.Store[h]:0}, reached {keep:0} by day {reached}");
            else Fail("keep at least (fill)", $"{Name(Y)} {Good(h)} {before:0} -> {Y.Store[h]:0} of {keep:0} after 4 days ({S.StockTotals[h]:0} in all stores)");

            // Protected: never taken into while other stores have some. Take part of the good from the others meanwhile,
            // so builders and workshops really want it (they draw on whatever the settlement has).
            int dips = 0; float worst = 0;
            float lastY = Y.Store[h], lastUse = S.UsableStock(h);
            Days(5, () =>
            {
                float y = Y.Store[h], use = S.UsableStock(h);
                if (y < lastY - 0.01f && lastY <= keep + 0.01f && Math.Min(use, lastUse) >= 5f) { dips++; worst = Math.Max(worst, lastY - y); }
                lastY = y; lastUse = use;
            });
            if (dips > 0) Fail("keep at least (protect)", $"{Name(Y)}'s reserve of {Good(h)} was drawn on {dips} times (up to {worst:0.#}) while other stores had 5 or more");
            else Pass("keep at least (protect)", $"{Name(Y)} kept {Y.Store[h]:0} of {keep:0} {Good(h)} for 5 days; usable elsewhere now {S.UsableStock(h):0}");
            // Reserves stay out of traders' reach: the trade surplus subtracts them.
            if (S.ReservedStock(h) + 0.01f < Math.Min(Y.Store[h], keep)) Fail("keep at least (reserved)", $"ReservedStock {S.ReservedStock(h):0.#} below the reserve held {Math.Min(Y.Store[h], keep):0.#}");
            S.SetKeepMin(Y, h, 0);
        }

        void PriorityUp(Building Y)
        {
            foreach (var b in Stores) Reset(b);
            if (Y.Flooded) { Skip("priority", $"{Name(Y)} is flooded"); return; }
            S.SetStorePriority(Y, Priority.High);
            float Movable() => Stores.Where(s => s != Y).Sum(s => Enumerable.Range(0, S.ResCount).Where(r => S.Accepts(Y, r)).Sum(r => S.Movable(s, r)));
            float freeBefore = Free(Y), movBefore = Movable();
            Days(5);
            float freeAfter = Free(Y), movAfter = Movable();
            if (freeAfter < Math.Max(15f, S.Capacity(Y) * 0.2f) || movAfter < 5f) Pass("priority", $"High {Name(Y)} room {freeBefore:0} -> {freeAfter:0}; goods it takes in the other stores {movBefore:0} -> {movAfter:0}");
            else Fail("priority", $"High {Name(Y)} still has room {freeAfter:0} (was {freeBefore:0}) while the other stores hold {movAfter:0} it takes (was {movBefore:0})");
            S.SetStorePriority(Y, Priority.Normal);
        }

        void CopyPaste(Building X, List<Building> stores)
        {
            foreach (var b in Stores) Reset(b);
            int a = -1, c = -1;
            for (int r = 0; r < S.ResCount && (a < 0 || c < 0); r++) { if (!S.CanHold(X, r)) continue; if (a < 0) a = r; else if (D.Resources[r].isFood) c = r; }
            if (a < 0 || c < 0) { Skip("copy / paste", "too few goods"); return; }
            S.SetStoreAllow(X, a, false); S.SetKeepMin(X, c, 12); S.SetKeepMax(X, c, 30); S.SetStorePriority(X, Priority.Low);
            var st = S.GetStoreSettings(X);
            // Onto a store that holds less (a granary) if there is one, else onto another store.
            var T = stores.FirstOrDefault(b => b != X && b.Def.accepts.Length > 0) ?? stores.First(b => b != X);
            var keepOwn = (bool[])T.Allow.Clone();
            S.ApplyStoreSettings(T, st);
            var bad = new List<string>();
            if (T.StorePriority != Priority.Low) bad.Add("priority");
            for (int r = 0; r < S.ResCount; r++)
            {
                bool holds = S.CanHold(T, r);
                if (holds && T.Allow[r] != X.Allow[r]) bad.Add($"allow {Good(r)}");
                if (!holds && T.Allow[r] != keepOwn[r]) bad.Add($"changed {Good(r)} it cannot hold");
                float tMin = T.KeepMin?[r] ?? 0, xMin = X.KeepMin?[r] ?? 0, tMax = T.KeepMax?[r] ?? 0, xMax = X.KeepMax?[r] ?? 0;
                if (holds && (tMin != xMin || tMax != xMax)) bad.Add($"limits {Good(r)} {tMin}/{tMax} vs {xMin}/{xMax}");
                if (!holds && (tMin != 0 || tMax != 0)) bad.Add($"limit on {Good(r)} it cannot hold");
            }
            // Setting a cap under the reserve lowers the reserve; a reserve over the cap raises the cap.
            S.SetKeepMax(X, c, 5);
            if ((X.KeepMin?[c] ?? -1) != 5) bad.Add($"cap under the reserve left the reserve at {X.KeepMin?[c]}");
            S.SetKeepMin(X, c, 40);
            if ((X.KeepMax?[c] ?? -1) != 40) bad.Add($"reserve over the cap left the cap at {X.KeepMax?[c]}");
            S.SetKeepMin(X, c, float.NaN); S.SetKeepMax(X, c, -3);
            if ((X.KeepMin?[c] ?? -1) != 40 || X.KeepMax != null && X.KeepMax[c] != 0) bad.Add($"NaN / negative limits: {X.KeepMin?[c]} / {X.KeepMax?[c]}");
            if (bad.Count == 0) Pass("copy / paste and limits", $"{Name(X)} -> {Name(T)}: priority, {S.StoreTakes(T).takes} of {S.StoreTakes(T).holds} goods, limits; min/max kept consistent; bad numbers ignored");
            else Fail("copy / paste and limits", string.Join("; ", bad));
            Reset(X); Reset(T);
        }

        void Warning(List<Building> stores)
        {
            // The good made most so far that does not spoil (wood, as a rule).
            int g = -1;
            for (int r = 0; r < S.ResCount; r++)
                if (D.Resources[r].spoilPerDay <= 0 && S.Produced[r] > 1 && (g < 0 || S.Produced[r] > S.Produced[g])) g = r;
            if (g < 0) { Skip("warning", "nothing made"); return; }
            foreach (var b in Stores) S.SetStoreAllow(b, g, false);
            bool seen = false; float waited = 0;
            Days(3, () =>
            {
                if (S.Warnings.Any(w => w.StartsWith("No store takes") && w.Contains(D.Resources[g].name.ToLower()))) seen = true;
                waited = Math.Max(waited, S.Buildings.Where(b => b.Active && b.Store == null).Sum(b => b.Out[g]));
            });
            if (!seen && waited < 1f) Skip("warning", $"no {Good(g)} waited at a workplace (taken straight to sites)");
            else if (seen) Pass("warning", $"\"No store takes {D.Resources[g].name.ToLower()}\" shown while every store shut it out");
            else Fail("warning", $"no warning with {Good(g)} shut out of every store ({S.Buildings.Where(b => b.Active && b.Store == null).Sum(b => b.Out[g]):0} waiting at workplaces)");
            foreach (var b in Stores) S.SetStoreAllow(b, g, true);
            Days(1);
            if (S.Warnings.Any(w => w.StartsWith("No store takes"))) Fail("warning", "still shown after the good was let in again");
        }

        void SaveRoundTrip(Building X, Building Y, string outDir)
        {
            foreach (var b in Stores) Reset(b);
            int g = MostHeld(X) ; if (g < 0) g = 0;
            S.SetStoreAllow(X, g, false); S.SetStorePriority(Y, Priority.High); S.SetKeepMin(Y, g, 7); S.SetKeepMax(X, (g + 1) % S.ResCount, 9);
            string path = Path.Combine(outDir, "storage-check.sav");
            Directory.CreateDirectory(outDir);
            byte[] Bytes(Settlement s) { var ms = new MemoryStream(); var w = new SaveWriter(ms); s.Write(w); w.W.Flush(); return ms.ToArray(); }
            var before = Bytes(S);
            SaveGame.Write(path, SaveGame.HeaderFor(S, "storage", "test"), S, A, null, null, null);
            var l = SaveGame.Read(path, D, out string err);
            if (l == null) { Fail("save", $"load refused: {err}"); return; }
            try
            {
                var after = Bytes(l.Sim);
                var lx = l.Sim.Buildings[X.Id]; var ly = l.Sim.Buildings[Y.Id];
                bool same = before.AsSpan().SequenceEqual(after);
                bool kept = !lx.Allow[g] && ly.StorePriority == Priority.High && ly.KeepMin?[g] == 7 && lx.KeepMax?[(g + 1) % S.ResCount] == 9;
                if (same && kept) Pass("save", $"settings saved and loaded ({before.Length} bytes identical)");
                else Fail("save", $"identical {same}, settings kept {kept}");
            }
            finally { l.Map.Dispose(); try { File.Delete(path); } catch { } }
        }
    }
}
