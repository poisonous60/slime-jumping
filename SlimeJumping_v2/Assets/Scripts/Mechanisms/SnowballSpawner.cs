using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using SlimeJumping.Audio;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    /// <summary>일정 간격으로 Snowball 을 ObjectPool 풀링해 spawn. 단일 배치는 이 컴포넌트 없이 Snowball.respawnTime 으로.</summary>
    public class SnowballSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] Snowball snowballPrefab;

        [Header("Initial Direction")]
        [Range(-180f, 180f)]
        [Tooltip("발사 각도 (도). 0 = 오른쪽, 90 = 위, 180 = 왼쪽, -90 = 아래. " +
                 "Rolling 모드는 이 각도로 ballistic 발사 후 표면에 닿으면 굴러감 (대각선/수직 가능). " +
                 "Flying 모드는 이 각도로 등속 직진.")]
        [SerializeField] float initialAngleDegrees = 0f;

        [Header("Cadence")]
        [SerializeField] float interval = 3f;
        [SerializeField] float startDelay = 0f;
        [Range(0f, 1f)]
        [Tooltip("interval 의 ±X 비율로 jitter — 여러 스포너 동기화 깨기.")]
        [SerializeField] float intervalJitter = 0.2f;
        [Tooltip("동시에 살아있을 수 있는 최대 인스턴스 수. 도달 시 다음 spawn 스킵.")]
        [SerializeField] int maxAlive = 3;

        [Header("Spawn Cue")]
        [SerializeField] SlimeJumping.Audio.SoundDef spawnSound;
        [Range(0f, 1f)]
        [Tooltip("Spawn 시 instantiate 할 ParticleSystem prefab. 자체 lifetime 으로 destroy 처리.")]
        [SerializeField] ParticleSystem spawnVFX;

        ObjectPool<Snowball> pool;
        readonly HashSet<Snowball> alive = new();
        float spawnTimer;

        void Awake()
        {
            pool = new ObjectPool<Snowball>(
                createFunc: () =>
                {
                    var s = Instantiate(snowballPrefab, transform);
                    s.gameObject.SetActive(false);
                    s.OnDespawn += HandleDespawn;
                    return s;
                },
                // 위치 먼저 → SetActive: 순서 뒤집으면 첫 render frame 에 옛 위치가 보임.
                actionOnGet:     s =>
                {
                    s.transform.position = transform.position;
                    s.gameObject.SetActive(true);
                },
                actionOnRelease: s => s.gameObject.SetActive(false),
                actionOnDestroy: s =>
                {
                    if (s != null && s.gameObject != null)
                        Destroy(s.gameObject);
                },
                collectionCheck: false,
                defaultCapacity: Mathf.Max(1, maxAlive),
                maxSize:         Mathf.Max(1, maxAlive));
            spawnTimer = startDelay;
        }

        void OnDestroy()
        {
            pool?.Clear();
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            if (snowballPrefab == null) return;

            spawnTimer -= Time.deltaTime;
            if (spawnTimer <= 0f)
            {
                if (alive.Count < maxAlive) Spawn();
                float jitterFactor = 1f + Random.Range(-intervalJitter, intervalJitter);
                spawnTimer = Mathf.Max(0.05f, interval * jitterFactor);
            }
        }

        void Spawn()
        {
            var s = pool.Get();
            float rad = initialAngleDegrees * Mathf.Deg2Rad;
            Vector2 launchDir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            s.Initialize(transform.position, launchDir);
            alive.Add(s);

            SoundService.Play(spawnSound, transform.position);
            if (spawnVFX != null)
                Instantiate(spawnVFX, transform.position, Quaternion.identity);
        }

        void HandleDespawn(Snowball s)
        {
            alive.Remove(s);
            if (pool != null) pool.Release(s);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.85f, 1f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.4f);
            float rad = initialAngleDegrees * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
            Vector3 tip = transform.position + dir * 1.2f;
            Gizmos.DrawLine(transform.position, tip);
        }
    }
}
