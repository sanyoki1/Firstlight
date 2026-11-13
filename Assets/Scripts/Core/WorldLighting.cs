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
            data.renderPostProcessing = false;
            go.AddComponent<AudioListener>();
            return go.AddComponent<OrbitCamera>();
        }

        public static Light CreateSun()
        {
            var go = new GameObject("Sun");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1.0f, 0.95f, 0.86f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
            go.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            SetAmbient(new Color(0.50f, 0.55f, 0.60f), new Color(0.30f, 0.28f, 0.24f));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = SkyColour;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 600f;
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
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
                urp.shadowDistance = Mathf.Clamp(distance * 2.2f, 40f, 260f);
            RenderSettings.fogStartDistance = distance * 1.8f + 40f;
            RenderSettings.fogEndDistance = distance * 5f + 250f;
        }

        public static Material FlatMaterial(Color tint)
        {
            var m = new Material(Resources.Load<Shader>("Shaders/FlatLit")) { name = "FlatLit" };
            m.SetColor("_BaseColor", tint);
            return m;
        }
    }
}
