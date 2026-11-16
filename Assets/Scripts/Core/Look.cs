using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Firstlight.Core
{
    /// <summary>
    /// The look of the picture, shared by every Firstlight scene: the sky, the haze the land fades into, cloud shadows,
    /// wind, and the post effects (ambient occlusion, bloom, tone mapping, colour grading, a soft vignette; depth of
    /// field in the cinematic view). Everything is set through shader globals and one global volume made in code.
    /// The player's "Visual effects" setting turns the post effects and the ambient occlusion off (<see cref="SetEffects"/>);
    /// sky, haze, clouds and wind are part of the shaders and always on.
    /// </summary>
    public static class Look
    {
        static readonly int FogColorId = Shader.PropertyToID("_FL_FogColor"), FogSunId = Shader.PropertyToID("_FL_FogSun");
        static readonly int FogParamsId = Shader.PropertyToID("_FL_FogParams"), FogHeightId = Shader.PropertyToID("_FL_FogHeight");
        static readonly int MapBoundsId = Shader.PropertyToID("_FL_MapBounds"), SunDirId = Shader.PropertyToID("_FL_SunDir");
        static readonly int CloudId = Shader.PropertyToID("_FL_Cloud"), WindId = Shader.PropertyToID("_FL_Wind");
        static readonly int ZenithId = Shader.PropertyToID("_FL_SkyZenith"), WaterSkyId = Shader.PropertyToID("_FL_WaterSky");
        static readonly int SunDiscId = Shader.PropertyToID("_FL_SunDisc"), SkyCloudId = Shader.PropertyToID("_FL_SkyCloud"), StarsId = Shader.PropertyToID("_FL_Stars");

        /// <summary>How far beyond the map's border the land is drawn before it is lost in the haze (world units).</summary>
        public const float Outskirts = 84f;

        public static bool Effects { get; private set; } = true;

        static Camera _cam;
        static Volume _volume;
        static Bloom _bloom;
        static Tonemapping _tone;
        static ColorAdjustments _adj;
        static WhiteBalance _wb;
        static Vignette _vig;
        static SplitToning _split;
        static DepthOfField _dof;
        static Material _sky;
        static float _mist, _mistTop = 0.5f, _cloudDark, _windStrength = 1f, _cloudTime, _windTime;
        static Vector2 _cloudOffset;

        /// <summary>Sky, volume and defaults for a new scene's camera (a clear summer day until something sets otherwise).</summary>
        public static void Init(Camera cam)
        {
            _cam = cam;
            if (_sky == null) _sky = new Material(Resources.Load<Shader>("Shaders/Sky")) { name = "Sky" };
            RenderSettings.skybox = _sky;
            RenderSettings.fog = false;       // the shaders do their own haze (FL_Fog)
            cam.clearFlags = CameraClearFlags.Skybox;

            var go = new GameObject("Look Volume");
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            _volume.profile = p;
            _tone = p.Add<Tonemapping>(true);
            _tone.mode.value = TonemappingMode.Neutral;
            _bloom = p.Add<Bloom>(true);
            _bloom.threshold.value = 1.0f; _bloom.intensity.value = 0.3f; _bloom.scatter.value = 0.62f;
            _bloom.highQualityFiltering.value = true;
            _adj = p.Add<ColorAdjustments>(true);
            _wb = p.Add<WhiteBalance>(true);
            _vig = p.Add<Vignette>(true);
            _vig.intensity.value = 0.2f; _vig.smoothness.value = 0.45f;
            _split = p.Add<SplitToning>(true);
            _dof = p.Add<DepthOfField>(true);
            _dof.mode.value = DepthOfFieldMode.Off;

            SetSky(new Color(0.70f, 0.77f, 0.83f), new Color(0.33f, 0.50f, 0.74f), new Color(0.30f, 0.26f, 0.18f), 6f,
                   new Vector3(0.4f, 0.74f, -0.54f), new Color(3f, 2.8f, 2.4f), 0f, new Color(0.95f, 0.96f, 0.97f), 0.9f);
            SetWeather(0f, 0.5f, 0.38f, 1f);
            SetMap(256, 256);
            Grade(1f, 0f, 0f);
            Cinematic(0f, 40f);
            UpdateForZoom(45f);
            SetEffects(Effects);
        }

        /// <summary>Post effects and ambient occlusion on or off (the player's setting).</summary>
        public static void SetEffects(bool on)
        {
            Effects = on;
            if (_cam == null) return;
            var data = _cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = on;
            // Renderer 0 has the ambient occlusion feature, renderer 1 (if the project has it) is the same without.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp && urp.rendererDataList.Length > 1)
                data.SetRenderer(on ? 0 : 1);
        }

        /// <summary>The map's size: beyond its border the land fades into the haze.</summary>
        public static void SetMap(float w, float h) => Shader.SetGlobalVector(MapBoundsId, new Vector4(w, h, 1f / (Outskirts * 0.4f), 0));

        /// <summary>
        /// The sky's colours (sRGB): haze at the horizon, the zenith, the glow round the sun and how tight it is, the
        /// direction to the sun or moon, its disc, the stars (0..1), and the clouds' colour and cover.
        /// </summary>
        public static void SetSky(Color horizon, Color zenith, Color sunGlow, float glowTightness, Vector3 toSun, Color disc, float stars, Color cloud, float cloudCover)
        {
            Shader.SetGlobalColor(FogColorId, horizon.linear);
            Shader.SetGlobalColor(ZenithId, zenith.linear);
            var g = sunGlow.linear; g.a = glowTightness;
            Shader.SetGlobalVector(FogSunId, g);
            Shader.SetGlobalVector(SunDirId, toSun.normalized);
            var d = disc.linear; d.a = 0.9994f;
            Shader.SetGlobalVector(SunDiscId, d);
            Shader.SetGlobalFloat(StarsId, stars);
            var c = cloud.linear; c.a = cloudCover;
            Shader.SetGlobalVector(SkyCloudId, c);
            // The water mirrors a sky between horizon and zenith; glints only while the sun (not the moon) is up.
            var w = Color.Lerp(horizon, zenith, 0.35f).linear; w.a = 1f - stars;
            Shader.SetGlobalVector(WaterSkyId, w);
        }

        /// <summary>Low mist (0..1) up to a height, how dark the cloud shadows are (0..1), and the wind's strength.</summary>
        public static void SetWeather(float mist, float mistTop, float cloudShadow, float wind)
        {
            _mist = mist; _mistTop = mistTop; _cloudDark = cloudShadow; _windStrength = wind;
        }

        /// <summary>Once a frame: clouds drift, the wind blows, shadows and haze follow the zoom.</summary>
        public static void UpdateForZoom(float distance)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = Mathf.Clamp(distance * 2.4f, 45f, 300f);
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _cloudTime += dt; _windTime += dt;
            _cloudOffset = new Vector2(_cloudTime * 0.011f, _cloudTime * 0.004f);
            Shader.SetGlobalVector(CloudId, new Vector4(_cloudOffset.x, _cloudOffset.y, 1f / 46f, _cloudDark));
            Shader.SetGlobalVector(WindId, new Vector4(0.86f, 0.5f, _windStrength, _windTime));
            // Haze starts beyond the part of the land in view at this zoom and is never complete inside the map.
            Shader.SetGlobalVector(FogParamsId, new Vector4(distance * 1.25f + 25f, 1f / (distance * 3.2f + 190f), 0.8f, _mist));
            Shader.SetGlobalVector(FogHeightId, new Vector4(_mistTop, 1f / 1.6f, 0, 0));
        }

        /// <summary>
        /// The grade for the time of day: <paramref name="day"/> 0 night .. 1 day, <paramref name="low"/> 0..1 how low
        /// the sun is while it is up (golden hour), <paramref name="cold"/> 0..1 winter.
        /// </summary>
        public static void Grade(float day, float low, float cold)
        {
            if (_adj == null) return;
            float golden = low * day, night = 1f - day;
            _adj.contrast.value = Mathf.Lerp(14f, 8f, night) + golden * 4f;
            _adj.saturation.value = 2f + golden * 12f - night * 12f - cold * 6f;
            _adj.postExposure.value = 0.05f + night * 0.35f;
            _wb.temperature.value = 2f + golden * 16f - night * 18f - cold * 10f;
            _wb.tint.value = golden * 5f;
            // Cool shadows against warm lights by day; at night both sides lean blue.
            _split.shadows.value = Color.Lerp(new Color(0.44f, 0.48f, 0.60f), new Color(0.40f, 0.45f, 0.62f), night);
            _split.highlights.value = Color.Lerp(Color.Lerp(new Color(0.58f, 0.54f, 0.46f), new Color(0.68f, 0.52f, 0.36f), golden), new Color(0.50f, 0.52f, 0.58f), night);
            _split.balance.value = 10f;
            _bloom.intensity.value = 0.28f + golden * 0.2f + night * 0.35f - cold * day * 0.12f;
            _bloom.threshold.value = Mathf.Lerp(1.0f, 0.85f, night) + cold * day * 0.25f;
            _vig.intensity.value = 0.2f + night * 0.08f + _cine * 0.12f;
        }

        static float _cine;

        /// <summary>The cinematic view (0..1): depth of field round the camera's focus, a stronger vignette.</summary>
        public static void Cinematic(float amount, float focusDistance)
        {
            _cine = amount;
            if (_dof == null) return;
            if (amount <= 0.01f) { _dof.mode.value = DepthOfFieldMode.Off; return; }
            _dof.mode.value = DepthOfFieldMode.Bokeh;
            _dof.focusDistance.value = Mathf.Max(0.5f, focusDistance);
            // A long lens wide open: the blur grows with the zoom so near and far views both show it.
            _dof.focalLength.value = Mathf.Lerp(40f, 115f, amount);
            _dof.aperture.value = Mathf.Clamp(focusDistance * 0.05f, 1f, 5.6f);
            _dof.bladeCount.value = 6;
        }
    }
}
