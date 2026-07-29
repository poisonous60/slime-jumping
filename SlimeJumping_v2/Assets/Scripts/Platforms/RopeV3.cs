using System;
using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Trampoline-coupled rope. Verlet core shared with Rope/RopeV2; the rider is a Hookean
    // spring pulling nearby nodes toward (attach snapshot − sag − impact compression) along
    // gravity, and the slime is positionally carried via ConsumeRiderCarryDelta so it cannot
    // detach when the rope dips faster than the surface probe. Assumes Multi probe mode
    // (rb.linearVelocity is zeroed by the teleport branch, so carry is the only tracking channel).
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class RopeV3 : MonoBehaviour, IRopePlatform
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
        [SerializeField] float restLengthMul = 1.08f;

        [Header("Solver")]
        [Tooltip("Distance constraint iterations per FixedUpdate.")]
        [SerializeField, Min(1)] int constraintIterations = 3;
        [SerializeField, Range(0f, 1f)] float damping = 0.98f;
        [SerializeField, Range(0f, 1f)] float stretchSoftness = 0f;
        [SerializeField, Range(1, 8)] int simulationSubsteps = 2;
        [SerializeField, Range(0f, 0.5f)] float zigzagSmoothing = 0f;
        [SerializeField, Min(0f)] float edgeRadius = 0f;

        [Header("Forces")]
        [SerializeField] float nodeGravity = 0.3f;
        [Tooltip("Tension-only elastic coefficient. " +
                 "Active when segment currentLen > restLen — pulls nodes together.")]
        [SerializeField, Min(0f)] float elasticFactor = 0.3f;

        [Header("Rider Influence")]
        [Tooltip("Rider influence radius in node-spacing multiples. " +
                 "4 ≈ force distributes across 4 nodes each side of contact.")]
        [SerializeField, Min(0.5f)] float riderInfluenceSpacing = 4f;

        [Header("Rider Spring (V3)")]
        [Tooltip("Steady-state sag depth target (m) — how far below the attach-time " +
                 "shape a 1 kg rider should pull the contact node. The Hookean spring " +
                 "(see riderSpringStiffness) drives the rope toward this depth and " +
                 "Jakobsen tension settles it. Approximate; tune by eye in play mode.")]
        [SerializeField, Min(0f)] float riderEquilibriumDepth = 0.6f;

        [Tooltip("Spring stiffness k (N/m equivalent, mass-normalized). " +
                 "Higher = faster return-to-equilibrium, shorter oscillation period. " +
                 "60 ≈ ~0.4 s undamped period at default damping.")]
        [SerializeField, Min(0.01f)] float riderSpringStiffness = 60f;

        [Tooltip("Critical-damping ratio ζ. 0 = undamped (rings forever), " +
                 "1 = critical (no overshoot, fastest settle), 0.3 = lively trampoline " +
                 "(one big overshoot then quick settle).")]
        [SerializeField, Range(0f, 1f)] float riderSpringDamping = 0.3f;

        [Tooltip("Initial extra compression depth per (m/s) of impact velocity. " +
                 "6 m/s drop × 0.04 = 0.24 m of one-shot dip on top of the " +
                 "steady-state equilibrium depth, decaying via the spring's natural " +
                 "damped oscillation. Replaces v2's impactGain (which had no clean " +
                 "physical unit and broke surface tracking).")]
        [SerializeField, Min(0f)] float impactCompressionFromVelocity = 0.04f;

        [Tooltip("Reference rider mass (kg). Equilibrium depth scales by " +
                 "(slimeMass / referenceMass) — heavier rider sags deeper.")]
        [SerializeField, Min(0.01f)] float referenceMass = 1f;

        [Tooltip("Per-FixedUpdate carry safety cap (m). Bounds how far the slime can " +
                 "be teleported by the rope's contact-point motion in a single frame. " +
                 "Prevents tunneling through walls in degenerate cases.")]
        [SerializeField, Min(0f)] float maxCarryPerStep = 0.25f;

        [Header("Events — MaxTension (hysteresis)")]
        [SerializeField] float maxTensionEnter = 1.25f;
        [SerializeField] float maxTensionExit = 1.10f;

        [Header("Rebound — Timing Bonus (same as v2)")]
        [Tooltip("X = projected rope velocity in launch direction (m/s, ≥0). " +
                 "Y = bonus speed added to launch (m/s).")]
        [SerializeField] AnimationCurve timingBonusCurve = AnimationCurve.Linear(0f, 0f, 20f, 20f);

        [Tooltip("X = charge ratio at launch (aimOffset / chargeRadius, 0..1). Y = scalar multiplier.")]
        [SerializeField] AnimationCurve chargeMultiplierCurve = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Hard cap on speed the rope can transfer to the slime. 0 = uncapped.")]
        [SerializeField, Min(0f)] float maxReboundSpeed = 18f;

        [Header("Scene Preview")]
        [SerializeField, Min(0f)] float previewRiderMass = 1f;
        [SerializeField, Min(10)] int previewSteps = 120;

        [Header("Resolution decoupling")]
        [SerializeField, Min(1)] int colliderSubsample = 2;
        [SerializeField, Min(1)] int visualSubdivisions = 3;

        public EdgeCollider2D Edge => edge;
        public float MaxReboundSpeed => maxReboundSpeed;
        public event Action<RopeEventType, Vector2> OnRopeEvent;

        Vector2[] pos;
        Vector2[] prevPos;
        Vector2[] attachPos;
        bool[] pinned;
        Vector2[] edgeLocalPoints;
        Vector3[] lineWorldPoints;
        int[] colliderSourceIndices;
        float segmentRestLen;
        int nodeCount;

        bool maxTensionActive;

        // Rider spring state.
        int riderSegment = -1;
        float riderSegmentT;
        Vector2 prevContactSampled;
        Vector2 carryDeltaAccum;
        bool riderActive;
        float activeImpactDepth;
        float riderMass = 1f;
        bool attachSnapshotValid;

        // Debug introspection (set by ConsumeRiderCarryDelta — read by RopeV3Debug).
        Vector2 dbgLastCarryRaw;
        Vector2 dbgLastCarryReturned;
        bool dbgLastCarryClamped;
        int dbgLastCarryFrame = -1;

        // Preview cache.
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
            attachPos = new Vector2[nodeCount];
            pinned = new bool[nodeCount];

            int step = Mathf.Max(1, colliderSubsample);
            int last = nodeCount - 1;
            int count = last / step + 1;
            bool tailIncluded = (last % step == 0);
            if (!tailIncluded) count++;
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
                attachPos[i] = p;
            }

            attachSnapshotValid = false;
            riderActive = false;
            activeImpactDepth = 0f;
            carryDeltaAccum = Vector2.zero;
            riderSegment = -1;

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
            if (!Application.isPlaying) return;
            if (nodeCount == 0) return;
            if (anchorA == null || anchorB == null) return;

            int substeps = Mathf.Clamp(simulationSubsteps, 1, 8);
            float subDt = Time.fixedDeltaTime / substeps;

            // Sample before the first substep, else the per-FU carry misses sub-step motion.
            bool sampleCarry = riderActive && attachSnapshotValid && riderSegment >= 0;
            if (sampleCarry)
                prevContactSampled = SamplePosAt(riderSegment, riderSegmentT);

            for (int s = 0; s < substeps; s++)
                StepSimulationOnce(subDt);

            if (sampleCarry)
            {
                Vector2 newContact = SamplePosAt(riderSegment, riderSegmentT);
                carryDeltaAccum += (newContact - prevContactSampled);
            }

            EvaluateMaxTension();
            SyncVisual();

            // Controller must re-assert via ReportRiderContact every FU, else the spring dies.
            riderActive = false;
        }

#if UNITY_EDITOR
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

        void StepSimulationOnce(float dt)
        {
            float dt2 = dt * dt;
            Vector2 gravStep = Physics2D.gravity * (nodeGravity * dt2);

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
                Vector2 next = pos[i] + vel + gravStep;
                prevPos[i] = pos[i];
                pos[i] = next;
            }

            // Applied as a Verlet position step (Δpos = accel × dt²) so it composes with the
            // Jakobsen tension pass below instead of fighting it.
            if (riderActive && attachSnapshotValid)
            {
                Vector2 ropeDown = ComputeRopeDownDir();
                if (ropeDown.sqrMagnitude > 0.5f)
                {
                    float radius = segmentRestLen * riderInfluenceSpacing;
                    if (radius > 1e-4f)
                    {
                        Vector2 contact = SamplePosAt(riderSegment, riderSegmentT);
                        float massScale = riderMass / Mathf.Max(0.001f, referenceMass);
                        float k = riderSpringStiffness;
                        float dampCoeff = 2f * riderSpringDamping * Mathf.Sqrt(Mathf.Max(0.01f, k));

                        for (int i = 0; i < nodeCount; i++)
                        {
                            if (pinned[i]) continue;
                            float d = Vector2.Distance(attachPos[i], contact);
                            if (d >= radius) continue;
                            float falloff = 1f - (d / radius);

                            float targetDepth = (riderEquilibriumDepth * massScale + activeImpactDepth) * falloff;
                            float currentDepth = Vector2.Dot(pos[i] - attachPos[i], ropeDown);
                            float springError = targetDepth - currentDepth;
                            float vDown = Vector2.Dot(pos[i] - prevPos[i], ropeDown) / dt;

                            float accel = k * springError - dampCoeff * vDown;
                            pos[i] += ropeDown * (accel * dt2);
                        }
                    }

                    float decayRate = Mathf.Sqrt(Mathf.Max(0.01f, riderSpringStiffness)) * riderSpringDamping;
                    activeImpactDepth = Mathf.Max(0f, activeImpactDepth * (1f - decayRate * dt));
                }
            }

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

            int iters = Mathf.Max(1, constraintIterations);
            float projectionScale = Mathf.Clamp01(1f - stretchSoftness);
            for (int it = 0; it < iters; it++)
            {
                for (int i = 0; i < nodeCount - 1; i++)
                {
                    Vector2 delta = pos[i + 1] - pos[i];
                    float len = delta.magnitude;
                    if (len < 1e-5f) continue;

                    float diff = (len - segmentRestLen) / len * projectionScale;
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

            if (zigzagSmoothing > 0f)
            {
                float s = Mathf.Clamp(zigzagSmoothing, 0f, 0.5f);
                for (int i = 1; i < nodeCount - 1; i++)
                {
                    if (pinned[i]) continue;
                    Vector2 avg = (pos[i - 1] + pos[i + 1]) * 0.5f;
                    pos[i] = Vector2.Lerp(pos[i], avg, s);
                }
            }
        }

        void SyncVisual()
        {
            if (edge != null)
            {
                for (int j = 0; j < colliderSourceIndices.Length; j++)
                    edgeLocalPoints[j] = transform.InverseTransformPoint(pos[colliderSourceIndices[j]]);
                edge.points = edgeLocalPoints;
            }

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
                    Debug.Assert(w == expected, $"[RopeV3] lineWorldPoints write mismatch: {w} vs {expected}");
                }
                line.SetPositions(lineWorldPoints);
            }
        }

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

        Vector2 SamplePosAt(int segIdx, float t)
        {
            if (nodeCount < 2) return Vector2.zero;
            int seg = Mathf.Clamp(segIdx, 0, nodeCount - 2);
            return Vector2.Lerp(pos[seg], pos[seg + 1], Mathf.Clamp01(t));
        }

        Vector2 ComputeRopeDownDir()
        {
            Vector2 g = Physics2D.gravity;
            float m = g.magnitude;
            if (m < 1e-4f) return new Vector2(0f, -1f);
            return g / m;
        }

        // === Public API ===

        public void ApplyRiderImpact(Vector2 worldContact, Vector2 impactVelocity)
        {
            if (nodeCount == 0) return;

            riderSegment = FindNearestSegment(worldContact, out riderSegmentT);
            // Snapshot the current shape as the spring's reference frame — equilibrium is
            // depth below where contact landed, not below the rope's rest shape.
            Array.Copy(pos, attachPos, nodeCount);
            attachSnapshotValid = true;

            Vector2 ropeDown = ComputeRopeDownDir();
            float vDown = Mathf.Max(0f, Vector2.Dot(impactVelocity, ropeDown));
            // Stacks on any decaying compression so a double-pump trampoline accumulates.
            activeImpactDepth += vDown * impactCompressionFromVelocity;

            // Reset the baseline, else the first consume after attach replays pre-attach motion.
            prevContactSampled = SamplePosAt(riderSegment, riderSegmentT);
            carryDeltaAccum = Vector2.zero;
        }

        public void ReportRiderContact(Collider2D _slimeCol, Vector2 worldContact, float mass, Vector2 _riderVel)
        {
            if (nodeCount == 0) return;

            riderActive = true;
            riderMass = mass;
            // Re-lock every step — the slime crawls along the rope, so carry must track its
            // current parametric location, not the original attach point.
            riderSegment = FindNearestSegment(worldContact, out riderSegmentT);

            // A zero-velocity attach (slime spawned already on the rope) never calls
            // ApplyRiderImpact, so seed the snapshot here too.
            if (!attachSnapshotValid)
            {
                Array.Copy(pos, attachPos, nodeCount);
                attachSnapshotValid = true;
                prevContactSampled = SamplePosAt(riderSegment, riderSegmentT);
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
            // Release the spring reference frame and zero the carry, else a re-attach
            // replays stale motion.
            attachSnapshotValid = false;
            activeImpactDepth = 0f;
            carryDeltaAccum = Vector2.zero;
            riderActive = false;
            riderSegment = -1;
            OnRopeEvent?.Invoke(RopeEventType.LaunchRebound, worldContact);
        }

        public Vector2 ConsumeRiderCarryDelta(Vector2 _worldContact)
        {
            Vector2 raw = carryDeltaAccum;
            carryDeltaAccum = Vector2.zero;
            Vector2 d = raw;
            bool clamped = false;
            if (maxCarryPerStep > 0f && d.sqrMagnitude > maxCarryPerStep * maxCarryPerStep)
            {
                d = d.normalized * maxCarryPerStep;
                clamped = true;
            }
            dbgLastCarryRaw = raw;
            dbgLastCarryReturned = d;
            dbgLastCarryClamped = clamped;
            dbgLastCarryFrame = Time.frameCount;
            return d;
        }

        // TautLine-only minimum-vy enforcement; v3 leaves launch velocity unchanged.
        public Vector2 PostprocessLaunchVelocity(Vector2 launchVel) => launchVel;

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

        public float GetLaunchBonus(Vector2 launchDir, Vector2 worldContact, float chargeRatio)
        {
            if (nodeCount < 2) return 0f;
            if (launchDir.sqrMagnitude < 1e-6f) return 0f;

            Vector2 ropeVel = SampleVelocityAt(worldContact);
            float proj = Mathf.Max(0f, Vector2.Dot(ropeVel, launchDir.normalized));

            float timingBonus = timingBonusCurve != null ? timingBonusCurve.Evaluate(proj) : 0f;
            float chargeMul = chargeMultiplierCurve != null ? chargeMultiplierCurve.Evaluate(chargeRatio) : 1f;

            float bonus = timingBonus * chargeMul;
            if (bonus < 0f) bonus = 0f;
            if (maxReboundSpeed > 0f && bonus > maxReboundSpeed) bonus = maxReboundSpeed;
            return bonus;
        }

        // === Debug introspection (read-only) ===

        public struct DebugSnapshot
        {
            public bool riderActive;
            public bool attachSnapshotValid;
            public int riderSegment;
            public float riderSegmentT;
            public Vector2 contactPos;
            public Vector2 prevContactSampled;
            public float activeImpactDepth;
            public Vector2 pendingCarry;        // carryDeltaAccum BEFORE consume
            public float maxCarryPerStep;
            public Vector2 lastCarryRaw;        // last consume's pre-cap value
            public Vector2 lastCarryReturned;   // last consume's post-cap value
            public bool lastCarryClamped;
            public int lastCarryFrame;
            public int nodeCount;
            public float segmentRestLen;
            public float maxTensionRatio;
        }

        public DebugSnapshot GetDebugSnapshot()
        {
            float maxRatio = 0f;
            if (nodeCount >= 2)
            {
                for (int i = 0; i < nodeCount - 1; i++)
                {
                    float len = Vector2.Distance(pos[i], pos[i + 1]);
                    float r = len / Mathf.Max(1e-5f, segmentRestLen);
                    if (r > maxRatio) maxRatio = r;
                }
            }
            return new DebugSnapshot
            {
                riderActive = riderActive,
                attachSnapshotValid = attachSnapshotValid,
                riderSegment = riderSegment,
                riderSegmentT = riderSegmentT,
                contactPos = (riderSegment >= 0 && nodeCount >= 2)
                    ? SamplePosAt(riderSegment, riderSegmentT)
                    : Vector2.zero,
                prevContactSampled = prevContactSampled,
                activeImpactDepth = activeImpactDepth,
                pendingCarry = carryDeltaAccum,
                maxCarryPerStep = maxCarryPerStep,
                lastCarryRaw = dbgLastCarryRaw,
                lastCarryReturned = dbgLastCarryReturned,
                lastCarryClamped = dbgLastCarryClamped,
                lastCarryFrame = dbgLastCarryFrame,
                nodeCount = nodeCount,
                segmentRestLen = segmentRestLen,
                maxTensionRatio = maxRatio,
            };
        }

        // === Editor ===

        void OnValidate()
        {
            int segCount = Mathf.Max(3, segmentCount);
            colliderSubsample = Mathf.Clamp(colliderSubsample, 1, Mathf.Max(1, (segCount - 1) / 2));

#if UNITY_EDITOR
            editNeedsRefresh = true;
#endif
        }

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
            }

            if (previewMaxSagPos != null && previewMaxSagPos.Length == nodeCount)
            {
                Gizmos.color = Color.magenta;
                for (int i = 0; i < nodeCount; i++)
                    Gizmos.DrawSphere(previewMaxSagPos[i], 0.04f);
            }
        }

#if UNITY_EDITOR
        void RefreshEditPreview()
        {
            Rebuild();
            if (nodeCount < 3) return;

            float dt = Time.fixedDeltaTime;
            int substeps = Mathf.Clamp(simulationSubsteps, 1, 8);
            float subDt = dt / substeps;
            int steps = Mathf.Max(10, previewSteps);

            // Phase 1 — settle under self-gravity only (no rider).
            for (int s = 0; s < steps; s++)
            {
                for (int sub = 0; sub < substeps; sub++)
                    StepSimulationOnce(subDt);
            }
            var restPos = (Vector2[])pos.Clone();
            SyncVisual();

            // Phase 2 — synthetic spring rider at midpoint, track deepest dip.
            riderSegment = (nodeCount - 1) / 2;
            riderSegmentT = 0f;
            riderMass = previewRiderMass;
            riderActive = true;
            Array.Copy(pos, attachPos, nodeCount);
            attachSnapshotValid = true;
            activeImpactDepth = 0f;

            float bestSag = GetMaxSagDistance();
            var bestPos = (Vector2[])pos.Clone();

            for (int s = 0; s < steps; s++)
            {
                riderActive = true; // FixedUpdate would clear it; preview re-asserts every "frame"
                for (int sub = 0; sub < substeps; sub++)
                    StepSimulationOnce(subDt);

                float sag = GetMaxSagDistance();
                if (sag > bestSag)
                {
                    bestSag = sag;
                    Array.Copy(pos, bestPos, nodeCount);
                }
            }

            previewMaxSagPos = bestPos;
            previewMaxSagValue = bestSag;

            // Restore the no-rider rest shape.
            riderActive = false;
            attachSnapshotValid = false;
            activeImpactDepth = 0f;
            Array.Copy(restPos, pos, nodeCount);
            Array.Copy(restPos, prevPos, nodeCount);
            SyncVisual();
        }

        [ContextMenu("Preview Max Sag")]
        void PreviewMaxSagMenu()
        {
            if (anchorA == null || anchorB == null)
            {
                Debug.LogWarning($"[RopeV3:{name}] Assign anchorA and anchorB before previewing.", this);
                return;
            }
            RefreshEditPreview();
            Debug.Log(
                $"[RopeV3:{name}] Preview max sag = {previewMaxSagValue:F3} m under {previewRiderMass:F1} kg rider " +
                $"at riderEquilibriumDepth={riderEquilibriumDepth:F2} m. " +
                "LineRenderer = rest shape. Magenta gizmo = deflected shape.", this);
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
