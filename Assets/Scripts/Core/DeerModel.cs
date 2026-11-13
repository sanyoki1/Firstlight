using UnityEngine;

namespace Firstlight.Core
{
    /// <summary>
    /// Low-poly deer built from tapered segments, authored in metres (about 1 m at the shoulder, 1.6 m nose to
    /// tail) and scaled to world units (2 m). Instances only carry a yaw, so each pose is its own mesh:
    /// standing, an 8-frame walk cycle, head half down, and head down (drinking or grazing).
    /// Faces +z, feet on y = 0.
    /// </summary>
    public static class DeerModel
    {
        public const int WalkFrames = 8;
        public enum Pose { Stand, HeadMid, HeadDown, Walk0 }   // Walk0 + k for walk frame k
        public static int PoseCount => (int)Pose.Walk0 + WalkFrames;

        static readonly Mesh[] _doe = new Mesh[PoseCount], _stag = new Mesh[PoseCount];

        public static Mesh Get(int pose, bool stag)
        {
            var set = stag ? _stag : _doe;
            return set[pose] ??= Build(pose, stag);
        }

        public static void Reset()
        {
            for (int i = 0; i < PoseCount; i++) { _doe[i] = null; _stag[i] = null; }
        }

        struct Params
        {
            public float neck, head, pitch;        // degrees from vertical toward +z; body pitch (nose down)
            public float[] swing, bend;            // per leg: LF, RF, LH, RH (radians, + = foot back)
        }

        static Params ParamsFor(int pose)
        {
            var p = new Params { swing = new float[4], bend = new float[4] };
            switch ((Pose)Mathf.Min(pose, (int)Pose.Walk0))
            {
                case Pose.Stand: p.neck = 32; p.head = 112; break;
                case Pose.HeadMid: p.neck = 88; p.head = 150; p.pitch = 3; p.swing[0] = p.swing[1] = -0.28f; break;
                case Pose.HeadDown: p.neck = 138; p.head = 176; p.pitch = 7; p.swing[0] = p.swing[1] = -0.38f; break;
                default:
                {
                    p.neck = 50; p.head = 122;
                    float ph = (pose - (int)Pose.Walk0) / (float)WalkFrames * Mathf.PI * 2f;
                    // Four-beat walk: left hind, left fore, right hind, right fore, a quarter cycle apart.
                    float[] offset = { Mathf.PI * 0.5f, Mathf.PI * 1.5f, 0f, Mathf.PI };
                    for (int l = 0; l < 4; l++)
                    {
                        float a = ph + offset[l];
                        p.swing[l] = 0.34f * Mathf.Sin(a);
                        p.bend[l] = 0.85f * Mathf.Max(0f, -Mathf.Cos(a));   // knee folds while the foot swings forward
                    }
                    break;
                }
            }
            return p;
        }

        static Vector3 Dir(float degFromUp) =>
            new(0, Mathf.Cos(degFromUp * Mathf.Deg2Rad), Mathf.Sin(degFromUp * Mathf.Deg2Rad));

        static Mesh Build(int pose, bool stag)
        {
            var p = ParamsFor(pose);
            var coat = new Color(1, 1, 1, 1);                    // instance colour varies the coat
            var pale = new Color(1, 1, 1, 0.4f);
            var dark = new Color(0.32f, 0.27f, 0.24f, 0);
            var hoof = new Color(0.28f, 0.25f, 0.23f, 0);
            var antler = new Color(0.78f, 0.7f, 0.6f, 0);

            // Body pitch turns everything about the rear hips, so a lowered head brings the shoulders down too.
            var pivot = new Vector3(0, 0.8f, -0.4f);
            var body = Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(p.pitch, 0, 0)) * Matrix4x4.Translate(-pivot);
            Vector3 B(Vector3 v) => body.MultiplyPoint3x4(v);

            // Legs first: their lowest hoof sets how far the whole deer is lowered onto the ground.
            Vector3[] hips =
            {
                B(new(-0.1f, 0.76f, 0.36f)), B(new(0.1f, 0.76f, 0.36f)),
                B(new(-0.11f, 0.78f, -0.4f)), B(new(0.11f, 0.78f, -0.4f)),
            };
            var legs = new (Vector3 hip, Vector3 knee, Vector3 foot, Vector3 toe)[4];
            float minY = float.MaxValue;
            for (int l = 0; l < 4; l++)
            {
                bool hind = l >= 2;
                float upper = hind ? -0.28f : 0f, lower = hind ? 0.3f : 0f;   // hind legs: thigh forward, hock back
                Vector3 down(float a) => new(0, -Mathf.Cos(a), -Mathf.Sin(a));
                float a1 = p.swing[l] + upper - p.pitch * Mathf.Deg2Rad;
                float a2 = p.swing[l] + lower + p.bend[l] - p.pitch * Mathf.Deg2Rad;
                Vector3 knee = hips[l] + down(a1) * (hind ? 0.4f : 0.38f);
                Vector3 foot = knee + down(a2) * (hind ? 0.36f : 0.32f);
                Vector3 toe = foot + down(a2) * 0.06f;
                legs[l] = (hips[l], knee, foot, toe);
                minY = Mathf.Min(minY, toe.y);
            }

            var b = new MeshBuilder();
            var world = Matrix4x4.Scale(Vector3.one * 0.5f) * Matrix4x4.Translate(new Vector3(0, -minY, 0));

            void Seg(string mat, Vector3 a, Vector3 c, float r0, float r1, Color col, float flat = 1f, int sides = 6)
            {
                Vector3 d = c - a;
                var m = world * Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d), new Vector3(flat, d.magnitude, 1));
                b.Mat(mat).Frustum(m, sides, r0, r1, col);
            }

            void Blob(string mat, Vector3 centre, Vector3 size, Color col)
            {
                var m = world * body * Matrix4x4.TRS(centre - new Vector3(0, size.y * 0.5f, 0), Quaternion.identity, size);
                b.Mat(mat).Sphere(m, col, 1);
            }

            // Trunk: barrel, deep chest, rounded haunch, pale belly and rump patch, short tail.
            Seg("deer", B(new(0, 0.84f, -0.5f)), B(new(0, 0.82f, 0.42f)), 0.19f, 0.21f, coat, 0.82f, 7);
            Blob("deer", new(0, 0.8f, 0.3f), new(0.34f, 0.46f, 0.42f), coat);
            Blob("deer", new(0, 0.86f, -0.38f), new(0.34f, 0.4f, 0.4f), coat);
            Blob("deer_pale", new(0, 0.67f, -0.02f), new(0.26f, 0.12f, 0.66f), pale);
            Blob("deer_pale", new(0, 0.8f, -0.585f), new(0.2f, 0.22f, 0.08f), pale);
            Seg("deer", B(new(0, 0.97f, -0.58f)), B(new(0, 0.85f, -0.65f)), 0.04f, 0.028f, coat, 0.8f, 5);

            // Legs: thigh / forearm, cannon, dark hoof.
            for (int l = 0; l < 4; l++)
            {
                var (hip, knee, foot, toe) = legs[l];
                bool hind = l >= 2;
                Seg("deer", hip + new Vector3(0, 0.06f, 0), knee, hind ? 0.08f : 0.065f, 0.04f, coat, 0.85f, 5);
                Seg("deer", knee, foot, 0.035f, 0.028f, coat, 1f, 5);
                Seg("deer", foot, toe, 0.032f, 0.036f, hoof, 1f, 5);
            }

            // Neck and head, hinged at the shoulder.
            Vector3 shoulder = B(new(0, 0.94f, 0.4f));
            Vector3 neckDir = body.MultiplyVector(Dir(p.neck));
            Vector3 jaw = shoulder + neckDir * 0.56f;
            Vector3 headDir = body.MultiplyVector(Dir(p.head));
            Vector3 up = body.MultiplyVector(Dir(p.head - 90f));      // top of the skull
            Seg("deer", shoulder - neckDir * 0.14f, jaw, 0.155f, 0.085f, coat, 0.75f, 6);
            Seg("deer", jaw - headDir * 0.03f, jaw + headDir * 0.33f, 0.085f, 0.042f, coat, 0.82f, 6);
            Seg("deer_pale", jaw + headDir * 0.16f - up * 0.03f, jaw + headDir * 0.31f - up * 0.02f, 0.04f, 0.03f, pale, 0.9f, 5);
            Seg("deer", jaw + headDir * 0.31f, jaw + headDir * 0.36f, 0.032f, 0.022f, dark, 1f, 5);   // nose
            for (int s = -1; s <= 1; s += 2)
            {
                var side = new Vector3(s, 0, 0);
                // Eyes, then ears that stand out to the side and up.
                Seg("deer", jaw + headDir * 0.1f + up * 0.035f + side * 0.055f, jaw + headDir * 0.13f + up * 0.035f + side * 0.06f, 0.016f, 0.012f, dark, 1f, 4);
                Vector3 ear = jaw + up * 0.06f + headDir * 0.02f + side * 0.045f;
                Seg("deer", ear, ear + side * 0.11f + up * 0.11f - headDir * 0.04f, 0.04f, 0.012f, coat, 0.45f, 4);
                if (!stag) continue;
                // Antlers: a beam rising up and back with two forward tines.
                Vector3 baseA = jaw + up * 0.08f + headDir * 0.06f + side * 0.035f;
                Vector3 mid = baseA + up * 0.16f + side * 0.07f - headDir * 0.05f;
                Vector3 tip = mid + up * 0.16f + side * 0.05f - headDir * 0.07f;
                Seg("deer_pale", baseA, mid, 0.022f, 0.017f, antler, 1f, 4);
                Seg("deer_pale", mid, tip, 0.017f, 0.008f, antler, 1f, 4);
                Seg("deer_pale", baseA + (mid - baseA) * 0.35f, baseA + (mid - baseA) * 0.35f + headDir * 0.1f + up * 0.05f, 0.013f, 0.006f, antler, 1f, 4);
                Seg("deer_pale", mid, mid + headDir * 0.1f + up * 0.07f, 0.013f, 0.006f, antler, 1f, 4);
            }
            return b.ToMesh(stag ? $"stag_{pose}" : $"doe_{pose}");
        }
    }
}
