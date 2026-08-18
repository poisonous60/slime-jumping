using UnityEngine;

namespace SlimeJumping.VFX.Waterfall
{
    // 물레방아 폭포 공유 튜닝. canonical asset = Assets/Resources/WaterfallConfig.asset (자동 로드).
    //
    // 좌표는 전부 월드 유닛이고 2026-08-17 에디터 실측에서 왔다. 기본값을 임의로 바꾸지 마라.
    //   흙 턱 상면  x 6.75~11.00, z −0.68~+1.12, 평탄면 y 35.39
    //   파인 구멍   x 8.3~10.1, 최저 y 35.12 (8.92, −0.06) — 유저가 m6 메시에 판 것
    //   카메라      pos z −12, forward +Z → z 작은 쪽이 앞, 큰 쪽이 뒤
    //   턱 아래     바위 27.9 까지 약 7.5유닛이 콜라이더도 비주얼도 없는 허공
    //
    // 룩 파라미터(_BaseColor, _TintStrength, _FlowSpeed 등)는 머티리얼 에셋에 둔다.
    [CreateAssetMenu(fileName = "WaterfallConfig",
        menuName = "SlimeJumping/VFX/Waterfall Config", order = 0)]
    public sealed class WaterfallConfig : ScriptableObject
    {
        [Header("흙 턱 기준면 (실측)")]
        public float ledgeY = 35.38f;
        public float ledgeXMin = 6.75f;
        public float ledgeXMax = 11.00f;
        public float ledgeZNear = -0.68f;
        public float ledgeZFar = 1.12f;

        [Header("웅덩이 (LedgePuddle) — 지형에서 유도")]
        // 이 영역을 훑어 수면보다 낮은 곳만 채운다 — 구멍 윤곽이 곧 물가가 된다.
        public float puddleAreaXMin = 6.75f;
        public float puddleAreaXMax = 11.00f;
        public float puddleAreaZMin = -0.68f;
        public float puddleAreaZMax = 1.12f;

        [Tooltip("수면 높이. 구멍 실측 = 바닥 35.18 / 테두리 35.38~35.42. 올릴수록 물가가 넓어진다.")]
        public float puddleWaterY = 35.33f;

        [Tooltip("지형 샘플 격자 해상도. 물가 윤곽의 정밀도를 결정한다.")]
        [Min(4)] public int puddleGridX = 64;
        [Min(4)] public int puddleGridZ = 32;

        [Tooltip("이 깊이 이하가 물가로 페이드된다. 작을수록 물가 선이 또렷하다.")]
        [Min(0.005f)] public float puddleShoreFeather = 0.055f;

        [Tooltip("수면을 지형 위로 살짝 띄우는 양. z-fight 방지용 최소값.")]
        [Min(0f)] public float puddleSurfaceLift = 0.008f;

        [Header("웅덩이 파문")]
        [Min(0.05f)] public float rippleWavelength = 0.28f;
        [Min(0.1f)] public float rippleSpeed = 0.9f;

        [Tooltip("거리 감쇠. 클수록 좁게 퍼진다.")]
        [Min(0f)] public float rippleDecay = 2.2f;

        [Min(0.1f)] public float rippleLife = 1.3f;

        [Tooltip("파문이 수면 알파에 실리는 양. 높이만으로는 정면 카메라에서 거의 안 읽힌다.")]
        [Range(0f, 1f)] public float rippleAlpha = 0.5f;

        [Tooltip("슬라임이 살살 내려앉을 때 찰박 진폭.")]
        [Min(0f)] public float slimeAmplitude = 0.09f;

        [Tooltip("hard 착지 찰박 진폭. LedgePuddle 의 landHardThreshold 를 넘을 때 이쪽을 쓴다.")]
        [Min(0f)] public float slimeAmplitudeHard = 1.0f;

        [Min(0f)] public float slimeSplashCooldown = 0.25f;

        [Tooltip("트리거 z 판정 여유. 2.5D 라 X/Y 만 겹치고 Z 로는 웅덩이 밖일 수 있다.")]
        [Min(0f)] public float puddleTriggerZMargin = 0.6f;

        [Min(1)] public int maxRipples = 10;

        /// 에셋을 고쳐도 컴포넌트는 통지를 못 받아 다시 안 굽는다. 인스펙터 편집을 흘려보낸다.
        public static event System.Action Changed;

#if UNITY_EDITOR
        void OnValidate() => Changed?.Invoke();
#endif

        const string ResourcesPath = "WaterfallConfig";
        static WaterfallConfig fallback;

        public static WaterfallConfig Resolve(WaterfallConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<WaterfallConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null) fallback = CreateInstance<WaterfallConfig>();
            return fallback;
        }
    }
}
