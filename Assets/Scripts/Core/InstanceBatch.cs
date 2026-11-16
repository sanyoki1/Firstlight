using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Firstlight.Core
{
    /// <summary>One instance for the InstancedFlat shader. Must match the HLSL struct (32 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct InstanceData
    {
        public float3 pos;
        public float yaw;
        public float3 scale;
        public uint color;

        public InstanceData(float3 pos, float yaw, float3 scale, uint color)
        {
            this.pos = pos; this.yaw = yaw; this.scale = scale; this.color = color;
        }
    }

    /// <summary>
    /// Draws up to <see cref="Capacity"/> copies of a mesh from a GPU buffer with one draw call
    /// (Graphics.RenderMeshPrimitives). No GameObjects involved.
    /// </summary>
    public sealed class InstanceBatch : IDisposable
    {
        static Material _sharedMaterial;
        public static Material SharedMaterial
        {
            get
            {
                if (_sharedMaterial == null)
                {
                    var shader = Resources.Load<Shader>("Shaders/InstancedFlat");
                    _sharedMaterial = new Material(shader) { name = "InstancedFlat", enableInstancing = true };
                }
                return _sharedMaterial;
            }
        }

        static Material _doubleSided;
        /// <summary>Same shader with back faces drawn, for crossed-quad plant sprites. They count as ground for the
        /// highlight x-ray (stencil 1), so a tuft never hides a selection ring.</summary>
        public static Material DoubleSidedMaterial
        {
            get
            {
                if (_doubleSided == null)
                {
                    _doubleSided = new Material(SharedMaterial) { name = "InstancedFlatDoubleSided" };
                    _doubleSided.SetFloat("_Cull", 0f);
                    _doubleSided.SetFloat("_StencilRef", 1f);
                }
                return _doubleSided;
            }
        }

        /// <summary>A copy of <paramref name="source"/> that draws only the parts hidden under the ground (terrain,
        /// water, ground cover): after everything else, where the depth test fails and the stencil says ground.</summary>
        public static Material XRayOf(Material source, string name)
        {
            var m = new Material(source) { name = name };
            m.SetFloat("_XRay", 1f);
            m.SetFloat("_ZTest", (float)CompareFunction.Greater);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_StencilRef", 1f);
            m.SetFloat("_StencilComp", (float)CompareFunction.Equal);
            m.SetFloat("_StencilPass", (float)StencilOp.Keep);
            m.renderQueue = 3100;   // after opaque and transparent geometry, outside the depth prepass and shadows
            return m;
        }

        public Mesh Mesh;
        public Material Material;
        public int Capacity { get; private set; }
        public int Count { get; private set; }
        public bool CastShadows = true;
        public Bounds WorldBounds = new Bounds(Vector3.zero, Vector3.one * 10000f);

        GraphicsBuffer _buffer;
        readonly MaterialPropertyBlock _mpb = new();
        static readonly int InstancesId = Shader.PropertyToID("_Instances");

        public InstanceBatch(Mesh mesh, int capacity, Material material = null)
        {
            Mesh = mesh;
            Material = material;
            Allocate(Mathf.Max(1, capacity));
        }

        void Allocate(int capacity)
        {
            _buffer?.Release();
            Capacity = capacity;
            _buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, capacity, Marshal.SizeOf<InstanceData>());
            _mpb.SetBuffer(InstancesId, _buffer);
        }

        public void Upload(NativeArray<InstanceData> data, int count)
        {
            if (count > Capacity) Allocate(Mathf.NextPowerOfTwo(count));
            Count = count;
            if (count > 0) _buffer.SetData(data, 0, 0, count);
        }

        public void Upload(InstanceData[] data, int count)
        {
            if (count > Capacity) Allocate(Mathf.NextPowerOfTwo(count));
            Count = count;
            if (count > 0) _buffer.SetData(data, 0, 0, count);
        }

        public void Draw(Camera cam = null)
        {
            if (Count <= 0) return;
            var rp = new RenderParams(Material != null ? Material : SharedMaterial)
            {
                worldBounds = WorldBounds,
                matProps = _mpb,
                shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                receiveShadows = true,
                camera = cam,
            };
            Graphics.RenderMeshPrimitives(rp, Mesh, 0, Count);
        }

        public void Dispose()
        {
            _buffer?.Release();
            _buffer = null;
        }
    }
}
