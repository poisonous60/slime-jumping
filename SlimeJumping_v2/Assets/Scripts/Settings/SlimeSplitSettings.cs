using UnityEngine;

namespace SlimeJumping.Settings
{
    /// <summary>
    /// "잘림 = 작아진 둥근 슬라임" 튜닝 (지속 단면 없음):
    ///   • 본체 — 부피를 잃고 실제로 작아짐(콜라이더+비주얼). 비주얼은 스프링으로 출렁이며 뭉치고,
    ///     잘린 직후 잠깐 보이는 평평한 단면이 몸이 줄어들며 원형으로 닫힘(transition).
    ///   • 조각 — 손실 부피만큼의 자기 블롭 메쉬가 sliver→droplet 으로 펴지며 날아가 melt.
    ///   • 회복 — recoveryDuration 동안 크기·점프력이 각자 곡선으로 복귀.
    /// ScriptableObject — 전부 인스펙터 튜닝, 코드에 매직넘버 없음.
    /// </summary>
    [CreateAssetMenu(menuName = "SlimeJumping/Slime Split Settings", fileName = "SlimeSplitSettings")]
    public class SlimeSplitSettings : ScriptableObject
    {
        // ───────────────────────── 컷 ─────────────────────────

        [Header("컷 — 부피 손실")]
        [Range(0.01f, 0.9f)]
        [Tooltip("블레이드 컷 한 번에 잃는 부피 (현재 부피의 비율 f). 조각 반경 = 몸반경×f^⅓, 본체 반경 ×(1-f)^⅓. 0.3 ≈ 눈에 띄게 작아지지만 멀쩡한 슬라임.")]
        public float cutVolumeFraction = 0.3f;
        [Range(0.01f, 0.9f)]
        [Tooltip("공중 셀프 분열(2단 점프 연료)이 내보내는 부피 비율. 반동 임펄스는 게임플레이 쪽 책임 — 여기선 몸을 얼마나 떼어주는지만.")]
        public float selfSplitVolumeFraction = 0.25f;
        [Tooltip("조각 사출 방향에 더하는 랜덤 회전 (±deg). 매번 똑같은 컷으로 안 보이게.")]
        public float cutNormalJitterDeg = 12f;
        [Tooltip("두 컷 사이 최소 간격 (초). 같은 프레임/이중 해저드 재트리거 가드. 블레이드 자체 hitCooldown이 1차 게이트.")]
        public float recutMinInterval = 0.15f;

        [Header("컷 — 본체 출렁임 (비주얼 스프링)")]
        [Tooltip("표시 크기가 물리 크기를 쫓아가는 스프링 강성. 클수록 빠르고 탱탱하게 뭉침. ω≈√k rad/s (60 ≈ 1.2Hz).")]
        [Min(1f)] public float bodyScaleSpringK = 60f;
        [Tooltip("스프링 감쇠. 낮을수록 출렁임(오버슛)이 크고 오래감, 높을수록 출렁임 없이 바로 정착. 임계값 = 2√k (k=60 → ≈15.5).")]
        [Min(0f)] public float bodyScaleSpringDamping = 7f;

        [Header("컷 — 과도기 (HOLD 단면 → CLOSE 아물며 작아짐)")]
        [Tooltip("HOLD: 잘린 직후 풀사이즈+평평한 단면을 유지하는 박자 (초). 조각의 sliver 박자(pieceSharpBeat)와 맞추면 '방금 잘렸다'가 한 호흡으로 읽힘.")]
        [Min(0f)] public float cutHoldDuration = 0.15f;
        [Tooltip("CLOSE: 단면이 아물면서 몸이 작은 슬라임으로 줄어드는 과도기 길이 (초). 몸 크기(물리+비주얼)와 단면 후퇴가 이 시간 동안 각자 곡선으로 진행.")]
        [Min(0.05f)] public float closeDuration = 0.5f;
        [Tooltip("CLOSE 동안 몸 크기 진행 0→1 (0=풀사이즈, 1=컷 후 크기). 물리 콜라이더가 이 곡선대로 줄고 비주얼 스프링이 그 위를 쫓아감(goo 추종+잔여 출렁). 끝에서 급히 떨어지는 모양 = 머뭇대다 한 번에 뭉침.")]
        public AnimationCurve closeBodyCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("CLOSE 동안 단면 아묾 진행 0→1 (0=부피 깊이 그대로, 1=메쉬 밖까지 후퇴=면적 0, 완전히 닫힘). 몸 곡선과 독립 — 늦게 두면 작아진 몸에 상처가 남아있다 닫히고, 빠르면 먼저 아문 뒤 줄어듦. 1에 도달하는 시점이 곧 단면이 사라지는 시점.")]
        public AnimationCurve closeSealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("컷 — 과도기 레이어 2: 비대칭 정착 (몸이 잘린 쪽으로 당겨짐)")]
        [Range(0f, 1.5f)]
        [Tooltip("CLOSE 동안 몸(비주얼)이 잘린 면 쪽으로 끌려가는 양. 1 = 잃은 반경만큼(잘린 면은 제자리, 반대쪽 살이 흘러와 채우는 느낌), 0 = 제자리 균일 축소.")]
        public float gatherAmount = 1f;
        [Tooltip("당겨짐의 시간 모양 0→1→0 (X = CLOSE 진행도, Y = 당겨짐 비율). 기본: 빠르게 쏠렸다가 닫히면서 중심으로 복귀.")]
        public AnimationCurve gatherCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));

        [Header("컷 — 상처 look (아무는 동안의 단면 표현)")]
        [Range(0f, 4f)]
        [Tooltip("아물수록 단면이 볼록하게 부푸는 정도 (표면장력 도밍). 0 = 끝까지 평평한 채 가장자리부터 닫힘(기본). closeSealCurve 진행 × 이 값으로 셰이더에서 휨 — 원하면 살짝만.")]
        public float cutRoundnessMax = 0f;
        [Range(0f, 0.3f)]
        [Tooltip("갓 잘린 가장자리의 거친 찢김 노이즈 (OS). 아물수록(=seal 진행) 매끈해짐. 0 = 처음부터 깨끗한 직선.")]
        public float tearAmp = 0.05f;
        [Range(0.5f, 8f)]
        [Tooltip("찢김 노이즈 주파수. 낮게 — 부드러운 굴곡 몇 개, 좁쌀 노이즈 아님.")]
        public float tearFreq = 2.5f;
        [Range(0.02f, 0.6f)]
        [Tooltip("아무는 가장자리의 표면장력 입술(lip) 폭 (OS, 반경 비율). 봉합될수록 도드라짐.")]
        public float lipBand = 0.18f;
        [Range(0f, 0.3f)]
        [Tooltip("입술이 바깥으로 부푸는 크기 (OS). 0 = 입술 없음.")]
        public float lipMag = 0.06f;
        [ColorUsage(false, false)]
        [Tooltip("아무는 가장자리에 차오르는 새살 goo 색. 몸색보다 살짝 밝고 젖은 톤.")]
        public Color sliceHealColor = new Color(0.6f, 0.88f, 0.55f, 1f);
        [Range(0f, 1f)]
        [Tooltip("새살 goo 띠의 최대 강도. 0 = 표시 안 함.")]
        public float sliceHealIntensity = 0.7f;
        [Tooltip("새살 goo 띠의 폭 (OS, 반경 비율).")]
        public float sliceHealWidth = 0.3f;
        [ColorUsage(false, false)]
        [Tooltip("단면 속(깊은 쪽) 색. 몸색보다 어둡게 — 열린 단면이 깊은 점액으로 읽힘. transition 동안만 보임.")]
        public Color sliceCapColor = new Color(0.18f, 0.32f, 0.18f, 1f);
        [ColorUsage(false, false)]
        [Tooltip("단면 가장자리(열린 입구) 젖은 색. 속색보다 밝게.")]
        public Color sliceCapRimColor = new Color(0.45f, 0.7f, 0.42f, 1f);
        [Range(0.02f, 1f)]
        [Tooltip("입구→속 색이 변하는 깊이 (OS, 슬라임 반경 비율).")]
        public float sliceCapDepth = 0.35f;

        // ───────────────────────── 조각 ─────────────────────────

        [Header("조각 — 사출 (슬라임과 같은 중력)")]
        [Range(0f, 1f)]
        [Tooltip("슬라임 진입 속도를 조각이 물려받는 비율.")]
        public float inheritVelocityFactor = 0.5f;
        [Tooltip("사출 방향 바깥쪽 팝 (범위 내 랜덤).")]
        public float severImpulseMin = 2f;
        public float severImpulseMax = 4f;
        [Tooltip("위쪽 살짝 킥 — 조각이 호를 그리며 떨어지게.")]
        public float upwardImpulse = 1.5f;
        public float linearDamping = 0.05f;
        public float angularDamping = 0.05f;
        [Range(0.1f, 1f)]
        [Tooltip("조각 CircleCollider2D 반경 = 메쉬 절반 크기 × 이 값.")]
        public float colliderRadiusFactor = 0.7f;
        [Tooltip("스핀 (deg/s) 범위. 둘 다 0 = 회전 없음.")]
        public float spinMinDeg = 0f;
        public float spinMaxDeg = 0f;
        [Tooltip("안전장치: 착지 못하면(구덩이) 이 시간 후 공중에서 melt.")]
        public float severedMaxAirTime = 4f;

        [Header("조각 — 표면장력 round-up (납작 sliver → 둥근 droplet)")]
        [Tooltip("스폰 시 사출축 방향 납작 정도. 0.1 = 아주 납작.")]
        [Range(0.02f, 1f)]
        public float pieceFlatten = 0.12f;
        [Tooltip("납작한 sliver 모양을 유지하는 박자 (초). 본체 cutHoldDuration과 맞추면 한 호흡.")]
        public float pieceSharpBeat = 0.15f;
        [Tooltip("sliver → 둥근 droplet 으로 펴지는 시간 (초).")]
        public float pieceRoundUpDuration = 0.35f;
        [Tooltip("펴짐 0→1 ease. 비우면 ease-in-out.")]
        public AnimationCurve pieceRoundUpCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("조각 — 점성 출렁임")]
        public float jiggleAmplitude = 0.35f;
        public float jiggleFrequency = 9f;
        public float jiggleDecay = 3.5f;
        [Tooltip("바운스가 출렁임 envelope를 재충전하는 양.")]
        public float landingJiggleBoost = 1f;
        [Range(0f, 1f)]
        [Tooltip("비행 중 셰이더 velocity-stretch(물방울꼴) 강도.")]
        public float velocityStretchScale = 0.5f;
        public float wobbleSpeedRef = 12f;

        [Header("조각 — melt (착지 후 녹아내림)")]
        [Tooltip("스폰 직후 이 시간 동안은 충돌해도 melt 시작 안 함 — 일단 날아가서 착지부터.")]
        public float meltContactGrace = 0.2f;
        [Tooltip("착지 후 녹아 사라지는 시간 (초).")]
        public float meltDuration = 1.2f;
        [Range(0f, 3f)]
        [Tooltip("납작해지며 옆으로 퍼지는 양.")]
        public float meltSpread = 0.8f;
        [Tooltip("착지 시 발밑 지면 탐색 추가 거리 (m). 퍼들 바닥을 그 면에 고정 — 공중에 뜨거나 바닥을 뚫지 않게.")]
        public float meltGroundProbe = 0.5f;
        [Tooltip("melt 지면 탐색 레이어. Nothing = 슬라임 레이어 제외 전부 (권장).")]
        public LayerMask groundMask = 0;
        [Range(0f, 1.5f)]
        [Tooltip("구덩이 폴백 전용 (발밑 지면 없음). 1 = 바닥 고정, >1 가라앉음, <1 떠오름.")]
        public float meltSinkFactor = 1f;

        [Header("조각 — melt 얼룩 (WorldTrail)")]
        [Tooltip("melt 자리에 찍는 얼룩 월드 반경. 0 = 얼룩 없음.")]
        public float meltStampRadius = 0.45f;
        [Range(0f, 1f)]
        [Tooltip("얼룩 마스크 강도.")]
        public float meltStampStrength = 0.7f;
        [Tooltip("melt 중 얼룩 재스탬프 간격 (초) — 퍼들이 퍼지는 만큼 얼룩도 넓어짐.")]
        public float meltStampInterval = 0.12f;

        // ───────────────────────── 회복 ─────────────────────────

        [Header("회복 — 크기 재성장 (~20–60s, 조작 가능)")]
        [Tooltip("전체 회복 시간 (초). 이 동안 작은 채로 조작 가능, 끝나면 풀사이즈.")]
        public float recoveryDuration = 30f;
        [Tooltip("크기 회복 0→1 곡선: BodyScaleRatio → 1. '낮게 머물다 끝에 차오름' 모양 추천. t=1에서 반드시 1.")]
        public AnimationCurve recoveryCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 0.15f), new Keyframe(1f, 1f));
        [Tooltip("점프력 회복 0→1 곡선: SplitLaunchMultiplier → 1. 크기 곡선과 독립 — 작아 보여도 힘은 먼저 돌아오게 (또는 반대로) 가능.")]
        public AnimationCurve launchRecoveryCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 0.15f), new Keyframe(1f, 1f));

        [Header("회복 — 마지막 뿅")]
        [Min(0f)]
        [Tooltip("크기 회복이 완료되는 순간 비주얼 스프링에 주는 바깥쪽 속도 킥 — '뿅' 하고 한 번 출렁이며 풀사이즈 복귀 선언. 0 = 조용히 끝남.")]
        public float regrowPopKick = 0.6f;

        [Header("회복 — 점프력")]
        [Range(0f, 1f)]
        [Tooltip("컷 직후 점프력 배율. launchRecoveryCurve 따라 1로 복귀. 1 = 감소 없음.")]
        public float woundedJumpMultiplierMin = 0.5f;
    }
}
