using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Platforms;

namespace SlimeJumping.Effects
{
    /// 로프가 늘어난 만큼 우는 소리. Bamboo 와 같은 결이지만 기준이 다르다 — 대나무는 착지가
    /// 예고하는 발사 세기로 한 번 울고, 로프는 지금 얼마나 끌려 내려가 있는지를 계속 따라간다.
    /// 그래서 착지 임펄스로 깊게 처졌다가 스프링이 되돌리는 동안 소리도 같이 오르내린다.
    /// 착지·발사음은 TautLine 의 Rope Surface Material 이 담당하므로 여기서 중복해 울지 않는다.
    /// 실행 순서 300 은 TautLine(200) 이 이번 틱 dip 을 다 쓴 뒤에 읽기 위한 것.
    [DefaultExecutionOrder(300)]
    [RequireComponent(typeof(TautLine))]
    [AddComponentMenu("SlimeJumping/Effects/Rope Stretch Audio")]
    public class RopeStretchAudio : MonoBehaviour
    {
        [Tooltip("비우면 Assets/Resources/RopeStretchAudioConfig.asset 공유 튜닝.")]
        [SerializeField] RopeStretchAudioConfig configOverride;

        RopeStretchAudioConfig config;
        TautLine rope;
        readonly StrainLoop loop = new StrainLoop();

        void Awake()
        {
            rope = GetComponent<TautLine>();
            config = RopeStretchAudioConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<RopeStretchAudioConfig>();
        }

        void OnDisable() => loop.Stop();

        void Update()
        {
            float depth = Mathf.Abs(rope.DebugDipDepth);
            float strain = Mathf.InverseLerp(config.startDepth,
                                             Mathf.Max(config.fullDepth, config.startDepth + 0.01f),
                                             depth);

            Vector3 at = rope.Polygon != null ? rope.Polygon.bounds.center : transform.position;
            loop.Drive(config.stretchSound, at, strain, config.pitchAtStart, config.pitchAtFull);
        }
    }
}
