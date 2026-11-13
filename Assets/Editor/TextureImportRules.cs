using UnityEditor;

namespace Firstlight.EditorTools
{
    /// <summary>
    /// Pixel-art material textures: sharp pixels (point filter), no compression (exact palette colours),
    /// mipmaps for distance, RGBA32 so every layer can be copied into one texture array.
    /// </summary>
    public sealed class TextureImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Textures/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = false;
            ti.mipmapEnabled = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.5f;
            ti.filterMode = UnityEngine.FilterMode.Point;
            ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.isReadable = false;
            var settings = ti.GetDefaultPlatformTextureSettings();
            settings.format = TextureImporterFormat.RGBA32;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(settings);
            var standalone = ti.GetPlatformTextureSettings("Standalone");
            standalone.overridden = true;
            standalone.format = TextureImporterFormat.RGBA32;
            standalone.maxTextureSize = 2048;
            ti.SetPlatformTextureSettings(standalone);
        }
    }
}
