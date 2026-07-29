using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>Per-patch data + GPU upload bridge for the snow visual. Mesh stays static
    /// (uv2.x → vertex index, uv2.y → heightRest, tangent.xyz → displacement dir); heightCurrent
    /// is uploaded to a ComputeBuffer each FixedUpdate when carve/recovery changed it.
    /// ComputeBuffer, not a V×1 RFloat texture: subdivided patches exceed the ~16384 max texture
    /// width and alloc threw. Needs shader target 4.5. Per-instance state lives in an MPB.</summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Strip")]
    public sealed class SnowStrip : MonoBehaviour
    {
        // -- Topology / data (populated by SnowField.RebuildNow) ------------------

        // Patch verts in mesh-local space (matches host MeshFilter's local space).
        [HideInInspector] public Vector3[] verts;
        // Per-vertex normals from kept top-facing tris; shader displacement dir (mesh.tangent.xyz).
        [HideInInspector] public Vector3[] vertNormals;
        // Patch triangulation (CCW), indices into verts.
        [HideInInspector] public int[] tris;
        // Directed boundary edges as pairs [a0, b0, a1, b1, ...].
        [HideInInspector] public int[] boundaryEdges;
        // Per-vertex current snow thickness (m). CPU source of truth, mirrored to heightBuf on flush.
        [HideInInspector] public float[] heightCurrent;
        // Per-vertex max thickness (m), taper-shaped at build time.
        [HideInInspector] public float[] heightRest;
        // The host transform (the MeshFilter GO).
        [HideInInspector] public Transform host;

        // -- GPU upload state (runtime only) -------------------------------------

        [System.NonSerialized] public ComputeBuffer heightBuf;
        [System.NonSerialized] MaterialPropertyBlock mpb;
        [System.NonSerialized] MeshRenderer cachedRenderer;
        [System.NonSerialized] public bool dirty;

        // xy-AABB of patch verts (mesh-local), cached at Init for carve pre-filter.
        [System.NonSerialized] public Vector2 aabbMin;
        [System.NonSerialized] public Vector2 aabbMax;

        // Largest heightRest in the strip, used as the sampler's AABB margin: the displaced
        // snow shell bulges past the un-displaced xy footprint by up to this much on tilted hosts.
        [System.NonSerialized] public float maxHeightRest;

        static readonly int IdSnowHeightBuf = Shader.PropertyToID("_SnowHeightBuf");

        // -- Init / lifecycle -----------------------------------------------------

        /// <summary>Called by SnowField once data arrays + mesh are set: allocs heightBuf, computes the xy-AABB, pushes initial heights.</summary>
        public void Init(MeshRenderer renderer)
        {
            cachedRenderer = renderer;
            if (mpb == null) mpb = new MaterialPropertyBlock();

            int V = verts != null ? verts.Length : 0;
            if (V == 0) return;

            // xy AABB (carve impacts are compared in mesh-local xy) + largest heightRest, one pass.
            aabbMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            aabbMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            maxHeightRest = 0f;
            int restLen = heightRest != null ? heightRest.Length : 0;
            for (int i = 0; i < V; i++)
            {
                var v = verts[i];
                if (v.x < aabbMin.x) aabbMin.x = v.x;
                if (v.y < aabbMin.y) aabbMin.y = v.y;
                if (v.x > aabbMax.x) aabbMax.x = v.x;
                if (v.y > aabbMax.y) aabbMax.y = v.y;
                if (i < restLen && heightRest[i] > maxHeightRest) maxHeightRest = heightRest[i];
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
            // Domain reload doesn't fire OnDestroy on recompiled instances — release here
            // too or the buffer leaks across reloads. Init/Flush re-allocate.
            if (heightBuf != null)
            {
                heightBuf.Release();
                heightBuf = null;
            }
        }

        // -- Carve / recovery -----------------------------------------------------

        /// <summary>Dip at a mesh-local position; true if any vertex deepened (caller then adds the strip to the active-tick set).</summary>
        public bool CarveLocal(Vector3 localImpactPos, float depth, float radius, CarveProfileType profile = CarveProfileType.RaisedCosine)
        {
            if (heightCurrent == null || verts == null) return false;
            if (depth <= 0f || radius <= 0f) return false;

            // AABB pre-filter — skips the V loop for the other strips on the same SnowField.
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

        /// <summary>Mirror of <see cref="CarveLocal"/> pushing heightCurrent up, capped per vertex at
        /// heightRest * heightMaxMultiplier — so bare verts (heightRest 0) can never mound.</summary>
        public bool RaiseLocal(Vector3 localImpactPos, float raise, float radius, CarveProfileType profile, float heightMaxMultiplier)
        {
            if (heightCurrent == null || heightRest == null || verts == null) return false;
            if (raise <= 0f || radius <= 0f) return false;
            if (heightMaxMultiplier <= 1f) return false;

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
                float push = raise * falloff;
                if (push < 1e-5f) continue;
                float cap = heightRest[v] * heightMaxMultiplier;
                if (cap <= 1e-5f) continue;
                float nh = heightCurrent[v] + push;
                if (nh > cap) nh = cap;
                if (nh > heightCurrent[v])
                {
                    heightCurrent[v] = nh;
                    anyChange = true;
                }
            }
            if (anyChange) dirty = true;
            return anyChange;
        }

        /// <summary>Remaining snow coverage at a mesh-local xy: nearest-vertex clamp01(heightCurrent /
        /// heightRest), 0 outside the patch. Nearest-vertex not barycentric tri walk — at subdivLevels
        /// >= 1 spacing is below slime contact resolution and the caller is a per-FixedUpdate hot path.</summary>
        public float SampleDepthRatio01(Vector3 localXY)
        {
            if (heightCurrent == null || heightRest == null || verts == null) return 0f;
            if (heightCurrent.Length == 0) return 0f;

            // Margin = maxHeightRest: without it a slime standing on the bulged shell edge
            // reads off-patch (depth 0) and Plow drag never engages.
            float margin = maxHeightRest;
            float dxOut = 0f, dyOut = 0f;
            if (localXY.x < aabbMin.x) dxOut = aabbMin.x - localXY.x;
            else if (localXY.x > aabbMax.x) dxOut = localXY.x - aabbMax.x;
            if (localXY.y < aabbMin.y) dyOut = aabbMin.y - localXY.y;
            else if (localXY.y > aabbMax.y) dyOut = localXY.y - aabbMax.y;
            float outDist2 = dxOut * dxOut + dyOut * dyOut;
            if (outDist2 > margin * margin) return 0f;

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
            float ratio = Mathf.Clamp01(heightCurrent[bestIdx] / rest);

            // Taper across the margin zone instead of a constant boundary value; inside the
            // AABB outDist2 is 0 → no attenuation.
            if (outDist2 > 0f && margin > 1e-5f)
            {
                float taper = 1f - Mathf.Sqrt(outDist2) / margin;
                if (taper <= 0f) return 0f;
                ratio *= taper;
            }
            return ratio;
        }

        /// <summary>Walk heightCurrent toward heightRest: carved verts up at accumulationRate, raised verts
        /// down at settleRate (0 = mounds stay forever). True while any vertex can still move.</summary>
        public bool TickRecovery(float dt, float accumulationRate, float settleRate)
        {
            if (heightCurrent == null || heightRest == null) return false;
            float upStep   = accumulationRate * dt;
            float downStep = settleRate       * dt;
            if (upStep <= 0f && downStep <= 0f) return false;

            // 회복률이 0 인 방향의 vertex 는 영원히 못 변하므로 anyDifferent 에 안 더함 —
            // 안 그러면 settleRate=0 인 카브된 strip 이 매 FixedUpdate 빈 작업으로 영원히 ticked.
            bool anyDifferent = false;
            bool anyChange = false;
            for (int v = 0; v < heightCurrent.Length; v++)
            {
                float rest = heightRest[v];
                float cur = heightCurrent[v];
                if (cur < rest)
                {
                    if (upStep <= 0f) continue;
                    float nc = cur + upStep;
                    if (nc > rest) nc = rest;
                    heightCurrent[v] = nc;
                    anyChange = true;
                    if (nc < rest) anyDifferent = true;
                }
                else if (cur > rest)
                {
                    if (downStep <= 0f) continue;
                    float nc = cur - downStep;
                    if (nc < rest) nc = rest;
                    heightCurrent[v] = nc;
                    anyChange = true;
                    if (nc > rest) anyDifferent = true;
                }
            }
            if (anyChange) dirty = true;
            return anyDifferent;
        }

        /// <summary>True iff the nearest mesh-local-xy vert has heightRest > 0 — the build pipeline's
        /// unified in-footprint output. Hands back that index so callers skip a second sweep.</summary>
        public bool TryFindNearestSnowVert(Vector3 localXY, out int vertIdx)
        {
            vertIdx = -1;
            if (verts == null || heightRest == null || verts.Length == 0) return false;

            float margin = maxHeightRest;
            float dxOut = 0f, dyOut = 0f;
            if (localXY.x < aabbMin.x) dxOut = aabbMin.x - localXY.x;
            else if (localXY.x > aabbMax.x) dxOut = localXY.x - aabbMax.x;
            if (localXY.y < aabbMin.y) dyOut = aabbMin.y - localXY.y;
            else if (localXY.y > aabbMax.y) dyOut = localXY.y - aabbMax.y;
            if (dxOut * dxOut + dyOut * dyOut > margin * margin) return false;

            float bestD2 = float.PositiveInfinity;
            int bestIdx = -1;
            for (int i = 0; i < verts.Length; i++)
            {
                float dx = verts[i].x - localXY.x;
                float dy = verts[i].y - localXY.y;
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
            }
            if (bestIdx < 0) return false;
            vertIdx = bestIdx;
            return heightRest[bestIdx] > 1e-5f;
        }

        /// <summary>Push heightCurrent[] to the GPU via the per-renderer MPB. No-op if not dirty.</summary>
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
