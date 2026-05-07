using System;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    // Power-line / tightrope rope. Built from scratch for the slime project —
    // no shared lineage with Rope/RopeV2/RopeV3 (verlet-based, all had attach
    // and underside bugs).
    //
    // Core model:
    //   Static parabolic baseline (gentle gravity sag, designer-tunable).
    //   Single 1D spring-damper integrator on dipDepth (the rider-induced
    //   sag depth). Curve shape = baseline + worldDown × dipDepth × Bell(u, riderT)
    //   where Bell is a piecewise-smoothstep that peaks at the rider's parameter
    //   along the rope and tapers to 0 at both anchors. The whole rope
    //   responds to the rider, deepest exactly under them.
    //
    // Trampoline behaviour:
    //   While charging the rope sits at its steady-state baseEquilibrium dip —
    //   no extra deepening. At launch, GetLaunchBonus injects a one-shot
    //   downward dipVelocity pulse (launchImpactKickCurve(chargeRatio) m/s)
    //   so the rope reads as PUNCHED by the kickoff, not slowly compressed.
    //   PostprocessLaunchVelocity then clamps the slime's upward component up
    //   to minLaunchVerticalSpeed so a near-horizontal aim isn't immediately
    //   recaught by the rope's rebound. Spring switches to overdamped mode
    //   on detach so the rope returns to baseline smoothly without snap-back
    //   that could catch the launching slime.
    //
    // Underside / pass-through:
    //   Rope collider is a thin trigger PolygonCollider2D ribbon, never an
    //   EdgeCollider2D (which always tunneled in past attempts).
    //   When slime enters from below (vy > 0), TautLine registers the
    //   polygon in slime.IgnoredColliderList (Passthrough mode) for the
    //   transit duration, so SurfaceProbe skips it and slime continues
    //   through. Same KnifeHazard-style pattern.
    //
    // Post-launch:
    //   No extra IgnoredCollider work — SlimeController.Launch already calls
    //   ignoredColliderList.RegisterOverlapping (ReattachGuard mode) which
    //   filters our polygon from SurfaceProbe until the slime physically
    //   clears it. The spring's overdamped post-detach mode (detachedDamping
    //   ≥1) prevents any upward overshoot that could catch the slime.
    //
    // Slime tracking:
    //   No carry-delta channel (RopeV3's ConsumeRiderCarryDelta accumulated
    //   small per-FU deltas + maxCarryPerStep clamp → desync → detach bug).
    //   TautLine instead writes rb.position absolutely each FU (executionOrder
    //   200, after SlimeController) — slime is recomputed from baseline
    //   projection to the current curve, no accumulation, no clamp.
    [ExecuteAlways]
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(PolygonCollider2D))]
    public class TautLine : MonoBehaviour, IRopePlatform
    {
        [Header("Anchors")]
        [SerializeField] Transform anchorA;
        [SerializeField] Transform anchorB;

        [Header("Targets")]
        [SerializeField] PolygonCollider2D polygon;
        [SerializeField] LineRenderer line;

        [Header("Slime ref (auto-find if null)")]
        [SerializeField] SlimeController slime;

        [Header("Shape")]
        [SerializeField, Min(4)] int sampleCount = 32;
        [Tooltip("Static parabolic sag at midpoint with no rider (m). 0 = perfectly straight.")]
        [SerializeField, Min(0f)] float baselineSag = 0.15f;
        [Tooltip("Polygon collider thickness (perpendicular to curve, m). Anti-tunnel.")]
        [SerializeField, Min(0.02f)] float ribbonThickness = 0.1f;

        [Header("Spring (rider sag)")]
        [Tooltip("Steady-state dip depth at center under standing slime (m).")]
        [SerializeField, Min(0f)] float baseEquilibrium = 0.3f;
        [Tooltip("Spring stiffness k (1/s²). Higher = faster settle, shorter visible oscillation.")]
        [SerializeField, Min(0.01f)] float springStiffness = 60f;
        [Tooltip("Damping ratio while slime is on rope. <1 = bounce on landing, =1 = no overshoot.")]
        [SerializeField, Range(0f, 2f)] float attachedDampingRatio = 0.5f;
        [Tooltip("Damping ratio while a trampoline cycle is active (between impact and trampolineHoldTimer expiry). Much lower than attachedDampingRatio so the rope visibly OSCILLATES up and down around the deep target inside the bowl rather than snapping to it once. Default 0.15 → ~4–5 visible bounces over a 1-second hold (period ≈ 2π/√springStiffness ≈ 0.8s).")]
        [SerializeField, Range(0f, 2f)] float trampolineDampingRatio = 0.15f;
        [Tooltip("Damping ratio after slime detaches. ≥1 = no oscillation past baseline.")]
        [SerializeField, Range(0.5f, 3f)] float detachedDampingRatio = 1.2f;
        [Tooltip("Initial dipVelocity kick per (m/s) of impact velocity into gravity. 0 = no impact reaction.")]
        [SerializeField, Min(0f)] float impactCompressionFromVelocity = 0.05f;

        [Header("Trampoline (impulse-driven bounce)")]
        [Tooltip("Impact speed (m/s into gravity) → initial DOWNWARD dipVelocity injected at landing. The spring's target stays at baseEquilibrium — this impulse drives the rope deep, the spring then pulls it back up THROUGH baseline and overshoots ABOVE baseline (because trampolineDampingRatio is underdamped). Asymmetric bouncing: deep first dip, smaller upward overshoot, decaying. X axis: impact speed (m/s). Y axis: dipVelocity kick (m/s downward).")]
        [SerializeField] AnimationCurve trampolineImpactKickCurve = AnimationCurve.Linear(0f, 0f, 30f, 15f);
        [Tooltip("Impact speed (m/s into gravity) → seconds the rope stays in the underdamped (trampolineDampingRatio) regime after impact. Longer = more visible up/down cycles before damping reverts to attachedDampingRatio. Independent of the natural spring period — you can have many quick cycles or a few slow cycles by tuning springStiffness. X axis: impact speed (m/s). Y axis: window duration (s).")]
        [SerializeField] AnimationCurve trampolineHoldCurve = AnimationCurve.Linear(0f, 0f, 30f, 1.5f);

        [Header("Launch boost (effect-mode)")]
        [Tooltip("Bonus speed added to slime's launch velocity at FULL charge (m/s). Scales linearly with chargeRatio.")]
        [SerializeField, Min(0f)] float jumpBoost = 3f;
        [Tooltip("Hard cap on bonus speed (m/s).")]
        [SerializeField, Min(0f)] float maxCap = 6f;
        [Tooltip("Downward dipVelocity kick (m/s) applied to the rope at launch — visual punch as the slime kicks off. Curve maps chargeRatio∈[0,1] to kick magnitude.")]
        [SerializeField] AnimationCurve launchImpactKickCurve = AnimationCurve.Linear(0f, 0f, 1f, 4f);
        [Tooltip("Minimum upward (worldUp) velocity guaranteed for any launch off this rope (m/s). Constant — does not scale with charge. 0 = disabled. Prevents near-horizontal launches from being immediately recaught by the rope's spring rebound.")]
        [SerializeField, Min(0f)] float minLaunchVerticalSpeed = 3f;
        [Tooltip("Multiplier on jumpBoost based on the rope's UPWARD speed at the launch moment. X axis: rope upward speed (m/s, = -dipVelocity, peaks as the rope passes through baseline on its way up). Y axis: multiplier on jumpBoost. Default Linear((0,0)→(5,1)) — zero bonus when rope is still or moving down, full bonus when rising at 5 m/s. Rewards player for syncing jump with the rope's upward swing.")]
        [SerializeField] AnimationCurve jumpTimingCurve = AnimationCurve.Linear(0f, 0f, 5f, 1f);

        [Header("Pass-through (one-way semi-solid)")]
        [Tooltip("Per-FU duration to refresh IgnoredCollider timer while in passthrough (s).")]
        [SerializeField, Min(0.05f)] float passthroughRefreshDuration = 0.2f;
        [Tooltip("|dipVelocity| below this (m/s) means rope settled enough to release post-launch passthrough.")]
        [SerializeField, Min(0.001f)] float settleVelocityThreshold = 0.05f;

        [Header("Surface Override")]
        [Tooltip("SurfaceMaterial pushed onto the slime's override stack while attached to this rope. " +
                 "Mirrors WaterCurrentZone / SpringyBranchPlatform pattern — Push on attach edge, " +
                 "Pop on detach edge (and safety pop in OnDisable). Use to grant rope-specific tuning " +
                 "(low friction, longer energyConversionCoyoteTime, etc.) without touching global " +
                 "SlimeSettings. Leave empty to skip override.")]
        [SerializeField] SurfaceMaterial ropeSurfaceMaterial;

        public EdgeCollider2D Edge => null;     // null → Attempt-4/5 hacks in SlimeController bypass naturally
        public float MaxReboundSpeed => maxCap;
        public event Action<RopeEventType, Vector2> OnRopeEvent;

        Vector2[] baselinePoints;
        Vector3[] lineWorldPoints;
        Vector2[] polygonLocalPoints;
        Vector2 worldDown = new Vector2(0f, -1f);
        Vector2 worldUp = new Vector2(0f, 1f);
        Vector2 baselineTangent = new Vector2(1f, 0f);
        Vector2 baselineNormal = new Vector2(0f, 1f);

        Vector3 lastAnchorAPos;
        Vector3 lastAnchorBPos;
        int lastSampleCount;
        float lastBaselineSag;
        float lastRibbonThickness;

        // Runtime spring state
        bool riderActive;
        float riderT;
        float dipDepth;
        float dipVelocity;
        SlimeController.SlimeState lastState = SlimeController.SlimeState.Airborne;
        bool postDetach;        // Use detachedDampingRatio while true

        // Trampoline state. Impulse-driven model — target stays at baseEquilibrium
        // throughout. ApplyRiderImpact injects a downward dipVelocity kick (proportional
        // to impact speed via trampolineImpactKickCurve), and trampolineHoldTimer keeps
        // the spring in the underdamped (trampolineDampingRatio) regime for a duration
        // proportional to impact speed. Spring physics naturally produce asymmetric
        // bouncing: deep first dip, smaller upward overshoot above baseline, decaying.
        float trampolineHoldTimer;

        // Pass-through (from-below) state
        bool inPassthroughBelow;

        // True while ropeSurfaceMaterial is on the slime's override stack and we still
        // owe a Pop. Tracked so that disabling the rope mid-attach doesn't leak the
        // override (mirrors WaterCurrentZone.overridePushed semantics).
        bool overridePushed;

        Rigidbody2D slimeRb;
        Collider2D slimeCol;

        void Reset() { polygon = GetComponent<PolygonCollider2D>(); }

        void OnEnable()
        {
            if (polygon == null) polygon = GetComponent<PolygonCollider2D>();
            if (polygon != null) polygon.isTrigger = true;
            ResolveSlime();
            Rebuild();
        }

        void OnDisable()
        {
            // Safety pop — disabling the rope while a slime is attached must not leak
            // ropeSurfaceMaterial onto the slime's override stack.
            if (overridePushed && slime != null && ropeSurfaceMaterial != null)
                slime.PopSurfaceMaterialOverride(ropeSurfaceMaterial);
            overridePushed = false;
        }

        void OnValidate()
        {
            if (sampleCount < 4) sampleCount = 4;
            if (polygon != null) polygon.isTrigger = true;
            if (isActiveAndEnabled) Rebuild();
        }

        void ResolveSlime()
        {
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (slime != null)
            {
                slimeRb = slime.GetComponent<Rigidbody2D>();
                slimeCol = slime.GetComponent<CircleCollider2D>();
            }
        }

        public void Rebuild()
        {
            if (anchorA == null || anchorB == null) return;
            if (sampleCount < 4) sampleCount = 4;

            if (baselinePoints == null || baselinePoints.Length != sampleCount)
            {
                baselinePoints = new Vector2[sampleCount];
                lineWorldPoints = new Vector3[sampleCount];
                polygonLocalPoints = new Vector2[sampleCount * 2];
            }

            Vector2 g = Physics2D.gravity;
            worldDown = g.sqrMagnitude > 1e-6f ? g.normalized : new Vector2(0f, -1f);
            worldUp = -worldDown;

            Vector2 a = anchorA.position;
            Vector2 b = anchorB.position;
            Vector2 ab = b - a;
            baselineTangent = ab.sqrMagnitude > 1e-6f ? ab.normalized : new Vector2(1f, 0f);
            // perpendicular pointing in worldUp direction (away from gravity)
            Vector2 perp = new Vector2(-baselineTangent.y, baselineTangent.x);
            if (Vector2.Dot(perp, worldUp) < 0f) perp = -perp;
            baselineNormal = perp;

            for (int i = 0; i < sampleCount; i++)
            {
                float u = i / (float)(sampleCount - 1);
                // Parabolic sag: 4u(1-u) gives 1 at midpoint, 0 at ends.
                baselinePoints[i] = Vector2.Lerp(a, b, u)
                    + worldDown * (baselineSag * 4f * u * (1f - u));
            }

            lastAnchorAPos = anchorA.position;
            lastAnchorBPos = anchorB.position;
            lastSampleCount = sampleCount;
            lastBaselineSag = baselineSag;
            lastRibbonThickness = ribbonThickness;

            SyncShapes();
        }

        bool AnchorsOrShapeChanged()
        {
            if (anchorA == null || anchorB == null) return false;
            return lastAnchorAPos != anchorA.position
                || lastAnchorBPos != anchorB.position
                || lastSampleCount != sampleCount
                || !Mathf.Approximately(lastBaselineSag, baselineSag)
                || !Mathf.Approximately(lastRibbonThickness, ribbonThickness);
        }

        void Update()
        {
            // Editor-mode preview: rebuild on anchor change so designer sees baseline shape.
            if (Application.isPlaying) return;
            if (anchorA == null || anchorB == null) return;
            if (AnchorsOrShapeChanged()) Rebuild();
            else SyncShapes();
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;
            if (anchorA == null || anchorB == null) return;
            if (AnchorsOrShapeChanged()) Rebuild();
            if (slime == null) ResolveSlime();
            if (slime == null) return;

            float dt = Time.fixedDeltaTime;

            // --- 1. State edge detection ---
            var state = slime.CurrentState;
            bool wasOnRope = (lastState == SlimeController.SlimeState.Attached
                           || lastState == SlimeController.SlimeState.Charging)
                           && riderActive;   // riderActive last FU
            bool nowAirborne = state == SlimeController.SlimeState.Airborne;

            // --- 2. Update riderActive from current attach state ---
            // (Slime is "on this rope" iff it is attached AND its surface collider is our polygon.)
            bool onThisRope = (state == SlimeController.SlimeState.Attached
                            || state == SlimeController.SlimeState.Charging)
                            && polygon != null
                            && slime.SurfaceCollider == polygon;

            if (onThisRope)
            {
                riderActive = true;
                riderT = ProjectToBaselineParam(slime.Position);
                postDetach = false;   // re-attached; clear post-detach mode
            }
            else
            {
                riderActive = false;
            }

            // --- 2b. Surface override push/pop edges ---
            // Mirrors WaterCurrentZone: Push on attach edge, Pop on detach edge. Driven by
            // riderActive transitions rather than trigger callbacks so it stays in sync with
            // the rest of the rope's state machine even if SlimeController detaches the slime
            // by other means (e.g. friction-detach mid-rope).
            if (ropeSurfaceMaterial != null)
            {
                if (riderActive && !overridePushed)
                {
                    slime.PushSurfaceMaterialOverride(ropeSurfaceMaterial);
                    overridePushed = true;
                }
                else if (!riderActive && overridePushed)
                {
                    slime.PopSurfaceMaterialOverride(ropeSurfaceMaterial);
                    overridePushed = false;
                }
            }

            // --- 3. Detach edge → switch spring to overdamped (no upward overshoot) ---
            // No IgnoredCollider work here — SlimeController.Launch already registered
            // our polygon as ReattachGuard via ignoredColliderList.RegisterOverlapping,
            // which filters us from SurfaceProbe until slime physically clears the area
            // (or postLaunchTimer expires). Adding extra Passthrough on top blocks the
            // legitimate fall-back-and-land that the player expects.
            if (wasOnRope && nowAirborne)
            {
                postDetach = true;
                // Trampoline state belongs to the rider that just left — clear so the
                // damping window doesn't carry over to no-rider physics.
                trampolineHoldTimer = 0f;
            }

            // --- 3b. Tick trampoline damping window ---
            if (trampolineHoldTimer > 0f)
            {
                trampolineHoldTimer -= dt;
                if (trampolineHoldTimer < 0f) trampolineHoldTimer = 0f;
            }

            // --- 4. Compute spring target ---
            // Charging no longer deepens the rope — that mass is delivered as a one-shot
            // dipVelocity kick at launch (see GetLaunchBonus) so the player feels the
            // rope punch DOWN as the slime kicks off, instead of slowly sagging while
            // they hold charge.
            // Trampoline does NOT shift the target — it injects an initial dipVelocity
            // impulse and lowers the damping ratio so the spring rings around the same
            // baseline target. Result: deep first dip, smaller upward overshoot ABOVE
            // baseline, decaying — natural asymmetric bouncing instead of a held bowl.
            float target = 0f;
            if (riderActive)
            {
                target = baseEquilibrium * ShapeFactor(riderT);
            }

            // --- 5. Integrate spring (semi-implicit Euler) ---
            // c = 2 ζ √k → ζ controls overshoot: <1 underdamped (visible bounce), ≥1 overdamped (no overshoot).
            // Three damping regimes:
            //   postDetach            → detachedDampingRatio (overdamped, no upward catch on launching slime)
            //   trampolineHoldTimer>0 → trampolineDampingRatio (underdamped, visible oscillation in the bowl)
            //   else                  → attachedDampingRatio (steady-state attached behavior)
            float zeta;
            if (postDetach) zeta = detachedDampingRatio;
            else if (trampolineHoldTimer > 0f) zeta = trampolineDampingRatio;
            else zeta = attachedDampingRatio;
            float c = 2f * zeta * Mathf.Sqrt(springStiffness);
            float accel = springStiffness * (target - dipDepth) - c * dipVelocity;
            dipVelocity += accel * dt;
            dipDepth += dipVelocity * dt;

            // --- 6. Clear post-detach when settled ---
            if (postDetach
                && Mathf.Abs(dipVelocity) < settleVelocityThreshold
                && Mathf.Abs(dipDepth) < settleVelocityThreshold * 0.1f)
            {
                postDetach = false;
                dipDepth = 0f;
                dipVelocity = 0f;
            }

            // --- 7. Sync collider + visual ---
            SyncShapes();

            // --- 8. Snap slime to current curve (overrides SlimeController's positioning) ---
            if (riderActive && slimeRb != null)
            {
                Vector2 curvePoint = SampleCurveAt(riderT);
                Vector2 desired = curvePoint + baselineNormal * slime.SlimeRadius;
                slimeRb.position = desired;
            }

            lastState = state;
        }

        // --- Curve sampling --------------------------------------------------

        float ShapeFactor(float u) => 4f * u * (1f - u);   // 1 at u=0.5, 0 at endpoints

        // Piecewise smoothstep bell, peaks at u=riderT, 0 at u=0 and u=1.
        // Smoothstep gives C¹ continuity at the peak — no kink under slime.
        float Bell(float u, float t)
        {
            t = Mathf.Clamp01(t);
            if (u <= t)
            {
                if (t < 1e-5f) return 0f;
                float s = u / t;
                return s * s * (3f - 2f * s);
            }
            else
            {
                float r = 1f - t;
                if (r < 1e-5f) return 0f;
                float s = (1f - u) / r;
                return s * s * (3f - 2f * s);
            }
        }

        Vector2 SampleBaselineAt(float u)
        {
            if (baselinePoints == null || baselinePoints.Length < 2) return Vector2.zero;
            u = Mathf.Clamp01(u);
            float ix = u * (baselinePoints.Length - 1);
            int i0 = Mathf.FloorToInt(ix);
            int i1 = Mathf.Min(i0 + 1, baselinePoints.Length - 1);
            float t = ix - i0;
            return Vector2.Lerp(baselinePoints[i0], baselinePoints[i1], t);
        }

        Vector2 SampleCurveAt(float u)
        {
            return SampleBaselineAt(u) + worldDown * (dipDepth * Bell(u, riderT));
        }

        float ProjectToBaselineParam(Vector2 worldPos)
        {
            if (baselinePoints == null || baselinePoints.Length < 2) return 0f;
            int n = baselinePoints.Length;
            float bestSq = float.MaxValue;
            float bestU = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = baselinePoints[i];
                Vector2 b = baselinePoints[i + 1];
                Vector2 ab = b - a;
                float len2 = ab.sqrMagnitude;
                float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(worldPos - a, ab) / len2) : 0f;
                Vector2 c = a + ab * t;
                float d2 = (worldPos - c).sqrMagnitude;
                if (d2 < bestSq)
                {
                    bestSq = d2;
                    bestU = (i + t) / (float)(n - 1);
                }
            }
            return bestU;
        }

        // --- Polygon + LineRenderer sync -----------------------------------

        void SyncShapes()
        {
            if (baselinePoints == null) return;
            int n = baselinePoints.Length;

            // Top edge of ribbon = displaced curve (where slime sits).
            // Bottom edge = top edge + worldDown × ribbonThickness.
            // Polygon is closed loop: top-left → top-right → bottom-right → bottom-left.
            for (int i = 0; i < n; i++)
            {
                float u = i / (float)(n - 1);
                Vector2 top = baselinePoints[i] + worldDown * (dipDepth * Bell(u, riderT));
                lineWorldPoints[i] = new Vector3(top.x, top.y, 0f);
                Vector2 bottom = top + worldDown * ribbonThickness;
                polygonLocalPoints[i] = transform.InverseTransformPoint(top);
                polygonLocalPoints[2 * n - 1 - i] = transform.InverseTransformPoint(bottom);
            }

            if (line != null)
            {
                if (line.positionCount != n) line.positionCount = n;
                line.SetPositions(lineWorldPoints);
            }

            if (polygon != null)
            {
                polygon.SetPath(0, polygonLocalPoints);
            }
        }

        // --- One-way pass-through (from-below entry) -----------------------

        void OnTriggerEnter2D(Collider2D other)
        {
            if (slime == null || polygon == null) return;
            var hitSlime = other.GetComponentInParent<SlimeController>();
            if (hitSlime != slime) return;

            if (slime.Velocity.y > 0f)
            {
                // Approaching upward → register Passthrough so probe skips us.
                slime.RegisterIgnoredCollider(polygon, passthroughRefreshDuration);
                inPassthroughBelow = true;
                return;
            }

            // Falling onto polygon. Anti-tunneling rescue:
            // Fast slime can move > polygon thickness in one FU, ending up DEEPLY inside the
            // polygon's interior. SurfaceProbe's ClosestPoint then returns the query point
            // itself ([SingleContactSurfaceProbe.cs:30-31] deep-penetration skip), so the
            // probe never sees the contact and slime tunnels through. Unity's normal contact
            // manifold doesn't have this problem; ours does.
            //
            // Pull slime back to the curve's top edge + slimeRadius so SurfaceProbe sees a
            // normal contact next FU and attaches cleanly.
            if (baselinePoints == null) return;
            float u = ProjectToBaselineParam(slime.Position);
            Vector2 topAtU = SampleCurveAt(u);
            Vector2 toSlime = (Vector2)slime.Position - topAtU;
            float aboveDistance = Vector2.Dot(toSlime, baselineNormal);
            if (aboveDistance < slime.SlimeRadius && slimeRb != null)
            {
                slimeRb.position = topAtU + baselineNormal * slime.SlimeRadius;
            }
        }

        void OnTriggerStay2D(Collider2D other)
        {
            if (!inPassthroughBelow) return;
            if (slime == null || polygon == null) return;
            var hitSlime = other.GetComponentInParent<SlimeController>();
            if (hitSlime != slime) return;
            // Refresh timer while still transiting.
            slime.RegisterIgnoredCollider(polygon, passthroughRefreshDuration);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (!inPassthroughBelow) return;
            if (slime == null || polygon == null) return;
            var hitSlime = other.GetComponentInParent<SlimeController>();
            if (hitSlime != slime) return;
            slime.UnregisterIgnoredCollider(polygon);
            inPassthroughBelow = false;
        }

        // --- IRopePlatform implementation ----------------------------------

        public Vector2 SampleVelocityAt(Vector2 worldPoint)
        {
            // Velocity at any point on rope = worldDown × dipVelocity × Bell(u, riderT).
            float u = ProjectToBaselineParam(worldPoint);
            return worldDown * (dipVelocity * Bell(u, riderT));
        }

        // We derive riderActive from slime.SurfaceCollider in FixedUpdate, not from this callback.
        // SlimeController calls ReportRiderContact every FU while attached; ignored here.
        public void ReportRiderContact(Collider2D _slimeCol, Vector2 _worldContact, float _mass, Vector2 _riderVel) { }

        public void NotifyFirstAttach(Vector2 worldContact)
        {
            postDetach = false;
            OnRopeEvent?.Invoke(RopeEventType.FirstAttach, worldContact);
        }

        public void NotifyLaunchRebound(Vector2 worldContact)
        {
            // Detach edge detection in FixedUpdate already starts the settle coroutine;
            // this hook is kept for parity with v1/v2/v3 and external listeners.
            OnRopeEvent?.Invoke(RopeEventType.LaunchRebound, worldContact);
        }

        public void ApplyRiderImpact(Vector2 _worldContact, Vector2 impactVelocity)
        {
            float vDown = Mathf.Max(0f, Vector2.Dot(impactVelocity, worldDown));
            if (vDown <= 0f) return;

            // Punch: small extra dipVelocity kick on top of trampoline impulse — keeps the
            // existing per-(m/s) coefficient available for fine-tuning. Set to 0 to use
            // pure trampoline curve.
            if (impactCompressionFromVelocity > 0f)
                dipVelocity += vDown * impactCompressionFromVelocity;

            // Trampoline: inject downward dipVelocity proportional to impact speed and
            // open the underdamped damping window. Spring target stays at baseEquilibrium
            // (no shift), so the system rings around baseline rather than around a deep
            // point — that's what lets it actually go ABOVE baseline on the upswing.
            // largest-wins composes near-simultaneous impacts into the bigger of the two.
            float kick = trampolineImpactKickCurve != null ? trampolineImpactKickCurve.Evaluate(vDown) : 0f;
            float hold = trampolineHoldCurve != null ? trampolineHoldCurve.Evaluate(vDown) : 0f;
            if (kick > 0f) dipVelocity += kick;
            if (hold > trampolineHoldTimer) trampolineHoldTimer = hold;
        }

        public float GetLaunchBonus(Vector2 launchDir, Vector2 _worldContact, float chargeRatio)
        {
            // Side effect: the rope absorbs the slime's kickoff as a downward dipVelocity
            // pulse, shaped by launchImpactKickCurve(chargeRatio). The visual deepening
            // that v6 used to draw during charging now happens here, in one frame, at
            // the moment the slime actually pushes off — so the rope reads as elastic
            // (punched, not slowly compressed).
            if (launchImpactKickCurve != null)
            {
                float kick = launchImpactKickCurve.Evaluate(Mathf.Clamp01(chargeRatio));
                if (kick > 0f) dipVelocity += kick;
            }

            if (launchDir.sqrMagnitude < 1e-6f) return 0f;
            Vector2 dir = launchDir.normalized;
            float upward = Mathf.Max(0f, Vector2.Dot(dir, worldUp));
            // Timing gate: rope's upward speed at this instant gates the bonus. dipVelocity
            // is in worldDown (+= downward), so -dipVelocity is upward speed. Peaks as the
            // rope passes through baseline on its way back up after a trampoline release.
            // jumpTimingCurve maps that speed to a multiplier — designer can shape what
            // counts as "good timing" (default Linear((0,0)→(5,1))).
            float ropeUpSpeed = Mathf.Max(0f, -dipVelocity);
            float timing = jumpTimingCurve != null ? jumpTimingCurve.Evaluate(ropeUpSpeed) : 1f;
            // Effect-mode (β): bonus scales linearly with chargeRatio, capped, gated by timing.
            // Designer tunes by jumpBoost (full-charge boost) + maxCap (hard cap) +
            // jumpTimingCurve (timing window shape).
            float bonus = jumpBoost * Mathf.Clamp01(chargeRatio) * upward * Mathf.Max(0f, timing);
            return Mathf.Clamp(bonus, 0f, maxCap);
        }

        public Vector2 PostprocessLaunchVelocity(Vector2 launchVel)
        {
            // Guarantee the slime leaves with at least minLaunchVerticalSpeed of upward
            // (worldUp) speed. A near-horizontal aim otherwise drops the slime onto the
            // rope's spring rebound, which then catches it and re-attaches.
            if (minLaunchVerticalSpeed <= 0f) return launchVel;
            float upSpeed = Vector2.Dot(launchVel, worldUp);
            if (upSpeed >= minLaunchVerticalSpeed) return launchVel;
            return launchVel + worldUp * (minLaunchVerticalSpeed - upSpeed);
        }

        public Vector2 ConsumeRiderCarryDelta(Vector2 _worldContact)
        {
            // Carry channel is unused — TautLine writes rb.position absolutely each FU
            // in SnapSlimeIfAttached. Returning zero here keeps SlimeController's
            // existing carry-add-to-rb.position branch a no-op for this rope.
            return Vector2.zero;
        }
    }
}
