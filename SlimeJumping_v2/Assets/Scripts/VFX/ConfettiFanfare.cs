using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.VFX
{
    /// <summary>
    /// 스테이지 종점 팡파레: 슬라임이 트리거에 처음 들어오면 색종이 포퍼들을 한 번만 터뜨린다.
    /// 파티클 레시피는 유저 지정 튜토리얼 영상(youtu.be/Zl-oZLLcK9g) 그대로 — 좌우 포퍼 2문,
    /// 원뿔 버스트 → 중력 낙하 + limit velocity/size 하강 커브. 3D화(쿼드 메시 + 3축 시작회전)만 추가.
    /// 수치는 전부 ConfettiFanfareConfig, 발사 방향은 각 포퍼 트랜스폼이 정한다.
    /// 렌더러(메시·머티리얼)는 셋업 스크립트가 굽고 여기서는 모듈 값만 덮어쓴다.
    /// </summary>
    [AddComponentMenu("SlimeJumping/VFX/Confetti Fanfare")]
    public sealed class ConfettiFanfare : EnvironmentalZone
    {
        [SerializeField] private ConfettiFanfareConfig configOverride;
        [Tooltip("터뜨릴 포퍼들. 모듈 값은 Awake 에서 config 로 덮어쓴다.")]
        [SerializeField] private ParticleSystem[] poppers;

        private ConfettiFanfareConfig cfg;
        private bool fired;

        void Awake()
        {
            if (poppers == null || poppers.Length == 0)
            {
                enabled = false;
                return;
            }
            cfg = ConfettiFanfareConfig.Resolve(configOverride);
            foreach (var popper in poppers)
                if (popper != null) Configure(popper, cfg);
        }

        protected override void OnSlimeEnter(SlimeController slime)
        {
            if (fired) return;
            fired = true;
            SoundService.Play(cfg.fanfareSound, transform.position);
            for (int i = 0; i < poppers.Length; i++)
            {
                var popper = poppers[i];
                if (popper == null) continue;
                SoundService.Play(i == 0 ? cfg.popSoundLeft : cfg.popSoundRight, popper.transform.position);
                popper.Play();
            }
        }

        /// config 수정 시 OnValidate 가 자동 호출; 인스펙터 우클릭은 수동 백업 경로.
        [ContextMenu("Apply Config To Poppers")]
        public void ApplyConfigToPoppers()
        {
            var applied = ConfettiFanfareConfig.Resolve(configOverride);
            foreach (var popper in poppers)
            {
                if (popper == null) continue;
                Configure(popper, applied);
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(popper);
#endif
            }
        }

        protected override void ApplyEffect(SlimeController slime, float dt) { }

        // 셋업 스크립트도 호출해 씬에 직렬화로 굽는다 — 에디터 프리뷰가 config 룩과 일치해야 한다.
        public static void Configure(ParticleSystem ps, ConfettiFanfareConfig cfg)
        {
            // 재생 중(에디터 프리뷰 포함)엔 duration 설정이 막힌다 — 완전 정지 후 쓴다.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = cfg.duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(cfg.lifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(cfg.speedMin, cfg.speedMax);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(cfg.gravityScale);

            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(cfg.sizeXMin, cfg.sizeXMax);
            main.startSizeY = new ParticleSystem.MinMaxCurve(cfg.sizeYMin, cfg.sizeYMax);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(1f);

            main.startRotation3D = true;
            var startTurn = new ParticleSystem.MinMaxCurve(0f, cfg.startRotationMaxDeg * Mathf.Deg2Rad);
            main.startRotationX = startTurn;
            main.startRotationY = startTurn;
            main.startRotationZ = startTurn;
            main.startColor = BuildPaletteGradient(cfg.palette);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(0f);
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, cfg.pieceCountPerPopper) });

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = cfg.coneAngle;
            shape.radius = cfg.nozzleRadius;

            // 상한 커브 배율 = 발사 최고속 — 배율 1이면 첫 프레임부터 1m/s 로 클램프돼 등속으로 기어간다.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.separateAxes = false;
            limit.limit = new ParticleSystem.MinMaxCurve(cfg.speedMax, cfg.slowdownCurve);
            limit.dampen = cfg.slowdownDampen;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = false;
            size.size = new ParticleSystem.MinMaxCurve(1f, cfg.shrinkCurve);
        }

        // Fixed 모드 균등 구간 → 팔레트 색이 섞이지 않고 조각마다 하나씩 뽑힌다.
        static ParticleSystem.MinMaxGradient BuildPaletteGradient(Color[] palette)
        {
            if (palette == null || palette.Length == 0) palette = new[] { Color.white };
            int count = Mathf.Min(palette.Length, 8);
            var colorKeys = new GradientColorKey[count];
            for (int i = 0; i < count; i++)
                colorKeys[i] = new GradientColorKey(palette[i], (i + 1f) / count);

            var gradient = new Gradient { mode = GradientMode.Fixed };
            gradient.SetKeys(colorKeys, new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            });
            return new ParticleSystem.MinMaxGradient(gradient)
            {
                mode = ParticleSystemGradientMode.RandomColor,
            };
        }
    }
}
