using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Firstlight.Core
{
    /// <summary>
    /// Pixel-art materials (grass, bark, stone...) packed into one Texture2DArray per pixel density.
    /// Shaders apply them at a fixed number of texture pixels per metre on every surface, which is what
    /// makes the world read as pixel art (the Minecraft rule), whatever the shape.
    /// Layer names and sprite sizes come from StreamingAssets/Data/materials.json (written by
    /// tools/textures/make_textures.py).
    /// </summary>
    public static class MaterialLibrary
    {
        public const float MetresPerUnit = 2f;

        [Serializable] class SpriteEntry { public string name; public int[] d8, d16, d32, d64, d128; }
        [Serializable] class Manifest { public string[] layers; public int[] densities; public int[] tileTexels; public SpriteEntry[] sprites; }

        static Manifest _m;
        static readonly Dictionary<string, int> _layer = new();
        static readonly Dictionary<int, Texture2DArray> _arrays = new();

        public static int Density { get; private set; } = 16;
        public static int TileTexels { get; private set; } = 64;
        public static int[] Densities => _m?.densities ?? new[] { 8, 16, 32 };
        public static event Action DensityChanged;

        static readonly int TexId = Shader.PropertyToID("_FL_Tex");
        static readonly int TpuId = Shader.PropertyToID("_FL_TexelsPerUnit");
        static readonly int TileId = Shader.PropertyToID("_FL_TileTexels");

        public static void Load()
        {
            if (_m != null) return;
            string path = Path.Combine(Application.streamingAssetsPath, "Data", "materials.json");
            _m = JsonUtility.FromJson<Manifest>(File.ReadAllText(path));
            for (int i = 0; i < _m.layers.Length; i++) _layer[_m.layers[i]] = i;
            Shader.SetGlobalFloat("_FL_LayerDirt", Layer("dirt"));
            Shader.SetGlobalFloat("_FL_LayerGrass", Layer("grass"));
            Shader.SetGlobalFloat("_FL_LayerLitter", Layer("litter"));
            Shader.SetGlobalFloat("_FL_LayerSnow", Layer("snow"));
        }

        public static int Layer(string name)
        {
            Load();
            if (_layer.TryGetValue(name, out int i)) return i;
            Debug.LogWarning($"[Materials] unknown layer {name}");
            return 0;
        }

        public static void SetDensity(int d)
        {
            Load();
            int k = Array.IndexOf(_m.densities, d);
            if (k < 0) { d = 16; k = Array.IndexOf(_m.densities, d); }
            Density = d;
            TileTexels = _m.tileTexels[k];
            if (!_arrays.TryGetValue(d, out var arr))
            {
                arr = BuildArray(d, TileTexels);
                _arrays[d] = arr;
            }
            Shader.SetGlobalTexture(TexId, arr);
            Shader.SetGlobalFloat(TpuId, d * MetresPerUnit);
            Shader.SetGlobalFloat(TileId, TileTexels);
            DensityChanged?.Invoke();
        }

        static Texture2DArray BuildArray(int d, int size)
        {
            var first = Resources.Load<Texture2D>($"Textures/d{d}/{_m.layers[0]}");
            int mips = first != null ? first.mipmapCount : 1;
            var arr = new Texture2DArray(size, size, _m.layers.Length, TextureFormat.RGBA32, mips, false)
            {
                name = $"Materials_d{d}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, anisoLevel = 0,
            };
            for (int i = 0; i < _m.layers.Length; i++)
            {
                var tex = Resources.Load<Texture2D>($"Textures/d{d}/{_m.layers[i]}");
                if (tex == null || tex.width != size || tex.mipmapCount != mips || tex.format != TextureFormat.RGBA32)
                {
                    Debug.LogError($"[Materials] layer {_m.layers[i]} (d{d}) missing or wrong size/format: {(tex == null ? "null" : $"{tex.width} {tex.format} mips {tex.mipmapCount}")}");
                    continue;
                }
                Graphics.CopyTexture(tex, 0, arr, i);
            }
            return arr;
        }

        /// <summary>UV rectangle of a sprite drawn at the top-left of its layer, at the current density.</summary>
        public static Rect SpriteUV(string name)
        {
            var size = SpritePixels(name);
            float t = TileTexels;
            return new Rect(0, 1f - size.y / t, size.x / t, size.y / t);
        }

        public static Vector2Int SpritePixels(string name)
        {
            Load();
            foreach (var s in _m.sprites)
            {
                if (s.name != name) continue;
                int[] v = Density == 8 ? s.d8 : Density == 32 ? s.d32 : Density == 64 ? s.d64 : Density == 128 ? s.d128 : s.d16;
                return new Vector2Int(v[0], v[1]);
            }
            return new Vector2Int(8, 8);
        }

        /// <summary>World size (units) of a sprite at its true pixel density.</summary>
        public static Vector2 SpriteUnits(string name)
        {
            var p = SpritePixels(name);
            return new Vector2(p.x, p.y) / (Density * MetresPerUnit);
        }
    }
}
