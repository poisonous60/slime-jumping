using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Platforms;

namespace SlimeJumping.Effects
{
    /// Creak + dust when the slime lands on this stalk, graded by how far that landing will bend it.
    /// The bend is predicted, not measured: the spring only reaches its peak ~a quarter period later,
    /// and a sound that late reads as a separate event. Landing amplitude of an undamped spring is
    /// dOmega / omega_n, and BambooPlatformV3 publishes both terms on the attach frame.
    /// The creak plays once and is cut short only when the rider leaves, so it never outlives what
    /// is on screen — a stalk still holding a slime is not creaking, and a clip stopped at the bend
    /// peak reads as a sound someone interrupted.
    /// Execution order 100 polls after the rider has pushed those terms in.
    /// All tuning lives on the shared BambooLandingFeedbackConfig.
    /// No RequireComponent: BambooOnThinIce strips the stalk driver off its corpse clone, and a
    /// declared dependency makes Unity refuse that removal — the corpse then keeps two PlatformMotors
    /// and never falls.
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("SlimeJumping/Effects/Bamboo Landing Feedback")]
    public class BambooLandingFeedback : MonoBehaviour
    {
        [Tooltip("Optional override of the shared BambooLandingFeedbackConfig. Leave null to auto-load " +
                 "Assets/Resources/BambooLandingFeedbackConfig.asset.")]
        [SerializeField] BambooLandingFeedbackConfig configOverride;

        BambooLandingFeedbackConfig config;
        BambooPlatformV3 bamboo;

        SoundHandle bendLoop;
        float bendVolume;
        float fadeTimer;
        float fadeDuration;
        bool wasAttached;

        void Awake()
        {
            bamboo = GetComponent<BambooPlatformV3>();
            if (bamboo == null)
            {
                enabled = false;
                return;
            }

            // Fallback instance so a config-less project needs no null guard on every read.
            config = BambooLandingFeedbackConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<BambooLandingFeedbackConfig>();
        }

        void OnDisable()
        {
            wasAttached = false;
            StopCreak();
        }

        void FixedUpdate()
        {
            // BambooOnThinIce strips the stalk driver off its corpse clone under a live component.
            if (bamboo == null)
            {
                StopCreak();
                enabled = false;
                return;
            }

            bool attached = bamboo.RiderAttached;
            if (attached && !wasAttached) HandleLanded();
            wasAttached = attached;

            bool barelyMoving = Mathf.Abs(bamboo.AngularVelocity) < config.settleSpeedDegPerSec;
            if (fadeTimer <= 0f && bendLoop.IsPlaying && (!attached || barelyMoving)) BeginFade();
        }

        void Update()
        {
            if (fadeTimer <= 0f)
            {
                // Ran to its end on its own; the pool entry is still locked until it is released.
                if (bendLoop.IsValid && !bendLoop.IsPlaying) StopCreak();
                return;
            }

            fadeTimer -= Time.deltaTime;
            if (fadeTimer <= 0f) StopCreak();
            else bendLoop.SetVolume(bendVolume * (fadeTimer / fadeDuration));
        }

        void HandleLanded()
        {
            float bendDeg = PredictedBendDeg();
            if (bendDeg < config.minBendDeg) return;

            float t = Mathf.InverseLerp(config.minBendDeg,
                                        Mathf.Max(config.largeBendDeg, config.minBendDeg + 0.01f), bendDeg);
            Vector2 position = bamboo.LastRiderWorldPos;

            StopCreak();
            bendVolume = Mathf.Lerp(config.volumeAtMinBend, config.volumeAtLargeBend, t);
            bendLoop = SoundService.PlayLooping(config.bendSound, null, bendVolume);
            bendLoop.SetLoop(false);
            bendLoop.SetPosition(position);
            bendLoop.SetPitch(Mathf.Lerp(config.pitchAtMinBend, config.pitchAtLargeBend, t));

            SpawnPuffs(position, t);
        }

        void BeginFade()
        {
            fadeDuration = Mathf.Max(config.fadeOutSeconds, 0.001f);
            fadeTimer = fadeDuration;
        }

        void StopCreak()
        {
            SoundService.StopLoop(bendLoop);
            bendLoop = default;
            fadeTimer = 0f;
        }

        // Kind 1 = landing kick; any other kind means no impulse this frame, so sag alone.
        float PredictedBendDeg()
        {
            float bend = Mathf.Abs(bamboo.CurrentDeflectionDeg);
            if (bamboo.LastRiderImpulseKind != 1) return bend;

            float omega = 2f * Mathf.PI * bamboo.NaturalFreqHz;
            if (omega < 1e-4f) return bend;
            return bend + Mathf.Abs(bamboo.LastRiderImpulseDeg) / omega;
        }

        void SpawnPuffs(Vector2 position, float t)
        {
            if (config.dustPuffPrefab == null) return;

            int count = Mathf.RoundToInt(Mathf.Lerp(config.puffsAtMinBend, config.puffsAtLargeBend, t));
            float scale = Mathf.Lerp(config.puffScaleAtMinBend, config.puffScaleAtLargeBend, t);

            for (int i = 0; i < count; i++)
            {
                Vector2 spawn = position + (i == 0 ? Vector2.zero : Random.insideUnitCircle * config.puffSpread);
                var go = Instantiate(config.dustPuffPrefab, spawn, Quaternion.identity);
                go.transform.localScale = config.dustPuffPrefab.transform.localScale * scale;
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null) ps.Play();
            }
        }
    }
}
