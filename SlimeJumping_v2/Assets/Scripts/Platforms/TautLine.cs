using System;
using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Static-baseline rope: a parabolic baseline between two anchors,
    // deformed only by a localized spring-damped dip under the rider.
    // Replaces RopeV3's verlet polyline (kept alongside, not removed) to
    // sidestep the contact-node thrashing that broke re-attach after an
    // intermediate non-rope contact. SlimeController stays untouched —
    // implements IRopePlatform like the rest of the rope family.
    [ExecuteAlways]
    [DefaultExecutionOrder(-100)]
    public class TautLine : MonoBehaviour, IRopePlatform
    {
        [Header("Anchors")]
        [SerializeField] Transform anchorA;
        [SerializeField] Transform anchorB;

        [Header("Targets")]
        [SerializeField] EdgeCollider2D edge;
        [SerializeField] LineRenderer line;

        [Header("Baseline shape")]
        [SerializeField, Min(2)] int sampleCount = 32;
        [Tooltip("Peak parabolic sag at midpoint (m). 0 = perfectly straight.")]
        [SerializeField, Min(0f)] float baselineSag = 0.15f;

        [Header("Rider Spring")]
        [Tooltip("Steady-state dip target (m) per kg of rider mass.")]
        [SerializeField, Min(0f)] float equilibriumDepthPerKg = 0.4f;
        [Tooltip("Spring stiffness k (1/s²). Higher = faster settle, shorter period.")]
        [SerializeField, Min(0.01f)] float springStiffness = 60f;
        [Tooltip("Damping coefficient (1/s). Higher = quicker settle, less ringing.")]
        [SerializeField, Min(0f)] float springDamping = 5f;
        [Tooltip("Spring-velocity kick per (m/s) of impact velocity into gravity.")]
        [SerializeField, Min(0f)] float impactCompressionFromVelocity = 0.3f;
        [Tooltip("Per-FU carry safety cap (m). Bounds slime teleport per frame.")]
        [SerializeField, Min(0f)] float maxCarryPerStep = 0.25f;

        [Header("Local-dip kernel")]
        [Tooltip("Half-width of the local depression, in baseline parameter (u ∈ [0,1]) space.")]
        [SerializeField, Range(0.01f, 0.5f)] float kernelHalfWidth = 0.15f;

        [Header("Launch — bonus curves")]
        [SerializeField] AnimationCurve timingBonusCurve = AnimationCurve.Linear(0f, 0f, 20f, 20f);
        [SerializeField] AnimationCurve chargeMultiplierCurve = AnimationCurve.Constant(0f, 1f, 1f);
        [SerializeField, Min(0f)] float maxReboundSpeed = 18f;

        public EdgeCollider2D Edge => edge;
        public float MaxReboundSpeed => maxReboundSpeed;
        public event Action<RopeEventType, Vector2> OnRopeEvent;

        Vector2[] baselinePoints;
        Vector2 worldDown = new Vector2(0f, -1f);

        Vector2[] displacedLocal;
        Vector3[] displacedWorld;

        bool riderActive;
        float riderMass;
        float riderT;
        float dipDepth;
        float dipVelocity;
        Vector2 prevContactWorld;
        bool prevContactValid;
        Vector2 carryDeltaAccum;

        Vector3 lastAnchorAPos;
        Vector3 lastAnchorBPos;
        int lastSampleCount;
        float lastBaselineSag;

        void OnEnable() { Rebuild(); }

        void OnValidate()
        {
            if (sampleCount < 2) sampleCount = 2;
            if (isActiveAndEnabled) Rebuild();
        }

        public void Rebuild()
        {
            if (anchorA == null || anchorB == null) return;
            if (sampleCount < 2) sampleCount = 2;

            if (baselinePoints == null || baselinePoints.Length != sampleCount)
            {
                baselinePoints = new Vector2[sampleCount];
                displacedLocal = new Vector2[sampleCount];
                displacedWorld = new Vector3[sampleCount];
            }

            Vector2 g = Physics2D.gravity;
            worldDown = g.sqrMagnitude > 1e-6f ? g.normalized : new Vector2(0f, -1f);

            Vector2 a = anchorA.position;
            Vector2 b = anchorB.position;
            for (int i = 0; i < sampleCount; i++)
            {
                float u = i / (float)(sampleCount - 1);
                baselinePoints[i] = Vector2.Lerp(a, b, u)
                    + worldDown * (baselineSag * 4f * u * (1f - u));
            }

            lastAnchorAPos = anchorA.position;
            lastAnchorBPos = anchorB.position;
            lastSampleCount = sampleCount;
            lastBaselineSag = baselineSag;

            Sync(-1f);
        }

        bool AnchorsOrShapeChanged()
        {
            if (anchorA == null || anchorB == null) return false;
            return lastAnchorAPos != anchorA.position
                || lastAnchorBPos != anchorB.position
                || lastSampleCount != sampleCount
                || !Mathf.Approximately(lastBaselineSag, baselineSag);
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (anchorA == null || anchorB == null) return;
            if (AnchorsOrShapeChanged()) Rebuild();

            float dt = Time.fixedDeltaTime;

            float target = riderActive ? Mathf.Max(0f, riderMass) * equilibriumDepthPerKg : 0f;
            float accel = springStiffness * (target - dipDepth) - springDamping * dipVelocity;
            dipVelocity += accel * dt;
            dipDepth += dipVelocity * dt;

            if (dipDepth < 0f)
            {
                dipDepth = 0f;
                if (dipVelocity < 0f) dipVelocity = 0f;
            }

            if (riderActive)
            {
                Vector2 contactWorld = SampleBaseline(riderT) + worldDown * dipDepth;
                if (prevContactValid)
                {
                    Vector2 delta = contactWorld - prevContactWorld;
                    if (maxCarryPerStep > 0f && delta.sqrMagnitude > maxCarryPerStep * maxCarryPerStep)
                        delta = delta.normalized * maxCarryPerStep;
                    carryDeltaAccum += delta;
                }
                prevContactWorld = contactWorld;
                prevContactValid = true;
            }
            else
            {
                prevContactValid = false;
            }

            Sync(riderActive ? riderT : -1f);

            riderActive = false;
        }

        void Update()
        {
            if (Application.isPlaying) return;
            if (anchorA == null || anchorB == null) return;
            if (AnchorsOrShapeChanged()) Rebuild();
            else Sync(-1f);
        }

        Vector2 SampleBaseline(float u)
        {
            u = Mathf.Clamp01(u);
            float ix = u * (sampleCount - 1);
            int i0 = Mathf.FloorToInt(ix);
            int i1 = Mathf.Min(i0 + 1, sampleCount - 1);
            float t = ix - i0;
            return Vector2.Lerp(baselinePoints[i0], baselinePoints[i1], t);
        }

        float Kernel(float du)
        {
            float a = Mathf.Abs(du) / Mathf.Max(0.0001f, kernelHalfWidth);
            if (a >= 1f) return 0f;
            float s = 1f - a;
            return s * s * (3f - 2f * s);
        }

        void Sync(float riderUOrNeg)
        {
            if (baselinePoints == null) return;

            for (int i = 0; i < sampleCount; i++)
            {
                float u = i / (float)(sampleCount - 1);
                Vector2 p = baselinePoints[i];
                if (riderUOrNeg >= 0f)
                    p += worldDown * (dipDepth * Kernel(u - riderUOrNeg));
                displacedWorld[i] = new Vector3(p.x, p.y, 0f);
            }

            if (line != null)
            {
                if (line.positionCount != sampleCount) line.positionCount = sampleCount;
                line.SetPositions(displacedWorld);
            }

            if (edge != null)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    Vector2 w = new Vector2(displacedWorld[i].x, displacedWorld[i].y);
                    displacedLocal[i] = transform.InverseTransformPoint(w);
                }
                edge.points = displacedLocal;
            }
        }

        public Vector2 SampleVelocityAt(Vector2 worldPoint)
        {
            float u = ParamFromWorld(worldPoint);
            return worldDown * (dipVelocity * Kernel(u - riderT));
        }

        public void ReportRiderContact(Collider2D _slimeCol, Vector2 worldContact, float mass, Vector2 _riderVel)
        {
            riderActive = true;
            riderMass = mass;
            riderT = ParamFromWorld(worldContact);
        }

        public void NotifyFirstAttach(Vector2 worldContact)
        {
            riderT = ParamFromWorld(worldContact);
            OnRopeEvent?.Invoke(RopeEventType.FirstAttach, worldContact);
        }

        public void NotifyLaunchRebound(Vector2 worldContact)
        {
            dipDepth = 0f;
            dipVelocity = 0f;
            carryDeltaAccum = Vector2.zero;
            prevContactValid = false;
            riderActive = false;
            OnRopeEvent?.Invoke(RopeEventType.LaunchRebound, worldContact);
        }

        public void ApplyRiderImpact(Vector2 worldContact, Vector2 impactVelocity)
        {
            riderT = ParamFromWorld(worldContact);
            float vDown = Mathf.Max(0f, Vector2.Dot(impactVelocity, worldDown));
            dipVelocity += vDown * impactCompressionFromVelocity;
        }

        public float GetLaunchBonus(Vector2 launchDir, Vector2 worldContact, float chargeRatio)
        {
            if (launchDir.sqrMagnitude < 1e-6f) return 0f;
            Vector2 dir = launchDir.normalized;
            Vector2 ropeVel = SampleVelocityAt(worldContact);
            float proj = Mathf.Max(0f, Vector2.Dot(ropeVel, dir));
            float timing = timingBonusCurve.Evaluate(proj);
            float charge = chargeMultiplierCurve.Evaluate(chargeRatio);
            return Mathf.Clamp(timing * charge, 0f, maxReboundSpeed);
        }

        public Vector2 ConsumeRiderCarryDelta(Vector2 _worldContact)
        {
            Vector2 d = carryDeltaAccum;
            carryDeltaAccum = Vector2.zero;
            return d;
        }

        float ParamFromWorld(Vector2 p)
        {
            if (baselinePoints == null || baselinePoints.Length < 2) return 0f;
            int n = baselinePoints.Length;
            float bestDistSq = float.MaxValue;
            float bestU = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = baselinePoints[i];
                Vector2 b = baselinePoints[i + 1];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
                Vector2 c = a + ab * t;
                float d2 = (p - c).sqrMagnitude;
                if (d2 < bestDistSq)
                {
                    bestDistSq = d2;
                    bestU = (i + t) / (float)(n - 1);
                }
            }
            return bestU;
        }
    }
}
