using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Slime-split integration ---
        //
        // While the slime is "wounded" (sliced), SlimeSplitSequence ramps this multiplier from a
        // reduced value back to 1 as the wound heals — a fragmented slime jumps weaker, recovering
        // full power. Default 1 = no effect, so normal play is byte-for-byte unchanged.
        //
        // Applied in Launch() to the charge-launch power only (momentum/carrier/rope inheritance
        // are NOT scaled — those are inherited motion, not jump strength).
        public float SplitLaunchMultiplier = 1f;

        // --- Cut SHAPE (the removed part is gone for collision/sticking/grounding) ---
        //
        // SlimeSplitSequence pushes the active cut planes (world 2D) here each LateUpdate. The
        // controller applies them to the surface probe every FixedUpdate (ApplySplitCollisionShape,
        // right after DecideProbeMode). The probe then collides as the CUT SHAPE via a directional
        // reach (support function of the disk minus the cut half-planes): toward the cut the slime
        // reaches only its cut plane, so it rests on / sticks by its flat cut face (no float); the
        // removed region is out of reach (no catch / no phantom stick); the center stays rb.position
        // (no transition pop). When not cut, count == 0 → probe is the full circle, byte-identical.
        private readonly Vector3[] splitCutPlanes = new Vector3[3];   // xy = world unit normal (→ removed side), z = off (world, from center)
        private int splitCutPlaneCount;

        /// <summary>
        /// Called by SlimeSplitSequence each LateUpdate. count == 0 restores the full circle.
        /// Each plane: xy = world unit normal pointing to the REMOVED side, z = signed distance
        /// from the slime center to the cut line (> 0; the core is never cut).
        /// </summary>
        public void SetSplitCutPlanes(Vector3[] planes, int count)
        {
            splitCutPlaneCount = Mathf.Clamp(count, 0, splitCutPlanes.Length);
            for (int i = 0; i < splitCutPlaneCount; i++) splitCutPlanes[i] = planes[i];
        }

        private void ApplySplitCollisionShape()
        {
            if (surfaceProbe == null) return;
            if (splitCutPlaneCount > 0) surfaceProbe.SetCutShape(splitCutPlanes, splitCutPlaneCount);
            else surfaceProbe.ClearCutShape();
        }

        // --- Launch direction bias (feel: a wounded slime launches lopsided) ---
        // SlimeSplitSequence pushes the kept-side direction + a (healLaunch-decayed) bias angle.
        // Launch() rotates the aimed launch dir toward the kept side by up to this angle — a small
        // clamped rotation, disabled while on a rope/bamboo/carrier so inherited momentum is intact.
        public Vector2 SplitLaunchKeptDir { get; private set; } = Vector2.zero;
        public float SplitLaunchBiasDeg { get; private set; }
        public void SetSplitLaunchBias(Vector2 keptDir, float deg)
        {
            SplitLaunchKeptDir = keptDir;
            SplitLaunchBiasDeg = deg;
        }
    }
}
