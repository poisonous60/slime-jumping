using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Counter-attack volume for <see cref="GraniteBlastEmitter"/>. Touch with the
    /// slime → emitter immediately stops shooting (FSM cancel + active Beam destroyed),
    /// the configured <see cref="SpaceWarpEffect"/> stretches between the emitter and
    /// the slime then shatters, the rest of the world freezes via
    /// <see cref="WorldTimeFreezeService"/> for the warp's duration, and the emitter
    /// visual shrinks to nothing before the GameObject is disabled.
    ///
    /// Already-airborne tendrils / burst flashes are left to fly out their natural
    /// lifetime — only the sustained Beam is killed on contact.
    /// </summary>
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
        [SerializeField] private AudioClip impactSfx;
        [Range(0f, 1f)][SerializeField] private float impactVolume = 0.9f;
        [Tooltip("Plays when the BulbShatter membrane breaks — fires when the pass-through drive ends and Pop() is called. Leave null to skip.")]
        [SerializeField] private AudioClip shatterSfx;
        [Range(0f, 1f)][SerializeField] private float shatterVolume = 1f;

        [Header("Slime Pass-Through Drive")]
        [Tooltip("On contact, take override control of the slime and drive it along the impact direction at a constant speed so the BulbShatter membrane visibly elongates. Set 0 to disable the drive (slime keeps whatever momentum it had).")]
        [Min(0f)][SerializeField] private float minPassThroughSpeed = 16f;
        [Tooltip("Multiplier applied to the slime's existing speed before clamping to minPassThroughSpeed. 1 = preserve, >1 = amplify.")]
        [Min(0f)][SerializeField] private float passThroughSpeedScale = 1.1f;
        [Tooltip("Realtime seconds the constant-velocity drive runs. Match to the warp's maxStretchDuration so the membrane reaches full stretch right as the drive ends. 0 = no override (one-shot velocity only).")]
        [Min(0f)][SerializeField] private float passThroughDuration = 0.55f;

        private bool consumed;
        private Collider2D ownCollider;

        // Sustained-drive runtime — set when the slime makes contact, consumed by
        // ExternalFixedUpdate to write a constant velocity, cleared on release.
        private SlimeController drivenSlime;
        private Vector2 driveVelocity;
        private float driveEndRealtime;

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

            // Use the held variant so the warp doesn't auto-pop at maxStretchLength /
            // maxStretchDuration mid-flight — we want the shatter to align with the
            // end of our pass-through drive instead. ReleaseDrive calls warpEffect.Pop().
            if (warpEffect != null)
            {
                if (passThroughDuration > 0f) warpEffect.TriggerStretchHeld(slime.transform);
                else warpEffect.TriggerStretch(slime.transform);
            }
            if (emitter != null) emitter.Cancel();

            if (worldFreezeDuration > 0f) WorldTimeFreezeService.Freeze(worldFreezeDuration);

            if (destroyVfx != null) destroyVfx.Play(true);
            if (impactSfx != null)
                SoundService.PlayAt(impactSfx, transform.position, SoundCategory.SFX_Object, impactVolume);

            StartCoroutine(ShrinkAndDeactivate());
        }

        // Drive the slime along the impact direction at a constant speed for
        // passThroughDuration realtime seconds. The BulbShatter warp's stretch
        // length follows anchor→slime distance, so sustained outbound velocity is
        // what makes the membrane visibly elongate before it pops. Implemented as
        // an IExternalController override — gravity, input, and state machine are
        // suspended for the duration.
        //
        // Direction prefers the slime's current velocity; falls back to the
        // emitter→slime axis when the slime is barely moving.
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
                driveEndRealtime = Time.realtimeSinceStartup + passThroughDuration;
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
            if (Time.realtimeSinceStartup >= driveEndRealtime)
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
            // Fire the warp shatter at drive end so the BulbShatter pop is
            // synchronized with the slime regaining normal physics, regardless
            // of how far the membrane managed to stretch.
            if (warpEffect != null) warpEffect.Pop();
            if (shatterSfx != null)
                SoundService.PlayAt(shatterSfx, transform.position, SoundCategory.SFX_Object, shatterVolume);
        }

        private void OnDisable()
        {
            // Safety: if the attackable GO gets disabled mid-drive (e.g. shrink
            // coroutine deactivated the wrong thing), make sure we hand control
            // back to the slime instead of stranding it in our override.
            ReleaseDrive();
        }

        private IEnumerator ShrinkAndDeactivate()
        {
            GameObject deactivateTarget = targetToDeactivate != null
                ? targetToDeactivate
                : (emitter != null ? emitter.gameObject : gameObject);

            // The emitter root is usually the parent of the warp quad — deactivating
            // it kills the warp's Update mid-pop. Hold the deactivation until both
            // the shrink finishes AND the warp has had time to play out its Pop().
            float warpHoldTime = passThroughDuration + (warpEffect != null ? warpEffect.PopDuration : 0f);
            float totalTime = Mathf.Max(shrinkDuration, warpHoldTime);

            Vector3 baseScale = visualToShrink != null ? visualToShrink.localScale : Vector3.one;
            float t = 0f;
            while (t < totalTime)
            {
                t += Time.unscaledDeltaTime;
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
