using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// -autotest -dismantlecheck -seed N [-dismantleyears Y] [-outdir DIR]: dismantling at work in a real settlement.
    /// The auto-player grows one for Y years (default 3) and keeps playing; then the check marks buildings the way a
    /// player would and lets the builders and haulers work, checking each rule: a marked building is not gone at once,
    /// builders take it apart, the share of its materials (rounded down) lies on its plot and is carried to a store, the
    /// ground is free afterwards; cancelling; a store that holds goods; a home people live in; a site (all that was
    /// delivered comes back), with and without work done on it; the campfire; and a save round trip while a building is
    /// half taken apart and while a pile lies waiting. Writes dismantle-check.md; exit 0 = every rule held.
    /// </summary>
    public sealed class DismantleCheck
    {
        readonly GameData D;
        readonly Settlement S;
        readonly AutoPlayer A;
        readonly float _hour;
        readonly StringBuilder _log = new();
        bool _ok = true;

        DismantleCheck(GameData data, Settlement s, AutoPlayer a) { D = data; S = s; A = a; _hour = data.Balance.dayLengthSeconds / 24f; }

        public static bool Run(GameData data, GameMap map, int seed, string outDir, int years = 3)
        {
            var s = new Settlement(data, map, seed);
            var a = new AutoPlayer(s);
            a.Think();
            var c = new DismantleCheck(data, s, a);
            try { c.All(outDir, Math.Max(1, years)); }
            catch (Exception e) { c.Fail("exception", e.ToString()); }
            string text = $"# Dismantle check, seed {seed}: {(c._ok ? "PASS" : "FAIL")}\n\n{c._log}";
            Debug.Log("[DismantleCheck] " + text.Replace("\n", "\n[DismantleCheck] "));
            try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "dismantle-check.md"), text); } catch (Exception e) { Debug.Log($"[DismantleCheck] report not written: {e.Message}"); }
            return c._ok;
        }

        void Note(string s) => _log.Append("- ").Append(s).Append('\n');
        void Pass(string what, string detail) => Note($"PASS {what}: {detail}");
        void Fail(string what, string detail) { _ok = false; Note($"**FAIL {what}**: {detail}"); }
        void Skip(string what, string detail) => Note($"skipped {what}: {detail}");
        void Check(bool ok, string what, string detail) { if (ok) Pass(what, detail); else Fail(what, detail); }

        /// <summary>Plays on hour by hour (the auto-player keeps playing) until <paramref name="done"/> or for at most so many days.</summary>
        bool Until(Func<bool> done, float maxDays, Action each = null)
        {
            int hours = (int)(maxDays * 24);
            for (int h = 0; h < hours && S.Population > 0; h++)
            {
                if (done()) return true;
                if (each == null) S.Tick(_hour);
                else
                {
                    // Watched step by step, so a pile is seen the moment it is made (within the hour builders and
                    // haulers are already taking from it).
                    for (float t = 0; t < _hour - 1e-3f; t += Settlement.Step) { S.Tick(Settlement.Step); each(); }
                }
                A.Think();
            }
            return done();
        }

        string Name(Building b) => $"{b.Def.name} #{b.Id}";
        string Goods(float[] a) { var p = new List<string>(); for (int r = 0; r < a.Length; r++) if (a[r] >= 0.005f) p.Add($"{a[r]:0.##} {D.Resources[r].id}"); return p.Count == 0 ? "nothing" : string.Join(", ", p); }
        float[] Expected(Building b) { var e = new float[S.ResCount]; foreach (var c in b.Def.cost) e[S.ResIndex(c.id)] += Mathf.Floor(c.amount * D.Balance.dismantleRefund + 1e-3f); return e; }
        bool AtLeast(float[] have, float[] want) { for (int r = 0; r < want.Length; r++) if (have[r] < want[r] - 0.01f) return false; return true; }
        bool GroundFree(Building b)
        {
            for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                    if (S.BuildingAtCell(new int2(x, y)) != null) return false;
            return true;
        }
        /// <summary>
        /// A marked building nobody took apart. With no builder to be had (every worker on a High-priority job, the
        /// auto-player's way in a struggling town: High jobs come before builders by design, for sites too) the rule
        /// was not tested, not broken; the mark is taken off again.
        /// </summary>
        void Standing(string what, Building b, int days)
        {
            bool nobody = !S.Citizens.Any(c => c.IsBuilder) && !S.Citizens.Any(c => !c.Gone && c.Job == null && !c.IsHauler && c.Age >= D.Balance.adultAge && !c.Sick);
            string detail = $"{Name(b)} still standing after {days} days ({b.Work:P0} left); {Why(b)}";
            if (nobody && b.Work > 0.99f) Note($"blocked {what}: no builder could be had, every worker was on a High-priority job ({detail})");
            else Fail(what, detail);
            S.CancelDismantle(b);
        }

        /// <summary>Why a pile is still there: the stores' room for its goods, and who could carry.</summary>
        string Why(Building b)
        {
            var sb = new StringBuilder();
            sb.Append($"storage {S.StorageUsed:0}/{S.StorageCapacity}; ");
            for (int r = 0; r < S.ResCount; r++)
            {
                if (b.Out[r] < 0.05f) continue;
                float room = 0; int takers = 0;
                foreach (var s in S.Buildings) if (s.Active && s.Store != null && !s.Flooded && S.Accepts(s, r)) { takers++; room = Math.Max(room, S.Capacity(s) - s.Stored - s.HaulReserved); }
                sb.Append($"{D.Resources[r].id}: {takers} stores take it, most unpromised room {room:0.#}; ");
            }
            sb.Append($"pile reserved {b.HaulReserved:0.#}; builders {S.Citizens.Count(c => c.IsBuilder)} of {S.BuildersWanted} wanted, haulers {S.Citizens.Count(c => c.IsHauler)}, adults without a job {S.Citizens.Count(c => !c.Gone && c.Job == null && !c.IsBuilder && !c.IsHauler && c.Age >= D.Balance.adultAge)}; ");
            sb.Append("builder tasks: " + string.Join(" ", S.Citizens.Where(c => c.IsBuilder).Select(c => $"{c.Task}{(c.TaskB2 != null ? "@" + c.TaskB2.Def.id + (c.TaskB2.Dismantling ? "(dismantling)" : "") : "")}{(c.TaskB != null ? "<" + c.TaskB.Def.id : "")}")));
            sb.Append($"; sites {S.Buildings.Count(x => x.UnderWork)}, marked {S.Buildings.Count(x => x.Dismantling)}, piles {S.Buildings.Count(x => x.IsPile)}; work time {S.IsWorkTime}");
            return sb.ToString();
        }

        /// <summary>Buildings the town can best spare first: not a food producer, then the kind it has most of.</summary>
        IEnumerable<Building> Spare(IEnumerable<Building> list) =>
            list.OrderBy(x => x.Def.outputs.Any(o => D.Resource(o.id)?.isFood == true) ? 1 : 0)
                .ThenByDescending(x => S.Buildings.Count(y => y.Active && y.Def == x.Def)).ThenBy(x => x.Id);

        double LostTotal() { double t = 0; foreach (var v in S.Lost) t += v; return t; }
        byte[] Bytes(Settlement s) { var ms = new MemoryStream(); var w = new SaveWriter(ms); s.Write(w); w.W.Flush(); return ms.ToArray(); }

        void All(string outDir, int years)
        {
            Until(() => S.Day >= years * S.DaysPerYear, years * S.DaysPerYear + 1);
            Note($"day {S.Day}: {S.Population} people, {S.Buildings.Count(b => b.Active)} buildings, builders wanted {S.BuildersWanted}; refund {D.Balance.dismantleRefund:P0} rounded down, work {D.Balance.dismantleWork:P0} of building");
            if (S.Population == 0) { Fail("setup", "nobody left"); return; }
            if (S.BuildersWanted < 2) S.SetBuilders(2);

            Campfire();
            Workplace();
            Cancel();
            Store();
            Home();
            Site(false);
            Site(true);
            SaveRoundTrip(outDir);
            // Afterwards the settlement plays on as before.
            int pop = S.Population;
            Until(() => false, 8);
            Check(S.Population > 0, "afterwards", $"8 more days: {pop} -> {S.Population} people, {S.Buildings.Count(b => b.Active)} buildings, {S.Buildings.Count(b => b.IsPile)} piles left, {S.Buildings.Count(b => b.Dismantling)} still marked");
        }

        void Campfire()
        {
            bool refused = !S.Dismantle(S.TownCenter, out string why);
            Check(refused && S.TownCenter.Active && !S.TownCenter.Dismantling, "campfire", $"refused ({why})");
        }

        /// <summary>The whole course: marked, taken apart over time, the pile, the hauling, the free ground.</summary>
        void Workplace()
        {
            var b = Spare(S.Buildings.Where(x => x.Active && !x.Dismantling && x != S.TownCenter && x.Def.IsWorkplace && !x.Def.crop && x.Store == null && x.Def.buildWork > 0 && Expected(x).Sum() >= 1 && x.Workers > 0)).FirstOrDefault();
            if (b == null) { Skip("workplace", "no workplace that gives materials back"); return; }
            var want = Expected(b);
            int day0 = S.Day; float t0 = S.Time;
            bool marked = S.Dismantle(b, out string why); S.SetPriority(b, Priority.High);
            Check(marked && b.Active && b.Dismantling && b.Work > 0.99f, "mark", $"{Name(b)} ({b.Workers} workers) marked, still standing; should give back {Goods(want)} of {string.Join(", ", b.Def.cost.Select(c => $"{c.amount:0.##} {c.id}"))} {why}");
            Check(!S.Dismantle(b, out _), "mark twice", "a second mark is refused");
            Until(() => false, 0.2f);
            Check(b.Workers == 0 && !S.Citizens.Any(c => c.Job == b), "workers leave", $"{b.Workers} workers a few hours after the mark");
            float[] pile = null; float downAt = -1;
            bool down = Until(() => !b.Active, 12, () => { if (b.IsPile && pile == null) { pile = (float[])b.Out.Clone(); downAt = S.Time; } });
            if (!down) { Standing("taken apart", b, 12); return; }
            if (b.IsPile && pile == null) { pile = (float[])b.Out.Clone(); downAt = S.Time; }
            float days = (downAt - t0) / D.Balance.dayLengthSeconds;
            Check(days > 0.05f, "takes time", $"down after {days:0.##} days (building it takes {b.Def.buildWork:0.#} units of work)");
            if (pile == null) { Fail("pile", "no pile was left"); return; }
            Check(AtLeast(pile, want), "materials back", $"the pile held {Goods(pile)}; the materials due were {Goods(want)}");
            bool cleared = Until(() => !b.OnMap, 12);
            Check(cleared && GroundFree(b), "hauled, ground free", cleared ? $"pile carried off by day {S.Day} (marked on day {day0}); the plot is free" : $"the pile still holds {Goods(b.Out)} after 12 days; {Why(b)}");
        }

        void Cancel()
        {
            var b = Spare(S.Buildings.Where(x => x.Active && !x.Dismantling && x != S.TownCenter && x.Def.IsWorkplace && !x.Def.crop && x.Def.buildWork >= 2 && x.Workers > 0)).FirstOrDefault();
            if (b == null) { Skip("cancel", "no staffed workplace"); return; }
            S.Dismantle(b); S.SetPriority(b, Priority.High);
            bool started = Until(() => b.Dismantling && b.Work < 0.95f || !b.Active, 6);
            if (!b.Active) { Skip("cancel", $"{Name(b)} was down before the cancel"); return; }
            float left = b.Work;
            bool cancelled = S.CancelDismantle(b);
            Check(started && cancelled && b.Active && !b.Dismantling && b.Work == 1f, "cancel", $"{Name(b)} cancelled with {left:P0} still standing: whole again");
            Until(() => b.Workers > 0, 3);
            Check(b.Active && b.Workers > 0, "cancel: back to work", $"{b.Workers} workers afterwards");
            Check(!S.CancelDismantle(b), "cancel twice", "refused when not marked");
        }

        void Store()
        {
            var b = S.Buildings.Where(x => x.Active && !x.Dismantling && x != S.TownCenter && x.Store != null).OrderByDescending(x => x.Stored).ThenBy(x => x.Id).FirstOrDefault();
            if (b == null || b.Stored < 3) { Skip("store", "no store with goods besides the campfire"); return; }
            float held = b.Stored; var want = Expected(b);
            double lost0 = LostTotal();
            int lossEvents0 = S.Events.Count(e => e.Text.Contains("were lost"));
            S.Dismantle(b); S.SetPriority(b, Priority.High);
            float most = b.Stored; bool grew = false; float[] pile = null;
            bool down = Until(() => !b.Active, 14, () =>
            {
                if (b.Active) { if (b.Stored > most + 0.01f) grew = true; most = Math.Min(most, b.Stored); }
                if (b.IsPile && pile == null) pile = (float[])b.Out.Clone();
            });
            if (!down) { Standing("store", b, 14); return; }
            if (b.IsPile && pile == null) pile = (float[])b.Out.Clone();
            Check(!grew, "store takes nothing", $"{Name(b)} held {held:0.#} when marked; nothing was brought in while it was marked (lowest {most:0.#})");
            Check(pile == null ? want.Sum() < 0.5f : AtLeast(pile, want), "store: goods kept", pile == null ? "nothing was left to pile" : $"the pile held {Goods(pile)} (materials due {Goods(want)}; the rest is what the store still held)");
            Until(() => !b.OnMap, 14);
            Check(S.Events.Count(e => e.Text.Contains("were lost")) == lossEvents0, "store: nothing thrown away", b.OnMap ? $"the pile still holds {Goods(b.Out)} (no room elsewhere?); none was lost" : $"pile carried off; lost goods counter moved {LostTotal() - lost0:0.##} in that time (spoilage)");
        }

        void Home()
        {
            var b = S.Buildings.Where(x => x.Active && !x.Dismantling && x.Def.housing > 0 && x.Residents > 0 && x.Def.buildWork > 0).OrderByDescending(x => x.Residents).ThenBy(x => x.Id).FirstOrDefault();
            if (b == null) { Skip("home", "no lived-in home"); return; }
            int residents = b.Residents;
            S.Dismantle(b); S.SetPriority(b, Priority.High);
            Until(() => false, 0.15f);
            int still = S.Citizens.Count(c => !c.Gone && c.Home == b);
            Check(b.Active && still > 0, "home: people stay", $"{Name(b)}: {residents} lived there, {still} still do a few hours after the mark");
            int peak = still; bool movedIn = false;
            bool down = Until(() => !b.Active, 12, () => { if (b.Active) { int n = S.Citizens.Count(c => !c.Gone && c.Home == b); if (n > peak) movedIn = true; } });
            if (!down) { Standing("home", b, 12); return; }
            Check(!movedIn, "home: nobody moves in", "no new resident while it was marked");
            Until(() => false, 0.1f);
            Check(!S.Citizens.Any(c => c.Home == b), "home: residents out", $"nobody lives in it once it is down; {S.Homeless} of {S.Population} without a home now (housing {S.HousingCapacity})");
        }

        /// <summary>A site gives back all that was delivered: at once if nobody has built on it yet, else after the work is taken down.</summary>
        void Site(bool withWork)
        {
            string what = withWork ? "site with work" : "site";
            var def = S.UnlockedBuildings.Where(d => !d.path && !d.unique && !d.crop && !d.HasNearby && string.IsNullOrEmpty(d.onDeposit) && d.cost.Length > 0 && d.buildWork >= 2).OrderByDescending(d => d.cost.Sum(c => c.amount)).FirstOrDefault();
            if (def == null) { Skip(what, "nothing suitable to place"); return; }
            Building b = null;
            var tc = (int2)S.TownCenter.Center;
            for (int ring = 4; ring < 30 && b == null; ring++)
                for (int dy = -ring; dy <= ring && b == null; dy++)
                    for (int dx = -ring; dx <= ring && b == null; dx++)
                        if (math.max(math.abs(dx), math.abs(dy)) == ring && S.CanPlace(def, tc + new int2(dx, dy))) b = S.Place(def, tc + new int2(dx, dy));
            if (b == null) { Skip(what, $"no room for a {def.name}"); return; }
            S.SetPriority(b, Priority.High);
            bool ready = withWork ? Until(() => b.Active || b.Work > 0.15f, 10) : Until(() => b.Active || b.Delivered.Sum() >= 1f, 10);
            if (b.Active || !ready || (!withWork && b.Work > 0)) { Skip(what, $"the {def.name} site was {(b.Active ? "finished" : ready ? "already being built" : "not supplied")} before it could be marked (delivered {b.Delivered.Sum():0.#}, work {b.Work:P0})"); if (b.Alive && !b.Active) S.Dismantle(b); S.SetPriority(b, Priority.High); return; }
            var delivered = new float[S.ResCount];
            for (int i = 0; i < def.cost.Length; i++) delivered[S.ResIndex(def.cost[i].id)] += b.Delivered[i];
            float work = b.Work;
            S.Dismantle(b); S.SetPriority(b, Priority.High);
            float[] pile = b.IsPile ? (float[])b.Out.Clone() : null;
            if (!withWork) Check(b.IsPile, "site: at once", $"{def.name} site with nothing built comes apart at once");
            else
            {
                Check(b.IsSite && b.Dismantling, "site with work: a job", $"{def.name} site with {work:P0} built waits for builders");
                bool down = Until(() => !b.IsSite, 10, () => { if (b.IsPile && pile == null) pile = (float[])b.Out.Clone(); });
                if (b.IsPile && pile == null) pile = (float[])b.Out.Clone();
                if (!down) { Standing(what, b, 10); return; }
            }
            bool all = pile != null && AtLeast(pile, delivered) && pile.Sum() <= delivered.Sum() + 0.02f;
            Check(all, what + ": all delivered comes back", $"delivered {Goods(delivered)}; the pile held {(pile == null ? "nothing" : Goods(pile))}");
            bool cleared = Until(() => !b.OnMap, 12);
            Check(cleared && GroundFree(b), what + ": hauled", cleared ? "pile carried off, plot free" : $"the pile still holds {Goods(b.Out)}; {Why(b)}");
        }

        /// <summary>Saved while one building is half taken apart and another's pile lies waiting: both load exactly as they were.</summary>
        void SaveRoundTrip(string outDir)
        {
            var picks = Spare(S.Buildings.Where(x => x.Active && !x.Dismantling && x != S.TownCenter && x.Def.buildWork >= 2 && Expected(x).Sum() >= 1 && x.Store == null && x.Def.housing == 0)).Take(2).ToList();
            if (picks.Count < 2) { Skip("save", "not two buildings left to dismantle"); return; }
            Building first = picks[0], second = picks[1];
            S.Dismantle(first); S.SetPriority(first, Priority.High);
            if (!Until(() => first.IsPile, 12)) { Skip("save", $"{Name(first)} left no pile in 12 days"); return; }
            S.Dismantle(second); S.SetPriority(second, Priority.High);
            Until(() => second.Dismantling && second.Work < 0.8f || !second.Active, 8);
            if (!second.Active || !second.Dismantling) { Skip("save", $"{Name(second)} was down before the save"); return; }
            if (!first.IsPile) Note($"(the pile of {Name(first)} was carried off before the save; only the half-dismantled building is checked)");
            string path = Path.Combine(outDir, "dismantle-check.sav");
            Directory.CreateDirectory(outDir);
            var before = Bytes(S);
            SaveGame.Write(path, SaveGame.HeaderFor(S, "dismantle", "test"), S, A, null, null, null);
            var l = SaveGame.Read(path, D, out string err);
            if (l == null) { Fail("save", $"load refused: {err}"); return; }
            try
            {
                var after = Bytes(l.Sim);
                var l1 = l.Sim.Buildings[first.Id]; var l2 = l.Sim.Buildings[second.Id];
                bool same = before.AsSpan().SequenceEqual(after);
                bool kept = l2.Dismantling && l2.Active && l2.Work == second.Work && l1.State == first.State && l1.OutTotal == first.OutTotal;
                Check(same && kept, "save", $"saved with {Name(second)} {second.Work:P0} standing and {(first.IsPile ? $"a pile of {first.OutTotal:0.#}" : "no pile")}: loaded the same ({before.Length} bytes identical {same}, marks kept {kept})");
                // Both go on: the loaded game finishes the job like the saved one.
                var auto = l.Auto ?? new AutoPlayer(l.Sim);
                for (int h = 0; h < 12 * 24 && l.Sim.Buildings[second.Id].Active; h++) { l.Sim.Tick(_hour); auto.Think(); }
                bool downA = Until(() => !second.Active, 12);
                Check(downA && !l.Sim.Buildings[second.Id].Active, "save: work goes on after loading", $"taken apart in the saved game {downA} and in the loaded one {!l.Sim.Buildings[second.Id].Active}");
            }
            finally { l.Map.Dispose(); try { File.Delete(path); } catch { } }
        }
    }
}
