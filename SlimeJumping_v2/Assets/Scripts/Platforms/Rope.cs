using System;
using UnityEngine;

namespace SlimeJumping.Platforms
{
    public enum RopeEventType { FirstAttach, MaxTension, LaunchRebound }

    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class Rope : MonoBehaviour
    {
        [Header("Anchors")]
        [SerializeField] Transform anchorA;
        [SerializeField] Transform anchorB;

        [Header("Targets")]
        [SerializeField] EdgeCollider2D edge;
        [SerializeField] LineRenderer line;

        [Header("Structure")]
        [SerializeField, Min(3)] int segmentCount = 15;
        [Tooltip("Total rope rest length = anchor distance × this. " +
                 "> 1 = slack rope (sag). < 1 = pre-tensioned elastic.")]
        [SerializeField] float restLengthMul = 1.05f;

        [Header("Solver")]
        [Tooltip("Distance constraint iterations per FixedUpdate. " +
                 "High (8-15) = stiff (telephone). Low (2-3) = soft (elastic).")]
        [SerializeField, Min(1)] int constraintIterations = 12;

        [Tooltip("Velocity retention per frame. " +
                 "vel = (pos - prevPos) * damping; pos' = pos + vel + accel*dt^2")]
        [SerializeField, Range(0f, 1f)] float damping = 0.98f;

        [Tooltip("EdgeCollider2D capsule thickness. 0 = zero-thickness polyline. " +
                 "Raise to ~0.05 if high-speed tunneling test fails.")]
        [SerializeField, Min(0f)] float edgeRadius = 0f;

        [Header("Forces")]
        [SerializeField] float nodeGravity = 0.3f;

        [Tooltip("Tension-only elastic coefficient. " +
                 "Active when segment currentLen > restLen — pulls nodes together. " +
                 "Compression (currentLen < restLen) = 0 (rope slack).")]
        [SerializeField, Min(0f)] float elasticFactor = 0f;

        [Header("Rider Influence")]
        [Tooltip("Rider influence radius in node-spacing multiples. " +
                 "4 ≈ force distributes across 4 nodes each side of contact.")]
        [SerializeField, Min(0.5f)] float riderInfluenceSpacing = 4f;

        [Tooltip("Slime mass × this = acceleration injected into nearby nodes. " +
                 "Raise for visible sag under weight.")]
        [SerializeField] float riderForceMul = 30f;

        [Header("Events — MaxTension (hysteresis)")]
        [SerializeField] float maxTensionEnter = 1.25f;
        [SerializeField] float maxTensionExit = 1.10f;

        [Header("Rebound")]
        [Tooltip("Extra launch speed added per meter of sag at release. " +
                 "Called from SlimeController.Launch. 0 = disabled.")]
        [SerializeField, Min(0f)] float reboundSpeedPerMeter = 4f;

        [Header("Scene Preview")]
        [Tooltip("Synthetic rider mass for the Preview Max Sag context menu.")]
        [SerializeField, Min(0f)] float previewRiderMass = 1f;
        [Tooltip("Fixed steps to simulate for the sag preview (dt = 0.02).")]
        [SerializeField, Min(10)] int previewSteps = 120;

        [Header("Resolution decoupling")]
        [Tooltip("Take every Nth simulation node into the EdgeCollider2D. " +
                 "1 = all nodes. 2 = every other node (recommended). " +
                 "Raises collider spacing without touching simulation density.")]
        [SerializeField, Min(1)] int colliderSubsample = 2;

        [Tooltip("Catmull-Rom samples per simulation segment for the LineRenderer. " +
                 "1 = no interpolation (one vertex per node). " +
                 "3 = three samples per segment, smoother curve at line-render cost only.")]
        [SerializeField, Min(1)] int visualSubdivisions = 1;

        [Header("OnValidate Hint")]
        [Tooltip("Used only by OnValidate density guard. Match SlimeSettings.slimeRadius.")]
        [SerializeField] float slimeRadiusHint = 0.4f;

        public EdgeCollider2D Edge => edge;
        public float ReboundSpeedPerMeter => reboundSpeedPerMeter;
        public event Action<RopeEventType, Vector2> OnRopeEvent;

        Vector2[] pos;
        Vector2[] prevPos;
        bool[] pinned;
        Vector2[] riderAccum;
        Vector2[] edgeLocalPoints;
        Vector3[] lineWorldPoints;
        int[] colliderSourceIndices;
        float segmentRestLen;
        int nodeCount;

        bool maxTensionActive;

        // Preview cache — with-rider deflected shape, drawn as magenta gizmo.
        // Populated by RefreshEditPreview() in edit mode (auto on anchor move)
        // or by the Preview Max Sag context menu.
        Vector2[] previewMaxSagPos;
        float previewMaxSagValue;

#if UNITY_EDITOR
        Vector3 editLastAnchorAPos;
        Vector3 editLastAnchorBPos;
        bool editNeedsRefresh = true;
#endif

        void Awake()
        {
            Rebuild();
        }

        [ContextMenu("Rebuild Rope")]
        public void Rebuild()
        {
            if (anchorA == null || anchorB == null)
            {
                nodeCount = 0;
                return;
            }

            nodeCount = Mathf.Max(3, segmentCount);
            pos = new Vector2[nodeCount];
            prevPos = new Vector2[nodeCount];
            pinned = new bool[nodeCount];
            riderAccum = new Vector2[nodeCount];

            // Collider index map: step by colliderSubsample, always finish on nodeCount-1
            // so the tail pin is never dropped.
            int step = Mathf.Max(1, colliderSubsample);
            int last = nodeCount - 1;
            int count = last / step + 1;                  // 0, step, 2*step, ..., up to last
            bool tailIncluded = (last % step == 0);
            if (!tailIncluded) count++;                   // append explicit tail
            colliderSourceIndices = new int[count];
            for (int k = 0; k < count - (tailIncluded ? 0 : 1); k++)
                colliderSourceIndices[k] = k * step;
            if (!tailIncluded) colliderSourceIndices[count - 1] = last;

            edgeLocalPoints = new Vector2[colliderSourceIndices.Length];

            int subdiv = Mathf.Max(1, visualSubdivisions);
            lineWorldPoints = new Vector3[(nodeCount - 1) * subdiv + 1];

            pinned[0] = true;
            pinned[nodeCount - 1] = true;

            Vector2 a = anchorA.position;
            Vector2 b = anchorB.position;
            float anchorDist = Vector2.Distance(a, b);
            float totalRest = anchorDist * restLengthMul;
            segmentRestLen = totalRest / (nodeCount - 1);

            float sag = Mathf.Max(0f, restLengthMul - 1f) * anchorDist * 0.5f;
            for (int i = 0; i < nodeCount; i++)
            {
                float t = (float)i / (nodeCount - 1);
                Vector2 p = Vector2.Lerp(a, b, t);
                p.y -= Mathf.Sin(t * Mathf.PI) * sag;
                pos[i] = p;
                prevPos[i] = p;
            }

            SyncVisual();

            if (edge != null)
            {
                edge.edgeRadius = edgeRadius;
                edge.points = edgeLocalPoints;
            }
            if (line != null)
            {
                line.useWorldSpace = true;
                line.positionCount = lineWorldPoints.Length;
                line.SetPositions(lineWorldPoints);
            }
        }

        void FixedUpdate()
        {
            // [ExecuteAlways] allows Update to drive an edit-mode preview, but the
            // real rope sim must only run in play mode; otherwise entering play with
            // a sag-preview shape would double-integrate from pre-stretched state.
            if (!Application.isPlaying) return;
            if (nodeCount == 0) return;
            if (anchorA == null || anchorB == null) return;

            StepSimulationOnce(Time.fixedDeltaTime);
            EvaluateMaxTension();
            SyncVisual();

            for (int i = 0; i < nodeCount; i++) riderAccum[i] = Vector2.zero;
        }

#if UNITY_EDITOR
        // Edit-mode only: poll anchor positions; when markers are moved in the scene
        // (or fields are edited via OnValidate) rerun RefreshEditPreview so the
        // LineRenderer + EdgeCollider show the current rest-sag shape and the magenta
        // gizmo shows the deflected-under-rider shape.
        void Update()
        {
            if (Application.isPlaying) return;
            if (anchorA == null || anchorB == null) return;

            bool anchorMoved = anchorA.position != editLastAnchorAPos
                            || anchorB.position != editLastAnchorBPos;
            if (!editNeedsRefresh && !anchorMoved) return;

            editLastAnchorAPos = anchorA.position;
            editLastAnchorBPos = anchorB.position;
            editNeedsRefresh = false;
            RefreshEditPreview();
        }
#endif

        // One Verlet + elastic + constraint pass. Consumes riderAccum but does not
        // clear it (caller's job), so PreviewMaxSag can drive it step-by-step.
        void StepSimulationOnce(float dt)
        {
            float dt2 = dt * dt;
            Vector2 gravStep = Physics2D.gravity * (nodeGravity * dt2);

            // (1)+(2) External forces + Verlet integration.
            for (int i = 0; i < nodeCount; i++)
            {
                if (pinned[i])
                {
                    Vector2 anchor = (i == 0) ? (Vector2)anchorA.position : (Vector2)anchorB.position;
                    pos[i] = anchor;
                    prevPos[i] = anchor;
                    continue;
                }

                Vector2 vel = (pos[i] - prevPos[i]) * damping;
                Vector2 riderStep = riderAccum[i] * (dt2 * riderForceMul);

                Vector2 next = pos[i] + vel + gravStep + riderStep;
                prevPos[i] = pos[i];
                pos[i] = next;
            }

            // (3) Tension-only elastic force.
            if (elasticFactor > 0f)
            {
                float k = elasticFactor * dt2 * 0.5f;
                for (int i = 0; i < nodeCount - 1; i++)
                {
                    Vector2 delta = pos[i + 1] - pos[i];
                    float len = delta.magnitude;
                    if (len <= segmentRestLen || len < 1e-5f) continue;

                    float excess = len - segmentRestLen;
                    Vector2 pull = (delta / len) * (excess * k);

                    if (!pinned[i]) pos[i] += pull;
                    if (!pinned[i + 1]) pos[i + 1] -= pull;
                }
            }

            // (4) Distance constraint (Jakobsen). Pin re-assert inside loop.
            int iters = Mathf.Max(1, constraintIterations);
            for (int it = 0; it < iters; it++)
            {
                for (int i = 0; i < nodeCount - 1; i++)
                {
                    Vector2 delta = pos[i + 1] - pos[i];
                    float len = delta.magnitude;
                    if (len < 1e-5f) continue;

                    float diff = (len - segmentRestLen) / len;
                    Vector2 offset = delta * (0.5f * diff);

                    bool pinA = pinned[i];
                    bool pinB = pinned[i + 1];

                    if (pinA && pinB) continue;
                    if (pinA) pos[i + 1] -= offset * 2f;
                    else if (pinB) pos[i] += offset * 2f;
                    else
                    {
                        pos[i] += offset;
                        pos[i + 1] -= offset;
                    }
                }

                pos[0] = anchorA.position;
                pos[nodeCount - 1] = anchorB.position;
            }
        }

        void SyncVisual()
        {
            // Edge — subsampled (every colliderSubsample-th simulation node, tail always).
            if (edge != null)
            {
                for (int j = 0; j < colliderSourceIndices.Length; j++)
                    edgeLocalPoints[j] = transform.InverseTransformPoint(pos[colliderSourceIndices[j]]);
                edge.points = edgeLocalPoints;
            }

            // Line — Catmull-Rom if subdivisions > 1, else 1:1 copy.
            if (line != null)
            {
                float z = line.transform.position.z;
                int subdiv = Mathf.Max(1, visualSubdivisions);

                if (subdiv == 1)
                {
                    for (int i = 0; i < nodeCount; i++)
                        lineWorldPoints[i] = new Vector3(pos[i].x, pos[i].y, z);
                }
                else
                {
                    int w = 0;
                    int expected = lineWorldPoints.Length;
                    for (int i = 0; i < nodeCount - 1; i++)
                    {
                        // Boundary: endpoint clamp (indices bounded to [0, nodeCount-1]).
                        Vector2 p0 = pos[Mathf.Max(0, i - 1)];
                        Vector2 p1 = pos[i];
                        Vector2 p2 = pos[i + 1];
                        Vector2 p3 = pos[Mathf.Min(nodeCount - 1, i + 2)];
                        for (int s = 0; s < subdiv; s++)
                        {
                            float t = s / (float)subdiv;
                            Vector2 v = CatmullRom(p0, p1, p2, p3, t);
                            lineWorldPoints[w++] = new Vector3(v.x, v.y, z);
                        }
                    }
                    lineWorldPoints[w++] = new Vector3(pos[nodeCount - 1].x, pos[nodeCount - 1].y, z);
                    Debug.Assert(w == expected, $"[Rope] lineWorldPoints write mismatch: {w} vs {expected}");
                }
                line.SetPositions(lineWorldPoints);
            }
        }

        // Catmull-Rom spline segment between p1 and p2, with p0/p3 as tangent influences.
        // Endpoint-clamp boundary (p0 = p1 or p3 = p2) keeps ends overshoot-free.
        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        void EvaluateMaxTension()
        {
            float maxRatio = 0f;
            for (int i = 0; i < nodeCount - 1; i++)
            {
                float len = Vector2.Distance(pos[i], pos[i + 1]);
                float r = len / segmentRestLen;
                if (r > maxRatio) maxRatio = r;
            }

            if (!maxTensionActive && maxRatio >= maxTensionEnter)
            {
                maxTensionActive = true;
                Vector2 mid = 0.5f * (pos[0] + pos[nodeCount - 1]);
                OnRopeEvent?.Invoke(RopeEventType.MaxTension, mid);
            }
            else if (maxTensionActive && maxRatio < maxTensionExit)
            {
                maxTensionActive = false;
            }
        }

        // === Public API ===

        public void ReportRiderContact(Collider2D _slimeCol, Vector2 worldContact, float mass, Vector2 _riderVel)
        {
            if (nodeCount == 0) return;

            float radius = segmentRestLen * riderInfluenceSpacing;
            if (radius < 1e-4f) return;

            Vector2 force = Physics2D.gravity * mass;
            for (int i = 0; i < nodeCount; i++)
            {
                if (pinned[i]) continue;
                float d = Vector2.Distance(pos[i], worldContact);
                if (d >= radius) continue;
                float falloff = 1f - (d / radius);
                riderAccum[i] += force * falloff;
            }
        }

        public int FindNearestSegment(Vector2 worldPoint, out float t)
        {
            t = 0f;
            if (nodeCount < 2) return -1;

            int best = 0;
            float bestDistSq = float.MaxValue;
            float bestT = 0f;

            for (int i = 0; i < nodeCount - 1; i++)
            {
                Vector2 a = pos[i];
                Vector2 ab = pos[i + 1] - a;
                float segLenSq = ab.sqrMagnitude;
                if (segLenSq < 1e-5f) continue;

                float tt = Mathf.Clamp01(Vector2.Dot(worldPoint - a, ab) / segLenSq);
                Vector2 proj = a + ab * tt;
                float dsq = (worldPoint - proj).sqrMagnitude;

                if (dsq < bestDistSq)
                {
                    bestDistSq = dsq;
                    best = i;
                    bestT = tt;
                }
            }

            t = bestT;
            return best;
        }

        public Vector2 SampleVelocityAt(Vector2 worldPoint)
        {
            if (nodeCount < 2) return Vector2.zero;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return Vector2.zero;
            int seg = FindNearestSegment(worldPoint, out float t);
            Vector2 vA = (pos[seg] - prevPos[seg]) / dt;
            Vector2 vB = (pos[seg + 1] - prevPos[seg + 1]) / dt;
            return Vector2.Lerp(vA, vB, t);
        }

        public void NotifyFirstAttach(Vector2 worldContact)
        {
            OnRopeEvent?.Invoke(RopeEventType.FirstAttach, worldContact);
        }

        public void NotifyLaunchRebound(Vector2 worldContact)
        {
            OnRopeEvent?.Invoke(RopeEventType.LaunchRebound, worldContact);
        }

        public void ApplyWorldImpulseToRange(Vector2 worldImpulse, int fromIdx, int toIdx)
        {
            if (nodeCount == 0) return;
            fromIdx = Mathf.Clamp(fromIdx, 0, nodeCount - 1);
            toIdx = Mathf.Clamp(toIdx, 0, nodeCount - 1);
            for (int i = fromIdx; i <= toIdx; i++)
            {
                if (pinned[i]) continue;
                pos[i] += worldImpulse;
            }
        }

        public int NodeCount => nodeCount;
        public Vector2 GetNodePosition(int i) => pos[i];

        /// <summary>
        /// Max perpendicular distance from any node to the straight anchorA→anchorB line.
        /// 0 = rope is taut straight. Used by Launch() to convert stored elastic
        /// displacement into a launch-speed bonus.
        /// </summary>
        public float GetMaxSagDistance()
        {
            if (nodeCount < 3) return 0f;
            Vector2 a = pos[0];
            Vector2 b = pos[nodeCount - 1];
            Vector2 ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-6f) return 0f;

            float best = 0f;
            for (int i = 1; i < nodeCount - 1; i++)
            {
                Vector2 ap = pos[i] - a;
                float t = Mathf.Clamp01(Vector2.Dot(ap, ab) / abLenSq);
                Vector2 proj = a + ab * t;
                float d = (pos[i] - proj).magnitude;
                if (d > best) best = d;
            }
            return best;
        }

        // === Editor ===

        void OnValidate()
        {
            // Hard clamp: subsample > (segmentCount-1)/2 degenerates the collider toward a
            // 2-point straight line. Block the extreme before any warning math runs.
            int segCount = Mathf.Max(3, segmentCount);
            colliderSubsample = Mathf.Clamp(colliderSubsample, 1, Mathf.Max(1, (segCount - 1) / 2));

            if (anchorA == null || anchorB == null) return;

            float anchorDist = Vector3.Distance(anchorA.position, anchorB.position);
            float nodeSpacing = (anchorDist * restLengthMul) / (segCount - 1);
            float colliderSpacing = nodeSpacing * colliderSubsample;
            float minSpacing = slimeRadiusHint * 1.5f;
            float maxSpacing = slimeRadiusHint * 3f;

            if (colliderSpacing < minSpacing)
                Debug.LogWarning(
                    $"[Rope:{name}] Collider spacing {colliderSpacing:F3} < slimeRadius*1.5 ({minSpacing:F3}). " +
                    "Raise colliderSubsample, lower segmentCount, or increase anchor distance to avoid normal jitter.", this);
            else if (colliderSpacing > maxSpacing)
                Debug.LogWarning(
                    $"[Rope:{name}] Collider spacing {colliderSpacing:F3} > slimeRadius*3 ({maxSpacing:F3}). " +
                    "Segments may feel kinked at vertices — lower colliderSubsample or raise segmentCount.", this);

            if (elasticFactor > 0f && constraintIterations > 5)
                Debug.LogWarning(
                    $"[Rope:{name}] elasticFactor > 0 AND constraintIterations={constraintIterations} > 5. " +
                    "Double-action: constraint already enforces rest length, elastic becomes redundant. " +
                    "Use iter 2-3 for elastic rope, or elasticFactor=0 for rigid rope.", this);

#if UNITY_EDITOR
            editNeedsRefresh = true;
#endif
        }

        // Always-visible overlay so authors moving anchor markers see the rider-loaded
        // shape without having to select the rope Body. The rest shape is already
        // visible via the LineRenderer; this gizmo is only the deflected-under-rider shape.
        void OnDrawGizmos()
        {
            if (previewMaxSagPos == null || nodeCount < 2) return;
            if (previewMaxSagPos.Length != nodeCount) return;

            Gizmos.color = Color.magenta;
            for (int i = 0; i < nodeCount - 1; i++)
                Gizmos.DrawLine(previewMaxSagPos[i], previewMaxSagPos[i + 1]);
        }

        void OnDrawGizmosSelected()
        {
            if (nodeCount == 0) return;

            for (int i = 0; i < nodeCount; i++)
            {
                Gizmos.color = pinned[i] ? Color.red : Color.yellow;
                Gizmos.DrawSphere(pos[i], 0.05f);

                if (riderAccum != null && riderAccum[i].sqrMagnitude > 1e-4f)
                {
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(pos[i], pos[i] + riderAccum[i] * 0.02f);
                }
            }

            if (previewMaxSagPos != null && previewMaxSagPos.Length == nodeCount)
            {
                Gizmos.color = Color.magenta;
                for (int i = 0; i < nodeCount; i++)
                    Gizmos.DrawSphere(previewMaxSagPos[i], 0.04f);
            }
        }

#if UNITY_EDITOR
        // Edit-mode authoring preview. Runs two headless simulation phases:
        //   (1) settle with no rider → rest-sag shape, written to LineRenderer + EdgeCollider.
        //   (2) continue with a synthetic rider at midpoint → deepest deflected shape,
        //       cached as magenta gizmo overlay.
        // pos/prevPos end on the rest shape so the visible rope represents the
        // no-load state; entering play mode rebuilds from straight via Awake anyway.
        void RefreshEditPreview()
        {
            Rebuild();
            if (nodeCount < 3) return;

            const float dt = 0.02f;
            int steps = Mathf.Max(10, previewSteps);

            // Phase 1 — settle under self-gravity only (no rider).
            for (int s = 0; s < steps; s++)
            {
                StepSimulationOnce(dt);
                for (int i = 0; i < nodeCount; i++) riderAccum[i] = Vector2.zero;
            }
            var restPos = (Vector2[])pos.Clone();
            SyncVisual();

            // Phase 2 — add rider load at midpoint, track the deepest-sag frame.
            float radius = segmentRestLen * riderInfluenceSpacing;
            Vector2 force = Physics2D.gravity * previewRiderMass;
            float bestSag = GetMaxSagDistance();
            var bestPos = (Vector2[])pos.Clone();

            for (int s = 0; s < steps; s++)
            {
                Vector2 riderPoint = pos[nodeCount / 2];
                if (radius > 1e-4f)
                {
                    for (int i = 0; i < nodeCount; i++)
                    {
                        if (pinned[i]) continue;
                        float d = Vector2.Distance(pos[i], riderPoint);
                        if (d >= radius) continue;
                        float falloff = 1f - (d / radius);
                        riderAccum[i] += force * falloff;
                    }
                }
                StepSimulationOnce(dt);
                for (int i = 0; i < nodeCount; i++) riderAccum[i] = Vector2.zero;

                float sag = GetMaxSagDistance();
                if (sag > bestSag)
                {
                    bestSag = sag;
                    Array.Copy(pos, bestPos, nodeCount);
                }
            }

            previewMaxSagPos = bestPos;
            previewMaxSagValue = bestSag;

            // Restore the no-rider rest shape so LineRenderer + EdgeCollider show the
            // unloaded pose. The magenta gizmo overlays the loaded pose.
            Array.Copy(restPos, pos, nodeCount);
            Array.Copy(restPos, prevPos, nodeCount);
            SyncVisual();
        }

        [ContextMenu("Preview Max Sag")]
        void PreviewMaxSagMenu()
        {
            if (anchorA == null || anchorB == null)
            {
                Debug.LogWarning($"[Rope:{name}] Assign anchorA and anchorB before previewing.", this);
                return;
            }
            RefreshEditPreview();
            Debug.Log(
                $"[Rope:{name}] Preview max sag = {previewMaxSagValue:F3} m under {previewRiderMass:F1} kg rider. " +
                "LineRenderer = rest shape (no rider). Magenta gizmo = deflected shape (with rider).", this);
        }

        [ContextMenu("Clear Preview")]
        void ClearPreview()
        {
            previewMaxSagPos = null;
            previewMaxSagValue = 0f;
        }
#endif
    }
}
