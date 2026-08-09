using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// GravityFieldV2 점 중력원. Wendland C2 힘 프로파일 — 중심과 반경 경계에서 0, 반경 25% 지점에서
    /// peakAccel 최대. 경계에서 힘과 1차 미분이 모두 0이라 겹치는 점들은 단순 덧셈으로 합성해도
    /// C1 연속이 유지된다. radius 는 월드 단위이며 transform 스케일의 영향을 받지 않는다.
    /// </summary>
    public class GravityPoint2D : MonoBehaviour
    {
        [Tooltip("영향 반경 (월드 단위). 반경 밖은 정확히 0.")]
        [Min(0.01f)]
        [SerializeField] private float radius = 6f;

        [Tooltip("최대 중력 가속도. 반경 25% 지점에서 이 값에 도달한다.")]
        [Min(0f)]
        [SerializeField] private float peakAccel = 20f;

        public float Radius => radius;

        // q(1-q)³ 최대값이 27/256 (q=0.25) — 역수를 곱해 |g| 최대치를 peakAccel 에 맞춘다.
        private const float PeakNormalizer = 256f / 27f;

        /// <summary>worldPos 에서의 가속도 기여. 반경 밖과 정중심은 0.</summary>
        public Vector2 Contribution(Vector2 worldPos)
        {
            Vector2 toCenter = (Vector2)transform.position - worldPos;
            float dist = toCenter.magnitude;
            if (dist < 0.0001f) return Vector2.zero;

            float q = dist / radius;
            if (q >= 1f) return Vector2.zero;

            float f = 1f - q;
            float magnitude = peakAccel * PeakNormalizer * q * f * f * f;
            return toCenter * (magnitude / dist);
        }

        // --- Gizmos ---

        private static readonly Color WireColor = new Color(0.7f, 0.3f, 1f, 0.6f);
        private static readonly Color PeakColor = new Color(1f, 0.6f, 0.2f, 0.5f);

        void OnDrawGizmos()
        {
            Gizmos.color = WireColor;
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawSphere(transform.position, 0.08f);
            // 주황 링 = 최대 세기 지점 (0.25R)
            Gizmos.color = PeakColor;
            Gizmos.DrawWireSphere(transform.position, radius * 0.25f);
        }
    }
}
