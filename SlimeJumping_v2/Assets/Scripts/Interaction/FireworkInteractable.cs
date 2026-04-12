using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Interaction
{
    public class FireworkInteractable : InteractableBase
    {
        [Header("Launch")]
        [SerializeField] private Vector2 launchVelocity = new Vector2(0f, 18f);
        [Tooltip("If true, direction is (slime - origin).normalized * launchVelocity.magnitude; otherwise launchVelocity is used directly.")]
        [SerializeField] private bool launchAwayFromCenter = false;
        [Tooltip("Local-space offset from this transform for the radial push origin (e.g. above the rocket where it explodes). Used only when Launch Away From Center is on.")]
        [SerializeField] private Vector2 pushOriginOffset;
        [Tooltip("Seconds to delay slime launch after interact. Match VFX burst timing (e.g. rocket rises then explodes).")]
        [SerializeField] private float launchDelay = 0f;

        [Header("FX")]
        [SerializeField] private ParticleSystem fireworkVFX;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip launchSfx;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private float impulseForce = 0f;

        protected override void OnInteract(SlimeController slime)
        {
            if (fireworkVFX != null) fireworkVFX.Play();
            if (audioSource != null && launchSfx != null) audioSource.PlayOneShot(launchSfx);

            if (launchDelay > 0f)
                StartCoroutine(LaunchAfterDelay(slime, launchDelay));
            else
                LaunchSlime(slime);
        }

        private IEnumerator LaunchAfterDelay(SlimeController slime, float delay)
        {
            yield return new WaitForSeconds(delay);
            LaunchSlime(slime);
        }

        private void OnDrawGizmosSelected()
        {
            if (!launchAwayFromCenter) return;
            Vector2 origin = (Vector2)transform.position + pushOriginOffset;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.9f);
            Gizmos.DrawSphere(origin, 0.15f);
            Gizmos.DrawLine(transform.position, origin);
        }

        private void LaunchSlime(SlimeController slime)
        {
            Vector2 v = launchVelocity;
            if (launchAwayFromCenter)
            {
                Vector2 origin = (Vector2)transform.position + pushOriginOffset;
                Vector2 dir = slime.Position - origin;
                v = dir.sqrMagnitude > 0.0001f
                    ? dir.normalized * launchVelocity.magnitude
                    : launchVelocity;
            }

            slime.ForceAirborneWithVelocity(v);
            if (impulseEmitter != null && impulseForce > 0f) impulseEmitter.Emit(impulseForce);
        }
    }
}
