using System;
using UnityEngine;
using SlimeJumping.MeshTools;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.Snow;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Single source of truth for the candidate component types that should be wrapped in
    /// an <see cref="OffscreenSleepable"/>. Shared by:
    ///   - <see cref="OffscreenSleeperManager.RuntimeFallbackScan"/> — adds default markers
    ///     to instances the editor batch tool missed (or that were authored after it last ran).
    ///   - The editor batch tool (Tools/Optimization/...) — bulk attaches markers to all
    ///     prefab/scene instances.
    ///
    /// Tier classification (see docs/features/optimization/offscreen-sleeper.md for full
    /// reasoning). Adding a new type here also requires:
    ///   1. Re-running the editor batch tool to attach markers
    ///   2. A playtest pass to confirm SetActive toggle doesn't break the new component
    ///
    /// Long-range physics dependencies (Snowball/GraniteBlast trajectories): instances whose
    /// collider must remain active even when the slime is far away should be marked
    /// `forceAlwaysActive = true` on their OffscreenSleepable manually in the Inspector.
    /// That's an instance-level escape hatch — the candidate type stays in this list so
    /// other instances still get the proximity gate.
    /// </summary>
    public static class OffscreenSleeperCandidates
    {
        public static readonly Type[] CandidateTypes = new Type[]
        {
            // Tier 1 — freeze on toggle is harmless (rest-state physics, visual loops, AI)
            typeof(TautLine),
            typeof(WaterZone_v2),
            typeof(WaterCurrentZone),
            typeof(ClothField),
            typeof(SawBladeHazard),
            typeof(WaterfallImpactVFX),
            typeof(ElevatorJoltVisual),
            typeof(PushDoorV2),
            typeof(PushDoor),
            typeof(TurretHazard),
            typeof(CCTVTracker),

            // Tier 2A — freeze when off-screen is acceptable; visual will resume from
            // last-active angle. Player won't see the discontinuity since the object was
            // off-screen during the freeze window.
            typeof(RotatingPlatform),

            // Tier 2B — static mesh-derived terrain. Largest contributor to broadphase
            // proxy count (one wrapper averages ~13 Box2D shapes due to LibTess
            // tessellation into ≤8-vert convex sub-polygons). Sleeping far-off terrain
            // shrinks the broadphase tree N. Instances on Snowball/GraniteBlast trajectories
            // must be marked forceAlwaysActive in the Inspector — see class doc above.
            typeof(MeshCollider2DWrapper),

            // SnowField is intentionally excluded. Empirically (2026-05-10 capture
            // analysis), SnowField.OnEnable's RebuildNow path costs ~287ms self-time per
            // call — when several SnowFields wake simultaneously (which happens whenever
            // the camera viewport grows, e.g. F10 Fullscreen Editor), the cumulative
            // wake spike causes a visible 0.5s stutter. Sleep cycles aren't worth that
            // cost; SnowFields stay always-active and their broadphase contribution is
            // accepted.
        };

        /// <summary>
        /// Returns true if <paramref name="go"/> hosts a candidate type but should NOT receive
        /// an OffscreenSleepable marker because its hierarchy contains a component that breaks
        /// under SetActive cycles. Currently the only such gate is SnowField — present anywhere
        /// in the GameObject's hierarchy (self or any descendant) — because:
        ///   - Sleeping the host SetActive(false)s the SnowField, whose OnDisable clears
        ///     activeStrips. Without strips, the field renders nothing on wake.
        ///   - Re-rebuilding strips on wake would cost ~287ms self-time per SnowField (mesh-
        ///     derived strip regeneration), and Fullscreen Editor's larger viewport routinely
        ///     triggers multi-SnowField simultaneous wakes → cumulative 0.5s stutter.
        ///
        /// Used by both <see cref="OffscreenSleeperManager.RuntimeFallbackScan"/> and the
        /// editor batch tool (Tools/Optimization/Attach...) so attach decisions stay consistent
        /// across runtime and authoring time.
        /// </summary>
        public static bool ShouldSkipAttach(GameObject go)
        {
            return go.GetComponentInChildren<SnowField>(true) != null;
        }
    }
}
