using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Auto-test check of the building panel's numbers (Settlement.EstimateWorkplace): through every game day it adds up
    /// each staffed workplace's estimate, hour by hour over the workday, and compares it with what the building really
    /// made and learned that day (Building.Made / Learned). The estimate is nominal (full workdays: no walking,
    /// carrying, waiting for inputs or for a full workplace to be emptied), so the ratio made / estimated sits below 1;
    /// what matters is that it does not move with difficulty, water or the Elders law, which the estimate includes.
    /// Only reads the settlement (the reports stay as they are); the results go to the log as "[Estimate]" lines.
    /// </summary>
    public sealed class EstimateCheck
    {
        sealed class Acc
        {
            public double Made0, Learned0, Est, EstK, Hours, WorkerHours, DryHours, ElderHours, LawHours;
            public double EstW, EstKW;           // the estimate for the workers who were at work (Activity.Working) at each hour
            public bool Clean = true;
        }

        readonly Settlement _s;
        readonly Dictionary<Building, Acc> _day = new();
        readonly Dictionary<Building, List<Citizen>> _crews = new();
        readonly List<Citizen> _working = new();
        int _dayNo = -1;
        // Samples: (def id, kind, ratio). Kinds: "out" every clean output day, "out-dry" most worker-hours without water,
        // "out-wet" none without water, "know" knowledge days, "know-elders" mostly elders without the law,
        // "know-elders-law" mostly elders with the Elders law all day.
        readonly List<(string def, string kind, double ratio)> _samples = new();

        public EstimateCheck(Settlement s) { _s = s; }

        /// <summary>Call after each game hour's tick.</summary>
        public void Hour()
        {
            var s = _s;
            if (s.NeedsCampfire) return;
            if (s.Day != _dayNo) { CloseDay(); _dayNo = s.Day; }
            var B = s.Data.Balance;
            float f = s.DayFraction, h = 1f / 24f;
            double overlap = Math.Max(0, Math.Min(f, B.workdayEnd) - Math.Max(f - h, B.workdayStart));
            if (overlap <= 0) return;
            double w = overlap / (B.workdayEnd - B.workdayStart);   // share of a full workday this hour
            _crews.Clear();
            foreach (var c in s.Citizens)
                if (!c.Gone && c.Job != null)
                {
                    if (!_crews.TryGetValue(c.Job, out var list)) _crews[c.Job] = list = new List<Citizen>();
                    list.Add(c);
                }
            bool law = s.LawActive("elders_settle");
            foreach (var kv in _crews)
            {
                var b = kv.Key;
                if (!b.Def.IsWorkplace || b.Def.crop) continue;
                if (!_day.TryGetValue(b, out var a)) continue;   // staffed since the day began: counted from tomorrow
                var e = _s.EstimateWorkplace(b, kv.Value);
                a.Est += e.OutputTotal * w;
                a.EstK += e.Knowledge * w;
                _working.Clear();
                foreach (var c in kv.Value) if (c.Activity == Activity.Working) _working.Add(c);
                if (_working.Count > 0)
                {
                    var ew = _s.EstimateWorkplace(b, _working);
                    a.EstW += ew.OutputTotal * w;
                    a.EstKW += ew.Knowledge * w;
                    a.WorkerHours += ew.Workers * w;
                    a.DryHours += ew.NoWater * w;
                    a.ElderHours += ew.Elders * w;
                }
                a.Hours += w;
                if (law) a.LawHours += w;
                if (!b.Active || b.Paused || b.Flooded || b.UpgradeTo != null || b.Status.Length > 0) a.Clean = false;
            }
        }

        void CloseDay()
        {
            foreach (var kv in _day)
            {
                var b = kv.Key; var a = kv.Value;
                if (!b.Alive || a.Hours < 0.999) continue;   // staffed for only part of the workday
                string id = b.Def.id;
                double made = b.Made - a.Made0, learned = b.Learned - a.Learned0;
                // Nominal: against the whole crew at full workdays (what the panel shows).
                if (a.Est > 0.5) _samples.Add((id, "nominal out", made / a.Est));
                if (a.EstK > 0.05) _samples.Add((id, "nominal know", learned / a.EstK));
                // Working: against the workers seen at work each hour. Walking and carrying drop out, so the factors the
                // estimate multiplies in (difficulty, water, laws, mood, tools) would show here as a shifted ratio.
                if (a.Clean && a.EstW > 0.5)
                {
                    double r = made / a.EstW;
                    _samples.Add((id, "working out", r));
                    if (a.DryHours >= 0.5 * a.WorkerHours) _samples.Add((id, "working out-dry", r));
                    else if (a.DryHours == 0) _samples.Add((id, "working out-wet", r));
                }
                if (a.EstKW > 0.05)
                {
                    double r = learned / a.EstKW;
                    _samples.Add((id, "working know", r));
                    if (a.ElderHours >= 0.5 * a.WorkerHours) _samples.Add((id, a.LawHours >= a.Hours - 1e-6 ? "working know-elders-law" : a.LawHours == 0 ? "working know-elders" : "working know-elders-mixed", r));
                    else if (a.ElderHours == 0) _samples.Add((id, "working know-noelders", r));
                    if (a.DryHours >= 0.5 * a.WorkerHours) _samples.Add((id, "working know-dry", r));
                    else if (a.DryHours == 0) _samples.Add((id, "working know-wet", r));
                }
            }
            _day.Clear();
            foreach (var b in _s.Buildings)
                if (b.Active && b.Def.IsWorkplace && !b.Def.crop) _day[b] = new Acc { Made0 = b.Made, Learned0 = b.Learned };
        }

        /// <summary>Logs the ratios made / estimated: per kind of sample over all workplaces, then per building and kind.</summary>
        public void Summary(string label)
        {
            static string Stats(List<double> r)
            {
                r.Sort();
                double Q(double p) => r[(int)Math.Round(p * (r.Count - 1))];
                return $"n {r.Count}, median {Q(0.5):F3}, p10 {Q(0.1):F3}, p25 {Q(0.25):F3}, p75 {Q(0.75):F3}, p90 {Q(0.9):F3}";
            }
            var sb = new System.Text.StringBuilder($"[Estimate] {label}: made / estimated a day (estimate at full workdays; walking and carrying make it < 1)");
            foreach (var g in _samples.GroupBy(x => x.kind).OrderBy(g => g.Key, StringComparer.Ordinal))
                sb.Append($"\n  all {g.Key}: {Stats(g.Select(x => x.ratio).ToList())}");
            foreach (var g in _samples.GroupBy(x => (x.def, x.kind)).OrderBy(g => g.Key.def, StringComparer.Ordinal).ThenBy(g => g.Key.kind, StringComparer.Ordinal))
                if (g.Count() >= 5) sb.Append($"\n  {g.Key.def} {g.Key.kind}: {Stats(g.Select(x => x.ratio).ToList())}");
            Debug.Log(sb.ToString());
        }
    }
}
