using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// 힘장(V2) 영역. 자식 GravityPoint2D 들의 Wendland 기여를 덧셈 합성해 중력의 방향과 크기를
    /// 함께 공급한다. V1 GravityField(방향만)·LowGravityZone(배율)과 별개 채널이며, 영역 경계
    /// featherWidth 안쪽에서는 전역 아래 중력과 블렌드해 이탈이 매끄럽다. 방향 안정화(각속도 상한,
    /// 상쇄점 freeze, 히스테리시스)는 SlimeController.GravityFieldV2 resolver 가 수행하고 이 컴포넌트의
    /// 노브가 그 값을 공급한다. 스택 소속은 LowGravityZone 과 같은 매 FixedUpdate 실측 방식.
    /// InsideDistance 는 Box/Circle/Polygon 콜라이더만 정밀 지원 — 그 외 타입은 페더 없이 내부 취급.
    /// </summary>
    public class GravityFieldV2 : EnvironmentalZone
    {
        [Tooltip("경계에서 이 폭(월드) 안쪽까지 전역 아래 중력과 블렌드한다. 0 = 페더 없음.")]
        [Min(0f)]
        [SerializeField] private float featherWidth = 2f;

        [Tooltip("gravityUp 회전 속도 상한 (도/초).")]
        [Min(1f)]
        [SerializeField] private float rotationDegPerSec = 360f;

        [Tooltip("샘플 크기가 기준 중력의 이 비율 밑이면 방향 갱신을 멈춘다 (상쇄점 보호).")]
        [Range(0.001f, 1f)]
        [SerializeField] private float lowGravityEnterRatio = 0.08f;

        [Tooltip("방향 갱신 재개 비율. Enter 보다 커야 래치가 경계에서 깜빡이지 않는다.")]
        [Range(0.001f, 1f)]
        [SerializeField] private float lowGravityExitRatio = 0.15f;

        [Tooltip("에디터 기즈모 미리보기용 기준 중력 크기 (페더 블렌드·저중력 경고 계산에만 사용).")]
        [Min(0.01f)]
        [SerializeField] private float gizmoPreviewGravity = 20f;

        public float RotationDegPerSec => rotationDegPerSec;
        public float LowGravityEnterRatio => lowGravityEnterRatio;
        public float LowGravityExitRatio => Mathf.Max(lowGravityExitRatio, lowGravityEnterRatio);

        private GravityPoint2D[] points;
        private Collider2D[] zoneColliders;

        void Awake()
        {
            points = GetComponentsInChildren<GravityPoint2D>();
            zoneColliders = GetComponents<Collider2D>();
        }

        protected override void Reset()
        {
            base.Reset();
            gameObject.layer = LayerMask.NameToLayer("GravityField");
        }

        protected override void OnSlimeEnter(SlimeController slime) { slime.PushGravityFieldV2(this); }

        protected override void OnSlimeExit(SlimeController slime) { slime.PopGravityFieldV2(this); }

        // 실측이 스택의 단일 진실. Push/Pop 둘 다 멱등이라 양방향으로 자가 복구된다.
        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            if (IsSlimeOverlapping(slime)) slime.PushGravityFieldV2(this);
            else slime.PopGravityFieldV2(this);
        }

        /// <summary>worldPos 의 가속도. 점 합성 + 경계 페더(전역 아래 중력으로 수렴).</summary>
        public Vector2 SampleAcceleration(Vector2 worldPos, float baseGravity)
        {
            if (points == null) points = GetComponentsInChildren<GravityPoint2D>();

            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Length; i++)
            {
                var p = points[i];
                if (p != null && p.isActiveAndEnabled) sum += p.Contribution(worldPos);
            }

            float w = FeatherWeight(worldPos);
            return w <= 0f ? sum : Vector2.Lerp(sum, Vector2.down * baseGravity, w);
        }

        // 경계에서 1, featherWidth 안쪽에서 0 이 되는 전역 중력 가중치 (스무스스텝 = C1).
        private float FeatherWeight(Vector2 worldPos)
        {
            if (featherWidth <= 0f) return 0f;
            float t = 1f - Mathf.Clamp01(InsideDistance(worldPos) / featherWidth);
            return t * t * (3f - 2f * t);
        }

        private float InsideDistance(Vector2 worldPos)
        {
            if (zoneColliders == null) zoneColliders = GetComponents<Collider2D>();
            float best = float.NegativeInfinity;
            for (int i = 0; i < zoneColliders.Length; i++)
            {
                var col = zoneColliders[i];
                if (col == null || !col.enabled) continue;
                best = Mathf.Max(best, InsideDistance(col, worldPos));
            }
            return best;
        }

        // 경계까지의 안쪽 거리 (밖이면 음수). 비균일 스케일은 근사 — 페더 폭 판정용이라 오차 허용.
        private static float InsideDistance(Collider2D col, Vector2 worldPos)
        {
            var tr = col.transform;

            if (col is CircleCollider2D circle)
            {
                Vector2 center = tr.TransformPoint(circle.offset);
                float scale = Mathf.Max(Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.y));
                return circle.radius * scale - Vector2.Distance(worldPos, center);
            }

            if (col is BoxCollider2D box)
            {
                Vector2 local = (Vector2)tr.InverseTransformPoint(worldPos) - box.offset;
                Vector2 half = box.size * 0.5f;
                float dx = (half.x - Mathf.Abs(local.x)) * Mathf.Abs(tr.lossyScale.x);
                float dy = (half.y - Mathf.Abs(local.y)) * Mathf.Abs(tr.lossyScale.y);
                return Mathf.Min(dx, dy);
            }

            if (col is PolygonCollider2D poly)
            {
                Vector2 local = (Vector2)tr.InverseTransformPoint(worldPos) - poly.offset;
                float minDist = float.MaxValue;
                for (int p = 0; p < poly.pathCount; p++)
                {
                    var path = poly.GetPath(p);
                    if (path.Length < 2) continue;
                    for (int i = 0; i < path.Length; i++)
                        minDist = Mathf.Min(minDist,
                            DistancePointSegment(local, path[i], path[(i + 1) % path.Length]));
                }
                float scale = Mathf.Max(Mathf.Abs(tr.lossyScale.x), Mathf.Abs(tr.lossyScale.y));
                return col.OverlapPoint(worldPos) ? minDist * scale : -minDist * scale;
            }

            return col.OverlapPoint(worldPos) ? float.MaxValue : float.MinValue;
        }

        private static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-8f));
            return Vector2.Distance(p, a + ab * t);
        }

        private bool IsSlimeOverlapping(SlimeController slime)
        {
            if (zoneColliders == null) zoneColliders = GetComponents<Collider2D>();

            var bodies = slime.BodyColliders;
            for (int z = 0; z < zoneColliders.Length; z++)
            {
                var zoneCol = zoneColliders[z];
                if (zoneCol == null || !zoneCol.enabled) continue;

                if (bodies == null)
                {
                    if (OverlapsBody(zoneCol, slime.BodyCollider)) return true;
                    continue;
                }
                for (int b = 0; b < bodies.Length; b++)
                    if (OverlapsBody(zoneCol, bodies[b])) return true;
            }
            return false;
        }

        private static bool OverlapsBody(Collider2D zoneCol, Collider2D body)
        {
            if (body == null || !body.enabled) return false;
            return zoneCol.Distance(body).isOverlapped;
        }

        // 텔레포트/override 해제 뒤엔 트리거 콜백이 안 온다. 컨트롤러가 양쪽 추적을 직접 맞춘다.
        internal void ForceExitTracking()
        {
            trackedSlime = null;
            overlappingColliderCount = 0;
        }

        // 재스캔 히트마다 1회. Push 는 멱등 — stale trackedSlime 이 남아 있어도 스택을 항상 복구한다.
        internal void ForceEnterTracking(SlimeController slime)
        {
            overlappingColliderCount++;
            trackedSlime = slime;
            slime.PushGravityFieldV2(this);
        }

        // --- Gizmos: 화살표 그리드 + 저중력 경고 ---

        private static readonly Color ArrowColor = new Color(0.8f, 0.5f, 1f, 0.55f);
        private static readonly Color LowGravityColor = new Color(1f, 0.25f, 0.25f, 0.9f);
        private static readonly Color WireColor = new Color(0.7f, 0.3f, 1f, 0.5f);

        void OnDrawGizmosSelected()
        {
            var cols = GetComponents<Collider2D>();
            if (cols.Length == 0) return;

            var bounds = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++) bounds.Encapsulate(cols[i].bounds);

            Gizmos.color = WireColor;
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            var previewPoints = GetComponentsInChildren<GravityPoint2D>();
            float step = Mathf.Max(0.5f, Mathf.Max(bounds.size.x, bounds.size.y) / 12f);
            float lowGravityThreshold = LowGravityEnterRatio * gizmoPreviewGravity;

            for (float x = bounds.min.x + step * 0.5f; x < bounds.max.x; x += step)
            {
                for (float y = bounds.min.y + step * 0.5f; y < bounds.max.y; y += step)
                {
                    var pos = new Vector2(x, y);
                    if (InsideDistanceForGizmo(cols, pos) < 0f) continue;

                    Vector2 g = SampleForGizmo(previewPoints, cols, pos);
                    float mag = g.magnitude;

                    if (mag < lowGravityThreshold)
                    {
                        Gizmos.color = LowGravityColor;
                        Gizmos.DrawSphere(pos, step * 0.07f);
                        continue;
                    }

                    float len = step * 0.42f * Mathf.Clamp01(mag / gizmoPreviewGravity);
                    Vector2 dir = g / mag;
                    Vector2 tip = pos + dir * len;
                    Vector2 perp = new Vector2(-dir.y, dir.x);
                    Gizmos.color = ArrowColor;
                    Gizmos.DrawLine(pos, tip);
                    Gizmos.DrawLine(tip, tip - dir * len * 0.3f + perp * len * 0.18f);
                    Gizmos.DrawLine(tip, tip - dir * len * 0.3f - perp * len * 0.18f);
                }
            }
        }

        // 기즈모 전용: 캐시(points/zoneColliders)를 건드리지 않고 매번 새로 계산한다.
        private Vector2 SampleForGizmo(GravityPoint2D[] previewPoints, Collider2D[] cols, Vector2 pos)
        {
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < previewPoints.Length; i++)
                if (previewPoints[i].isActiveAndEnabled) sum += previewPoints[i].Contribution(pos);

            if (featherWidth <= 0f) return sum;
            float edge = InsideDistanceForGizmo(cols, pos);
            float t = 1f - Mathf.Clamp01(edge / featherWidth);
            float w = t * t * (3f - 2f * t);
            return Vector2.Lerp(sum, Vector2.down * gizmoPreviewGravity, w);
        }

        private static float InsideDistanceForGizmo(Collider2D[] cols, Vector2 pos)
        {
            float best = float.NegativeInfinity;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] == null || !cols[i].enabled) continue;
                best = Mathf.Max(best, InsideDistance(cols[i], pos));
            }
            return best;
        }
    }
}
