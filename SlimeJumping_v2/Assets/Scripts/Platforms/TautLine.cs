using System;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    // Tightrope. Static parabolic baseline + one 1D spring-damper on dipDepth;
    // curve = baseline + worldDown × dipDepth × Bell(u, riderT).
    // Collider is a thin trigger PolygonCollider2D — EdgeCollider2D always tunneled.
    // From-below entry registers the polygon as Passthrough in slime.IgnoredColliderList.
    // Rider pos written absolutely each FU (order 200); carry-delta accumulation desynced.
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
        [Tooltip("Below this pre-bonus launch speed (m/s) the rope grants full jumpBoost. " +
                 "Pre-bonus speed = slime.Velocity.magnitude at the moment GetLaunchBonus is " +
                 "called — already includes EnergyConversion power, carrier inheritance, hyperdash, " +
                 "but NOT the rope bonus itself. Lets the rope reward normal launches without " +
                 "stacking extra height on top of a high-fall EnergyConversion launch.")]
        [SerializeField, Min(0f)] float boostFalloffStartSpeed = 12f;
        [Tooltip("Above this pre-bonus launch speed (m/s) the rope grants zero jumpBoost. " +
                 "Linear falloff between boostFalloffStartSpeed and boostFalloffEndSpeed. " +
                 "Set ≤ boostFalloffStartSpeed for hard cutoff. The slime's own (EnergyConversion " +
                 "+ inheritance) speed passes through unchanged — only the rope's added boost is " +
                 "removed, so a high-fall landing still launches with its full converted energy.")]
        [SerializeField, Min(0f)] float boostFalloffEndSpeed = 18f;
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

        public EdgeCollider2D Edge => null;     // null → SlimeController's edge-collider paths bypass naturally
        public float MaxReboundSpeed => maxCap;
        public event Action<RopeEventType, Vector2> OnRopeEvent;

        // Read-only diagnostics surface (pulled by debug components; never written here).
        public PolygonCollider2D Polygon => polygon;
        public bool DebugRiderActive => riderActive;
        public float DebugRiderT => riderT;
        public float DebugDipDepth => dipDepth;
        public float DebugDipVelocity => dipVelocity;
        public Vector2 DebugBaselineTangent => baselineTangent;
        public float DebugProjectParam(Vector2 worldPos) => ProjectToBaselineParam(worldPos);

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

        bool riderActive;
        float riderT;
        float dipDepth;
        float dipVelocity;
        SlimeController.SlimeState lastState = SlimeController.SlimeState.Airborne;
        bool postDetach;        // Use detachedDampingRatio while true

        // polygon.SetPath dirties the Box2D broadphase against every neighboring static
        // collider (5–20ms on a dense stage), so skip when (dipDepth, riderT) is unchanged.
        // INVARIANT: transform must be static at runtime — local points are cached.
        Vector2 lastSyncedDipDepth_riderT;
        bool    syncedAtLeastOnce;

        // Impulse model: the spring target stays at baseEquilibrium, so the ring goes ABOVE
        // baseline on the upswing. This timer holds the underdamped regime after an impact.
        float trampolineHoldTimer;

        bool inPassthroughBelow;

        // Broad-phase reject for UpdateFromBelowPassthrough, before the O(sampleCount) projection.
        Vector2 ropeBoundsCenter;
        float ropeBoundsRadiusSq;

        // Owed Pop for ropeSurfaceMaterial — disabling the rope mid-attach must not leak it.
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

            // Generous margin so a rising slime trips the from-below test before the probe's clip range.
            ropeBoundsCenter = (a + b) * 0.5f;
            float boundR = ab.magnitude * 0.5f + baselineSag + ribbonThickness + 1f;
            ropeBoundsRadiusSq = boundR * boundR;

            lastAnchorAPos = anchorA.position;
            lastAnchorBPos = anchorB.position;
            lastSampleCount = sampleCount;
            lastBaselineSag = baselineSag;
            lastRibbonThickness = ribbonThickness;

            // Invalidate the SyncShapes cache — baseline moved under an unchanged shape key.
            syncedAtLeastOnce = false;

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
            bool onThisRope = (state == SlimeController.SlimeState.Attached
                            || state == SlimeController.SlimeState.Charging)
                            && polygon != null
                            && slime.SurfaceCollider == polygon;

            bool wasRiderActive = riderActive;   // value carried over from last FU
            if (onThisRope)
            {
                riderActive = true;
                // Never re-derive riderT from the snapped position: the slime parks at curve +
                // normal·radius, and re-projecting that offset shifts u toward the low anchor every
                // FU on a tilted rope (velocity-free drift). Seed on attach, then advance by
                // tangential velocity only — at rest riderT is frozen.
                if (!wasRiderActive)
                {
                    riderT = ProjectToChordParam(slime.Position);
                }
                else if (anchorA != null && anchorB != null)
                {
                    Vector2 chord = (Vector2)anchorB.position - (Vector2)anchorA.position;
                    float len = chord.magnitude;
                    if (len > 1e-4f)
                    {
                        float alongSpeed = Vector2.Dot(slime.Velocity, chord / len);
                        riderT = Mathf.Clamp01(riderT + alongSpeed * dt / len);
                    }
                }
                postDetach = false;   // re-attached; clear post-detach mode
            }
            else
            {
                riderActive = false;
            }

            // --- 2a. Proactive from-below passthrough (1-FU race) ---
            // Geometric registration each FU; the OnTrigger callback alone fires one FU too late.
            UpdateFromBelowPassthrough();

            // --- 2b. Surface override push/pop edges ---
            // Driven by riderActive edges, not trigger callbacks, so it stays in sync when
            // SlimeController detaches the slime by other means (friction-detach mid-rope).
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
            // No IgnoredCollider work here — Launch already registered our polygon as
            // ReattachGuard; adding Passthrough on top blocks the legitimate fall-back-and-land.
            if (wasOnRope && nowAirborne)
            {
                postDetach = true;
                // Window belonged to the rider that just left; must not carry into no-rider physics.
                trampolineHoldTimer = 0f;
            }

            // --- 3b. Tick trampoline damping window ---
            if (trampolineHoldTimer > 0f)
            {
                trampolineHoldTimer -= dt;
                if (trampolineHoldTimer < 0f) trampolineHoldTimer = 0f;
            }

            // --- 4. Compute spring target ---
            // Neither charging nor trampoline shifts the target — both are one-shot dipVelocity
            // impulses, so the spring rings around baseline and overshoots ABOVE it.
            float target = 0f;
            if (riderActive)
            {
                target = baseEquilibrium * ShapeFactor(riderT);
            }

            // --- 5. Integrate spring (semi-implicit Euler) ---
            // ζ regimes: postDetach overdamped so the rebound can't catch the launching slime,
            // trampoline window underdamped for visible bounce, else attached steady state.
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

        // Piecewise smoothstep bell peaking at riderT; C¹ at the peak, so no kink under the slime.
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

        // Parameter along the STRAIGHT chord. Unlike the perpendicular foot on the sagged
        // polyline, the slime's radius offset barely moves it — stable seed, no drift.
        float ProjectToChordParam(Vector2 worldPos)
        {
            if (anchorA == null || anchorB == null) return 0f;
            Vector2 a = anchorA.position;
            Vector2 ab = (Vector2)anchorB.position - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-8f) return 0f;
            return Mathf.Clamp01(Vector2.Dot(worldPos - a, ab) / len2);
        }

        // --- Polygon + LineRenderer sync -----------------------------------

        void SyncShapes()
        {
            if (baselinePoints == null) return;
            int n = baselinePoints.Length;

            // Runtime: skip when the shape key is unchanged. Editor preview falls through so
            // designer sag/thickness edits and gizmo refreshes always re-render.
            if (Application.isPlaying)
            {
                Vector2 current = new Vector2(dipDepth, riderT);
                if (syncedAtLeastOnce && current == lastSyncedDipDepth_riderT) return;
                lastSyncedDipDepth_riderT = current;
                syncedAtLeastOnce = true;
            }

            // Ribbon as one closed loop: top-left → top-right → bottom-right → bottom-left.
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

        // Rising along the normal AND either genuinely under the ribbon or rising steeply
        // (covers a jump that tunneled the thin ribbon in one FU). Tested on the up-FRACTION,
        // not raw Velocity.y: a fast near-horizontal slide-in must land, not fall through.
        bool IsRisingThroughFromBelow(float aboveDistance, float approachUp, float speed)
        {
            float belowGate = ribbonThickness + slime.SlimeRadius * 0.5f;
            return approachUp > 0f
                && (aboveDistance < -belowGate || approachUp > 0.5f * speed);
        }

        // OnTriggerEnter2D fires later in the SAME FU (inside Physics2D.Simulate), after
        // SlimeController (order 0) already zeroed the slime's upward speed against the rope's
        // ceiling contact — so its passthrough only lands one FU late and the slime sticks just
        // under the rope. Registering geometrically here trips while it is still well below.
        void UpdateFromBelowPassthrough()
        {
            if (slime == null || polygon == null || baselinePoints == null) return;
            if (riderActive) return;   // sitting on top — not a from-below pass

            // Cheap gates before the O(sampleCount) projection.
            float approachUp = Vector2.Dot(slime.Velocity, baselineNormal);
            if (approachUp <= 0f) { inPassthroughBelow = false; return; }
            if (((Vector2)slime.Position - ropeBoundsCenter).sqrMagnitude > ropeBoundsRadiusSq)
            { inPassthroughBelow = false; return; }

            float u = ProjectToBaselineParam(slime.Position);
            Vector2 topAtU = SampleCurveAt(u);
            float aboveDistance = Vector2.Dot((Vector2)slime.Position - topAtU, baselineNormal);

            if (IsRisingThroughFromBelow(aboveDistance, approachUp, slime.Velocity.magnitude))
            {
                slime.RegisterIgnoredCollider(polygon, passthroughRefreshDuration);
                inPassthroughBelow = true;
            }
            else if (inPassthroughBelow)
            {
                // No longer rising through — stop refreshing; passthrough times out, rope re-solidifies.
                inPassthroughBelow = false;
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (slime == null || polygon == null) return;
            var hitSlime = other.GetComponentInParent<SlimeController>();
            if (hitSlime != slime) return;
            if (baselinePoints == null) return;

            float u = ProjectToBaselineParam(slime.Position);
            Vector2 topAtU = SampleCurveAt(u);
            Vector2 toSlime = (Vector2)slime.Position - topAtU;
            float aboveDistance = Vector2.Dot(toSlime, baselineNormal);
            float approachUp = Vector2.Dot(slime.Velocity, baselineNormal);

            if (IsRisingThroughFromBelow(aboveDistance, approachUp, slime.Velocity.magnitude))
            {
                slime.RegisterIgnoredCollider(polygon, passthroughRefreshDuration);
                inPassthroughBelow = true;
                return;
            }

            // Anti-tunnel rescue for a landing slime that ended up deep inside the polygon:
            // SingleContactSurfaceProbe's deep-penetration skip makes ClosestPoint return the
            // query point, so the probe never sees the contact. Pull it back to top + radius.
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
            float u = ProjectToBaselineParam(worldPoint);
            return worldDown * (dipVelocity * Bell(u, riderT));
        }

        // riderActive comes from slime.SurfaceCollider in FixedUpdate, so this callback is ignored.
        public void ReportRiderContact(Collider2D _slimeCol, Vector2 _worldContact, float _mass, Vector2 _riderVel) { }

        public void NotifyFirstAttach(Vector2 worldContact)
        {
            postDetach = false;
            OnRopeEvent?.Invoke(RopeEventType.FirstAttach, worldContact);
        }

        public void NotifyLaunchRebound(Vector2 worldContact)
        {
            // Detach edge in FixedUpdate already handles settling; kept for external listeners.
            OnRopeEvent?.Invoke(RopeEventType.LaunchRebound, worldContact);
        }

        public void ApplyRiderImpact(Vector2 _worldContact, Vector2 impactVelocity)
        {
            float vDown = Mathf.Max(0f, Vector2.Dot(impactVelocity, worldDown));
            if (vDown <= 0f) return;

            // Extra per-(m/s) kick on top of the trampoline impulse; 0 = pure curve.
            if (impactCompressionFromVelocity > 0f)
                dipVelocity += vDown * impactCompressionFromVelocity;

            // Largest-wins on the hold window so near-simultaneous impacts compose into the bigger one.
            float kick = trampolineImpactKickCurve != null ? trampolineImpactKickCurve.Evaluate(vDown) : 0f;
            float hold = trampolineHoldCurve != null ? trampolineHoldCurve.Evaluate(vDown) : 0f;
            if (kick > 0f) dipVelocity += kick;
            if (hold > trampolineHoldTimer) trampolineHoldTimer = hold;
        }

        public float GetLaunchBonus(Vector2 launchDir, Vector2 _worldContact, float chargeRatio)
        {
            // Side effect: the kickoff enters as a one-shot downward dipVelocity pulse, so the
            // rope reads as punched at push-off instead of slowly sagging during the charge.
            if (launchImpactKickCurve != null)
            {
                float kick = launchImpactKickCurve.Evaluate(Mathf.Clamp01(chargeRatio));
                if (kick > 0f) dipVelocity += kick;
            }

            if (launchDir.sqrMagnitude < 1e-6f) return 0f;
            Vector2 dir = launchDir.normalized;
            float upward = Mathf.Max(0f, Vector2.Dot(dir, worldUp));
            // dipVelocity is measured along worldDown, so -dipVelocity is the rope's upward speed.
            float ropeUpSpeed = Mathf.Max(0f, -dipVelocity);
            float timing = jumpTimingCurve != null ? jumpTimingCurve.Evaluate(ropeUpSpeed) : 1f;
            float bonus = jumpBoost * Mathf.Clamp01(chargeRatio) * upward * Mathf.Max(0f, timing);
            float clamped = Mathf.Clamp(bonus, 0f, maxCap);
            // slime.Velocity here is the launch speed built so far but BEFORE the rope bonus, so a
            // high-fall EnergyConversion launch falls off to 0 and gains no extra height.
            float preBonusSpeed = slime != null ? slime.Velocity.magnitude : 0f;
            float speedMul = boostFalloffEndSpeed > boostFalloffStartSpeed
                ? 1f - Mathf.Clamp01(Mathf.InverseLerp(boostFalloffStartSpeed, boostFalloffEndSpeed, preBonusSpeed))
                : (preBonusSpeed >= boostFalloffStartSpeed ? 0f : 1f);
            return clamped * speedMul;
        }

        public Vector2 PostprocessLaunchVelocity(Vector2 launchVel)
        {
            // A near-horizontal aim otherwise drops onto the rope's rebound and gets re-caught.
            if (minLaunchVerticalSpeed > 0f)
            {
                float upSpeed = Vector2.Dot(launchVel, worldUp);
                if (upSpeed < minLaunchVerticalSpeed)
                    launchVel += worldUp * (minLaunchVerticalSpeed - upSpeed);
            }
            return launchVel;
        }

        public Vector2 ConsumeRiderCarryDelta(Vector2 _worldContact)
        {
            // Carry channel unused — TautLine writes rb.position absolutely each FU, so zero here
            // keeps SlimeController's carry-add branch a no-op for this rope.
            return Vector2.zero;
        }
    }
}
