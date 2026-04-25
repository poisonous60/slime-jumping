using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    // Passable 3D-oriented cloth (#16). Verlet mass-spring grid simulated in the
    // cloth's local XY plane — transform.rotation places the plane anywhere in
    // world space (a "doorway curtain" uses Euler(0, 90, 0) so local +X maps to
    // world +Z). Mesh is extruded along local ±Z/2 for authored thickness; side
    // skirts close the front/back layers around the perimeter.
    //
    // Slime passes through (no collider), nodes get pushed out of the slime's
    // 3D sphere (spherical collision in local frame), cloth drags the slime
    // with three commitment safeguards (per-step cap, accumulated clamp, zero
    // during Attached/Charging). Ground collision uses Physics2D.OverlapCircle
    // + Collider2D.ClosestPoint in world XY — correct for rotated cloth because
    // Collider2D is infinite in world Z.
    //
    // Reference: docs/기능_구현_가이드.md §16, docs/클로드 연구/Unity 2D 플랫포머를
    // 위한 천 시뮬레이션 완전 가이드.md. Jakobsen 2003 + pikuma.com tutorial.
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ClothField : MonoBehaviour
    {
        public enum PinPreset { TopRow }

        [Header("Topology (Local Frame)")]
        [Tooltip("Number of node columns (local +X axis).")]
        [SerializeField, Min(2)] int gridColumns = 10;
        [Tooltip("Number of node rows (local -Y axis; row 0 = top).")]
        [SerializeField, Min(2)] int gridRows = 14;
        [Tooltip("World units per cell (both axes).")]
        [SerializeField, Min(0.01f)] float cellSize = 0.25f;
        [Tooltip("Mesh thickness along local Z (front at -thickness/2, back at +thickness/2). Purely visual; sim stays 2D.")]
        [SerializeField, Min(0f)] float clothThickness = 0.12f;

        [Header("Pins")]
        [SerializeField] PinPreset pinPreset = PinPreset.TopRow;

        [Header("Solver")]
        [SerializeField, Range(1, 20)] int constraintIterations = 10;
        [Tooltip("Per-step velocity retention. Closer to 1 = longer oscillation, more visible sway.")]
        [SerializeField, Range(0f, 1f)] float damping = 0.995f;
        [SerializeField, Range(1, 8)] int simulationSubsteps = 2;
        [Tooltip("Structural (horiz+vert neighbor) softness. 0 = fully rigid (cloth refuses to sway under lateral wind). " +
                 "0.15~0.3 = visibly responsive to wind.")]
        [SerializeField, Range(0f, 1f)] float structuralSoftness = 0.2f;
        [Tooltip("Shear (diagonal) softness. Higher = more parallelogram skew allowed → more wrinkles.")]
        [SerializeField, Range(0f, 1f)] float shearSoftness = 0.35f;
        [Tooltip("Bend (2-step) softness. Higher = floppier folds.")]
        [SerializeField, Range(0f, 1f)] float bendSoftness = 0.6f;

        [Header("Forces")]
        [Tooltip("Multiplier on Physics2D.gravity applied to every node.")]
        [SerializeField] float nodeGravityScale = 0.6f;
        [Tooltip("Wind acceleration in WORLD space so rotated curtains share direction.")]
        [SerializeField] Vector3 windAccelerationWorld = new Vector3(0.5f, 0f, 0f);
        [Tooltip("Per-node magnitude noise on the wind vector. Creates uneven push across the cloth.")]
        [SerializeField, Range(0f, 1f)] float windTurbulence = 0.4f;
        [Tooltip("Per-node PERPENDICULAR (local-Z, out-of-plane) turbulence as fraction of wind magnitude. " +
                 "This is what produces visible wrinkles/flutter — without it all nodes drift in the same " +
                 "direction and the cloth translates as a rigid body.")]
        [SerializeField, Range(0f, 2f)] float windFlutter = 0.6f;
        [SerializeField, Min(0f)] float windFrequency = 0.6f;

        [Header("Slime Interaction")]
        [Tooltip("Drag coefficient. 0.5 = light curtain, 1.5+ = heavy drape.")]
        [SerializeField, Min(0f)] float clothDragCoefficient = 0.8f;
        [Tooltip("Hard cap on drag per second (fraction of speed). Commitment safeguard #1.")]
        [SerializeField, Range(0f, 1f)] float maxDragPerSecond = 0.4f;
        [Tooltip("Max fraction of entry speed removed during one traversal. Commitment safeguard #2.")]
        [SerializeField, Range(0f, 1f)] float maxAccumulatedDrag = 0.5f;
        [Tooltip("Slime radius override. -1 = auto-read from SlimeController.SlimeRadius each FixedUpdate (default).")]
        [SerializeField] float slimeRadiusOverride = -1f;
        [Tooltip("Extra local-Z slack beyond thickness/2 + slimeRadius before slime is considered 'in cloth'.")]
        [SerializeField, Min(0f)] float thicknessGateMargin = 0.3f;
        [Tooltip("Fraction of slime's velocity transferred to cloth nodes inside the sphere (trailing effect). 0 = off.")]
        [SerializeField, Range(0f, 1f)] float slimeToNodeVelocityTransfer = 0.12f;

        [Header("Ground Collision")]
        [Tooltip("Node collision radius for ground queries (small).")]
        [SerializeField, Min(0f)] float nodeCollisionRadius = 0.02f;
        [Tooltip("Layers treated as ground. If 0 at Awake, auto-resolves to 'Environment' layer.")]
        [SerializeField] LayerMask groundLayerMask = 0;

        [Header("Performance")]
        [Tooltip("Skip sim + mesh update when the cloth's AABB is outside the culling camera frustum. " +
                 "Default OFF — enable per-curtain in scenes with many cloth instances. For rotated " +
                 "(edge-on) curtains the renderer AABB can be very thin; test before shipping.")]
        [SerializeField] bool disableOffscreen = false;
        [Tooltip("Camera used for frustum culling. Null = Camera.main at runtime.")]
        [SerializeField] UnityEngine.Camera cullingCamera;

        [Header("Rendering")]
        [SerializeField] Material material;


        // === Runtime state ===
        // Nodes simulate in FULL 3D local space (Vector3). Topology stays 2D grid (c,r)
        // but each node is free to move in local X, Y, and Z — so slime passing through
        // can bulge the cloth out of its initial plane, and the mesh reacts volumetrically.
        Vector3[] localPos;
        Vector3[] localPrevPos;
        bool[] pinned;
        Vector3[] localRestPos;

        int nodeColCount, nodeRowCount;
        float localOriginX;

        Mesh mesh;
        Vector3[] meshVertices;
        int[] meshTriangles;
        Vector2[] meshUVs;
        Color[] meshColors;
        int frontVertOffset;
        int backVertOffset;
        int allocatedVertCount = -1;        // guards against reallocation when topology unchanged

        // All slime candidates in the scene (re-scanned when any tracked slime goes null).
        readonly List<SlimeController> trackedSlimes = new List<SlimeController>(4);
        float slimeRescanTimer;

        // Per-slime drag session state — keyed by SlimeController.
        struct DragSession { public bool insideLastTick; public float entrySpeed; public float removed; }
        readonly Dictionary<SlimeController, DragSession> dragSessions = new Dictionary<SlimeController, DragSession>();

        // Collider query buffer (shared across the ground loop).
        readonly Collider2D[] overlapBuf = new Collider2D[4];
        ContactFilter2D groundContactFilter;

        // Cached for offscreen LOD — avoid per-tick GetComponent + per-tick Plane[6] alloc.
        MeshRenderer cachedRenderer;
        readonly Plane[] frustumPlanes = new Plane[6];


        // Per-substep snapshot of each slime in cloth local frame. Populated once per substep,
        // then used by the per-iteration collision resolver inside the constraint loop so
        // push-outs survive subsequent distance-constraint passes.
        struct SlimeContact
        {
            public Vector3 localCenter;      // slime local XYZ at substep start
            public float radius;             // slime collision radius (SlimeController.SlimeRadius)
            public Vector3 localVel;         // 3D local velocity (for trailing transfer)
            public Vector3 fallbackDir;      // local gravity-up 3D normalized, used when node sits exactly on slime center
        }
        readonly List<SlimeContact> slimeContacts = new List<SlimeContact>(4);

        // Warn-once flags for author mistakes we can detect but not fix.
        bool warnedNonUniformScale;


        // === Unity lifecycle ===

        void Awake()
        {
            cachedRenderer = GetComponent<MeshRenderer>();
            ResolveDefaultLayerMask();
            Rebuild();
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Rebuild();
        }

        void OnDestroy()
        {
            if (mesh != null)
            {
                if (Application.isPlaying) Destroy(mesh);
                else DestroyImmediate(mesh);
            }
        }

        void ResolveDefaultLayerMask()
        {
            if (groundLayerMask.value != 0) return;
            int envLayer = LayerMask.NameToLayer("Environment");
            if (envLayer >= 0) groundLayerMask = 1 << envLayer;
        }

        [ContextMenu("Rebuild Cloth")]
        public void Rebuild()
        {
            nodeColCount = Mathf.Max(2, gridColumns);
            nodeRowCount = Mathf.Max(2, gridRows);

            int nodeCount = nodeColCount * nodeRowCount;
            if (localPos == null || localPos.Length != nodeCount)
            {
                localPos = new Vector3[nodeCount];
                localPrevPos = new Vector3[nodeCount];
                pinned = new bool[nodeCount];
                localRestPos = new Vector3[nodeCount];
            }

            float width = (nodeColCount - 1) * cellSize;
            localOriginX = -width * 0.5f;

            // Rest: flat grid in the cloth's local XY plane at local Z = 0. Sim is 3D,
            // so nodes can drift off this plane under forces / collision.
            for (int r = 0; r < nodeRowCount; r++)
            {
                for (int c = 0; c < nodeColCount; c++)
                {
                    int i = r * nodeColCount + c;
                    Vector3 rest = new Vector3(localOriginX + c * cellSize, -r * cellSize, 0f);
                    localPos[i] = rest;
                    localPrevPos[i] = rest;
                    localRestPos[i] = rest;
                    pinned[i] = IsPinnedForPreset(c, r);
                }
            }

            BuildMesh();
            SyncMesh();

            var mr = GetComponent<MeshRenderer>();
            if (mr != null && material != null && mr.sharedMaterial != material)
                mr.sharedMaterial = material;
        }

        bool IsPinnedForPreset(int c, int r)
        {
            switch (pinPreset)
            {
                case PinPreset.TopRow: return r == 0;
                // Defensive: unrecognized enum value falls back to curtain (top-row) pinning
                // rather than silently pinning nothing.
                default: return r == 0;
            }
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (localPos == null) return;

            WarnIfNonUniformScale();

            if (disableOffscreen && IsOffscreen()) return;

            RefreshTrackedSlimes();

            int substeps = Mathf.Clamp(simulationSubsteps, 1, 8);
            float subDt = Time.fixedDeltaTime / substeps;
            for (int s = 0; s < substeps; s++)
                StepOnce(subDt);

            ApplyDragToSlimes();
            SyncMesh();
        }

        // === Verlet step ===

        void StepOnce(float dt)
        {
            float dt2 = dt * dt;

            // All forces projected to local 3D (not just XY) so nodes can move out of
            // the initial cloth plane — the core requirement for 3D cloth behavior.
            Vector3 gravWorld = (Vector3)Physics2D.gravity * nodeGravityScale;
            Vector3 windWorldBase = windAccelerationWorld;
            Vector3 gravLocal = transform.InverseTransformDirection(gravWorld);
            Vector3 windLocalBase = transform.InverseTransformDirection(windWorldBase);

            float t = Time.time;
            float turb = windTurbulence;
            float flutter = windFlutter;
            float freq = windFrequency;
            float windMag = windLocalBase.magnitude;

            // (1) Verlet integration in full 3D. Wind turbulence uses TWO per-node Perlin
            // samples: one scales the main wind magnitude (gives gusty variation), the other
            // drives a PERPENDICULAR (local-Z, out-of-plane) force. The perpendicular kick is
            // what creates wrinkles — without it, all nodes get parallel force and the cloth
            // translates rigidly instead of rippling.
            for (int i = 0; i < localPos.Length; i++)
            {
                if (pinned[i])
                {
                    localPos[i] = localRestPos[i];
                    localPrevPos[i] = localRestPos[i];
                    continue;
                }

                Vector3 accel = gravLocal;

                if (windMag > 1e-5f)
                {
                    float magNoise = turb > 0f
                        ? (Mathf.PerlinNoise(t * freq + i * 0.17f, t * freq * 0.73f) * 2f - 1f) * turb
                        : 0f;
                    accel += windLocalBase * (1f + magNoise);

                    if (flutter > 0f)
                    {
                        // Decorrelated noise channel for Z (cloth-plane-perpendicular) flutter.
                        float zNoise = Mathf.PerlinNoise(t * freq * 1.3f + i * 0.31f,
                                                         t * freq * 0.91f + i * 0.53f) * 2f - 1f;
                        accel.z += zNoise * windMag * flutter;
                    }
                }

                Vector3 vel = (localPos[i] - localPrevPos[i]) * damping;
                Vector3 next = localPos[i] + vel + accel * dt2;
                localPrevPos[i] = localPos[i];
                localPos[i] = next;
            }

            // (2) Snapshot slime contacts for this substep — used by per-iteration collision
            // resolver so push-outs stay enforced through all constraint iterations.
            BuildSlimeContacts();

            // (3) Constraint iteration — structural + shear + bend + collisions (slime + ground).
            // Collisions must run INSIDE this loop; otherwise the distance constraints of the
            // next iteration would pull collision-corrected nodes back through the slime sphere.
            int iters = Mathf.Max(1, constraintIterations);
            float structScale = 1f - Mathf.Clamp01(structuralSoftness);
            float shearScale = 1f - Mathf.Clamp01(shearSoftness);
            float bendScale = 1f - Mathf.Clamp01(bendSoftness);
            float structRest = cellSize;
            float shearRest = cellSize * Mathf.Sqrt(2f);
            float bendRest = cellSize * 2f;

            for (int it = 0; it < iters; it++)
            {
                for (int r = 0; r < nodeRowCount; r++)
                    for (int c = 0; c < nodeColCount - 1; c++)
                        SolveDistance(Idx(c, r), Idx(c + 1, r), structRest, structScale);
                for (int r = 0; r < nodeRowCount - 1; r++)
                    for (int c = 0; c < nodeColCount; c++)
                        SolveDistance(Idx(c, r), Idx(c, r + 1), structRest, structScale);
                for (int r = 0; r < nodeRowCount - 1; r++)
                    for (int c = 0; c < nodeColCount - 1; c++)
                    {
                        SolveDistance(Idx(c, r), Idx(c + 1, r + 1), shearRest, shearScale);
                        SolveDistance(Idx(c + 1, r), Idx(c, r + 1), shearRest, shearScale);
                    }
                for (int r = 0; r < nodeRowCount; r++)
                    for (int c = 0; c < nodeColCount - 2; c++)
                        SolveDistance(Idx(c, r), Idx(c + 2, r), bendRest, bendScale);
                for (int r = 0; r < nodeRowCount - 2; r++)
                    for (int c = 0; c < nodeColCount; c++)
                        SolveDistance(Idx(c, r), Idx(c, r + 2), bendRest, bendScale);

                // Collision passes — treated as hard position constraints. Applied LAST each
                // iteration so they're the final word on node placement at iteration boundary,
                // but earlier constraints still get multiple passes to distribute the push-out
                // across the mesh (neighbors stretch to accommodate, giving the "wrap" look).
                // Edge resolver runs after node resolver so segments between coarse-grid nodes
                // can't chord through the slime sphere when no node sits inside it.
                ResolveSlimeContactsOnce();
                ResolveSlimeEdgeContactsOnce();
                ResolveGroundContactsOnce();

                for (int i = 0; i < localPos.Length; i++)
                    if (pinned[i]) localPos[i] = localRestPos[i];
            }
        }

        // Build the per-substep slime snapshot in cloth local frame. Uses current slime.Position
        // so substep-to-substep resampling remains correct at high slime speeds.
        void BuildSlimeContacts()
        {
            slimeContacts.Clear();
            for (int k = 0; k < trackedSlimes.Count; k++)
            {
                var slime = trackedSlimes[k];
                if (slime == null) continue;

                float radius = GetEffectiveSlimeRadius(slime);
                if (radius <= 1e-4f) continue;

                // 2.5D slime is treated as a 3D sphere at world z=0 with the authored radius.
                Vector3 slimeWorld = new Vector3(slime.Position.x, slime.Position.y, 0f);
                Vector3 localCenter = transform.InverseTransformPoint(slimeWorld);

                Vector3 localVel = Vector3.zero;
                if (slimeToNodeVelocityTransfer > 0f)
                {
                    Vector3 velWorld = new Vector3(slime.Velocity.x, slime.Velocity.y, 0f);
                    localVel = transform.InverseTransformDirection(velWorld);
                }

                Vector3 upWorld = new Vector3(slime.GravityUp.x, slime.GravityUp.y, 0f);
                Vector3 upLocal = transform.InverseTransformDirection(upWorld);
                if (upLocal.sqrMagnitude < 1e-6f) upLocal = Vector3.up;
                else upLocal.Normalize();

                slimeContacts.Add(new SlimeContact
                {
                    localCenter = localCenter,
                    radius = radius,
                    localVel = localVel,
                    fallbackDir = upLocal,
                });
            }
        }

        // Per-iteration 3D slime sphere resolver. For each node inside any slime's sphere,
        // push it out radially in FULL 3D. Applied inside the constraint loop so subsequent
        // iterations can't pull nodes back through the slime.
        void ResolveSlimeContactsOnce()
        {
            if (slimeContacts.Count == 0) return;
            float fdt = Time.fixedDeltaTime;
            float transferScale = slimeToNodeVelocityTransfer;

            for (int k = 0; k < slimeContacts.Count; k++)
            {
                SlimeContact sc = slimeContacts[k];
                float target = sc.radius;
                float targetSq = target * target;

                for (int i = 0; i < localPos.Length; i++)
                {
                    if (pinned[i]) continue;
                    Vector3 delta = localPos[i] - sc.localCenter;
                    float distSq = delta.sqrMagnitude;
                    if (distSq >= targetSq) continue;

                    float dist = Mathf.Sqrt(distSq);
                    Vector3 newPos;
                    if (dist < 1e-5f)
                        newPos = sc.localCenter + sc.fallbackDir * target;
                    else
                        newPos = sc.localCenter + delta * (target / dist);

                    // Trailing: shift prevPos by slime velocity so Verlet inertia drags the
                    // node along with the slime next substep.
                    if (transferScale > 0f)
                        localPrevPos[i] = newPos - sc.localVel * (fdt * transferScale);

                    localPos[i] = newPos;
                }
            }
        }

        // Per-iteration edge-vs-sphere resolver. The node resolver above only corrects nodes
        // strictly INSIDE the sphere — when the grid is coarse relative to the slime (e.g. 0.25
        // cell vs 0.4 radius, only 2-3 nodes inside) the segments between adjacent nodes can
        // chord straight through the sphere even though every endpoint is on/outside it. This
        // pass closes that gap by checking each structural and shear edge's closest point to
        // the slime center, and pushing the segment out if it dips inside the sphere.
        // Bend (skip-1) edges are intentionally excluded — they aren't visible mesh boundaries.
        void ResolveSlimeEdgeContactsOnce()
        {
            if (slimeContacts.Count == 0) return;

            for (int k = 0; k < slimeContacts.Count; k++)
            {
                SlimeContact sc = slimeContacts[k];
                float radius = sc.radius;
                float radiusSq = radius * radius;

                for (int r = 0; r < nodeRowCount; r++)
                    for (int c = 0; c < nodeColCount - 1; c++)
                        PushEdgeOutOfSphere(Idx(c, r), Idx(c + 1, r), sc, radius, radiusSq);

                for (int r = 0; r < nodeRowCount - 1; r++)
                    for (int c = 0; c < nodeColCount; c++)
                        PushEdgeOutOfSphere(Idx(c, r), Idx(c, r + 1), sc, radius, radiusSq);

                for (int r = 0; r < nodeRowCount - 1; r++)
                    for (int c = 0; c < nodeColCount - 1; c++)
                    {
                        PushEdgeOutOfSphere(Idx(c, r), Idx(c + 1, r + 1), sc, radius, radiusSq);
                        PushEdgeOutOfSphere(Idx(c + 1, r), Idx(c, r + 1), sc, radius, radiusSq);
                    }
            }
        }

        void PushEdgeOutOfSphere(int ia, int ib, in SlimeContact sc, float radius, float radiusSq)
        {
            bool pa = pinned[ia];
            bool pb = pinned[ib];
            if (pa && pb) return;

            Vector3 a = localPos[ia];
            Vector3 b = localPos[ib];
            Vector3 ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-10f) return;

            float t = Mathf.Clamp01(Vector3.Dot(sc.localCenter - a, ab) / abLenSq);
            Vector3 closest = a + ab * t;
            Vector3 outward = closest - sc.localCenter;
            float distSq = outward.sqrMagnitude;
            if (distSq >= radiusSq) return;

            float dist = Mathf.Sqrt(distSq);
            Vector3 dir = dist < 1e-5f ? sc.fallbackDir : outward / dist;
            Vector3 disp = dir * (radius - dist);

            if (pa)
            {
                localPos[ib] += disp;
            }
            else if (pb)
            {
                localPos[ia] += disp;
            }
            else
            {
                // Distribute by (1-t, t) and rescale by 1/((1-t)²+t²) so the closest point at
                // parameter t moves by the full disp this pass (without the rescale a midpoint
                // contact would only move 0.5*disp and need extra iterations to escape).
                float w = (1f - t) * (1f - t) + t * t;
                float scale = w > 1e-5f ? 1f / w : 1f;
                localPos[ia] += disp * ((1f - t) * scale);
                localPos[ib] += disp * (t * scale);
            }
        }

        // Per-iteration ground resolver. Same rationale as slime: inside the loop so
        // distance constraints can't pull nodes back through the floor.
        void ResolveGroundContactsOnce()
        {
            if (groundLayerMask.value == 0) return;

            // Lazy-init the contact filter once; layerMask kept in sync each tick.
            groundContactFilter.useTriggers = false;
            groundContactFilter.useLayerMask = true;
            groundContactFilter.layerMask = groundLayerMask;

            for (int i = 0; i < localPos.Length; i++)
            {
                if (pinned[i]) continue;

                // Node's true 3D local position mapped to world. Collider2D is infinite in
                // world Z so the XY query is valid regardless of the node's world Z drift.
                Vector3 worldNode3D = transform.TransformPoint(localPos[i]);
                Vector2 worldNode2D = new Vector2(worldNode3D.x, worldNode3D.y);

                int hitCount = Physics2D.OverlapCircle(worldNode2D, nodeCollisionRadius, groundContactFilter, overlapBuf);
                if (hitCount == 0) continue;

                for (int h = 0; h < hitCount; h++)
                {
                    var col = overlapBuf[h];
                    if (col == null) continue;

                    Vector2 closest = col.ClosestPoint(worldNode2D);
                    Vector2 out2D = worldNode2D - closest;
                    float d = out2D.magnitude;
                    if (d >= nodeCollisionRadius) continue;

                    if (d < 1e-5f) out2D = Vector2.up;
                    else out2D /= d;
                    worldNode2D = closest + out2D * nodeCollisionRadius;
                }

                // Preserve the node's world Z (= whatever the 3D sim produced); only update XY.
                Vector3 newWorld = new Vector3(worldNode2D.x, worldNode2D.y, worldNode3D.z);
                localPos[i] = transform.InverseTransformPoint(newWorld);
            }
        }

        int Idx(int c, int r) => r * nodeColCount + c;

        void SolveDistance(int a, int b, float rest, float projectionScale)
        {
            Vector3 delta = localPos[b] - localPos[a];
            float len = delta.magnitude;
            if (len < 1e-5f) return;

            float diff = (len - rest) / len * projectionScale;
            Vector3 offset = delta * (0.5f * diff);

            bool pa = pinned[a];
            bool pb = pinned[b];
            if (pa && pb) return;
            if (pa) localPos[b] -= offset * 2f;
            else if (pb) localPos[a] += offset * 2f;
            else
            {
                localPos[a] += offset;
                localPos[b] -= offset;
            }
        }

        // === Slime tracking ===

        void RefreshTrackedSlimes()
        {
            // Re-scan on first tick, when any tracked slime went null, or every ~0.5s
            // for respawn-race coverage (see audit limitation #4).
            bool needsRescan = trackedSlimes.Count == 0;
            if (!needsRescan)
            {
                for (int i = 0; i < trackedSlimes.Count; i++)
                    if (trackedSlimes[i] == null) { needsRescan = true; break; }
            }

            slimeRescanTimer -= Time.fixedDeltaTime;
            if (slimeRescanTimer <= 0f)
            {
                needsRescan = true;
                slimeRescanTimer = 0.5f;
            }

            if (!needsRescan) return;

            trackedSlimes.Clear();
#if UNITY_2023_1_OR_NEWER
            trackedSlimes.AddRange(FindObjectsByType<SlimeController>(FindObjectsSortMode.None));
#else
            trackedSlimes.AddRange(FindObjectsOfType<SlimeController>());
#endif

            // Prune drag sessions for slimes that no longer exist.
            var stale = new List<SlimeController>();
            foreach (var kv in dragSessions)
                if (kv.Key == null || !trackedSlimes.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var s in stale) dragSessions.Remove(s);
        }

        float GetEffectiveSlimeRadius(SlimeController slime)
        {
            if (slimeRadiusOverride > 0f) return slimeRadiusOverride;
            return slime != null ? slime.SlimeRadius : 0.4f;
        }

        // (Slime + ground collision resolvers moved inline into StepOnce's constraint loop —
        //  see ResolveSlimeContactsOnce / ResolveGroundContactsOnce above.)

        // === Drag ===

        void ApplyDragToSlimes()
        {
            for (int k = 0; k < trackedSlimes.Count; k++)
            {
                var slime = trackedSlimes[k];
                if (slime == null) continue;
                ApplyDragToSlime(slime);
            }
        }

        void ApplyDragToSlime(SlimeController slime)
        {
            float radius = GetEffectiveSlimeRadius(slime);
            Vector3 slimeWorld3D = new Vector3(slime.Position.x, slime.Position.y, 0f);
            Vector3 slimeLocal = transform.InverseTransformPoint(slimeWorld3D);

            float gateZ = clothThickness * 0.5f + radius + thicknessGateMargin;
            float gridHalfW = (nodeColCount - 1) * cellSize * 0.5f;
            float gridH = (nodeRowCount - 1) * cellSize;

            bool insideNow =
                Mathf.Abs(slimeLocal.z) <= gateZ &&
                slimeLocal.x >= -gridHalfW - radius &&
                slimeLocal.x <= gridHalfW + radius &&
                slimeLocal.y >= -gridH - radius &&
                slimeLocal.y <= radius;

            dragSessions.TryGetValue(slime, out DragSession ses);

            if (insideNow && !ses.insideLastTick)
            {
                ses.entrySpeed = slime.Velocity.magnitude;
                ses.removed = 0f;
            }
            ses.insideLastTick = insideNow;
            dragSessions[slime] = ses;

            if (!insideNow) return;

            var state = slime.CurrentState;
            if (state == SlimeController.SlimeState.Charging ||
                state == SlimeController.SlimeState.Attached) return;

            float cx = Mathf.InverseLerp(gridHalfW, 0f, Mathf.Abs(slimeLocal.x));
            float cy = Mathf.InverseLerp(gridH, 0f, Mathf.Abs(slimeLocal.y + gridH * 0.5f));
            float coverageRatio = Mathf.Clamp01(cx * cy);
            if (coverageRatio <= 0f) return;

            float dt = Time.fixedDeltaTime;
            Vector3 velWorld3 = new Vector3(slime.Velocity.x, slime.Velocity.y, 0f);
            Vector3 velLocal3 = transform.InverseTransformDirection(velWorld3);
            Vector2 velLocal = new Vector2(velLocal3.x, velLocal3.y);

            Vector2 dvLocal = -velLocal * clothDragCoefficient * coverageRatio * dt;

            float velMag = velLocal.magnitude;
            if (velMag > 1e-5f)
            {
                float perStepMax = velMag * maxDragPerSecond * dt;
                if (dvLocal.magnitude > perStepMax)
                    dvLocal = dvLocal.normalized * perStepMax;
            }

            if (ses.entrySpeed > 0f)
            {
                float budget = ses.entrySpeed * maxAccumulatedDrag;
                float remaining = budget - ses.removed;
                if (remaining <= 0f) return;
                float add = dvLocal.magnitude;
                if (add > remaining)
                {
                    dvLocal = dvLocal.normalized * remaining;
                    add = remaining;
                }
                ses.removed += add;
                dragSessions[slime] = ses;
            }

            Vector3 dvLocal3 = new Vector3(dvLocal.x, dvLocal.y, 0f);
            Vector3 dvWorld3 = transform.TransformDirection(dvLocal3);
            Vector2 dvWorld = new Vector2(dvWorld3.x, dvWorld3.y);
            slime.AddExternalVelocity(dvWorld);
        }

        // === Offscreen LOD ===

        bool IsOffscreen()
        {
            if (cullingCamera == null) cullingCamera = UnityEngine.Camera.main;
            if (cullingCamera == null || mesh == null) return false;
            if (cachedRenderer == null) cachedRenderer = GetComponent<MeshRenderer>();
            if (cachedRenderer == null) return false;

            GeometryUtility.CalculateFrustumPlanes(cullingCamera, frustumPlanes);
            return !GeometryUtility.TestPlanesAABB(frustumPlanes, cachedRenderer.bounds);
        }

        // === Non-uniform scale warning ===

        void WarnIfNonUniformScale()
        {
            if (warnedNonUniformScale) return;
            Vector3 ls = transform.lossyScale;
            if (Mathf.Abs(ls.x - 1f) > 1e-3f || Mathf.Abs(ls.y - 1f) > 1e-3f)
            {
                Debug.LogWarning(
                    $"[ClothField:{name}] lossyScale ({ls}) != 1. Constraint rest lengths assume " +
                    "unscaled local XY — scale the cloth via cellSize / gridColumns / gridRows instead.", this);
                warnedNonUniformScale = true;
            }
        }

        // === Slime found? (for other systems / editor tools) ===

        public bool HasTrackedSlime => trackedSlimes.Count > 0 && trackedSlimes[0] != null;

        // === Mesh ===

        void BuildMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (mesh == null)
            {
                mesh = new Mesh { name = "ClothMesh" };
                mesh.MarkDynamic();
                mf.sharedMesh = mesh;
            }
            else if (mf.sharedMesh != mesh)
            {
                mf.sharedMesh = mesh;
            }

            int nodeCount = nodeColCount * nodeRowCount;
            int cellCols = nodeColCount - 1;
            int cellRows = nodeRowCount - 1;
            int vertCount = nodeCount * 2;

            // Fix (audit #1): only reallocate arrays when topology changes.
            if (allocatedVertCount != vertCount)
            {
                frontVertOffset = 0;
                backVertOffset = nodeCount;

                meshVertices = new Vector3[vertCount];
                meshUVs = new Vector2[vertCount];
                meshColors = new Color[vertCount];

                int frontTris = cellCols * cellRows * 2;
                int backTris = frontTris;
                int skirtTris = (cellCols * 2 + cellRows * 2) * 2;
                int triCount = (frontTris + backTris + skirtTris) * 3;
                meshTriangles = new int[triCount];

                allocatedVertCount = vertCount;
            }

            // UVs and vertex colors — cheap, rewrite every Rebuild (pin state may have changed).
            for (int r = 0; r < nodeRowCount; r++)
            {
                for (int c = 0; c < nodeColCount; c++)
                {
                    int i = r * nodeColCount + c;
                    Vector2 uv = new Vector2(c / (float)(nodeColCount - 1), 1f - r / (float)(nodeRowCount - 1));
                    meshUVs[frontVertOffset + i] = uv;
                    meshUVs[backVertOffset + i] = uv;

                    float maskR = pinned[i] ? 0f : 1f;
                    Color col = new Color(maskR, 1f, 1f, 1f);
                    meshColors[frontVertOffset + i] = col;
                    meshColors[backVertOffset + i] = col;
                }
            }

            int t = 0;
            for (int r = 0; r < cellRows; r++)
            {
                for (int c = 0; c < cellCols; c++)
                {
                    int a = frontVertOffset + Idx(c, r);
                    int b = frontVertOffset + Idx(c + 1, r);
                    int d = frontVertOffset + Idx(c, r + 1);
                    int e = frontVertOffset + Idx(c + 1, r + 1);
                    meshTriangles[t++] = a; meshTriangles[t++] = e; meshTriangles[t++] = b;
                    meshTriangles[t++] = a; meshTriangles[t++] = d; meshTriangles[t++] = e;
                }
            }
            for (int r = 0; r < cellRows; r++)
            {
                for (int c = 0; c < cellCols; c++)
                {
                    int a = backVertOffset + Idx(c, r);
                    int b = backVertOffset + Idx(c + 1, r);
                    int d = backVertOffset + Idx(c, r + 1);
                    int e = backVertOffset + Idx(c + 1, r + 1);
                    meshTriangles[t++] = a; meshTriangles[t++] = b; meshTriangles[t++] = e;
                    meshTriangles[t++] = a; meshTriangles[t++] = e; meshTriangles[t++] = d;
                }
            }
            for (int c = 0; c < cellCols; c++)
            {
                int f0 = frontVertOffset + Idx(c, 0);
                int f1 = frontVertOffset + Idx(c + 1, 0);
                int b0 = backVertOffset + Idx(c, 0);
                int b1 = backVertOffset + Idx(c + 1, 0);
                meshTriangles[t++] = f0; meshTriangles[t++] = b1; meshTriangles[t++] = b0;
                meshTriangles[t++] = f0; meshTriangles[t++] = f1; meshTriangles[t++] = b1;
            }
            {
                int rBot = nodeRowCount - 1;
                for (int c = 0; c < cellCols; c++)
                {
                    int f0 = frontVertOffset + Idx(c, rBot);
                    int f1 = frontVertOffset + Idx(c + 1, rBot);
                    int b0 = backVertOffset + Idx(c, rBot);
                    int b1 = backVertOffset + Idx(c + 1, rBot);
                    meshTriangles[t++] = f0; meshTriangles[t++] = b0; meshTriangles[t++] = b1;
                    meshTriangles[t++] = f0; meshTriangles[t++] = b1; meshTriangles[t++] = f1;
                }
            }
            for (int r = 0; r < cellRows; r++)
            {
                int f0 = frontVertOffset + Idx(0, r);
                int f1 = frontVertOffset + Idx(0, r + 1);
                int b0 = backVertOffset + Idx(0, r);
                int b1 = backVertOffset + Idx(0, r + 1);
                meshTriangles[t++] = f0; meshTriangles[t++] = b0; meshTriangles[t++] = b1;
                meshTriangles[t++] = f0; meshTriangles[t++] = b1; meshTriangles[t++] = f1;
            }
            {
                int cRt = nodeColCount - 1;
                for (int r = 0; r < cellRows; r++)
                {
                    int f0 = frontVertOffset + Idx(cRt, r);
                    int f1 = frontVertOffset + Idx(cRt, r + 1);
                    int b0 = backVertOffset + Idx(cRt, r);
                    int b1 = backVertOffset + Idx(cRt, r + 1);
                    meshTriangles[t++] = f0; meshTriangles[t++] = b1; meshTriangles[t++] = b0;
                    meshTriangles[t++] = f0; meshTriangles[t++] = f1; meshTriangles[t++] = b1;
                }
            }

            mesh.Clear();
            mesh.vertices = meshVertices;
            mesh.uv = meshUVs;
            mesh.colors = meshColors;
            mesh.triangles = meshTriangles;
            mesh.RecalculateBounds();
        }

        void SyncMesh()
        {
            if (mesh == null || meshVertices == null) return;

            int nodeCount = localPos.Length;
            float half = clothThickness * 0.5f;

            // Nodes now have full 3D local positions. Thickness offsets the front/back mesh
            // layers around the node's 3D position, so cloth displacement in local Z carries
            // into the mesh naturally.
            for (int i = 0; i < nodeCount; i++)
            {
                Vector3 p = localPos[i];
                meshVertices[frontVertOffset + i] = new Vector3(p.x, p.y, p.z - half);
                meshVertices[backVertOffset + i]  = new Vector3(p.x, p.y, p.z + half);
            }

            mesh.vertices = meshVertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // === Gizmos ===

        void OnDrawGizmosSelected()
        {
            if (localPos == null) return;

            for (int i = 0; i < localPos.Length; i++)
            {
                Vector3 w = transform.TransformPoint(localPos[i]);
                Gizmos.color = pinned[i] ? Color.red : new Color(1f, 0.9f, 0.2f, 0.8f);
                Gizmos.DrawSphere(w, 0.04f);
            }

            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.5f);
            float halfW = (nodeColCount - 1) * cellSize * 0.5f;
            float h = (nodeRowCount - 1) * cellSize;
            float tZ = clothThickness * 0.5f;
            Vector3[] corners = new[]
            {
                new Vector3(-halfW, 0f, -tZ), new Vector3( halfW, 0f, -tZ),
                new Vector3( halfW, -h, -tZ), new Vector3(-halfW, -h, -tZ),
                new Vector3(-halfW, 0f,  tZ), new Vector3( halfW, 0f,  tZ),
                new Vector3( halfW, -h,  tZ), new Vector3(-halfW, -h,  tZ),
            };
            for (int i = 0; i < corners.Length; i++) corners[i] = transform.TransformPoint(corners[i]);
            Gizmos.DrawLine(corners[0], corners[1]); Gizmos.DrawLine(corners[1], corners[2]);
            Gizmos.DrawLine(corners[2], corners[3]); Gizmos.DrawLine(corners[3], corners[0]);
            Gizmos.DrawLine(corners[4], corners[5]); Gizmos.DrawLine(corners[5], corners[6]);
            Gizmos.DrawLine(corners[6], corners[7]); Gizmos.DrawLine(corners[7], corners[4]);
            for (int i = 0; i < 4; i++) Gizmos.DrawLine(corners[i], corners[i + 4]);

            if (Application.isPlaying)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.3f);
                for (int k = 0; k < trackedSlimes.Count; k++)
                {
                    var slime = trackedSlimes[k];
                    if (slime == null) continue;
                    Gizmos.DrawWireSphere(slime.Position, GetEffectiveSlimeRadius(slime));
                }
            }
        }
    }
}
