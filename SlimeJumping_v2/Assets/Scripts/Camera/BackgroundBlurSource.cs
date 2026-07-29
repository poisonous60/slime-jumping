using UnityEngine;
using LeTai.Asset.TranslucentImage;

namespace SlimeJumping.Camera
{
    /// Blur source for the background camera. Drops the editor-only blur region
    /// handle overlay that the base class draws whenever Preview is on.
    [ExecuteAlways]
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class BackgroundBlurSource : TranslucentImageSource
    {
#if UNITY_EDITOR
        protected override void OnGUI() { }
#endif
    }
}
