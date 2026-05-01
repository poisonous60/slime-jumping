using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Drape a slope-filtered snow visual on a mesh-based platform. Spawns one child
    /// "SnowStrip_N" GameObject per upward-facing connected component of the host's
    /// triangulation; each child gets a snow mesh whose vertex displacement is driven
    /// by an MPB-bound 1D heightBuf (Phase 2 GPU shader path).
    ///
    /// Pure visual: no collider, no SurfaceTag — the slime always walks on the host's
    /// own collider. The snow follows the host's full 3D top surface (slopes, hills,
    /// heightmap bumps, undulation in Z), not just a planar cross-section.
    ///
    /// Phase 2 dynamics (this file):
    ///  - Optional barycentric subdivision (subdivLevels) so low-poly hosts have enough
    ///    vertex density for visible carve dips without changing surface shape.
    ///  - Registers (hostCollider → this) with SnowFieldRegistry on enable so a single
    ///    SnowStamper on the slime can route OnLanded events here in O(1).
    ///  - HandleLanded converts world contact to mesh-local and fans the carve out to
    ///    every child SnowStrip (each does its own AABB pre-filter).
    ///  - FixedUpdate ticks recovery only on strips currently dirty/recovering and
    ///    prunes them from the active set once fully recovered → idle cost 0.
    ///
    /// Phase 2.1 (current): per-vertex height now ships to the GPU as a
    /// StructuredBuffer&lt;float&gt; (_SnowHeightBuf) bound per-renderer via MPB; replaces
    /// the previous V×1 RFloat Texture2D so dense subdivided patches that exceed GPU
    /// max texture width upload cleanly.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Field")]
    public sealed class SnowField : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Tuning asset (deposit slope / mesh density / carve depth-radius / recovery rate / snow material). All snow tuning lives here so multiple platforms share one source of truth. If null, the inspector falls back to baked-in defaults.")]
        public SnowSettings settings;

        [Header("Source")]
        [Tooltip("MeshFilter whose sharedMesh provides the host triangulation. Auto-resolved on Reset to this GO or first child with a mesh.")]
        public MeshFilter sourceMeshFilter;

        [Header("Wiring")]
        [Tooltip("Host platform's Collider2D. Registered as the routing key in SnowFieldRegistry — SlimeController.SurfaceCollider must equal this for carve events to land here.")]
        public Collider2D hostCollider;

        [Header("Overlap masking")]
        [Tooltip("Drop snow triangles whose XY centroid lies inside another Collider2D (anything other than hostCollider on the layer mask below). Use to keep snow off regions hidden behind other platforms — only the externally exposed rim of the host top keeps snow, so overlapping meshes don't z-fight.")]
        public bool excludeOverlapWithOtherPlatforms = true;

        [Tooltip("Layers tested for overlap. Colliders on these layers (excluding hostCollider) mask out snow triangles. Defaults to all layers.")]
        public LayerMask overlapLayerMask = ~0;

        [Tooltip("Tiebreak when two SnowFields overlap each other. Higher priority keeps snow in the overlap; lower priority drops it. Equal priority falls back to hostCollider InstanceID (higher wins). Without this rule both fields would mask each other and the overlap stays bare.")]
        public int overlapPriority = 0;

        [Header("Debug")]
        public bool drawPatchOutlines = true;
        public bool drawVertNormals = false;

        // -- Settings accessors (settings asset wins, otherwise fall back) -------

        const float DefaultMaxSnowSlopeDeg = 45f;
        const int   DefaultMinPatchTris    = 3;
        const float DefaultRestThickness   = 0.25f;
        const int   DefaultEdgeTaperHops   = 1;
        const int   DefaultSubdivLevels    = 1;
        const float DefaultAccumRate       = 0.05f;

        public float MaxSnowSlopeDeg => settings != null ? settings.maxSnowSlopeDeg : DefaultMaxSnowSlopeDeg;
        public int   MinPatchTris    => settings != null ? settings.minPatchTris    : DefaultMinPatchTris;
        public float RestThickness   => settings != null ? settings.restThickness   : DefaultRestThickness;
        public int   EdgeTaperHops   => settings != null ? settings.edgeTaperHops   : DefaultEdgeTaperHops;
        public int   SubdivLevels    => settings != null ? settings.subdivLevels    : DefaultSubdivLevels;
        public float AccumulationRate=> settings != null ? settings.accumulationRate: DefaultAccumRate;
        public Material SnowMaterial => settings != null ? settings.snowMaterial    : null;

        // Cached host mesh hash so the editor watcher can prompt a rebuild when the host
        // mesh changes (Unity doesn't fire a callback on MeshFilter.sharedMesh swaps).
        [HideInInspector] public int lastHostMeshHash;

        // Dedupe delayCall storms — slider drags fire OnValidate many times per frame.
        [System.NonSerialized] bool rebuildScheduled;

        // Strips currently dirty (recently carved) or still recovering toward heightRest.
        // Idle SnowField has zero entries → FixedUpdate is a no-op.
        [System.NonSerialized] readonly HashSet<SnowStrip> activeStrips = new HashSet<SnowStrip>();
        [System.NonSerialized] List<SnowStrip> tickBuffer;

        // Track the collider we actually registered so OnDisable unregisters the same
        // key even if hostCollider got reassigned in the inspector mid-session.
        [System.NonSerialized] Collider2D registeredCollider;

        // Runtime clone of SnowSettings.snowMaterial with Phase 3 textures + keywords
        // applied. Created in RebuildNow, assigned to every child SnowStrip renderer,
        // disposed in ClearChildren / OnDisable / OnDestroy. The shared snowMaterial
        // asset itself is never mutated (matches the read-only invariant the rest of
        // the system already follows for per-strip heightBuf via MPB).
        [System.NonSerialized] Material runtimeMaterial;

        // Property IDs for runtime-material texture pushes.
        static readonly int IdGradientRamp    = Shader.PropertyToID("_GradientRamp");
        static readonly int IdFootprintAlbedo = Shader.PropertyToID("_FootprintAlbedo");
        static readonly int IdGlitterNoise    = Shader.PropertyToID("_GlitterNoise");
        const string KwSnowFootprint = "_SNOW_FOOTPRINT";
        const string KwSnowFresnel   = "_SNOW_FRESNEL";
        const string KwSnowGlitter   = "_SNOW_GLITTER";

        // -- Unity ---------------------------------------------------------------

        void Reset()
        {
            ResolveSourceMeshFilter();
            ResolveSettings();
            ResolveHostCollider();
            ScheduleRebuild();
        }

        void Awake()
        {
            ResolveSourceMeshFilter();
            ResolveSettings();
            ResolveHostCollider();
            if (Application.isPlaying) RebuildNow();
        }

        void OnEnable()
        {
            // Children spawned by RebuildNow use HideFlags.DontSave, so they're gone after
            // a scene reload / domain reload in edit mode. Re-emit them via the delayCall
            // path so the snow visual reappears without manual Rebuild Now clicks.
            if (!Application.isPlaying) { ScheduleRebuild(); return; }

            if (hostCollider != null)
            {
                SnowFieldRegistry.Register(hostCollider, this);
                registeredCollider = hostCollider;
            }
        }

        void OnDisable()
        {
            if (registeredCollider != null)
            {
                SnowFieldRegistry.Unregister(registeredCollider);
                registeredCollider = null;
            }
            // Flush any pending uploads before forgetting the strips — otherwise the GPU
            // texture stays at its last-uploaded value while heightCurrent[] has moved on
            // from recent recovery ticks. Toggling enable in play mode without a rebuild
            // would otherwise show stale snow depth until a fresh carve forces another flush.
            foreach (var s in activeStrips)
                if (s != null) s.FlushIfDirty();
            activeStrips.Clear();
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (activeStrips.Count == 0) return;

            float dt = Time.fixedDeltaTime;
            if (tickBuffer == null) tickBuffer = new List<SnowStrip>(activeStrips.Count);
            tickBuffer.Clear();
            tickBuffer.AddRange(activeStrips);
            for (int i = 0; i < tickBuffer.Count; i++)
            {
                var strip = tickBuffer[i];
                if (strip == null) { activeStrips.Remove(strip); continue; }
                bool stillBelow = strip.TickRecovery(dt, AccumulationRate);
                strip.FlushIfDirty();
                if (!stillBelow) activeStrips.Remove(strip);
            }
        }

        // -- Carve dispatch (called by SnowStamper) ------------------------------

        // Carve mapping fallback values (used only when no SnowSettings asset is wired).
        const float DefaultMinImpactSpeed   = 1.5f;
        const float DefaultDepthPerImpact   = 0.012f;
        const float DefaultDepthMax         = 0.20f;
        const float DefaultRadiusPerImpact  = 0.05f;
        const float DefaultRadiusMax        = 0.8f;

        /// <summary>Convert a world-space contact + raw impact speed into a per-strip
        /// gaussian carve. depth/radius mapping comes from SnowSettings so designers
        /// can retune one asset and every platform that points at it picks up the
        /// change. Soft impacts (below minImpactSpeed) are rejected. Mapping result
        /// is fanned out to the generic <see cref="Carve"/>.</summary>
        public void HandleLanded(Vector2 contactWorld, float impactSpeed)
        {
            float minImpact = settings != null ? settings.minImpactSpeed : DefaultMinImpactSpeed;
            if (impactSpeed < minImpact) return;

            float depthPerImpact  = settings != null ? settings.depthPerImpact  : DefaultDepthPerImpact;
            float depthMax        = settings != null ? settings.depthMax        : DefaultDepthMax;
            float radiusPerImpact = settings != null ? settings.radiusPerImpact : DefaultRadiusPerImpact;
            float radiusMax       = settings != null ? settings.radiusMax       : DefaultRadiusMax;

            float depth  = Mathf.Min(depthMax,  impactSpeed * depthPerImpact);
            float radius = Mathf.Min(radiusMax, impactSpeed * radiusPerImpact);
            Carve(contactWorld, depth, radius);
        }

        const CarveProfileType DefaultCarveProfile = CarveProfileType.RaisedCosine;
        public CarveProfileType CarveProfile => settings != null ? settings.carveProfile : DefaultCarveProfile;

        /// <summary>Generic carve at a world-space point. Use this for non-landing
        /// sources (blast radius, projectiles, debug tools) where the caller already
        /// knows the desired dip depth and radius — bypasses the impact-speed mapping
        /// in <see cref="HandleLanded"/>. Each child strip AABB-prefilters; only
        /// strips that actually changed enter the active recovery tick set.</summary>
        public void Carve(Vector2 contactWorld, float depth, float radius)
        {
            if (sourceMeshFilter == null) return;
            if (depth <= 0f || radius <= 0f) return;

            Vector3 localImpact = sourceMeshFilter.transform.InverseTransformPoint(
                new Vector3(contactWorld.x, contactWorld.y, 0f));

            var profile = CarveProfile;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                var strip = child.GetComponent<SnowStrip>();
                if (strip == null) continue;
                if (strip.CarveLocal(localImpact, depth, radius, profile))
                    activeStrips.Add(strip);
            }
        }

        /// <summary>Sample remaining snow coverage at a world-space xy point. 0 = bare
        /// or outside any patch, 1 = full untouched thickness. Read-only — gameplay's
        /// single entry point for "is this point on snow, and how much". Walks each
        /// child strip's AABB pre-filter then nearest-vertex; returns the maximum
        /// when patches overlap.</summary>
        public float SampleDepthRatio01(Vector2 worldXY)
        {
            if (sourceMeshFilter == null) return 0f;
            Vector3 local = sourceMeshFilter.transform.InverseTransformPoint(
                new Vector3(worldXY.x, worldXY.y, 0f));

            float best = 0f;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                var strip = child.GetComponent<SnowStrip>();
                if (strip == null) continue;
                float r = strip.SampleDepthRatio01(local);
                if (r > best) best = r;
            }
            return best;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            ResolveSourceMeshFilter();
            ResolveSettings();
            ResolveHostCollider();
            ScheduleRebuild();
        }

        void ScheduleRebuild()
        {
            if (Application.isPlaying) return;
            if (rebuildScheduled) return;
            rebuildScheduled = true;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                rebuildScheduled = false;
                if (this == null) return;
                RebuildNow();
            };
        }
#else
        void ScheduleRebuild() { /* runtime: handled by Awake */ }
#endif

        void ResolveSourceMeshFilter()
        {
            if (sourceMeshFilter != null && sourceMeshFilter.sharedMesh != null) return;
            var mf = GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) { sourceMeshFilter = mf; return; }
            var inChildren = GetComponentsInChildren<MeshFilter>(includeInactive: true);
            for (int i = 0; i < inChildren.Length; i++)
            {
                if (inChildren[i] == null) continue;
                if (inChildren[i].gameObject == gameObject) continue;
                if (inChildren[i].sharedMesh == null) continue;
                // Skip our own spawned SnowStrip_* children — picking one of those as the
                // host would feed the snow's previous output back in as input next rebuild.
                if (inChildren[i].GetComponent<SnowStrip>() != null) continue;
                sourceMeshFilter = inChildren[i];
                return;
            }
        }

        const string DefaultSettingsAssetPath = "Assets/Settings/Snow_Default.asset";

        void ResolveSettings()
        {
            if (settings != null) return;
#if UNITY_EDITOR
            var found = UnityEditor.AssetDatabase.LoadAssetAtPath<SnowSettings>(DefaultSettingsAssetPath);
            if (found != null) settings = found;
#endif
        }

        // Walk this GO and parent chain looking for a Collider2D. Mirrors the d2-style
        // setup where the platform's PolygonCollider2D lives on the parent and the mesh
        // (with this SnowField) lives on a child.
        void ResolveHostCollider()
        {
            if (hostCollider != null) return;
            var c = GetComponent<Collider2D>();
            if (c != null) { hostCollider = c; return; }
            var t = transform.parent;
            while (t != null)
            {
                c = t.GetComponent<Collider2D>();
                if (c != null) { hostCollider = c; return; }
                t = t.parent;
            }
        }

        // -- Build ---------------------------------------------------------------

        public void RebuildNow()
        {
            ResolveSourceMeshFilter();
            if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null)
            {
                ClearChildren();
                return;
            }

            var mesh = sourceMeshFilter.sharedMesh;
            var patches = SnowTopSurfaceExtractor.Extract(mesh, MaxSnowSlopeDeg);

            // Mask requires hostCollider to recognise its OWN collider as host —
            // otherwise the host's polygon hits OverlapPoint at every interior vert
            // and the entire patch gets dropped. Warn + skip masking instead of
            // silently producing nearly-empty snow.
            bool maskActive = excludeOverlapWithOtherPlatforms;
            if (maskActive && hostCollider == null)
            {
                Debug.LogWarning(
                    $"[SnowField] '{name}': excludeOverlapWithOtherPlatforms is ON but hostCollider " +
                    $"is unassigned — overlap mask is skipped (otherwise the host's own collider would " +
                    $"mask the entire patch). Assign Host Collider in the inspector to use the mask.",
                    this);
                maskActive = false;
            }

            ClearChildren();
            activeStrips.Clear();

            // Rebuild the runtime material clone so child renderers below pick it up
            // with current Phase 3 textures + keywords from SnowSettings applied.
            BuildRuntimeMaterial();

            int emitted = 0;
            for (int s = 0; s < patches.Count; s++)
            {
                var patch = patches[s];
                if (patch.verts == null || patch.verts.Length < 3) continue;
                int triCount = patch.tris != null ? patch.tris.Length / 3 : 0;
                // Min-tri filter on ORIGINAL host triangulation, not the subdivided result —
                // otherwise 1-tri noise patches get inflated past the threshold.
                if (triCount < MinPatchTris) continue;

                int subdiv = SubdivLevels;
                if (subdiv > 0)
                    patch = SnowPatchSubdivider.Subdivide(patch, subdiv);

                // Drop tris hidden behind other platforms so overlapping meshes don't
                // z-fight. Runs after Subdivide so the mask works at the same vertex
                // resolution the snow mesh will be emitted at; runs before BFS taper
                // so the new boundary verts get the same edge ramp as the original rim.
                if (maskActive)
                {
                    patch = MaskOutOverlap(patch, sourceMeshFilter.transform);
                    if (patch.verts == null || patch.verts.Length < 3) continue;
                    if (patch.tris == null || patch.tris.Length < 3) continue;
                }

                int V = patch.verts.Length;
                var heights = new float[V];
                int taperHops = EdgeTaperHops;
                float restThick = RestThickness;

                if (taperHops > 0 && patch.boundaryEdges != null && patch.boundaryEdges.Length >= 2)
                {
                    // Graph BFS: hopDist[v] = min number of edge traversals to reach a
                    // boundary vertex. Boundary verts get 0; their neighbors 1; etc.
                    // Independent of patch shape so thin/long strips keep their interior.
                    var hop = new int[V];
                    for (int i = 0; i < V; i++) hop[i] = -1;

                    // Adjacency from triangulation (undirected via tri edges). HashSet
                    // dedupes shared edges in O(1) — large patches with high-degree verts
                    // become quadratic with List.Contains.
                    var adj = new HashSet<int>[V];
                    for (int i = 0; i < V; i++) adj[i] = new HashSet<int>();
                    for (int t = 0; t < patch.tris.Length; t += 3)
                    {
                        int a = patch.tris[t], b = patch.tris[t + 1], c = patch.tris[t + 2];
                        adj[a].Add(b); adj[a].Add(c);
                        adj[b].Add(a); adj[b].Add(c);
                        adj[c].Add(a); adj[c].Add(b);
                    }

                    var queue = new Queue<int>();
                    for (int e = 0; e < patch.boundaryEdges.Length; e++)
                    {
                        int v = patch.boundaryEdges[e];
                        if (hop[v] != 0) { hop[v] = 0; queue.Enqueue(v); }
                    }
                    while (queue.Count > 0)
                    {
                        int u = queue.Dequeue();
                        foreach (int n in adj[u])
                        {
                            if (hop[n] == -1)
                            {
                                hop[n] = hop[u] + 1;
                                queue.Enqueue(n);
                            }
                        }
                    }

                    for (int v = 0; v < V; v++)
                    {
                        int h = hop[v] < 0 ? taperHops : hop[v];
                        float ramp = Mathf.Clamp01((float)h / taperHops);
                        heights[v] = restThick * Mathf.SmoothStep(0f, 1f, ramp);
                    }
                }
                else
                {
                    for (int i = 0; i < V; i++) heights[i] = restThick;
                }

                // Per-vertex slope-based height modulation. Replaces the previous
                // hard-coded smoothstep — designer now controls the angle→height
                // mapping via SnowSettings.slopeToHeightScale. Default curve fades
                // from full at 18° to 0 at 45°, mimicking the old behaviour.
                //
                // The face-level slope filter at SnowTopSurfaceExtractor cuts tris
                // whose face normal exceeds maxSnowSlopeDeg, but a vertex shared by
                // several borderline tris ends up with an averaged normal that can
                // read a non-trivial angle even when the surface is visually flat.
                // The user is aware of this and accepts it; mitigate by flattening
                // the low-angle portion of the curve if it bleeds onto level ground.
                {
                    var slopeCurve = settings != null ? settings.slopeToHeightScale : null;
                    if (slopeCurve != null && slopeCurve.length > 0)
                    {
                        for (int i = 0; i < V; i++)
                        {
                            float ny = Mathf.Clamp(patch.vertNormals[i].y, -1f, 1f);
                            float angleDeg = Mathf.Acos(ny) * Mathf.Rad2Deg;
                            float scale = slopeCurve.Evaluate(angleDeg);
                            if (scale < 0f) scale = 0f;
                            heights[i] *= scale;
                        }
                    }
                }

                var go = new GameObject($"SnowStrip_{emitted}");
                emitted++;
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                // DontSave (not just DontSaveInBuild): keep these out of the edit-mode scene
                // file too. Their meshes are runtime-built and would deserialize as null
                // after a reload, leaving empty SnowStrip GOs in the hierarchy. OnEnable
                // re-emits them via ScheduleRebuild whenever the scene/domain reloads.
                go.hideFlags = HideFlags.DontSave;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                if (runtimeMaterial != null) mr.sharedMaterial = runtimeMaterial;

                var ss = go.AddComponent<SnowStrip>();
                ss.verts = patch.verts;
                ss.vertNormals = patch.vertNormals;
                ss.tris = patch.tris;
                ss.boundaryEdges = patch.boundaryEdges;
                // heights is the per-vertex rest profile (taper-shaped). Phase 2 starts
                // heightCurrent at the rest profile (fresh, untouched snow); carve will
                // mutate heightCurrent only.
                ss.heightRest = heights;
                ss.heightCurrent = (float[])heights.Clone();
                ss.host = transform;

                var built = SnowMeshBuilder.Build(patch, heights, new SnowMeshBuilder.BuildParams
                {
                    meshName = $"SnowStrip_{emitted - 1}_Mesh",
                });
                mf.sharedMesh = built;

                ss.Init(mr);
            }

            lastHostMeshHash = ComputeHostMeshHash(mesh);
        }

        // True if `worldXY` lies inside any non-host Collider2D on the filter, with
        // SnowField↔SnowField arbitration via overlapPriority (higher wins; tiebreak
        // on hostCollider InstanceID). Used by MaskOutOverlap to test multiple sample
        // points per triangle.
        bool SamplePointHitsOther(Vector2 worldXY, ContactFilter2D filter, Collider2D[] hits, int myId)
        {
            int n = Physics2D.OverlapPoint(worldXY, filter, hits);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h == null) continue;
                if (h == hostCollider) continue;
                if (SnowFieldRegistry.TryGet(h, out var otherField) && otherField != null && otherField != this)
                {
                    if (otherField.overlapPriority > overlapPriority) return true;
                    if (otherField.overlapPriority < overlapPriority) continue;
                    if (h.GetInstanceID() > myId) return true;
                    continue;
                }
                return true;
            }
            return false;
        }

        // Filter out triangles where any of (3 verts, centroid) — in world XY — falls
        // inside another Collider2D on the mask. Rebuild the patch's verts/normals/
        // tris/boundaryEdges from the kept tris so the BFS taper downstream sees the
        // new directed boundary and curls the snow rim down at the cut edge.
        SnowTopSurfaceExtractor.TopPatch MaskOutOverlap(
            SnowTopSurfaceExtractor.TopPatch patch, Transform meshT)
        {
            if (patch.tris == null || patch.tris.Length < 3) return patch;

            var filter = ContactFilter2D.noFilter;
            filter.SetLayerMask(overlapLayerMask);
            filter.useTriggers = false;

            var hits = new Collider2D[8];
            int triCount = patch.tris.Length / 3;
            var keep = new bool[triCount];
            int kept = 0;

            // Sample 4 points per tri (3 verts + centroid) instead of centroid only.
            // Centroid-only kept tris that straddle the overlap boundary — one vertex
            // pokes into the other platform — producing a thin tearing band along the
            // seam. Dropping the tri if any of the 4 samples is inside another platform
            // lets the BFS taper close the gap cleanly.
            int myId = hostCollider != null ? hostCollider.GetInstanceID() : GetInstanceID();
            for (int t = 0; t < triCount; t++)
            {
                int ia = patch.tris[t * 3];
                int ib = patch.tris[t * 3 + 1];
                int ic = patch.tris[t * 3 + 2];
                Vector3 wa = meshT.TransformPoint(patch.verts[ia]);
                Vector3 wb = meshT.TransformPoint(patch.verts[ib]);
                Vector3 wc = meshT.TransformPoint(patch.verts[ic]);
                Vector2 p0 = new Vector2(wa.x, wa.y);
                Vector2 p1 = new Vector2(wb.x, wb.y);
                Vector2 p2 = new Vector2(wc.x, wc.y);
                Vector2 pc = new Vector2(
                    (wa.x + wb.x + wc.x) / 3f,
                    (wa.y + wb.y + wc.y) / 3f);

                bool dropTri = SamplePointHitsOther(p0, filter, hits, myId)
                            || SamplePointHitsOther(p1, filter, hits, myId)
                            || SamplePointHitsOther(p2, filter, hits, myId)
                            || SamplePointHitsOther(pc, filter, hits, myId);
                keep[t] = !dropTri;
                if (keep[t]) kept++;
            }

            if (kept == triCount) return patch;
            if (kept == 0)
            {
                return new SnowTopSurfaceExtractor.TopPatch
                {
                    verts = new Vector3[0],
                    vertNormals = new Vector3[0],
                    tris = new int[0],
                    boundaryEdges = new int[0],
                };
            }

            int origV = patch.verts.Length;
            var oldToNew = new int[origV];
            for (int i = 0; i < origV; i++) oldToNew[i] = -1;
            int newV = 0;
            for (int t = 0; t < triCount; t++)
            {
                if (!keep[t]) continue;
                for (int e = 0; e < 3; e++)
                {
                    int oi = patch.tris[t * 3 + e];
                    if (oldToNew[oi] < 0) oldToNew[oi] = newV++;
                }
            }

            var newVerts = new Vector3[newV];
            var newNorms = new Vector3[newV];
            for (int i = 0; i < origV; i++)
            {
                int n = oldToNew[i];
                if (n < 0) continue;
                newVerts[n] = patch.verts[i];
                newNorms[n] = patch.vertNormals[i];
            }

            var newTris = new int[kept * 3];
            int wt = 0;
            for (int t = 0; t < triCount; t++)
            {
                if (!keep[t]) continue;
                newTris[wt++] = oldToNew[patch.tris[t * 3]];
                newTris[wt++] = oldToNew[patch.tris[t * 3 + 1]];
                newTris[wt++] = oldToNew[patch.tris[t * 3 + 2]];
            }

            // Re-derive directed boundary edges (a→b appears, b→a doesn't) from the
            // filtered triangulation. Same scheme SnowTopSurfaceExtractor uses, so the
            // taper / side-strip downstream treats this output identically to a fresh
            // patch.
            var dirSet = new HashSet<long>();
            for (int t = 0; t < newTris.Length; t += 3)
            {
                int a = newTris[t], b = newTris[t + 1], c = newTris[t + 2];
                dirSet.Add(((long)a << 32) | (uint)b);
                dirSet.Add(((long)b << 32) | (uint)c);
                dirSet.Add(((long)c << 32) | (uint)a);
            }
            var bedges = new List<int>();
            foreach (long e in dirSet)
            {
                int a = (int)(e >> 32);
                int b = (int)(e & 0xFFFFFFFFL);
                long rev = ((long)b << 32) | (uint)a;
                if (!dirSet.Contains(rev)) { bedges.Add(a); bedges.Add(b); }
            }

            return new SnowTopSurfaceExtractor.TopPatch
            {
                verts = newVerts,
                vertNormals = newNorms,
                tris = newTris,
                boundaryEdges = bedges.ToArray(),
            };
        }

        void ClearChildren()
        {
            var toKill = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
            {
                var c = transform.GetChild(i);
                if (c != null && c.name.StartsWith("SnowStrip_"))
                    toKill.Add(c.gameObject);
            }
            foreach (var go in toKill)
            {
                // Free the runtime-built mesh first — destroying the GO alone leaks the
                // Mesh until GC runs, and every slider tweak rebuilds.
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var m = mf.sharedMesh;
                    mf.sharedMesh = null;
                    if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
                }
                if (Application.isPlaying) Destroy(go);
                else DestroyImmediate(go);
            }
        }

        // -- Runtime material (clone of SnowSettings.snowMaterial) --------------

        void BuildRuntimeMaterial()
        {
            DestroyRuntimeMaterial();

            var baseMat = SnowMaterial;
            if (baseMat == null) return;

            // Clone the asset so keyword/texture writes here never dirty the source.
            // HideFlags.DontSave keeps the clone out of the scene file — same posture
            // as the per-strip heightBuf.
            runtimeMaterial = new Material(baseMat)
            {
                name = baseMat.name + " (Snow Runtime)",
                hideFlags = HideFlags.DontSave,
            };

            ApplySettingsToRuntimeMaterial();
        }

        void ApplySettingsToRuntimeMaterial()
        {
            if (runtimeMaterial == null) return;

            // Textures: only push when set. Null-push would override the source
            // material's slot with a black 1×1 (Quibli reads ramps as black =
            // everything-shadowed). Skipping null lets the source material's bound
            // texture (or shader default) survive.
            if (settings != null)
            {
                if (settings.shadowRamp      != null) runtimeMaterial.SetTexture(IdGradientRamp,    settings.shadowRamp);
                if (settings.footprintAlbedo != null) runtimeMaterial.SetTexture(IdFootprintAlbedo, settings.footprintAlbedo);
                if (settings.glitterNoise    != null) runtimeMaterial.SetTexture(IdGlitterNoise,    settings.glitterNoise);

                ToggleKeyword(runtimeMaterial, KwSnowFootprint, settings.footprintEnabled);
                ToggleKeyword(runtimeMaterial, KwSnowFresnel,   settings.fresnelEnabled);
                ToggleKeyword(runtimeMaterial, KwSnowGlitter,   settings.glitterEnabled);
            }
        }

        static void ToggleKeyword(Material m, string keyword, bool enabled)
        {
            if (enabled) m.EnableKeyword(keyword);
            else         m.DisableKeyword(keyword);
        }

        void DestroyRuntimeMaterial()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }

        // -- Cleanup on component removal ---------------------------------------

        void OnDestroy()
        {
            // When the SnowField is removed (or the GO is destroyed), drop all child
            // SnowStrip_* GameObjects so the snow visual disappears with the component.
            // If the parent GO is being destroyed too, Unity destroys the children anyway
            // — our calls are redundant but null-checked.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i);
                if (c == null) continue;
                if (!c.name.StartsWith("SnowStrip_")) continue;
                if (Application.isPlaying) Destroy(c.gameObject);
                else DestroyImmediate(c.gameObject);
            }
            DestroyRuntimeMaterial();
        }

        // -- Host mesh hash (editor watcher uses this) --------------------------

        public static int ComputeHostMeshHash(Mesh mesh)
        {
            unchecked
            {
                int h = 17;
                if (mesh == null) return h;
                h = h * 31 + mesh.GetInstanceID();
                h = h * 31 + mesh.vertexCount;
                h = h * 31 + (mesh.triangles?.Length ?? 0);
                var verts = mesh.vertices;
                if (verts != null && verts.Length > 0)
                {
                    int step = Mathf.Max(1, verts.Length / 8);
                    for (int i = 0; i < verts.Length; i += step)
                        h = h * 31 + verts[i].GetHashCode();
                }
                return h;
            }
        }

        // -- Gizmos -------------------------------------------------------------

        void OnDrawGizmos()
        {
            if (!drawPatchOutlines && !drawVertNormals) return;
            if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null) return;

            var t = sourceMeshFilter.transform;

            // Recompute on draw (cheap for typical patch sizes; only runs when selected).
            var patches = SnowTopSurfaceExtractor.Extract(sourceMeshFilter.sharedMesh, MaxSnowSlopeDeg);

            if (drawPatchOutlines)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.95f);
                foreach (var patch in patches)
                {
                    for (int e = 0; e < patch.boundaryEdges.Length; e += 2)
                    {
                        Vector3 a = t.TransformPoint(patch.verts[patch.boundaryEdges[e]]);
                        Vector3 b = t.TransformPoint(patch.verts[patch.boundaryEdges[e + 1]]);
                        Gizmos.DrawLine(a, b);
                    }
                }
            }

            if (drawVertNormals)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
                foreach (var patch in patches)
                {
                    for (int i = 0; i < patch.verts.Length; i++)
                    {
                        Vector3 v = t.TransformPoint(patch.verts[i]);
                        Vector3 n = t.TransformVector(patch.vertNormals[i]);
                        Gizmos.DrawLine(v, v + n * (RestThickness * 0.5f));
                    }
                }
            }
        }
    }
}
