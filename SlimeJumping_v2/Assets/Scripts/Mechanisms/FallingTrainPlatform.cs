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
    /// silence beat → physical-feel drop → hit-stop → rebound → landed shortcut.
    ///
    /// Inherits <see cref="PlatformMotor"/> so the slime rides every phase
    /// automatically through the existing PlatformCarrier system, including the
    /// sag rotation (PlatformCarrier propagates AngularVelocity tangentially).
    ///
    /// Design notes (why no continuous rumble):
    ///   Continuous sin/cos shake reads as "controller buzzing." Real games
    ///   (Uncharted 2 train, HL2 scaffolds, INSIDE) sell collapse with discrete
    ///   jolt beats separated by stillness, sequential connector failure that
    ///   physically tilts the geometry, and an audio-leading silence beat
    ///   right before the drop. This component implements that pattern.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Mechanisms/Falling Train Platform")]
    public class FallingTrainPlatform : PlatformMotor
    {
        private enum TrainState { Idle, Telegraphing, Falling, HitStop, Rebound, Landed }

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

        [Header("Fall Trajectory")]
        [Tooltip("Designer-set destination. The train always arrives here regardless of curve tuning.")]
        [SerializeField] private Transform landTarget;
        [SerializeField] private float fallDuration = 1.4f;
        [Tooltip("Vertical interpolation 0→1 across fallDuration. Default eases in (gravity feel).")]
        [SerializeField] private AnimationCurve fallCurveY = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(1f, 1f, 2f, 0f));
        [Tooltip("Horizontal interpolation 0→1. Default linear (constant horizontal velocity).")]
        [SerializeField] private AnimationCurve fallCurveX = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("Rotation accumulated by impact (deg). Negative = nose dips forward.")]
        [SerializeField] private float fallTilt = -10f;
        [SerializeField] private AnimationCurve fallTiltCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

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

        [Header("Rebound (settle bounce)")]
        [Tooltip("Vertical bounce amplitude on landing (world units).")]
        [SerializeField] private float reboundHeight = 0.35f;
        [Tooltip("Rotation overshoot on landing (deg).")]
        [SerializeField] private float reboundRotation = 3f;
        [SerializeField] private float reboundDuration = 0.5f;
        [Tooltip("Damped oscillation frequency (cycles per reboundDuration).")]
        [SerializeField] private float reboundFrequency = 1.5f;
        [Tooltip("Damping factor — higher = bounce dies faster.")]
        [SerializeField] private float reboundDamping = 5f;

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
        [Tooltip("Spawned at landTarget on impact (dust/debris cloud). Auto-destroyed after impactVFXLifetime.")]
        [SerializeField] private GameObject impactVFXPrefab;
        [SerializeField] private float impactVFXLifetime = 4f;

        [Header("Events")]
        [Tooltip("Fired once at the moment Falling begins (after the silence beat). Wire neighbor-train wobbles, decals, gameplay flags here.")]
        public UnityEvent onFallStart;
        [Tooltip("Fired once at the moment of ground impact (before hit-stop release).")]
        public UnityEvent onImpact;

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

        // fall
        private Vector2 fallStart;
        private Vector2 fallEnd;
        private float fallStartRot;
        private float fallEndRot;
        private float fallTimer;

        // rebound
        private float reboundTimer;

        protected override void Awake()
        {
            base.Awake();
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
                    return TickFalling(dt);

                case TrainState.HitStop:
                    return (fallEnd, fallEndRot);

                case TrainState.Rebound:
                    return TickRebound(dt);

                case TrainState.Landed:
                    return (fallEnd, fallEndRot);
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
                return TickFalling(dt);
            }

            // Fire the next snap if it's time.
            if (nextSnapIdx < connectors.Count && telegraphElapsed >= nextSnapTime)
            {
                SnapConnector(nextSnapIdx);
                nextSnapIdx++;
                if (nextSnapIdx < connectors.Count)
                {
                    // Next snap waits for THIS snap's sag to settle, then its own preDelay.
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
                return TickFalling(dt);
            }

            return (currentSagPos, currentSagRot);
        }

        private void SnapConnector(int i)
        {
            var c = connectors[i];
            if (c == null) return;

            // Pivot = centroid of REMAINING (still-attached) connectors, in current train pose.
            // If none remain, skip the sag (the last snap leads straight into the fall).
            Vector2 pivotWorld;
            bool hasPivot = TryGetRemainingPivotWorld(i, out pivotWorld);

            if (hasPivot)
            {
                // Auto-sign: snapped side should drop. Snapped anchor world-x relative to pivot.
                Vector2 snappedAnchorWorld = TrainLocalToWorld(connectorLocalPositions[i]);
                float side = snappedAnchorWorld.x - pivotWorld.x;
                float signedSag = c.sagDegrees * (side < 0f ? 1f : -1f); // CCW drops left side, CW drops right.

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
            // Apply current sag pose (rotation around currentSagPos, then translate).
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

        // --- Falling ---

        private (Vector2, float) TickFalling(float dt)
        {
            fallTimer += dt;
            float t = fallDuration > 0f ? Mathf.Clamp01(fallTimer / fallDuration) : 1f;
            float ty   = fallCurveY    != null ? fallCurveY.Evaluate(t)    : t;
            float tx   = fallCurveX    != null ? fallCurveX.Evaluate(t)    : t;
            float trot = fallTiltCurve != null ? fallTiltCurve.Evaluate(t) : t;

            Vector2 pos = new Vector2(
                Mathf.Lerp(fallStart.x, fallEnd.x, tx),
                Mathf.Lerp(fallStart.y, fallEnd.y, ty));
            float rot = Mathf.Lerp(fallStartRot, fallEndRot, trot);

            if (t >= 1f) EnterHitStop();
            return (pos, rot);
        }

        // --- Rebound ---

        private (Vector2, float) TickRebound(float dt)
        {
            reboundTimer += dt;
            float t = reboundDuration > 0f ? Mathf.Clamp01(reboundTimer / reboundDuration) : 1f;
            // Damped sine: sin(2π·f·t) · exp(-d·t). Settles to 0 at t=1.
            float decay = Mathf.Exp(-reboundDamping * t);
            float osc = Mathf.Sin(2f * Mathf.PI * reboundFrequency * t) * decay;
            // Cosine-leading rotation so it kicks at impact rather than ramping in.
            float oscRot = Mathf.Cos(2f * Mathf.PI * reboundFrequency * t) * decay - decay; // start at 0

            Vector2 pos = fallEnd + new Vector2(0f, reboundHeight * osc);
            float rot = fallEndRot + reboundRotation * oscRot;

            if (t >= 1f) EnterLanded();
            return (pos, rot);
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
            if (landTarget == null)
            {
                Debug.LogError("[FallingTrainPlatform] landTarget is not assigned — fall aborted.", this);
                state = TrainState.Idle;
                hasFiredOnce = true;
                if (creakSource != null && creakSource.isPlaying) creakSource.Stop();
                return;
            }

            state = TrainState.Falling;
            fallTimer = 0f;
            fallStart = currentSagPos;
            fallEnd = landTarget.position;
            fallStartRot = currentSagRot;
            fallEndRot = currentSagRot + fallTilt;

            if (creakSource != null && creakSource.isPlaying) creakSource.Stop();
            if (riderSensor != null) riderSensor.gameObject.SetActive(false);

            SpawnDebris();
            onFallStart?.Invoke();
        }

        private void EnterHitStop()
        {
            state = TrainState.HitStop;

            Vector3 impactPos = landTarget != null ? landTarget.position : (Vector3)fallEnd;
            Quaternion impactRot = landTarget != null ? landTarget.rotation : Quaternion.identity;

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
                var go = Instantiate(impactVFXPrefab, impactPos, impactRot);
                if (impactVFXLifetime > 0f) Destroy(go, impactVFXLifetime);
            }

            onImpact?.Invoke();

            if (hitStopDuration > 0f) StartCoroutine(HitStopRoutine());
            else EnterRebound();
        }

        private IEnumerator HitStopRoutine()
        {
            float prev = Time.timeScale;
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(hitStopDuration);
            Time.timeScale = prev;
            if (state == TrainState.HitStop) EnterRebound();
        }

        private IEnumerator PlayDelayedRealtime(AudioClip clip, Vector3 pos, float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            SoundService.PlayAt(clip, pos, SoundCategory.SFX_Object, impactVolume);
        }

        private void EnterRebound()
        {
            state = TrainState.Rebound;
            reboundTimer = 0f;
        }

        private void EnterLanded()
        {
            state = TrainState.Landed;
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
                    // Outward+down bias: random angle in lower 240° fan.
                    float angle = UnityEngine.Random.Range(-150f, -30f) * Mathf.Deg2Rad;
                    float speed = UnityEngine.Random.Range(debrisSpeedRange.x, debrisSpeedRange.y);
                    rb2d.linearVelocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
                    rb2d.angularVelocity = UnityEngine.Random.Range(-360f, 360f);
                }

                if (debrisLifetime > 0f) Destroy(go, debrisLifetime);
            }
        }

        // --- Gizmos ---

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (landTarget != null)
            {
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.85f);
                Gizmos.DrawLine(transform.position, landTarget.position);
                Gizmos.DrawWireSphere(landTarget.position, 0.5f);
            }
            for (int i = 0; i < connectors.Count; i++)
            {
                var c = connectors[i];
                if (c == null || c.anchor == null) continue;
                Gizmos.color = new Color(0.9f, 0.3f, 0.3f, 0.9f);
                Gizmos.DrawWireCube(c.anchor.position, Vector3.one * 0.25f);
                UnityEditor.Handles.Label(c.anchor.position + Vector3.up * 0.3f, $"#{i}");
            }
        }
#endif
    }
}
