using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Agricultural Age economy: fields through the year, moving food into the granary, tool tiers, and traders (Barter).
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ fields

        /// <summary>
        /// One step of work on a crop building. Spring (and early summer if late): sowing; summer: tending (growth is
        /// slower untended); autumn: harvesting into the worker's load, the crop's full yield times how well it grew;
        /// winter: nothing. The crop sown is the field's chosen output (Focus), fixed for the year at sowing.
        /// </summary>
        bool CropWork(Citizen c, Building b, float frac, bool field)
        {
            var d = b.Def;
            int season = Season;
            if (season == 0 || season == 1 && b.Sown < 0.999f)
            {
                if (b.Sown <= 0f) b.CropOut = math.clamp(b.Focus, 0, d.outputs.Length - 1);
                b.Sown = math.min(1f, b.Sown + frac / math.max(0.1f, d.sowWork));
                b.Status = b.Sown >= 0.999f ? "Sown" : "Sowing";
                return true;
            }
            if (season == 1)
            {
                if (b.TendedToday >= d.tendWork) { b.Status = "Tended for today"; return false; }
                b.TendedToday += frac;
                b.Status = "Tending the crop";
                return true;
            }
            if (season == 2)
            {
                if (b.Harvested >= 0.999f || b.Growth <= 0.001f) { b.Status = b.Growth <= 0.001f ? "Nothing was sown this year" : "The harvest is in"; return false; }
                float share = math.min(frac / math.max(0.1f, d.harvestWork), 1f - b.Harvested);
                b.Harvested += share;
                int r = b.OutRes[b.CropOut];
                float amt = d.outputs[b.CropOut].amount * b.Growth * share * CropYieldMult(b);
                if (field)
                {
                    c.Load ??= new float[ResCount];
                    c.Load[r] += amt;
                    RecountLoad(c);
                }
                else { b.Out[r] += amt; b.OutTotal += amt; }
                Produced[r] += amt;
                b.Status = "Harvesting";
                return true;
            }
            b.Status = "Nothing to do this season";
            return false;
        }

        /// <summary>Research and irrigation multiplier on a field's harvest.</summary>
        public float CropYieldMult(Building b) =>
            (string.IsNullOrEmpty(b.Def.yieldTag) ? 1f : Effect(b.YieldKey)) * (b.Irrigated ? Effect("yield.irrigated") : 1f);

        /// <summary>A field's expected harvest this year at its current growth (for the HUD and the auto-player).</summary>
        public float ExpectedHarvest(Building b)
        {
            if (!b.Def.crop || b.Def.outputs.Length == 0) return 0;
            int o = b.Sown > 0 ? b.CropOut : math.clamp(b.Focus, 0, b.Def.outputs.Length - 1);
            float growth = Season == 0 ? B.growthSpring * 8 * math.max(b.Sown, 0.5f) + B.growthSummer * 8 : Season == 1 ? b.Growth + B.growthSummer * (B.daysPerSeason - DayOfSeason) * b.Sown : b.Growth;
            return b.Def.outputs[o].amount * math.min(1f, growth) * (1f - b.Harvested) * CropYieldMult(b);
        }

        /// <summary>Daily: crops grow while sown (faster when tended in summer); a new year starts with bare fields.</summary>
        void DailyCrops()
        {
            int season = Season;
            foreach (var b in Buildings)
            {
                if (!b.Active || !b.Def.crop) continue;
                if (season == 0 && DayOfSeason == 0) { b.Sown = b.Growth = b.Harvested = 0; b.TendedToday = 0; continue; }
                if (season == 0) b.Growth += B.growthSpring * b.Sown;
                else if (season == 1)
                {
                    float tended = math.saturate(b.TendedToday / math.max(0.05f, b.Def.tendWork));
                    b.Growth += B.growthSummer * b.Sown * (B.untendedGrowth + (1f - B.untendedGrowth) * tended);
                }
                b.Growth = math.min(b.Growth, 1f);
                b.TendedToday = 0;
            }
        }

        /// <summary>Fields need no hands in winter (their workers go hauling and building).</summary>
        bool WantsWorkers(Building b) => !(b.Def.crop && (Season == 3 || Season == 2 && b.Harvested >= 0.999f));

        /// <summary>A spot inside a field's footprint to work at (fields are walkable).</summary>
        int CropSpot(Building b, out int feature)
        {
            int x = b.Origin.x + _rng.Next(b.Def.sizeX), y = b.Origin.y + _rng.Next(b.Def.sizeY);
            feature = Map.Cell(math.min(x + 1, b.Origin.x + b.Def.sizeX - 1), y);
            return Map.Cell(x, y);
        }

        // ------------------------------------------------------------------ granary

        /// <summary>
        /// Idle haulers move food that spoils from open stores into a granary with room (biggest pile first), so the
        /// harvest and the meat keep through winter.
        /// </summary>
        bool MoveStockDecide(Citizen c)
        {
            Building to = null; float room = 0;
            foreach (var s in Buildings)
                if (s.Active && s.Store != null && s.Def.accepts.Length > 0 && s.Def.spoilMultiplier < 1f && Free(s) - s.HaulReserved > room) { room = Free(s) - s.HaulReserved; to = s; }
            if (to == null || room < B.carryCapacity) return false;
            Building from = null; int res = -1; float most = 2f;
            foreach (var s in Buildings)
            {
                if (!s.Active || s.Store == null || s == to || s.Def.spoilMultiplier <= to.Def.spoilMultiplier) continue;
                foreach (int r in _foodByPerish)
                    if (Accepts(to, r) && s.Store[r] > most) { most = s.Store[r]; from = s; res = r; }
            }
            if (from == null) return false;
            float amt = math.min(math.min(CarryCap(res), most), room);
            to.HaulReserved += amt;
            c.Task = Task.MoveStock;
            c.TaskB = from; c.TaskB2 = to; c.TaskIdx = res; c.TaskAmt = amt;
            Walk(c, from.Door, Activity.Walking);
            return true;
        }

        // ------------------------------------------------------------------ traders (Barter)

        public int Trades { get; private set; }

        /// <summary>
        /// With Barter known, a trading party calls at the start of spring, summer and autumn: it takes the settlement's
        /// biggest surplus (by value) and leaves the traded good it is shortest of, at the trader's rate.
        /// </summary>
        void TraderVisit()
        {
            if (Effect("traders") <= 1f || TownCenter == null || Season == 3 || DayOfSeason != 1) return;
            // What the settlement is short of: tin once it can make bronze, then tools, clothing, pottery, herbs.
            int want = -1; float wantScore = 0;
            for (int r = 0; r < ResCount; r++)
            {
                var rd = Data.Resources[r];
                if (!rd.traded) continue;
                float target = rd.toolSpeed > 0 ? Adults * 0.35f
                    : r == _clothing ? Population * 0.3f
                    : r == _goods ? HomeCount() * 0.5f
                    : r == _herbs ? 4f
                    : rd.id == "tin_ore" ? (IsResearched("bronze_working") ? 12f : 0f)
                    : 0f;
                if (target <= 0) continue;
                float score = (target - StockTotals[r]) / target;
                if (score > wantScore) { wantScore = score; want = r; }
            }
            if (want < 0) return;
            // What it can spare: the good with the most value above what the settlement keeps of it.
            int give = -1; float spare = 0;
            for (int r = 0; r < ResCount; r++)
            {
                var rd = Data.Resources[r];
                if (r == want || rd.toolSpeed > 0 || rd.value <= 0) continue;
                float keep = rd.isFood ? DailyFoodNeed * 20f : r == _fuel ? FuelNeedUntilSpring() + 20f : 40f;
                float v = (StockTotals[r] - keep) * rd.value;
                if (v > spare) { spare = v; give = r; }
            }
            if (give < 0 || spare < B.traderValue * 0.5f) return;
            float value = math.min(B.traderValue, spare);
            float giveAmt = value / Data.Resources[give].value;
            float getAmt = math.floor(value * B.traderRate / math.max(0.1f, Data.Resources[want].value));
            if (getAmt < 1) return;
            float took = Withdraw(give, giveAmt);
            Consumed[give] -= took;            // traded away, not used up
            Deposit(want, getAmt, TownCenter.Center);
            Trades++;
            Log(Severity.Info, $"Traders came by: {took:0} {Data.Resources[give].name.ToLower()} swapped for {getAmt:0} {Data.Resources[want].name.ToLower()}.", TownCenter.Center);
        }

        int HomeCount() { int n = 0; foreach (var b in Buildings) if (b.Active && b.Def.housing > 0 && b.Residents > 0) n++; return n; }
    }
}
