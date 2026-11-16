using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Firstlight.Core
{
    /// <summary>Creates the camera, sun and ambient settings shared by every Firstlight scene.</summary>
    public static class WorldLighting
    {
        static readonly int AmbientSkyId = Shader.PropertyToID("_FL_AmbientSky");
        static readonly int AmbientGroundId = Shader.PropertyToID("_FL_AmbientGround");

        public static readonly Color SkyColour = new(0.62f, 0.68f, 0.72f);
        // Full daylight: a warm sun against cool light from the sky, so lit and shaded sides differ in colour as well as brightness.
        public static readonly Color SunColour = new(1.0f, 0.93f, 0.80f);
        public const float SunIntensity = 1.42f;
        public static readonly Color AmbientSky = new(0.40f, 0.49f, 0.62f), AmbientGround = new(0.27f, 0.25f, 0.22f);

        public static OrbitCamera CreateCamera()
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColour;
            cam.fieldOfView = 40f;
            cam.allowMSAA = true;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderShadows = true;
            go.AddComponent<AudioListener>();
            Look.Init(cam);       // sky, haze, post effects
            return go.AddComponent<OrbitCamera>();
        }

        public static Light CreateSun()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = SunColour;
            light.intensity = SunIntensity;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.82f;
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            SetAmbient(AmbientSky, AmbientGround);
            return light;
        }

        public static void SetAmbient(Color sky, Color ground)
        {
            Shader.SetGlobalColor(AmbientSkyId, sky.linear);
            Shader.SetGlobalColor(AmbientGroundId, ground.linear);
        }

        /// <summary>Keeps shadow distance and fog sensible for the current zoom.</summary>
        public static void UpdateForZoom(float distance)
        {
            Look.UpdateForZoom(distance);
        }

        public static Material FlatMaterial(Color tint)
        {
            var m = new Material(Resources.Load<Shader>("Shaders/FlatLit")) { name = "FlatLit" };
            m.SetColor("_BaseColor", tint);
            return m;
        }
    }
}
