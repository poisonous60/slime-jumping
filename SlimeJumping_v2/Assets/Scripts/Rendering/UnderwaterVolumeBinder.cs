using UnityEngine;
using UnityEngine.Rendering;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// <summary>Fades the wired URP Volume (underwater profile) in/out with SlimeController.InFluid.</summary>
    public class UnderwaterVolumeBinder : MonoBehaviour
    {
        [SerializeField] Volume volume;
        [SerializeField] SlimeController slime;
        [Tooltip("Weight units per second. ~5 = ~0.2s fade.")]
        [SerializeField] float fadeSpeed = 5f;

        void Reset()
        {
            volume = GetComponent<Volume>();
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
        }

        void Update()
        {
            if (volume == null || slime == null) return;
            float target = slime.InFluid ? 1f : 0f;
            volume.weight = Mathf.MoveTowards(volume.weight, target, fadeSpeed * Time.deltaTime);
        }
    }
}
