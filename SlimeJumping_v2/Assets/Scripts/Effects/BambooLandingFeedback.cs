using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Effects
{
    /// Creak + dust when the slime lands on a bamboo stalk, graded by how far that landing will
    /// bend it. The bend is predicted, not measured: the spring only reaches its peak ~a quarter
    /// period later, and a sound that late reads as a separate event. Landing amplitude of an
    /// undamped spring is dOmega / omega_n, and BambooPlatformV3 publishes both terms on the
    /// attach frame, which lands before OnLanded fires.
    /// Pull-only from SlimeController, so nothing is wired on the stalks; sits on the Slime.
    [AddComponentMenu("SlimeJumping/Effects/Bamboo Landing Feedback")]
    public class BambooLandingFeedback : MonoBehaviour
    {
        [SerializeField] SlimeController slimeController;

        [Header("Sound")]
        [Tooltip("Creak for a landing whose predicted bend is under Large Bend Deg.")]
        [SerializeField] SoundDef bendSmallSound;
        [Tooltip("Creak for a landing at or over Large Bend Deg.")]
        [SerializeField] SoundDef bendLargeSound;

        [Header("Bend grading")]
        [Tooltip("Predicted bend below this plays nothing — a graze must not creak.")]
        [Min(0f)] [SerializeField] float minBendDeg = 2f;
        [Tooltip("Predicted bend at or above this uses the large creak and the full dust burst.")]
        [Min(0f)] [SerializeField] float largeBendDeg = 20f;

        [Header("Dust")]
        [Tooltip("Puff spawned at the contact point. playOnAwake must be OFF; Play() is called explicitly.")]
        [SerializeField] GameObject dustPuffPrefab;
        [Tooltip("Puffs spawned at Min Bend Deg.")]
        [Min(0)] [SerializeField] int puffsAtMinBend = 1;
        [Tooltip("Puffs spawned at Large Bend Deg.")]
        [Min(0)] [SerializeField] int puffsAtLargeBend = 3;
        [Tooltip("Puff scale at Min Bend Deg.")]
        [Min(0.01f)] [SerializeField] float puffScaleAtMinBend = 0.6f;
        [Tooltip("Puff scale at Large Bend Deg.")]
        [Min(0.01f)] [SerializeField] float puffScaleAtLargeBend = 1.4f;
        [Tooltip("World spread of the extra puffs around the contact point.")]
        [Min(0f)] [SerializeField] float puffSpread = 0.15f;

        void Reset()
        {
            if (slimeController == null) slimeController = GetComponentInParent<SlimeController>();
        }

        void OnEnable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded += HandleLanded;
        }

        void OnDisable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded -= HandleLanded;
        }

        void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            var col = slimeController.SurfaceCollider;
            if (col == null) return;
            var bamboo = col.GetComponentInParent<BambooPlatformV3>();
            if (bamboo == null) return;

            float bendDeg = PredictedBendDeg(bamboo);
            if (bendDeg < minBendDeg) return;

            SoundService.Play(bendDeg >= largeBendDeg ? bendLargeSound : bendSmallSound, position);

            float t = Mathf.InverseLerp(minBendDeg, Mathf.Max(largeBendDeg, minBendDeg + 0.01f), bendDeg);
            SpawnPuffs(position, t);
        }

        // Kind 1 = landing kick; any other kind means no impulse this frame, so sag alone.
        float PredictedBendDeg(BambooPlatformV3 bamboo)
        {
            float bend = Mathf.Abs(bamboo.CurrentDeflectionDeg);
            if (bamboo.LastRiderImpulseKind != 1) return bend;

            float omega = 2f * Mathf.PI * bamboo.NaturalFreqHz;
            if (omega < 1e-4f) return bend;
            return bend + Mathf.Abs(bamboo.LastRiderImpulseDeg) / omega;
        }

        void SpawnPuffs(Vector2 position, float t)
        {
            if (dustPuffPrefab == null) return;

            int count = Mathf.RoundToInt(Mathf.Lerp(puffsAtMinBend, puffsAtLargeBend, t));
            float scale = Mathf.Lerp(puffScaleAtMinBend, puffScaleAtLargeBend, t);

            for (int i = 0; i < count; i++)
            {
                Vector2 spawn = position + (i == 0 ? Vector2.zero : Random.insideUnitCircle * puffSpread);
                var go = Instantiate(dustPuffPrefab, spawn, Quaternion.identity);
                go.transform.localScale = dustPuffPrefab.transform.localScale * scale;
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null) ps.Play();
            }
        }
    }
}
