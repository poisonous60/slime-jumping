using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterCurrentZone
    {
        [Header("Wave Simulation")]
        [Tooltip("Hooke restore coefficient. Higher = stiffer, shorter wave period. " +
                 "Default 1.25 matches WaterZone_v2's calibration (0.025/frame at 50Hz).")]
        [Min(0f)] [SerializeField] float springK = 1.25f;
        [Tooltip("Velocity damping per second. Higher = faster wake decay. " +
                 "Default 2.0 matches WaterZone_v2 (0.04/frame at 50Hz).")]
        [Min(0f)] [SerializeField] float damping = 2.0f;
        [Tooltip("Neighbor propagation coefficient — how fast the wake travels along the ribbon. " +
                 "Default 2.5 matches WaterZone_v2 (0.05/frame at 50Hz).")]
        [Min(0f)] [SerializeField] float spread = 2.5f;

        // Scratch arrays for the two-pass spring integration; resized when the sample count changes.
        float[] deltaVL, deltaVR;

        void EnsureSimBuffers(int n)
        {
            if (deltaVL == null || deltaVL.Length != n) deltaVL = new float[n];
            if (deltaVR == null || deltaVR.Length != n) deltaVR = new float[n];
        }

        /// <summary>Two-pass spring sim, L/R channels independent. Neighbor clamp (not modulo wrap) so endpoints reflect instead of wrapping.</summary>
        void SimulateWaves(float dt)
        {
            if (points == null || points.Length < 2 || dt <= 0f) return;
            int n = points.Length;
            EnsureSimBuffers(n);

            // Pass 1: accumulate accel into deltaV[].
            for (int i = 0; i < n; i++)
            {
                int l = i > 0     ? i - 1 : i;
                int r = i < n - 1 ? i + 1 : i;

                float diffLL = points[i].dispL - points[l].dispL;
                float diffRL = points[i].dispL - points[r].dispL;
                deltaVL[i] = (-spread * (diffLL + diffRL)
                              - springK * points[i].dispL
                              - damping * points[i].velL) * dt;

                float diffLR = points[i].dispR - points[l].dispR;
                float diffRR = points[i].dispR - points[r].dispR;
                deltaVR[i] = (-spread * (diffLR + diffRR)
                              - springK * points[i].dispR
                              - damping * points[i].velR) * dt;
            }

            // Pass 2: integrate.
            for (int i = 0; i < n; i++)
            {
                points[i].velL  += deltaVL[i];
                points[i].dispL += points[i].velL * dt;
                points[i].velR  += deltaVR[i];
                points[i].dispR += points[i].velR * dt;
            }
        }
    }
}
