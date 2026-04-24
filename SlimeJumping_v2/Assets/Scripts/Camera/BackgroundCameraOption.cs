using UnityEngine;
using LeTai.Asset.TranslucentImage;
using SlimeJumping.Settings;

namespace SlimeJumping.Camera
{
    /// Live-applies GameOptions.BackgroundCameraEnabled by toggling the BG
    /// camera's culling mask and the TranslucentImage blur source on it. When
    /// off, the BG camera still does its SolidColor clear (so the stage
    /// backdrop color stays visible) but renders no Background-layer objects,
    /// and the blur pass is skipped (Preview off + TIS disabled) to drop the
    /// per-frame blit cost without leaving a stale preview frame on screen.
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
