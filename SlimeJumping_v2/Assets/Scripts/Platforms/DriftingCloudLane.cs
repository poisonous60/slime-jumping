using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class DriftingCloudLane : MonoBehaviour
    {
        [Header("Config")]
        public DriftingCloudLaneConfig configOverride;   // 비우면 Assets/Resources/DriftingCloudLaneConfig.asset.

        DriftingCloudLaneConfig cfg;
        public DriftingCloudLaneConfig Cfg
        {
            get
            {
#if UNITY_EDITOR
                // 에디터에선 매번 다시 풀어야 configOverride 를 바꾼 게 즉시 먹는다.
                if (!Application.isPlaying) return DriftingCloudLaneConfig.Resolve(configOverride);
#endif
                if (cfg == null) cfg = DriftingCloudLaneConfig.Resolve(configOverride);
                return cfg;
            }
        }

        // 폭 = 그 구름의 속도 × 시간. 속도가 달라도 자라고 줄어드는 시간은 같다.
        public float SpawnFadeWidth(float speed) => ClampFade(speed * Cfg.spawnFadeTime,
                                                              speed * Cfg.despawnFadeTime);
        public float DespawnFadeWidth(float speed) => ClampFade(speed * Cfg.despawnFadeTime,
                                                                speed * Cfg.spawnFadeTime);
        public Vector2 BandSize => Cfg.bandSize;

        // 두 폭의 합이 밴드보다 넓으면 온전한 크기 구간이 사라져 구름을 영영 못 밟는다.
        float ClampFade(float mine, float other)
        {
            mine = Mathf.Max(0f, mine);
            float sum = mine + Mathf.Max(0f, other);
            float w = Cfg.bandSize.x;
            return sum > w && sum > 0.0001f ? mine * (w / sum) : mine;
        }

        public float MinX => transform.position.x - Cfg.bandSize.x * 0.5f;
        public float MaxX => transform.position.x + Cfg.bandSize.x * 0.5f;
        public float Width => Cfg.bandSize.x;

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            var c = Cfg;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(c.bandSize.x, c.bandSize.y, 0f));
            Gizmos.matrix = Matrix4x4.identity;

            foreach (var cloud in GetComponentsInChildren<DriftingCloudPlatform>())
            {
                float speed = cloud.speed;
                Vector3 p = cloud.transform.position;

                // 화살표 길이 = 1 초 이동거리.
                Vector3 tip = p + Vector3.right * speed;
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
                Gizmos.DrawLine(p, tip);
                Gizmos.DrawLine(tip, tip + new Vector3(-0.25f, 0.14f, 0f));
                Gizmos.DrawLine(tip, tip + new Vector3(-0.25f, -0.14f, 0f));
                UnityEditor.Handles.Label(p + Vector3.up * 0.35f, $"{cloud.name}  {speed:0.##} u/s");

                Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.5f);
                float sx = MinX + SpawnFadeWidth(speed);
                float dx = MaxX - DespawnFadeWidth(speed);
                Gizmos.DrawLine(new Vector3(sx, p.y - 0.3f, p.z), new Vector3(sx, p.y + 0.3f, p.z));
                Gizmos.DrawLine(new Vector3(dx, p.y - 0.3f, p.z), new Vector3(dx, p.y + 0.3f, p.z));
            }
        }
#endif
    }
}
