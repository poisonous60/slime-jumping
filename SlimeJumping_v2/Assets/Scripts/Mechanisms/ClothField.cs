using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    // Passable 3D-oriented cloth (#16). Verlet mass-spring grid: topology is a 2D grid, nodes move
    // in full 3D local space, so transform.rotation can place the plane anywhere in world space.
    // Slime passes through (no collider) — nodes push out of its sphere; drag on the slime is capped
    // per step, clamped per traversal, and zero during Attached/Charging.
    // Ground queries stay in world XY: Collider2D is infinite in world Z, so rotation is harmless.
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ClothField : MonoBehaviour
    {
        public enum PinPreset { TopRow, None }

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

        struct DragSession { public bool insideLastTick; public float entrySpeed; public float removed; }
        readonly Dictionary<SlimeController, DragSession> dragSessions = new Dictionary<SlimeController, DragSession>();

        readonly Collider2D[] overlapBuf = new Collider2D[4];
        ContactFilter2D groundContactFilter;

        // Cached for offscreen LOD — avoid per-tick GetComponent + per-tick Plane[6] alloc.
        MeshRenderer cachedRenderer;
        readonly Plane[] frustumPlanes = new Plane[6];


        // Per-substep slime snapshot in cloth local frame, reused by the per-iteration collision
        // resolvers so push-outs survive the later distance-constraint passes.
        struct SlimeContact
        {
            public Vector3 localCenter;      // slime local XYZ at substep start
            public float radius;
            public Vector3 localVel;         // 3D local velocity (for trailing transfer)
            public Vector3 fallbackDir;      // used when a node sits exactly on the slime center
        }
        readonly List<SlimeContact> slimeContacts = new List<SlimeContact>(4);

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

            // Rest: flat grid at local Z = 0; the 3D sim lets nodes drift off this plane.
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
                case PinPreset.None: return false;
                case PinPreset.TopRow: return r == 0;
                // Unknown enum → curtain pinning, not a silently unpinned cloth.
                default: return r == 0;
            }
        }

        // === External driver API (e.g. ClothWrapAnimator) ===
        // StepOnce re-clamps pinned nodes to localRestPos every iteration, so a driver calls this
        // once per FixedUpdate — from a LOWER DefaultExecutionOrder than this component's -100.
        public void SetNodeTarget(int col, int row, Vector3 worldPos, bool pin)
        {
            if (localPos == null) return;
            if ((uint)col >= (uint)nodeColCount || (uint)row >= (uint)nodeRowCount) return;
            int i = row * nodeColCount + col;
            pinned[i] = pin;
            if (pin) localRestPos[i] = transform.InverseTransformPoint(worldPos);
        }

        public int GridColumns => nodeColCount;
        public int GridRows => nodeRowCount;
        public float CellSize => cellSize;

        // Local-space rest position Rebuild() lays out for (col, row) — for drivers pinning nodes
        // at their default rest.
        public Vector3 GetNodeRestLocalPos(int col, int row)
        {
            if ((uint)col >= (uint)nodeColCount || (uint)row >= (uint)nodeRowCount) return Vector3.zero;
            float width = (nodeColCount - 1) * cellSize;
            float originX = -width * 0.5f;
            return new Vector3(originX + col * cellSize, -row * cellSize, 0f);
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

            Vector3 gravWorld = (Vector3)Physics2D.gravity * nodeGravityScale;
            Vector3 windWorldBase = windAccelerationWorld;
            Vector3 gravLocal = transform.InverseTransformDirection(gravWorld);
            Vector3 windLocalBase = transform.InverseTransformDirection(windWorldBase);

            float t = Time.time;
            float turb = windTurbulence;
            float flutter = windFlutter;
            float freq = windFrequency;
            float windMag = windLocalBase.magnitude;

            // (1) Verlet integration in full 3D. The second, PERPENDICULAR (local-Z) noise channel
            // is what creates wrinkles — with parallel force only the cloth translates rigidly.
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
                        // Decorrelated noise channel for the out-of-plane (local-Z) flutter.
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

            // (2) Snapshot slime contacts once per substep for the resolvers below.
            BuildSlimeContacts();

            // (3) Constraint iteration. Collisions must run INSIDE this loop, else the next
            // iteration's distance constraints pull corrected nodes back through the slime sphere.
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

                // Hard position constraints, applied LAST each iteration so they get the final word
                // while earlier passes still distribute the push-out (the "wrap" look). Edge resolver
                // after the node resolver, else segments chord through with no node inside.
                ResolveSlimeContactsOnce();
                ResolveSlimeEdgeContactsOnce();
                ResolveGroundContactsOnce();

                for (int i = 0; i < localPos.Length; i++)
                    if (pinned[i]) localPos[i] = localRestPos[i];
            }
        }

        // Resampled per substep (not per FixedUpdate) so fast slimes stay correct.
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

        // Pushes every node inside a slime's sphere out radially in full 3D.
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

                    // Shifting prevPos is how Verlet inertia drags the node along next substep.
                    if (transferScale > 0f)
                        localPrevPos[i] = newPos - sc.localVel * (fdt * transferScale);

                    localPos[i] = newPos;
                }
            }
        }

        // The node resolver only fixes nodes strictly INSIDE the sphere; on a coarse grid (0.25 cell
        // vs 0.4 radius) a segment chords through it with both endpoints outside. Structural + shear
        // edges only — bend (skip-1) edges aren't visible mesh boundaries.
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
                // Rescale by 1/((1-t)²+t²) so the closest point moves the full disp this pass;
                // without it a midpoint contact moves only 0.5*disp and needs extra iterations.
                float w = (1f - t) * (1f - t) + t * t;
                float scale = w > 1e-5f ? 1f / w : 1f;
                localPos[ia] += disp * ((1f - t) * scale);
                localPos[ib] += disp * (t * scale);
            }
        }

        // Inside the constraint loop, same reason as the slime resolver.
        void ResolveGroundContactsOnce()
        {
            if (groundLayerMask.value == 0) return;

            groundContactFilter.useTriggers = false;
            groundContactFilter.useLayerMask = true;
            groundContactFilter.layerMask = groundLayerMask;

            for (int i = 0; i < localPos.Length; i++)
            {
                if (pinned[i]) continue;

                // Collider2D is infinite in world Z, so the XY query stays valid however far the
                // node has drifted in Z.
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

                // Keep the node's world Z from the 3D sim; the query only corrected XY.
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
            // Also re-scanned every ~0.5s: a respawn can swap the slime without nulling this one.
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

            // Rewritten every Rebuild even when nothing was reallocated — pin state may have changed.
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
