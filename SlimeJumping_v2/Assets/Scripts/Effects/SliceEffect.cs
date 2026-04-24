using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// One-shot VFX for slime being cut by Knife/Saw hazards.
    /// Spawned at the contact point, oriented so local +Y aligns with the signed tangent
    /// (slime's slide direction along the blade edge).
    /// </summary>
    public class SliceEffect : MonoBehaviour
    {
        [SerializeField] private ParticleSystem particles;
        [SerializeField] private AudioClip[] hitClips;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField, Range(0f, 0.5f)] private float pitchJitter = 0.1f;

        /// <summary>
        /// Instantiate and play a slice effect. `signedTangent` should point in the
        /// slime's slide direction (not the blade's rotation direction).
        /// </summary>
        public static SliceEffect Spawn(SliceEffect prefab, Vector3 contactPoint, Vector2 signedTangent)
        {
            if (prefab == null) return null;

            Vector3 tangent3 = signedTangent.sqrMagnitude > 0.0001f
                ? (Vector3)signedTangent.normalized
                : Vector3.up;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, tangent3);

            SliceEffect instance = Instantiate(prefab, contactPoint, rot);
            instance.Play();
            return instance;
        }

        private void Play()
        {
            if (particles != null) particles.Play();

            AudioClip chosen = null;
            if (hitClips != null && hitClips.Length > 0)
            {
                chosen = hitClips[Random.Range(0, hitClips.Length)];
                if (chosen != null)
                {
                    float pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
                    SoundService.PlayAt(chosen, transform.position,
                                        SoundCategory.SFX_Object, sfxVolume, pitch);
                }
            }

            StartCoroutine(SelfDestruct(chosen));
        }

        private IEnumerator SelfDestruct(AudioClip playedClip)
        {
            float particleLife = 0f;
            if (particles != null)
            {
                var main = particles.main;
                particleLife = main.duration + main.startLifetime.constantMax;
            }
            float clipLife = playedClip != null ? playedClip.length : 0f;
            float wait = Mathf.Max(particleLife, clipLife) + 0.1f;

            yield return new WaitForSeconds(wait);
            Destroy(gameObject);
        }
    }
}
