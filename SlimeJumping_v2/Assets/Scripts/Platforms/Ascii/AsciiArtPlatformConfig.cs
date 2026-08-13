using UnityEngine;

namespace SlimeJumping.Platforms
{
    // 아스키아트 발판 공유 튜닝. canonical asset = Assets/Resources/AsciiArtPlatformConfig.asset.
    // 셀 크기는 fontSize·charWidthEm·lineSpacing 이 결정하고, 콜라이더는 빌드 시점 TMP 실측치를 쓴다.
    [CreateAssetMenu(fileName = "AsciiArtPlatformConfig",
        menuName = "SlimeJumping/Ascii Art Platform/Config", order = 0)]
    public sealed class AsciiArtPlatformConfig : ScriptableObject
    {
        [Header("글자 배치")]
        public float fontSize = 3f;        // 3D TMP 는 1pt ≈ 0.1 월드 단위 — 3 이면 셀 ≈ 0.17×0.35.
        public float charWidthEm = 0.55f;  // mspace 고정 폭(em) — 셀 가로 = fontSize × 0.1 × 이 값.
        public float lineSpacing = 0f;     // TMP lineSpacing(폰트 단위) — 셀 세로 간격 보정.

        [Header("비주얼")]
        public Color textColor = Color.white;

        [Header("콜라이더")]
        public float simplifyEpsilonCells = 0.35f; // RDP 허용오차(셀 최소변 배수). 0 = 끄기.

        [Header("출렁임 — 깊이 (셀 세로 배수). 양자화 스텝의 절반 미만은 화면에서 0 이 된다")]
        public float dentDepthCells = 1.5f;          // 착지 발자국 깊이.
        public float dentDepthPerSpeedCells = 0.08f; // 착지 속도 1u/s 당 추가 깊이.
        public float restDentDepthCells = 0.8f;      // 탑승 중 발밑에 유지되는 눌림.
        public float rollWavePerSpeedCells = 0.25f;  // 탑승 이동 속도 비례 트레일 물결.
        public float popWaveDepthCells = 1f;         // 이탈 순간 바깥으로 퍼지는 물결.
        public float maxTotalDisplacementCells = 3f; // 점당 변위 상한.

        [Header("출렁임 — 파동")]
        public float waveResolutionCells = 2f; // 파동 점 최대 간격(셀 배수) — 긴 직선 엣지 세분. 클수록 성김.
        public float dentRadius = 5f;      // 발자국 반경(슬라임 반지름 배수).
        public float waveSpread = 160f;    // 이웃 전파 — 클수록 물결이 빨리 번짐.
        public float waveSpring = 35f;     // 원위치 복원력.
        public float waveDamping = 3f;     // 감쇠 — 작을수록 오래 출렁.
        public float maxSurfaceSpeed = 3f; // 표면 최대 이동 속도(world u/s) — probe 추적 한계.

        [Header("출렁임 — 매핑 / 휴면")]
        public float visualInfluenceRadius = 1f;   // 글리프가 반응하는 윤곽 거리(world).
        public int mappingArcWindow = 16;          // 글리프가 참조하는 윤곽 arc 폭(±점 개수).
        public float waveQuantizeCells = 0.25f;    // 글자 이동 격자 스텝(셀 배수). 0 = 부드러운 연속.
        public float restEpsilonCells = 0.01f;     // 이하 진폭이면 완전 휴면.

        [Header("출렁임 사운드 — 착지·발사음은 SurfaceTag/SurfaceMaterial 몫")]
        public SlimeJumping.Audio.SoundDef wobbleSound;      // 출렁이는 동안 이어지는 소리.
        public float wobbleFullAmplitudeCells = 2f;          // 최대 음량이 되는 윤곽 변위(셀 세로 배수).
        public float wobblePitchAtRest = 0.9f;
        public float wobblePitchAtFull = 1.15f;

        const string ResourcesPath = "AsciiArtPlatformConfig";
        static AsciiArtPlatformConfig cached;

        public static AsciiArtPlatformConfig Resolve(AsciiArtPlatformConfig overrideConfig)
        {
            if (overrideConfig != null) return overrideConfig;
            if (cached == null) cached = Resources.Load<AsciiArtPlatformConfig>(ResourcesPath);
            if (cached == null) Debug.LogError($"[AsciiArt] No config override and Resources/{ResourcesPath} missing.");
            return cached;
        }
    }
}
