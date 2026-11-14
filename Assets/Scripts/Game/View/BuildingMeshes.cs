using System.Collections.Generic;
using Firstlight.Core;
using UnityEngine;

namespace Firstlight.Game
{
    /// <summary>
    /// Meshes for the settlement, built once from data: each building from its <c>parts</c> (every part wears a
    /// material layer; "flame" parts go into a separate glowing mesh so a fire can be lit or out), construction
    /// site markers and scaffolds per footprint size, goods piles and carried bundles per resource.
    /// Origin of a building mesh = the footprint centre at ground level; the door side is -z.
    /// </summary>
    public static class BuildingMeshes
    {
        static readonly Color Fixed = new(1, 1, 1, 0), Tinted = new(1, 1, 1, 1);
        static readonly Dictionary<string, (Mesh body, Mesh glow, float height)> _defs = new();
        static readonly Dictionary<int, Mesh> _siteMarks = new(), _scaffolds = new();
        static readonly Dictionary<string, Mesh> _piles = new();
        static Mesh _ring, _marker, _bundleBasket;

        /// <summary>Body (lit), glow (flames, may be null) and the body's height in units.</summary>
        public static (Mesh body, Mesh glow, float height) For(BuildingDef d)
        {
            if (_defs.TryGetValue(d.id, out var m)) return m;
            var body = new MeshBuilder();
            var glow = new MeshBuilder { Glow = true };
            // An earth pad under the footprint: on a slope it shows as a levelled platform instead of a gap.
            body.Mat("dirt").Box(MeshBuilder.TRS(new Vector3(0, -0.6f, 0), 0, new Vector3(d.sizeX - 0.08f, 0.63f, d.sizeY - 0.08f)), Fixed);
            float top = 0.05f;
            foreach (var p in d.parts)
            {
                var pos = p.pos != null && p.pos.Length == 3 ? new Vector3(p.pos[0], p.pos[1], p.pos[2]) : Vector3.zero;
                var size = p.size != null && p.size.Length == 3 ? new Vector3(p.size[0], p.size[1], p.size[2]) : Vector3.one;
                var col = string.IsNullOrEmpty(p.color) ? Color.white : Pal.Hex(p.color);
                col.a = 0;
                bool flame = p.material == "flame";
                var b = flame ? glow : body;
                b.Mat(string.IsNullOrEmpty(p.material) ? "wood" : p.material);
                Prim(b, p.prim, MeshBuilder.TRS(pos, p.yaw, size), col);
                if (!flame) top = Mathf.Max(top, pos.y + size.y);
            }
            m = (body.ToMesh(d.id), glow.VertexCount > 0 ? glow.ToMesh(d.id + "_glow") : null, top);
            _defs[d.id] = m;
            return m;
        }

        /// <summary>Data primitives: the MeshBuilder ones plus a few for camp scenery.</summary>
        public static void Prim(MeshBuilder b, string type, Matrix4x4 m, Color col)
        {
            switch (type)
            {
                case "log":       // lying along local x: size x = length, y/z = thickness
                    b.Cylinder(m * Matrix4x4.Translate(new Vector3(-0.5f, 0.5f, 0)) * Matrix4x4.Rotate(Quaternion.Euler(0, 0, -90)), 7, col);
                    break;
                case "logs":      // a stack of logs along x filling the box: three below, two above
                    for (int r = 0; r < 2; r++)
                    {
                        int n = r == 0 ? 3 : 2;
                        for (int i = 0; i < n; i++)
                        {
                            float z = (i - (n - 1) * 0.5f) / 3f;
                            var lm = m * Matrix4x4.Translate(new Vector3(0, r * 0.45f, z)) * Matrix4x4.Scale(new Vector3(1f - r * 0.1f, 0.5f, 1f / 3f));
                            Prim(b, "log", lm, col);
                        }
                    }
                    break;
                case "stonering": // a ring of stones round a hearth, as wide as the box
                    for (int k = 0; k < 10; k++)
                    {
                        float a = k * Mathf.PI * 2f / 10f;
                        var sm = m * Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.44f, -0.1f, Mathf.Sin(a) * 0.44f), Quaternion.Euler(0, a * 57f + k * 23f, 0),
                            new Vector3(0.17f + (k % 3) * 0.02f, 1f, 0.13f));
                        b.Sphere(sm, col, 1);
                    }
                    break;
                default:
                    b.Prim(type, m, col);
                    break;
            }
        }

        /// <summary>Corner stakes and a cord round a footprint: a marked-out site.</summary>
        public static Mesh SiteMarks(int sx, int sy)
        {
            int key = sx * 64 + sy;
            if (_siteMarks.TryGetValue(key, out var mesh)) return mesh;
            var b = new MeshBuilder();
            float hx = sx * 0.5f - 0.1f, hz = sy * 0.5f - 0.1f;
            b.Mat("dirt").Box(MeshBuilder.TRS(new Vector3(0, -0.6f, 0), 0, new Vector3(sx - 0.08f, 0.62f, sy - 0.08f)), Fixed);
            b.Mat("wood");
            for (int k = 0; k < 4; k++)
            {
                float x = (k & 1) == 0 ? -hx : hx, z = (k & 2) == 0 ? -hz : hz;
                b.Cylinder(MeshBuilder.TRS(new Vector3(x, 0, z), 0, new Vector3(0.06f, 0.42f, 0.06f)), 5, Fixed);
            }
            b.Mat("hide");
            var cord = new Color(0.85f, 0.8f, 0.7f, 0);
            b.Box(MeshBuilder.TRS(new Vector3(0, 0.33f, -hz), 0, new Vector3(hx * 2, 0.015f, 0.015f)), cord);
            b.Box(MeshBuilder.TRS(new Vector3(0, 0.33f, hz), 0, new Vector3(hx * 2, 0.015f, 0.015f)), cord);
            b.Box(MeshBuilder.TRS(new Vector3(-hx, 0.33f, 0), 0, new Vector3(0.015f, 0.015f, hz * 2)), cord);
            b.Box(MeshBuilder.TRS(new Vector3(hx, 0.33f, 0), 0, new Vector3(0.015f, 0.015f, hz * 2)), cord);
            mesh = b.ToMesh($"site_{sx}x{sy}");
            _siteMarks[key] = mesh;
            return mesh;
        }

        /// <summary>Poles lashed round a rising building.</summary>
        public static Mesh Scaffold(int sx, int sy)
        {
            int key = sx * 64 + sy;
            if (_scaffolds.TryGetValue(key, out var mesh)) return mesh;
            var b = new MeshBuilder().Mat("bark_broad");
            float hx = sx * 0.5f - 0.05f, hz = sy * 0.5f - 0.05f, h = 1.5f;
            for (int k = 0; k < 4; k++)
            {
                float x = (k & 1) == 0 ? -hx : hx, z = (k & 2) == 0 ? -hz : hz;
                b.Cylinder(MeshBuilder.TRS(new Vector3(x, 0, z), 0, new Vector3(0.07f, h, 0.07f)), 5, Fixed);
            }
            foreach (float y in new[] { 0.7f, 1.35f })
            {
                Prim(b, "log", MeshBuilder.TRS(new Vector3(0, y, -hz), 0, new Vector3(hx * 2, 0.05f, 0.05f)), Fixed);
                Prim(b, "log", MeshBuilder.TRS(new Vector3(0, y, hz), 0, new Vector3(hx * 2, 0.05f, 0.05f)), Fixed);
                Prim(b, "log", MeshBuilder.TRS(new Vector3(-hx, y, 0), 90, new Vector3(hz * 2, 0.05f, 0.05f)), Fixed);
                Prim(b, "log", MeshBuilder.TRS(new Vector3(hx, y, 0), 90, new Vector3(hz * 2, 0.05f, 0.05f)), Fixed);
            }
            mesh = b.ToMesh($"scaffold_{sx}x{sy}");
            _scaffolds[key] = mesh;
            return mesh;
        }

        /// <summary>Pile kind for a resource: logs, split wood, stones, hides, tools, food basket, clay, metal.</summary>
        public static string PileKind(ResourceDef r)
        {
            if (r == null) return "basket";
            switch (r.id)
            {
                case "wood": return "logs";
                case "firewood": return "firewood";
                case "stone": case "copper_ore": case "tin_ore": return "stones";
                case "hides": case "clothing": return "hides";
                case "clay": return "clay";
                case "meat": case "preserved": return "meat";
                case "copper": case "bronze": return "ingots";
            }
            if (r.toolSpeed > 0) return "tools";
            return "basket";
        }

        public static readonly string[] PileKinds = { "logs", "firewood", "stones", "hides", "clay", "meat", "ingots", "tools", "basket" };

        /// <summary>A pile of goods about 0.6 units across (scaled by how much there is).</summary>
        public static Mesh Pile(string kind)
        {
            if (_piles.TryGetValue(kind, out var mesh)) return mesh;
            var b = new MeshBuilder();
            switch (kind)
            {
                case "logs": b.Mat("bark_broad"); Prim(b, "logs", MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.7f, 0.32f, 0.42f)), Fixed); break;
                case "firewood": b.Mat("wood"); Prim(b, "logs", MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.4f, 0.26f, 0.42f)), new Color(0.9f, 0.82f, 0.7f, 0)); break;
                case "stones":
                    b.Mat("rock");
                    b.Sphere(MeshBuilder.TRS(new Vector3(-0.12f, 0, 0), 20, new Vector3(0.3f, 0.22f, 0.28f)), Fixed, 1);
                    b.Sphere(MeshBuilder.TRS(new Vector3(0.14f, 0, 0.05f), 60, new Vector3(0.26f, 0.2f, 0.24f)), Fixed, 1);
                    b.Sphere(MeshBuilder.TRS(new Vector3(0, 0.13f, 0), 10, new Vector3(0.24f, 0.18f, 0.22f)), Fixed, 1);
                    break;
                case "hides":
                    b.Mat("hide");
                    b.Box(MeshBuilder.TRS(Vector3.zero, 10, new Vector3(0.5f, 0.08f, 0.4f)), Fixed);
                    b.Box(MeshBuilder.TRS(new Vector3(0, 0.08f, 0), -15, new Vector3(0.45f, 0.08f, 0.36f)), new Color(0.85f, 0.8f, 0.75f, 0));
                    b.Mat("fur").Box(MeshBuilder.TRS(new Vector3(0, 0.16f, 0), 5, new Vector3(0.4f, 0.07f, 0.32f)), Fixed);
                    break;
                case "clay": b.Mat("clay").Sphere(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.45f, 0.28f, 0.4f)), Fixed, 1); break;
                case "meat":
                    b.Mat("hide").Box(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.45f, 0.06f, 0.35f)), Fixed);
                    b.Mat("meat").Sphere(MeshBuilder.TRS(new Vector3(0, 0.05f, 0), 20, new Vector3(0.36f, 0.2f, 0.26f)), Fixed, 1);
                    break;
                case "ingots":
                    b.Mat("rock");
                    for (int i = 0; i < 3; i++) b.Box(MeshBuilder.TRS(new Vector3((i - 1) * 0.13f, 0, 0), 0, new Vector3(0.1f, 0.06f, 0.3f)), new Color(1.2f, 0.75f, 0.5f, 0));
                    break;
                case "tools":
                    b.Mat("wood");
                    Prim(b, "log", MeshBuilder.TRS(new Vector3(0, 0.03f, 0), 20, new Vector3(0.45f, 0.05f, 0.05f)), Fixed);
                    Prim(b, "log", MeshBuilder.TRS(new Vector3(0, 0.03f, 0.08f), -15, new Vector3(0.45f, 0.05f, 0.05f)), Fixed);
                    b.Mat("rock").Box(MeshBuilder.TRS(new Vector3(0.2f, 0.01f, 0.05f), 30, new Vector3(0.1f, 0.08f, 0.14f)), Fixed);
                    break;
                default:
                    b.Mat("basket").Cylinder(MeshBuilder.TRS(Vector3.zero, 0, new Vector3(0.36f, 0.3f, 0.36f)), 7, Fixed);
                    b.Mat("leaves").Sphere(MeshBuilder.TRS(new Vector3(0, 0.22f, 0), 0, new Vector3(0.3f, 0.14f, 0.3f)), new Color(1.1f, 0.85f, 0.85f, 0), 1);
                    break;
            }
            mesh = b.ToMesh("pile_" + kind);
            _piles[kind] = mesh;
            return mesh;
        }

        /// <summary>A flat glowing ring (selection), unit radius, tinted by the instance colour.</summary>
        public static Mesh Ring => _ring ??= BuildRing();
        static Mesh BuildRing()
        {
            var b = new MeshBuilder { Glow = true }.Mat("snow");
            b.Ring(Matrix4x4.Translate(new Vector3(0, 0.06f, 0)), 32, 0.86f, 1f, Tinted);
            return b.ToMesh("sel_ring");
        }

        /// <summary>A small glowing stud for radius previews (placed on the ground at intervals).</summary>
        public static Mesh Marker => _marker ??= BuildMarker();
        static Mesh BuildMarker()
        {
            var b = new MeshBuilder { Glow = true }.Mat("snow");
            b.Frustum(Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(0.22f, 0.08f, 0.22f)), 6, 0.5f, 0.35f, Tinted);
            return b.ToMesh("marker");
        }
    }
}
