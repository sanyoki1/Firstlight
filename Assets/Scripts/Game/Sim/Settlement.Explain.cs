using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    // Read-only explanations for the HUD: the parts of a figure the sim works out, as the sim works them out, so the panels
    // never re-derive a rule (and drift from it). Nothing here changes the settlement.
    public sealed partial class Settlement
    {
        /// <summary>One input of a person's happiness: what it is ("no home", "hungry") and its value (1 = fully met).</summary>
        public struct HappinessPart
        {
            public string What;
            public float Value;
        }

        /// <summary>
        /// The needs behind a person's happiness, as the daily update (<c>DailyNeeds</c>) last weighed them: food, warmth,
        /// shelter and health, then water, pottery and faith once they apply. Only the ones not fully met are added, worst
        /// first. Returns the happiness the person is moving toward (the daily update eases toward it).
        /// </summary>
        public float ExplainHappiness(Citizen c, List<HappinessPart> into)
        {
            into.Clear();
            float comfort = B.warmthComfort;
            float food = c.Hungry ? 0f : 1f;
            float warm = math.saturate(c.Warmth / comfort);
            float shelter = c.Home != null ? 1f : B.homelessShelter;
            float health = c.Health * (c.Sick ? 0.6f : 1f);
            float sum = food + warm + shelter + health; int count = 4;
            float min = math.min(math.min(food, warm), math.min(shelter, health));
            void Add(string what, float v) { if (v < 0.999f) into.Add(new HappinessPart { What = what, Value = v }); }
            Add("hunger", food);
            Add("cold", warm);
            Add("no home", shelter);
            Add(c.Sick ? "sickness" : "poor health", health);
            void Extra(bool active, bool unmet, string id, string what)
            {
                if (!active) return;
                float v = unmet ? (Need(id)?.unmet ?? 0.6f) : 1f;
                sum += v; count++; min = math.min(min, v);
                Add(what, v);
            }
            Extra(_needWater, c.NoWater, "water", "no clean water");
            Extra(_needGoods, c.NoGoods, "goods", "no pots at home");
            Extra(_needFaith, c.NoFaith, "faith", "no faith");
            into.Sort((a, b) => a.Value.CompareTo(b.Value));
            float avg = sum / count;
            return math.saturate((avg * (1f - B.happinessWorstWeight) + min * B.happinessWorstWeight) * Effect("happiness"));
        }
    }
}
