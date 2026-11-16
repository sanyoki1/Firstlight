using UnityEngine;

namespace Firstlight.Core
{
    /// <summary>
    /// An open fire: a bed of glowing embers and a bundle of flame tongues (a tall core, a ring of shorter ones round
    /// it, a few low ones at the edge). The tongues are teardrop shapes; the shader makes them lick and sway, colours
    /// them by height in a few steps (pale at the foot, orange, red at the tip) and eats their tips away raggedly.
    /// Each tongue vertex carries: uv = (how far up the tongue 0..1, how far it sways in units), colour r = the tongue's
    /// own phase, g = how hot it burns.
    /// </summary>
    public static class FlameModel
    {
        static Mesh _spark;

        /// <summary>One spark: a small glowing bit, one unit across, coloured by its instance.</summary>
        public static Mesh Spark
        {
            get
            {
                if (_spark != null) return _spark;
                var b = new MeshBuilder { Glow = true, GlowKind = 3 };
                b.Mat("flame").Box(Matrix4x4.Translate(new Vector3(0, -0.5f, 0)), new Color(1, 1, 1, 1));
                return _spark = b.ToMesh("spark");
            }
        }

        /// <summary>Adds a fire filling the box <paramref name="m"/> (origin = the middle of its foot, x/z = width, y = height).</summary>
        public static void Add(MeshBuilder b, Matrix4x4 m, int seed = 0)
        {
            bool glow = b.Glow; int kind = b.GlowKind, layer = b.Layer;
            b.Mat("flame");
            b.Glow = true;
            b.GlowKind = 2;
            b.Sphere(m * Matrix4x4.TRS(new Vector3(0, -0.04f, 0), Quaternion.identity, new Vector3(0.92f, 0.14f, 0.92f)), new Color(1, 1, 1, 0), 1);
            b.GlowKind = 1;
            float width = m.lossyScale.x, turn = seed * 0.7f;
            Tongue(b, m, Vector2.zero, new Vector2(0.04f, -0.03f), 0.2f, 1f, 1f, 0.13f + seed * 0.31f, width);
            for (int k = 0; k < 5; k++)
            {
                float a = k * Mathf.PI * 0.4f + 0.35f + turn;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Tongue(b, m, dir * 0.22f, dir * 0.17f, 0.15f + 0.02f * (k % 2), 0.56f + 0.1f * (k * 3 % 4), 0.5f, k * 0.19f + 0.4f + seed * 0.17f, width);
            }
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 0.667f + 1.0f + turn;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Tongue(b, m, dir * 0.33f, dir * 0.4f, 0.1f, 0.32f + 0.06f * k, 0.3f, k * 0.27f + 0.07f + seed * 0.23f, width);
            }
            b.Glow = glow; b.GlowKind = kind; b.Layer = layer;
        }

        static readonly float[] RingAt = { 0f, 0.28f, 0.62f, 1f }, RingWide = { 0.6f, 1f, 0.62f, 0f };

        // One tongue from foot to tip (both in the fire's box, x/z), r wide at its widest, h tall.
        static void Tongue(MeshBuilder b, Matrix4x4 m, Vector2 foot, Vector2 tip, float r, float h, float heat, float phase, float width)
        {
            const int sides = 5;
            phase -= Mathf.Floor(phase);
            var col = new Color(Mathf.LinearToGammaSpace(phase), Mathf.LinearToGammaSpace(heat), 0, 0);
            float sway = 0.8f * Mathf.Max(r, 0.13f) * width;
            Vector3 Axis(float t) { var c = Vector2.Lerp(foot, tip, t * t); return new Vector3(c.x, t * h, c.y); }
            Vector3 P(int ring, int s)
            {
                float a = (s + ring * 0.5f) / sides * Mathf.PI * 2f + phase * 6f, rr = r * RingWide[ring];
                return m.MultiplyPoint3x4(Axis(RingAt[ring]) + new Vector3(Mathf.Cos(a) * rr, 0, Mathf.Sin(a) * rr));
            }
            for (int ring = 0; ring < 3; ring++)
            {
                var inside = m.MultiplyPoint3x4(Axis((RingAt[ring] + RingAt[ring + 1]) * 0.5f));
                Vector2 lo = new(RingAt[ring], sway), hi = new(RingAt[ring + 1], sway);
                for (int s = 0; s < sides; s++)
                {
                    Vector3 a = P(ring, s), a2 = P(ring, s + 1), c = P(ring + 1, s), c2 = P(ring + 1, s + 1);
                    if (ring == 2) { b.TriData(a, a2, c, lo, lo, hi, inside, col); continue; }
                    b.TriData(a, a2, c, lo, lo, hi, inside, col);
                    b.TriData(a2, c2, c, lo, hi, hi, inside, col);
                }
            }
        }
    }
}
