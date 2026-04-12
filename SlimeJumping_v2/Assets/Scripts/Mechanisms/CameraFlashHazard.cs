using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;
using SlimeJumping.UI;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class CameraFlashHazard : MonoBehaviour, IHazardSource
    {
        [Header("Behavior")]
        [SerializeField] private bool oneShot = true;
        [SerializeField] private float flashWarmup = 0.3f;
        [SerializeField] private float stunDuration = 0.6f;

        [Header("Visual / Audio")]
        [SerializeField] private FlashCanvasController flashCanvas;
        [SerializeField] private AudioSource shutterAudio;
        [SerializeField] private AudioClip shutterClip;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private Light2D bulbLight;
        [SerializeField] private float bulbBaseIntensity = 0f;
        [SerializeField] private float bulbWarmupIntensity = 0.3f;
        [SerializeField] private float bulbFlashIntensity = 1.5f;

        private bool isFlashing;
        private bool hasFired;

        public bool IsActive => isFlashing;

        public HazardEffect GetHazardEffect() => new HazardEffect
        {
            knockbackForce = 0f,
            knockbackDirection = Vector2.zero,
            stunDuration = stunDuration,
            damageType = DamageType.CameraFlash
        };

        public void ResetOneShot() => hasFired = false;

        void OnDisable()
        {
            // Coroutine is killed by Unity when disabled; clear transient state so re-enable is usable.
            isFlashing = false;
            if (bulbLight != null) bulbLight.intensity = bulbBaseIntensity;
        }

        void Reset()
        {
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            TriggerFlash(slime);
        }

        public void TriggerFlash(SlimeController slime)
        {
            if (oneShot && hasFired) return;
            if (isFlashing) return;
            isFlashing = true;
            hasFired = true;
            StartCoroutine(FlashSequence(slime));
        }

        IEnumerator FlashSequence(SlimeController slime)
        {
            float t = 0f;
            while (t < flashWarmup)
            {
                t += Time.deltaTime;
                if (bulbLight != null)
                    bulbLight.intensity = Mathf.Lerp(bulbBaseIntensity, bulbWarmupIntensity, t / Mathf.Max(flashWarmup, 0.0001f));
                yield return null;
            }

            if (bulbLight != null) bulbLight.intensity = bulbFlashIntensity;
            if (shutterAudio != null && shutterClip != null) shutterAudio.PlayOneShot(shutterClip);
            if (impulseEmitter != null) impulseEmitter.Emit(0.2f);
            if (flashCanvas != null) flashCanvas.Trigger();

            if (slime != null) slime.ApplyHazardEffect(GetHazardEffect());

            yield return null;
            isFlashing = false;

            if (bulbLight != null) bulbLight.intensity = bulbBaseIntensity;
        }
    }
}
