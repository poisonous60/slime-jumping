using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Per-patch data + GPU upload bridge for the snow visual.
    ///
    /// Phase 1 baked geometry from heightCurrent at build time. Phase 2 onward keeps
    /// the mesh static (un-displaced verts; uv2.x → vertex index, uv2.y → heightRest;
    /// tangent.xyz → vertex-normal displacement direction) and uploads the per-vertex
    /// current-height array to a ComputeBuffer (StructuredBuffer&lt;float&gt; on the GPU
    /// side) each FixedUpdate when carve / recovery actually changed values.
    ///
    /// ComputeBuffer (vs the previous V×1 RFloat Texture2D) lifts the ~16384 vertex
    /// cap from GPU max texture width — patches at high subdivLevels can exceed that
    /// after subdivision and the texture path threw "invalid parameters" on alloc.
    /// Requires shader target 4.5 (already set on the ForwardLit pass).
    ///
    /// All shader-side per-instance state lives in a MaterialPropertyBlock — the shared
    /// material asset is never modified.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Strip")]
    public sealed class SnowStrip : MonoBehaviour
    {
        // -- Topology / data (populated by SnowField.RebuildNow) ------------------

        // Patch verts in mesh-local space (matches host MeshFilter's local space).
        [HideInInspector] public Vector3[] verts;
        // Per-vertex normals averaged across kept top-facing tris. Drives shader
        // displacement direction (also written into mesh.tangent.xyz).
        [HideInInspector] public Vector3[] vertNormals;
        // Patch triangulation (CCW), indices into verts.
        [HideInInspector] public int[] tris;
        // Directed boundary edges as pairs [a0, b0, a1, b1, ...].
        [HideInInspector] public int[] boundaryEdges;
        // Per-vertex current snow thickness (m). Carve subtracts, TickRecovery lerps
        // toward heightRest. Source of truth on the CPU; mirrored to heightBuf on flush.
        [HideInInspector] public float[] heightCurrent;
        // Per-vertex max thickness (m). Phase 2: equals build-time taper-shaped restThickness.
        // Phase 4 will override per-vertex via ceiling raycast.
        [HideInInspector] public float[] heightRest;
        // The host transform (the MeshFilter GO). Reserved for Phase 4 ceiling-occlusion
        // (Physics2D.Raycast from each vert in world space needs the host's local→world
        // matrix) and Phase 5 slope-diffusion neighbour weights. Phase 2 doesn't read it.
        [HideInInspector] public Transform host;

        // -- GPU upload state (runtime only) -------------------------------------

        [System.NonSerialized] public ComputeBuffer heightBuf;
        [System.NonSerialized] MaterialPropertyBlock mpb;
        [System.NonSerialized] MeshRenderer cachedRenderer;
        [System.NonSerialized] public bool dirty;

        // xy-AABB of patch verts (mesh-local), cached at Init for carve pre-filter.
        [System.NonSerialized] public Vector2 aabbMin;
        [System.NonSerialized] public Vector2 aabbMax;

        static readonly int IdSnowHeightBuf = Shader.PropertyToID("_SnowHeightBuf");

        // -- Init / lifecycle -----------------------------------------------------

        /// <summary>Called by SnowField after data arrays + mesh are set. Allocates the
        /// heightBuf (V × float StructuredBuffer), computes the xy-AABB, pushes the
        /// initial heightCurrent values to the GPU.</summary>
        public void Init(MeshRenderer renderer)
        {
            cachedRenderer = renderer;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            int V = verts != null ? verts.Length : 0;
            if (V == 0) return;

            // AABB on xy projection — carve impact is compared in mesh-local xy.
            aabbMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            aabbMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < V; i++)
            {
                var v = verts[i];
                if (v.x < aabbMin.x) aabbMin.x = v.x;
                if (v.y < aabbMin.y) aabbMin.y = v.y;
                if (v.x > aabbMax.x) aabbMax.x = v.x;
                if (v.y > aabbMax.y) aabbMax.y = v.y;
            }

            // Reuse existing buffer if size matches (RebuildNow re-emit case).
            if (heightBuf != null && heightBuf.IsValid() && heightBuf.count == V) { /* keep */ }
            else
            {
                if (heightBuf != null) { heightBuf.Release(); heightBuf = null; }
                heightBuf = new ComputeBuffer(V, sizeof(float), ComputeBufferType.Default);
            }

            dirty = true;
            FlushIfDirty();
        }

        void OnDestroy()
        {
            if (heightBuf != null)
            {
                heightBuf.Release();
                heightBuf = null;
            }
        }

        void OnDisable()
        {
            // Domain reload doesn't fire OnDestroy on script-recompiled instances —
            // release here too so the buffer doesn't leak across reloads. Init/Flush
            // re-allocate as needed.
            if (heightBuf != null)
            {
                heightBuf.Release();
                heightBuf = null;
            }
        }

        // -- Carve / recovery -----------------------------------------------------

        /// <summary>Apply a dip at the given mesh-local position using the chosen
        /// falloff profile. Returns true if any vertex was actually deepened (caller
        /// can then add this strip to the active-tick set).</summary>
        public bool CarveLocal(Vector3 localImpactPos, float depth, float radius, CarveProfileType profile = CarveProfileType.RaisedCosine)
        {
            if (heightCurrent == null || verts == null) return false;
            if (depth <= 0f || radius <= 0f) return false;

            // AABB pre-filter — if impact is outside the strip's xy bounds (padded by
            // radius) every vertex is past the falloff's effective range anyway. Skip
            // the V loop entirely for unrelated strips on the same SnowField.
            if (localImpactPos.x < aabbMin.x - radius || localImpactPos.x > aabbMax.x + radius ||
                localImpactPos.y < aabbMin.y - radius || localImpactPos.y > aabbMax.y + radius)
                return false;

            float invR = 1f / radius;
            float ix = localImpactPos.x;
            float iy = localImpactPos.y;
            bool anyChange = false;
            for (int v = 0; v < heightCurrent.Length; v++)
            {
                float dx = (verts[v].x - ix) * invR;
                float dy = (verts[v].y - iy) * invR;
                float d2 = dx * dx + dy * dy;
                float falloff;
                switch (profile)
                {
                    case CarveProfileType.Gaussian:
                        // exp(-4·(d/r)²): falloff ~e^-4 ≈ 0.018 at d=r.
                        falloff = Mathf.Exp(-4f * d2);
                        break;
                    case CarveProfileType.SphericalCap:
                        falloff = d2 >= 1f ? 0f : Mathf.Sqrt(1f - d2);
                        break;
                    case CarveProfileType.Parabola:
                        falloff = d2 >= 1f ? 0f : (1f - d2);
                        break;
                    case CarveProfileType.RaisedCosine:
                    default:
                        if (d2 >= 1f) { falloff = 0f; }
                        else
                        {
                            float d = Mathf.Sqrt(d2);
                            falloff = 0.5f * (1f + Mathf.Cos(Mathf.PI * d));
                        }
                        break;
                }
                float cut = depth * falloff;
                if (cut < 1e-5f) continue;
                float nh = heightCurrent[v] - cut;
                if (nh < 0f) nh = 0f;
                if (nh < heightCurrent[v])
                {
                    heightCurrent[v] = nh;
                    anyChange = true;
                }
            }
            if (anyChange) dirty = true;
            return anyChange;
        }

        /// <summary>Sample remaining snow coverage at a mesh-local xy position. Returns
        /// 0 outside the patch xy AABB or when the strip has no data, otherwise the
        /// nearest-vertex ratio clamp01(heightCurrent / heightRest). 0 = bare ground
        /// (carved through or natural taper edge), 1 = full untouched thickness.
        ///
        /// Nearest-vertex (vs barycentric tri walk) is intentional: subdivLevels >= 1
        /// makes vertex spacing well below the slime's contact resolution, and the
        /// caller is per-FixedUpdate hot-path (slide gravity blend) — a tri walk would
        /// dominate cost without a perceptible quality gain.</summary>
        public float SampleDepthRatio01(Vector3 localXY)
        {
            if (heightCurrent == null || heightRest == null || verts == null) return 0f;
            if (heightCurrent.Length == 0) return 0f;

            // AABB pre-filter — the slide blend asks every strip on every FU; skip the
            // V loop entirely for unrelated patches on the same SnowField.
            if (localXY.x < aabbMin.x || localXY.x > aabbMax.x ||
                localXY.y < aabbMin.y || localXY.y > aabbMax.y)
                return 0f;

            float bestD2 = float.PositiveInfinity;
            int bestIdx = -1;
            for (int i = 0; i < verts.Length; i++)
            {
                float dx = verts[i].x - localXY.x;
                float dy = verts[i].y - localXY.y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
            }
            if (bestIdx < 0) return 0f;
            float rest = heightRest[bestIdx];
            if (rest <= 1e-5f) return 0f;
            return Mathf.Clamp01(heightCurrent[bestIdx] / rest);
        }

        /// <summary>Lerp heightCurrent toward heightRest. Returns true while any vertex
        /// is still below its rest value (i.e., this strip should keep ticking next
        /// frame); false once everything is fully recovered.</summary>
        public bool TickRecovery(float dt, float accumulationRate)
        {
            if (heightCurrent == null || heightRest == null) return false;
            float step = accumulationRate * dt;
            if (step <= 0f) return false;

            bool anyBelow = false;
            bool anyChange = false;
            for (int v = 0; v < heightCurrent.Length; v++)
            {
                float rest = heightRest[v];
                float cur = heightCurrent[v];
                if (cur >= rest) continue;
                float nc = cur + step;
                if (nc > rest) nc = rest;
                heightCurrent[v] = nc;
                anyChange = true;
                if (nc < rest) anyBelow = true;
            }
            if (anyChange) dirty = true;
            return anyBelow;
        }

        /// <summary>Push heightCurrent[] to the GPU via the per-renderer MPB. No-op if
        /// nothing has changed since the last flush.</summary>
        public void FlushIfDirty()
        {
            if (!dirty) return;
            if (heightCurrent == null) { dirty = false; return; }
            if (heightBuf == null || !heightBuf.IsValid() || heightBuf.count != heightCurrent.Length)
            {
                if (heightBuf != null) heightBuf.Release();
                heightBuf = new ComputeBuffer(heightCurrent.Length, sizeof(float), ComputeBufferType.Default);
            }
            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponent<MeshRenderer>();
                if (cachedRenderer == null) { dirty = false; return; }
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();

            heightBuf.SetData(heightCurrent);

            cachedRenderer.GetPropertyBlock(mpb);
            mpb.SetBuffer(IdSnowHeightBuf, heightBuf);
            cachedRenderer.SetPropertyBlock(mpb);

            dirty = false;
        }
    }
}
