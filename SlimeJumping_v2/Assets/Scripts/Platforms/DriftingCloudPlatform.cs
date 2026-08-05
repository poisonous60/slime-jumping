using UnityEngine;

namespace SlimeJumping.Platforms
{
    // 밴드를 왼→오로 흘러가는 구름 발판. 요구사항:
    // - 이동은 ComputeMotion 안에서만. 밖에서 밀면 base 가 restPos 로 되돌리고,
    //   자체 MovePosition 은 라이더에게 50Hz 톱니 떨림을 준다.
    // - wrap 은 라이더가 없을 때만. 있으면 -Width 델타가 PlatformCarrier 를 타고 슬라임을 끌고 간다.
    // - 좌우 경계에서 팽창/수축하고, 온전한 크기일 때만 밟힌다.
    // - 밟혀서 소멸/재생 중인 구름의 scale 과 콜라이더는 base 소유다.
    // - 밟혀 사라진 구름도 계속 흘러가고, 오른쪽 끝 wrap 에서 되살아난다.
    //   제자리 재생성 여부는 CloudPlatformConfig.respawn 소관 — 흘러가는 구름 전용 asset 은 false.
    public class DriftingCloudPlatform : CloudPlatform
    {
        [Header("Lane")]
        public DriftingCloudLane lane;
        public float speed = 1.2f;   // units/s. 구름마다 따로.

        float driftX;
        Transform visual;
        Vector3 visualRestScale;
        Collider2D myCol;
        bool ready;

        protected override void Awake()
        {
            base.Awake();

            if (lane == null) lane = GetComponentInParent<DriftingCloudLane>();
            if (disperser == null) disperser = GetComponentInChildren<CloudDisperser>();
            if (disperser != null && disperser.sceneCloud != null)
            {
                visual = disperser.sceneCloud.transform;
                visualRestScale = visual.localScale;
            }
            myCol = GetComponentInChildren<Collider2D>();

            ready = lane != null && visual != null && myCol != null;
            if (!ready)
                Debug.LogError($"DriftingCloudPlatform '{name}': lane/sceneCloud/collider 중 없는 것이 있어 " +
                               "drift 를 끕니다. 정지 구름으로만 동작합니다.", this);
        }

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            var (pos, rot) = base.ComputeMotion(dt);
            if (!ready) return (pos, rot);

            if (!HasRider || pos.x + driftX <= lane.MaxX)
                driftX += speed * dt;

            float x = pos.x + driftX;

            if (x > lane.MaxX && !HasRider)
            {
                float w = Mathf.Max(0.0001f, lane.Width);
                float back = Mathf.Ceil((x - lane.MaxX) / w) * w;
                driftX -= back;
                x -= back;
                if (!IsIdle) Commit();   // 왼쪽 경계 페이드가 그대로 등장 연출이 된다.
            }

            ApplyEdgeFade(x);
            return (new Vector2(x, pos.y), rot);
        }

        void ApplyEdgeFade(float x)
        {
            if (!IsIdle) return;

            float spawnW = Mathf.Max(0.0001f, lane.SpawnFadeWidth(speed));
            float despawnW = Mathf.Max(0.0001f, lane.DespawnFadeWidth(speed));
            float u = Mathf.Min(Mathf.SmoothStep(0f, 1f, (x - lane.MinX) / spawnW),
                                Mathf.SmoothStep(0f, 1f, (lane.MaxX - x) / despawnW));

            visual.localScale = visualRestScale * u;
            myCol.enabled = u >= 1f;
        }
    }
}
