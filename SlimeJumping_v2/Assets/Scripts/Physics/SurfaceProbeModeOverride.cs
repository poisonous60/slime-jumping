using UnityEngine;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Per-platform override of the slime's surface-probe mode. Place on a platform
    /// GameObject (or any ancestor of its collider) — the slime detects it via
    /// GetComponentInParent on surface change and swaps probe mode. Reverts to
    /// SlimeSettings.surfaceProbeMode when the slime leaves.
    ///
    /// Why per-platform, not per-scene: MultiContactSurfaceProbe (edge-enumeration +
    /// teleport-then-snap) is needed for faceted ice tremor suppression, but breaks
    /// on moving thin colliders that are themselves the slime's current surface
    /// (rotating SpringyBranch sweeping past the slime). Scenes can mix both surface
    /// types, so the toggle must be per-collider, not scene-wide.
    /// </summary>
    public class SurfaceProbeModeOverride : MonoBehaviour
    {
        [Tooltip("Probe mode to use while the slime is attached to a collider under this GameObject.")]
        public SurfaceProbeMode mode = SurfaceProbeMode.Single;
    }
}
