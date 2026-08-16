using UnityEngine;

namespace SlimeJumping.UI
{
    /// Shared menu slime arena tuning; canonical asset
    /// <c>Assets/Resources/MenuSlimeArenaConfig.asset</c> (auto-loaded).
    [CreateAssetMenu(fileName = "MenuSlimeArenaConfig",
        menuName = "SlimeJumping/Menu Slime Arena Config", order = 0)]
    public sealed class MenuSlimeArenaConfig : ScriptableObject
    {
        [Tooltip("Thickness of the invisible floor and side walls (world units).")]
        [Min(0.1f)] public float wallThickness = 1f;

        [Header("젤리 파동 — 깊이/길이는 world units")]
        [Tooltip("이웃 전파 — 클수록 물결이 빨리 번짐. 과하면 고주파 잔떨림.")]
        public float waveSpread = 400f;
        [Tooltip("원위치 복원력.")]
        public float waveSpring = 35f;
        [Tooltip("감쇠 — 작을수록 오래 출렁 (3 ≈ 1초 링아웃).")]
        public float waveDamping = 3f;
        [Tooltip("파동 점 최대 간격 — 촘촘할수록 고주파 모드가 살아 떨림이 는다.")]
        public float waveResolution = 0.12f;
        [Tooltip("착지 발자국 깊이.")]
        public float dentDepth = 0.15f;
        [Tooltip("착지 속도 1u/s 당 추가 깊이.")]
        public float dentDepthPerSpeed = 0.01f;
        [Tooltip("발자국 반경(슬라임 반지름 배수).")]
        public float dentRadius = 2.5f;
        [Tooltip("탑승 중 발밑에 유지되는 눌림.")]
        public float restDentDepth = 0.08f;
        [Tooltip("탑승 이동 속도 비례 트레일 물결.")]
        public float rollWavePerSpeed = 0.02f;
        [Tooltip("이탈 순간 바깥으로 퍼지는 물결.")]
        public float popDepth = 0.07f;
        [Tooltip("점당 변위 상한.")]
        public float maxDisplacement = 0.3f;
        [Tooltip("표면 최대 이동 속도(u/s) — probe 추적 한계.")]
        public float maxSurfaceSpeed = 3f;
        [Tooltip("이하 진폭이면 완전 휴면.")]
        public float restEpsilon = 0.002f;
        [Tooltip("탑승 접촉부 추가 감쇠 — 발밑 표면(측벽 포함)을 빠르게 재워 슬라임과 싸우지 않게. 잔떨림 대책.")]
        public float contactCalmDamping = 12f;

        [Header("젤리 전신 압축 — 깊이는 젤리 높이 비율 (SquishyCloud 계열)")]
        [Tooltip("탑승 중 유지되는 전신 눌림.")]
        public float loadCompression = 0.08f;
        [Tooltip("착지 속도 1u/s 당 추가 전신 눌림.")]
        public float impactCompressionPerSpeed = 0.015f;
        [Tooltip("전신 눌림 상한.")]
        public float impactCompressionMax = 0.25f;
        [Tooltip("전신 복귀 스프링 Hz.")]
        public float bodyFrequency = 2.2f;
        [Range(0.05f, 1f)]
        [Tooltip("전신 감쇠 — 1 미만이면 이탈 후 오버슈트 잔동.")]
        public float bodyDamping = 0.3f;
        [Tooltip("눌린 만큼 옆구리가 부푸는 비율.")]
        public float bulgeRatio = 0.6f;

        [Header("글자 스킨")]
        [Tooltip("스킨 그리드 버텍스 간격.")]
        public float skinGridStep = 0.05f;
        [Tooltip("버텍스가 반응하는 윤곽 거리.")]
        public float visualInfluenceRadius = 0.35f;
        [Tooltip("버텍스가 참조하는 윤곽 arc 폭(±점 개수) — 다른 획 오염 차단.")]
        public int mappingArcWindow = 10;
        [Tooltip("글자 z (슬라임 뒤, 영상 앞).")]
        public float letterZOffset = 0.5f;

        [Header("버튼 젤리")]
        [Tooltip("모서리 반경(버튼 높이 비율, 0.5 = 알약).")]
        [Range(0f, 0.5f)] public float buttonCornerRadius = 0.5f;
        [Tooltip("버튼 이미지 세로 스트립 분할 수.")]
        public int buttonStrips = 24;

        const string ResourcesPath = "MenuSlimeArenaConfig";

        /// Override if assigned, else the Resources asset, else null.
        public static MenuSlimeArenaConfig Resolve(MenuSlimeArenaConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<MenuSlimeArenaConfig>(ResourcesPath);
        }
    }
}
