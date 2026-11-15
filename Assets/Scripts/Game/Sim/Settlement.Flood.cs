using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Spring floods, trader offers and the difficulty setting.
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ spring floods

        /// <summary>Day the next flood is due (-1 = none this spring). Decided on the first day of spring, warned the day before.</summary>
        public int FloodDay { get; private set; } = -1;
        /// <summary>How far the river rises over its normal level (world units) in the coming or current flood.</summary>
        public float FloodStrength { get; private set; }
        /// <summary>Day the current flood ends (the waters are down at its start); -1 while no flood runs.</summary>
        public int FloodEndDay { get; private set; } = -1;
        public bool Flooding => FloodEndDay >= 0;
        public bool FloodWarned => FloodDay >= 0 && Day == FloodDay - 1;
        public int Floods { get; private set; }

        bool[] _floodRisk;
        /// <summary>The floodplain: every cell the strongest spring flood would reach (fixed for a map; for placement hints).</summary>
        public bool[] FloodRisk => _floodRisk ??= ComputeFloodCells(B.floodStrengthMax);

        /// <summary>Footprint cells on the floodplain.</summary>
        public int FloodRiskCells(BuildingDef d, int2 origin, int rot = 0)
        {
            var risk = FloodRisk; var size = Size(d, rot); int n = 0;
            for (int y = origin.y; y < origin.y + size.y; y++)
                for (int x = origin.x; x < origin.x + size.x; x++)
                    if (Map.InBounds(x, y) && risk[Map.Cell(x, y)]) n++;
            return n;
        }

        /// <summary>Cells under water in the coming or current flood (null when none is due). Derived from the strength.</summary>
        public bool[] FloodCells { get; private set; }

        /// <summary>For the view's water: the river's rise right now, ramping up over the flood's first hours and down over its last.</summary>
        public float FloodLevel
        {
            get
            {
                if (!Flooding) return 0;
                float t = Day - (FloodEndDay - B.floodDays) + DayFraction, len = B.floodDays;
                return FloodStrength * math.saturate(math.min(t / 0.25f, (len - t) / 0.6f));
            }
        }

        /// <summary>Daily, before anything else is used or made: decide, warn, start and end floods.</summary>
        void DailyFlood()
        {
            if (B.floodChance <= 0) return;
            // First day of spring (not the first spring: the band has only just arrived): will the river flood?
            if (Season == 0 && DayOfSeason == 0 && Year >= B.floodFromYear)
            {
                FloodDay = -1;
                if (_rng.NextDouble() < B.floodChance)
                {
                    FloodDay = Day + 2 + _rng.Next(3);
                    FloodStrength = R(B.floodStrengthMin, B.floodStrengthMax);
                }
            }
            if (FloodDay >= 0 && Day == FloodDay - 1) WarnFlood();
            if (FloodDay >= 0 && Day == FloodDay) StartFlood();
            else if (Flooding && Day >= FloodEndDay) EndFlood();
        }

        void WarnFlood()
        {
            FloodCells = ComputeFloodCells(FloodStrength);
            int at = BuildingsIn(FloodCells, out var where);
            Log(Severity.Warning, at > 0 ? $"The river is rising: it will flood tomorrow. {at} building{(at == 1 ? "" : "s")} on the low ground by the river will be under water." : "The river is rising: it will flood tomorrow.", where);
        }

        /// <summary>Screenshots and tests only: a flood of this strength tomorrow, warned now.</summary>
        public void DebugFloodTomorrow(float strength)
        {
            if (TownCenter == null || Flooding) return;
            FloodDay = Day + 1; FloodStrength = strength;
            WarnFlood();
        }

        void StartFlood()
        {
            FloodCells ??= ComputeFloodCells(FloodStrength);
            FloodEndDay = Day + B.floodDays;
            FloodDay = -1;
            Floods++;
            float lost = 0; int hit = 0;
            foreach (var b in Buildings)
            {
                if (!b.Alive || !UnderWater(b)) continue;
                b.Flooded = true; hit++;
                // Stored and waiting goods get soaked: what spoils is mostly lost, the rest partly.
                for (int r = 0; r < ResCount; r++)
                {
                    float k = Data.Resources[r].spoilPerDay > 0 ? B.floodLossSpoiling : B.floodLoss;
                    if (b.Store != null && b.Store[r] > 0) { float l = b.Store[r] * k; b.Store[r] -= l; b.Stored -= l; Lost[r] += l; lost += l; }
                    if (b.Out[r] > 0) { float l = b.Out[r] * k; b.Out[r] -= l; b.OutTotal -= l; Lost[r] += l; lost += l; }
                    if (b.In[r] > 0) { float l = b.In[r] * k; b.In[r] -= l; Lost[r] += l; lost += l; }
                }
                // Silt: a flooded field grows more this year.
                if (b.Def.crop) b.FloodBonus = 1f + B.floodFertility;
            }
            RefreshStock();
            _jobsDirty = true;
            Log(Severity.Bad, hit > 0 ? $"The river has flooded: {hit} building{(hit == 1 ? " is" : "s are")} under water{(lost >= 1.5f ? $" and {lost:0} goods were spoiled" : lost > 0.01f ? " and a few goods were spoiled" : "")}. Work stops there until the water goes down."
                                      : "The river has flooded the low ground; no buildings stand there.", FloodCentre());
        }

        void EndFlood()
        {
            FloodEndDay = -1;
            int fields = 0;
            foreach (var b in Buildings) { if (b.Flooded && b.Def.crop) fields++; b.Flooded = false; }
            FloodCells = null;
            _jobsDirty = true;
            Log(Severity.Info, fields > 0 ? $"The flood waters have gone down. The silt they left will make {fields} field{(fields == 1 ? "" : "s")} yield more this year." : "The flood waters have gone down.");
        }

        bool UnderWater(Building b)
        {
            if (FloodCells == null) return false;
            int wet = 0;
            for (int y = b.Origin.y; y < b.Origin.y + b.SY; y++)
                for (int x = b.Origin.x; x < b.Origin.x + b.SX; x++)
                    if (Map.InBounds(x, y) && FloodCells[Map.Cell(x, y)]) wet++;
            return wet * 2 >= b.SX * b.SY;          // half the footprint or more
        }

        int BuildingsIn(bool[] cells, out float2 where)
        {
            int n = 0; where = TownCenter?.Center ?? default; float2 sum = 0;
            foreach (var b in Buildings) if (b.Alive && UnderWater(b)) { n++; sum += b.Center; }
            if (n > 0) where = sum / n; else where = FloodCentre();
            return n;
        }

        float2 FloodCentre()
        {
            // The flooded cell nearest the campfire (where the player will look).
            if (FloodCells == null || TownCenter == null) return TownCenter?.Center ?? default;
            float best = float.MaxValue; float2 at = TownCenter.Center;
            for (int c = 0; c < FloodCells.Length; c++)
            {
                if (!FloodCells[c] || Map.Ground[c] != Ground.Grass) continue;
                var p = new float2(c % Map.W + 0.5f, c / Map.W + 0.5f);
                float d = math.distancesq(p, TownCenter.Center);
                if (d < best) { best = d; at = p; }
            }
            return at;
        }

        /// <summary>
        /// The low ground the river spreads over when it rises by <paramref name="rise"/>: dry cells joined to the river
        /// through cells lower than the raised water (the spring surge of the water simulation covers about the same).
        /// </summary>
        public bool[] ComputeFloodCells(float rise)
        {
            int n = Map.W * Map.H;
            var wet = new bool[n];
            var queue = new Queue<int>();
            for (int c = 0; c < n; c++) if (Map.Ground[c] != Ground.Grass) { wet[c] = true; queue.Enqueue(c); }
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                int x = c % Map.W, y = c / Map.W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (!Map.InBounds(nx, ny)) continue;
                    int nc = Map.Cell(nx, ny);
                    if (wet[nc] || Map.CellHeight(nx, ny) >= Map.RiverLevelAt(ny) + rise * 0.85f) continue;
                    wet[nc] = true;
                    queue.Enqueue(nc);
                }
            }
            return wet;
        }

        // ------------------------------------------------------------------ trader offers

        /// <summary>A trading party's offer waiting for the player's answer (null if none).</summary>
        public sealed class TradeOffer { public int Give, Want, Day; public float GiveAmount, GetAmount; }
        public TradeOffer PendingTrade { get; private set; }
        /// <summary>Accept every trader's offer without asking (the auto-player's way, and a HUD toggle).</summary>
        public bool AutoTrade;
        public int TradesDeclined { get; private set; }

        public bool AcceptTrade()
        {
            var o = PendingTrade;
            if (o == null) return false;
            PendingTrade = null;
            DoTrade(o.Give, o.GiveAmount, o.Want, o.GetAmount);
            return true;
        }

        public void DeclineTrade()
        {
            if (PendingTrade == null) return;
            PendingTrade = null;
            TradesDeclined++;
            Log(Severity.Info, "The traders moved on without a deal.");
        }

        void DoTrade(int give, float giveAmt, int want, float getAmt)
        {
            float took = Withdraw(give, giveAmt);
            Consumed[give] -= took;            // traded away, not used up
            // Short of what was promised (the stores changed while the offer stood): fewer goods in return.
            if (giveAmt > 0 && took < giveAmt) getAmt = math.floor(getAmt * took / giveAmt);
            float stored = Deposit(want, getAmt, TownCenter.Center);
            Traded[give] -= took; Traded[want] += stored;
            Trades++;
            Log(Severity.Info, $"Traders came by: {took:0} {Data.Resources[give].name.ToLower()} swapped for {getAmt:0} {Data.Resources[want].name.ToLower()}." +
                (getAmt - stored >= 1f ? $" {getAmt - stored:0} of it had no room in store and was left behind." : ""), TownCenter.Center);
        }

        /// <summary>Daily: an unanswered offer lapses after its second day.</summary>
        void DailyTradeExpiry()
        {
            if (PendingTrade != null && Day > PendingTrade.Day + 1) { PendingTrade = null; Log(Severity.Info, "The traders could not wait any longer and moved on."); }
        }

        // ------------------------------------------------------------------ difficulty

        /// <summary>Index into Balance.difficulties (0 Gentle, 1 Standard, 2 Harsh). Set before the campfire is placed.</summary>
        public int Difficulty { get; private set; } = 1;
        public DifficultyDef Diff => B.difficulties != null && Difficulty >= 0 && Difficulty < B.difficulties.Length ? B.difficulties[Difficulty] : DifficultyDef.Standard;

        public void SetDifficulty(int d)
        {
            if (!NeedsCampfire) return;           // fixed once the band has settled
            Difficulty = B.difficulties != null && B.difficulties.Length > 0 ? math.clamp(d, 0, B.difficulties.Length - 1) : 1;
        }
    }
}
