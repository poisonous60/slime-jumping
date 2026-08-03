using System;
using UnityEngine;
using SlimeJumping.MeshTools;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.Snow;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Candidate component types for <see cref="OffscreenSleepable"/>, shared by
    /// <see cref="OffscreenSleeperManager.RuntimeFallbackScan"/> and the editor batch tool —
    /// adding a type here requires re-running that tool to attach markers.
    /// Instances on long-range physics trajectories (Snowball/GraniteBlast) need
    /// `forceAlwaysActive` set per-instance in the Inspector.
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

            // Tier 2A — resumes from its last-active angle; the discontinuity happens off-screen.
            typeof(RotatingPlatform),

            // Tier 2B — mesh terrain, the largest broadphase contributor (~13 Box2D shapes per
            // wrapper from LibTess). Trajectory-critical instances need forceAlwaysActive.
            typeof(MeshCollider2DWrapper),

            // SnowField excluded on purpose: OnEnable's RebuildNow costs ~287ms self-time, and a
            // viewport growth wakes several at once → visible 0.5s stutter.
        };

        /// <summary>True when <paramref name="go"/> hosts a candidate but must not sleep: a SnowField
        /// anywhere in its hierarchy clears activeStrips on disable and costs ~287ms to rebuild, and a
        /// WaypointPlatform's journeyProgress is a FixedUpdate accumulator — sleeping freezes its phase
        /// while platforms that stayed awake keep advancing, so authored choreography drifts apart.</summary>
        public static bool ShouldSkipAttach(GameObject go)
        {
            return go.GetComponentInChildren<SnowField>(true) != null
                || go.GetComponentInChildren<WaypointPlatform>(true) != null;
        }
    }
}
