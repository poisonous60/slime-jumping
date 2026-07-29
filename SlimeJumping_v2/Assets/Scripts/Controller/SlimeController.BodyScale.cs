using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Cut = smaller round slime. Single source of physics truth: effective radius is
        // settings.slimeRadius * bodyScaleRatio, fed to the CircleCollider2D and to every
        // radius-derived length (probe, CCD sweep, anchors, launch clearance) via SlimeRadius.
        private float bodyScaleRatio = 1f;

        // Floor against a degenerate slime that tunnels or divides by ~0 in radius-derived math.
        private const float MinBodyScale = 0.25f;

        public float BodyScaleRatio => bodyScaleRatio;

        /// <summary>Set by SlimeSplitSequence: instant drop on cut, slow ramp back to 1 while healing.</summary>
        public void SetBodyScale(float ratio)
        {
            ratio = Mathf.Clamp(ratio, MinBodyScale, 1f);
            if (ratio == bodyScaleRatio) return;
            bodyScaleRatio = ratio;
            ApplyBodyScale();
        }

        private void ApplyBodyScale()
        {
            // pinchColliderScale shrinks ONLY the collider + probe during a squeeze, decoupled from
            // the visual (which reads BodyScaleRatio). 1f outside a pinch.
            float effective = bodyScaleRatio * pinchColliderScale;
            if (circleCollider != null)
            {
                float s = transform.localScale.x;
                float r = settings.slimeRadius * effective;
                circleCollider.radius = s > 0.001f ? r / s : r;
            }
            if (surfaceProbe != null)
                surfaceProbe.SetRadiusScale(effective);
        }
    }
}
