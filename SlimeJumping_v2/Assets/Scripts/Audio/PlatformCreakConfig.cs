using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Shared PlatformCreakAudio tuning; canonical asset <c>Assets/Resources/PlatformCreakConfig.asset</c>
    /// (auto-loaded). Per-platform variants go through the component's Config Override field.
    [CreateAssetMenu(fileName = "PlatformCreakConfig",
        menuName = "SlimeJumping/Platform Creak Config", order = 0)]
    public sealed class PlatformCreakConfig : ScriptableObject
    {
        [Tooltip("Groan played when the platform's rotation accelerates past the trigger.")]
        public SoundDef creakSound;

        [Tooltip("Rotation speed (deg/s) at which it starts to groan — a hinge squeaks by how fast " +
                 "it turns, not by what landed on it.")]
        [Min(0f)] public float triggerSpeed = 20f;

        [Tooltip("Speed must fall back under this before it can groan again, so one swing groans " +
                 "once: the slow-down at each end of the arc re-arms it.")]
        [Min(0f)] public float rearmSpeed = 8f;

        const string ResourcesPath = "PlatformCreakConfig";

        /// Override if assigned, else the Resources asset, else null — callers fall back to class defaults.
        public static PlatformCreakConfig Resolve(PlatformCreakConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<PlatformCreakConfig>(ResourcesPath);
        }
    }
}
