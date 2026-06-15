using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Rectangular moving platform v2. Layers composable feel + behaviors on top of PlatformMotor:
    ///  - UI-style damped follow toward the waypoint target (laggier while ridden)
    ///  - Vertical sag spring: dip on mount ("흔들림"), recoil on leave ("반동")
    ///  - Edge tilt + ice override → an idle slime slides off, forced to keep jumping
    ///  - F1: mount-triggered trip to destination, hold while a slime stays in range, return + rearm
    ///  - F2: leader/partner pair converge to sandwich the slime, which extrudes out the open side
    ///
    /// Everything folds into ComputeMotion's single (pos,rot) return, so PlatformMotor keeps
    /// computing DeltaPosition/DeltaRotation and the existing PlatformCarrier rider-carry stays
    /// correct for free. No SlimeController changes — F2 escape uses public AddExternalVelocity.
    /// </summary>
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
        [Tooltip("The other half of the sandwich. Leave null for a solo platform.")]
        [SerializeField] WaypointPlatformV2 partner;
        [Tooltip("Leader = the platform the slime mounts. Mounting it also triggers the partner. Partner should have its own (opposite) waypoints.")]
        [SerializeField] bool isLeader;
        [Tooltip("How far past first touch (units) the plates press the slime before the decks stall. Sets the squash depth: gap stalls at 2R − this. ~0.45 for a 0.4-radius slime → ~55% squash.")]
        [SerializeField] float pinchPressDepth = 0.45f;
        [Tooltip("Max opening tilt (deg) each deck rotates toward the slime's side when squeezed. The two decks rotate opposite → a V that opens at the slime.")]
        [SerializeField] float openTiltMax = 18f;
        [SerializeField] float openTiltStiffness = 26f;
        [SerializeField] float openTiltDamping = 7f;

        public override SurfaceMaterial OverrideSurfaceMaterial => iceMaterial;

        /// <summary>This deck's solid collider — handed to the pinched slime so it can suppress
        /// Unity-solver depenetration against the plates while UpdatePinch owns its position.</summary>
        public Collider2D DeckCollider => box;

        // --- runtime ---
        Vector2[] worldWaypoints;
        Vector2 baseOrigin;
        float baseRotation;
        Phase phase = Phase.Parked;
        float pathParam;            // 0..(N-1) position along the polyline

        Vector2 rigidTarget;        // un-damped path target
        Vector2 smoothPos;          // damped actual position
        Vector2 followVel;          // SmoothDamp velocity ref

        float sagY, sagVel;
        float currentTilt, tiltVel, targetTilt;
        float lastOffsetSign = 1f;

        // Sandwich open tilt — leader drives both decks' targets; each springs its own.
        float sandwichOpen, sandwichOpenVel, sandwichOpenTarget;
        bool pinchStall;   // frozen while pressing the caught slime (leader sets it on both decks)

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
        bool triggeredByLeader;
        SlimeController cachedSlime;

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

            box = GetComponentInChildren<BoxCollider2D>();
            if (box != null)
            {
                Vector3 ls = box.transform.lossyScale;
                boxHalf = new Vector2(box.size.x * Mathf.Abs(ls.x) * 0.5f, box.size.y * Mathf.Abs(ls.y) * 0.5f);
            }

            holdFilter = new ContactFilter2D { useTriggers = true };
            holdFilter.SetLayerMask(riderLayers);
            holdFilter.useLayerMask = true;
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

        /// <summary>Called on the partner by the leader when a slime mounts the leader.</summary>
        public void TriggerFromLeader() => triggeredByLeader = true;

        /// <summary>Half-extent of this platform's box projected onto a world-space unit axis (rotation-aware).</summary>
        public float ExtentAlong(Vector2 worldAxisN)
        {
            Vector2 right = transform.right;
            Vector2 up = transform.up;
            return Mathf.Abs(boxHalf.x * Vector2.Dot(right, worldAxisN)) + Mathf.Abs(boxHalf.y * Vector2.Dot(up, worldAxisN));
        }

        /// <summary>Half-extent along a world axis using the deck's BASE (un-tilted) orientation.
        /// The scissor open-tilt is cosmetic (collision is suppressed during pinch), so pinch
        /// geometry must ignore it — else tilting a wide deck inflates its axial extent and crushes
        /// the measured gap shut while the deck is supposedly frozen.</summary>
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
            // Resolve rider state for this step — only a slime on the TOP face counts.
            bool influenced = riderInfluencedThisStep;
            riderInfluencedThisStep = false;
            riderOnTop = influenced && IsRiderOnTop();
            bool mountRisingEdge = riderOnTop && !prevRiderOnTop;

            UpdatePhase(mountRisingEdge);
            // Sandwich detection runs BEFORE the motion integration below so pinchStall is fresh
            // THIS frame — otherwise the decks coast ~2 extra frames before the freeze lands and
            // cross the slime. Leader-only; it also sets partner.pinchStall.
            if (isLeader && partner != null)
                UpdateSandwich();

            AdvanceRigidTarget(dt);

            // Damped follow (UI feel) — laggier while ridden. HARD-frozen while pinch-stalled:
            // freezing only pathParam (AdvanceRigidTarget) isn't enough — the SmoothDamp lag
            // residual (mountedSmoothTime) keeps coasting smoothPos toward its pre-stall target by
            // more than a 0.2-radius slime's diameter, so the plates cross and crush through. Hold
            // smoothPos in place so the plates truly stop on the slime.
            if (pinchStall)
            {
                followVel = Vector2.zero;
            }
            else
            {
                float smoothTime = riderOnTop ? mountedSmoothTime : followSmoothTime;
                smoothPos = Vector2.SmoothDamp(smoothPos, rigidTarget, ref followVel, Mathf.Max(0.0001f, smoothTime), Mathf.Infinity, dt);
            }

            // Vertical sag spring — dip on mount, recoil overshoot on leave. FROZEN while
            // pinch-stalled: once the slime detaches into the hold, sagTarget snaps to 0 and the
            // leader climbs ~sagDepth back up INTO the slime, closing the held gap shut. Hold sagY.
            if (!pinchStall)
            {
                float sagTarget = riderOnTop ? -sagDepth * Mathf.Max(0.01f, riderMass) : 0f;
                float sagAccel = (sagTarget - sagY) * sagStiffness - sagVel * sagDamping;
                sagVel += sagAccel * dt;
                sagY += sagVel * dt;
            }

            UpdateTilt(dt);

            // Spring the open tilt (both leader and partner do this on their own target).
            float openAccel = (sandwichOpenTarget - sandwichOpen) * openTiltStiffness - sandwichOpenVel * openTiltDamping;
            sandwichOpenVel += openAccel * dt;
            sandwichOpen += sandwichOpenVel * dt;

            prevRiderOnTop = riderOnTop;

            Vector2 outPos = smoothPos + new Vector2(0f, sagY);
            float outRot = baseRotation + (enableTilt ? currentTilt : 0f) + sandwichOpen;
            return (outPos, outRot);
        }

        void UpdatePhase(bool mountRisingEdge)
        {
            if (!mountTriggered || !HasPath)
                return; // stationary: stays parked at wp0, only tilt/sag/ice act

            int last = worldWaypoints.Length - 1;
            switch (phase)
            {
                case Phase.Parked:
                    if (mountRisingEdge || triggeredByLeader)
                    {
                        phase = Phase.Departing;
                        if (isLeader && partner != null) partner.TriggerFromLeader();
                    }
                    triggeredByLeader = false;
                    break;

                case Phase.Departing:
                    if (pathParam >= last - 1e-3f && Vector2.Distance(smoothPos, worldWaypoints[last]) <= arriveEpsilon)
                        phase = Phase.Holding;
                    break;

                case Phase.Holding:
                    if (!SlimeInHoldRange())
                        phase = Phase.Returning;
                    break;

                case Phase.Returning:
                    if (pathParam <= 1e-3f && Vector2.Distance(smoothPos, worldWaypoints[0]) <= arriveEpsilon)
                        phase = Phase.Parked;
                    break;
            }
        }

        void AdvanceRigidTarget(float dt)
        {
            int N = worldWaypoints.Length;
            if (N == 1) { rigidTarget = worldWaypoints[0]; return; }

            if (!mountTriggered) { rigidTarget = worldWaypoints[0]; return; }

            int dir = phase == Phase.Departing ? 1 : phase == Phase.Returning ? -1 : 0;
            if (pinchStall) dir = 0;   // frozen while pressing the caught slime

            if (dir != 0)
            {
                int legLo = Mathf.Clamp(Mathf.FloorToInt(dir > 0 ? pathParam : pathParam - 1e-4f), 0, N - 2);
                float legLen = Vector2.Distance(worldWaypoints[legLo], worldWaypoints[legLo + 1]);
                float dParam = legLen > 1e-4f ? (travelSpeed * dt) / legLen : 1f;
                pathParam = Mathf.Clamp(pathParam + dir * dParam, 0f, N - 1);
            }
            else if (!pinchStall)
            {
                pathParam = phase == Phase.Holding ? (N - 1) : 0f;
            }
            // pinchStall: leave pathParam frozen so the deck holds its current pressed position

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

                // Ice forced-jump: seed a small tilt toward the rider's side while it sits still
                // so dead-center stops being a stable resting spot. tilt sign is -side.
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

        // Leader-only. Detects the slime caught between the two plates, REPORTS the pinch geometry to
        // the slime (which then holds + extrudes it — see SlimeController.Pinch.cs), STALLS both decks
        // once pressed to depth, and TILTS them open toward the slime (scissor). The slime's sideways
        // motion is owned by the slime's pinch subsystem, not by collision squirt.
        void UpdateSandwich()
        {
            if (cachedSlime == null) cachedSlime = Object.FindFirstObjectByType<SlimeController>();

            bool caught = false;
            float openTarget = 0f, partnerOpenTarget = 0f;

            if (cachedSlime != null)
            {
                Vector2 toPartner = partner.rb.position - rb.position;
                if (toPartner.sqrMagnitude > 1e-6f)
                {
                    Vector2 axisN = toPartner.normalized;             // leader → partner
                    Vector2 tangent = new Vector2(-axisN.y, axisN.x); // extrude direction
                    Vector2 slimePos = cachedSlime.Position;
                    float R = cachedSlime.SlimeRadius;

                    float leaderFaceProj  = Vector2.Dot(rb.position, axisN) + NominalExtentAlong(axisN);
                    float partnerFaceProj = Vector2.Dot(partner.rb.position, axisN) - partner.NominalExtentAlong(axisN);
                    float gap = partnerFaceProj - leaderFaceProj;     // free space between the plate faces

                    float slimeAxisProj = Vector2.Dot(slimePos, axisN);
                    bool betweenAxis = slimeAxisProj > leaderFaceProj - R && slimeAxisProj < partnerFaceProj + R;

                    float halfSpan = Mathf.Min(NominalExtentAlong(tangent), partner.NominalExtentAlong(tangent));
                    float deckCenterTangent = Vector2.Dot((rb.position + partner.rb.position) * 0.5f, tangent);
                    float lateral = Vector2.Dot(slimePos, tangent) - deckCenterTangent;

                    bool wouldCatch = betweenAxis && Mathf.Abs(lateral) <= halfSpan && gap < 2f * R;
                    Debug.LogWarning($"[PinchDbg] #{Time.frameCount} phase={phase} pStall={pinchStall} gap={gap:F3} 2R={2f * R:F3} between={betweenAxis} lat={lateral:F3} half={halfSpan:F3} caught={wouldCatch} pinchActive={cachedSlime.PinchActive} comp={cachedSlime.PinchCompression01:F2} slimeAxis={slimeAxisProj:F2} LF={leaderFaceProj:F2} PF={partnerFaceProj:F2} leaderY={rb.position.y:F2} partnerY={partner.rb.position.y:F2} slimeState={cachedSlime.CurrentState} <<<PINCH");

                    if (betweenAxis && Mathf.Abs(lateral) <= halfSpan && gap < 2f * R)
                    {
                        caught = true;

                        // Gap-center world point at the DECK center (so release = slime reaches the edge).
                        float centerAxisProj = (leaderFaceProj + partnerFaceProj) * 0.5f;
                        Vector2 gapCenter = axisN * centerAxisProj + tangent * deckCenterTangent;
                        cachedSlime.SetPinch(axisN, tangent, gapCenter, gap, halfSpan, box, partner.DeckCollider);

                        // Stall both decks once pressed to depth so they don't plow further.
                        bool atDepth = gap <= 2f * R - pinchPressDepth;
                        pinchStall = atDepth;
                        partner.pinchStall = atDepth;

                        // Scissor tilt: open the V on the slime's EXTRUDE side (where it slides out)
                        // and pinch the far side, so the plates appear to squeeze it out. The slime
                        // extrudes toward sign(lateral)*tangent, so leader rotates +s and partner -s
                        // (opposite) to swing the gap open there. (Was negated → V opened the far side,
                        // plates bit down on the exit side → looked reversed.)
                        float comp01 = Mathf.Clamp01((2f * R - gap) / (2f * R));
                        float s = Mathf.Abs(lateral) > 0.02f ? Mathf.Sign(lateral) : lastOffsetSign;
                        float mag = openTiltMax * comp01;
                        openTarget = s * mag;
                        partnerOpenTarget = -s * mag;
                    }
                }
            }

            if (!caught)
            {
                pinchStall = false;
                partner.pinchStall = false;
            }

            sandwichOpenTarget = openTarget;
            partner.SetSandwichOpenTarget(partnerOpenTarget);
        }

        /// <summary>Leader→partner: sets this deck's open-tilt target (it springs toward it in ComputeMotion).</summary>
        public void SetSandwichOpenTarget(float t) => sandwichOpenTarget = t;

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
