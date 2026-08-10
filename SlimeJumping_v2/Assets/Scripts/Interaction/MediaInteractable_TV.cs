using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    /// Keeps its own AudioSource because the on/off clip must be stoppable mid-playback, which the
    /// SoundService pool API does not expose; the mixer group comes from the category.
    public class MediaInteractable_TV : InteractableBase
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private SoundCategory category = SoundCategory.SFX_Object;
        [SerializeField] private AudioClip onClip;
        [SerializeField] private AudioClip offClip;

        [Header("Screen")]
        [SerializeField] private Renderer screenRenderer;
        [SerializeField] private Material onMaterial;
        [SerializeField] private Material offMaterial;
        [SerializeField] private int screenMaterialIndex = 0;

        private bool isOn;

        protected override void Awake()
        {
            base.Awake();
            var group = SoundService.GetGroup(category);
            if (audioSource != null && group != null) audioSource.outputAudioMixerGroup = group;
        }

        protected override void OnInteract(SlimeController slime)
        {
            isOn = !isOn;

            if (audioSource != null)
            {
                var clip = isOn ? onClip : offClip;
                if (clip != null)
                {
                    audioSource.clip = clip;
                    audioSource.Play();
                }
                else
                {
                    audioSource.Stop();
                }
            }

            if (screenRenderer != null)
            {
                var target = isOn ? onMaterial : offMaterial;
                if (target != null)
                {
                    var mats = screenRenderer.materials;
                    if (screenMaterialIndex >= 0 && screenMaterialIndex < mats.Length)
                    {
                        mats[screenMaterialIndex] = target;
                        screenRenderer.materials = mats;
                    }
                }
            }
        }
    }
}
