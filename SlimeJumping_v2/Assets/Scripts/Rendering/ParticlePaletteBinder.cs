using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// <summary>Binds the ParticleSystem's start color to the VFXColorPalette entry for the chosen VFXType.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    public class ParticlePaletteBinder : MonoBehaviour
    {
        [SerializeField] private VFXColorPalette palette;
        [SerializeField] private VFXType vfxType;

        private void Awake()
        {
            ApplyPalette();
        }

        private void OnValidate()
        {
            ApplyPalette();
        }

        private void ApplyPalette()
        {
            if (palette == null) return;

            var ps = GetComponent<ParticleSystem>();
            if (ps == null) return;

            if (!palette.TryGetColors(vfxType, out Color primary, out Color secondary))
                return;

            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(primary, secondary);
        }
    }
}
