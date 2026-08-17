using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.VFX.Sandevistan
{
    /// <summary>
    /// Map-wide Sandevistan look, gated by the "Use a rudimentary implant" option. Purely cosmetic:
    /// it spawns fading clones of the slime and grades the screen. Nothing here spawns a collider or
    /// touches the slime, so the option can never change how the game plays.
    ///
    /// Two halves, both tunable here:
    ///  - Afterimage: a ghost every `stampIntervalSeconds`, suppressed unless the slime has moved
    ///    `minStampSpacing` slime-bodies since the last one, so standing still or charging emits none.
    ///  - Environment grade: a quad parented to the camera, drawn through SpaceWarpSourceFeature's
    ///    colour copy. Post-processing is NOT used — Main Camera runs with renderPostProcessing off,
    ///    and switching it on would also wake PostFX_Stage1 and UnderwaterVolume across the game.
    ///
    /// The quad and its material instance are created at runtime and destroyed with this object, so
    /// removing the manager removes the whole effect.
    /// </summary>
    [AddComponentMenu("SlimeJumping/VFX/Sandevistan Manager")]
    [DisallowMultipleComponent]
    public class SandevistanManager : MonoBehaviour
    {
        [Header("Afterimage — 잔상")]
        [Tooltip("Transparent material using SlimeJumping/SlimeAfterimage. Mat_SlimeAfterimage works.")]
        [SerializeField] private Material ghostMaterial;
        [Tooltip("잔상색. Ghost Desaturation 이 0 이면 원래 색이 남아 이 색이 거의 안 보인다.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color ghostTint = new Color(0.35f, 0.85f, 1.15f);
        [Range(0f, 1f)]
        [Tooltip("얼마나 원래 색을 버리고 잔상색으로 갈지. 1 = 완전히 잔상색.")]
        [SerializeField] private float ghostDesaturation = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float ghostPeakAlpha = 0.5f;
        [Tooltip("잔상 하나가 사라지기까지의 초.")]
        [Min(0.05f)]
        [SerializeField] private float ghostLifetimeSeconds = 0.55f;
        [Tooltip("수명 대비 알파 곡선. x = 경과 0~1, y = 알파 배율.")]
        [SerializeField] private AnimationCurve ghostFade = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] private bool includeFace = true;

        [Header("Afterimage — 간격")]
        [Tooltip("잔상 사이 최소 시간(초).")]
        [Min(0.02f)]
        [SerializeField] private float stampIntervalSeconds = 0.06f;
        [Tooltip("잔상 사이 최소 거리. 단위는 슬라임 지름. 0 이면 제자리에서도 계속 찍힌다.")]
        [Min(0f)]
        [SerializeField] private float minStampSpacing = 0.4f;
        [Tooltip("동시에 살아있는 잔상 상한. 넘으면 새 잔상을 안 찍는다.")]
        [Min(1)]
        [SerializeField] private int maxLiveGhosts = 48;

        [Header("Environment — 주변 환경색")]
        [Tooltip("SlimeJumping/Sandevistan_EnvTint 를 쓰는 머티리얼. 런타임에 인스턴스로 복제해 쓴다.")]
        [SerializeField] private Material envTintMaterial;
        [Tooltip("환경색. 화면 전체가 이 색 쪽으로 끌린다.")]
        [ColorUsage(false, true)]
        [SerializeField] private Color envTint = new Color(0.35f, 0.75f, 1f);
        [Range(0f, 1f)]
        [SerializeField] private float envTintStrength = 0.45f;
        [Range(0f, 1f)]
        [SerializeField] private float envDesaturation = 0.6f;
        [Range(0.5f, 2f)]
        [SerializeField] private float envContrast = 1.15f;
        [Tooltip("환경 그레이딩 세기. 0 이면 잔상만 나오고 화면 색은 그대로다.")]
        [Range(0f, 1f)]
        [SerializeField] private float envAmount = 1f;
        [Tooltip("옵션을 켜고 끌 때 환경색이 번지는 시간(초). 실시간이라 일시정지 중에도 흐른다.")]
        [Min(0f)]
        [SerializeField] private float envBlendSeconds = 0.35f;

        static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        static readonly int TintStrengthId = Shader.PropertyToID("_TintStrength");
        static readonly int DesaturationId = Shader.PropertyToID("_Desaturation");
        static readonly int ContrastId = Shader.PropertyToID("_Contrast");
        static readonly int EffectId = Shader.PropertyToID("_Effect");

        const float SlimeSearchInterval = 1f;

        SlimeController slime;
        float nextSlimeSearch;
        float nextStampTime;
        Vector2 lastStampPos;
        bool hasStamped;
        int liveGhosts;

        Transform ghostRoot;
        UnityEngine.Camera cam;
        Transform tintQuad;
        Material tintInstance;
        float envBlend;

        bool Enabled => GameOptions.ImplantEnabled;

        void Awake()
        {
            ghostRoot = new GameObject("Ghosts").transform;
            ghostRoot.SetParent(transform, worldPositionStays: false);
            BuildTintQuad();
        }

        void OnEnable() => GameOptions.ImplantEnabledChanged += HandleOptionChanged;

        void OnDisable()
        {
            GameOptions.ImplantEnabledChanged -= HandleOptionChanged;
            // 꺼진 채로 남으면 화면이 물든 상태로 굳는다.
            if (tintInstance != null) tintInstance.SetFloat(EffectId, 0f);
        }

        void OnDestroy()
        {
            if (tintInstance != null) Destroy(tintInstance);
        }

        void HandleOptionChanged(bool on)
        {
            if (on) return;
            hasStamped = false;
            nextStampTime = 0f;
        }

        void Update()
        {
            if (!ResolveSlime()) return;
            if (!Enabled) return;
            if (liveGhosts >= maxLiveGhosts) return;
            if (Time.time < nextStampTime) return;

            Vector2 pos = slime.Position;
            float spacing = minStampSpacing * slime.SlimeRadius * 2f;
            if (hasStamped && (pos - lastStampPos).sqrMagnitude < spacing * spacing) return;

            var ghost = SandevistanGhost.Spawn(slime, ghostRoot, ghostMaterial, ghostLifetimeSeconds,
                                               ghostPeakAlpha, ghostTint, ghostDesaturation,
                                               ghostFade, includeFace);
            if (ghost == null) return;

            liveGhosts++;
            ghost.gameObject.AddComponent<GhostCounter>().Bind(this);
            nextStampTime = Time.time + stampIntervalSeconds;
            lastStampPos = pos;
            hasStamped = true;
        }

        void LateUpdate()
        {
            if (tintQuad == null) return;
            if (!ResolveCamera()) return;

            FitQuadToFrustum();

            // 옵션 창은 timeScale 0 이라 스케일 시간으로는 페이드가 얼어붙는다.
            float target = Enabled ? Mathf.Clamp01(envAmount) : 0f;
            envBlend = envBlendSeconds <= 0f
                ? target
                : Mathf.MoveTowards(envBlend, target, Time.unscaledDeltaTime / envBlendSeconds);

            tintQuad.gameObject.SetActive(envBlend > 0.0005f);
            if (!tintQuad.gameObject.activeSelf) return;

            tintInstance.SetColor(TintColorId, envTint);
            tintInstance.SetFloat(TintStrengthId, envTintStrength);
            tintInstance.SetFloat(DesaturationId, envDesaturation);
            tintInstance.SetFloat(ContrastId, envContrast);
            tintInstance.SetFloat(EffectId, envBlend);
        }

        internal void ReleaseGhost() => liveGhosts = Mathf.Max(0, liveGhosts - 1);

        bool ResolveSlime()
        {
            if (slime != null) return true;
            if (Time.time < nextSlimeSearch) return false;
            nextSlimeSearch = Time.time + SlimeSearchInterval;
            slime = FindFirstObjectByType<SlimeController>();
            return slime != null;
        }

        bool ResolveCamera()
        {
            if (cam != null) return true;
            cam = UnityEngine.Camera.main;
            if (cam == null) return false;
            tintQuad.SetParent(cam.transform, worldPositionStays: false);
            return true;
        }

        // 근평면 바로 앞 + 여유 5% — 프러스텀 밖으로 컬링되지 않으면서 가장자리 틈이 안 생긴다.
        void FitQuadToFrustum()
        {
            float d = cam.nearClipPlane + 0.05f;
            float h = cam.orthographic
                ? cam.orthographicSize * 2f
                : 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            tintQuad.localPosition = new Vector3(0f, 0f, d);
            tintQuad.localRotation = Quaternion.identity;
            tintQuad.localScale = new Vector3(h * cam.aspect * 1.05f, h * 1.05f, 1f);
        }

        void BuildTintQuad()
        {
            if (envTintMaterial == null)
            {
                Debug.LogWarning("[Sandevistan] envTintMaterial 이 비었다 — 잔상만 나오고 환경색은 안 걸린다.", this);
                return;
            }

            var go = new GameObject("EnvTintQuad") { layer = 0 };
            go.transform.SetParent(transform, worldPositionStays: false);
            go.AddComponent<MeshFilter>().sharedMesh = BuildQuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            tintInstance = new Material(envTintMaterial) { name = "Mat_SandevistanEnvTint (Instance)" };
            // 에셋에 큐 오버라이드가 박혀 있어도 왜곡 메시들보다 먼저 그려야 한다 — 셰이더 헤더 참조.
            tintInstance.renderQueue = 2600;
            tintInstance.SetFloat(EffectId, 0f);
            mr.sharedMaterial = tintInstance;
            go.SetActive(false);
            tintQuad = go.transform;
        }

        static Mesh BuildQuadMesh()
        {
            var m = new Mesh { name = "SandevistanQuad" };
            m.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            };
            m.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            // 카메라에 붙어 항상 화면을 채우므로 프러스텀 컬링 판정을 크게 잡아 둔다.
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            return m;
        }

        /// 잔상이 스스로 죽을 때 카운터를 돌려준다 — 매니저가 매 프레임 리스트를 훑지 않게.
        class GhostCounter : MonoBehaviour
        {
            SandevistanManager owner;
            public void Bind(SandevistanManager m) => owner = m;
            void OnDestroy() { if (owner != null) owner.ReleaseGhost(); }
        }
    }
}
