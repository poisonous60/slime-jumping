using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    /// <summary>Rectangular moving platform v2 on PlatformMotor: damped follow, sag spring, edge tilt +
    /// ice override, F1 mount-triggered trip, F2 sandwich pair. All folded into ComputeMotion's single
    /// (pos,rot) return so PlatformCarrier rider-carry keeps working unchanged.</summary>
    [DisallowMultipleComponent]
    public class WaypointPlatformV2 : PlatformMotor
    {
        enum Phase { Parked, Departing, Holding, Returning }

        [Header("Path")]
        [Tooltip("Local-space path. [0] = park/start, [last] = destination. <2 entries (or mountTriggered off) = stationary (tilt/ice only).")]
        [SerializeField] Vector2[] localWaypoints = { Vector2.zero, new Vector2(4f, 0f) };
        [SerializeField] float travelSpeed = 3f;
        [SerializeField] AnimationCurve easeCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("Damped follow (UI feel)")]
        [Tooltip("SmoothDamp time when empty. Lower = snappier.")]
        [SerializeField] float followSmoothTime = 0.05f;
        [Tooltip("SmoothDamp time while a slime rides. Higher = laggier UI-spring feel.")]
        [SerializeField] float mountedSmoothTime = 0.18f;
        [Tooltip("Seconds to blend the follow smooth time between empty and mounted. SmoothDamp's steady-state lag is travelSpeed × smoothTime, so flipping the value instantly dumps that whole position debt in one settle — the deck lurches along the path on launch and stalls on landing. Blending pays it gradually.")]
        [SerializeField] float smoothTimeBlendTime = 0.5f;

        [Header("Sag spring (mount wobble / leave recoil)")]
        [Tooltip("Downward dip (units) per unit rider mass.")]
        [SerializeField] float sagDepth = 0.15f;
        [SerializeField] float sagStiffness = 140f;
        [Tooltip("Lower = more wobble/recoil. Under-damped when < 2*sqrt(stiffness).")]
        [SerializeField] float sagDamping = 8f;

        [Header("Tilt")]
        [SerializeField] bool enableTilt = true;
        [SerializeField] float maxTiltAngle = 14f;
        [SerializeField] float tiltStiffness = 22f;
        [SerializeField] float tiltDamping = 5f;
        [Tooltip("Ice forced-jump seed: extra tilt (deg) toward the rider's side while it sits still, so dead-center isn't a stable resting spot. The offset→tilt→ice-slide feedback takes over from there. 0 = off.")]
        [SerializeField] float idleTipBias = 6f;
        [Tooltip("Rider speed (m/s) below which the idle-tip seed engages.")]
        [SerializeField] float idleSpeedThreshold = 0.6f;

        [Header("Ice")]
        [Tooltip("SurfaceMaterial pushed to the slime while attached (assign Settings/SurfaceMaterials/Ice.asset). Null = keep host material.")]
        [SerializeField] SurfaceMaterial iceMaterial;

        [Header("Feature 1 — mount trigger + range hold")]
        [SerializeField] bool mountTriggered = true;
        [Tooltip("Radius around the destination. A slime within → hold; once none in range → return + rearm.")]
        [SerializeField] float holdRange = 2.5f;
        [Tooltip("Layers the hold-range proximity check looks for the slime on. Set to the slime's layer.")]
        [SerializeField] LayerMask riderLayers = ~0;
        [SerializeField] float arriveEpsilon = 0.05f;

        [Header("Feature 2 — sandwich pair")]
        [Tooltip("The other half of the sandwich. Mounting EITHER half departs both. Wire it on one deck — the other side is linked back automatically. Leave null for a solo platform.")]
        [SerializeField] WaypointPlatformV2 partner;
        [Tooltip("Which deck owns the pinch solve (and sits on the −axis side of the gap). Check exactly one of the pair. Does NOT decide which deck the slime may mount.")]
        [SerializeField] bool isLeader;
        [Tooltip("Gap (units) at which the squeezed slime fully stops the deck. The deck's CLOSING speed is scaled by (gap−this)/(2R−this): full at first touch (gap=2R), easing to 0 here as the incompressible slime resists harder. The slime collider shrinks to fit down to this gap, so there's no overlap/fling. ~0.12.")]
        [SerializeField] float pinchSqueezeGapMin = 0.12f;
        [Tooltip("Open-tilt TORQUE gain: angular accel (deg/s²) per unit (penetration × off-centre) — the asymmetric slime load tips each deck open toward the escape side. Reaction-driven, not a scripted scissor.")]
        [SerializeField] float openTiltTorqueGain = 600f;
        [Tooltip("Clamp (deg) on the reaction-driven open tilt.")]
        [SerializeField] float openTiltMax = 18f;
        [Tooltip("(DEAD — old freeze model) kept for .asset compat.")]
        [SerializeField] float pinchPressDepth = 0.45f;
        [SerializeField] float openTiltStiffness = 26f;
        [SerializeField] float openTiltDamping = 7f;

        public override SurfaceMaterial OverrideSurfaceMaterial => iceMaterial;

        // --- runtime ---
        Vector2[] worldWaypoints;
        Vector2 baseOrigin;
        float baseRotation;
        Phase phase = Phase.Parked;
        float pathParam;            // 0..(N-1) position along the polyline

        Vector2 rigidTarget;        // un-damped path target
        Vector2 smoothPos;          // damped actual position
        Vector2 followVel;          // SmoothDamp velocity ref
        float curSmoothTime;        // blended between followSmoothTime and mountedSmoothTime

        float sagY, sagVel;
        float currentTilt, tiltVel, targetTilt;
        float lastOffsetSign = 1f;

        // Open tilt = reaction torque (penetration × off-centre); sandwichOpenTarget stays 0.
        float sandwichOpen, sandwichOpenVel, sandwichOpenTarget;

        // Leader-computed squeeze, mirrored to partner via SetSandwichReaction. pinchReactionFactor
        // scales the deck's axial closing (1 = free → 0 at pinchSqueezeGapMin), a continuous
        // deceleration, not a hold flag. pinchOff = signed off-centre load → open-tilt torque.
        float pinchReactionFactor = 1f;
        float pinchOff;
        Vector2 pinchAxisN;        // world closing axis, leader→partner (same value on both decks)
        Vector2 prevSmoothPos;     // to scale this frame's axial closing by the reaction factor
        // Hard gap floor: rf-scaling alone can't stop the deck (rigidTarget over-travels ahead of the
        // lagging smoothPos, and Holding then pins pathParam to full-close), so each deck also clamps
        // its face to ≥ gapMin/2 from this shared gap-center.
        float pinchCenterAxisProj;

        // rider tracking — set by ApplyRiderInfluence (carrier, order -200),
        // consumed in ComputeMotion (motor, order -100) the same FixedUpdate.
        bool riderInfluencedThisStep;
        bool riderOnTop;       // attached AND standing on the top face
        bool prevRiderOnTop;
        Vector2 riderPos;
        float riderMass = 1f;
        Vector2 riderVel;

        BoxCollider2D box;
        Vector2 boxHalf = new Vector2(1f, 0.25f);
        bool triggeredByPartner;
        SlimeController cachedSlime;

        // Non-null (set by RegisterParentZone): the platform stays out until the zone calls
        // RequestReturn. Null = legacy holdRange proximity auto-return.
        WaypointReturnZone parentZone;

        ContactFilter2D holdFilter;
        readonly List<Collider2D> overlapBuf = new List<Collider2D>(8);

        protected override void Awake()
        {
            base.Awake();
            baseOrigin = rb.position;
            baseRotation = rb.rotation;

            int n = localWaypoints != null ? localWaypoints.Length : 0;
            worldWaypoints = new Vector2[Mathf.Max(n, 1)];
            if (n == 0) worldWaypoints[0] = rb.position;
            for (int i = 0; i < n; i++) worldWaypoints[i] = baseOrigin + localWaypoints[i];

            rigidTarget = worldWaypoints[0];
            smoothPos = worldWaypoints[0];
            prevSmoothPos = smoothPos;
            curSmoothTime = followSmoothTime;

            box = GetComponentInChildren<BoxCollider2D>();
            if (box != null)
            {
                Vector3 ls = box.transform.lossyScale;
                boxHalf = new Vector2(box.size.x * Mathf.Abs(ls.x) * 0.5f, box.size.y * Mathf.Abs(ls.y) * 0.5f);
            }

            // Symmetric link: authoring the pair on one deck links the other back.
            if (partner != null && partner.partner == null) partner.partner = this;

            holdFilter = new ContactFilter2D { useTriggers = true };
            holdFilter.SetLayerMask(riderLayers);
            holdFilter.useLayerMask = true;
        }

        // A deck deactivated mid-trip keeps phase/pathParam frozen, so the pair wakes a full leg out
        // of step and both decks end up stacked on the same side. Always re-park instead of resuming.
        protected override void OnEnable()
        {
            ResetToPark();
            base.OnEnable();
        }

        /// <summary>Snap back to the parked base pose and drop all in-flight trip / sandwich state.</summary>
        public void ResetToPark(bool propagateToPartner = true)
        {
            if (worldWaypoints == null) return;   // pre-Awake enable, nothing to reset yet

            phase = Phase.Parked;
            pathParam = 0f;
            triggeredByPartner = false;

            rigidTarget = worldWaypoints[0];
            smoothPos = worldWaypoints[0];
            prevSmoothPos = smoothPos;
            followVel = Vector2.zero;
            curSmoothTime = followSmoothTime;

            sagY = 0f; sagVel = 0f;
            currentTilt = 0f; tiltVel = 0f; targetTilt = 0f;
            sandwichOpen = 0f; sandwichOpenVel = 0f;
            pinchReactionFactor = 1f; pinchOff = 0f;

            riderInfluencedThisStep = false;
            riderOnTop = false;
            prevRiderOnTop = false;

            rb.position = worldWaypoints[0];
            rb.rotation = baseRotation;

            // The pair only makes sense in lockstep — re-park the partner from the same wake.
            if (propagateToPartner && partner != null) partner.ResetToPark(false);
        }

        bool HasPath => worldWaypoints != null && worldWaypoints.Length >= 2;

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            riderInfluencedThisStep = true;
            riderPos = riderWorldPos;
            riderMass = mass;
            riderVel = riderVelocity;
        }

        public override void RemoveRider()
        {
            riderInfluencedThisStep = false;
            riderOnTop = false;
        }

        /// <summary>Called on one half of the pair when a slime mounts the other. Parked-only, else the
        /// deck that just departed re-arms its own trip from the echo back.</summary>
        public void TriggerFromPartner()
        {
            if (phase == Phase.Parked) triggeredByPartner = true;
        }

        /// <summary>Half-extent of this platform's box projected onto a world-space unit axis (rotation-aware).</summary>
        public float ExtentAlong(Vector2 worldAxisN)
        {
            Vector2 right = transform.right;
            Vector2 up = transform.up;
            return Mathf.Abs(boxHalf.x * Vector2.Dot(right, worldAxisN)) + Mathf.Abs(boxHalf.y * Vector2.Dot(up, worldAxisN));
        }

        /// <summary>Half-extent along a world axis using the deck's BASE (un-tilted) orientation — the
        /// cosmetic open tilt would inflate a wide deck's axial extent and crush the measured gap
        /// shut while the deck is supposedly frozen.</summary>
        public float NominalExtentAlong(Vector2 worldAxisN)
        {
            float rad = baseRotation * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 right = new Vector2(c, s);
            Vector2 up = new Vector2(-s, c);
            return Mathf.Abs(boxHalf.x * Vector2.Dot(right, worldAxisN)) + Mathf.Abs(boxHalf.y * Vector2.Dot(up, worldAxisN));
        }

        // Rider X in the box's local space, normalized to [-1,1] across half-width (scale-invariant).
        float RiderLocalX()
        {
            if (box == null) return 0f;
            float lx = box.transform.InverseTransformPoint(riderPos).x;
            return lx / Mathf.Max(0.0001f, box.size.x * 0.5f);
        }

        // True only when the slime sits on the TOP face (upper half of the box, within the deck span).
        bool IsRiderOnTop()
        {
            if (box == null) return true; // no box to test against → allow
            Vector2 bl = box.transform.InverseTransformPoint(riderPos);
            float ry = bl.y / Mathf.Max(0.0001f, box.size.y * 0.5f);
            float rx = bl.x / Mathf.Max(0.0001f, box.size.x * 0.5f);
            return ry > 0f && Mathf.Abs(rx) <= 1.15f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            bool influenced = riderInfluencedThisStep;
            riderInfluencedThisStep = false;
            riderOnTop = influenced && IsRiderOnTop();
            bool mountRisingEdge = riderOnTop && !prevRiderOnTop;

            UpdatePhase(mountRisingEdge);
            // Must run BEFORE the motion integration below so the reaction is fresh this frame.
            if (isLeader && partner != null)
                UpdateSandwich();

            AdvanceRigidTarget(dt);

            // Smooth time is BLENDED, not flipped: SmoothDamp trails the target by travelSpeed ×
            // smoothTime, so a hard switch dumps that whole gap in one settle — lurch on launch,
            // hitch on landing.
            float targetSmoothTime = riderOnTop ? mountedSmoothTime : followSmoothTime;
            float blendRate = Mathf.Abs(mountedSmoothTime - followSmoothTime) / Mathf.Max(1e-4f, smoothTimeBlendTime);
            curSmoothTime = Mathf.MoveTowards(curSmoothTime, targetSmoothTime, blendRate * dt);
            smoothPos = Vector2.SmoothDamp(smoothPos, rigidTarget, ref followVel, Mathf.Max(0.0001f, curSmoothTime), Mathf.Infinity, dt);

            // Scale ONLY the closing component of this frame's axial move — opening and lateral must
            // stay untouched or the slime can't be released.
            if (pinchReactionFactor < 1f && pinchAxisN.sqrMagnitude > 0.5f)
            {
                float axialDelta = Vector2.Dot(smoothPos - prevSmoothPos, pinchAxisN);
                float closingDir = isLeader ? 1f : -1f;   // closing = +axisN (leader) / −axisN (partner)
                if (axialDelta * closingDir > 0f)
                    smoothPos += pinchAxisN * (axialDelta * (pinchReactionFactor - 1f));
            }
            // Clamp the STATE, not just the output: it keeps smoothPos short of wp[last] so the phase
            // never flips to Holding, which would pin pathParam to full-close.
            smoothPos = ClampPinchGap(smoothPos);
            prevSmoothPos = smoothPos;

            float sagTarget = riderOnTop ? -sagDepth * Mathf.Max(0.01f, riderMass) : 0f;
            float sagAccel = (sagTarget - sagY) * sagStiffness - sagVel * sagDamping;
            sagVel += sagAccel * dt;
            sagY += sagVel * dt;

            UpdateTilt(dt);

            // Squeeze hardness (1−reactionFactor) × off-centre load tips the deck open toward the
            // slime's escape side; the damped spring restores it.
            sandwichOpenVel += (isLeader ? 1f : -1f) * ((1f - pinchReactionFactor) * pinchOff * openTiltTorqueGain) * dt;
            float openAccel = (sandwichOpenTarget - sandwichOpen) * openTiltStiffness - sandwichOpenVel * openTiltDamping;
            sandwichOpenVel += openAccel * dt;
            sandwichOpen += sandwichOpenVel * dt;
            sandwichOpen = Mathf.Clamp(sandwichOpen, -openTiltMax, openTiltMax);

            prevRiderOnTop = riderOnTop;

            Vector2 outPos = ClampPinchGap(smoothPos + new Vector2(0f, sagY));   // re-clamp so sag can't sink past the floor
            float outRot = baseRotation + (enableTilt ? currentTilt : 0f) + sandwichOpen;
            return (outPos, outRot);
        }

        // Clamp this deck's leading face to ≥ gapMin/2 from the shared gap-center; both decks use the
        // same center, so the gap floors at exactly pinchSqueezeGapMin regardless of SmoothDamp lag.
        // Inert with no slime caught (factor == 1) or before the first catch (axis unset).
        Vector2 ClampPinchGap(Vector2 pos)
        {
            if (pinchReactionFactor >= 1f || pinchAxisN.sqrMagnitude < 0.5f)
                return pos;

            float extent = NominalExtentAlong(pinchAxisN);
            float half = 0.5f * pinchSqueezeGapMin;
            // Leader sits on the −axisN side of the center, partner on the +axisN side.
            if (isLeader)
            {
                float face = Vector2.Dot(pos, pinchAxisN) + extent;   // face toward the partner (+axisN side)
                float maxFace = pinchCenterAxisProj - half;
                if (face > maxFace) pos -= pinchAxisN * (face - maxFace);
            }
            else
            {
                float face = Vector2.Dot(pos, pinchAxisN) - extent;   // face toward the leader (−axisN side)
                float minFace = pinchCenterAxisProj + half;
                if (face < minFace) pos += pinchAxisN * (minFace - face);
            }
            return pos;
        }

        void UpdatePhase(bool mountRisingEdge)
        {
            if (!mountTriggered || !HasPath)
                return; // stationary: stays parked at wp0, only tilt/sag/ice act

            int last = worldWaypoints.Length - 1;
            switch (phase)
            {
                case Phase.Parked:
                    if (mountRisingEdge || triggeredByPartner)
                    {
                        phase = Phase.Departing;
                        if (partner != null) partner.TriggerFromPartner();
                    }
                    triggeredByPartner = false;
                    break;

                case Phase.Departing:
                    if (pathParam >= last - 1e-3f && Vector2.Distance(smoothPos, worldWaypoints[last]) <= arriveEpsilon)
                        phase = Phase.Holding;
                    break;

                case Phase.Holding:
                    // Mounting a deck that already sits at its destination runs the trip in reverse —
                    // for a sandwich pair that re-crosses the two decks without leaving the zone first.
                    if (mountRisingEdge)
                    {
                        phase = Phase.Returning;
                        if (partner != null) partner.RequestReturn();
                    }
                    // With a zone owner the return is commanded by RequestReturn; only the zone-less
                    // legacy platform may self-return on holdRange proximity.
                    else if (parentZone == null && !SlimeInHoldRange())
                        phase = Phase.Returning;
                    break;

                case Phase.Returning:
                    if (pathParam <= 1e-3f && Vector2.Distance(smoothPos, worldWaypoints[0]) <= arriveEpsilon)
                        phase = Phase.Parked;
                    break;
            }
        }

        /// <summary>Switches this platform from holdRange auto-return to zone-driven return.</summary>
        public void RegisterParentZone(WaypointReturnZone zone) => parentZone = zone;

        /// <summary>Zone → member: retract now. Only while out (Departing/Holding).</summary>
        public void RequestReturn()
        {
            if (phase == Phase.Departing || phase == Phase.Holding)
                phase = Phase.Returning;
        }

        void AdvanceRigidTarget(float dt)
        {
            int N = worldWaypoints.Length;
            if (N == 1) { rigidTarget = worldWaypoints[0]; return; }

            if (!mountTriggered) { rigidTarget = worldWaypoints[0]; return; }

            int dir = phase == Phase.Departing ? 1 : phase == Phase.Returning ? -1 : 0;

            if (dir != 0)
            {
                int legLo = Mathf.Clamp(Mathf.FloorToInt(dir > 0 ? pathParam : pathParam - 1e-4f), 0, N - 2);
                float legLen = Vector2.Distance(worldWaypoints[legLo], worldWaypoints[legLo + 1]);
                float dParam = legLen > 1e-4f ? (travelSpeed * dt) / legLen : 1f;
                // The TARGET advance must be scaled too, not just smoothPos's closing component —
                // otherwise SmoothDamp keeps chasing an over-travelled rigidTarget all the way shut.
                // Departing (dir > 0) is the closing direction for a sandwich pair.
                if (dir > 0 && pinchReactionFactor < 1f && pinchAxisN.sqrMagnitude > 0.5f)
                    dParam *= pinchReactionFactor;
                pathParam = Mathf.Clamp(pathParam + dir * dParam, 0f, N - 1);
            }
            else
            {
                pathParam = phase == Phase.Holding ? (N - 1) : 0f;
            }

            int i = Mathf.Clamp(Mathf.FloorToInt(pathParam), 0, N - 2);
            float t = Mathf.Clamp01(pathParam - i);
            rigidTarget = Vector2.Lerp(worldWaypoints[i], worldWaypoints[i + 1], easeCurve.Evaluate(t));
        }

        bool SlimeInHoldRange()
        {
            Vector2 dest = worldWaypoints[worldWaypoints.Length - 1];
            int count = Physics2D.OverlapCircle(dest, holdRange, holdFilter, overlapBuf);
            for (int k = 0; k < count; k++)
            {
                Collider2D c = overlapBuf[k];
                if (c != null && c.GetComponentInParent<SlimeController>() != null)
                    return true;
            }
            return false;
        }

        void UpdateTilt(float dt)
        {
            if (!enableTilt)
            {
                targetTilt = 0f; currentTilt = 0f; tiltVel = 0f;
                return;
            }

            if (riderOnTop)
            {
                float ratio = Mathf.Clamp(RiderLocalX(), -1f, 1f);
                targetTilt = Mathf.Clamp(-ratio * maxTiltAngle * Mathf.Max(0.01f, riderMass), -maxTiltAngle, maxTiltAngle);

                if (Mathf.Abs(ratio) > 0.05f) lastOffsetSign = Mathf.Sign(ratio);

                // Seed a tilt toward the idle rider's side so dead-center isn't a stable resting spot.
                if (idleTipBias > 0f && riderVel.magnitude < idleSpeedThreshold)
                {
                    float tipSign = Mathf.Abs(ratio) > 0.05f ? Mathf.Sign(ratio) : lastOffsetSign;
                    targetTilt = Mathf.Clamp(targetTilt - tipSign * idleTipBias, -maxTiltAngle, maxTiltAngle);
                }
            }
            else
            {
                targetTilt = 0f;
            }

            float accel = (targetTilt - currentTilt) * tiltStiffness - tiltVel * tiltDamping;
            tiltVel += accel * dt;
            currentTilt += tiltVel * dt;
        }

        // Leader-only. Reports the pinch geometry + each plate's contact normal to the slime (see
        // SlimeController.Pinch.cs) and feeds the reaction to both decks.
        void UpdateSandwich()
        {
            if (cachedSlime == null) cachedSlime = Object.FindFirstObjectByType<SlimeController>();

            bool caught = false;

            if (cachedSlime != null)
            {
                Vector2 toPartner = partner.rb.position - rb.position;
                if (toPartner.sqrMagnitude > 1e-6f)
                {
                    Vector2 axisN = toPartner.normalized;             // leader → partner
                    Vector2 tangent = new Vector2(-axisN.y, axisN.x); // extrude direction
                    Vector2 slimePos = cachedSlime.Position;
                    // FULL (unshrunk) radius: the collider shrinks to fit the gap mid-squeeze, which
                    // would otherwise make the catch/compression drift.
                    float fullR = cachedSlime.SlimeRadius / Mathf.Max(0.01f, cachedSlime.BodyScaleRatio);

                    float leaderFaceProj  = Vector2.Dot(rb.position, axisN) + NominalExtentAlong(axisN);
                    float partnerFaceProj = Vector2.Dot(partner.rb.position, axisN) - partner.NominalExtentAlong(axisN);
                    float gap = partnerFaceProj - leaderFaceProj;     // free space between the plate faces

                    float slimeAxisProj = Vector2.Dot(slimePos, axisN);
                    bool betweenAxis = slimeAxisProj > leaderFaceProj - fullR && slimeAxisProj < partnerFaceProj + fullR;

                    float halfSpan = Mathf.Min(NominalExtentAlong(tangent), partner.NominalExtentAlong(tangent));
                    float deckCenterTangent = Vector2.Dot((rb.position + partner.rb.position) * 0.5f, tangent);
                    float lateral = Vector2.Dot(slimePos, tangent) - deckCenterTangent;

                    if (betweenAxis && Mathf.Abs(lateral) <= halfSpan && gap < 2f * fullR)
                    {
                        caught = true;
                        float comp01 = Mathf.Clamp01((2f * fullR - gap) / (2f * fullR));
                        float off = Mathf.Clamp(lateral / Mathf.Max(1e-4f, halfSpan), -1f, 1f);
                        if (Mathf.Abs(off) > 0.05f) lastOffsetSign = Mathf.Sign(off);

                        // Gap-center at the DECK center — reported for the squeeze-out tangent/sign.
                        float centerAxisProj = (leaderFaceProj + partnerFaceProj) * 0.5f;
                        Vector2 gapCenter = axisN * centerAxisProj + tangent * deckCenterTangent;
                        cachedSlime.SetPinch(axisN, tangent, gapCenter, gap, halfSpan);

                        // Each plate's TRUE (tilted) face normal toward the slime — the visual flattens
                        // against the real contact face (see SlimeDeform.hlsl 3.5).
                        Vector2 leaderN = transform.up;
                        if (Vector2.Dot(leaderN, slimePos - (Vector2)rb.position) < 0f) leaderN = -leaderN;
                        Vector2 partnerN = partner.transform.up;
                        if (Vector2.Dot(partnerN, slimePos - (Vector2)partner.rb.position) < 0f) partnerN = -partnerN;
                        cachedSlime.AddPinchContact(leaderN, comp01);
                        cachedSlime.AddPinchContact(partnerN, comp01);

                        // Geometry-derived reaction factor: 1 = free → 0 = stopped at gapMin.
                        float rf = Mathf.Clamp01((gap - pinchSqueezeGapMin) / Mathf.Max(1e-4f, 2f * fullR - pinchSqueezeGapMin));
                        pinchReactionFactor = rf; pinchOff = off; pinchAxisN = axisN;
                        pinchCenterAxisProj = centerAxisProj;   // shared gap-center for the hard floor clamp
                        partner.SetSandwichReaction(rf, off, axisN, centerAxisProj);
                    }
                }
            }

            if (!caught)
            {
                pinchReactionFactor = 1f; pinchOff = 0f;
                partner.SetSandwichReaction(1f, 0f, pinchAxisN, pinchCenterAxisProj);
            }
        }

        /// <summary>Leader→partner: mirrors the squeeze state so the partner decelerates, tilts and
        /// clamps symmetrically in its own ComputeMotion.</summary>
        public void SetSandwichReaction(float reactionFactor, float off, Vector2 axisN, float centerAxisProj)
        {
            pinchReactionFactor = reactionFactor;
            pinchOff = off;
            pinchAxisN = axisN;
            pinchCenterAxisProj = centerAxisProj;
        }

        void OnDrawGizmos()
        {
            Vector2 origin = Application.isPlaying ? baseOrigin : (Vector2)transform.position;
            int n = localWaypoints != null ? localWaypoints.Length : 0;

            for (int i = 0; i < n; i++)
            {
                Vector2 wp = Application.isPlaying && worldWaypoints != null && i < worldWaypoints.Length
                    ? worldWaypoints[i]
                    : origin + localWaypoints[i];
                Gizmos.color = i == 0 ? Color.green : (i == n - 1 ? Color.red : Color.cyan);
                Gizmos.DrawSphere(wp, 0.15f);
                if (i > 0)
                {
                    Vector2 prev = Application.isPlaying && worldWaypoints != null && i - 1 < worldWaypoints.Length
                        ? worldWaypoints[i - 1]
                        : origin + localWaypoints[i - 1];
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(prev, wp);
                }
            }

            if (n >= 2)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
                Vector2 dest = Application.isPlaying && worldWaypoints != null
                    ? worldWaypoints[worldWaypoints.Length - 1]
                    : origin + localWaypoints[n - 1];
                Gizmos.DrawWireSphere(dest, holdRange);
            }

            if (partner != null)
            {
                Gizmos.color = isLeader ? Color.magenta : new Color(1f, 0f, 1f, 0.4f);
                Gizmos.DrawLine(transform.position, partner.transform.position);
            }
        }
    }
}
