using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Per-mode tuning for GraniteBlastEmitter Branching mode. Created as a
    /// ScriptableObject so multiple emitters can share a config asset, and
    /// designers can swap configs without dirtying the scene.
    /// Scene-level references (e.g. <see cref="GraniteBlastNavGraph"/>) stay on
    /// the emitter — only project-asset-safe values live here.
    /// </summary>
    [CreateAssetMenu(fileName = "GraniteBlastBranchingConfig",
        menuName = "SlimeJumping/Granite Blast/Branching Config", order = 0)]
    public class GraniteBlastBranchingConfig : ScriptableObject
    {
        [Tooltip("Layers that count as walls for NavGraph LOS gating. Used by IsDirectPathClear (direct shortcut) and ClosestVisibleNode (entry/exit selection). Should NOT include the slime layer. Default = Environment (layer 7). 0 disables LOS gating.")]
        public LayerMask navWallMask = (1 << 7);
        [Tooltip("Per-edge cost jitter in A* (multiplicative ±this). 0 = deterministic shortest path. Higher values let alternative shortest-ish paths win randomly so a fan of tendrils takes diverse routes through the graph. Typical 0.2–0.5.")]
        [Range(0f, 1f)]
        public float navPathCostJitter = 0.35f;
        [Tooltip("Entry/exit node randomization. 0 = always pick the closest LOS-visible node. >0 lets each tendril pick a random visible node within (1+this) × nearestDist. Typical 0.2–0.5.")]
        [Range(0f, 1f)]
        public float navEntryJitter = 0.3f;
        [Tooltip("Smooth low-frequency perpendicular wander (world units) applied to a tendril's NavGraph polyline. 0 = bolt sticks to waypoint segments. >0 = bolt snakes around the path with Perlin noise.")]
        public float tendrilPathNoiseAmplitude = 0.5f;
        [Tooltip("Cycles-per-world-unit of the path-following noise. Higher = tighter wobble; lower = broad slow waves. Typical 0.3–1.5.")]
        public float tendrilPathNoiseFrequency = 0.8f;

        public int tendrilCount = 7;
        [Tooltip("World-space horizontal jitter (±units) on the captured slime XY before computing the fire direction.")]
        public float targetRadius = 0f;
        [Tooltip("World-space upward bow added to Bezier control points. 0 = perfectly straight bolt. Higher = bolt arches upward.")]
        public float bowHeight = 0f;
        [Tooltip("World-space Z bow added to Bezier control points (depth). 0 = bolt stays in muzzle's Z plane.")]
        public float bowDepthZ = 0f;
        [Tooltip("World-space perpendicular jitter (±units) on each Bezier control point.")]
        public float controlPointJitter = 0.3f;
        [Tooltip("World-space Z jitter (±units) on each Bezier control point. Independent from controlPointJitter — adds depth wobble per tendril.")]
        public float controlPointZJitter = 0f;
        [Tooltip("World-space distance per Bezier segment. Total segments per tendril = path-length / this, clamped to [6, 48].")]
        public float tendrilSegmentLength = 0.6f;
        [Tooltip("Max perpendicular noise (units) along the curve, scaled by segment length.")]
        public float tendrilJaggedness = 0.35f;
        [Tooltip("Projectile travel speed in world units per second.")]
        public float tendrilTravelSpeed = 60f;
        [Tooltip("Fallback fixed travel time (sec) for the tip to traverse the full maxRange. Used only when tendrilTravelSpeed <= 0.")]
        public float tendrilDrawTime = 0.2f;
        public float tendrilHoldTime = 0.18f;
        public float tendrilFadeTime = 0.28f;
        [Tooltip("Core line width for tendrils.")]
        public float tendrilCoreWidth = 0.12f;
        [Tooltip("Fraction of tendrils spawned at FireStart. The rest are staggered through Firing.")]
        [Range(0f, 1f)]
        public float branchingFireStartFraction = 0.3f;

        [Tooltip("Chase spread (sec). At FireStart, the N tendrils are spread across the slime's recorded XY history.")]
        public float trailDuration = 0.6f;
        [Tooltip("Base lag (sec). 0 = newest tendril uses FireStart position. Combined with trailDuration, the OLDEST tendril references (trailLagOffset + trailDuration) seconds ago.")]
        public float trailLagOffset = 0f;
        [Tooltip("Sample interval for trail recording (sec).")]
        public float trailSampleInterval = 0.04f;
    }
}
