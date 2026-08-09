using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// GravityFieldV2 점 중력원. 튜닝(radius/peakAccel/forceProfile)은 전부 GravityPointConfig 로 —
    /// 기본은 Resources canonical asset, 변종만 configOverride. 힘 분포 곡선은 x = 중심→반경 0..1,
    /// y = 0..1 × peakAccel, 기본 곡선은 Wendland 모양(중심 0 → 0.25R 최대 → 경계 0).
    /// 영향권 안에서는 전역 중력을 무시하고 이 점(들)의 힘만 적용된다 — 이행은 GravityFieldV2 의
    /// dominanceEdgeFraction 구간이 담당. radius 는 월드 단위, transform 스케일 영향 없음.
    /// </summary>
    public class GravityPoint2D : MonoBehaviour
    {
        [Tooltip("비우면 Resources/GravityPointConfig.asset 사용. 반경/세기가 다른 변종만 지정.")]
        [SerializeField] private GravityPointConfig configOverride;

        private GravityPointConfig cachedConfig;

        private GravityPointConfig Config
        {
            get
            {
                if (cachedConfig == null) cachedConfig = GravityPointConfig.Resolve(configOverride);
                return cachedConfig;
            }
        }

        public float Radius => Config.radius;
        public AnimationCurve ForceProfile => Config.forceProfile;

        void OnValidate() { cachedConfig = null; }

        /// <summary>worldPos 에서의 가속도 기여. 반경 밖과 정중심은 0.</summary>
        public Vector2 Contribution(Vector2 worldPos)
        {
            Vector2 toCenter = (Vector2)transform.position - worldPos;
            float dist = toCenter.magnitude;
            if (dist < 0.0001f) return Vector2.zero;

            var cfg = Config;
            float q = dist / cfg.radius;
            if (q >= 1f) return Vector2.zero;

            // Clamp01 = 베이크 텍스처(R8)와 동일 범위 — 곡선이 1을 넘어도 물리·비주얼이 같이 캡된다.
            float magnitude = cfg.peakAccel * Mathf.Clamp01(cfg.forceProfile.Evaluate(q));
            return toCenter * (magnitude / dist);
        }

        /// <summary>중심 기준 정규화 거리 (반경 = 1).</summary>
        public float NormalizedDistance(Vector2 worldPos)
        {
            return Vector2.Distance(transform.position, worldPos) / Config.radius;
        }

        // --- Gizmos ---

        private static readonly Color WireColor = new Color(0.7f, 0.3f, 1f, 0.6f);
        private static readonly Color PeakColor = new Color(1f, 0.6f, 0.2f, 0.5f);

        void OnDrawGizmos()
        {
            var cfg = Config;
            Gizmos.color = WireColor;
            Gizmos.DrawWireSphere(transform.position, cfg.radius);
            Gizmos.DrawSphere(transform.position, 0.08f);
            // 주황 링 = forceProfile 최대 세기 지점
            Gizmos.color = PeakColor;
            Gizmos.DrawWireSphere(transform.position, cfg.radius * FindPeakQ(cfg.forceProfile));
        }

        private static float FindPeakQ(AnimationCurve profile)
        {
            float bestQ = 0.25f, bestV = float.MinValue;
            for (int i = 0; i <= 32; i++)
            {
                float q = i / 32f;
                float v = Mathf.Clamp01(profile.Evaluate(q));
                if (v > bestV) { bestV = v; bestQ = q; }
            }
            return bestQ;
        }
    }
}
