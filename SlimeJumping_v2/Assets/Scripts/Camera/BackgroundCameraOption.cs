using UnityEngine;
using LeTai.Asset.TranslucentImage;
using SlimeJumping.Settings;

namespace SlimeJumping.Camera
{
    /// Live-applies GameOptions.BackgroundCameraEnabled: culling mask → 0 (the camera's clear still paints the
    /// backdrop) and TranslucentImageSource Preview+enabled off — Preview must go too, else a stale frame stays.
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class BackgroundCameraOption : MonoBehaviour
    {
        [Tooltip("The TranslucentImageSource on this BG camera. Preview + enabled are toggled with the option.")]
        [SerializeField] private TranslucentImageSource translucentImageSource;

        private UnityEngine.Camera bgCamera;
        private int originalCullingMask;
        private bool originalPreview;
        private bool subscribed;

        private void Awake()
        {
            bgCamera = GetComponent<UnityEngine.Camera>();
            originalCullingMask = bgCamera.cullingMask;
            if (translucentImageSource != null) originalPreview = translucentImageSource.Preview;
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
            if (bgCamera != null) bgCamera.cullingMask = on ? originalCullingMask : 0;
            if (translucentImageSource != null)
            {
                translucentImageSource.Preview = on && originalPreview;
                translucentImageSource.enabled = on;
            }
        }
    }
}
