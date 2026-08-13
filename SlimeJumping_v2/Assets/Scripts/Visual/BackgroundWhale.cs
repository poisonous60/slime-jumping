using SlimeJumping.Platforms;
using UnityEngine;

namespace SlimeJumping.Visual
{
    // 배경 실루엣 고래: 경로(ISnakePath) 지정 시 경로 비행, 없으면 배치점 기준 직선 순환 — 양쪽 다 상하 출렁임.
    [DisallowMultipleComponent]
    public class BackgroundWhale : MonoBehaviour
    {
        [SerializeField] BackgroundWhaleConfig configOverride;
        [Tooltip("ISnakePath 구현 컴포넌트(보통 WaypointSnakePath). 웨이포인트 speedAfter 0 이면 config flySpeed 사용.")]
        [SerializeField] MonoBehaviour pathProviderBehaviour;
        [Tooltip("고래 뒤에 줄지어 따라오는 비주얼 루트들. 순서 = 대열 순서.")]
        [SerializeField] Transform[] followers;

        BackgroundWhaleConfig config;
        ISnakePath path;
        Vector3 anchor;
        Vector3 flyDirection;
        float traveled;
        float bobPhase;

        void Start()
        {
            config = BackgroundWhaleConfig.Resolve(configOverride);
            if (pathProviderBehaviour != null && pathProviderBehaviour is ISnakePath p) path = p;
            anchor = transform.position;
            flyDirection = transform.forward;
            flyDirection.y = 0f;
            if (flyDirection.sqrMagnitude < 0.0001f) flyDirection = Vector3.right;
            flyDirection.Normalize();
            bobPhase = anchor.x * 0.37f + anchor.y * 0.13f;

            var animator = GetComponentInChildren<Animator>();
            if (animator != null) animator.speed = config.swimAnimSpeed;
            if (followers == null) return;
            foreach (var f in followers)
            {
                if (f == null) continue;
                var fa = f.GetComponentInChildren<Animator>();
                if (fa != null) fa.speed = config.followerSwimAnimSpeed;
            }
        }

        void Update()
        {
            bool onPath = path != null && path.TotalArcLen > 0f;
            if (onPath)
            {
                float speed = path.SpeedAt(traveled);
                if (speed <= 0f) speed = config.flySpeed;
                traveled += speed * Time.deltaTime;
            }
            else
            {
                traveled += config.flySpeed * Time.deltaTime;
            }

            Place(transform, traveled, 0, onPath);
            if (followers == null) return;
            for (int i = 0; i < followers.Length; i++)
            {
                if (followers[i] == null) continue;
                float arc = traveled - config.followStartOffset - config.followSpacing * i;
                Place(followers[i], arc, i + 1, onPath);
            }
        }

        void Place(Transform target, float arc, int rank, bool onPath)
        {
            float bob = Mathf.Sin((Time.time * config.bobFrequency + bobPhase + rank * 0.9f) * Mathf.PI * 2f)
                        * config.bobAmplitude;

            if (onPath)
            {
                // 내장 접선은 조밀 표본 경계에서 계단식 — 전후 넓은 기준선 차분으로 연속 회전을 얻는다.
                path.SampleWorld(arc, out var pos, out _);
                float h = config.turnLookAhead * 0.5f;
                path.SampleWorld(arc - h, out var behind, out _);
                path.SampleWorld(arc + h, out var ahead, out _);
                var dir = ahead - behind;
                target.position = pos + Vector3.up * bob;
                if (dir.sqrMagnitude > 0.0001f)
                    target.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
                return;
            }

            float half = config.travelSpan * 0.5f;
            float offset = Mathf.Repeat(arc + half, config.travelSpan) - half;
            target.position = anchor + flyDirection * offset + Vector3.up * bob;
            target.rotation = Quaternion.LookRotation(flyDirection, Vector3.up);
        }
    }
}
