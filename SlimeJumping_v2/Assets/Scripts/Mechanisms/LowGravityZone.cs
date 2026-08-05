using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// 구간 내부에서 중력 크기에만 배율을 건다. 방향은 GravityField 소관이라 둘을 겹쳐 놓으면
    /// 방향은 GravityField, 크기는 이쪽이 결정한다. 겹침은 LIFO — 마지막 진입 zone 이 이긴다.
    ///
    /// 스택 소속은 트리거 enter/exit 카운터가 아니라 매 FixedUpdate 실측한 겹침으로 정한다.
    /// 카운터는 콜라이더 on/off · IgnoreCollision · 존 재활성에서 새고, 한 번 새면 영구히 갇힌다.
    /// </summary>
    public class LowGravityZone : EnvironmentalZone
    {
        [Tooltip("구간 안 중력 배율. 0.35 = 원래의 35%. 1 = 무효과, 0 = 무중력.")]
        [Range(0f, 1f)]
        [SerializeField] private float gravityScale = 0.35f;

        [Tooltip("끄면 자식 'Visual' 이펙트를 비활성화한다. 스테이지 전체를 덮는 큰 존은 꺼서 쓴다.")]
        [SerializeField] private bool showVisualEffect = true;

        public float GravityScale => gravityScale;

        private Collider2D[] zoneColliders;

        void Awake()
        {
            zoneColliders = GetComponents<Collider2D>();
            ApplyVisualToggle();
        }

        protected override void Reset()
        {
            base.Reset();
            gameObject.layer = LayerMask.NameToLayer("GravityField");
        }

        protected override void OnSlimeEnter(SlimeController slime) { slime.PushLowGravityZone(this); }

        protected override void OnSlimeExit(SlimeController slime) { slime.PopLowGravityZone(this); }

        // 실측이 스택의 단일 진실. Push/Pop 둘 다 멱등이라 양방향으로 자가 복구된다.
        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            if (IsSlimeOverlapping(slime)) slime.PushLowGravityZone(this);
            else slime.PopLowGravityZone(this);
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

        // 재스캔 히트마다 1회. 본문 순서는 베이스 OnTriggerEnter2D 와 같다.
        internal void ForceEnterTracking(SlimeController slime)
        {
            overlappingColliderCount++;
            if (trackedSlime == slime) return;

            trackedSlime = slime;
            slime.PushLowGravityZone(this);
        }

        private void ApplyVisualToggle()
        {
            var visual = transform.Find("Visual");
            if (visual != null && visual.gameObject.activeSelf != showVisualEffect)
                visual.gameObject.SetActive(showVisualEffect);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // OnValidate 안에서 SetActive 는 금지 — 다음 에디터 틱으로 미룬다.
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null) ApplyVisualToggle();
            };
        }
#endif

        // --- Gizmos ---

        private static readonly Color GizmoFill = new Color(0.3f, 0.9f, 0.85f, 0.12f);
        private static readonly Color GizmoWire = new Color(0.3f, 0.9f, 0.85f, 0.6f);

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            if (col is CircleCollider2D circle)
            {
                Vector3 center = transform.TransformPoint(circle.offset);
                float worldRadius = circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
                Gizmos.color = GizmoFill;
                Gizmos.DrawSphere(center, worldRadius);
                Gizmos.color = GizmoWire;
                Gizmos.DrawWireSphere(center, worldRadius);
            }
            else if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset), transform.rotation, transform.lossyScale);
                Gizmos.color = GizmoFill;
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = GizmoWire;
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }
            else if (col is PolygonCollider2D poly)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = GizmoWire;
                Vector2 off = poly.offset;
                for (int p = 0; p < poly.pathCount; p++)
                {
                    var points = poly.GetPath(p);
                    if (points.Length < 2) continue;
                    for (int i = 0; i < points.Length; i++)
                        Gizmos.DrawLine(
                            (Vector3)(points[i] + off),
                            (Vector3)(points[(i + 1) % points.Length] + off));
                }
                Gizmos.matrix = old;
            }

            // 배율이 낮을수록 위로 뜨는 화살표를 길게: 한눈에 강도 비교
            Gizmos.color = GizmoWire;
            float len = Mathf.Lerp(1.8f, 0.2f, gravityScale);
            Vector3 basePos = col.bounds.center;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 start = basePos + Vector3.right * (i * 0.5f);
                Vector3 tip = start + Vector3.up * len;
                Gizmos.DrawLine(start, tip);
                Gizmos.DrawLine(tip, tip + new Vector3(-0.12f, -0.22f, 0f));
                Gizmos.DrawLine(tip, tip + new Vector3(0.12f, -0.22f, 0f));
            }
        }
    }
}
