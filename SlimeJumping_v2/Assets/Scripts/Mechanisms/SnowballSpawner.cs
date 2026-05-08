using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;
using SlimeJumping.Audio;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// 일정 간격으로 Snowball 인스턴스를 spawn. Unity 6 의 ObjectPool<T> 로 풀링.
    /// 단일 배치를 원하면 이 컴포넌트 없이 Snowball 만 씬에 두고 Snowball.respawnTime 양수 사용.
    /// </summary>
    public class SnowballSpawner : MonoBehaviour
    {
        [Header("Prefab")]
        [SerializeField] Snowball snowballPrefab;

        [Header("Direction Override")]
        [Tooltip("Rolling 모드 prefab 의 direction 을 덮어씀 (±1).")]
        [SerializeField] int directionOverride = 1;
        [Tooltip("Flying 모드 prefab 의 flyDirection 을 덮어씀.")]
        [SerializeField] Vector2 flyDirectionOverride = Vector2.left;

        [Header("Cadence")]
        [SerializeField] float interval = 3f;
        [SerializeField] float startDelay = 0f;
        [Range(0f, 1f)]
        [Tooltip("interval 의 ±X 비율로 jitter — 여러 스포너 동기화 깨기.")]
        [SerializeField] float intervalJitter = 0.2f;
        [Tooltip("동시에 살아있을 수 있는 최대 인스턴스 수. 도달 시 다음 spawn 스킵.")]
        [SerializeField] int maxAlive = 3;

        [Header("Spawn Cue")]
        [SerializeField] AudioClip spawnClip;
        [Range(0f, 1f)]
        [SerializeField] float spawnVolume = 1f;
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
                // 위치 먼저 → SetActive — 활성화 직전에 transform 을 옮겨야 첫 render
                // frame 에 옛 위치 (지난번 부서졌던 자리) 가 안 보임. Initialize 가 뒤이어
                // rb.position 도 같은 값으로 set 하므로 물리도 일관.
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
            s.Initialize(transform.position, directionOverride, flyDirectionOverride);
            alive.Add(s);

            if (spawnClip != null)
                SoundService.PlayAt(spawnClip, transform.position, SoundCategory.SFX_Object, spawnVolume);
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
            // 방향 화살표 (Rolling override 기준 — Flying 은 별도 표시 안 함)
            float xSign = directionOverride >= 0 ? 1f : -1f;
            Vector3 tip = transform.position + Vector3.right * (xSign * 1.2f);
            Gizmos.DrawLine(transform.position, tip);
        }
    }
}
