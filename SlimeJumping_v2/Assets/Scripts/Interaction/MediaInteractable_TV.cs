using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    public class MediaInteractable_TV : InteractableBase
    {
        [Header("Audio")]
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip onClip;
        [SerializeField] private AudioClip offClip;

        [Header("Screen")]
        [SerializeField] private Renderer screenRenderer;
        [SerializeField] private Material onMaterial;
        [SerializeField] private Material offMaterial;
        [SerializeField] private int screenMaterialIndex = 0;

        private bool isOn;

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
