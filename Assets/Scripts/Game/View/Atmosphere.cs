using Firstlight.Core;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Time of day and season made visible, cheaply: the sun's angle and colour over the day (a dim blue moonlight
    /// at night, never black, so the town stays readable), sky and fog colour, fires glowing brighter in the dark,
    /// grass and leaves tinted by season, and snow lying in winter. All through a few shader globals.
    /// </summary>
    public sealed class Atmosphere
    {
        readonly Light _sun;
        readonly Camera _cam;
        static readonly int GrassId = Shader.PropertyToID("_FL_SeasonGrass"), LeafId = Shader.PropertyToID("_FL_SeasonLeaf");
        static readonly int NeedleId = Shader.PropertyToID("_FL_SeasonNeedle"), SnowId = Shader.PropertyToID("_FL_Snow"), GlowId = Shader.PropertyToID("_FL_Glow");

        /// <summary>0 = deep night, 1 = full day (for the HUD and the view).</summary>
        public float Daylight { get; private set; } = 1f;
        public float Snow { get; private set; }

        // Spring, summer, autumn, winter.
        static readonly Vector3[] Grass = { new(1.0f, 1.05f, 0.95f), new(1.03f, 1.0f, 0.86f), new(1.14f, 0.97f, 0.7f), new(0.98f, 0.9f, 0.78f) };
        static readonly Vector3[] Leaf = { new(0.98f, 1.06f, 0.95f), new(0.98f, 1.0f, 0.9f), new(1.5f, 0.95f, 0.5f), new(1.2f, 0.85f, 0.62f) };
        static readonly Vector3[] Needle = { new(1f, 1.02f, 1f), new(1f, 1f, 0.95f), new(0.97f, 0.97f, 0.9f), new(0.86f, 0.92f, 0.94f) };

        public Atmosphere(Light sun, Camera cam) { _sun = sun; _cam = cam; }

        /// <summary>Sets lighting for a time of day (0..1, the sim's DayFraction) and a season position (0..4, continuous).</summary>
        public void Apply(float dayFraction, float seasonPos, float snow)
        {
            // Day runs from about 0.2 (sunrise) to 0.82 (sunset); dawn and dusk blend over a few hours.
            const float rise = 0.2f, set = 0.82f, blend = 0.05f;
            float day = math.saturate(math.min((dayFraction - (rise - blend)) / (2 * blend), ((set + blend) - dayFraction) / (2 * blend)));
            day = day * day * (3 - 2 * day);
            Daylight = day;
            float t = math.saturate((dayFraction - rise) / (set - rise));
            int season = (int)math.floor(seasonPos) & 3;
            float maxElev = season == 1 ? 58f : season == 3 ? 34f : 47f;
            float elev = math.lerp(8f, maxElev, math.sin(t * math.PI));
            float az = math.lerp(-75f, 75f, t) - 35f;
            var sunRot = Quaternion.Euler(elev, az, 0);
            var moonRot = Quaternion.Euler(52f, 150f, 0);
            _sun.transform.rotation = Quaternion.Slerp(moonRot, sunRot, day);
            float low = 1f - math.saturate((elev - 8f) / 22f);                 // warm light near the horizon
            var sunCol = Color.Lerp(new Color(1.0f, 0.95f, 0.86f), new Color(1.0f, 0.74f, 0.52f), low * 0.8f);
            var moonCol = new Color(0.55f, 0.64f, 0.85f);
            _sun.color = Color.Lerp(moonCol, sunCol, day);
            _sun.intensity = math.lerp(0.38f, 1.25f - low * 0.25f, day);
            _sun.shadowStrength = math.lerp(0.45f, 0.75f, day);
            var skyDay = WorldLighting.SkyColour;
            var dusk = new Color(0.64f, 0.52f, 0.47f);
            var night = new Color(0.13f, 0.16f, 0.23f);
            var sky = Color.Lerp(night, Color.Lerp(dusk, skyDay, 1f - low), day);
            if (season == 3) sky = Color.Lerp(sky, new Color(0.7f, 0.73f, 0.76f) * (0.3f + 0.7f * day), 0.35f);
            _cam.backgroundColor = sky;
            RenderSettings.fogColor = sky;
            WorldLighting.SetAmbient(Color.Lerp(new Color(0.22f, 0.26f, 0.36f), new Color(0.50f, 0.55f, 0.60f), day),
                                     Color.Lerp(new Color(0.12f, 0.12f, 0.15f), new Color(0.30f, 0.28f, 0.24f), day));
            Shader.SetGlobalFloat(GlowId, math.lerp(1.9f, 1.05f, day));

            // Season tints blend into the next season over its last couple of days.
            float f = seasonPos - math.floor(seasonPos);
            float k = math.saturate((f - 0.75f) / 0.25f);
            int next = (season + 1) & 3;
            Shader.SetGlobalVector(GrassId, Vector3.Lerp(Grass[season], Grass[next], k));
            Shader.SetGlobalVector(LeafId, Vector3.Lerp(Leaf[season], Leaf[next], k));
            Shader.SetGlobalVector(NeedleId, Vector3.Lerp(Needle[season], Needle[next], k));
            Snow = snow;
            Shader.SetGlobalFloat(SnowId, snow);
        }

        /// <summary>From the settlement: snow settles over the first days of winter and melts in the first days of spring.</summary>
        public void Apply(Settlement s)
        {
            if (s == null || s.NeedsCampfire) { Apply(0.38f, 0.3f, 0f); return; }
            int dps = s.Data.Balance.daysPerSeason;
            float seasonPos = (s.Day % (dps * 4) + s.DayFraction) / dps;
            float inSeason = s.DayOfSeason + s.DayFraction;
            float snow = s.Season == 3 ? math.saturate(inSeason / 2.5f) * 0.9f : s.Season == 0 && s.Day >= dps * 4 ? math.saturate(1f - inSeason / 2f) * 0.9f : 0f;
            Apply(s.DayFraction, seasonPos, snow);
        }
    }
}
