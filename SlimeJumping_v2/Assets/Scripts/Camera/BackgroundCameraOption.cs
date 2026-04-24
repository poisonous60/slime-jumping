using UnityEngine;
using UnityEngine.Rendering.Universal;
using SlimeJumping.Settings;

namespace SlimeJumping.Camera
{
    /// Live-applies GameOptions.BackgroundCameraEnabled to the URP Base camera
    /// this sits on. When disabled, the paired Main Camera is flipped from
    /// Overlay to Base so the scene still renders, and the TranslucentImage
    /// blur source (if any) is switched off to drop the per-frame blit cost.
    /// Intended for the background camera documented in MainScene_성능_프로파일_2026-04-24.md.
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class BackgroundCameraOption : MonoBehaviour
    {
        [Tooltip("The Overlay camera paired with this Base BackgroundCamera. Flipped to Base while the background camera is off.")]
        [SerializeField] private UnityEngine.Camera mainCamera;

        [Tooltip("Optional. Disabled alongside this camera so its blur blit stops too. Typically the TranslucentImageSource on this GameObject.")]
        [SerializeField] private Behaviour translucentImageSource;

        private UnityEngine.Camera bgCamera;
        private UniversalAdditionalCameraData mainData;
        private bool subscribed;

        private void Awake()
        {
            bgCamera = GetComponent<UnityEngine.Camera>();
            if (mainCamera != null) mainData = mainCamera.GetComponent<UniversalAdditionalCameraData>();
        }

        private void OnEnable()
        {
            GameOptions.BackgroundCameraEnabledChanged += Apply;
            subscribed = true;
            Apply(GameOptions.BackgroundCameraEnabled);
        }

        private void OnDisable()
        {
            if (subscribed)
            {
                GameOptions.BackgroundCameraEnabledChanged -= Apply;
                subscribed = false;
            }
        }

        private void Apply(bool on)
        {
            if (bgCamera != null) bgCamera.enabled = on;
            if (translucentImageSource != null) translucentImageSource.enabled = on;
            if (mainData != null)
                mainData.renderType = on ? CameraRenderType.Overlay : CameraRenderType.Base;
        }
    }
}
