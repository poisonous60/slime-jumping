using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Counter-attack volume for <see cref="GraniteBlastEmitter"/>: slime touch cancels the emitter, stretches the warp, freezes the world, then shrinks the visual away.</summary>
    [RequireComponent(typeof(Collider2D))]
    [AddComponentMenu("SlimeJumping/Mechanisms/Granite Blast Emitter Attackable")]
    public class GraniteBlastEmitterAttackable : MonoBehaviour, IExternalController
    {
        [Header("Wiring")]
        [Tooltip("Emitter to cancel on contact. Required.")]
        [SerializeField] private GraniteBlastEmitter emitter;
        [Tooltip("Stretch warp effect to fire on contact. Anchor on the warp should be set to the emitter so the membrane spans emitter→slime.")]
        [SerializeField] private SpaceWarpEffect warpEffect;
        [Tooltip("Visual root that shrinks to 0 on destruction. Leave null to skip the shrink and just deactivate.")]
        [SerializeField] private Transform visualToShrink;
        [Tooltip("GameObject deactivated when shrink completes. Defaults to emitter.gameObject.")]
        [SerializeField] private GameObject targetToDeactivate;

        [Header("Tuning")]
        [Tooltip("Realtime seconds the rest of the world stays frozen while the warp plays out. Slime + warp keep moving — only co-operating mechanisms (in-flight tendrils, JumpBall bob, FreezableParticleSystem) pause.")]
        [Min(0f)][SerializeField] private float worldFreezeDuration = 0.55f;
        [Tooltip("Seconds the visual shrink coroutine runs. Independent of worldFreezeDuration.")]
        [Min(0f)][SerializeField] private float shrinkDuration = 0.45f;
        [Tooltip("Scale-over-time curve for the shrink. X = 0..1 progress, Y = scale multiplier. Default eases 1 → 0.")]
        [SerializeField] private AnimationCurve shrinkCurve =
            new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -2f, 0f));

        [Header("Feedback (optional)")]
        [SerializeField] private ParticleSystem destroyVfx;
        [Tooltip("Plays at first contact — the moment the slime touches the emitter.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef impactSound;
        [Tooltip("Plays when the BulbShatter membrane breaks — fires when the pass-through drive ends and Pop() is called. Leave null to skip.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef shatterSound;

        [Header("Slime Pass-Through Drive")]
        [Tooltip("On contact, take override control of the slime and drive it along the impact direction at a constant speed so the BulbShatter membrane visibly elongates. Set 0 to disable the drive (slime keeps whatever momentum it had).")]
        [Min(0f)][SerializeField] private float minPassThroughSpeed = 16f;
        [Tooltip("Multiplier applied to the slime's existing speed before clamping to minPassThroughSpeed. 1 = preserve, >1 = amplify.")]
        [Min(0f)][SerializeField] private float passThroughSpeedScale = 1.1f;
        [Tooltip("Realtime seconds the constant-velocity drive runs. Match to the warp's maxStretchDuration so the membrane reaches full stretch right as the drive ends. 0 = no override (one-shot velocity only).")]
        [Min(0f)][SerializeField] private float passThroughDuration = 0.55f;

        private bool consumed;
        private Collider2D ownCollider;

        private SlimeController drivenSlime;
        private Vector2 driveVelocity;
        private float driveEndTime;

        private void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        private void Awake()
        {
            ownCollider = GetComponent<Collider2D>();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed) return;
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            consumed = true;
            if (ownCollider != null) ownCollider.enabled = false;

            ApplyPassThroughBoost(slime);

            // Held variant: plain TriggerStretch auto-pops at maxStretchLength/Duration
            // mid-flight; ReleaseDrive fires Pop() at drive end instead.
            if (warpEffect != null)
            {
                if (passThroughDuration > 0f) warpEffect.TriggerStretchHeld(slime.transform);
                else warpEffect.TriggerStretch(slime.transform);
            }
            if (emitter != null) emitter.Cancel();

            if (worldFreezeDuration > 0f) WorldTimeFreezeService.Freeze(worldFreezeDuration);

            if (destroyVfx != null) destroyVfx.Play(true);
            SoundService.Play(impactSound, transform.position);

            StartCoroutine(ShrinkAndDeactivate());
        }

        // Drives the slime outward under an IExternalController override — the warp's
        // stretch follows anchor→slime distance, so only sustained motion elongates it.
        private void ApplyPassThroughBoost(SlimeController slime)
        {
            if (minPassThroughSpeed <= 0f && passThroughSpeedScale <= 0f) return;

            Vector2 incoming = slime.Velocity;
            float incomingSpeed = incoming.magnitude;

            Vector2 dir;
            if (incomingSpeed > 0.05f)
            {
                dir = incoming / incomingSpeed;
            }
            else
            {
                Vector2 fromEmitter = (Vector2)slime.transform.position - (Vector2)transform.position;
                dir = fromEmitter.sqrMagnitude > 0.0001f ? fromEmitter.normalized : Vector2.up;
            }

            float scaledSpeed = incomingSpeed * passThroughSpeedScale;
            float finalSpeed = Mathf.Max(scaledSpeed, minPassThroughSpeed);
            Vector2 v = dir * finalSpeed;

            if (passThroughDuration > 0f)
            {
                driveVelocity = v;
                // Scaled clock — a realtime window would scale travel distance with game speed.
                driveEndTime = Time.time + passThroughDuration;
                drivenSlime = slime;
                slime.SetOverride(this);
            }
            else
            {
                slime.ForceAirborneWithVelocity(v);
            }
        }

        // ---- IExternalController ----

        public void OnTakeControl(SlimeController slime)
        {
            slime.OverrideVelocity(driveVelocity);
        }

        public void OnReleaseControl(SlimeController slime) { }

        public void ExternalUpdate(SlimeController slime)
        {
            if (Time.time >= driveEndTime)
            {
                ReleaseDrive();
            }
        }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            slime.OverrideVelocity(driveVelocity);
        }

        private void ReleaseDrive()
        {
            if (drivenSlime == null) return;
            var s = drivenSlime;
            drivenSlime = null;
            s.ReleaseOverride(this);
            // Pop at drive end so the shatter lands as the slime regains normal physics.
            if (warpEffect != null) warpEffect.Pop();
            SoundService.Play(shatterSound, transform.position);
        }

        private void OnDisable()
        {
            // Disabled mid-drive would strand the slime inside our override.
            ReleaseDrive();
        }

        private IEnumerator ShrinkAndDeactivate()
        {
            GameObject deactivateTarget = targetToDeactivate != null
                ? targetToDeactivate
                : (emitter != null ? emitter.gameObject : gameObject);

            // The emitter root usually parents the warp quad — deactivating before
            // Pop() finishes kills the warp's Update mid-pop.
            float warpHoldTime = passThroughDuration + (warpEffect != null ? warpEffect.PopDuration : 0f);
            float totalTime = Mathf.Max(shrinkDuration, warpHoldTime);

            Vector3 baseScale = visualToShrink != null ? visualToShrink.localScale : Vector3.one;
            float t = 0f;
            while (t < totalTime)
            {
                t += Time.deltaTime;
                if (visualToShrink != null && shrinkDuration > 0f)
                {
                    float k = shrinkCurve.Evaluate(Mathf.Clamp01(t / shrinkDuration));
                    visualToShrink.localScale = baseScale * k;
                }
                yield return null;
            }
            if (visualToShrink != null && shrinkDuration > 0f)
                visualToShrink.localScale = baseScale * shrinkCurve.Evaluate(1f);

            if (deactivateTarget != null) deactivateTarget.SetActive(false);
        }
    }
}
