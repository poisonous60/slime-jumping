using UnityEngine;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Per-platform surface-probe mode override (found via GetComponentInParent, reverts to
    /// SlimeSettings.surfaceProbeMode on leave). Per-platform, not scene-wide, because
    /// MultiContact — needed for faceted-ice tremor — breaks on moving thin colliders that
    /// are themselves the slime's surface (a SpringyBranch sweeping past it).
    /// </summary>
    public class SurfaceProbeModeOverride : MonoBehaviour
    {
        [Tooltip("Probe mode to use while the slime is attached to a collider under this GameObject.")]
        public SurfaceProbeMode mode = SurfaceProbeMode.Single;
    }
}
