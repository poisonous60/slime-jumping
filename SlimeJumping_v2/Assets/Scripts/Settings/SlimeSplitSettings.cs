using UnityEngine;

namespace SlimeJumping.Settings
{
    /// <summary>"잘림 = 작아진 둥근 슬라임 + 자크식 회수" 튜닝 (지속 단면 없음).</summary>
    /// 본체는 부피를 잃고 실제로 작아지며 메니스커스로 봉합, 조각은 날아가 잔존하다 근접 시 흡수 = 즉시 회복.
    /// 조각 분실 시에만 slowRecoveryDuration 기반 느린 회복. 점프력 페널티는 몸집 비율에서 파생(독립 타이머 없음).
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
        [Tooltip("CLOSE 동안 몸 크기 진행 0→1 (0=풀사이즈, 1=컷 후 크기). 물리 콜라이더가 이 곡선대로 줄고 비주얼 스프링이 그 위를 쫓아감(goo 추종+잔여 출렁). 끝에서 급히 떨어지는 모양 = 머뭇대다 한 번에 뭉침. 상처 봉합은 이 보이는 수축에 잠금(독립 곡선 없음) — 몸이 줄어드는 만큼 정확히 닫힘.")]
        public AnimationCurve closeBodyCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("컷 — 과도기 레이어 2: 비대칭 정착 (몸이 잘린 쪽으로 당겨짐)")]
        [Range(0f, 1.5f)]
        [Tooltip("CLOSE 동안 몸(비주얼)이 잘린 면 쪽으로 끌려가는 양. 1 = 잃은 반경만큼(잘린 면은 제자리, 반대쪽 살이 흘러와 채우는 느낌), 0 = 제자리 균일 축소.")]
        public float gatherAmount = 1f;
        [Tooltip("당겨짐의 시간 모양 0→1→0 (X = CLOSE 진행도, Y = 당겨짐 비율). 기본: 빠르게 쏠렸다가 닫히면서 중심으로 복귀.")]
        public AnimationCurve gatherCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f));

        [Header("컷 — 메니스커스 (상처 막의 스프링 동역학)")]
        [Tooltip("봉합 진행(타깃 곡선)을 쫓는 막 스프링 강성. 클수록 빠르고 탱탱하게 차오름.")]
        [Min(1f)] public float meniscusSpringK = 90f;
        [Tooltip("막 스프링 감쇠. 임계값 = 2√k (k=90 → ≈19). 낮을수록 차오른 뒤 출렁임이 큼.")]
        [Min(0f)] public float meniscusSpringDamping = 9f;
        [Range(0f, 0.5f)]
        [Tooltip("HOLD 동안 단면이 파여 있는 깊이 (잘린 캡 높이 비율). 날이 파고든 자국 — 0 = 완전 평면에서 시작.")]
        public float cutConcaveDepthFrac = 0.12f;

        [Header("컷 — 봉합 잔여 출렁 (봉합 직후 → 작은 슬라임 사이 점성 보간)")]
        [Tooltip("봉합 완료 후 부풀음이 가라앉는 감쇠율. 클수록 빨리 잦아듦 (5 ≈ 0.8초 안에 소멸).")]
        public float sealRelaxDecay = 5f;
        [Tooltip("가라앉는 동안 위로만 살짝 튕기는 바운스의 진동수 (Hz). 출렁임의 박자.")]
        public float sealRelaxFreq = 2.5f;
        [Range(0f, 0.9f)]
        [Tooltip("바운스 세기. 부풀음 높이가 가라앉는 궤적 위에서 이만큼 출렁임 — 0 = 단조 감쇠, 반전(안쪽 함몰) 없음.")]
        public float sealRelaxBounce = 0.35f;
        [Range(0f, 4f)]
        [Tooltip("가라앉는 동안 부풀음의 발(폭)이 퍼지는 양 — 점성 액체가 납작하게 퍼지며 녹는 핵심 단서. 1.5 = 폭이 최대 2.5배까지 넓어지며 낮아짐.")]
        public float sealRelaxSpread = 1.5f;
        [Range(0f, 2f)]
        [Tooltip("봉합 완료 순간 상처 부위에 주는 잔물결 킥 — 막이 합쳐지는 '뭉클' 반동. RELAX 동안은 저주파 슬로시로 흐름.")]
        public float sealRelaxWobbleKick = 0.9f;

        [Header("컷 — 타격 잔동 (HOLD 퀴버)")]
        [Range(0f, 0.3f)]
        [Tooltip("잘린 순간 단면+림이 부들부들 떨리는 진폭 (OS, 메쉬 반경 비율). 0 = 퀴버 없음.")]
        public float quiverAmp = 0.05f;
        [Tooltip("퀴버 진동수 (Hz).")]
        public float quiverFreq = 16f;
        [Tooltip("퀴버 감쇠율. 클수록 빨리 잦아듦 (5 ≈ 0.6초 내 소멸).")]
        public float quiverDecay = 5f;
        [Tooltip("퀴버의 공간 주파수 (경계를 따라 물결 개수 느낌).")]
        public float quiverSpatialFreq = 7f;

        [Header("컷 — 상처 look (아무는 동안의 단면 표현)")]
        [Range(0f, 0.3f)]
        [Tooltip("갓 잘린 가장자리의 거친 찢김 노이즈 (OS). 아물수록(=seal 진행) 매끈해짐. 0 = 처음부터 깨끗한 직선.")]
        public float tearAmp = 0.05f;
        [Range(0.5f, 8f)]
        [Tooltip("찢김 노이즈 주파수. 낮게 — 부드러운 굴곡 몇 개, 좁쌀 노이즈 아님.")]
        public float tearFreq = 2.5f;
        [Range(0.02f, 0.6f)]
        [Tooltip("단면 테두리(림) 밴드 폭 (OS, 반경 비율). 이 폭 안의 살이 아래 두 값만큼 움직인다.")]
        public float lipBand = 0.18f;
        [Range(0f, 0.3f)]
        [Tooltip("림 필렛: 절단 코너 바로 안쪽 살이 꺼져 코너가 둥글게 읽히는 깊이 (OS). 림 자체는 제자리 — 림을 움직이면 clip 경계와 벌어져 틈이 보인다. 갓 잘렸을 때 최대, 아물면서 사라짐. 0 = 각진 코너 유지.")]
        public float filletMag = 0.02f;
        [Range(0f, 0.3f)]
        [Tooltip("흡수 합체 벌지: 조각이 파고드는 지점이 바깥으로 불룩해지는 크기 (OS). 상처 필렛과 독립.")]
        public float mergeBulgeMag = 0.06f;
        [Range(1f, 1.15f)]
        [Tooltip("봉합 막이 몸 구면을 넘어 밀리는 여유 (1 = 정확히 일치). 블롭 메쉬가 완전한 구가 아니라 남는 클립 조각을 덮는 용도 — 물리 판정에는 이 여유를 쓰지 않는다.")]
        public float meniscusSealMargin = 1.02f;
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
        [Range(0.5f, 3f)]
        [Tooltip("조각 크기 배율 (비주얼+콜라이더 동반). 1 = 부피 정직값(조각 반경 = 몸반경×f^⅓). 게임플레이 회복량은 부피 비율 그대로 — 보이는 크기만 키움.")]
        public float pieceSizeScale = 1.4f;
        [Range(0.1f, 1f)]
        [Tooltip("조각 CircleCollider2D 반경 = 메쉬 절반 크기 × 이 값.")]
        public float colliderRadiusFactor = 0.7f;
        [Tooltip("스핀 (deg/s) 범위. 둘 다 0 = 회전 없음.")]
        public float spinMinDeg = 0f;
        public float spinMaxDeg = 0f;
        [Tooltip("안전장치: 연속 공중 시간(흡인 중 제외)이 이를 넘으면 구덩이 추락으로 보고 조각 소멸 → 느린 자연 회복으로 전환.")]
        public float severedMaxAirTime = 4f;
        [Tooltip("안전장치: 슬라임이 이보다 멀어지면 조각을 두고 왔다고 보고 소멸 → 느린 자연 회복으로 전환 (월드 m). 화면 대각 반경(≈12.5)보다 크게 잡아야 소멸이 화면 안에서 안 보임.")]
        public float severedMaxDistance = 14f;

        [Header("조각 — 표면장력 round-up (납작 sliver → 둥근 droplet)")]
        [Tooltip("스폰 시 사출축 방향 납작 정도. 0.1 = 아주 납작.")]
        [Range(0.02f, 1f)]
        public float pieceFlatten = 0.12f;
        [Range(1f, 3f)]
        [Tooltip("납작해질 때 수직축이 퍼지는 최대 배율. 완전 부피 보존은 1/√flatten (flatten 0.12 → ×2.9 = 길게 늘어진 판)이라 의도적으로 클램프 — 1 = 퍼짐 없음(그냥 납작), 1.2 ≈ 살짝 퍼진 조각.")]
        public float piecePerpExpandMax = 1.2f;
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

        [Header("조각 — 절단면 (본체와 같은 메니스커스 언어)")]
        [Range(0f, 0.8f)]
        [Tooltip("조각의 절단면 깊이 (중심에서 절단면까지, 조각 OS 반경 비율). 작을수록 깊은(큰) 단면 — 너무 깊으면 조각이 얇은 판처럼 보임. 단면은 -사출방향 쪽에 생기고 round-up 동안 메니스커스로 아묾.")]
        public float pieceCutFaceFrac = 0.6f;

        [Header("조각 — 바닥 잔존 (흡수 대기)")]
        [Tooltip("대기 중 숨쉬듯 말랑이는 아이들 워블 진폭 (스케일 비율).")]
        [Range(0f, 0.3f)] public float restBreathAmp = 0.05f;
        [Tooltip("아이들 워블 주기 (Hz).")]
        public float restBreathFreq = 1.1f;

        [Header("조각 — 흡수 (자크식 근접 흡인)")]
        [Tooltip("슬라임 중심이 이 거리 안에 들어오면 조각이 끌려오기 시작 (월드 m).")]
        public float absorbPullRadius = 2.5f;
        [Tooltip("끌림 가속도 (m/s²). 점성 있는 자석 느낌 — 너무 크면 순간이동처럼 보임.")]
        public float absorbPullAccel = 60f;
        [Tooltip("끌림 최고 속도 (m/s).")]
        public float absorbPullMaxSpeed = 14f;
        [Tooltip("스폰 직후 이 시간 동안은 흡인 무장 안 함 — 일단 날아가서 몸 밖으로 나가야 함 (스폰 위치가 슬라임 중심이라 즉시 재흡수 방지).")]
        public float absorbArmDelay = 0.35f;
        [Range(0f, 1f)]
        [Tooltip("끌려오는 동안 셰이더 velocity-stretch 추가 강도 — 본체 쪽으로 길쭉하게 늘어나는 점성 표현.")]
        public float absorbStretchBoost = 0.5f;

        [Header("흡수 — 합체 연출")]
        [Tooltip("조각이 닿은 뒤 부피가 차오르는 시간 (초). 이 동안 닿은 지점이 국소적으로 불룩(립+워블)했다가 가라앉음.")]
        public float mergeDuration = 0.35f;
        [Min(0f)]
        [Tooltip("상처 봉합이 끝난 뒤 몸이 차오르기까지 쉬는 박자 (초). 봉합 중에 삼킨 조각은 이 박자를 기다렸다가 합체 — 상처 차오름과 몸 차오름이 겹쳐 보이지 않게.")]
        public float mergeDeferDelay = 0.3f;
        [Min(0f)]
        [Tooltip("합체 순간 전신 스케일 스프링에 주는 바깥쪽 속도 킥 — 꿀꺽 삼키는 출렁임.")]
        public float absorbPopKick = 0.6f;
        [Range(0f, 2f)]
        [Tooltip("합체 지점 워블(잔물결) 킥 — 닿은 부위가 부르르 떨리는 정도.")]
        public float mergeWobbleKick = 0.8f;
        [Range(0.5f, 0.98f)]
        [Tooltip("합체 벌지의 막 위치 (중심에서 표면까지 비율). 1에 가까울수록 표면에 얕고 국소적인 불룩.")]
        public float mergeFaceFrac = 0.85f;

        // ───────────────────────── 회복 ─────────────────────────

        [Header("회복 — 흡수가 곧 회복")]
        [Tooltip("조각을 영영 잃었을 때(낙사·두고 떠남)의 느린 자연 회복: 잃은 몫이 전부 돌아오기까지 걸리는 시간 (초). 손실량과 무관하게 항상 이 시간 — 상처 봉합이 끝난 시점부터 카운트. 흡수는 mergeDuration 만에 그 조각 몫을 즉시 회복.")]
        [Min(0.05f)] public float slowRecoveryDuration = 15f;

        [Header("회복 — 점프력 (몸집 파생, 독립 타이머 없음)")]
        [Range(0f, 1f)]
        [Tooltip("점프력 배율의 하한. 몸집이 woundedScaleRef 까지 작아졌을 때 이 값.")]
        public float woundedJumpMultiplierMin = 0.5f;
        [Range(0.1f, 0.99f)]
        [Tooltip("점프력이 하한에 닿는 몸집 비율. 몸집이 이 값~1 사이를 오가면 점프력도 min~1 사이를 선형으로 따라감.")]
        public float woundedScaleRef = 0.6f;
    }
}
