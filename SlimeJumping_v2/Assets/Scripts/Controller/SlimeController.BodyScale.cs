using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Body scale (slime-cut: "a cut slime is a smaller slime") ---
        //
        // When the slime is cut it loses volume and becomes a smaller round slime; healing grows
        // it back. Physics truth is this single ratio: the effective radius is
        // settings.slimeRadius * bodyScaleRatio, applied to the CircleCollider2D and to every
        // radius-derived length (probe reach/broadphase, CCD sweep, rope/bamboo anchors, launch
        // clearance) via the SlimeRadius property. The smaller slime therefore genuinely
        // collides, rests, and sticks at its smaller size — no hidden geometry, no cut planes.
        // 1 = full size: normal play is byte-for-byte unchanged.
        //
        // Growth while resting on a surface needs no special handling: the collider expands a
        // tiny amount per frame and the probe's normal depenetration push lifts the slime.
        private float bodyScaleRatio = 1f;

        // Safety floor — gameplay decides actual cut depths, this only guards against a
        // degenerate slime that could tunnel or divide by ~0 in radius-derived math.
        private const float MinBodyScale = 0.25f;

        public float BodyScaleRatio => bodyScaleRatio;

        /// <summary>
        /// Set by SlimeSplitSequence: an instant drop on cut, a slow ramp back to 1 while
        /// healing. Applied to the collider (and probe) immediately.
        /// </summary>
        public void SetBodyScale(float ratio)
        {
            ratio = Mathf.Clamp(ratio, MinBodyScale, 1f);
            if (ratio == bodyScaleRatio) return;
            bodyScaleRatio = ratio;
            ApplyBodyScale();
        }

        private void ApplyBodyScale()
        {
            // pinchColliderScale (SlimeController.Pinch.cs) shrinks ONLY the collider + probe during a
            // squeeze, decoupled from the visual (which reads BodyScaleRatio). 1f outside a pinch, so
            // normal play and the cut path are byte-for-byte unchanged.
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
