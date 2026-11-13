using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Firstlight.Core
{
    /// <summary>
    /// Moves agents along cached flow fields. Positions are in grid units (cell x,y = world x,z).
    /// slot &lt; 0 means the agent is standing still. Close to the goal (directRadius) or when the field
    /// has no path, the agent walks straight at its goal.
    /// </summary>
    [BurstCompile]
    public struct MoveAgentsJob : IJobParallelFor
    {
        public NativeArray<float2> pos;
        public NativeArray<float> yaw;
        public NativeArray<byte> arrived;
        [ReadOnly] public NativeArray<float2> goal;
        [ReadOnly] public NativeArray<int> slot;
        [ReadOnly] public NativeArray<float> speed;
        [ReadOnly] public NativeArray<byte> fields;
        [ReadOnly] public NativeArray<byte> cost;
        public int width, height;
        public float dt;
        public float directRadius;

        public void Execute(int i)
        {
            int s = slot[i];
            if (s < 0) return;
            int cells = width * height;
            float2 p = pos[i];
            float2 g = goal[i];
            int cx = math.clamp((int)math.floor(p.x), 0, width - 1);
            int cy = math.clamp((int)math.floor(p.y), 0, height - 1);
            int c = cy * width + cx;
            byte cc = cost[c];
            float step = speed[i] * dt / math.max(1, cc == 0 ? 1 : cc * 0.5f + 0.5f);

            float2 aim;
            byte d = fields[s * cells + c];
            bool direct = d == FlowDir.Target || d == FlowDir.None || math.distancesq(p, g) < directRadius * directRadius;
            if (direct) aim = g;
            else
            {
                int2 o = FlowDir.Offset(d);
                int2 n1 = new int2(cx, cy) + o;
                aim = new float2(n1.x + 0.5f, n1.y + 0.5f);
                // Look one cell further to smooth zig-zags.
                if (n1.x >= 0 && n1.y >= 0 && n1.x < width && n1.y < height)
                {
                    byte d2 = fields[s * cells + n1.y * width + n1.x];
                    if (d2 < 8)
                    {
                        int2 n2 = n1 + FlowDir.Offset(d2);
                        aim = math.lerp(aim, new float2(n2.x + 0.5f, n2.y + 0.5f), 0.5f);
                    }
                    else if (d2 == FlowDir.Target) aim = math.lerp(aim, g, 0.5f);
                }
            }

            float2 delta = aim - p;
            float len = math.length(delta);
            if (direct && len <= step)
            {
                pos[i] = g;
                arrived[i] = 1;
                return;
            }
            if (len > 1e-5f)
            {
                p += delta / len * math.min(step, len);
                yaw[i] = math.atan2(delta.x, delta.y);
            }
            pos[i] = p;
        }
    }

    /// <summary>
    /// Picks the agents worth drawing (inside the view frustum and within a radius of the camera focus)
    /// and writes their instance data. Output capacity must be >= agent count.
    /// </summary>
    [BurstCompile]
    public struct CullAgentsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> pos;
        [ReadOnly] public NativeArray<float> yaw;
        [ReadOnly] public NativeArray<uint> color;
        [ReadOnly] public NativeArray<byte> hidden;
        [ReadOnly] public NativeArray<float> cornerHeights; // (width+1)*(height+1), may be empty
        [ReadOnly] public NativeArray<float4> planes;       // 6 frustum planes (xyz normal, w distance)
        public NativeList<InstanceData>.ParallelWriter output;
        public float2 focus;
        public float radiusSq;
        public float scale;
        public int width, height;

        public void Execute(int i)
        {
            if (hidden.Length > 0 && hidden[i] != 0) return;
            float2 p = pos[i];
            if (math.distancesq(p, focus) > radiusSq) return;
            float h = cornerHeights.Length > 0 ? SampleHeight(cornerHeights, width, height, p) : 0f;
            float3 wp = new float3(p.x, h, p.y);
            for (int k = 0; k < 6; k++)
            {
                float4 pl = planes[k];
                if (math.dot(pl.xyz, wp + new float3(0, 0.4f, 0)) + pl.w < -0.8f) return;
            }
            output.AddNoResize(new InstanceData(wp, yaw[i], new float3(scale), color[i]));
        }

        public static float SampleHeight(NativeArray<float> corners, int width, int height, float2 p)
        {
            float x = math.clamp(p.x, 0, width - 0.001f), y = math.clamp(p.y, 0, height - 0.001f);
            int ix = (int)x, iy = (int)y;
            float fx = x - ix, fy = y - iy;
            int w1 = width + 1;
            float h00 = corners[iy * w1 + ix], h10 = corners[iy * w1 + ix + 1];
            float h01 = corners[(iy + 1) * w1 + ix], h11 = corners[(iy + 1) * w1 + ix + 1];
            return math.lerp(math.lerp(h00, h10, fx), math.lerp(h01, h11, fx), fy);
        }

        public static void FillPlanes(Camera cam, NativeArray<float4> planes)
        {
            var ps = GeometryUtility.CalculateFrustumPlanes(cam);
            for (int k = 0; k < 6; k++) planes[k] = new float4(ps[k].normal, ps[k].distance);
        }
    }
}
