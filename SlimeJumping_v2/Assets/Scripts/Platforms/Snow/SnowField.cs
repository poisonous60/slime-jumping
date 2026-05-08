using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>How <see cref="SnowField"/> decides what to drop from each snow tri
    /// when masking against foreign colliders. See SnowField.maskMode tooltip.</summary>
    public enum OverlapMaskMode
    {
        /// <summary>Legacy 4-sample drop (default). Drops a tri if any of its 3 verts
        /// or centroid is inside any foreign collider. Cascades via shared verts.</summary>
        SampleBased = 0,

        /// <summary>Sutherland-Hodgman geometric subtraction. Clips each tri exactly at
        /// the foreign collider's edges, producing kept geometry == (tri ∩ NOT foreign).
        /// Falls back to SampleBased per pair for unsupported foreigns.</summary>
        GeometricClipping = 1,
    }

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

        [Tooltip("Per-field override for the deposit slope angle. When OFF (default), this field uses settings.maxSnowSlopeDeg from the SnowSettings asset. When ON, maxSnowSlopeDegOverride below replaces it for THIS SnowField only — settings asset is not mutated. Use to tune one platform's snow extent without forking the whole settings asset.")]
        public bool overrideMaxSnowSlope = false;

        [Tooltip("Override value for the deposit slope angle (degrees from horizontal). Triangles whose face normal exceeds this stay bare. Only consumed when overrideMaxSnowSlope is true.")]
        [Range(0f, 89f)] public float maxSnowSlopeDegOverride = 45f;

        [Header("Source")]
        [Tooltip("MeshFilter whose sharedMesh provides the host triangulation. Auto-resolved on Reset to this GO or first child with a mesh.")]
        public MeshFilter sourceMeshFilter;

        [Header("Wiring")]
        [Tooltip("Host platform's Collider2D. Registered as the routing key in SnowFieldRegistry — SlimeController.SurfaceCollider must equal this for carve events to land here.")]
        public Collider2D hostCollider;

        [Header("Overlap masking")]
        [Tooltip("Drop snow triangles whose XY centroid lies inside another Collider2D (anything other than hostCollider on the layer mask below). Use to keep snow off regions hidden behind other platforms — only the externally exposed rim of the host top keeps snow, so overlapping meshes don't z-fight.")]
        public bool excludeOverlapWithOtherPlatforms = true;

        [Tooltip("How the overlap mask decides what to drop from each snow triangle. SampleBased (default): drops a whole tri if any of its 3 verts or centroid lies inside a foreign collider — fast, matches existing scene behavior, but cascades through shared verts so the visible gap is wider than the foreign's actual footprint. GeometricClipping: clips each tri exactly at the foreign collider's edges via Sutherland–Hodgman, keeping only (tri ∩ NOT foreign). Tighter rim, no cascade. Falls back to sample-based per (tri, foreign) pair for unsupported foreign types (concave / multi-path Polygon, Circle, Capsule, Edge, Composite, foreign fully inside one tri). Default is SampleBased so existing scenes keep producing identical snow without intervention.")]
        public OverlapMaskMode maskMode = OverlapMaskMode.SampleBased;

        [Tooltip("Layers tested for overlap. Colliders on these layers (excluding hostCollider) mask out snow triangles. Defaults to all layers.")]
        public LayerMask overlapLayerMask = ~0;

        [Tooltip("Tiebreak when two SnowFields overlap each other. Higher priority keeps snow in the overlap; lower priority drops it. Equal priority falls back to hostCollider InstanceID (higher wins). Without this rule both fields would mask each other and the overlap stays bare.")]
        public int overlapPriority = 0;

        [Tooltip("When true, the overlap mask only fires if the foreign collider's world Z is within ±maskZWindow of the snow triangle's world Z. Lets a single SnowField on a Z-deep mesh keep snow on its back-Z slice (which sits in the void behind foreground objects) while still masking the front-Z slice that shares Z with foreground colliders. When false (default) the mask fires on any XY overlap regardless of Z, matching the original behavior.")]
        public bool maskUsesZ = false;

        [Tooltip("World-Z window for the maskUsesZ check. A foreign collider at world Z = Cz only masks a snow triangle whose world Z is in [Cz - maskZWindow, Cz + maskZWindow]. Smaller = stricter (only same-Z foregrounds mask), larger = looser (deeper foregrounds also mask). Typical 0.2 ~ 0.5. Ignored when maskUsesZ is false.")]
        [Min(0f)] public float maskZWindow = 0.5f;

        [Header("Mask overrides")]
        [Tooltip("Collider2D override regions that RESTORE snow tris the overlap mask would otherwise drop — use to plug seam gaps where the foreground-overlap test was too aggressive. A snow triangle is restored when it actually shares area with any collider listed here (verts of either inside the other, or edges cross). Supported with full-precision overlap: PolygonCollider2D and BoxCollider2D. Other Collider2D types (Circle, Capsule, Edge, Composite) fall back to a cheap 'tri vertex inside collider' check — works but coarser. Author by adding an empty GameObject with the desired collider over each gap, set Is Trigger = true on the collider (otherwise it would itself act as a foreign mask), then drag the GameObject's collider into this list. No effect when excludeOverlapWithOtherPlatforms is off (everything is kept anyway). Output is tri-granular — bump SnowSettings.subdivLevels for finer boundaries.")]
        [FormerlySerializedAs("manualKeepPolygons")]
        public List<Collider2D> manualKeepColliders = new List<Collider2D>();

        [Header("Debug")]
        public bool drawPatchOutlines = true;
        public bool drawVertNormals = false;

        [Tooltip("On the next rebuild, log every Collider2D that the overlap mask treated as 'foreign' (and dropped snow tris against), and draw their world-space bounds as red wire boxes in the scene view. Use to diagnose unexpected gaps — toggle on, click Rebuild Now, look at the console + scene view to find the offender. The console entry is clickable and pings the GameObject in the hierarchy.")]
        public bool debugLogMaskColliders = false;

        // Snapshot of last rebuild's foreign colliders. Populated only when
        // debugLogMaskColliders is on; consumed by OnDrawGizmos for the red wire cubes.
        // HashSet to dedupe across patches that share foreign candidates.
        [System.NonSerialized] readonly HashSet<Collider2D> debugLastForeignColliders = new HashSet<Collider2D>();

        // -- Settings accessors (settings asset wins, otherwise fall back) -------

        const float DefaultMaxSnowSlopeDeg = 45f;
        const int   DefaultMinPatchTris    = 3;
        const float DefaultRestThickness   = 0.25f;
        const int   DefaultEdgeTaperHops   = 1;
        const int   DefaultSubdivLevels    = 1;
        const float DefaultAccumRate       = 0.05f;

        public float MaxSnowSlopeDeg =>
            overrideMaxSnowSlope ? maxSnowSlopeDegOverride
            : settings != null   ? settings.maxSnowSlopeDeg
            : DefaultMaxSnowSlopeDeg;
        public int   MinPatchTris    => settings != null ? settings.minPatchTris    : DefaultMinPatchTris;
        public float RestThickness   => settings != null ? settings.restThickness   : DefaultRestThickness;
        public int   EdgeTaperHops   => settings != null ? settings.edgeTaperHops   : DefaultEdgeTaperHops;
        public int   SubdivLevels    => settings != null ? settings.subdivLevels    : DefaultSubdivLevels;
        public float AccumulationRate=> settings != null ? settings.accumulationRate: DefaultAccumRate;
        public Material SnowMaterial => settings != null ? settings.snowMaterial    : null;

        public int SlopeCutTaperHops => settings != null ? settings.slopeCutTaperHops : 0;

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
        static readonly int IdFootprintTint   = Shader.PropertyToID("_FootprintTint");
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

        /// <summary>Mesh-local nearest-vertex hit-test against heightRest > 0. Returns
        /// the winning strip + vertIdx so callers (RefreshSnowBlend → ApplySnowDrag)
        /// can read heightCurrent / heightRest in O(1) without re-running the sweep.
        /// The slope-normal primary gate (surfaceNormal.y ≥ cos(maxSnowSlopeDeg))
        /// is the caller's responsibility — keeping it out of this method lets the
        /// caller skip the InverseTransformPoint and the per-strip walk entirely
        /// when the contact is on a steep face that can't carry snow anyway.</summary>
        public bool TryFindFootprintHit(Vector2 worldXY, out SnowStrip strip, out int vertIdx)
        {
            strip = null;
            vertIdx = -1;
            if (sourceMeshFilter == null) return false;
            Vector3 local = sourceMeshFilter.transform.InverseTransformPoint(
                new Vector3(worldXY.x, worldXY.y, 0f));
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                var s = child.GetComponent<SnowStrip>();
                if (s == null) continue;
                if (s.TryFindNearestSnowVert(local, out int idx))
                {
                    strip = s;
                    vertIdx = idx;
                    return true;
                }
            }
            return false;
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

            // Reset foreign-collider snapshot so the gizmo reflects only THIS rebuild's
            // candidates. Done unconditionally — the cache is cheap and the toggle gates
            // the population step inside MaskOutOverlap.
            debugLastForeignColliders.Clear();

            var mesh = sourceMeshFilter.sharedMesh;
            var patches = SnowTopSurfaceExtractor.Extract(mesh, MaxSnowSlopeDeg, detectSlopeCut: SlopeCutTaperHops > 0);

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
                int scTaperHops = SlopeCutTaperHops;
                float restThick = RestThickness;
                bool hasSlopeCut = patch.isSlopeCutAdjacent != null && patch.isSlopeCutAdjacent.Length == V;
                bool runSlopeCutBFS = hasSlopeCut && scTaperHops > 0;

                // Adjacency built once: shared by both BFS passes.
                HashSet<int>[] adj = null;
                if ((taperHops > 0 || runSlopeCutBFS) && patch.boundaryEdges != null && patch.boundaryEdges.Length >= 2)
                {
                    adj = new HashSet<int>[V];
                    for (int i = 0; i < V; i++) adj[i] = new HashSet<int>();
                    for (int t = 0; t < patch.tris.Length; t += 3)
                    {
                        int a = patch.tris[t], b = patch.tris[t + 1], c = patch.tris[t + 2];
                        adj[a].Add(b); adj[a].Add(c);
                        adj[b].Add(a); adj[b].Add(c);
                        adj[c].Add(a); adj[c].Add(b);
                    }
                }

                // Compute the per-vertex height ramp as the MIN of two BFS passes:
                //   1. edgeTaperHops BFS seeded by ALL boundary verts
                //   2. slopeCutTaperHops BFS seeded ONLY by slope-cut boundary verts
                // Each pass yields a 0..1 ramp via SmoothStep(hop / hopBudget). Verts
                // unreached by a pass score 1.0 (no taper from that source). Final height
                // = restThick * min(rampMesh, rampSlopeCut) so whichever taper is more
                // restrictive wins, allowing slope-cut endings to fade further inland
                // than mesh-edge boundaries (or in isolation, when edgeTaperHops=0).

                static int[] BFSHops(HashSet<int>[] adjGraph, int vertCount, IEnumerable<int> seeds)
                {
                    var hop = new int[vertCount];
                    for (int i = 0; i < vertCount; i++) hop[i] = -1;
                    var queue = new Queue<int>();
                    foreach (int v in seeds)
                    {
                        if (v < 0 || v >= vertCount) continue;
                        if (hop[v] == -1) { hop[v] = 0; queue.Enqueue(v); }
                    }
                    while (queue.Count > 0)
                    {
                        int u = queue.Dequeue();
                        foreach (int n in adjGraph[u])
                        {
                            if (hop[n] == -1)
                            {
                                hop[n] = hop[u] + 1;
                                queue.Enqueue(n);
                            }
                        }
                    }
                    return hop;
                }

                int[] hopAll = null;
                int[] hopSC = null;
                if (taperHops > 0 && adj != null)
                {
                    var seeds = new List<int>(patch.boundaryEdges.Length);
                    for (int e = 0; e < patch.boundaryEdges.Length; e++) seeds.Add(patch.boundaryEdges[e]);
                    hopAll = BFSHops(adj, V, seeds);
                }
                if (runSlopeCutBFS && adj != null)
                {
                    // Seed from boundary verts that are slope-cut adjacent. Filter
                    // by current boundary so verts whose slope-cut edges were dropped
                    // by Mask don't seed phantom taper holes inside the patch.
                    var boundarySet = new HashSet<int>();
                    for (int e = 0; e < patch.boundaryEdges.Length; e++) boundarySet.Add(patch.boundaryEdges[e]);
                    var seeds = new List<int>();
                    foreach (int v in boundarySet)
                        if (patch.isSlopeCutAdjacent[v]) seeds.Add(v);
                    if (seeds.Count > 0) hopSC = BFSHops(adj, V, seeds);
                }

                for (int v = 0; v < V; v++)
                {
                    float ramp = 1f;
                    if (hopAll != null)
                    {
                        int h = hopAll[v] < 0 ? taperHops : hopAll[v];
                        float r = Mathf.Clamp01((float)h / taperHops);
                        if (r < ramp) ramp = r;
                    }
                    if (hopSC != null)
                    {
                        int h = hopSC[v] < 0 ? scTaperHops : hopSC[v];
                        float r = Mathf.Clamp01((float)h / scTaperHops);
                        if (r < ramp) ramp = r;
                    }
                    heights[v] = restThick * Mathf.SmoothStep(0f, 1f, ramp);
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

        // Z-aware variant: when useZ is true, a foreign hit only counts if the
        // sample's world Z is within zWindow of the foreign's pre-cached world Z.
        // Lets a single SnowField on a Z-deep mesh keep snow on its back-Z slice
        // (deep in the void behind foreground objects) while still masking the
        // front-Z slice that shares Z with foreground colliders.
        static bool HitsAnyForeign(Vector3 worldPos, List<Collider2D> foreign, float[] foreignZs, bool useZ, float zWindow)
        {
            Vector2 xy = new Vector2(worldPos.x, worldPos.y);
            for (int i = 0; i < foreign.Count; i++)
            {
                var c = foreign[i];
                if (c == null) continue;
                if (useZ && Mathf.Abs(worldPos.z - foreignZs[i]) > zWindow) continue;
                if (c.OverlapPoint(xy)) return true;
            }
            return false;
        }

        // Membership test against the manual-keep override list. Used both during
        // candidate filtering (so our own override colliders never act as foreign
        // maskers, even if the user forgot isTrigger=true) and during the per-tri
        // restore pass (so a seam tri the foreign mask dropped gets resurrected
        // when it overlaps a keep collider).
        bool IsManualKeep(Collider2D c)
        {
            if (manualKeepColliders == null) return false;
            for (int i = 0; i < manualKeepColliders.Count; i++)
                if (manualKeepColliders[i] == c) return true;
            return false;
        }

        // Tri-vs-keep-collider overlap. Replaces the old 4-sample (3 verts + centroid)
        // OverlapPoint check, which only fired when one of those 4 specific points was
        // inside the collider — small ones between sample points restored nothing,
        // partially-covered tris restored either fully or not at all → blocky "grid"
        // pattern visible in the result. The proper overlap test fires whenever the
        // collider and triangle actually share any area. Output is still tri-granular —
        // bump SnowSettings.subdivLevels for finer boundaries.
        bool TriOverlapsAnyKeep(Vector2 a, Vector2 b, Vector2 c, ref Vector2[] worldPathBuf)
        {
            if (manualKeepColliders == null) return false;
            for (int i = 0; i < manualKeepColliders.Count; i++)
            {
                var col = manualKeepColliders[i];
                if (col == null) continue;
                if (TriOverlapsCollider(a, b, c, col, ref worldPathBuf)) return true;
            }
            return false;
        }

        static bool TriOverlapsCollider(Vector2 a, Vector2 b, Vector2 c, Collider2D col, ref Vector2[] worldPathBuf)
        {
            // Cheap path: any tri vert inside the collider (Unity's optimized PIP works
            // for every Collider2D subtype, so this branch covers Circle/Capsule/Edge/
            // Composite "for free" without a type-specific edge extraction).
            if (col.OverlapPoint(a)) return true;
            if (col.OverlapPoint(b)) return true;
            if (col.OverlapPoint(c)) return true;

            // Type-specific edge extraction for the proper overlap test (poly vert in tri
            // + edge-edge cross). Supported: PolygonCollider2D, BoxCollider2D. Other
            // types fall back to the cheap path only — partial overlaps where neither
            // a tri vert nor a collider vert is inside the other (e.g. a small circle
            // crossing a tri edge between vertices) won't restore. Acceptable trade-off
            // for now; bump subdivLevels or use a Polygon/Box if you hit that case.
            if (col is PolygonCollider2D poly)
            {
                var polyT = poly.transform;
                Vector2 ofs = poly.offset;
                int pathCount = poly.pathCount;
                for (int p = 0; p < pathCount; p++)
                {
                    var path = poly.GetPath(p);
                    int n = path.Length;
                    if (n < 2) continue;
                    if (worldPathBuf == null || worldPathBuf.Length < n) worldPathBuf = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 pw = polyT.TransformPoint(new Vector3(path[i].x + ofs.x, path[i].y + ofs.y, 0f));
                        worldPathBuf[i] = new Vector2(pw.x, pw.y);
                    }
                    if (PathOverlapsTri(worldPathBuf, n, a, b, c)) return true;
                }
                return false;
            }

            if (col is BoxCollider2D box)
            {
                // Build world-space corners from local size + offset so a rotated box
                // is handled correctly. Box.bounds (axis-aligned in world) would lose
                // orientation if the GO is rotated.
                var boxT = box.transform;
                Vector2 half = box.size * 0.5f;
                Vector2 ofs = box.offset;
                if (worldPathBuf == null || worldPathBuf.Length < 4) worldPathBuf = new Vector2[4];
                Vector3 p0 = boxT.TransformPoint(new Vector3(-half.x + ofs.x, -half.y + ofs.y, 0f));
                Vector3 p1 = boxT.TransformPoint(new Vector3( half.x + ofs.x, -half.y + ofs.y, 0f));
                Vector3 p2 = boxT.TransformPoint(new Vector3( half.x + ofs.x,  half.y + ofs.y, 0f));
                Vector3 p3 = boxT.TransformPoint(new Vector3(-half.x + ofs.x,  half.y + ofs.y, 0f));
                worldPathBuf[0] = new Vector2(p0.x, p0.y);
                worldPathBuf[1] = new Vector2(p1.x, p1.y);
                worldPathBuf[2] = new Vector2(p2.x, p2.y);
                worldPathBuf[3] = new Vector2(p3.x, p3.y);
                return PathOverlapsTri(worldPathBuf, 4, a, b, c);
            }

            return false;
        }

        static bool PathOverlapsTri(Vector2[] path, int n, Vector2 a, Vector2 b, Vector2 c)
        {
            // Any path vert inside tri (catches collider fully contained inside a tri).
            for (int i = 0; i < n; i++)
                if (PointInTri(path[i], a, b, c)) return true;
            // Any tri edge crosses any path edge (catches partial overlaps where no
            // vertex of either is inside the other).
            for (int i = 0; i < n; i++)
            {
                Vector2 pa = path[i];
                Vector2 pb = path[(i + 1) % n];
                if (SegmentsIntersect(a, b, pa, pb)) return true;
                if (SegmentsIntersect(b, c, pa, pb)) return true;
                if (SegmentsIntersect(c, a, pa, pb)) return true;
            }
            return false;
        }

        // Half-plane test via signed cross-product. Inclusive on edges (collinear → in).
        static bool PointInTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool hasNeg = (d1 < 0f) || (d2 < 0f) || (d3 < 0f);
            bool hasPos = (d1 > 0f) || (d2 > 0f) || (d3 > 0f);
            return !(hasNeg && hasPos);
        }

        // Strict (non-collinear, non-endpoint-sharing) segment intersection. Collinear
        // overlaps are rare in practice and would already be caught by the
        // PointInTri / OverlapPoint checks above.
        static bool SegmentsIntersect(Vector2 p1, Vector2 p2, Vector2 p3, Vector2 p4)
        {
            Vector2 r = p2 - p1;
            Vector2 s = p4 - p3;
            float rxs = r.x * s.y - r.y * s.x;
            if (Mathf.Abs(rxs) < 1e-9f) return false;
            Vector2 qmp = p3 - p1;
            float t = (qmp.x * s.y - qmp.y * s.x) / rxs;
            float u = (qmp.x * r.y - qmp.y * r.x) / rxs;
            return t > 0f && t < 1f && u > 0f && u < 1f;
        }

        // -- GeometricClipping mask path (Sutherland-Hodgman) --

        // Clip-polygon vertex during SH passes. worldXY drives the clip math; the (ba,
        // bb, bc) barycentric coords reference the parent tri's three corners so we
        // can interpolate mesh-local position + normal cleanly after all clipping is
        // done. Lerping bary across multiple clip steps yields a true convex
        // combination of the original three corners — same as a barycentric sample.
        struct ClipVert
        {
            public Vector2 worldXY;
            public float ba, bb, bc;
        }

        // GeometricClipping mode entry point. For each tri: (1) preempt clipping if it
        // overlaps a manual-keep collider; (2) for each foreign collider that survives
        // the Z filter, either clip via SH or fall back per-pair to sample-based for
        // unsupported foreign types and the foreign-fully-inside-tri rare case; (3)
        // fan-triangulate the kept polygon. Returns the original patch unchanged if no
        // tri was actually clipped (fast path when nothing in foreign overlaps).
        SnowTopSurfaceExtractor.TopPatch ApplyClippingMask(
            SnowTopSurfaceExtractor.TopPatch patch,
            Transform meshT,
            List<Collider2D> foreign,
            float[] foreignZs,
            bool useZ,
            float zWin,
            bool hasKeepPolys,
            ref Vector2[] keepPathBuf)
        {
            int triCount = patch.tris.Length / 3;
            int origV = patch.verts.Length;

            var outVerts    = new List<Vector3>(origV);
            var outNorms    = new List<Vector3>(origV);
            bool hasSlopeCut = patch.isSlopeCutAdjacent != null && patch.isSlopeCutAdjacent.Length == origV;
            var outSlopeCut = hasSlopeCut ? new List<bool>(origV) : null;
            var outTris     = new List<int>(patch.tris.Length);

            // Position-quantized dedup: adjacent tris that emit a clip vert at the same
            // shared edge (same intersection with foreign) MUST collapse to one index,
            // otherwise the boundary regen below sees the seam as two separate boundaries
            // and the BFS taper seeds spurious rims through the patch interior.
            const float quant = 1e-4f;
            var posToIdx = new Dictionary<long, int>(origV * 2);

            int Canon(Vector3 meshLocal, Vector3 normal, bool sc)
            {
                long kx = (long)Mathf.RoundToInt(meshLocal.x / quant);
                long ky = (long)Mathf.RoundToInt(meshLocal.y / quant);
                long kz = (long)Mathf.RoundToInt(meshLocal.z / quant);
                long k = ((kx & 0x1FFFFFL) << 42) ^ ((ky & 0x1FFFFFL) << 21) ^ (kz & 0x1FFFFFL);
                if (posToIdx.TryGetValue(k, out int idx)) return idx;
                int newIdx = outVerts.Count;
                outVerts.Add(meshLocal);
                outNorms.Add(normal);
                if (outSlopeCut != null) outSlopeCut.Add(sc);
                posToIdx[k] = newIdx;
                return newIdx;
            }

            // Per-tri buffers (allocated lazily inside the loop to avoid leaking state).
            // Reusable foreign world-space vert buffer.
            Vector2[] foreignWorldBuf = null;
            // Reused per-foreign decomposition output. Cleared+filled by
            // TryDecomposeForeignPolygon. Each element is a Vector2[] (typically a triangle)
            // representing one convex piece of the foreign in world XY.
            var decomposedPieces = new List<Vector2[]>(16);

            bool anyClipped = false;

            for (int t = 0; t < triCount; t++)
            {
                int ia = patch.tris[t * 3];
                int ib = patch.tris[t * 3 + 1];
                int ic = patch.tris[t * 3 + 2];
                Vector3 va = patch.verts[ia], vb = patch.verts[ib], vc = patch.verts[ic];
                Vector3 wa = meshT.TransformPoint(va);
                Vector3 wb = meshT.TransformPoint(vb);
                Vector3 wc = meshT.TransformPoint(vc);
                Vector3 wcen = (wa + wb + wc) / 3f;
                Vector3 na = patch.vertNormals[ia], nb = patch.vertNormals[ib], nc = patch.vertNormals[ic];
                bool sca = hasSlopeCut && patch.isSlopeCutAdjacent[ia];
                bool scb = hasSlopeCut && patch.isSlopeCutAdjacent[ib];
                bool scc = hasSlopeCut && patch.isSlopeCutAdjacent[ic];

                // Manual-keep preempt — overlapping a keep collider skips clipping.
                if (hasKeepPolys && TriOverlapsAnyKeep(
                        new Vector2(wa.x, wa.y), new Vector2(wb.x, wb.y), new Vector2(wc.x, wc.y),
                        ref keepPathBuf))
                {
                    int ka = Canon(va, na, sca);
                    int kb = Canon(vb, nb, scb);
                    int kc = Canon(vc, nc, scc);
                    outTris.Add(ka); outTris.Add(kb); outTris.Add(kc);
                    continue;
                }

                // Tri AABB in world XY for foreign-AABB pre-filter.
                float triMinX = Mathf.Min(wa.x, Mathf.Min(wb.x, wc.x));
                float triMaxX = Mathf.Max(wa.x, Mathf.Max(wb.x, wc.x));
                float triMinY = Mathf.Min(wa.y, Mathf.Min(wb.y, wc.y));
                float triMaxY = Mathf.Max(wa.y, Mathf.Max(wb.y, wc.y));

                // Polygon-difference algorithm. Maintain `pieces` = the kept-so-far convex
                // pieces of the tri after subtracting all foreigns processed so far. Each
                // subsequent foreign is subtracted from each piece independently.
                //
                // Subtraction primitive: for each foreign edge (CCW), split current by
                // the edge into (inside, outside). Outside is final → goes to nextPieces.
                // Inside continues to next edge. After all edges, the remaining "inside
                // every edge" piece is the part of current that's inside the foreign,
                // which we discard. This produces tri − foreign as a list of convex pieces.
                var pieces = new List<List<ClipVert>>();
                var initial = new List<ClipVert>(3);
                initial.Add(new ClipVert { worldXY = new Vector2(wa.x, wa.y), ba = 1f, bb = 0f, bc = 0f });
                initial.Add(new ClipVert { worldXY = new Vector2(wb.x, wb.y), ba = 0f, bb = 1f, bc = 0f });
                initial.Add(new ClipVert { worldXY = new Vector2(wc.x, wc.y), ba = 0f, bb = 0f, bc = 1f });
                pieces.Add(initial);

                bool triDropped = false;
                bool triClipped = false;

                for (int fi = 0; fi < foreign.Count; fi++)
                {
                    var col = foreign[fi];
                    if (col == null) continue;

                    // Z filter — same window as SampleBased, applied per pair via centroid Z.
                    if (useZ && Mathf.Abs(wcen.z - foreignZs[fi]) > zWin) continue;

                    // Foreign AABB pre-filter: skip foreigns whose world-AABB doesn't
                    // overlap the tri's AABB. Most scene-wide foreigns are far away, so
                    // this rejects them with cheap math. If every foreign is rejected
                    // here, triClipped stays false → emit original tri unchanged.
                    var fb = col.bounds;
                    if (fb.max.x < triMinX || fb.min.x > triMaxX ||
                        fb.max.y < triMinY || fb.min.y > triMaxY) continue;

                    // Get convex CCW world-space verts for this foreign, or 0 if unsupported.
                    int foreignN = TryGetConvexForeignWorldVerts(col, ref foreignWorldBuf);
                    if (foreignN >= 3)
                    {
                        pieces = SubtractConvexPieceFromPieces(pieces, foreignWorldBuf, foreignN);
                        triClipped = true;
                        if (pieces.Count == 0) { triDropped = true; break; }
                        continue;
                    }

                    // Convex fast-path rejected (concave / multi-path PolygonCollider2D, or
                    // Edge/Composite). Try decomposition: split the foreign into convex
                    // pieces (typically triangles via ear-clipping) and subtract each as
                    // its own convex polygon. (foreign − ∪ pieces) ⇔ ((tri − piece₁) − …)
                    // by union-distribution of subtraction over disjoint pieces.
                    int pieceCount = TryDecomposeForeignPolygon(col, decomposedPieces, ref foreignWorldBuf);
                    if (pieceCount > 0)
                    {
                        for (int pIdx = 0; pIdx < pieceCount; pIdx++)
                        {
                            var dp = decomposedPieces[pIdx];
                            pieces = SubtractConvexPieceFromPieces(pieces, dp, dp.Length);
                            if (pieces.Count == 0) { triDropped = true; break; }
                        }
                        triClipped = true;
                        if (triDropped) break;
                        continue;
                    }

                    // Decomposition also failed: sample-based fallback for THIS pair only.
                    // Preserves SampleBased mode's cascade behaviour on foregrounds that
                    // neither the convex fast-path nor the decomposer can handle.
                    if (HitsAnyForeignOne(col, wa, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wb, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wc, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wcen, foreignZs, fi, useZ, zWin))
                    {
                        triDropped = true;
                        break;
                    }
                    triClipped = true; // sample-based fallback ran; mark as processed
                }

                if (triDropped) continue;
                if (pieces.Count == 0) continue;

                if (!triClipped)
                {
                    // No clipping ran for this tri (all foreigns Z-filtered or AABB-rejected) —
                    // emit the tri unchanged using its three original corners.
                    int ka = Canon(va, na, sca);
                    int kb = Canon(vb, nb, scb);
                    int kc = Canon(vc, nc, scc);
                    outTris.Add(ka); outTris.Add(kb); outTris.Add(kc);
                    continue;
                }

                // Detect the "no actual cut happened" case (foreign passed AABB but didn't
                // intersect the tri geometrically): pieces is exactly [originalTri]. In
                // that case emit unchanged so the output is byte-identical to SampleBased
                // for tris that no foreign actually clips.
                if (pieces.Count == 1 && pieces[0].Count == 3 &&
                    pieces[0][0].ba > 0.999f && pieces[0][1].bb > 0.999f && pieces[0][2].bc > 0.999f)
                {
                    int ka = Canon(va, na, sca);
                    int kb = Canon(vb, nb, scb);
                    int kc = Canon(vc, nc, scc);
                    outTris.Add(ka); outTris.Add(kb); outTris.Add(kc);
                    continue;
                }

                anyClipped = true;

                // Fan-triangulate each kept piece (each piece is convex by construction).
                for (int pi = 0; pi < pieces.Count; pi++)
                {
                    var piece = pieces[pi];
                    if (piece.Count < 3) continue;
                    int firstIdx = EmitClipVert(piece[0], va, vb, vc, na, nb, nc, sca, scb, scc, Canon);
                    int prevIdx = -1;
                    for (int p = 1; p < piece.Count; p++)
                    {
                        int idx = EmitClipVert(piece[p], va, vb, vc, na, nb, nc, sca, scb, scc, Canon);
                        if (prevIdx >= 0)
                        {
                            outTris.Add(firstIdx);
                            outTris.Add(prevIdx);
                            outTris.Add(idx);
                        }
                        prevIdx = idx;
                    }
                }
            }

            if (!anyClipped) return patch;

            if (outTris.Count < 3 || outVerts.Count < 3)
            {
                return new SnowTopSurfaceExtractor.TopPatch
                {
                    verts = new Vector3[0],
                    vertNormals = new Vector3[0],
                    tris = new int[0],
                    boundaryEdges = new int[0],
                    isSlopeCutAdjacent = hasSlopeCut ? new bool[0] : null,
                };
            }

            var newTrisArr = outTris.ToArray();

            // Re-derive directed boundary edges (same scheme as SampleBased path).
            var dirSet = new HashSet<long>();
            for (int t = 0; t < newTrisArr.Length; t += 3)
            {
                int a = newTrisArr[t], b = newTrisArr[t + 1], c = newTrisArr[t + 2];
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
                verts = outVerts.ToArray(),
                vertNormals = outNorms.ToArray(),
                tris = newTrisArr,
                boundaryEdges = bedges.ToArray(),
                isSlopeCutAdjacent = outSlopeCut?.ToArray(),
            };
        }

        // Emit a single clip-vert: barycentric-interpolate mesh-local pos + normal +
        // slope-cut flag, then dedup via Canon. Only original tri corners (bary == 1
        // on one axis) inherit their slopeCut flag; new clip verts on the foreign
        // boundary set slopeCut = false (mask cuts are not slope cuts).
        static int EmitClipVert(
            ClipVert cv,
            Vector3 va, Vector3 vb, Vector3 vc,
            Vector3 na, Vector3 nb, Vector3 nc,
            bool sca, bool scb, bool scc,
            System.Func<Vector3, Vector3, bool, int> canon)
        {
            Vector3 ml  = cv.ba * va + cv.bb * vb + cv.bc * vc;
            Vector3 nrm = cv.ba * na + cv.bb * nb + cv.bc * nc;
            bool sc = false;
            if      (cv.ba > 0.999f) sc = sca;
            else if (cv.bb > 0.999f) sc = scb;
            else if (cv.bc > 0.999f) sc = scc;
            return canon(ml, nrm, sc);
        }

        // Polygon-difference primitive: split the input polygon by a directed edge
        // (edgeStart → edgeEnd) into the "inside" half-plane (LEFT of edge for CCW
        // clip polygon) and the "outside" half (RIGHT). Crossing edges emit an
        // intersection point added to BOTH outputs. Both halves are convex if input
        // was convex (a half-plane intersected with a convex polygon stays convex).
        // Used by ApplyClippingMask to compute T - foreign as the union of "outside"
        // pieces accumulated across all foreign edges.
        static void SplitPolygonByEdge(List<ClipVert> input, Vector2 edgeStart, Vector2 edgeEnd,
                                       List<ClipVert> insideOut, List<ClipVert> outsideOut)
        {
            int n = input.Count;
            if (n == 0) return;
            Vector2 edgeDir = edgeEnd - edgeStart;

            for (int i = 0; i < n; i++)
            {
                ClipVert curr = input[i];
                ClipVert prev = input[(i + n - 1) % n];

                bool currOut = IsOutsideEdge(curr.worldXY, edgeStart, edgeDir);
                bool prevOut = IsOutsideEdge(prev.worldXY, edgeStart, edgeDir);

                if (currOut)
                {
                    if (!prevOut)
                    {
                        // Crossing inside→outside: emit intersection to BOTH halves.
                        ClipVert isect = EdgeEdgeIntersect(prev, curr, edgeStart, edgeDir);
                        insideOut.Add(isect);
                        outsideOut.Add(isect);
                    }
                    outsideOut.Add(curr);
                }
                else
                {
                    if (prevOut)
                    {
                        // Crossing outside→inside: emit intersection to BOTH halves.
                        ClipVert isect = EdgeEdgeIntersect(prev, curr, edgeStart, edgeDir);
                        outsideOut.Add(isect);
                        insideOut.Add(isect);
                    }
                    insideOut.Add(curr);
                }
            }
        }

        // True iff p is on the OUTSIDE (right) side of the CCW-oriented edge.
        // For a CCW polygon: cross(p - edgeStart, edgeDir) > 0 ⇔ p is on the RIGHT
        // of the edge ⇔ exterior of the polygon. (The earlier `< 0` was wrong — it
        // returns LEFT/interior; that's the bug that made GeometricClipping compute
        // `tri ∩ foreign` instead of `tri − foreign`.)
        static bool IsOutsideEdge(Vector2 p, Vector2 edgeStart, Vector2 edgeDir)
        {
            Vector2 d = p - edgeStart;
            float cross = d.x * edgeDir.y - d.y * edgeDir.x;
            return cross > 0f;
        }

        // Intersection of segment (prev, curr) with the line through (edgeStart, +edgeDir).
        // Bary coords lerp at the same t — this chains correctly through multi-edge clips
        // because each lerp is a convex combination, and the composition of convex
        // combinations is itself a convex combination of the originals.
        static ClipVert EdgeEdgeIntersect(ClipVert prev, ClipVert curr, Vector2 edgeStart, Vector2 edgeDir)
        {
            Vector2 p1 = prev.worldXY, p2 = curr.worldXY;
            Vector2 edgeNormal = new Vector2(edgeDir.y, -edgeDir.x);
            float denom = Vector2.Dot(p2 - p1, edgeNormal);
            if (Mathf.Abs(denom) < 1e-9f) return curr; // parallel; degenerate, fallback
            float t = Vector2.Dot(edgeStart - p1, edgeNormal) / denom;
            t = Mathf.Clamp01(t);
            return new ClipVert
            {
                worldXY = Vector2.Lerp(p1, p2, t),
                ba = Mathf.Lerp(prev.ba, curr.ba, t),
                bb = Mathf.Lerp(prev.bb, curr.bb, t),
                bc = Mathf.Lerp(prev.bc, curr.bc, t),
            };
        }

        // Subtract one convex CCW piece from the running pieces list.
        // pieces (snow-tri fragments after prior subtractions) is consumed; result
        // is the new pieces collection = (input pieces − piece). Each input piece
        // is split per piece-edge into outside-half (kept) and inside-half (continues
        // to next edge); after all edges, leftover inside is the part covered by
        // piece → discard. This is the same per-edge SH loop the original
        // ApplyClippingMask ran inline; extracted so it can be reused once per
        // decomposed convex piece.
        static List<List<ClipVert>> SubtractConvexPieceFromPieces(
            List<List<ClipVert>> pieces, Vector2[] pieceVerts, int pieceN)
        {
            var nextPieces = new List<List<ClipVert>>();
            for (int pi = 0; pi < pieces.Count; pi++)
            {
                var current = pieces[pi];
                for (int ei = 0; ei < pieceN; ei++)
                {
                    if (current.Count < 3) break;
                    Vector2 e0 = pieceVerts[ei];
                    Vector2 e1 = pieceVerts[(ei + 1) % pieceN];

                    var insideOut = new List<ClipVert>(current.Count + 1);
                    var outsideOut = new List<ClipVert>(current.Count + 1);
                    SplitPolygonByEdge(current, e0, e1, insideOut, outsideOut);

                    if (outsideOut.Count >= 3) nextPieces.Add(outsideOut);
                    current = insideOut;
                }
                // current = piece-covered part of pieces[pi] → discard.
            }
            return nextPieces;
        }

        // Decompose a foreign collider into a list of convex CCW pieces in world XY.
        // Used as a slow-path when TryGetConvexForeignWorldVerts rejects the collider
        // (concave PolygonCollider2D, multi-path PolygonCollider2D). On success, the
        // pieces tile the foreign with disjoint convex polygons (typically triangles
        // from ear-clipping). Returns piece count, 0 = decomposition not applicable
        // or self-check failed → caller falls back to sample-based.
        //
        // Reuses scratch as a path extraction buffer; expand on demand.
        static int TryDecomposeForeignPolygon(
            Collider2D col, List<Vector2[]> piecesOut, ref Vector2[] scratch)
        {
            piecesOut.Clear();
            if (!(col is PolygonCollider2D poly)) return 0;
            int pathCount = poly.pathCount;
            if (pathCount < 1) return 0;

            var polyT = poly.transform;
            Vector2 ofs = poly.offset;

            // Extract every path to its own world-XY array. Validate simplicity per
            // path. Single-path uses scratch; multi-path allocates per-path.
            Vector2[][] paths;
            if (pathCount == 1)
            {
                var path = poly.GetPath(0);
                int n = path.Length;
                if (n < 3) return 0;
                if (scratch == null || scratch.Length < n) scratch = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 pw = polyT.TransformPoint(new Vector3(path[i].x + ofs.x, path[i].y + ofs.y, 0f));
                    scratch[i] = new Vector2(pw.x, pw.y);
                }
                if (!ValidatePolygonSimple(scratch, n)) return 0;

                // Trim scratch to exact size for unified processing path.
                var trimmed = new Vector2[n];
                System.Array.Copy(scratch, trimmed, n);
                paths = new Vector2[][] { trimmed };
            }
            else
            {
                paths = new Vector2[pathCount][];
                for (int p = 0; p < pathCount; p++)
                {
                    var path = poly.GetPath(p);
                    int n = path.Length;
                    if (n < 3) return 0;
                    var arr = new Vector2[n];
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 pw = polyT.TransformPoint(new Vector3(path[i].x + ofs.x, path[i].y + ofs.y, 0f));
                        arr[i] = new Vector2(pw.x, pw.y);
                    }
                    if (!ValidatePolygonSimple(arr, n)) return 0;
                    paths[p] = arr;
                }
            }

            // Nesting depth: # of OTHER paths whose interior contains this path's
            // first vertex. Even depth = solid (outer), odd depth = hole. Assumes
            // paths don't cross (true for MeshToCollider2DGenerator output).
            var depth = new int[pathCount];
            if (pathCount > 1)
            {
                for (int i = 0; i < pathCount; i++)
                {
                    Vector2 testPt = paths[i][0];
                    for (int j = 0; j < pathCount; j++)
                    {
                        if (i == j) continue;
                        if (PointInPolygon(testPt, paths[j], paths[j].Length))
                            depth[i]++;
                    }
                }
            }

            // Per-outer process: normalise winding, gather direct holes, bridge,
            // ear-clip, accumulate. Track expected area for end-of-function check.
            float expectedArea = 0f;
            var holeBuf = new List<Vector2[]>(4);
            var merged = new List<Vector2>();

            for (int i = 0; i < pathCount; i++)
            {
                if ((depth[i] & 1) != 0) continue; // skip holes
                var outer = paths[i];
                int outerN = outer.Length;

                if (!IsCCW(outer, outerN))
                {
                    for (int a = 0, b = outerN - 1; a < b; a++, b--)
                    { var tmp = outer[a]; outer[a] = outer[b]; outer[b] = tmp; }
                }

                expectedArea += Mathf.Abs(PolygonSignedArea2(outer, outerN)) * 0.5f;

                // Direct holes = paths at depth[i]+1 whose first vert is inside this outer.
                holeBuf.Clear();
                if (pathCount > 1)
                {
                    for (int j = 0; j < pathCount; j++)
                    {
                        if (j == i) continue;
                        if (depth[j] != depth[i] + 1) continue;
                        if (!PointInPolygon(paths[j][0], outer, outerN)) continue;
                        var hole = paths[j];
                        int holeN = hole.Length;
                        // CW so bridge into CCW outer yields weakly simple CCW merged polygon.
                        if (IsCCW(hole, holeN))
                        {
                            for (int a = 0, b = holeN - 1; a < b; a++, b--)
                            { var tmp = hole[a]; hole[a] = hole[b]; hole[b] = tmp; }
                        }
                        holeBuf.Add(hole);
                        expectedArea -= Mathf.Abs(PolygonSignedArea2(hole, holeN)) * 0.5f;
                    }
                }

                // Build merged polygon: outer first, then each hole spliced via bridge.
                merged.Clear();
                for (int v = 0; v < outerN; v++) merged.Add(outer[v]);
                for (int h = 0; h < holeBuf.Count; h++)
                {
                    if (!BridgeHoleIntoOuter(merged, holeBuf[h]))
                    {
                        piecesOut.Clear();
                        return 0;
                    }
                }

                // Convert merged List → Vector2[] for ear-clip API.
                int mergedN = merged.Count;
                var mergedArr = new Vector2[mergedN];
                for (int k = 0; k < mergedN; k++) mergedArr[k] = merged[k];

                if (!TryEarClipPolygon(mergedArr, mergedN, piecesOut))
                {
                    piecesOut.Clear();
                    return 0;
                }
            }

            // Final self-check: total triangle area ≈ expected solid area.
            if (expectedArea < 1e-8f) { piecesOut.Clear(); return 0; }
            float sumArea = 0f;
            for (int i = 0; i < piecesOut.Count; i++)
            {
                var t = piecesOut[i];
                if (t == null || t.Length != 3) { piecesOut.Clear(); return 0; }
                sumArea += Mathf.Abs(PolygonSignedArea2(t, 3)) * 0.5f;
            }
            if (Mathf.Abs(sumArea - expectedArea) > expectedArea * 0.01f)
            {
                piecesOut.Clear();
                return 0;
            }

            return piecesOut.Count;
        }

        // Splice a hole (CW) into an outer (CCW) at a chosen bridge vertex, producing
        // a single weakly simple CCW polygon. The merged sequence inserts:
        //     outer[..bridgeIdx], M (hole rightmost), hole[mIdx+1..mIdx-1], M', V'
        // after position bridgeIdx, where M' and V' duplicate M and outer[bridgeIdx]
        // to close the bridge. Ear-clipping handles the duplicate verts via its
        // collinear-cleanup fallback.
        //
        // Bridge vertex selection: rightmost-vert-of-hole + closest +x ray-cast hit
        // on outer. Picks the higher-x endpoint of the hit edge as bridge target.
        // Skips Eberly's full visibility refinement — for foreign collider shapes
        // the simple heuristic suffices in practice; on failure caller falls back
        // to sample-based.
        static bool BridgeHoleIntoOuter(List<Vector2> outer, Vector2[] hole)
        {
            int holeN = hole.Length;
            if (holeN < 3) return false;

            // Rightmost hole vertex M (max x; tie-break by smaller y for determinism).
            int mIdx = 0;
            for (int i = 1; i < holeN; i++)
            {
                if (hole[i].x > hole[mIdx].x ||
                    (hole[i].x == hole[mIdx].x && hole[i].y < hole[mIdx].y))
                    mIdx = i;
            }
            Vector2 M = hole[mIdx];

            // Find outer edge straddling y=M.y with rightward intersection closest to M.x.
            int outerN = outer.Count;
            int bridgeOuterIdx = -1;
            float closestX = float.PositiveInfinity;
            for (int i = 0; i < outerN; i++)
            {
                Vector2 a = outer[i];
                Vector2 b = outer[(i + 1) % outerN];
                if ((a.y > M.y) == (b.y > M.y)) continue; // both same side of M.y
                float dy = b.y - a.y;
                if (Mathf.Abs(dy) < 1e-12f) continue;
                float t = (M.y - a.y) / dy;
                float ix = a.x + t * (b.x - a.x);
                if (ix < M.x) continue;
                if (ix < closestX)
                {
                    closestX = ix;
                    // Pick the endpoint with greater x as bridge target (closer to M).
                    bridgeOuterIdx = (a.x > b.x) ? i : (i + 1) % outerN;
                }
            }

            if (bridgeOuterIdx < 0) return false;

            // Splice after bridgeOuterIdx:
            //   ..., outer[bridgeOuterIdx], M, hole[mIdx+1], hole[mIdx+2], ...,
            //        hole[mIdx-1], hole[mIdx]=M, outer[bridgeOuterIdx], ...
            // (M and outer[bridgeOuterIdx] each appear twice — bridge edges.)
            Vector2 V = outer[bridgeOuterIdx];
            var insertion = new List<Vector2>(holeN + 2);
            insertion.Add(M);
            for (int k = 1; k < holeN; k++) insertion.Add(hole[(mIdx + k) % holeN]);
            insertion.Add(M);
            insertion.Add(V);
            outer.InsertRange(bridgeOuterIdx + 1, insertion);
            return true;
        }

        // Extract a convex CCW polygon (in world XY) from the foreign collider.
        // Returns the vertex count, or 0 if unsupported / non-convex / multi-path —
        // the caller falls back to sample-based for that pair, preserving current
        // behavior on collider types this clipper doesn't handle precisely.
        static int TryGetConvexForeignWorldVerts(Collider2D col, ref Vector2[] worldBuf)
        {
            if (col is BoxCollider2D box)
            {
                var boxT = box.transform;
                Vector2 half = box.size * 0.5f;
                Vector2 ofs = box.offset;
                if (worldBuf == null || worldBuf.Length < 4) worldBuf = new Vector2[4];
                Vector3 p0 = boxT.TransformPoint(new Vector3(-half.x + ofs.x, -half.y + ofs.y, 0f));
                Vector3 p1 = boxT.TransformPoint(new Vector3( half.x + ofs.x, -half.y + ofs.y, 0f));
                Vector3 p2 = boxT.TransformPoint(new Vector3( half.x + ofs.x,  half.y + ofs.y, 0f));
                Vector3 p3 = boxT.TransformPoint(new Vector3(-half.x + ofs.x,  half.y + ofs.y, 0f));
                worldBuf[0] = new Vector2(p0.x, p0.y);
                worldBuf[1] = new Vector2(p1.x, p1.y);
                worldBuf[2] = new Vector2(p2.x, p2.y);
                worldBuf[3] = new Vector2(p3.x, p3.y);
                if (!IsCCW(worldBuf, 4))
                {
                    var tmp0 = worldBuf[0]; worldBuf[0] = worldBuf[3]; worldBuf[3] = tmp0;
                    var tmp1 = worldBuf[1]; worldBuf[1] = worldBuf[2]; worldBuf[2] = tmp1;
                }
                return 4;
            }

            if (col is PolygonCollider2D poly)
            {
                if (poly.pathCount != 1) return 0;
                var polyT = poly.transform;
                Vector2 ofs = poly.offset;
                var path = poly.GetPath(0);
                int n = path.Length;
                if (n < 3) return 0;
                if (worldBuf == null || worldBuf.Length < n) worldBuf = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 pw = polyT.TransformPoint(new Vector3(path[i].x + ofs.x, path[i].y + ofs.y, 0f));
                    worldBuf[i] = new Vector2(pw.x, pw.y);
                }
                // Strip near-collinear intermediate verts before convexity check.
                // MeshToCollider2DGenerator emits many evenly-spaced verts along
                // visually-straight edges; feeding them all into SH per-edge
                // subtraction accumulates float drift that nibbles ~1 sub-tri off
                // the snow rim at the foreign boundary. Simplifying to true corners
                // first keeps the SH input geometrically equivalent but tight.
                n = SimplifyCollinear(worldBuf, n, 1e-4f);
                if (n < 3) return 0;
                if (!IsConvex(worldBuf, n)) return 0;
                if (!IsCCW(worldBuf, n))
                {
                    for (int i = 0, j = n - 1; i < j; i++, j--)
                    {
                        var tmp = worldBuf[i]; worldBuf[i] = worldBuf[j]; worldBuf[j] = tmp;
                    }
                }
                return n;
            }

            if (col is CircleCollider2D circle)
            {
                // 24-gon in local space, then TransformPoint each. Under uniform scale
                // matches OverlapPoint's "circle of radius * max_scale". Non-uniform scale
                // yields an ellipse approximation — accepted as edge case.
                const int N = 24;
                var cT = circle.transform;
                Vector2 ofs = circle.offset;
                float r = circle.radius;
                if (worldBuf == null || worldBuf.Length < N) worldBuf = new Vector2[N];
                for (int i = 0; i < N; i++)
                {
                    float theta = (i / (float)N) * (Mathf.PI * 2f);
                    Vector3 pl = new Vector3(ofs.x + r * Mathf.Cos(theta), ofs.y + r * Mathf.Sin(theta), 0f);
                    Vector3 pw = cT.TransformPoint(pl);
                    worldBuf[i] = new Vector2(pw.x, pw.y);
                }
                if (!IsCCW(worldBuf, N))
                {
                    for (int i = 0, j = N - 1; i < j; i++, j--)
                    {
                        var tmp = worldBuf[i]; worldBuf[i] = worldBuf[j]; worldBuf[j] = tmp;
                    }
                }
                return N;
            }

            if (col is CapsuleCollider2D capsule)
            {
                // Capsule = two semicircles + two straight edges. 12 segments per cap.
                // direction picks which axis is "long". Local space, TransformPoint each.
                const int CAP_SEG = 12;
                var cT = capsule.transform;
                Vector2 ofs = capsule.offset;
                Vector2 sz = capsule.size;
                bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                // r = perpendicular half-extent; L = inner straight length on the long axis.
                float r, L;
                if (vertical) { r = sz.x * 0.5f; L = Mathf.Max(0f, sz.y - sz.x); }
                else          { r = sz.y * 0.5f; L = Mathf.Max(0f, sz.x - sz.y); }

                // Vert layout (vertical, CCW): right-bottom corner, right-top corner,
                // top semicircle interior+end (CAP_SEG verts at θ=π/12..π), left-bottom
                // corner, bottom semicircle interior (CAP_SEG-1 verts at θ=π+π/12..π+11π/12).
                // Total = 2 + CAP_SEG + 1 + (CAP_SEG - 1) = 2*CAP_SEG + 2.
                int N = 2 * CAP_SEG + 2;
                if (worldBuf == null || worldBuf.Length < N) worldBuf = new Vector2[N];
                // Local alias: C# disallows capturing `ref` parameters in nested
                // closures, so the emit lambda below uses this non-ref reference.
                var buf = worldBuf;

                // Build in long-axis-vertical local frame, then rotate 90° if horizontal.
                int idx = 0;
                System.Action<float, float> Emit = (lx, ly) =>
                {
                    Vector3 local = vertical
                        ? new Vector3(ofs.x + lx, ofs.y + ly, 0f)
                        : new Vector3(ofs.x + ly, ofs.y + lx, 0f); // swap so "long axis" → x
                    Vector3 pw = cT.TransformPoint(local);
                    buf[idx++] = new Vector2(pw.x, pw.y);
                };

                float halfL = L * 0.5f;
                Emit(r, -halfL);                                  // right-bottom
                Emit(r,  halfL);                                  // right-top  (= top arc θ=0)
                for (int i = 1; i <= CAP_SEG; i++)               // top arc θ=π/CAP_SEG..π
                {
                    float theta = (i / (float)CAP_SEG) * Mathf.PI;
                    Emit(r * Mathf.Cos(theta), halfL + r * Mathf.Sin(theta));
                }
                // last top-arc vert was (-r, halfL) — the left-top corner; left edge goes
                // straight down to left-bottom.
                Emit(-r, -halfL);                                 // left-bottom (= bottom arc θ=π)
                for (int i = 1; i < CAP_SEG; i++)                // bottom arc θ=π+π/CAP_SEG..π+11π/12
                {
                    float theta = Mathf.PI + (i / (float)CAP_SEG) * Mathf.PI;
                    Emit(r * Mathf.Cos(theta), -halfL + r * Mathf.Sin(theta));
                }
                // bottom arc θ=2π would coincide with the start (r, -halfL), so skip.

                if (!IsCCW(worldBuf, N))
                {
                    for (int i = 0, j = N - 1; i < j; i++, j--)
                    {
                        var tmp = worldBuf[i]; worldBuf[i] = worldBuf[j]; worldBuf[j] = tmp;
                    }
                }
                return N;
            }

            return 0;
        }

        // Convexity test: every consecutive triple turns the same direction.
        static bool IsConvex(Vector2[] verts, int n)
        {
            if (n < 3) return false;
            int sign = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = verts[i];
                Vector2 b = verts[(i + 1) % n];
                Vector2 c = verts[(i + 2) % n];
                float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                if      (cross >  1e-9f) { if (sign == -1) return false; sign =  1; }
                else if (cross < -1e-9f) { if (sign ==  1) return false; sign = -1; }
            }
            return true;
        }

        // Shoelace signed-area test. Positive area = CCW (in standard math orientation
        // with +Y up, which matches Unity world XY).
        static bool IsCCW(Vector2[] verts, int n)
        {
            float area2 = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = verts[i];
                Vector2 b = verts[(i + 1) % n];
                area2 += (a.x * b.y - b.x * a.y);
            }
            return area2 > 0f;
        }

        // Signed double-area of a simple polygon. Caller can take Mathf.Abs(.) * 0.5f
        // for the area magnitude. Sign is positive for CCW, negative for CW.
        static float PolygonSignedArea2(Vector2[] verts, int n)
        {
            float a = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = verts[i];
                Vector2 p1 = verts[(i + 1) % n];
                a += p0.x * p1.y - p1.x * p0.y;
            }
            return a;
        }

        // Remove vertices that lie within `epsilon` perpendicular distance of the
        // line through their immediate neighbours (= near-collinear intermediate
        // verts). Iterates until no more removals so a long collinear run collapses
        // fully. Preserves CCW winding and exact polygon area (collinear verts
        // contribute zero to the shoelace integral). In-place: surviving verts are
        // packed at verts[0..return-1].
        //
        // Use case: MeshToCollider2DGenerator produces PolygonCollider2D paths with
        // many evenly-sampled verts along edges that are visually straight. When the
        // SH per-edge subtraction loop iterates each of these as a separate
        // half-plane, slightly different normals (from float noise / actual tilt)
        // cause precision drift at the snow-tri rim. Simplifying to true corners
        // first keeps the SH input geometrically equivalent but algorithmically
        // tight.
        //
        // epsilon = 1e-4f (= 0.1mm in world units) is well below any genuine corner
        // perpendicular-distance (which scales with edge length, typically ≥ 0.05).
        // Safe margin so this never removes a real corner — only float-noise
        // intermediate verts.
        static int SimplifyCollinear(Vector2[] verts, int n, float epsilon)
        {
            if (n < 4) return n;

            var alive = new bool[n];
            var prev = new int[n];
            var next = new int[n];
            for (int i = 0; i < n; i++) { alive[i] = true; prev[i] = (i - 1 + n) % n; next[i] = (i + 1) % n; }
            int remaining = n;
            float epsSq = epsilon * epsilon;

            bool changed;
            do
            {
                changed = false;
                for (int v = 0; v < n; v++)
                {
                    if (!alive[v]) continue;
                    if (remaining <= 3) break;
                    int ip = prev[v], in_ = next[v];
                    Vector2 a = verts[ip], b = verts[v], c = verts[in_];
                    Vector2 ac = c - a;
                    float lenSq = ac.x * ac.x + ac.y * ac.y;
                    if (lenSq < 1e-18f) continue; // a≈c degenerate; leave b alone
                    float cross = (b.x - a.x) * ac.y - (b.y - a.y) * ac.x;
                    float perpSq = (cross * cross) / lenSq;
                    if (perpSq < epsSq)
                    {
                        alive[v] = false;
                        next[ip] = in_;
                        prev[in_] = ip;
                        remaining--;
                        changed = true;
                    }
                }
            } while (changed && remaining > 3);

            if (remaining == n) return n;

            // Pack survivors via linked-list walk (preserves order). Temp buffer
            // because the walk reads from verts[] indices we'd otherwise overwrite.
            int start = -1;
            for (int i = 0; i < n; i++) if (alive[i]) { start = i; break; }
            if (start < 0) return 0;

            var packed = new Vector2[remaining];
            int cur = start;
            for (int k = 0; k < remaining; k++)
            {
                packed[k] = verts[cur];
                cur = next[cur];
            }
            for (int k = 0; k < remaining; k++) verts[k] = packed[k];
            return remaining;
        }

        // Even-odd ray-cast point-in-polygon. Used to classify multi-path nesting
        // in TryDecomposeForeignPolygon. Boundary points are not handled specially
        // (in/out flips at horizontal-line crossings); for our use the test point
        // is a polygon's own vertex which is never on another path's edge in a
        // properly authored multi-path collider.
        static bool PointInPolygon(Vector2 p, Vector2[] verts, int n)
        {
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = verts[i];
                Vector2 pj = verts[j];
                if (((pi.y > p.y) != (pj.y > p.y)) &&
                    (p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y) + pi.x))
                    inside = !inside;
            }
            return inside;
        }

        // Self-intersection test: any two non-adjacent edges crossing → not a simple
        // polygon. O(n²) edge-pair test, fine for foreign sizes (~17 verts). Reuses
        // SegmentsIntersect's strict-cross check (collinear overlaps not flagged, but
        // those would already break ear-clipping in a way the validator catches via
        // "no ear found" failure).
        static bool ValidatePolygonSimple(Vector2[] verts, int n)
        {
            if (n < 3) return false;
            for (int i = 0; i < n; i++)
            {
                Vector2 a1 = verts[i];
                Vector2 a2 = verts[(i + 1) % n];
                for (int j = i + 2; j < n; j++)
                {
                    // Edges (i,i+1) and (j,j+1) share endpoint when j+1 wraps to i.
                    if (i == 0 && j == n - 1) continue;
                    Vector2 b1 = verts[j];
                    Vector2 b2 = verts[(j + 1) % n];
                    if (SegmentsIntersect(a1, a2, b1, b2)) return false;
                }
            }
            return true;
        }

        // Ear-clipping triangulation of a simple polygon. Input may be CCW or CW —
        // canonicalised to CCW internally. Output triangles are CCW in world XY.
        // Appends one Vector2[3] per triangle to trianglesOut (caller clears on entry).
        // Returns false on degenerate / non-simple input (no ear found in a full pass,
        // or remaining vert count not 3 at end). Caller falls back to sample-based
        // when this returns false.
        //
        // Naive O(n³) implementation (no ear-cache, alive bitmap scanned linearly).
        // For foreign sizes ≲ 30 this is ~30k ops — invisible against the SH per-edge
        // loop that runs per-tri downstream. Optimise only if profiling demands.
        static bool TryEarClipPolygon(Vector2[] verts, int n, List<Vector2[]> trianglesOut)
        {
            if (n < 3) return false;

            // Working copy in CCW order. work[] indexed 0..n-1 throughout the algo.
            var work = new Vector2[n];
            if (PolygonSignedArea2(verts, n) > 0f)
                for (int i = 0; i < n; i++) work[i] = verts[i];
            else
                for (int i = 0; i < n; i++) work[i] = verts[n - 1 - i];

            // Doubly-linked list of alive verts.
            var prev = new int[n];
            var next = new int[n];
            var alive = new bool[n];
            for (int i = 0; i < n; i++) { prev[i] = (i - 1 + n) % n; next[i] = (i + 1) % n; alive[i] = true; }
            int remaining = n;

            // Outer iteration cap (n per ear emission, plus same again for collinear cleanup).
            int safety = 2 * n * n;
            while (remaining > 3 && safety-- > 0)
            {
                bool foundEar = false;
                for (int v = 0; v < n; v++)
                {
                    if (!alive[v]) continue;
                    int ip = prev[v];
                    int in_ = next[v];
                    Vector2 a = work[ip], b = work[v], c = work[in_];

                    // Convex angle test: CCW polygon → ear has positive cross.
                    float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                    if (cross <= 1e-9f) continue;

                    // No other alive vertex strictly inside (a,b,c).
                    bool blocked = false;
                    for (int u = 0; u < n; u++)
                    {
                        if (!alive[u] || u == ip || u == v || u == in_) continue;
                        if (PointInTri(work[u], a, b, c)) { blocked = true; break; }
                    }
                    if (blocked) continue;

                    trianglesOut.Add(new Vector2[3] { a, b, c });
                    alive[v] = false;
                    next[ip] = in_;
                    prev[in_] = ip;
                    remaining--;
                    foundEar = true;
                    break;
                }
                if (foundEar) continue;

                // No convex ear found. Fallback: drop a collinear / zero-area
                // vertex (cross ≈ 0) without emitting a triangle. Handles bridge-
                // induced duplicate verts in multi-path holes-aware decomposition,
                // and stripping accidental collinear runs in real polygons. If
                // even this finds nothing, the polygon is non-simple → fail.
                bool removedCollinear = false;
                for (int v = 0; v < n; v++)
                {
                    if (!alive[v]) continue;
                    int ip = prev[v];
                    int in_ = next[v];
                    Vector2 a = work[ip], b = work[v], c = work[in_];
                    float cross = (b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x);
                    if (Mathf.Abs(cross) > 1e-9f) continue;
                    alive[v] = false;
                    next[ip] = in_;
                    prev[in_] = ip;
                    remaining--;
                    removedCollinear = true;
                    break;
                }
                if (!removedCollinear) return false;
            }
            if (remaining < 3) return trianglesOut.Count > 0; // collinear cleanup may have collapsed below 3
            if (remaining > 3) return false;

            // Emit the final triangle from the surviving 3 verts in CCW link order.
            int s0 = -1;
            for (int i = 0; i < n; i++) if (alive[i]) { s0 = i; break; }
            if (s0 < 0) return false;
            int s1 = next[s0];
            int s2 = next[s1];
            trianglesOut.Add(new Vector2[3] { work[s0], work[s1], work[s2] });
            return true;
        }

        // (Note: an earlier `IsForeignFullyInsideTri` rare-case detector was removed —
        // the polygon-difference algorithm in ApplyClippingMask handles the donut case
        // natively because each split-by-edge correctly accumulates the four "outside"
        // pieces around a fully-enclosed foreign.)

        // Single-collider variant of HitsAnyForeign — used by the per-pair fallback
        // inside the clipping branch where we already know which foreign we're testing.
        static bool HitsAnyForeignOne(Collider2D col, Vector3 worldPos, float[] foreignZs, int idx, bool useZ, float zWindow)
        {
            if (col == null) return false;
            if (useZ && Mathf.Abs(worldPos.z - foreignZs[idx]) > zWindow) return false;
            return col.OverlapPoint(new Vector2(worldPos.x, worldPos.y));
        }

        // Filter out triangles where any of (3 verts, centroid) — in world XY — falls
        // inside another Collider2D on the mask. Rebuild the patch's verts/normals/
        // tris/boundaryEdges from the kept tris so the BFS taper downstream sees the
        // new directed boundary and curls the snow rim down at the cut edge.
        //
        // Perf: instead of calling scene-wide Physics2D.OverlapPoint per sample (which
        // at subdivLevels=5 fired hundreds of thousands of scene queries and froze
        // the editor), we collect candidate foreign colliders ONCE via a patch-AABB
        // OverlapAreaAll, then test each tri sample against that small candidate list
        // with the per-collider Collider2D.OverlapPoint. SnowField↔SnowField arbitration
        // (overlapPriority + InstanceID tiebreak) is applied during candidate filtering.
        //
        // SyncTransforms is needed because RebuildNow is called from Awake during scene
        // load; at that point the Physics2D scene hasn't yet synced freshly-deserialized
        // transforms and OverlapAreaAll would return nothing — leaving the snow visually
        // unmasked in play mode while edit mode (where time has elapsed) worked fine.
        SnowTopSurfaceExtractor.TopPatch MaskOutOverlap(
            SnowTopSurfaceExtractor.TopPatch patch, Transform meshT)
        {
            if (patch.tris == null || patch.tris.Length < 3) return patch;

            Physics2D.SyncTransforms();

            // Patch AABB in world XY (use verts only — meshT may rotate/scale them).
            float wxmin = float.PositiveInfinity, wxmax = float.NegativeInfinity;
            float wymin = float.PositiveInfinity, wymax = float.NegativeInfinity;
            for (int i = 0; i < patch.verts.Length; i++)
            {
                Vector3 w = meshT.TransformPoint(patch.verts[i]);
                if (w.x < wxmin) wxmin = w.x;
                if (w.x > wxmax) wxmax = w.x;
                if (w.y < wymin) wymin = w.y;
                if (w.y > wymax) wymax = w.y;
            }

            var filter = ContactFilter2D.noFilter;
            filter.SetLayerMask(overlapLayerMask);
            filter.useTriggers = false;

            var hitList = new List<Collider2D>(16);
            Physics2D.OverlapArea(new Vector2(wxmin, wymin), new Vector2(wxmax, wymax), filter, hitList);

            // Reduce candidates to the foreign-and-loses-arbitration set. Anything else
            // (host's own collider, sister SnowField that wins arbitration, our own
            // manual-keep polys) is ignored.
            int myId = hostCollider != null ? hostCollider.GetInstanceID() : GetInstanceID();
            var foreign = new List<Collider2D>(hitList.Count);
            for (int i = 0; i < hitList.Count; i++)
            {
                var h = hitList[i];
                if (h == null) continue;
                if (h == hostCollider) continue;
                // Belt-and-braces: our own manual-keep polys are an override mechanism,
                // not occluders. Triggers are already excluded by useTriggers=false above,
                // but skip them here too so a non-trigger keep poly can't accidentally
                // mask the field it's supposed to restore.
                if (IsManualKeep(h)) continue;
                if (SnowFieldRegistry.TryGet(h, out var otherField) && otherField != null && otherField != this)
                {
                    if (otherField.overlapPriority > overlapPriority) { foreign.Add(h); continue; }
                    if (otherField.overlapPriority < overlapPriority) continue;
                    if (h.GetInstanceID() > myId) foreign.Add(h);
                    continue;
                }
                foreign.Add(h);
            }

            // Debug log — surface every foreign collider this patch is masking against,
            // with hierarchy path and bounds. Click the log entry to ping the GO.
            if (debugLogMaskColliders)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append($"[SnowField '{name}'] mask candidates: hit by AABB={hitList.Count}, kept as foreign={foreign.Count}");
                for (int i = 0; i < foreign.Count; i++)
                {
                    var f = foreign[i];
                    if (f == null) continue;
                    debugLastForeignColliders.Add(f);
                    sb.Append($"\n  [{i}] {GetGameObjectPath(f.gameObject)} ({f.GetType().Name}) pos={f.transform.position} bounds={f.bounds}");
                }
                Debug.Log(sb.ToString(), this);
            }

            int triCount = patch.tris.Length / 3;
            var keep = new bool[triCount];
            int kept = 0;

            if (foreign.Count == 0)
            {
                // Nothing to mask against — keep everything.
                return patch;
            }

            // Pre-cache per-foreign world Z so the per-tri Z-window check avoids a
            // transform.position dispatch per (tri, foreign) pair.
            var foreignZs = new float[foreign.Count];
            for (int i = 0; i < foreign.Count; i++)
            {
                var c = foreign[i];
                foreignZs[i] = c != null ? c.transform.position.z : 0f;
            }

            bool useZ = maskUsesZ;
            float zWin = maskZWindow;
            bool hasKeepPolys = manualKeepColliders != null && manualKeepColliders.Count > 0;
            // Reused per-collider world-space path buffer for the tri-vs-collider overlap test.
            Vector2[] keepPathBuf = null;

            // Mode branch: GeometricClipping path runs Sutherland-Hodgman tri-vs-foreign
            // subtraction and produces its own re-derived patch (incl. boundary). Returns
            // the original patch unchanged if no tris were actually clipped.
            if (maskMode == OverlapMaskMode.GeometricClipping)
            {
                return ApplyClippingMask(patch, meshT, foreign, foreignZs, useZ, zWin, hasKeepPolys, ref keepPathBuf);
            }

            // SampleBased path (default, unchanged from before this commit). Sample 4
            // points per tri (3 verts + centroid) for the FOREIGN mask.
            // Centroid-only kept tris that straddle the overlap boundary — one vertex
            // pokes into the other platform — producing a thin tearing band along the
            // seam. Dropping the tri if any of the 4 samples is inside another platform
            // lets the BFS taper close the gap cleanly.
            for (int t = 0; t < triCount; t++)
            {
                int ia = patch.tris[t * 3];
                int ib = patch.tris[t * 3 + 1];
                int ic = patch.tris[t * 3 + 2];
                Vector3 wa = meshT.TransformPoint(patch.verts[ia]);
                Vector3 wb = meshT.TransformPoint(patch.verts[ib]);
                Vector3 wc = meshT.TransformPoint(patch.verts[ic]);
                Vector3 wcen = new Vector3(
                    (wa.x + wb.x + wc.x) / 3f,
                    (wa.y + wb.y + wc.y) / 3f,
                    (wa.z + wb.z + wc.z) / 3f);

                bool dropTri = HitsAnyForeign(wa,   foreign, foreignZs, useZ, zWin)
                            || HitsAnyForeign(wb,   foreign, foreignZs, useZ, zWin)
                            || HitsAnyForeign(wc,   foreign, foreignZs, useZ, zWin)
                            || HitsAnyForeign(wcen, foreign, foreignZs, useZ, zWin);

                // Manual-keep override: tri-vs-poly overlap test (NOT 4-sample). Restore
                // the tri whenever it shares any area with a user-drawn keep polygon —
                // verts of one inside the other, or edges cross. Old 4-sample test left
                // a blocky grid pattern when the polygon sat between sample points.
                if (dropTri && hasKeepPolys)
                {
                    Vector2 a2 = new Vector2(wa.x, wa.y);
                    Vector2 b2 = new Vector2(wb.x, wb.y);
                    Vector2 c2 = new Vector2(wc.x, wc.y);
                    if (TriOverlapsAnyKeep(a2, b2, c2, ref keepPathBuf))
                        dropTri = false;
                }

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
                    isSlopeCutAdjacent = patch.isSlopeCutAdjacent != null ? new bool[0] : null,
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
            bool[] newIsSlopeCut = patch.isSlopeCutAdjacent != null && patch.isSlopeCutAdjacent.Length == origV ? new bool[newV] : null;
            for (int i = 0; i < origV; i++)
            {
                int n = oldToNew[i];
                if (n < 0) continue;
                newVerts[n] = patch.verts[i];
                newNorms[n] = patch.vertNormals[i];
                if (newIsSlopeCut != null) newIsSlopeCut[n] = patch.isSlopeCutAdjacent[i];
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
                isSlopeCutAdjacent = newIsSlopeCut,
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
                runtimeMaterial.SetColor(IdFootprintTint, settings.footprintTint);

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
            // Debug overlay: foreign colliders the last rebuild treated as maskers,
            // drawn as red wire boxes using world-space AABB. Independent of the patch
            // outline gizmos so it stays visible whether or not the field is selected.
            if (debugLogMaskColliders && debugLastForeignColliders.Count > 0)
            {
                Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.9f);
                foreach (var f in debugLastForeignColliders)
                {
                    if (f == null) continue;
                    var b = f.bounds;
                    Gizmos.DrawWireCube(b.center, b.size);
                }
            }

            if (!drawPatchOutlines && !drawVertNormals) return;
            if (sourceMeshFilter == null || sourceMeshFilter.sharedMesh == null) return;

            var t = sourceMeshFilter.transform;

            // Recompute on draw (cheap for typical patch sizes; only runs when selected).
            var patches = SnowTopSurfaceExtractor.Extract(sourceMeshFilter.sharedMesh, MaxSnowSlopeDeg, detectSlopeCut: SlopeCutTaperHops > 0);

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

        // "Path/To/GO" — used in mask-debug logs so the user can find the offending
        // collider quickly even when the scene has many similarly-named GameObjects.
        static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "(null)";
            string path = go.name;
            var t = go.transform.parent;
            while (t != null)
            {
                path = t.name + "/" + path;
                t = t.parent;
            }
            return path;
        }
    }
}
