using UnityEngine;

namespace SlimeJumping.VFX.Waterfall
{
    /// <summary>
    /// 물 튀김 입자를 그 자리 휠 표면 속도로 실어 보낸다.
    ///
    /// 휠 콜라이더가 돌면 레이 명중점이 표면을 따라 미끄러지는데, 입자는 World 공간이라
    /// 태어난 자리에 박혀 세로 잔상으로 남는다. 명중점에서 가장 가까운 휠의 접선 속도를
    /// velocityOverLifetime 에 넣어 입자가 표면을 따라가게 한다.
    ///
    /// WaterfallImpactVFX 가 Splash 를 런타임 복제하므로 이 컴포넌트도 같이 복제된다.
    /// </summary>
    // WaterfallImpactVFX 가 LateUpdate 에서 명중점으로 옮긴 뒤에 읽어야 한 프레임 늦지 않는다.
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class SplashWheelCarry : MonoBehaviour
    {
        [Header("휠 (+각속도 = 반시계)")]
        [SerializeField] private Transform[] wheels;
        [SerializeField] private float[] degreesPerSecond;

        [Tooltip("각 휠의 테 반경. 대형 9.15, 소형 2.75 처럼 크기가 달라 휠마다 따로 준다.")]
        [SerializeField] private float[] rimRadius;

        [Tooltip("테에서 이만큼 벗어난 지점은 그 휠 것으로 안 본다. 지면 충돌 지점을 거르는 값.")]
        [SerializeField] private float carryBand = 1.5f;

        [Tooltip("표면 속도를 그대로 주면 너무 빨라 보인다. 실어 보내는 비율.")]
        [Range(0f, 1f)] [SerializeField] private float carryScale = 0.75f;

        private ParticleSystem ps;

        private void Awake() => ps = GetComponent<ParticleSystem>();

        private void LateUpdate()
        {
            if (ps == null || wheels == null || rimRadius == null || degreesPerSecond == null) return;

            Vector2 p = transform.position;
            Vector2 carry = Vector2.zero;
            float best = carryBand;

            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                Vector2 r = p - (Vector2)w.position;
                float d = r.magnitude;
                if (d < 1e-3f) continue;
                float rim = i < rimRadius.Length ? rimRadius[i] : 0f;
                float off = Mathf.Abs(d - rim);
                if (off > best) continue;
                best = off;
                float omega = (i < degreesPerSecond.Length ? degreesPerSecond[i] : 0f) * Mathf.Deg2Rad;
                carry = new Vector2(-r.y, r.x) * (omega * carryScale);
            }

            var vol = ps.velocityOverLifetime;
            vol.enabled = true;
            vol.space = ParticleSystemSimulationSpace.World;
            vol.x = new ParticleSystem.MinMaxCurve(carry.x);
            vol.y = new ParticleSystem.MinMaxCurve(carry.y);
            vol.z = new ParticleSystem.MinMaxCurve(0f);
        }
    }
}
