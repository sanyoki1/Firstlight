using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Agricultural Age society: needs that arrive with the age (water, goods, faith), health service tiers, laws,
    // unrest (disputes, theft) and disease outbreaks.
    public sealed partial class Settlement
    {
        // ------------------------------------------------------------------ needs

        /// <summary>A need counts once its age is reached and its research (if any) is known.</summary>
        public bool NeedActive(string id)
        {
            foreach (var n in B.needs)
                if (n.id == id)
                    return (string.IsNullOrEmpty(n.age) || Data.AgeOrder(n.age) <= Age.order) && (string.IsNullOrEmpty(n.research) || IsResearched(n.research));
            return false;
        }

        public NeedDef Need(string id) { foreach (var n in B.needs) if (n.id == id) return n; return null; }

        bool _needWater, _needGoods, _needFaith;

        /// <summary>Hourly: which homes have clean water (river close by, or a well in reach) and a shaman in reach.</summary>
        void RefreshHomes()
        {
            _needWater = NeedActive("water"); _needGoods = NeedActive("goods"); _needFaith = NeedActive("faith");
            float wellMult = Effect("water.radius");
            foreach (var h in Buildings)
            {
                if (!h.Active || h.Def.housing <= 0 && h != TownCenter) continue;
                bool water = Map.CountFeature("water", h.Center, B.riverWaterRadius) > 0, faith = false;
                foreach (var s in Buildings)
                {
                    if (!s.Active) continue;
                    if (!water && s.Def.waterRadius > 0 && math.distance(s.Center, h.Center) <= s.Def.waterRadius * wellMult) water = true;
                    if (!faith && s.Def.faithRadius > 0 && s.Workers > 0 && math.distance(s.Center, h.Center) <= s.Def.faithRadius) faith = true;
                }
                h.HasWater = water; h.HasFaith = faith;
            }
        }

        /// <summary>Daily: each occupied home keeps one pottery good in use (it wears out and is replaced from store).</summary>
        void DailyGoods()
        {
            if (_goods < 0) return;
            float life = B.goodsWearDays / math.max(0.1f, Effect("wear.goods"));
            foreach (var h in Buildings)
            {
                if (!h.Active || h.Def.housing <= 0 || h.Residents == 0) continue;
                if (h.GoodsLife <= 0 && _needGoods && StockTotals[_goods] >= 1f && Withdraw(_goods, 1f) >= 1f) h.GoodsLife = life;
                if (h.GoodsLife > 0) h.GoodsLife -= 1f;
            }
        }

        /// <summary>Happiness from the needs that have arrived (1 = met), and whether each is met, for a citizen.</summary>
        void ExtraNeeds(Citizen c, ref float sum, ref int count, ref float min)
        {
            var home = c.Home ?? TownCenter;
            c.NoWater = _needWater && !home.HasWater;
            c.NoGoods = _needGoods && (c.Home == null || c.Home.GoodsLife <= 0);
            c.NoFaith = _needFaith && !home.HasFaith;
            void Add(bool active, bool unmet, string id, ref float s, ref int n, ref float m)
            {
                if (!active) return;
                float v = unmet ? (Need(id)?.unmet ?? 0.6f) : 1f;
                s += v; n++; m = math.min(m, v);
            }
            Add(_needWater, c.NoWater, "water", ref sum, ref count, ref min);
            Add(_needGoods, c.NoGoods, "goods", ref sum, ref count, ref min);
            Add(_needFaith, c.NoFaith, "faith", ref sum, ref count, ref min);
        }

        /// <summary>Share of people whose need is met (for the HUD); -1 if the need does not apply yet.</summary>
        public float NeedMet(string id)
        {
            if (!NeedActive(id)) return -1;
            int n = 0, met = 0;
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                n++;
                bool unmet = id == "water" ? c.NoWater : id == "goods" ? c.NoGoods : id == "faith" ? c.NoFaith : false;
                if (!unmet) met++;
            }
            return n == 0 ? 1 : met / (float)n;
        }

        // ------------------------------------------------------------------ health services

        /// <summary>
        /// The best health service in reach of a point (staffed, with herbs in store): multipliers on the chance of falling
        /// sick and on its harm. A healer's hut covers farther and does better than a herbalist.
        /// </summary>
        bool HealthCover(float2 p, out float chance, out float damage)
        {
            chance = 1f; damage = 1f;
            bool any = false;
            foreach (var b in Buildings)
            {
                if (!b.Active || b.Workers <= 0 || b.Def.healthRadius <= 0 || math.distance(b.Center, p) > b.Def.healthRadius) continue;
                any = true;
                chance = math.min(chance, b.Def.healChance);
                damage = math.min(damage, b.Def.healDamage);
            }
            return any;
        }

        // ------------------------------------------------------------------ laws

        readonly HashSet<string> _laws = new();
        readonly Dictionary<string, int> _lawChanged = new();

        public bool LawActive(string id) => _laws.Contains(id);
        public IEnumerable<string> ActiveLaws => _laws;

        /// <summary>Laws need their research and a finished council; they are suspended while there is none.</summary>
        public bool LawsInForce { get { foreach (var b in Buildings) if (b.Active && b.Def.enablesLaws) return true; return false; } }

        public bool CanSetLaw(string id, bool on, out string reason)
        {
            reason = "";
            var l = Data.Laws.Find(x => x.id == id);
            if (l == null) { reason = "Unknown law"; return false; }
            if (!string.IsNullOrEmpty(l.research) && !IsResearched(l.research)) { reason = $"Needs research: {Data.ResearchById(l.research)?.name}"; return false; }
            var council = Data.Buildings.Find(b => b.enablesLaws);
            if (!LawsInForce) { reason = $"Needs a {council?.name ?? "council"}"; return false; }
            if (LawActive(id) == on) { reason = on ? "Already in force" : "Not in force"; return false; }
            if (_lawChanged.TryGetValue(id, out int day) && Day - day < B.lawCooldownDays) { int left = B.lawCooldownDays - (Day - day); reason = $"Changed recently: wait {left} day{(left == 1 ? "" : "s")}"; return false; }
            return true;
        }

        public bool SetLaw(string id, bool on, out string reason)
        {
            if (!CanSetLaw(id, on, out reason)) return false;
            if (on) _laws.Add(id); else _laws.Remove(id);
            _lawChanged[id] = Day;
            RebuildEffects();
            var l = Data.Laws.Find(x => x.id == id);
            Log(Severity.Info, on ? $"The council passed a law: {l.name}." : $"The council ended the law: {l.name}.", TownCenter?.Center ?? default);
            return true;
        }
        public bool SetLaw(string id, bool on) => SetLaw(id, on, out _);

        // ------------------------------------------------------------------ unrest

        /// <summary>0..1: grows with unhappiness, crowding, people without faith and sheer numbers; calmed by a council and laws.</summary>
        public float Unrest { get; private set; }
        public float UnrestTarget { get; private set; }
        public int Disputes, Thefts, Outbreaks;

        bool CouncilStaffed(out float power)
        {
            power = 0;
            foreach (var b in Buildings) if (b.Active && b.Def.orderPower > 0 && b.Workers > 0) power = math.max(power, b.Def.orderPower);
            return power > 0;
        }

        void DailyUnrest()
        {
            int pop = 0, unhappy = 0, faithless = 0, crowded = 0;
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                pop++;
                if (c.Happiness < 0.5f) unhappy++;
                if (c.NoFaith) faithless++;
                if (c.Home == null) crowded++;
            }
            foreach (var h in Buildings) if (h.Active && h.Def.housing > 0) crowded += math.max(0, h.Residents - h.Def.housing);
            if (pop == 0 || !string.IsNullOrEmpty(B.unrestAge) && Data.AgeOrder(B.unrestAge) > Age.order) { Unrest = 0; UnrestTarget = 0; return; }
            float target = pop / math.max(1f, B.unrestPopScale) + unhappy / (float)pop * B.unrestUnhappy
                         + crowded / (float)pop * B.unrestCrowding + faithless / (float)pop * B.unrestFaithless;
            target *= Effect("unrest");
            if (CouncilStaffed(out float power)) target *= 1f - power;
            UnrestTarget = math.saturate(target);
            Unrest = math.lerp(Unrest, UnrestTarget, B.unrestSmoothing);

            foreach (var c in Citizens) if (c.DisputeDays > 0 && --c.DisputeDays == 0) _jobsDirty = true;
            if (pop < 20) return;
            // Disputes: a few workers caught up in a quarrel lose work days.
            if (Unrest > B.disputeAt && _rng.NextDouble() < (Unrest - B.disputeAt) * B.disputeChance)
            {
                var workers = Citizens.Where(c => !c.Gone && c.Job != null && c.DisputeDays == 0).ToList();
                if (workers.Count >= 2)
                {
                    int n = math.min(workers.Count, 2 + _rng.Next(3));
                    var a = workers[_rng.Next(workers.Count)];
                    // The quarrel's people: a spread of distinct workers starting from a (a step of 7 can come round to
                    // the same person when the count divides by it, so step by one past anyone already in it).
                    int at = workers.IndexOf(a), caught = 0;
                    for (int i = 0; i < workers.Count && caught < n; i++)
                    {
                        var w = workers[(at + i * 7) % workers.Count];
                        if (w.DisputeDays > 0) w = workers.FirstOrDefault(x => x.DisputeDays == 0);
                        if (w == null) break;
                        w.DisputeDays = 2; caught++;
                    }
                    n = caught;
                    Disputes++;
                    _jobsDirty = true;
                    Log(Severity.Warning, $"A dispute broke out around {a.Name}'s household: {n} people lost two days of work.", a.Home?.DoorPos ?? a.Pos);
                }
            }
            // Theft: part of one store's most valuable good goes missing.
            if (Unrest > B.theftAt && _rng.NextDouble() < (Unrest - B.theftAt) * B.theftChance)
            {
                Building from = null; int res = -1; float worth = 0;
                foreach (var s in Buildings)
                {
                    if (!s.Active || s.Store == null) continue;
                    for (int r = 0; r < ResCount; r++)
                    {
                        float v = s.Store[r] * math.max(0.2f, Data.Resources[r].value) * (0.5f + (float)_rng.NextDouble());
                        if (s.Store[r] >= 3 && v > worth) { worth = v; from = s; res = r; }
                    }
                }
                if (from != null)
                {
                    float amt = math.min(40f, from.Store[res] * R(0.08f, 0.2f));
                    amt = TakeFrom(from, res, amt);
                    Lost[res] += amt;
                    Thefts++;
                    Log(Severity.Warning, $"Theft: {amt:0} {Data.Resources[res].name.ToLower()} went missing from the {from.Def.name}.", from.Center);
                }
            }
        }

        // ------------------------------------------------------------------ outbreaks

        /// <summary>Today's chance of a disease outbreak (0 below the population threshold).</summary>
        public float OutbreakRisk { get; private set; }

        void RefreshOutbreakRisk()
        {
            int pop = 0, noWater = 0, crowdedPeople = 0, covered = 0;
            foreach (var c in Citizens)
            {
                if (c.Gone) continue;
                pop++;
                if (c.NoWater) noWater++;
                var p = (c.Home ?? TownCenter).Center;
                // Only a healer (not a herbalist) keeps an outbreak from starting.
                if (HealthCover(p, out float ch, out _) && ch <= 0.45f && _herbs >= 0 && StockTotals[_herbs] > 0.5f) covered++;
            }
            foreach (var h in Buildings) if (h.Active && h.Def.housing > 0 && h.Residents > CrowdFree(h)) crowdedPeople += h.Residents;
            if (pop < B.outbreakMinPop) { OutbreakRisk = 0; return; }
            float risk = B.outbreakBase * (1f + 3f * crowdedPeople / pop + 4f * noWater / pop + pop / 100f);
            risk *= 1f - 0.6f * covered / pop;
            OutbreakRisk = risk;
        }

        /// <summary>Residents a home holds before it counts as crowded (sickness spreads in crowded homes).</summary>
        float CrowdFree(Building h) => math.max(B.crowdingFreeOccupants, h.Def.housing * 0.75f);

        void DailyOutbreak()
        {
            if (OutbreakRisk <= 0 || _rng.NextDouble() >= OutbreakRisk) return;
            // It starts in the worst home: crowded and without clean water first.
            Building seed = null; float worst = -1;
            foreach (var h in Buildings)
            {
                if (!h.Active || h.Def.housing <= 0 || h.Residents == 0) continue;
                float w = h.Residents / CrowdFree(h) + (h.HasWater || !_needWater ? 0 : 1f) + (float)_rng.NextDouble() * 0.8f;
                if (w > worst) { worst = w; seed = h; }
            }
            if (seed == null) return;
            int ill = 0;
            foreach (var c in Citizens)
            {
                if (c.Gone || c.Sick || c.Home == null || math.distance(c.Home.Center, seed.Center) > B.outbreakRadius) continue;
                float chance = B.outbreakCatch * (c.NoWater ? 1.3f : 1f);
                bool care = HealthCover(c.Home.Center, out float ch, out _) && _herbs >= 0 && StockTotals[_herbs] > 0.5f;
                if (care) chance *= ch;
                if (_rng.NextDouble() >= chance) continue;
                c.SickDays = _rng.Next(B.sickDaysMin, B.sickDaysMax + 1) + (care ? 0 : 3);
                ill++;
            }
            if (ill == 0) return;
            Outbreaks++;
            _jobsDirty = true;
            Log(Severity.Bad, $"Sickness is spreading among the homes around the {seed.Def.name}: {ill} {(ill == 1 ? "person" : "people")} fell ill.", seed.Center);
        }
    }
}
