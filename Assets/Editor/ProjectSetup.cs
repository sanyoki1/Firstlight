using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Firstlight.EditorTools
{
    /// <summary>
    /// One-click (or command-line) project setup and build.
    ///   Unity.exe -batchmode -projectPath . -executeMethod Firstlight.EditorTools.ProjectSetup.Setup -quit
    ///   Unity.exe -batchmode -projectPath . -executeMethod Firstlight.EditorTools.ProjectSetup.BuildWindows -quit
    /// </summary>
    public static class ProjectSetup
    {
        const string SettingsDir = "Assets/Settings";
        const string UrpPath = SettingsDir + "/URP.asset";
        const string RendererPath = SettingsDir + "/URP_Renderer.asset";
        const string PlainRendererPath = SettingsDir + "/URP_Renderer_Plain.asset";
        const string PostDataPath = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";
        static readonly string[] Scenes = { "Assets/Scenes/Game.unity", "Assets/Scenes/StressTest.unity" };

        [MenuItem("Firstlight/Setup Project")]
        public static void Setup()
        {
            Directory.CreateDirectory(SettingsDir);
            var urp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (urp == null)
            {
                var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                urp = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(urp, UrpPath);
            }

            var so = new SerializedObject(urp);
            SetBool(so, "m_MainLightShadowsSupported", true);
            SetInt(so, "m_MainLightShadowmapResolution", 4096);
            SetFloat(so, "m_ShadowDistance", 150f);
            SetInt(so, "m_ShadowCascadeCount", 4);
            SetBool(so, "m_SoftShadowsSupported", true);
            SetInt(so, "m_SoftShadowQuality", 3);
            SetInt(so, "m_MSAA", 4);
            SetBool(so, "m_SupportsHDR", true);
            SetBool(so, "m_AdditionalLightShadowsSupported", false);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            SetupRenderers(urp);

            GraphicsSettings.defaultRenderPipeline = urp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urp;
            }
            QualitySettings.SetQualityLevel(current, false);

            PlayerSettings.companyName = "Firstlight";
            PlayerSettings.productName = "Firstlight";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.usePlayerLog = true;

            // Use the new Input System only.
            var playerSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (playerSettings != null)
            {
                var pso = new SerializedObject(playerSettings);
                var prop = pso.FindProperty("activeInputHandler");
                if (prop != null) { prop.intValue = 1; pso.ApplyModifiedPropertiesWithoutUndo(); }
            }

            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(Scenes[0])) CreateScene(Scenes[0], typeof(Bootstrap));
            if (!File.Exists(Scenes[1])) CreateScene(Scenes[1], typeof(StressTest.StressTestController));
            EditorBuildSettings.scenes = Scenes.Select(s => new EditorBuildSettingsScene(s, true)).ToArray();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Setup] Firstlight project configured.");
        }

        /// <summary>
        /// Renderer 0: post-processing and screen-space ambient occlusion (the look, see Core/Look.cs). Renderer 1: the
        /// same without the occlusion, used when the player turns "Visual effects" off.
        /// </summary>
        static void SetupRenderers(UniversalRenderPipelineAsset urp)
        {
            var post = AssetDatabase.LoadAssetAtPath<PostProcessData>(PostDataPath);
            if (post == null) Debug.LogWarning($"[Setup] {PostDataPath} not found: no post effects");
            var main = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (main == null) { Debug.LogWarning($"[Setup] {RendererPath} not found"); return; }
            main.postProcessData = post;

            var ssao = main.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if (ssao == null)
            {
                ssao = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ssao.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(ssao, main);
                main.rendererFeatures.Add(ssao);
            }
            // Depth only (the flat-shaded meshes' true normals come out of the depth), a radius of about a hut's wall,
            // applied in the shaders (FlatShade) rather than over the finished picture.
            var fso = new SerializedObject(ssao);
            SetInt(fso, "m_Settings.Source", 0);
            SetInt(fso, "m_Settings.NormalSamples", 1);
            SetInt(fso, "m_Settings.AOMethod", 0);
            SetInt(fso, "m_Settings.Samples", 1);
            SetInt(fso, "m_Settings.BlurQuality", 0);
            SetBool(fso, "m_Settings.Downsample", false);
            SetBool(fso, "m_Settings.AfterOpaque", false);
            SetFloat(fso, "m_Settings.Intensity", 1.6f);
            SetFloat(fso, "m_Settings.Radius", 0.45f);
            SetFloat(fso, "m_Settings.DirectLightingStrength", 0.3f);
            SetFloat(fso, "m_Settings.Falloff", 420f);
            fso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(ssao);
            typeof(ScriptableRendererData).GetMethod("ValidateRendererFeatures", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(main, null);
            EditorUtility.SetDirty(main);

            var plain = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PlainRendererPath);
            if (plain == null)
            {
                plain = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(plain, PlainRendererPath);
            }
            plain.postProcessData = post;
            EditorUtility.SetDirty(plain);

            var uso = new SerializedObject(urp);
            var list = uso.FindProperty("m_RendererDataList");
            list.arraySize = 2;
            list.GetArrayElementAtIndex(0).objectReferenceValue = main;
            list.GetArrayElementAtIndex(1).objectReferenceValue = plain;
            SetInt(uso, "m_DefaultRendererIndex", 0);
            uso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
        }

        static void CreateScene(string path, Type component)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Bootstrap").AddComponent(component);
            EditorSceneManager.SaveScene(scene, path);
        }

        [MenuItem("Firstlight/Build Windows")]
        public static void BuildWindows()
        {
            Setup();
            // -buildout PATH: build somewhere else (a test build while Build/ is busy with a gate run).
            var args = System.Environment.GetCommandLineArgs();
            int bo = System.Array.IndexOf(args, "-buildout");
            var options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = bo >= 0 && bo + 1 < args.Length ? args[bo + 1] : "Build/Firstlight.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[Build] {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime}");
            if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }

        static void SetBool(SerializedObject so, string name, bool v) { var p = so.FindProperty(name); if (p != null) p.boolValue = v; else Debug.LogWarning($"[Setup] URP field {name} not found"); }
        static void SetInt(SerializedObject so, string name, int v) { var p = so.FindProperty(name); if (p != null) p.intValue = v; else Debug.LogWarning($"[Setup] URP field {name} not found"); }
        static void SetFloat(SerializedObject so, string name, float v) { var p = so.FindProperty(name); if (p != null) p.floatValue = v; else Debug.LogWarning($"[Setup] URP field {name} not found"); }
    }
}
