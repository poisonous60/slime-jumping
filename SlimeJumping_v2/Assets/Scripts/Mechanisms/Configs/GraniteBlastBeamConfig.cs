using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Per-mode tuning for GraniteBlastEmitter Beam mode.</summary>
    [CreateAssetMenu(fileName = "GraniteBlastBeamConfig",
        menuName = "SlimeJumping/Granite Blast/Beam Config", order = 1)]
    public class GraniteBlastBeamConfig : ScriptableObject
    {
        [Tooltip("Time the beam takes to fade out at the end of its life.")]
        public float beamFadeTime = 0.18f;
        [Tooltip("Core line width for the beam.")]
        public float beamCoreWidth = 0.55f;
        [Tooltip("Beam tip travel speed in world units per second. 0 = instant full-length beam.")]
        public float beamTravelSpeed = 80f;
        [Tooltip("Angular tracking speed (deg/sec) toward the lagged slime position. 0 = legacy frozen direction. >0 = re-sweep + sustained behavior + first-contact impulse + per-frame accel.")]
        public float beamTrackAngularSpeed = 90f;
        [Tooltip("Aim lag (sec). The beam targets the slime's position from this many seconds ago.")]
        public float beamTrackLag = 0.25f;
        [Tooltip("How beamTrackLag is applied during the beam's lifetime.\n• InitialLagThenLive: initial aim is snapshot at the lagged position, then the beam rotates toward the LIVE slime position (deg/sec capped).\n• ContinuousLag: every frame the rotation target is the lagged trail position.")]
        public GraniteBlastEmitter.BeamTrackMode beamTrackMode = GraniteBlastEmitter.BeamTrackMode.InitialLagThenLive;
        [Tooltip("Surface-slide multiplier applied to beam pushes while the slime is grounded (Attached/Charging). 1 = full slide, 0 = no slide while in beam, 0.3 = 30% slide.")]
        [Range(0f, 1f)]
        public float beamGroundedSlideScale = 0.3f;
        [Tooltip("When true (default), beam hits OVERWRITE the slime's surface slide instead of adding to it. False = legacy additive impulse + accumulating accel hybrid.")]
        public bool beamOverwriteSlide = true;
        [Tooltip("Pinned tangent speed (m/s) used by the overwrite channel. Each frame the beam hits a grounded slime, the slime's tangent velocity is set to pushDir * (this) * beamGroundedSlideScale.")]
        public float beamOverwritePinnedSpeed = 14f;
    }
}
