using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Effects
{
    /// <summary>One-shot slime-cut VFX, spawned at the contact point with local +Y along the signed tangent.</summary>
    public class SliceEffect : MonoBehaviour
    {
        [SerializeField] private ParticleSystem particles;
        [SerializeField] private SlimeJumping.Audio.SoundDef hitSound;

        /// <summary>Instantiate and play. signedTangent is the slime's slide direction, not the blade's rotation direction.</summary>
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

            // Pick once: the clip whose length gates SelfDestruct must be the clip that actually played.
            AudioClip chosen = hitSound != null ? hitSound.PickClip() : null;
            if (chosen != null)
                SoundService.PlayAt(chosen, transform.position, hitSound.category,
                                    hitSound.PickVolume(), hitSound.PickPitch());

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
