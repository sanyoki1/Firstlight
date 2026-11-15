using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Save / load: every piece of state the sim carries from one step to the next, written exactly (float bits), so a
    // loaded settlement goes on exactly as the saved one would have. Derived state that is recomputed at a fixed point
    // (research effects, door cells, cost/output indices) is rebuilt; anything refreshed only hourly or daily but read in
    // between is saved as it was. Buildings and citizens refer to each other by building index (Id == list index).
    public sealed partial class Settlement
    {
        public void Write(SaveWriter w)
        {
            // Calendar and counters.
            w.Float(Time); w.Int(Day); w.Float(DayFraction); w.Int(WintersSurvived); w.Float(Speed);
            w.Float(_acc); w.Float(_hourAcc); w.Int(_nextId); w.Bool(_jobsDirty);
            w.ULong(_rng.State);
            w.Int(BuildersWanted); w.Int(Births); w.Int(Migrants); w.Int(Leavers);
            w.Int(Deaths.Count); foreach (var kv in Deaths) { w.Str(kv.Key); w.Int(kv.Value); }
            w.Double(KnowledgeEarned); w.Float(KnowledgeBank);                  // v2: the cumulative counters are doubles
            w.Floats(StockTotals); w.Doubles(Produced); w.Doubles(Consumed);
            w.Double(WorkerSeconds); w.Double(ProductiveSeconds);
            w.Float(GrainProducedF);                                            // v2
            w.Int(ToolSwaps); w.Int(PathCount); w.Int(Upgrades); w.Int(Trades);
            w.Int(Disputes); w.Int(Thefts); w.Int(Outbreaks);
            w.Float(Unrest); w.Float(UnrestTarget); w.Float(OutbreakRisk);
            w.Bool(_needWater); w.Bool(_needGoods); w.Bool(_needFaith);

            // Research, ages, laws.
            var researched = new List<string>(); foreach (var r in Data.Research) if (_researched.Contains(r.id)) researched.Add(r.id);
            w.Strs(researched);
            w.Int(_progress.Count); foreach (var r in Data.Research) if (_progress.TryGetValue(r.id, out float p)) { w.Str(r.id); w.Float(p); }
            w.Strs(ResearchQueue);
            w.Str(Age?.id);
            w.Int(Milestones.Count); foreach (var m in Milestones) { w.Str(MilestoneKey(m.Def)); w.Bool(m.Done); w.Int(m.DoneDay); w.Int(m.Current); }   // v2: by key
            w.Int(ResearchLog.Count); foreach (var (id, day) in ResearchLog) { w.Str(id); w.Int(day); }
            w.Int(AgeLog.Count); foreach (var (id, day) in AgeLog) { w.Str(id); w.Int(day); }
            var laws = new List<string>(); foreach (var l in Data.Laws) if (_laws.Contains(l.id)) laws.Add(l.id);
            w.Strs(laws);
            w.Int(_lawChanged.Count); foreach (var kv in _lawChanged) { w.Str(kv.Key); w.Int(kv.Value); }

            // Floods, traders, difficulty.
            w.Int(FloodDay); w.Float(FloodStrength); w.Int(FloodEndDay); w.Int(Floods);
            w.Bool(AutoTrade); w.Int(TradesDeclined); w.Int(Difficulty);
            w.Bool(PendingTrade != null);
            if (PendingTrade != null) { w.Int(PendingTrade.Give); w.Float(PendingTrade.GiveAmount); w.Int(PendingTrade.Want); w.Float(PendingTrade.GetAmount); w.Int(PendingTrade.Day); }

            // Events and warnings.
            w.Int(Events.Count);
            foreach (var e in Events) { w.Int(e.Day); w.Byte((byte)e.Severity); w.Str(e.Text); w.Bool(e.HasPlace); w.F2(e.Place); }
            w.Strs(Warnings);
            w.Int(_warnSeen.Count); foreach (var kv in _warnSeen) { w.Str(kv.Key); w.Int(kv.Value); }

            // Buildings, then people (who point at buildings).
            w.Int(TownCenter?.Id ?? -1);
            w.Int(Buildings.Count);
            foreach (var b in Buildings) WriteBuilding(w, b);
            w.Int(Citizens.Count);
            foreach (var c in Citizens) WriteCitizen(w, c);
            w.Int(Departing.Count);
            foreach (var c in Departing) WriteCitizen(w, c);
        }

        static int Ref(Building b) => b?.Id ?? -1;
        Building Ref(int id) => id >= 0 && id < Buildings.Count ? Buildings[id] : null;

        void WriteBuilding(SaveWriter w, Building b)
        {
            w.Str(b.Def.id); w.I2(b.Origin); w.Int(b.Rot); w.Byte((byte)b.State);
            w.Float(b.Work); w.Floats(b.Delivered); w.Floats(b.InTransit);
            w.Floats(b.Store); w.Float(b.Stored); w.Floats(b.Out); w.Floats(b.In); w.Float(b.OutTotal); w.Float(b.HaulReserved);
            w.Byte((byte)b.Priority); w.Bool(b.Paused); w.Int(b.Focus);
            w.Int(b.Workers); w.Int(b.Residents); w.Int(b.Builders);
            w.Bool(b.Heated); w.Bool(b.Fueled); w.Float(b.Fullness); w.Float(b.FeatureLeft); w.Float(b.FeatureAcc);
            w.Str(b.Status); w.Str(b.UpgradeTo?.id);
            w.Float(b.Sown); w.Float(b.Growth); w.Float(b.Harvested); w.Int(b.CropOut); w.Float(b.TendedToday); w.Bool(b.Irrigated);
            w.Float(b.GoodsLife); w.Bool(b.HasWater); w.Bool(b.HasFaith); w.Bool(b.Flooded); w.Float(b.FloodBonus);
        }

        Building ReadBuilding(SaveReader r, int id)
        {
            string defId = r.Str();
            var d = Data.Building(defId) ?? throw new InvalidDataException($"unknown building '{defId}' (made with other game data?)");
            var b = new Building { Id = id, Def = d, Origin = r.I2(), Rot = r.Int() & 3, State = (BuildState)r.Byte() };
            b.Work = r.Float(); b.Delivered = r.Floats(); b.InTransit = r.Floats();
            b.Store = r.ResFloats(); b.Stored = r.Float(); b.Out = r.ResFloats(); b.In = r.ResFloats(); b.OutTotal = r.Float(); b.HaulReserved = r.Float();
            b.Priority = (Priority)r.Byte(); b.Paused = r.Bool(); b.Focus = r.Int();
            b.Workers = r.Int(); b.Residents = r.Int(); b.Builders = r.Int();
            b.Heated = r.Bool(); b.Fueled = r.Bool(); b.Fullness = r.Float(); b.FeatureLeft = r.Float(); b.FeatureAcc = r.Float();
            b.Status = r.Str() ?? ""; string up = r.Str(); b.UpgradeTo = up != null ? Data.Building(up) : null;
            b.Sown = r.Float(); b.Growth = r.Float(); b.Harvested = r.Float(); b.CropOut = r.Int(); b.TendedToday = r.Float(); b.Irrigated = r.Bool();
            b.GoodsLife = r.Float(); b.HasWater = r.Bool(); b.HasFaith = r.Bool(); b.Flooded = r.Bool(); b.FloodBonus = r.Float();
            // Derived: where it stands and its resource indices (from the upgrade while one is under way).
            b.Center = FootprintCenter(d, b.Origin, b.Rot);
            var door = DoorCell(d, b.Origin, b.Rot);
            b.Door = Map.Cell(door.x, door.y);
            b.DoorPos = new float2(door.x + 0.5f, door.y + 0.5f);
            b.CostRes = Array.ConvertAll((b.UpgradeTo ?? d).cost, a => ResIndex(a.id));
            b.OutRes = Array.ConvertAll(d.outputs, a => ResIndex(a.id));
            b.InRes = Array.ConvertAll(d.inputs, i => Array.ConvertAll(i.ids, ResIndex));
            b.YieldKey = "yield." + d.yieldTag;
            if (r.GoodsRemapped)
            {
                // Goods moved or dropped: the totals are recounted from what is left (otherwise kept bit for bit).
                if (b.Store != null) { float st = 0; foreach (var v in b.Store) st += v; b.Stored = st; }
                if (b.Out != null) { float ot = 0; foreach (var v in b.Out) ot += v; b.OutTotal = ot; }
            }
            if (b.Out == null || b.Out.Length != ResCount || b.In == null || b.In.Length != ResCount) throw new InvalidDataException("goods list does not match the game data");
            return b;
        }

        void WriteCitizen(SaveWriter w, Citizen c)
        {
            w.Int(c.Id); w.Str(c.Name); w.Bool(c.Female); w.Float(c.Age);
            w.Int(Ref(c.Home)); w.Int(Ref(c.Job)); w.Bool(c.IsBuilder);
            w.F2(c.Pos); w.Float(c.Facing); w.Byte((byte)c.Activity);
            w.Float(c.Health); w.Float(c.Happiness); w.Float(c.Warmth); w.Bool(c.Hungry); w.Bool(c.Cold); w.Float(c.Ate); w.Int(c.HungryDays);
            w.Int(c.SickDays); w.Bool(c.Treated); w.Int(c.Tool); w.Float(c.ToolLife); w.Float(c.ClothingLife);
            w.Int(c.CarryRes); w.Float(c.CarryAmt); w.Float(c.LoadTotal); w.Int(c.LoadRes); w.Floats(c.Load);
            w.Int(c.FieldFeature); w.Bool(c.AtField); w.Int(c.UnhappyDays); w.Bool(c.NoWater); w.Bool(c.NoGoods); w.Bool(c.NoFaith);
            w.Int(c.DisputeDays); w.Float(c.BirthCooldown); w.Bool(c.Gone); w.Str(c.LastHarm);
            w.Byte((byte)c.Task); w.Int(Ref(c.TaskB)); w.Int(Ref(c.TaskB2)); w.Int(c.TaskIdx); w.Float(c.TaskAmt);
            w.Int(c.Target); w.Float(c.Stuck); w.Int(c.ShiftDay);
        }

        Citizen ReadCitizen(SaveReader r)
        {
            var c = new Citizen { Id = r.Int(), Name = r.Str(), Female = r.Bool(), Age = r.Float() };
            c.Home = Ref(r.Int()); c.Job = Ref(r.Int()); c.IsBuilder = r.Bool();
            c.Pos = r.F2(); c.Facing = r.Float(); c.Activity = (Activity)r.Byte();
            c.Health = r.Float(); c.Happiness = r.Float(); c.Warmth = r.Float(); c.Hungry = r.Bool(); c.Cold = r.Bool(); c.Ate = r.Float(); c.HungryDays = r.Int();
            c.SickDays = r.Int(); c.Treated = r.Bool(); c.Tool = r.ResIdx(); c.ToolLife = r.Float(); c.ClothingLife = r.Float();
            c.CarryRes = r.ResIdx(); c.CarryAmt = r.Float(); c.LoadTotal = r.Float(); c.LoadRes = r.ResIdx(); c.Load = r.ResFloats();
            c.FieldFeature = r.Int(); c.AtField = r.Bool(); c.UnhappyDays = r.Int(); c.NoWater = r.Bool(); c.NoGoods = r.Bool(); c.NoFaith = r.Bool();
            c.DisputeDays = r.Int(); c.BirthCooldown = r.Float(); c.Gone = r.Bool(); c.LastHarm = r.Str() ?? "";
            c.Task = (Task)r.Byte(); c.TaskB = Ref(r.Int()); c.TaskB2 = Ref(r.Int()); c.TaskIdx = r.Int(); c.TaskAmt = r.Float();
            c.Target = r.Int(); c.Stuck = r.Float(); c.ShiftDay = r.Int();
            if (r.GoodsRemapped)
            {
                // Errands whose index is a good (the others index a building's cost list).
                if (c.Task == Task.MoveStock || c.Task == Task.FetchInput || c.Task == Task.DeliverInput)
                {
                    c.TaskIdx = r.MapRes(c.TaskIdx);
                    if (c.TaskIdx < 0)
                    {
                        if (c.Task == Task.MoveStock && c.TaskB2 != null) c.TaskB2.HaulReserved = math.max(0, c.TaskB2.HaulReserved - c.TaskAmt);
                        c.Task = Task.None; c.TaskB = c.TaskB2 = null; c.TaskAmt = 0; c.Target = -1;
                    }
                }
                if (c.Tool < 0) c.ToolLife = 0;
                if (c.CarryRes < 0) c.CarryAmt = 0;
                if (c.Load != null) { float lt = 0; foreach (var v in c.Load) lt += v; c.LoadTotal = lt; if (lt <= 0) c.LoadRes = -1; }
            }
            return c;
        }

        /// <summary>Restores a settlement written by <see cref="Write"/> into this freshly made one (same data, same map).</summary>
        public void Read(SaveReader r)
        {
            Time = r.Float(); Day = r.Int(); DayFraction = r.Float(); WintersSurvived = r.Int(); Speed = r.Float();
            _acc = r.Float(); _hourAcc = r.Float(); _nextId = r.Int(); _jobsDirty = r.Bool();
            _rng.State = r.ULong();
            BuildersWanted = r.Int(); Births = r.Int(); Migrants = r.Int(); Leavers = r.Int();
            Deaths.Clear(); for (int i = 0, n = r.Int(); i < n; i++) Deaths[r.Str()] = r.Int();
            bool v2 = r.Version >= 2;
            KnowledgeEarned = v2 ? r.Double() : r.Float(); KnowledgeBank = r.Float();
            CopyInto(r.ResFloats(), StockTotals);
            CopyInto(v2 ? r.ResDoubles() : Widen(r.ResFloats()), Produced); CopyInto(v2 ? r.ResDoubles() : Widen(r.ResFloats()), Consumed);
            WorkerSeconds = v2 ? r.Double() : r.Float(); ProductiveSeconds = v2 ? r.Double() : r.Float();
            GrainProducedF = v2 ? r.Float() : _grain >= 0 ? (float)Produced[_grain] : 0;   // v1 kept it in Produced (a float then)
            ToolSwaps = r.Int(); PathCount = r.Int(); Upgrades = r.Int(); Trades = r.Int();
            Disputes = r.Int(); Thefts = r.Int(); Outbreaks = r.Int();
            Unrest = r.Float(); UnrestTarget = r.Float(); OutbreakRisk = r.Float();
            _needWater = r.Bool(); _needGoods = r.Bool(); _needFaith = r.Bool();

            // Research and law ids the data no longer has are dropped (they would throw later, inside Tick).
            var unknown = new SortedSet<string>();
            bool Known(string id) { if (id != null && Data.ResearchById(id) != null) return true; unknown.Add(id ?? "(none)"); return false; }
            bool KnownLaw(string id) { foreach (var l in Data.Laws) if (l.id == id) return true; unknown.Add("law " + (id ?? "(none)")); return false; }
            _researched.Clear(); foreach (var id in r.Strs()) if (Known(id)) _researched.Add(id);
            _progress.Clear(); for (int i = 0, n = r.Int(); i < n; i++) { string id = r.Str(); float p = r.Float(); if (Known(id)) _progress[id] = p; }
            ResearchQueue.Clear(); foreach (var id in r.Strs()) if (Known(id)) ResearchQueue.Add(id);
            string ageId = r.Str();
            Age = Data.Age(ageId) ?? throw new InvalidDataException($"unknown age '{ageId}'");
            // Milestones: matched to this build's list for the age (v2 by key, v1 by position); a saved one the data no
            // longer has is dropped, a new one starts not done.
            Milestones.Clear();
            var savedMs = new List<(string key, bool done, int day, int cur)>();
            for (int i = 0, ms = r.Int(); i < ms; i++)
            {
                string key = r.Version >= 2 ? r.Str() : (i < Age.milestones.Length ? MilestoneKey(Age.milestones[i]) : null);
                savedMs.Add((key, r.Bool(), r.Int(), r.Int()));
            }
            foreach (var def in Age.milestones)
            {
                var m = new MilestoneState { Def = def };
                int k = savedMs.FindIndex(x => x.key == MilestoneKey(def));
                if (k >= 0) { m.Done = savedMs[k].done; m.DoneDay = savedMs[k].day; m.Current = savedMs[k].cur; savedMs.RemoveAt(k); }
                Milestones.Add(m);
            }
            if (savedMs.Count > 0) UnityEngine.Debug.LogWarning($"[Save] milestones this build does not have were dropped: {string.Join(", ", savedMs.ConvertAll(x => x.key ?? "?"))}");
            ResearchLog.Clear(); for (int i = 0, n = r.Int(); i < n; i++) ResearchLog.Add((r.Str(), r.Int()));
            AgeLog.Clear(); for (int i = 0, n = r.Int(); i < n; i++) AgeLog.Add((r.Str(), r.Int()));
            _laws.Clear(); foreach (var id in r.Strs()) if (KnownLaw(id)) _laws.Add(id);
            _lawChanged.Clear(); for (int i = 0, n = r.Int(); i < n; i++) { string id = r.Str(); int day = r.Int(); if (KnownLaw(id)) _lawChanged[id] = day; }
            if (unknown.Count > 0) UnityEngine.Debug.LogWarning($"[Save] research or laws this build does not have were dropped: {string.Join(", ", unknown)}");

            FloodDay = r.Int(); FloodStrength = r.Float(); FloodEndDay = r.Int(); Floods = r.Int();
            AutoTrade = r.Bool(); TradesDeclined = r.Int(); Difficulty = r.Int();
            PendingTrade = r.Bool() ? new TradeOffer { Give = r.ResIdx(), GiveAmount = r.Float(), Want = r.ResIdx(), GetAmount = r.Float(), Day = r.Int() } : null;
            if (PendingTrade != null && (PendingTrade.Give < 0 || PendingTrade.Want < 0)) PendingTrade = null;   // a good gone from the data

            Events.Clear();
            for (int i = 0, n = r.Int(); i < n; i++) Events.Add(new SimEvent { Day = r.Int(), Severity = (Severity)r.Byte(), Text = r.Str(), HasPlace = r.Bool(), Place = r.F2() });
            Warnings.Clear(); Warnings.AddRange(r.Strs());
            _warnSeen.Clear(); for (int i = 0, n = r.Int(); i < n; i++) _warnSeen[r.Str()] = r.Int();

            int tc = r.Int();
            Buildings.Clear();
            int nb = r.Int();
            for (int i = 0; i < nb; i++) Buildings.Add(ReadBuilding(r, i));
            // Second pass: buildings exist before anything refers to them.
            TownCenter = Ref(tc);
            Citizens.Clear();
            for (int i = 0, n = r.Int(); i < n; i++) Citizens.Add(ReadCitizen(r));
            Departing.Clear();
            for (int i = 0, n = r.Int(); i < n; i++) Departing.Add(ReadCitizen(r));

            if (r.GoodsRemapped) RefreshStock();
            RebuildEffects();
            FloodCells = Flooding || FloodDay >= 0 && Day >= FloodDay - 1 ? ComputeFloodCells(FloodStrength) : null;
        }

        /// <summary>What a milestone is saved by: its label (shown to the player, unique within an age).</summary>
        static string MilestoneKey(MilestoneDef d) => d.label ?? $"{d.type}:{d.target}:{d.value}";

        static void CopyInto<T>(T[] from, T[] to)
        {
            if (from == null || from.Length != to.Length) throw new InvalidDataException("goods list does not match the game data");
            Array.Copy(from, to, to.Length);
        }

        static double[] Widen(float[] a) => a == null ? null : Array.ConvertAll(a, v => (double)v);
    }
}
