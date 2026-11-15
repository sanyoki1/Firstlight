using System.Collections.Generic;
using Unity.Mathematics;

namespace Firstlight.Game
{
    /// <summary>
    /// What a workplace makes, uses and learns in a full workday with its present workers: the same product of factors
    /// as the sim's WorkStep (per-worker amount, season, what is in reach, research and laws, difficulty, and each
    /// worker's speed from mood, water and tools). Nominal: it leaves out the time spent walking and carrying, and
    /// inputs or storage running short. The building panel only formats it.
    /// </summary>
    public sealed class WorkEstimate
    {
        public int Workers;
        public float[] Outputs = System.Array.Empty<float>();   // per Def.outputs entry, a day
        public float[] Inputs = System.Array.Empty<float>();    // per Def.inputs group, a day
        public float Knowledge;                                 // a day
        // The factors behind it (each worker's speed factors as the crew's average), for the panel's breakdown.
        public float Season = 1, Near = 1, Research = 1, Difficulty = 1, Mood = 1, Water = 1, Tools = 1;
        public float ElderKnowledge = 1;   // elders' knowledge multiplier (elderKnowledgeMult x laws), when elders work here
        public int Elders, NoWater;        // workers who are elders / who have no clean water nearby
        public float OutputTotal { get { float t = 0; foreach (var o in Outputs) t += o; return t; } }
    }

    public sealed partial class Settlement
    {
        /// <summary>
        /// The daily estimate for a workplace (see <see cref="WorkEstimate"/>). <paramref name="workers"/>: the citizens
        /// working there, if the caller already has them (else they are looked up). Crops are not covered (their work
        /// follows the seasons: sowing, tending, harvest), so a field's outputs stay 0.
        /// </summary>
        public WorkEstimate EstimateWorkplace(Building b, List<Citizen> workers = null)
        {
            var d = b.Def;
            var e = new WorkEstimate { Outputs = new float[d.outputs.Length], Inputs = new float[d.inputs.Length] };
            // Per worker: speed (WorkSpeed, as each sim step uses it) and the elders' knowledge multiplier (as WorkStep).
            float speedSum = 0, knowSum = 0, mood = 0, water = 0, tools = 0;
            float elderMult = B.elderKnowledgeMult * Effect("knowledge.elders");
            void Add(Citizen c)
            {
                float speed = WorkSpeed(c, d.usesTools, d.requiresTools);
                speedSum += speed;
                bool elder = c.Age >= B.elderAge;
                knowSum += speed * (elder ? elderMult : 1f);
                if (elder) e.Elders++;
                if (c.NoWater) e.NoWater++;
                mood += math.lerp(B.workSpeedMin, 1f, math.saturate(c.Happiness / 0.6f));
                water += c.NoWater ? B.noWaterWorkSpeed : 1f;
                tools += !d.usesTools ? 1f : c.Tool >= 0 ? Data.Resources[c.Tool].toolSpeed : d.requiresTools ? 0f : B.noToolWorkSpeed;
                e.Workers++;
            }
            if (workers != null) { foreach (var c in workers) if (!c.Gone && c.Job == b) Add(c); }
            else foreach (var c in Citizens) if (!c.Gone && c.Job == b) Add(c);
            if (e.Workers > 0) { e.Mood = mood / e.Workers; e.Water = water / e.Workers; e.Tools = tools / e.Workers; }
            e.ElderKnowledge = elderMult;

            // The same yield product as WorkStep (one full workday of work = speed; Step / WorkdaySeconds per step).
            e.Research = string.IsNullOrEmpty(d.yieldTag) ? 1f : Effect(b.YieldKey);
            if (d.knowledgePerWorker > 0) e.Knowledge = d.knowledgePerWorker * Effect(b.YieldKey) * knowSum;
            if (d.crop) return e;
            e.Season = d.SeasonYield(Season);
            e.Near = b.Fullness;
            e.Difficulty = Diff.yield;
            float work = speedSum * e.Season * e.Near * e.Research * e.Difficulty;   // = sum over the day of WorkStep's frac x yield
            if (work <= 0) return e;
            for (int i = 0; i < d.outputs.Length; i++) e.Outputs[i] = OutputPerWorkday(b, i) * work;
            for (int i = 0; i < d.inputs.Length; i++) e.Inputs[i] = d.inputs[i].amount * work;
            return e;
        }
    }
}
