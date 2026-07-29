using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>Arc-length-parameterized 3D path source consumed by SnakeSpine.</summary>
    public interface ISnakePath
    {
        float TotalArcLen { get; }
        bool  Loop        { get; }

        /// <summary>World position + unit tangent at arcLen; clamped on open paths, wrapped on loops.</summary>
        void SampleWorld(float arcLen, out Vector3 position, out Vector3 tangent);

        /// <summary>Head speed (m/s) while its arc-cursor sits in the segment containing arcLen.</summary>
        float SpeedAt(float arcLen);
    }
}
