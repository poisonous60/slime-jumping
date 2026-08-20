using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>How <see cref="SnowField"/> decides what to drop from each snow tri when masking against foreign colliders.</summary>
    public enum OverlapMaskMode
    {
        /// <summary>Drops a whole tri if any of its 3 verts or centroid is inside a foreign collider; cascades via shared verts.</summary>
        SampleBased = 0,

        /// <summary>Sutherland-Hodgman subtraction, keeping (tri ∩ NOT foreign). Falls back to SampleBased per pair for unsupported foreigns.</summary>
        GeometricClipping = 1,
    }

    /// <summary>
    /// Drape a slope-filtered snow visual on a mesh-based platform: one child
    /// "SnowStrip_N" per upward-facing connected component of the host triangulation,
    /// height driven by an MPB-bound StructuredBuffer&lt;float&gt; (_SnowHeightBuf).
    /// Pure visual — no collider; the slime walks on the host's own collider. Carve
    /// routes via SnowFieldRegistry(hostCollider → this) and ticks only activeStrips.
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
        [Tooltip("Background decoration mode. Keeps hostCollider permanently unassigned so this field never registers with SnowFieldRegistry — the slime can never carve it — and skips the overlap mask without warning. Auto-resolution from the parent chain is disabled while this is on, so re-parenting under a collider-bearing platform can't silently make the field carvable. Coverage is also unrestricted: the slope filter opens to 89.99 deg, the min-patch-tri filter drops to 1, subdivision drops to 0 (nothing carves, so the extra tris would only alias at distance), and the slope-to-height curve is bypassed so steep faces keep full thickness instead of emitting zero-height snow that z-fights the host. Override Max Snow Slope still wins if you want a specific angle back. Use for scenery snow that extends the playable field visually.")]
        public bool backgroundOnly = false;

        [Tooltip("Host platform's Collider2D. Registered as the routing key in SnowFieldRegistry — SlimeController.SurfaceCollider must equal this for carve events to land here. Ignored and forced null while Background Only is on.")]
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

        // Last rebuild's foreign colliders; filled only when debugLogMaskColliders is on.
        [System.NonSerialized] readonly HashSet<Collider2D> debugLastForeignColliders = new HashSet<Collider2D>();

        // -- Settings accessors (settings asset wins, otherwise fall back) -------

        const float DefaultMaxSnowSlopeDeg = 45f;
        const int   DefaultMinPatchTris    = 3;
        const float DefaultRestThickness   = 0.25f;
        const int   DefaultEdgeTaperHops   = 1;
        const int   DefaultSubdivLevels    = 1;
        const float DefaultAccumRate       = 0.05f;
        const float DefaultSettleRate      = 0f;
        const float DefaultHeightMaxMult   = 1.5f;

        // Background fields cover every upward face: slope filter opens to the
        // extractor's clamp ceiling and single-triangle patches are kept.
        const float BackgroundMaxSnowSlopeDeg = 89.99f;
        const int   BackgroundMinPatchTris    = 1;
        // Subdivision only buys carve resolution; a background field never carves,
        // so extra tris would just alias at distance.
        const int   BackgroundSubdivLevels    = 0;

        public float MaxSnowSlopeDeg =>
            overrideMaxSnowSlope ? maxSnowSlopeDegOverride
            : backgroundOnly     ? BackgroundMaxSnowSlopeDeg
            : settings != null   ? settings.maxSnowSlopeDeg
            : DefaultMaxSnowSlopeDeg;
        public int   MinPatchTris    => backgroundOnly ? BackgroundMinPatchTris
            : settings != null ? settings.minPatchTris : DefaultMinPatchTris;
        public float RestThickness   => settings != null ? settings.restThickness   : DefaultRestThickness;
        public int   EdgeTaperHops   => settings != null ? settings.edgeTaperHops   : DefaultEdgeTaperHops;
        public int   SubdivLevels    => backgroundOnly ? BackgroundSubdivLevels
            : settings != null ? settings.subdivLevels : DefaultSubdivLevels;
        public float AccumulationRate=> settings != null ? settings.accumulationRate: DefaultAccumRate;
        public float SettleRate      => settings != null ? settings.settleRate      : DefaultSettleRate;
        public float HeightMaxMultiplier => settings != null ? settings.heightMaxMultiplier : DefaultHeightMaxMult;
        public Material SnowMaterial => settings != null ? settings.snowMaterial    : null;

        public int SlopeCutTaperHops => settings != null ? settings.slopeCutTaperHops : 0;

        // Unity fires no callback on MeshFilter.sharedMesh swaps, so the editor watcher
        // polls this hash to know the host mesh changed.
        [HideInInspector] public int lastHostMeshHash;

        // Dedupe delayCall storms — slider drags fire OnValidate many times per frame.
        [System.NonSerialized] bool rebuildScheduled;

        // Dirty or still-recovering strips; empty on an idle field → FixedUpdate no-ops.
        [System.NonSerialized] readonly HashSet<SnowStrip> activeStrips = new HashSet<SnowStrip>();
        [System.NonSerialized] List<SnowStrip> tickBuffer;

        // Unregister by the key we actually registered — hostCollider can be reassigned
        // in the inspector mid-session.
        [System.NonSerialized] Collider2D registeredCollider;

        // Clone of SnowSettings.snowMaterial; the shared asset is never mutated.
        [System.NonSerialized] Material runtimeMaterial;

        // Duplicating the GO copies SnowStrip children whose MeshFilter still points at
        // the ORIGINAL's mesh, so ClearChildren may only destroy meshes in this set.
        [System.NonSerialized] readonly HashSet<Mesh> ownedMeshes = new HashSet<Mesh>();

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
            // Children are HideFlags.DontSave, so a scene/domain reload wipes them —
            // re-emit or the snow visual stays gone until a manual Rebuild Now.
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
            // Flush before forgetting the strips, else the GPU buffer keeps its last
            // uploaded value and a re-enable shows stale depth until the next carve.
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
                bool stillRecovering = strip.TickRecovery(dt, AccumulationRate, SettleRate);
                strip.FlushIfDirty();
                if (!stillRecovering) activeStrips.Remove(strip);
            }
        }

        // -- Carve dispatch (called by SnowStamper) ------------------------------

        // Carve mapping fallback values (used only when no SnowSettings asset is wired).
        const float DefaultMinImpactSpeed   = 1.5f;
        const float DefaultDepthPerImpact   = 0.012f;
        const float DefaultDepthMax         = 0.20f;
        const float DefaultRadiusPerImpact  = 0.05f;
        const float DefaultRadiusMax        = 0.8f;

        /// <summary>Map a world contact + impact speed through SnowSettings into <see cref="Carve"/>; impacts below minImpactSpeed are rejected.</summary>
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

        /// <summary>Carve at a world point with explicit depth/radius, bypassing the impact-speed mapping in <see cref="HandleLanded"/>.</summary>
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

        /// <summary>Crater + raised rim at a world point. ringRadius must exceed dipRadius, and dipDepth must dominate ringRaise or the raise's center-peaked falloff refills the crater.</summary>
        public void Splash(Vector2 contactWorld, float dipDepth, float dipRadius, float ringRaise, float ringRadius)
        {
            if (sourceMeshFilter == null) return;
            bool wantDip   = dipDepth  > 0f && dipRadius  > 0f;
            bool wantRaise = ringRaise > 0f && ringRadius > 0f;
            if (!wantDip && !wantRaise) return;

            Vector3 localImpact = sourceMeshFilter.transform.InverseTransformPoint(
                new Vector3(contactWorld.x, contactWorld.y, 0f));

            var profile = CarveProfile;
            float maxMult = HeightMaxMultiplier;
            for (int i = 0; i < transform.childCount; i++)
            {
                var child = transform.GetChild(i);
                if (child == null) continue;
                var strip = child.GetComponent<SnowStrip>();
                if (strip == null) continue;

                bool changed = false;
                if (wantDip   && strip.CarveLocal(localImpact, dipDepth, dipRadius, profile))
                    changed = true;
                if (wantRaise && strip.RaiseLocal(localImpact, ringRaise, ringRadius, profile, maxMult))
                    changed = true;
                if (changed) activeStrips.Add(strip);
            }
        }

        /// <summary>Nearest-vertex hit-test against heightRest &gt; 0. The slope-normal gate
        /// (surfaceNormal.y ≥ cos(maxSnowSlopeDeg)) is deliberately the caller's job.</summary>
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

        /// <summary>Remaining snow coverage at a world xy: 0 = bare or off-patch, 1 = full thickness; max across overlapping patches.</summary>
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
                // Skip our own SnowStrip_* children, else the previous rebuild's output
                // becomes the next rebuild's input.
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

        // Walk the parent chain: the platform's collider usually lives on the parent
        // while the mesh (with this SnowField) lives on a child.
        void ResolveHostCollider()
        {
            if (backgroundOnly) { hostCollider = null; return; }
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

            debugLastForeignColliders.Clear();

            var mesh = sourceMeshFilter.sharedMesh;
            var patches = SnowTopSurfaceExtractor.Extract(mesh, MaxSnowSlopeDeg, detectSlopeCut: SlopeCutTaperHops > 0);

            // Without hostCollider the host's own polygon hits OverlapPoint at every
            // interior vert and the whole patch drops — warn and skip the mask instead.
            bool maskActive = excludeOverlapWithOtherPlatforms && !backgroundOnly;
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

            BuildRuntimeMaterial();

            int emitted = 0;
            for (int s = 0; s < patches.Count; s++)
            {
                var patch = patches[s];
                if (patch.verts == null || patch.verts.Length < 3) continue;
                int triCount = patch.tris != null ? patch.tris.Length / 3 : 0;
                // Filter on the ORIGINAL triangulation, not the subdivided result, else
                // 1-tri noise patches inflate past the threshold.
                if (triCount < MinPatchTris) continue;

                int subdiv = SubdivLevels;
                if (subdiv > 0)
                    patch = SnowPatchSubdivider.Subdivide(patch, subdiv);

                // Order matters: after Subdivide so the mask runs at emit resolution,
                // before the BFS taper so new boundary verts get the same edge ramp.
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

                // Height ramp = min of two BFS passes (all-boundary seeds, slope-cut-only
                // seeds); verts unreached by a pass score 1.0 so the stricter taper wins.

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
                    // Filter seeds by the CURRENT boundary, else verts whose slope-cut
                    // edges the mask dropped seed phantom taper holes inside the patch.
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

                // Per-vertex slope→height via SnowSettings.slopeToHeightScale. Verts shared
                // by borderline tris carry an averaged normal that reads a non-trivial
                // angle on flat ground — flatten the curve's low end if snow thins there.
                {
                    // Background fields skip the curve: their slope filter is wide open,
                    // so a zeroing curve emits coplanar snow that z-fights the host.
                    var slopeCurve = backgroundOnly ? null
                        : settings != null ? settings.slopeToHeightScale : null;
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
                // Follow the host's layer, else background snow lands on the overlay
                // camera and draws over the background.
                go.layer = gameObject.layer;
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                // DontSave, not DontSaveInBuild: their meshes are runtime-built and
                // deserialize as null, leaving empty SnowStrip GOs in a saved scene.
                go.hideFlags = HideFlags.DontSave;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                // The host terrain already casts; strips only add shadow-pass draws.
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (runtimeMaterial != null) mr.sharedMaterial = runtimeMaterial;

                var ss = go.AddComponent<SnowStrip>();
                ss.verts = patch.verts;
                ss.vertNormals = patch.vertNormals;
                ss.tris = patch.tris;
                ss.boundaryEdges = patch.boundaryEdges;
                // Carve mutates heightCurrent only; heightRest stays the taper profile.
                ss.heightRest = heights;
                ss.heightCurrent = (float[])heights.Clone();
                ss.host = transform;

                var built = SnowMeshBuilder.Build(patch, heights, new SnowMeshBuilder.BuildParams
                {
                    meshName = $"SnowStrip_{emitted - 1}_Mesh",
                });
                mf.sharedMesh = built;
                ownedMeshes.Add(built);

                ss.Init(mr);
            }

            lastHostMeshHash = ComputeHostMeshHash(mesh);
        }

        // useZ: a hit only counts within zWindow of the foreign's world Z, so a Z-deep
        // mesh keeps snow on its back slice while its front slice still masks.
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

        // Also used during candidate filtering so an override collider never acts as a
        // foreign masker, even when the user forgot isTrigger=true.
        bool IsManualKeep(Collider2D c)
        {
            if (manualKeepColliders == null) return false;
            for (int i = 0; i < manualKeepColliders.Count; i++)
                if (manualKeepColliders[i] == c) return true;
            return false;
        }

        // Area overlap, not 4-sample OverlapPoint: sampling missed keep colliders that
        // sat between the 4 points and restored tris all-or-nothing (blocky grid).
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
            // Cheap path: OverlapPoint works for every Collider2D subtype, so this
            // covers Circle/Capsule/Edge/Composite without type-specific extraction.
            if (col.OverlapPoint(a)) return true;
            if (col.OverlapPoint(b)) return true;
            if (col.OverlapPoint(c)) return true;

            // Only Polygon/Box get the exact test; other types keep the cheap path, so
            // a partial overlap with no vertex inside the other shape won't restore.
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
                // Corners from local size + offset: box.bounds is world-axis-aligned and
                // loses orientation on a rotated GO.
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

        // Strict (non-collinear, non-endpoint-sharing) segment intersection; collinear
        // overlaps are left to the PointInTri / OverlapPoint checks above.
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

        // worldXY drives the clip math; (ba, bb, bc) carry the parent tri's barycentric
        // coords so mesh-local pos + normal interpolate once, after all clipping.
        struct ClipVert
        {
            public Vector2 worldXY;
            public float ba, bb, bc;
        }

        // Per tri: keep-collider preempt → SH-subtract each foreign (sample-based per pair
        // when unsupported) → fan-triangulate. Returns patch unchanged if nothing clipped.
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

            // Adjacent tris emitting a clip vert at the same shared edge MUST collapse to
            // one index, else boundary regen sees a seam and the BFS taper seeds spurious
            // rims through the patch interior.
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

            Vector2[] foreignWorldBuf = null;
            // One convex world-XY piece of the foreign per element, filled by
            // TryDecomposeForeignPolygon.
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

                // Polygon difference: `pieces` = convex fragments of the tri kept so far.
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

                    // AABB pre-filter; if every foreign is rejected here triClipped stays
                    // false and the original tri is emitted unchanged.
                    var fb = col.bounds;
                    if (fb.max.x < triMinX || fb.min.x > triMaxX ||
                        fb.max.y < triMinY || fb.min.y > triMaxY) continue;

                    int foreignN = TryGetConvexForeignWorldVerts(col, ref foreignWorldBuf);
                    if (foreignN >= 3)
                    {
                        pieces = SubtractConvexPieceFromPieces(pieces, foreignWorldBuf, foreignN);
                        triClipped = true;
                        if (pieces.Count == 0) { triDropped = true; break; }
                        continue;
                    }

                    // Convex fast-path rejected (concave / multi-path polygon, Edge,
                    // Composite): decompose into convex pieces and subtract each.
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

                    // Decomposition failed too: sample-based fallback for THIS pair only.
                    if (HitsAnyForeignOne(col, wa, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wb, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wc, foreignZs, fi, useZ, zWin) ||
                        HitsAnyForeignOne(col, wcen, foreignZs, fi, useZ, zWin))
                    {
                        triDropped = true;
                        break;
                    }
                    triClipped = true;
                }

                if (triDropped) continue;
                if (pieces.Count == 0) continue;

                if (!triClipped)
                {
                    // Every foreign was Z-filtered or AABB-rejected: emit unchanged.
                    int ka = Canon(va, na, sca);
                    int kb = Canon(vb, nb, scb);
                    int kc = Canon(vc, nc, scc);
                    outTris.Add(ka); outTris.Add(kb); outTris.Add(kc);
                    continue;
                }

                // Foreign passed AABB but didn't actually cut: pieces is [originalTri],
                // so emit the original corners rather than re-deriving them.
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

        // Barycentric-interpolate pos + normal, then dedup via Canon. Only original tri
        // corners inherit slopeCut — a mask cut is not a slope cut.
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

        // Split the polygon by a directed edge into inside (LEFT, for a CCW clip polygon)
        // and outside (RIGHT); crossings emit the intersection point into BOTH halves.
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

        // For a CCW polygon, cross > 0 is RIGHT of the edge = exterior. Flipping the sign
        // silently turns the whole mask into `tri ∩ foreign`.
        static bool IsOutsideEdge(Vector2 p, Vector2 edgeStart, Vector2 edgeDir)
        {
            Vector2 d = p - edgeStart;
            float cross = d.x * edgeDir.y - d.y * edgeDir.x;
            return cross > 0f;
        }

        // Intersection of segment (prev, curr) with the line through (edgeStart, +edgeDir);
        // bary lerps at the same t, which stays valid through multi-edge clips.
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

        // Returns (input pieces − piece): each piece is split per piece-edge, outside-half
        // kept, inside-half carried on, and the final inside-all leftover discarded.
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

        // Slow path when TryGetConvexForeignWorldVerts rejects the collider: tile the
        // foreign with disjoint convex CCW pieces in world XY. Returns piece count;
        // 0 = not applicable or self-check failed → caller falls back to sample-based.
        static int TryDecomposeForeignPolygon(
            Collider2D col, List<Vector2[]> piecesOut, ref Vector2[] scratch)
        {
            piecesOut.Clear();
            if (!(col is PolygonCollider2D poly)) return 0;
            int pathCount = poly.pathCount;
            if (pathCount < 1) return 0;

            var polyT = poly.transform;
            Vector2 ofs = poly.offset;

            // Every path to its own world-XY array, validated simple.
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

            // Even depth = solid, odd = hole. Assumes paths never cross (true for
            // MeshToCollider2DGenerator output).
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

        // Splice a CW hole into a CCW outer → one weakly simple CCW polygon; the duplicated
        // bridge verts are cleared by ear-clipping's collinear fallback. Bridge pick skips
        // Eberly's visibility refinement (rightmost hole vert + closest +x hit).
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

            // M and outer[bridgeOuterIdx] each appear twice — those are the bridge edges.
            Vector2 V = outer[bridgeOuterIdx];
            var insertion = new List<Vector2>(holeN + 2);
            insertion.Add(M);
            for (int k = 1; k < holeN; k++) insertion.Add(hole[(mIdx + k) % holeN]);
            insertion.Add(M);
            insertion.Add(V);
            outer.InsertRange(bridgeOuterIdx + 1, insertion);
            return true;
        }

        // Convex CCW world-XY polygon for the foreign collider; returns 0 for
        // unsupported / non-convex / multi-path so the caller uses sample-based.
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
                // MeshToCollider2DGenerator's evenly-spaced verts on straight edges each
                // become an SH half-plane; the accumulated float drift nibbles ~1 sub-tri
                // off the snow rim, so collapse them to true corners first.
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
                // 24-gon: matches OverlapPoint under uniform scale; non-uniform scale
                // yields an ellipse approximation, accepted.
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
                // Two semicircles + two straight edges, 12 segments per cap.
                const int CAP_SEG = 12;
                var cT = capsule.transform;
                Vector2 ofs = capsule.offset;
                Vector2 sz = capsule.size;
                bool vertical = capsule.direction == CapsuleDirection2D.Vertical;
                // r = perpendicular half-extent; L = inner straight length on the long axis.
                float r, L;
                if (vertical) { r = sz.x * 0.5f; L = Mathf.Max(0f, sz.y - sz.x); }
                else          { r = sz.y * 0.5f; L = Mathf.Max(0f, sz.x - sz.y); }

                // 2 corners + CAP_SEG top-arc + 1 corner + (CAP_SEG-1) bottom-arc verts.
                int N = 2 * CAP_SEG + 2;
                if (worldBuf == null || worldBuf.Length < N) worldBuf = new Vector2[N];
                // C# forbids capturing a `ref` parameter in a lambda, hence the alias.
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
                // last top-arc vert is the left-top corner; left edge runs straight down.
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

        // Shoelace test: positive area = CCW with +Y up, matching Unity world XY.
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

        // Signed DOUBLE area; take Mathf.Abs(.) * 0.5f for the magnitude.
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

        // Drop near-collinear verts in place (survivors packed at verts[0..return-1]);
        // winding and area are preserved. epsilon is a perpendicular distance — 1e-4
        // (0.1mm) sits far below any genuine corner, which scales with edge length.
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

            // Temp buffer: the linked-list walk reads verts[] indices we'd overwrite.
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

        // Even-odd ray cast; boundary points are undefined, which is safe here because
        // callers only test a path's own vertex against a different path.
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

        // O(n²) self-intersection test. Collinear overlaps slip through SegmentsIntersect
        // but surface later as ear-clipping's "no ear found" failure.
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

        // Ear-clip a simple polygon (CW or CCW in, CCW tris out) appending to
        // trianglesOut; false on degenerate / non-simple input. Naive O(n³), fine for
        // foreign sizes ≲ 30 next to the per-tri SH loop downstream.
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

                // No ear: drop a zero-area vertex without emitting a triangle — this is
                // what clears the bridge-induced duplicate verts from hole splicing.
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

        // Single-collider variant of HitsAnyForeign for the per-pair clipping fallback.
        static bool HitsAnyForeignOne(Collider2D col, Vector3 worldPos, float[] foreignZs, int idx, bool useZ, float zWindow)
        {
            if (col == null) return false;
            if (useZ && Mathf.Abs(worldPos.z - foreignZs[idx]) > zWindow) return false;
            return col.OverlapPoint(new Vector2(worldPos.x, worldPos.y));
        }

        // Candidates from ONE patch-AABB OverlapArea — per-sample scene queries froze the
        // editor at subdivLevels=5. SyncTransforms first: RebuildNow runs from Awake before
        // Physics2D syncs deserialized transforms, and OverlapArea would return nothing.
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

            // Keep only colliders that are foreign AND win arbitration against us.
            int myId = hostCollider != null ? hostCollider.GetInstanceID() : GetInstanceID();
            var foreign = new List<Collider2D>(hitList.Count);
            for (int i = 0; i < hitList.Count; i++)
            {
                var h = hitList[i];
                if (h == null) continue;
                if (h == hostCollider) continue;
                // A keep poly authored without isTrigger would otherwise mask the very
                // field it exists to restore.
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

            // Pre-cache world Z to avoid a transform.position dispatch per (tri, foreign).
            var foreignZs = new float[foreign.Count];
            for (int i = 0; i < foreign.Count; i++)
            {
                var c = foreign[i];
                foreignZs[i] = c != null ? c.transform.position.z : 0f;
            }

            bool useZ = maskUsesZ;
            float zWin = maskZWindow;
            bool hasKeepPolys = manualKeepColliders != null && manualKeepColliders.Count > 0;
            Vector2[] keepPathBuf = null;

            // GeometricClipping re-derives its own patch, boundary included.
            if (maskMode == OverlapMaskMode.GeometricClipping)
            {
                return ApplyClippingMask(patch, meshT, foreign, foreignZs, useZ, zWin, hasKeepPolys, ref keepPathBuf);
            }

            // 4 samples per tri (3 verts + centroid): centroid alone kept straddling tris
            // whose vertex poked into the other platform, tearing a band along the seam.
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

                // Area overlap, NOT 4-sample: a keep poly sitting between sample points
                // restored nothing and left a blocky grid.
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

            // Directed boundary (a→b present, b→a absent) — same scheme
            // SnowTopSurfaceExtractor uses, so downstream sees a normal patch.
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
                // Destroying the GO alone leaks the runtime Mesh until GC, and every
                // slider tweak rebuilds.
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                {
                    var m = mf.sharedMesh;
                    mf.sharedMesh = null;
                    if (ownedMeshes.Remove(m))
                    {
                        if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
                    }
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

            // Clone so keyword/texture writes never dirty the source asset; DontSave
            // keeps it out of the scene file.
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

            // Never push a null texture: it overrides the slot with a black 1×1 and
            // Quibli reads a black ramp as everything-shadowed.
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
            // Drop the SnowStrip_* children so the visual leaves with the component.
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
            // Red wire boxes for last rebuild's maskers; drawn even when unselected.
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

            // Recompute on draw; cheap for typical patch sizes.
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

        // "Path/To/GO" for mask-debug logs; scenes have many same-named GameObjects.
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
