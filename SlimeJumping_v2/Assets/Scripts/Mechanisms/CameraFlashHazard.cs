using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Interfaces;
using SlimeJumping.UI;

namespace SlimeJumping.Mechanisms
{
    public class CameraFlashHazard : MonoBehaviour, IHazardSource
    {
        [Header("Timing")]
        [SerializeField] private float flashInterval = 4f;
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

        private float timer;
        private bool isFlashing;

        // TODO: SlimeController.TeleportTo leaves stale entries (OnTriggerExit2D미발화).
        // 현재 슬라임 1마리 가정으로 실용 영향 없음. 다중 도입 시 정리 훅 필요.
        private readonly HashSet<SlimeController> insideArea = new();

        public bool IsActive => isFlashing;

        public HazardEffect GetHazardEffect() => new HazardEffect
        {
            knockbackForce = 0f,
            knockbackDirection = Vector2.zero,
            stunDuration = stunDuration,
            damageType = DamageType.CameraFlash
        };

        void Update()
        {
            timer += Time.deltaTime;
            if (timer >= flashInterval)
            {
                timer = 0f;
                StartCoroutine(FlashSequence());
            }
        }

        IEnumerator FlashSequence()
        {
            float t = 0f;
            while (t < flashWarmup)
            {
                t += Time.deltaTime;
                if (bulbLight != null)
                    bulbLight.intensity = Mathf.Lerp(bulbBaseIntensity, bulbWarmupIntensity, t / Mathf.Max(flashWarmup, 0.0001f));
                yield return null;
            }

            isFlashing = true;
            if (bulbLight != null) bulbLight.intensity = bulbFlashIntensity;
            if (shutterAudio != null && shutterClip != null) shutterAudio.PlayOneShot(shutterClip);
            if (impulseEmitter != null) impulseEmitter.Emit(0.2f);
            if (flashCanvas != null) flashCanvas.Trigger();

            var effect = GetHazardEffect();
            foreach (var slime in insideArea)
            {
                if (slime != null) slime.ApplyHazardEffect(effect);
            }

            yield return null;
            isFlashing = false;

            if (bulbLight != null) bulbLight.intensity = bulbBaseIntensity;
        }

        internal void NotifyEnter(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null) insideArea.Add(slime);
        }

        internal void NotifyExit(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null) insideArea.Remove(slime);
        }
    }
}
