using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using SlimeJumping.Audio;
using SlimeJumping.Effects;
using SlimeJumping.Platforms;

namespace SlimeJumping.Mechanisms
{
    /// One-shot falling train. Sequential connector failure → broken-hinge sag →
    /// silence beat → physics-driven fall (Rigidbody2D Dynamic) → first-ground-contact
    /// hit-stop → settle → permanent Static shortcut platform.
    ///
    /// Inherits <see cref="PlatformMotor"/>. The slime rides the train via
    /// PlatformCarrier through every phase, including the physics-driven fall —
    /// see the Velocity/AngularVelocity property overrides which forward to
    /// rb.linearVelocity/rb.angularVelocity once the body is Dynamic.
    ///
    /// Designer iteration workflow: adjust Train Transform.position to set the
    /// fall START. Physics determines the END. Console logs the settled pose.
    /// Use [ContextMenu] "DEBUG: Trigger Fall Now" to skip telegraph during testing.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Mechanisms/Falling Train Platform")]
    public class FallingTrainPlatform : PlatformMotor
    {
        private enum TrainState { Idle, Telegraphing, Falling, HitStop, Settling, Landed }

        [Serializable]
        public class Connector
        {
            public Transform anchor;
            [Tooltip("Particle system prefab — instantiated and Played at the anchor on snap; auto-destroyed.")]
            public ParticleSystem snapVFXPrefab;
            [Tooltip("Sharp crack/snap one-shot for this connector.")]
            public AudioClip snapSound;
            [Tooltip("Wait this long after the previous snap (or after Telegraph entry for the first one) before this connector breaks.")]
            public float preDelay = 1.0f;
            [Tooltip("Degrees the train sags after this connector snaps. Pivot is automatically the centroid of the *remaining* connectors. Direction is auto-signed so the snapped side drops.")]
            public float sagDegrees = 4f;
            [Tooltip("Time over which the sag transition plays out (ease-out).")]
            public float sagDuration = 0.5f;
            [Tooltip("Cinemachine impulse force when this connector snaps. 0 = no shake.")]
            public float joltForce = 0.3f;
        }

        [Header("Rider Detection")]
        [Tooltip("Trigger that counts riders. Triggering once is enough — the train never re-arms.")]
        [SerializeField] private PlatformRiderSensor riderSensor;

        [Header("Telegraph (sequential connector failure)")]
        [Tooltip("Connectors snap in list order. Each contributes its preDelay + sagDuration to the total telegraph time.")]
        [SerializeField] private List<Connector> connectors = new List<Connector>();
        [Tooltip("Sag transition shape. Default ease-out (snap fast, settle).")]
        [SerializeField] private AnimationCurve sagCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 4f), new Keyframe(1f, 1f, 0f, 0f));
        [Tooltip("Pure stillness after the last connector snaps, before the fall begins. The audio-leading silence beat — most important single frame for selling collapse.")]
        [SerializeField] private float silenceBeforeFall = 0.2f;
        [Tooltip("If true, rider exiting the sensor mid-telegraph short-circuits to Falling immediately (any remaining snaps are skipped).")]
        [SerializeField] private bool fallOnRiderExit = true;
        [Tooltip("Looping creak/groan during telegraph. Auto-stopped silenceBeforeFall before drop.")]
        [SerializeField] private AudioSource creakSource;
        [Tooltip("Shared Cinemachine impulse for connector snaps. Per-connector joltForce is the magnitude.")]
        [SerializeField] private ImpulseEmitter snapImpulse;

        [Header("Fall Physics")]
        [Tooltip("Rigidbody mass when Dynamic. Heavy = slime can't shove it on contact.")]
        [SerializeField] private float fallMass = 200f;
        [Tooltip("Gravity multiplier during fall. >1 = snappier fall in a small camera frame.")]
        [SerializeField] private float fallGravityScale = 2.0f;
        [SerializeField] private float fallLinearDamping = 0.05f;
        [Tooltip("Higher = stops spinning faster after impact. The main 'doesn't spin forever' knob.")]
        [SerializeField] private float fallAngularDamping = 0.6f;
        [Tooltip("Use Continuous collision detection during fall — heavy bodies on thin terrain otherwise tunnel.")]
        [SerializeField] private bool useContinuousCollision = true;
        [Tooltip("Optional. friction 0.7 / bounciness 0 recommended for 'lands and stops'. Null = Unity defaults (slippery).")]
        [SerializeField] private PhysicsMaterial2D fallPhysicsMaterial;

        [Header("Release Impulse (optional tumble seed)")]
        [Tooltip("Linear impulse applied at the LAST snapped connector position (off-center → free angular momentum). Default 0 = pure gravity acceleration from rest. Set a small downward value if you want the train to look 'pushed off the rails' rather than just released.")]
        [SerializeField] private Vector2 releaseImpulse = Vector2.zero;
        [Tooltip("Additional angular velocity added at release (deg/s). Positive = CCW. Default 0 — telegraph sag already provides the asymmetric pose that lets gravity tumble it naturally.")]
        [SerializeField] private float releaseAngularVelocity = 0f;

        [Header("Impact Detection")]
        [Tooltip("Layers that count as 'ground' for first-contact hit-stop trigger. Should EXCLUDE the slime layer for safety, but the speed/time guards below also catch the case.")]
        [SerializeField] private LayerMask groundLayers = ~0;
        [Tooltip("Minimum downward speed (units/s) required to trigger hit-stop. Filters out the slime-resting-on-top contact that re-evaluates the moment the train becomes Dynamic.")]
        [SerializeField] private float impactMinSpeed = 2f;
        [Tooltip("Minimum time after EnterFalling before any collision can trigger hit-stop. Belt-and-suspenders against slime contact firing on the first physics step.")]
        [SerializeField] private float impactMinFallTime = 0.08f;

        [Header("Settlement")]
        [Tooltip("Linear velocity below this magnitude (units/s) counts as 'at rest'. The rest-snap below makes this almost trivial to satisfy.")]
        [SerializeField] private float settledLinearThreshold = 0.1f;
        [Tooltip("Angular velocity below this magnitude (deg/s) counts as 'at rest'.")]
        [SerializeField] private float settledAngularThreshold = 8f;
        [Tooltip("Must stay below thresholds for this long before transitioning to Landed.")]
        [SerializeField] private float settledHoldTime = 0.2f;
        [Tooltip("Force Landed if Falling+Settling takes longer than this (safety against infinite spin / fell-into-pit).")]
        [SerializeField] private float settleSafetyTimeout = 10f;

        [Header("Rest-Snap (kill micro-tremble while preserving bounces)")]
        [Tooltip("Linear speed (units/s) below this counts as 'low'. Snap only fires when both linear AND angular are low while in stable ground contact (see restSnapStableTime).")]
        [SerializeField] private float restSnapLinearSpeed = 0.6f;
        [Tooltip("Angular speed (deg/s) below this counts as 'low'.")]
        [SerializeField] private float restSnapAngularSpeed = 30f;
        [Tooltip("Both speeds must stay below thresholds AND ground contact must persist for this long before snap fires. Prevents transient bounce-contact from killing in-air angular velocity during a tumble.")]
        [SerializeField] private float restSnapStableTime = 0.15f;

        [Header("Debris (simulated trail)")]
        [Tooltip("Small Rigidbody2D prefab spawned at fall start. Convinces the eye the main object is also physical. Skip if null.")]
        [SerializeField] private GameObject debrisPrefab;
        [SerializeField] private int debrisCount = 8;
        [Tooltip("Initial velocity range (min..max magnitude) applied in random direction (mostly outward+down).")]
        [SerializeField] private Vector2 debrisSpeedRange = new Vector2(2f, 6f);
        [SerializeField] private float debrisLifetime = 4f;
        [Tooltip("Random spawn jitter from connector anchors (world units).")]
        [SerializeField] private float debrisSpawnJitter = 0.3f;

        [Header("Hit-Stop (impact freeze)")]
        [Tooltip("Real-time duration of the global timeScale dip on impact. 80–120ms is the standard punch range.")]
        [SerializeField] private float hitStopDuration = 0.08f;
        [Tooltip("Time.timeScale during hit-stop. ~0.05 = near-freeze.")]
        [Range(0.01f, 1f)][SerializeField] private float hitStopTimeScale = 0.05f;
        [Tooltip("Velocity remaining after hit-stop release (0–1). Higher = visible bounce/slide after impact. Tremble from this is killed by the rest-snap below.")]
        [Range(0f, 1f)][SerializeField] private float postHitStopVelocityRetention = 0.4f;
        [Range(0f, 1f)][SerializeField] private float postHitStopAngularRetention = 0.6f;

        [Header("Impact (3-layer SFX)")]
        [Tooltip("Sharp initial crack — first 100ms of impact.")]
        [SerializeField] private AudioClip crackSound;
        [Tooltip("Sustained metallic groan — 1–2s tail.")]
        [SerializeField] private AudioClip groanSound;
        [Tooltip("Long dust-settle hiss — 3–4s tail. Played slightly delayed.")]
        [SerializeField] private AudioClip dustSettleSound;
        [SerializeField] private float dustSettleDelay = 0.15f;
        [Range(0f, 1f)][SerializeField] private float impactVolume = 1f;
        [SerializeField] private ImpulseEmitter impactImpulse;
        [SerializeField] private float impactImpulseForce = 1.0f;
        [Tooltip("Spawned at the contact point on impact (dust/debris cloud). Auto-destroyed after impactVFXLifetime.")]
        [SerializeField] private GameObject impactVFXPrefab;
        [SerializeField] private float impactVFXLifetime = 4f;

        [Header("Events")]
        [Tooltip("Fired once at the moment Falling begins (after the silence beat).")]
        public UnityEvent onFallStart;
        [Tooltip("Fired once at the moment of first ground impact (before hit-stop release).")]
        public UnityEvent onImpact;
        [Tooltip("Fired once when the train comes to rest and switches to Static.")]
        public UnityEvent onLanded;

        // --- runtime state ---
        private TrainState state = TrainState.Idle;
        private bool hasFiredOnce;
        private Vector2 spawnPos;
        private float spawnRot;

        // sag pose: composed by snaps; ComputeMotion returns this during Telegraphing
        private Vector2 currentSagPos;
        private float currentSagRot;
        private Vector2 sagAnimStartPos, sagAnimTargetPos;
        private float sagAnimStartRot, sagAnimTargetRot;
        private float sagAnimTimer, sagAnimDuration;
        private bool sagging;

        // connector schedule
        private float telegraphElapsed;
        private int nextSnapIdx;
        private float nextSnapTime;
        private float lastSnapFinishTime; // when the last snap's sag finishes

        // cached at Awake from anchor.localPosition for stable pivot math
        private List<Vector2> connectorLocalPositions = new List<Vector2>();

        // physics fall
        private bool hasFiredImpact;
        private float fallElapsed;
        private float settledTimer;
        private float restSnapStableTimer;
        private Vector2 hitStopSavedVelocity;
        private float hitStopSavedAngular;
        private Vector2 lastSnappedConnectorWorldPos;

        // diagnostic — last settled pose, exposed to ContextMenu marker helper
        private Vector2 lastSettledPos;
        private float lastSettledRot;
        private bool hasLastSettled;

        // --- PlatformCarrier coupling: forward physics velocity once Dynamic ---
        // base.Velocity / base.AngularVelocity are computed from MovePosition deltas,
        // which are no-ops on Dynamic bodies. PlatformCarrier reads these properties
        // each FixedUpdate to inject velocity into the slime — so during physics
        // phases we forward the actual rb velocity, otherwise the slime would freeze
        // mid-air while the train physics-falls without it.
        public override Vector2 Velocity =>
            (state == TrainState.Falling || state == TrainState.Settling)
                ? rb.linearVelocity
                : base.Velocity;

        public override float AngularVelocity =>
            (state == TrainState.Falling || state == TrainState.Settling)
                ? rb.angularVelocity
                : base.AngularVelocity;

        protected override void Awake()
        {
            base.Awake();
            // base sets Interpolate, but assert in case a subclass/serialized value undid it.
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            spawnPos = rb.position;
            spawnRot = rb.rotation;
            currentSagPos = spawnPos;
            currentSagRot = spawnRot;

            connectorLocalPositions.Clear();
            for (int i = 0; i < connectors.Count; i++)
            {
                var c = connectors[i];
                connectorLocalPositions.Add(
                    c != null && c.anchor != null
                        ? (Vector2)transform.InverseTransformPoint(c.anchor.position)
                        : Vector2.zero);
            }
        }

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            switch (state)
            {
                case TrainState.Idle:
                    if (!hasFiredOnce && riderSensor != null && riderSensor.RiderCount > 0)
                        EnterTelegraphing();
                    return (spawnPos, spawnRot);

                case TrainState.Telegraphing:
                    return TickTelegraphing(dt);

                case TrainState.Falling:
                    fallElapsed += dt;
                    CheckSettleSafetyTimeout();
                    // Passive — physics drives the body. Returning rb.position keeps
                    // PlatformMotor's prevPosition tracking in sync (no spurious deltas).
                    return (rb.position, rb.rotation);

                case TrainState.HitStop:
                    // Held by Kinematic with zero velocity. Return current pose so base
                    // FixedUpdate's MovePosition is a no-op (target == current).
                    return (rb.position, rb.rotation);

                case TrainState.Settling:
                    fallElapsed += dt;
                    TickSettling(dt);
                    CheckSettleSafetyTimeout();
                    return (rb.position, rb.rotation);

                case TrainState.Landed:
                    return (rb.position, rb.rotation);
            }
            return (rb.position, rb.rotation);
        }

        // --- Telegraphing (sequential snaps + sag) ---

        private (Vector2, float) TickTelegraphing(float dt)
        {
            telegraphElapsed += dt;

            // Rider exit short-circuit.
            if (fallOnRiderExit && riderSensor != null && riderSensor.RiderCount == 0)
            {
                EnterFalling();
                return (rb.position, rb.rotation);
            }

            // Fire the next snap if it's time.
            if (nextSnapIdx < connectors.Count && telegraphElapsed >= nextSnapTime)
            {
                SnapConnector(nextSnapIdx);
                nextSnapIdx++;
                if (nextSnapIdx < connectors.Count)
                {
                    var prev = connectors[nextSnapIdx - 1];
                    nextSnapTime = telegraphElapsed + prev.sagDuration + connectors[nextSnapIdx].preDelay;
                }
                else
                {
                    var prev = connectors[nextSnapIdx - 1];
                    lastSnapFinishTime = telegraphElapsed + prev.sagDuration;
                }
            }

            // Drive active sag transition.
            if (sagging)
            {
                sagAnimTimer += dt;
                float t = sagAnimDuration > 0f ? Mathf.Clamp01(sagAnimTimer / sagAnimDuration) : 1f;
                float eased = sagCurve != null ? sagCurve.Evaluate(t) : t;
                currentSagPos = Vector2.Lerp(sagAnimStartPos, sagAnimTargetPos, eased);
                currentSagRot = Mathf.Lerp(sagAnimStartRot, sagAnimTargetRot, eased);
                if (t >= 1f)
                {
                    currentSagPos = sagAnimTargetPos;
                    currentSagRot = sagAnimTargetRot;
                    sagging = false;
                }
            }

            // Audio-leading silence: cut creak ~silenceBeforeFall before the drop.
            if (creakSource != null && creakSource.isPlaying
                && nextSnapIdx >= connectors.Count
                && telegraphElapsed >= lastSnapFinishTime)
            {
                creakSource.Stop();
            }

            // All snaps done + silence beat elapsed → fall.
            if (nextSnapIdx >= connectors.Count
                && telegraphElapsed >= lastSnapFinishTime + silenceBeforeFall)
            {
                EnterFalling();
                return (rb.position, rb.rotation);
            }

            return (currentSagPos, currentSagRot);
        }

        private void SnapConnector(int i)
        {
            var c = connectors[i];
            if (c == null) return;

            // Pivot = centroid of REMAINING (still-attached) connectors, in current train pose.
            Vector2 pivotWorld;
            bool hasPivot = TryGetRemainingPivotWorld(i, out pivotWorld);

            // Capture the snapped anchor's CURRENT world position — used by EnterFalling
            // as the off-center impulse application point for natural tumble seed.
            lastSnappedConnectorWorldPos = TrainLocalToWorld(connectorLocalPositions[i]);

            if (hasPivot)
            {
                // Auto-sign: snapped side should drop.
                float side = lastSnappedConnectorWorldPos.x - pivotWorld.x;
                float signedSag = c.sagDegrees * (side < 0f ? 1f : -1f);

                sagAnimStartPos = currentSagPos;
                sagAnimStartRot = currentSagRot;
                sagAnimTargetPos = RotateAroundPivot(currentSagPos, pivotWorld, signedSag);
                sagAnimTargetRot = currentSagRot + signedSag;
                sagAnimTimer = 0f;
                sagAnimDuration = c.sagDuration;
                sagging = true;
            }

            // Snap effects.
            if (c.snapSound != null)
                SoundService.PlayAt(c.snapSound, c.anchor != null ? c.anchor.position : (Vector3)currentSagPos,
                                    SoundCategory.SFX_Object, impactVolume);
            if (snapImpulse != null && c.joltForce > 0f) snapImpulse.Emit(c.joltForce);
            if (c.snapVFXPrefab != null && c.anchor != null)
            {
                var ps = Instantiate(c.snapVFXPrefab, c.anchor.position, c.anchor.rotation);
                ps.Play(true);
                var main = ps.main;
                Destroy(ps.gameObject, main.duration + main.startLifetime.constantMax + 1f);
            }
        }

        private bool TryGetRemainingPivotWorld(int snappedIdx, out Vector2 pivotWorld)
        {
            int n = 0;
            Vector2 sumLocal = Vector2.zero;
            for (int j = snappedIdx + 1; j < connectorLocalPositions.Count; j++)
            {
                sumLocal += connectorLocalPositions[j];
                n++;
            }
            if (n == 0) { pivotWorld = currentSagPos; return false; }
            Vector2 avgLocal = sumLocal / n;
            pivotWorld = TrainLocalToWorld(avgLocal);
            return true;
        }

        private Vector2 TrainLocalToWorld(Vector2 local)
        {
            return currentSagPos + Rotate2D(local, currentSagRot);
        }

        private static Vector2 RotateAroundPivot(Vector2 point, Vector2 pivot, float deg)
        {
            return pivot + Rotate2D(point - pivot, deg);
        }

        private static Vector2 Rotate2D(Vector2 v, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // --- Settling tick ---

        private void TickSettling(float dt)
        {
            // Rest-snap: kill micro-tremble at *final rest*, NOT during a tumble's
            // glancing contacts. Snap requires:
            //   (a) train is touching ground
            //   (b) BOTH linear AND angular below thresholds
            //   (c) (a)+(b) held continuously for restSnapStableTime
            // A tumbling train that briefly grazes ground while spinning fast won't
            // satisfy (b), so angular velocity is preserved through the air phase.
            bool touching = rb.IsTouchingLayers(groundLayers);
            bool linearLow = rb.linearVelocity.sqrMagnitude < restSnapLinearSpeed * restSnapLinearSpeed;
            bool angularLow = Mathf.Abs(rb.angularVelocity) < restSnapAngularSpeed;

            if (touching && linearLow && angularLow)
                restSnapStableTimer += dt;
            else
                restSnapStableTimer = 0f;

            if (restSnapStableTimer >= restSnapStableTime)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
            }

            bool atRest =
                rb.linearVelocity.sqrMagnitude < settledLinearThreshold * settledLinearThreshold
                && Mathf.Abs(rb.angularVelocity) < settledAngularThreshold;

            if (atRest) settledTimer += dt;
            else        settledTimer = 0f;

            // Box2D may also flag the body as sleeping — short-circuit to Landed.
            if (settledTimer >= settledHoldTime || rb.IsSleeping())
                EnterLanded();
        }

        private void CheckSettleSafetyTimeout()
        {
            if (fallElapsed > settleSafetyTimeout && state != TrainState.Landed)
            {
                Debug.LogWarning($"[FallingTrainPlatform] Settle safety timeout ({settleSafetyTimeout}s) — forcing Landed at current pose. " +
                                 "Check that ground colliders exist below the fall path, or extend settleSafetyTimeout.", this);
                EnterLanded();
            }
        }

        // --- Transitions ---

        private void EnterTelegraphing()
        {
            state = TrainState.Telegraphing;
            hasFiredOnce = true;
            telegraphElapsed = 0f;
            nextSnapIdx = 0;
            nextSnapTime = connectors.Count > 0 ? Mathf.Max(0f, connectors[0].preDelay) : 0f;
            lastSnapFinishTime = 0f;
            sagging = false;
            currentSagPos = spawnPos;
            currentSagRot = spawnRot;

            if (creakSource != null && creakSource.clip != null && !creakSource.isPlaying)
            {
                creakSource.loop = true;
                creakSource.Play();
            }
        }

        private void EnterFalling()
        {
            state = TrainState.Falling;
            hasFiredOnce = true;
            fallElapsed = 0f;
            settledTimer = 0f;
            hasFiredImpact = false;

            if (creakSource != null && creakSource.isPlaying) creakSource.Stop();
            if (riderSensor != null) riderSensor.gameObject.SetActive(false);

            // Swap to Dynamic. Per Unity docs, contacts re-evaluate next FixedUpdate.
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.mass = fallMass;
            rb.gravityScale = fallGravityScale;
            rb.linearDamping = fallLinearDamping;
            rb.angularDamping = fallAngularDamping;
            rb.collisionDetectionMode = useContinuousCollision
                ? CollisionDetectionMode2D.Continuous
                : CollisionDetectionMode2D.Discrete;
            rb.constraints = RigidbodyConstraints2D.None;
            if (fallPhysicsMaterial != null) rb.sharedMaterial = fallPhysicsMaterial;

            // Off-center release impulse at the last-snapped connector — free angular
            // momentum from the lever arm. AddForceAtPosition with ForceMode2D.Impulse
            // takes Δp directly (mass-independent), so multiply by mass to make the
            // inspector value read as "Δv applied to the body at this point."
            if (releaseImpulse.sqrMagnitude > 0.0001f)
            {
                Vector2 applyAt = lastSnappedConnectorWorldPos.sqrMagnitude > 0.0001f
                    ? lastSnappedConnectorWorldPos
                    : (Vector2)transform.position;
                rb.AddForceAtPosition(releaseImpulse * fallMass, applyAt, ForceMode2D.Impulse);
            }
            if (Mathf.Abs(releaseAngularVelocity) > 0.001f)
                rb.angularVelocity += releaseAngularVelocity;

            SpawnDebris();
            onFallStart?.Invoke();
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (state != TrainState.Falling || hasFiredImpact) return;
            int otherLayerMask = 1 << c.gameObject.layer;
            if ((groundLayers.value & otherLayerMask) == 0) return;

            // Speed + time guards: when the train transitions Kinematic→Dynamic, contacts
            // that already exist (e.g. slime resting on top) re-evaluate next FixedUpdate
            // and would fire OnCollisionEnter2D with near-zero velocity. Filter those out.
            if (fallElapsed < impactMinFallTime) return;
            if (rb.linearVelocity.sqrMagnitude < impactMinSpeed * impactMinSpeed) return;

            hasFiredImpact = true;
            Vector3 contactPoint = c.contactCount > 0 ? (Vector3)c.GetContact(0).point : transform.position;
            EnterHitStop(contactPoint);
        }

        private void EnterHitStop(Vector3 impactPos)
        {
            state = TrainState.HitStop;

            // Stash physics state and freeze visually by switching to Kinematic with zero
            // velocity for the duration. timeScale dip alone would still allow forward
            // momentum through the freeze; Kinematic gives a clean stop.
            hitStopSavedVelocity = rb.linearVelocity;
            hitStopSavedAngular = rb.angularVelocity;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;

            // 3-layer SFX
            if (crackSound != null)
                SoundService.PlayAt(crackSound, impactPos, SoundCategory.SFX_Object, impactVolume);
            if (groanSound != null)
                SoundService.PlayAt(groanSound, impactPos, SoundCategory.SFX_Object, impactVolume * 0.85f);
            if (dustSettleSound != null)
                StartCoroutine(PlayDelayedRealtime(dustSettleSound, impactPos, dustSettleDelay));

            if (impactImpulse != null) impactImpulse.Emit(impactImpulseForce);
            if (impactVFXPrefab != null)
            {
                var go = Instantiate(impactVFXPrefab, impactPos, Quaternion.identity);
                if (impactVFXLifetime > 0f) Destroy(go, impactVFXLifetime);
            }

            onImpact?.Invoke();

            if (hitStopDuration > 0f) StartCoroutine(HitStopRoutine());
            else EnterSettling();
        }

        private IEnumerator HitStopRoutine()
        {
            float prev = Time.timeScale;
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(hitStopDuration);
            Time.timeScale = prev;
            if (state == TrainState.HitStop) EnterSettling();
        }

        private IEnumerator PlayDelayedRealtime(AudioClip clip, Vector3 pos, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            SoundService.PlayAt(clip, pos, SoundCategory.SFX_Object, impactVolume);
        }

        private void EnterSettling()
        {
            state = TrainState.Settling;
            settledTimer = 0f;
            restSnapStableTimer = 0f;

            // Resume physics with bled-down velocity — train is "compressed" against
            // the ground after the freeze, not still falling at full speed.
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.linearVelocity = hitStopSavedVelocity * postHitStopVelocityRetention;
            rb.angularVelocity = hitStopSavedAngular * postHitStopAngularRetention;
        }

        private void EnterLanded()
        {
            state = TrainState.Landed;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Static;

            lastSettledPos = rb.position;
            lastSettledRot = rb.rotation;
            hasLastSettled = true;

            Debug.Log($"[FallingTrainPlatform] Settled at ({lastSettledPos.x:F2}, {lastSettledPos.y:F2}) " +
                      $"rot={lastSettledRot:F1}° after {fallElapsed:F2}s. Adjust train Transform.position " +
                      $"to nudge the landing.", this);

            onLanded?.Invoke();
        }

        // --- Debris ---

        private void SpawnDebris()
        {
            if (debrisPrefab == null || debrisCount <= 0 || connectors.Count == 0) return;

            for (int i = 0; i < debrisCount; i++)
            {
                var c = connectors[i % connectors.Count];
                Vector3 origin = c != null && c.anchor != null
                    ? c.anchor.position
                    : (Vector3)currentSagPos;
                Vector2 jitter = UnityEngine.Random.insideUnitCircle * debrisSpawnJitter;
                Vector3 spawnPos3 = origin + new Vector3(jitter.x, jitter.y, 0f);

                var go = Instantiate(debrisPrefab, spawnPos3,
                    Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f)));

                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d != null)
                {
                    float angle = UnityEngine.Random.Range(-150f, -30f) * Mathf.Deg2Rad;
                    float speed = UnityEngine.Random.Range(debrisSpeedRange.x, debrisSpeedRange.y);
                    rb2d.linearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                    rb2d.angularVelocity = UnityEngine.Random.Range(-360f, 360f);
                }

                if (debrisLifetime > 0f) Destroy(go, debrisLifetime);
            }
        }

        // --- Designer iteration helpers ---

        [ContextMenu("DEBUG: Trigger Fall Now (skip telegraph)")]
        private void DebugTriggerFallNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[FallingTrainPlatform] Trigger Fall Now requires Play mode.", this);
                return;
            }
            if (state != TrainState.Idle && state != TrainState.Telegraphing)
            {
                Debug.LogWarning($"[FallingTrainPlatform] Already past Telegraphing (state={state}).", this);
                return;
            }
            if (creakSource != null && creakSource.isPlaying) creakSource.Stop();
            EnterFalling();
        }

        [ContextMenu("DEBUG: Mark Last Settle Position In Scene")]
        private void DebugMarkLastSettle()
        {
            if (!hasLastSettled)
            {
                Debug.LogWarning("[FallingTrainPlatform] No settle recorded yet — fall + land first.", this);
                return;
            }
            var marker = new GameObject($"SettleMark_{lastSettledPos.x:F1}_{lastSettledPos.y:F1}");
            marker.transform.position = new Vector3(lastSettledPos.x, lastSettledPos.y, transform.position.z);
            marker.transform.rotation = Quaternion.Euler(0f, 0f, lastSettledRot);
            Debug.Log($"[FallingTrainPlatform] Dropped marker '{marker.name}' at last settle pose.", marker);
        }

        // --- Gizmos ---

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // Connector markers + indices.
            for (int i = 0; i < connectors.Count; i++)
            {
                var c = connectors[i];
                if (c == null || c.anchor == null) continue;
                Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(c.anchor.position, Vector3.one * 0.25f);
                UnityEditor.Handles.Label(c.anchor.position + Vector3.up * 0.3f, $"#{i}");
            }

            // Last settled pose (only meaningful at runtime after Landed).
            if (hasLastSettled)
            {
                Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.9f);
                Gizmos.DrawWireSphere(lastSettledPos, 0.3f);
                UnityEditor.Handles.Label(lastSettledPos + Vector2.up * 0.4f,
                    $"Last settle: ({lastSettledPos.x:F2}, {lastSettledPos.y:F2})  rot={lastSettledRot:F1}°");
            }
        }
#endif
    }
}
