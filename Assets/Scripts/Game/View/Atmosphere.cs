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
        /// <summary>0 = clear, 1 = snow falling thickly (the first days of winter, and showers after).</summary>
        public float Snowfall { get; private set; }
        /// <summary>World height the morning mist reaches up to (a little above the river).</summary>
        public float MistTop = 0.9f;

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
            var sunCol = Color.Lerp(WorldLighting.SunColour, new Color(1.0f, 0.66f, 0.40f), low * 0.85f);
            var moonCol = new Color(0.50f, 0.62f, 0.90f);
            _sun.color = Color.Lerp(moonCol, sunCol, day);
            _sun.intensity = math.lerp(0.42f, WorldLighting.SunIntensity - low * 0.2f, day);
            _sun.shadowStrength = math.lerp(0.5f, 0.82f, day);
            float winter = season == 3 ? 1f : 0f;
            // Snow falls while it settles in the first days of winter, then in showers that come and go.
            float intoWinter = seasonPos - 3f;
            Snowfall = season == 3 ? math.max(math.saturate(1.25f - intoWinter * 5f), math.saturate((Mathf.PerlinNoise(seasonPos * 23f, 0.37f) - 0.56f) * 6f)) : 0f;
            // Under falling snow the sun is veiled: weaker, with softer shadows.
            _sun.intensity *= 1f - 0.3f * Snowfall;
            _sun.shadowStrength *= 1f - 0.35f * Snowfall;
            // The sky: haze at the horizon, deeper overhead; orange low in the sky at dawn and dusk, a band of rose
            // lingering through twilight; deep blue at night.
            float twi = math.saturate(1f - math.abs(day - 0.45f) / 0.45f);
            var horizon = Color.Lerp(new Color(0.07f, 0.10f, 0.17f), Color.Lerp(new Color(0.70f, 0.77f, 0.83f), new Color(0.90f, 0.68f, 0.50f), low), day);
            horizon = Color.Lerp(horizon, new Color(0.38f, 0.37f, 0.46f), twi * 0.5f);
            var zenith = Color.Lerp(new Color(0.02f, 0.035f, 0.08f), Color.Lerp(new Color(0.33f, 0.50f, 0.74f), new Color(0.36f, 0.40f, 0.58f), low), day);
            zenith = Color.Lerp(zenith, new Color(0.20f, 0.22f, 0.40f), twi * 0.5f);
            if (season == 3) { horizon = Color.Lerp(horizon, new Color(0.80f, 0.83f, 0.86f) * (0.12f + 0.88f * day), 0.5f); zenith = Color.Lerp(zenith, new Color(0.55f, 0.62f, 0.72f) * (0.1f + 0.9f * day), 0.45f); }
            var glow = Color.Lerp(new Color(0.05f, 0.07f, 0.12f), Color.Lerp(new Color(0.32f, 0.27f, 0.18f), new Color(0.95f, 0.50f, 0.20f), low), day);
            var disc = Color.Lerp(new Color(1.6f, 1.7f, 1.9f), Color.Lerp(new Color(3.2f, 3.0f, 2.6f), new Color(3.4f, 2.0f, 1.0f), low), day);
            var cloud = Color.Lerp(new Color(0.10f, 0.12f, 0.18f), Color.Lerp(new Color(0.96f, 0.97f, 0.98f), new Color(0.98f, 0.76f, 0.62f), low), day);
            Look.SetSky(horizon, zenith, glow, math.lerp(5f, 3.5f, low * day), -_sun.transform.forward, disc, 1f - day, cloud, 0.9f);
            // Mist lies in the valley from before dawn into the morning, thickest in autumn; cloud shadows by day only.
            float dawn = math.saturate((dayFraction - 0.12f) / 0.06f) * math.saturate((0.36f - dayFraction) / 0.1f);
            float mist = dawn * (season == 2 ? 0.75f : season == 3 ? 0.5f : 0.55f) + (1f - day) * 0.12f;
            Look.SetWeather(mist, MistTop, 0.42f * day * (1f - low * 0.5f), season == 3 ? 0.7f : 1f);
            Look.Grade(day, low, winter * math.saturate(snow * 2f));
            WorldLighting.SetAmbient(Color.Lerp(new Color(0.20f, 0.26f, 0.40f), Color.Lerp(WorldLighting.AmbientSky, new Color(0.42f, 0.42f, 0.52f), low), day),
                                     Color.Lerp(new Color(0.11f, 0.12f, 0.17f), Color.Lerp(WorldLighting.AmbientGround, new Color(0.30f, 0.23f, 0.18f), low), day));
            Shader.SetGlobalFloat(GlowId, math.lerp(2.4f, 1.25f, day));

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
