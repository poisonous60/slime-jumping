using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Per-patch data + GPU upload bridge for the snow visual.
    ///
    /// Phase 1 baked Geometry from heightCurrent at build time. Phase 2 keeps the mesh
    /// static (un-displaced verts; uv2 → heightTex coord; tangent.xyz → vertex-normal
    /// displacement direction) and instead uploads the per-vertex height array to a small
    /// 1D RFloat texture each FixedUpdate when carve / recovery actually changed values.
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
        // toward heightRest. Source of truth on the CPU; mirrored to heightTex on flush.
        [HideInInspector] public float[] heightCurrent;
        // Per-vertex max thickness (m). Phase 2: equals build-time taper-shaped restThickness.
        // Phase 4 will override per-vertex via ceiling raycast.
        [HideInInspector] public float[] heightRest;
        // The host transform (the MeshFilter GO). Reserved for Phase 4 ceiling-occlusion
        // (Physics2D.Raycast from each vert in world space needs the host's local→world
        // matrix) and Phase 5 slope-diffusion neighbour weights. Phase 2 doesn't read it.
        [HideInInspector] public Transform host;

        // -- GPU upload state (runtime only) -------------------------------------

        [System.NonSerialized] public Texture2D heightTex;
        [System.NonSerialized] MaterialPropertyBlock mpb;
        [System.NonSerialized] MeshRenderer cachedRenderer;
        [System.NonSerialized] public bool dirty;

        // xy-AABB of patch verts (mesh-local), cached at Init for carve pre-filter.
        [System.NonSerialized] public Vector2 aabbMin;
        [System.NonSerialized] public Vector2 aabbMax;

        static readonly int IdSnowHeightTex = Shader.PropertyToID("_SnowHeightTex");

        // -- Init / lifecycle -----------------------------------------------------

        /// <summary>Called by SnowField after data arrays + mesh are set. Allocates the
        /// 1D heightTex (V × 1 RFloat), computes the xy-AABB, pushes the initial
        /// heightCurrent values to the GPU.</summary>
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

            // Reuse existing texture if dimensions match (RebuildNow re-emit case).
            if (heightTex != null && heightTex.width == V && heightTex.height == 1) { /* keep */ }
            else
            {
                if (heightTex != null)
                {
                    if (Application.isPlaying) Destroy(heightTex);
                    else DestroyImmediate(heightTex);
                }
                heightTex = new Texture2D(V, 1, TextureFormat.RFloat, mipChain: false, linear: true)
                {
                    name = $"SnowHeightTex_{name}",
                    filterMode = FilterMode.Point,   // bilinear would bleed neighbouring patch verts
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.DontSave,
                };
            }

            dirty = true;
            FlushIfDirty();
        }

        void OnDestroy()
        {
            if (heightTex != null)
            {
                if (Application.isPlaying) Destroy(heightTex);
                else DestroyImmediate(heightTex);
                heightTex = null;
            }
        }

        // -- Carve / recovery -----------------------------------------------------

        /// <summary>Apply a gaussian dip at the given mesh-local position. Returns true
        /// if any vertex was actually deepened (caller can then add this strip to the
        /// active-tick set).</summary>
        public bool CarveLocal(Vector3 localImpactPos, float depth, float radius)
        {
            if (heightCurrent == null || verts == null) return false;
            if (depth <= 0f || radius <= 0f) return false;

            // AABB pre-filter — if impact is outside the strip's xy bounds (padded by
            // radius) every vertex is past the gaussian's effective range anyway. Skip
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
                // exp(-4 * (d/r)^2): falloff ~e^-4 ≈ 0.018 at d=r — effectively 0 by 1.5r.
                float falloff = Mathf.Exp(-4f * d2);
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
            if (heightTex == null || heightCurrent == null) { dirty = false; return; }
            if (cachedRenderer == null)
            {
                cachedRenderer = GetComponent<MeshRenderer>();
                if (cachedRenderer == null) { dirty = false; return; }
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();

            heightTex.SetPixelData(heightCurrent, 0);
            heightTex.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            cachedRenderer.GetPropertyBlock(mpb);
            mpb.SetTexture(IdSnowHeightTex, heightTex);
            cachedRenderer.SetPropertyBlock(mpb);

            dirty = false;
        }
    }
}
