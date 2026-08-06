using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    /// <summary>Shared tuning for <see cref="BellRevealCamera"/>; canonical asset auto-loaded from
    /// <c>Assets/Resources/BellRevealCameraConfig.asset</c>, per-instance override optional.
    ///
    /// The reveal owns its blend times instead of riding the Brain's DefaultBlend, which is global —
    /// retuning it there would also retime RainWorldZone's zone enter/exit.</summary>
    [CreateAssetMenu(fileName = "BellRevealCameraConfig", menuName = "SlimeJumping/Camera/Bell Reveal Camera Config", order = 1)]
    public sealed class BellRevealCameraConfig : ScriptableObject
    {
        private const string ResourcesPath = "BellRevealCameraConfig";
        private static BellRevealCameraConfig fallback;

        [Header("Hold — how long the region stays on screen")]
        [Tooltip("Seconds the camera stays parked on the region. A default sound wave lives about 4.5s (maxRadius/waveSpeed + frontWidth/waveSpeed + hold + fade).")]
        [Min(0.1f)] public float holdSeconds = 4.5f;
        [Tooltip("ON: the hold starts when the camera arrives, so this value is the time the region is actually still — total away = blendIn + hold + blendOut. OFF: the hold starts at the strike and the blend in eats into it.")]
        public bool holdStartsOnArrival = true;

        [Tooltip("ON: once holdSeconds is up the reveal still waits while the player stays hands-off, and the first move or charge sends the camera back. OFF: it leaves on the timer alone.")]
        public bool holdUntilIdleInput = true;
        [Tooltip("ON: while the camera is on the region, a wave that has finished expanding stops aging, so the platforms it revealed do not fade out from under the shot. Resumes on the way back.")]
        public bool freezeRevealWhileShown = true;

        [Header("Transition — this reveal only, not the Brain's global blend")]
        [Tooltip("Seconds to travel from the slime to the region.")]
        [Min(0f)] public float blendInSeconds = 2f;
        [Tooltip("Seconds to travel back to the slime.")]
        [Min(0f)] public float blendOutSeconds = 2f;
        [Tooltip("Curve of both transitions. Cut = instant, Rain World style.")]
        public CinemachineBlendDefinition.Styles blendStyle = CinemachineBlendDefinition.Styles.EaseInOut;

        [Header("Audio")]
        [Tooltip("ON: while the camera is away the listening point stays with the slime, so the bell and every other 3D sound keep the mix they had. OFF: the listener rides the camera and distant sounds attenuate and pan wrong.")]
        public bool keepAudioAtSlime = true;

        [Header("Framing")]
        [Tooltip("World margin added around the region box so its edges are not flush with the screen.")]
        [Min(0f)] public float regionPadding = 1f;

        [Header("Arbitration")]
        [Tooltip("Priority while revealing. Must beat every other VCam, RainWorldZone's active room included (that one uses 100).")]
        public int activePriority = 200;
        [Tooltip("Priority while idle. Must sit below the global SlimeVCam so the reveal never wins by accident.")]
        public int idlePriority = -100;

        public static BellRevealCameraConfig Resolve(BellRevealCameraConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<BellRevealCameraConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<BellRevealCameraConfig>();
                Debug.LogWarning("BellRevealCameraConfig: no Assets/Resources/BellRevealCameraConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > " +
                                 "Camera > Bell Reveal Camera Config.");
            }
            return fallback;
        }
    }
}
