using UnityEngine;

namespace Firstlight.Core
{
    /// <summary>
    /// Early Stone Age person built from tapered segments, authored in metres (1.65 m tall) and scaled to world
    /// units (2 m). Hide tunic, fur cape, bare arms and lower legs, wrapped feet. Two builds: a man (beard, short
    /// hair) and a woman (long hair, longer tunic). Instances only carry a yaw, so each pose is its own mesh.
    /// The spear is a separate mesh placed at <see cref="Hand"/> with the pose's <see cref="SpearHold"/>.
    /// Faces +z, feet on y = 0.
    /// </summary>
    public static class PersonModel
    {
        public const int Frames = 8;
        public enum Pose { Stand, Windup, Release, Bend, Walk0, Stalk0 = Walk0 + Frames, Carry0 = Stalk0 + Frames, Sit = Carry0 + Frames }
        public static int PoseCount => (int)Pose.Sit + 1;
        public enum Hold { Upright, Level, Back, None }

        static readonly Mesh[] _man = new Mesh[PoseCount], _woman = new Mesh[PoseCount];
        static readonly Vector3[] _hand = new Vector3[PoseCount];
        static readonly bool[] _handDone = new bool[PoseCount];
        static Mesh _spearUpright, _spearLevel, _tent, _fire;

        public static Mesh Get(int pose, bool woman)
        {
            var set = woman ? _woman : _man;
            return set[pose] ??= Build(pose, woman);
        }

        /// <summary>Right hand position in the person's own frame (world units, +z forward).</summary>
        public static Vector3 Hand(int pose)
        {
            if (!_handDone[pose]) Get(pose, false);
            return _hand[pose];
        }

        public static Hold SpearHold(int pose)
        {
            if (pose == (int)Pose.Windup) return Hold.Level;
            if (pose == (int)Pose.Release) return Hold.None;
            if (pose == (int)Pose.Bend || pose == (int)Pose.Sit) return Hold.None;
            if (pose >= (int)Pose.Carry0) return Hold.Back;
            if (pose >= (int)Pose.Stalk0) return Hold.Level;
            return Hold.Upright;
        }

        struct Params
        {
            public float lean;                    // torso lean forward from the hips (degrees)
            public float[] thigh, shin;           // L, R (radians from straight down, + = foot back)
            public float[] arm, elbow;            // L, R (radians from straight down, + = hand forward); elbow bends forward
        }

        static Params ParamsFor(int pose)
        {
            var p = new Params { thigh = new float[2], shin = new float[2], arm = new float[2], elbow = new float[2] };
            p.arm[0] = 0.08f; p.elbow[0] = 0.15f;
            p.arm[1] = 0.25f; p.elbow[1] = 1.25f;                      // right hand forward at the waist: holds the spear
            int frame = -1; float stride = 0.42f, armSwing = 0.3f;
            if (pose >= (int)Pose.Carry0 && pose < (int)Pose.Sit) { frame = pose - (int)Pose.Carry0; stride = 0.32f; armSwing = 0f; }
            else if (pose >= (int)Pose.Stalk0) { frame = pose - (int)Pose.Stalk0; stride = 0.24f; armSwing = 0.1f; }
            else if (pose >= (int)Pose.Walk0) frame = pose - (int)Pose.Walk0;

            if (frame >= 0)
            {
                float ph = frame / (float)Frames * Mathf.PI * 2f;
                for (int l = 0; l < 2; l++)
                {
                    float a = ph + l * Mathf.PI;
                    p.thigh[l] = stride * Mathf.Sin(a);
                    p.shin[l] = p.thigh[l] + 0.9f * Mathf.Max(0f, -Mathf.Cos(a));   // knee folds as the foot swings forward
                }
                p.arm[0] = -armSwing * Mathf.Sin(ph) + 0.08f;            // left arm swings against the left leg
            }
            if (pose >= (int)Pose.Stalk0 && pose < (int)Pose.Carry0)
            {
                // Crouched creep: knees bent, body low and leaning in, spear held level at the hip.
                p.lean = 28;
                for (int l = 0; l < 2; l++) { p.thigh[l] -= 0.75f; p.shin[l] += 0.75f; }
                p.arm[1] = 0.55f; p.elbow[1] = 0.9f;
                p.arm[0] = 0.5f + p.arm[0] * 0.3f; p.elbow[0] = 0.6f;
            }
            if (pose >= (int)Pose.Carry0 && pose < (int)Pose.Sit)
            {
                // Both hands up at the shoulders, steadying the deer across them.
                p.lean = 8;
                p.arm[0] = p.arm[1] = 2.7f; p.elbow[0] = p.elbow[1] = 1.9f;
            }
            switch ((Pose)pose)
            {
                case Pose.Windup:
                    p.lean = -6; p.thigh[0] = -0.3f; p.shin[0] = -0.3f; p.thigh[1] = 0.25f; p.shin[1] = 0.25f;
                    p.arm[1] = 3.5f; p.elbow[1] = 0.5f;                    // up and back behind the head
                    p.arm[0] = 1.3f; p.elbow[0] = 0.1f;                    // the free arm points at the deer
                    break;
                case Pose.Release:
                    p.lean = 18; p.thigh[0] = -0.35f; p.shin[0] = -0.2f; p.thigh[1] = 0.35f; p.shin[1] = 0.6f;
                    p.arm[1] = 1.5f; p.elbow[1] = 0f;                      // thrown arm follows through, forward
                    p.arm[0] = 0.3f; p.elbow[0] = 0.4f;
                    break;
                case Pose.Sit:
                    // On the ground, legs out in front, hands in the lap.
                    p.lean = 8; p.thigh[0] = p.thigh[1] = -1.45f; p.shin[0] = -1.35f; p.shin[1] = -1.55f;
                    p.arm[0] = p.arm[1] = 0.45f; p.elbow[0] = p.elbow[1] = 0.9f;
                    break;
                case Pose.Bend:
                    p.lean = 55; p.thigh[0] = p.thigh[1] = -0.6f; p.shin[0] = p.shin[1] = 0.5f;
                    p.arm[1] = 0.6f; p.elbow[1] = 0.1f; p.arm[0] = 0.4f; p.elbow[0] = 0.3f;
                    break;
            }
            return p;
        }

        static Vector3 Down(float a) => new(0, -Mathf.Cos(a), Mathf.Sin(a));      // + = forward (person faces +z)

        static Mesh Build(int pose, bool woman)
        {
            var p = ParamsFor(pose);
            var cloth = new Color(1, 1, 1, 1);        // instance colour varies the hide
            var skin = new Color(1, 1, 1, 0);
            var hair = new Color(1, 1, 1, 0);
            var fur = new Color(0.9f, 0.88f, 0.84f, 0);
            var wrap = new Color(0.72f, 0.66f, 0.6f, 0);

            // Upper body leans about the hips.
            var pelvis = new Vector3(0, 0.88f, 0);
            var body = Matrix4x4.Translate(pelvis) * Matrix4x4.Rotate(Quaternion.Euler(p.lean, 0, 0)) * Matrix4x4.Translate(-pelvis);
            Vector3 B(Vector3 v) => body.MultiplyPoint3x4(v);
            Vector3 BD(Vector3 v) => body.MultiplyVector(v);

            // Legs first: the lowest foot sets the ground offset.
            var legs = new (Vector3 hip, Vector3 knee, Vector3 ankle)[2];
            float minY = float.MaxValue;
            for (int l = 0; l < 2; l++)
            {
                var hip = new Vector3(l == 0 ? -0.09f : 0.09f, 0.88f, 0);
                var knee = hip + Down(-p.thigh[l]) * 0.44f;
                var ankle = knee + Down(-p.shin[l]) * 0.4f;
                legs[l] = (hip, knee, ankle);
                minY = Mathf.Min(minY, ankle.y - 0.05f);
            }

            var b = new MeshBuilder();
            var world = Matrix4x4.Scale(Vector3.one * 0.5f) * Matrix4x4.Translate(new Vector3(0, -minY, 0));

            void Seg(string mat, Vector3 a, Vector3 c, float r0, float r1, Color col, float wx = 1f, float wz = 1f, int sides = 6)
            {
                Vector3 d = c - a;
                var m = world * Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d), new Vector3(wx, d.magnitude, wz));
                b.Mat(mat).Frustum(m, sides, r0, r1, col);
            }
            void Blob(string mat, Vector3 centre, Vector3 size, Color col)
            {
                var m = world * Matrix4x4.TRS(centre - new Vector3(0, size.y * 0.5f, 0), Quaternion.identity, size);
                b.Mat(mat).Sphere(m, col, 1);
            }

            // Legs: bare shins, hide-wrapped feet pointing forward.
            for (int l = 0; l < 2; l++)
            {
                var (hip, knee, ankle) = legs[l];
                Seg("skin", hip, knee, 0.075f, 0.055f, skin, 1f, 1f, 6);
                Seg("skin", knee, ankle, 0.05f, 0.038f, skin, 1f, 1f, 6);
                var toeDir = new Vector3(0, -0.15f, 1f).normalized;
                Seg("hide", ankle + new Vector3(0, 0.02f, -0.05f), ankle + toeDir * 0.17f + new Vector3(0, -0.02f, 0), 0.05f, 0.04f, wrap, 1f, 0.75f, 5);
            }

            // Torso, tunic and cape (all lean with the body).
            Vector3 chest = B(new Vector3(0, 1.36f, 0));
            Seg("hide", B(new Vector3(0, 0.84f, 0)), chest, 0.16f, 0.185f, cloth, 1f, 0.68f, 7);
            float hem = woman ? 0.5f : 0.66f;
            Seg("hide", B(new Vector3(0, 1.02f, 0)), B(new Vector3(0, hem, 0.01f)), 0.17f, woman ? 0.23f : 0.21f, cloth, 1f, 0.8f, 7);
            Blob("fur", B(new Vector3(0, 1.36f, -0.02f)), new Vector3(0.48f, 0.16f, 0.3f), fur);
            Seg("fur", B(new Vector3(0, 1.42f, -0.08f)), B(new Vector3(0, 1.08f, -0.13f)), 0.17f, 0.12f, fur, 1.2f, 0.4f, 6);

            // Arms: shoulders on the torso, angles relative to it.
            Vector3 handR = Vector3.zero;
            for (int s = 0; s < 2; s++)
            {
                var shoulder = B(new Vector3(s == 0 ? -0.2f : 0.2f, 1.36f, 0));
                var out1 = new Vector3(s == 0 ? -0.08f : 0.08f, 0, 0);
                var elbowPt = shoulder + (BD(Down(p.arm[s])) + out1).normalized * 0.29f;
                var hand = elbowPt + (BD(Down(p.arm[s] + p.elbow[s])) + out1 * 0.5f).normalized * 0.26f;
                Seg("skin", shoulder, elbowPt, 0.052f, 0.042f, skin, 1f, 1f, 5);
                Seg("skin", elbowPt, hand, 0.04f, 0.032f, skin, 1f, 1f, 5);
                Blob("skin", hand, new Vector3(0.07f, 0.08f, 0.07f), skin);
                if (s == 1) handR = hand;
            }

            // Neck, head, hair; a beard for the man, long hair down the back for the woman.
            Vector3 neck = B(new Vector3(0, 1.42f, 0.01f));
            Vector3 head = B(new Vector3(0, 1.56f, 0.02f));
            Seg("skin", neck, head, 0.05f, 0.05f, skin, 1f, 1f, 5);
            Blob("skin", head + BD(new Vector3(0, 0.04f, 0)), new Vector3(0.18f, 0.22f, 0.2f), skin);
            Blob("hair", head + BD(new Vector3(0, 0.1f, -0.025f)), new Vector3(0.2f, 0.13f, 0.21f), hair);
            Blob("skin", head + BD(new Vector3(0, 0.03f, 0.1f)), new Vector3(0.04f, 0.05f, 0.04f), skin);            // nose
            if (woman)
                Seg("hair", head + BD(new Vector3(0, 0.08f, -0.07f)), head + BD(new Vector3(0, -0.2f, -0.11f)), 0.09f, 0.06f, hair, 1.1f, 0.6f, 6);
            else
                Blob("hair", head + BD(new Vector3(0, -0.04f, 0.06f)), new Vector3(0.15f, 0.1f, 0.1f), hair);

            _hand[pose] = world.MultiplyPoint3x4(handR);
            _handDone[pose] = true;
            return b.ToMesh(woman ? $"woman_{pose}" : $"man_{pose}");
        }

        // ------------------------------------------------------------------ spear and camp

        /// <summary>Spear, 1.9 m: origin at the grip (40% from the butt). Upright = shaft vertical; Level = pointing +z.</summary>
        public static Mesh Spear(bool level) => level ? (_spearLevel ??= BuildSpear(true)) : (_spearUpright ??= BuildSpear(false));

        static Mesh BuildSpear(bool level)
        {
            var b = new MeshBuilder();
            var world = Matrix4x4.Scale(Vector3.one * 0.5f);
            Vector3 axis = level ? Vector3.forward : Vector3.up;
            Vector3 butt = -axis * 0.76f, tip = axis * 1.14f, headBase = axis * 0.98f;
            void Seg(string mat, Vector3 a, Vector3 c, float r0, float r1, Color col)
            {
                Vector3 d = c - a;
                b.Mat(mat).Frustum(world * Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, d), new Vector3(1, d.magnitude, 1)), 5, r0, r1, col);
            }
            Seg("wood", butt, headBase, 0.017f, 0.015f, new Color(1, 1, 1, 0));
            Seg("rock", headBase - axis * 0.02f, tip, 0.03f, 0.002f, new Color(0.62f, 0.6f, 0.58f, 0));   // knapped stone point
            Seg("hide", headBase - axis * 0.05f, headBase + axis * 0.02f, 0.022f, 0.022f, new Color(0.7f, 0.62f, 0.55f, 0)); // binding
            return b.ToMesh(level ? "spear_level" : "spear_upright");
        }

        /// <summary>A conical hide tent on wooden poles, about 3 m across.</summary>
        public static Mesh Tent => _tent ??= BuildTent();

        static Mesh BuildTent()
        {
            var b = new MeshBuilder();
            var world = Matrix4x4.Scale(Vector3.one * 0.5f);
            b.Mat("hide").Frustum(world * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(3f, 2.3f, 3f)), 9, 0.5f, 0.06f, new Color(1, 1, 1, 1), false);
            // Pole ends sticking out of the top.
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + 0.4f;
                var top = new Vector3(0, 2.2f, 0);
                var end = top + new Vector3(Mathf.Cos(a) * 0.25f, 0.45f, Mathf.Sin(a) * 0.25f);
                var d = end - top;
                b.Mat("wood").Frustum(world * Matrix4x4.TRS(top, Quaternion.FromToRotation(Vector3.up, d), new Vector3(1, d.magnitude, 1)), 4, 0.03f, 0.02f, new Color(1, 1, 1, 0));
            }
            // Dark door flap.
            b.Mat("hide").Box(world * Matrix4x4.TRS(new Vector3(0, 0, 1.22f), Quaternion.Euler(-24, 0, 0), new Vector3(0.6f, 0.95f, 0.06f)), new Color(0.45f, 0.4f, 0.36f, 0));
            return b.ToMesh("tent");
        }

        /// <summary>Hearth: a ring of stones, crossed logs and a low flame.</summary>
        public static Mesh Fire => _fire ??= BuildFire();

        static Mesh BuildFire()
        {
            var b = new MeshBuilder();
            var world = Matrix4x4.Scale(Vector3.one * 0.5f);
            for (int k = 0; k < 9; k++)
            {
                float a = k * Mathf.PI * 2f / 9f;
                b.Mat("rock").Sphere(world * Matrix4x4.TRS(new Vector3(Mathf.Cos(a) * 0.45f, -0.03f, Mathf.Sin(a) * 0.45f), Quaternion.Euler(0, a * 57f, 0), new Vector3(0.2f, 0.15f, 0.16f)), new Color(0.8f, 0.78f, 0.75f, 0), 1);
            }
            for (int k = 0; k < 4; k++)
            {
                float a = k * Mathf.PI * 0.5f + 0.3f;
                var from = new Vector3(Mathf.Cos(a) * 0.38f, 0.03f, Mathf.Sin(a) * 0.38f);
                var to = new Vector3(Mathf.Cos(a) * 0.04f, 0.22f, Mathf.Sin(a) * 0.04f);
                var d = to - from;
                b.Mat("bark_broad").Frustum(world * Matrix4x4.TRS(from, Quaternion.FromToRotation(Vector3.up, d), new Vector3(1, d.magnitude, 1)), 5, 0.05f, 0.04f, new Color(0.7f, 0.65f, 0.6f, 0));
            }
            FlameModel.Add(b, world * Matrix4x4.TRS(new Vector3(0, 0.04f, 0), Quaternion.identity, new Vector3(0.6f, 0.75f, 0.6f)), 3);
            return b.ToMesh("hearth");
        }
    }
}
