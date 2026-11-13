using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Firstlight.Core
{
    /// <summary>Colour helpers. Authoring is in sRGB hex; meshes store linear colours.</summary>
    public static class Pal
    {
        public static Color Hex(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return Color.magenta;
            if (hex[0] != '#') hex = "#" + hex;
            return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;
        }

        /// <summary>Pack an sRGB colour into RGBA8 for instance buffers (the shader converts to linear).</summary>
        public static uint Pack(Color c)
        {
            uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(c.a * 255f), 0, 255);
            return r | (g << 8) | (b << 16) | (a << 24);
        }

        public static Color Vary(Color c, float amount, System.Random rng)
        {
            float k = 1f + ((float)rng.NextDouble() * 2f - 1f) * amount;
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }

    /// <summary>
    /// Builds flat-shaded low-poly meshes from simple primitives. Every triangle has its own vertices, so
    /// normals are per face. Primitives are unit-sized (base on y=0) and placed with a matrix.
    /// Vertex colour alpha = how much an instance colour may tint the vertex (InstancedFlat shader).
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> _v = new();
        readonly List<Vector3> _n = new();
        readonly List<Color> _c = new();
        readonly List<int> _t = new();

        public int VertexCount => _v.Count;

        public void Clear() { _v.Clear(); _n.Clear(); _c.Clear(); _t.Clear(); }

        /// <summary>Adds a triangle wound so its face points away from <paramref name="inside"/>.</summary>
        public void TriOut(Vector3 a, Vector3 b, Vector3 c, Vector3 inside, Color col)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, (a + b + c) / 3f - inside) < 0) { (b, c) = (c, b); n = -n; }
            Tri(a, b, c, n.normalized, col);
        }

        /// <summary>Adds a clockwise (front-facing) triangle.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col)
        {
            Tri(a, b, c, Vector3.Cross(b - a, c - a).normalized, col);
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Color col)
        {
            int i = _v.Count;
            Color lin = col.linear; lin.a = col.a;
            _v.Add(a); _v.Add(b); _v.Add(c);
            _n.Add(n); _n.Add(n); _n.Add(n);
            _c.Add(lin); _c.Add(lin); _c.Add(lin);
            _t.Add(i); _t.Add(i + 1); _t.Add(i + 2);
        }

        public void QuadOut(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color col)
        {
            TriOut(a, b, c, inside, col);
            TriOut(a, c, d, inside, col);
        }

        // ---------------------------------------------------------------- primitives

        public void Box(Matrix4x4 m, Color col)
        {
            Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
            Vector3 inside = P(0, 0.5f, 0);
            Vector3 a = P(-.5f, 0, -.5f), b = P(.5f, 0, -.5f), c = P(.5f, 0, .5f), d = P(-.5f, 0, .5f);
            Vector3 e = P(-.5f, 1, -.5f), f = P(.5f, 1, -.5f), g = P(.5f, 1, .5f), h = P(-.5f, 1, .5f);
            QuadOut(e, f, g, h, inside, col);
            QuadOut(a, b, f, e, inside, col);
            QuadOut(b, c, g, f, inside, col);
            QuadOut(c, d, h, g, inside, col);
            QuadOut(d, a, e, h, inside, col);
            QuadOut(a, b, c, d, inside, col);
        }

        /// <summary>Cylinder / truncated cone. Radii are relative to the unit footprint (0.5 = full).</summary>
        public void Frustum(Matrix4x4 m, int sides, float rBottom, float rTop, Color col, bool caps = true)
        {
            Vector3 inside = m.MultiplyPoint3x4(new Vector3(0, 0.5f, 0));
            Vector3 cb = m.MultiplyPoint3x4(Vector3.zero), ct = m.MultiplyPoint3x4(Vector3.up);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 b0 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * rBottom, 0, Mathf.Sin(a0) * rBottom));
                Vector3 b1 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * rBottom, 0, Mathf.Sin(a1) * rBottom));
                Vector3 t0 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * rTop, 1, Mathf.Sin(a0) * rTop));
                Vector3 t1 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * rTop, 1, Mathf.Sin(a1) * rTop));
                if (rTop > 0.0001f) QuadOut(b0, b1, t1, t0, inside, col);
                else TriOut(b0, b1, t0, inside, col);
                if (caps)
                {
                    TriOut(cb, b0, b1, inside, col);
                    if (rTop > 0.0001f) TriOut(ct, t0, t1, inside, col);
                }
            }
        }

        public void Cylinder(Matrix4x4 m, int sides, Color col) => Frustum(m, sides, 0.5f, 0.5f, col);
        public void Cone(Matrix4x4 m, int sides, Color col) => Frustum(m, sides, 0.5f, 0f, col);

        public void Pyramid(Matrix4x4 m, Color col)
        {
            Frustum(m * Matrix4x4.Rotate(Quaternion.Euler(0, 45, 0)) * Matrix4x4.Scale(new Vector3(1.4142f, 1, 1.4142f)), 4, 0.5f, 0f, col);
        }

        /// <summary>Ridge tent: triangular cross-section in X/Y, extruded along Z.</summary>
        public void Prism(Matrix4x4 m, Color col)
        {
            Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
            Vector3 inside = P(0, 0.3f, 0);
            Vector3 a = P(-.5f, 0, -.5f), b = P(.5f, 0, -.5f), c = P(0, 1, -.5f);
            Vector3 d = P(-.5f, 0, .5f), e = P(.5f, 0, .5f), f = P(0, 1, .5f);
            TriOut(a, b, c, inside, col);
            TriOut(d, e, f, inside, col);
            QuadOut(a, c, f, d, inside, col);
            QuadOut(b, c, f, e, inside, col);
            QuadOut(a, b, e, d, inside, col);
        }

        /// <summary>Lean-to wedge: high edge at z=-0.5, slopes down to the ground at z=+0.5.</summary>
        public void Wedge(Matrix4x4 m, Color col)
        {
            Vector3 P(float x, float y, float z) => m.MultiplyPoint3x4(new Vector3(x, y, z));
            Vector3 inside = P(0, 0.3f, -0.2f);
            Vector3 a = P(-.5f, 0, -.5f), b = P(.5f, 0, -.5f), c = P(.5f, 0, .5f), d = P(-.5f, 0, .5f);
            Vector3 e = P(-.5f, 1, -.5f), f = P(.5f, 1, -.5f);
            QuadOut(a, b, f, e, inside, col);
            QuadOut(e, f, c, d, inside, col);
            TriOut(a, e, d, inside, col);
            TriOut(b, f, c, inside, col);
            QuadOut(a, b, c, d, inside, col);
        }

        /// <summary>Low-poly sphere (subdivided octahedron) of diameter 1, sitting on y=0.</summary>
        public void Sphere(Matrix4x4 m, Color col, int subdiv = 1)
        {
            var tris = new List<(Vector3, Vector3, Vector3)>
            {
                (Vector3.up, Vector3.right, Vector3.forward), (Vector3.up, Vector3.forward, Vector3.left),
                (Vector3.up, Vector3.left, Vector3.back), (Vector3.up, Vector3.back, Vector3.right),
                (Vector3.down, Vector3.forward, Vector3.right), (Vector3.down, Vector3.left, Vector3.forward),
                (Vector3.down, Vector3.back, Vector3.left), (Vector3.down, Vector3.right, Vector3.back),
            };
            for (int s = 0; s < subdiv; s++)
            {
                var next = new List<(Vector3, Vector3, Vector3)>();
                foreach (var (a, b, c) in tris)
                {
                    Vector3 ab = (a + b).normalized, bc = (b + c).normalized, ca = (c + a).normalized;
                    next.Add((a, ab, ca)); next.Add((ab, b, bc)); next.Add((ca, bc, c)); next.Add((ab, bc, ca));
                }
                tris = next;
            }
            Vector3 inside = m.MultiplyPoint3x4(new Vector3(0, 0.5f, 0));
            foreach (var (a, b, c) in tris)
            {
                Vector3 Tp(Vector3 p) => m.MultiplyPoint3x4(p * 0.5f + new Vector3(0, 0.5f, 0));
                TriOut(Tp(a), Tp(b), Tp(c), inside, col);
            }
        }

        /// <summary>Flat ring on the ground (selection / radius markers).</summary>
        public void Ring(Matrix4x4 m, int sides, float inner, float outer, Color col)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 i0 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * inner, 0, Mathf.Sin(a0) * inner));
                Vector3 i1 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * inner, 0, Mathf.Sin(a1) * inner));
                Vector3 o0 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * outer, 0, Mathf.Sin(a0) * outer));
                Vector3 o1 = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a1) * outer, 0, Mathf.Sin(a1) * outer));
                Vector3 below = m.MultiplyPoint3x4(new Vector3(Mathf.Cos(a0) * (inner + outer) * 0.5f, -1, Mathf.Sin(a0) * (inner + outer) * 0.5f));
                QuadOut(i0, o0, o1, i1, below, col);
            }
        }

        /// <summary>Named primitive, used by data-driven building shapes.</summary>
        public void Prim(string type, Matrix4x4 m, Color col)
        {
            switch (type)
            {
                case "box": Box(m, col); break;
                case "cylinder": Cylinder(m, 8, col); break;
                case "cone": Cone(m, 8, col); break;
                case "pyramid": Pyramid(m, col); break;
                case "prism": Prism(m, col); break;
                case "wedge": Wedge(m, col); break;
                case "sphere": Sphere(m, col); break;
                case "ring": Ring(m, 24, 0.42f, 0.5f, col); break;
                default: Box(m, col); break;
            }
        }

        public Mesh ToMesh(string name = "mesh")
        {
            var mesh = new Mesh { name = name };
            if (_v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_v);
            mesh.SetNormals(_n);
            mesh.SetColors(_c);
            mesh.SetTriangles(_t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Matrix4x4 TRS(Vector3 pos, float yawDeg, Vector3 size) =>
            Matrix4x4.TRS(pos, Quaternion.Euler(0, yawDeg, 0), size);
    }

    /// <summary>Shared placeholder meshes.</summary>
    public static class Shapes
    {
        static Mesh _citizen, _carry, _conifer, _broadleaf, _rock, _animal, _box;

        /// <summary>Simple standing figure ~0.85 units tall (a 1.7 m person at 2 m per cell).</summary>
        public static Mesh Citizen => _citizen ??= BuildCitizen();
        public static Mesh CarryBox => _carry ??= Build(b => b.Box(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.22f, 0.16f, 0.22f)), new Color(1, 1, 1, 1)), "carry");
        public static Mesh Conifer => _conifer ??= BuildConifer();
        public static Mesh Broadleaf => _broadleaf ??= BuildBroadleaf();
        public static Mesh Rock => _rock ??= BuildRock();
        public static Mesh Animal => _animal ??= BuildAnimal();
        public static Mesh UnitBox => _box ??= Build(b => b.Box(Matrix4x4.identity, new Color(1, 1, 1, 1)), "box");

        static Mesh Build(System.Action<MeshBuilder> f, string name)
        {
            var b = new MeshBuilder(); f(b); return b.ToMesh(name);
        }

        static Mesh BuildCitizen()
        {
            var b = new MeshBuilder();
            var cloth = new Color(1, 1, 1, 1);           // tinted by instance colour
            var skin = new Color(0.62f, 0.50f, 0.40f, 0); // not tinted
            b.Frustum(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.34f, 0.62f, 0.34f)), 6, 0.42f, 0.32f, cloth);
            b.Sphere(MeshBuilder.TRS(new Vector3(0, 0.6f, 0), 0, new Vector3(0.22f, 0.24f, 0.22f)), skin, 1);
            return b.ToMesh("citizen");
        }

        static Mesh BuildConifer()
        {
            var b = new MeshBuilder();
            var trunk = new Color(0.36f, 0.27f, 0.19f, 0);
            var leaf = new Color(1, 1, 1, 1);
            b.Cylinder(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.18f, 0.5f, 0.18f)), 5, trunk);
            b.Cone(MeshBuilder.TRS(new Vector3(0, 0.35f, 0), 0, new Vector3(1.0f, 1.1f, 1.0f)), 7, leaf);
            b.Cone(MeshBuilder.TRS(new Vector3(0, 0.95f, 0), 20, new Vector3(0.72f, 0.95f, 0.72f)), 7, leaf);
            return b.ToMesh("conifer");
        }

        static Mesh BuildBroadleaf()
        {
            var b = new MeshBuilder();
            var trunk = new Color(0.38f, 0.29f, 0.21f, 0);
            var leaf = new Color(1, 1, 1, 1);
            b.Frustum(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.2f, 0.75f, 0.2f)), 5, 0.5f, 0.35f, trunk);
            b.Sphere(MeshBuilder.TRS(new Vector3(0, 0.55f, 0), 0, new Vector3(1.0f, 0.95f, 1.0f)), leaf, 1);
            return b.ToMesh("broadleaf");
        }

        static Mesh BuildRock()
        {
            var b = new MeshBuilder();
            var col = new Color(1, 1, 1, 1);
            b.Sphere(MeshBuilder.TRS(new Vector3(0, -0.15f, 0), 0, new Vector3(1.0f, 0.75f, 0.85f)), col, 1);
            b.Sphere(MeshBuilder.TRS(new Vector3(0.3f, -0.1f, 0.25f), 30, new Vector3(0.55f, 0.5f, 0.5f)), col, 1);
            return b.ToMesh("rock");
        }

        static Mesh BuildAnimal()
        {
            var b = new MeshBuilder();
            var fur = new Color(1, 1, 1, 1);
            var dark = new Color(0.25f, 0.2f, 0.16f, 0);
            b.Box(MeshBuilder.TRS(new Vector3(0, 0.3f, 0), 0, new Vector3(0.22f, 0.22f, 0.55f)), fur);
            b.Box(MeshBuilder.TRS(new Vector3(0, 0.45f, 0.3f), 0, new Vector3(0.14f, 0.16f, 0.18f)), fur);
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0) ? -0.07f : 0.07f, z = (i < 2) ? -0.2f : 0.2f;
                b.Box(MeshBuilder.TRS(new Vector3(x, 0, z), 0, new Vector3(0.05f, 0.3f, 0.05f)), dark);
            }
            return b.ToMesh("animal");
        }
    }
}
