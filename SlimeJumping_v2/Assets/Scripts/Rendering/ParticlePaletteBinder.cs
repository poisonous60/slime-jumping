using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// <summary>
    /// Binds a ParticleSystem's start color to the VFXColorPalette.
    /// Attach to a GameObject with a ParticleSystem. Select a VFXType in the inspector
    /// and the particle colors update automatically (edit-time via OnValidate, runtime via Awake).
    /// </summary>
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
