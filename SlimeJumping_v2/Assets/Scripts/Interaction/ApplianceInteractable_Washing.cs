using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    /// Keeps its own AudioSource because the drum loop toggles on and off with the appliance, which
    /// the SoundService pool API does not expose; the mixer group comes from the category.
    public class ApplianceInteractable_Washing : InteractableBase
    {
        [Header("Vibration (visual only)")]
        [Tooltip("Visual-only child transform to vibrate. MUST NOT host any collider.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private float amplitude = 0.02f;
        [SerializeField] private float frequency = 8f;

        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private SoundCategory category = SoundCategory.SFX_Object;

        private bool isOn;
        private Vector3 visualBaseLocalPos;

        protected override void Awake()
        {
            base.Awake();
            if (visualRoot != null) visualBaseLocalPos = visualRoot.localPosition;
            var group = SoundService.GetGroup(category);
            if (audioSource != null && group != null) audioSource.outputAudioMixerGroup = group;
        }

        protected override void OnInteract(SlimeController slime)
        {
            isOn = !isOn;

            if (audioSource != null)
            {
                if (isOn) { audioSource.loop = true; audioSource.Play(); }
                else audioSource.Stop();
            }

            if (!isOn && visualRoot != null) visualRoot.localPosition = visualBaseLocalPos;
        }

        private void Update()
        {
            if (!isOn || visualRoot == null) return;

            float t = Time.time * frequency * Mathf.PI * 2f;
            float ox = Mathf.Sin(t) * amplitude;
            float oy = Mathf.Cos(t) * amplitude * 0.5f;
            visualRoot.localPosition = visualBaseLocalPos + new Vector3(ox, oy, 0f);
        }
    }
}
