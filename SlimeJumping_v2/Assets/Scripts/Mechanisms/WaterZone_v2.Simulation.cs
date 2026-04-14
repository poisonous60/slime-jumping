using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterZone_v2
    {
        float[] deltaV;

        void EnsureSimBuffers()
        {
            if (deltaV == null || deltaV.Length != points.Length)
                deltaV = new float[points.Length];
        }

        void SimulateWaves(float dt)
        {
            if (points == null || points.Length == 0 || dt <= 0f) return;
            EnsureSimBuffers();

            int n = points.Length;

            // Pass 1: compute velocity delta from neighbor propagation + Hooke + damping.
            // Closed loop has no directional bias — single pass is unbiased.
            for (int i = 0; i < n; i++)
            {
                int l = (i - 1 + n) % n;
                int r = (i + 1) % n;
                float diffL = points[i].displacement - points[l].displacement;
                float diffR = points[i].displacement - points[r].displacement;

                float accel = -spread * (diffL + diffR)
                            - springK * points[i].displacement
                            - damping * points[i].velocity;
                deltaV[i] = accel * dt;
            }

            // Pass 2: integrate.
            for (int i = 0; i < n; i++)
            {
                points[i].velocity     += deltaV[i];
                points[i].displacement += points[i].velocity * dt;
            }
        }
    }
}
