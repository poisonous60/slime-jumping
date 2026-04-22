using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Arc-length-parameterized 3D path source consumed by SnakeSpine.
    /// Concrete implementations: WaypointSnakePath (prototype, inspector-authored).
    /// Future: MegaShapeSnakePath (wraps a MegaShape animated Bezier).
    /// </summary>
    public interface ISnakePath
    {
        float TotalArcLen { get; }
        bool  Loop        { get; }

        /// <summary>
        /// World-space position and unit tangent at arcLen. For non-loop paths, arcLen
        /// is clamped to [0, TotalArcLen]. For loop paths, wrapped modulo TotalArcLen.
        /// </summary>
        void SampleWorld(float arcLen, out Vector3 position, out Vector3 tangent);

        /// <summary>
        /// Speed (m/s) that the head should travel at while its arc-cursor sits
        /// inside the path-segment containing arcLen.
        /// </summary>
        float SpeedAt(float arcLen);
    }
}
