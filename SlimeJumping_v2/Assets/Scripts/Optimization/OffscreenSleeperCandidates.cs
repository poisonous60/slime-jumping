using System;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.Snow;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Single source of truth for the 13 candidate component types that should be wrapped in
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
            typeof(SnowField),
        };
    }
}
