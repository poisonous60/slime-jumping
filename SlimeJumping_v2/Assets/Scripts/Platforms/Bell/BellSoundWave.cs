using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Platforms.Bell
{
    /// <summary>Turns each audible bell strike into an expanding sound wave: pushes wave centers/ages
    /// as shader globals (read by the Trail Receiver Local Reveal shader to fade hidden platforms in
    /// and out as the front passes) and fires a heat-haze distortion ring per strike.</summary>
    [AddComponentMenu("SlimeJumping/Bell/Bell Sound Wave")]
    public class BellSoundWave : MonoBehaviour
    {
        [Tooltip("The bell whose audible strikes spawn waves.")]
        [SerializeField] private SwingingBell bell;
        [Tooltip("Wave origin relative to the bell body's rigidbody position (world units).")]
        [SerializeField] private Vector2 centerOffset;
        [Tooltip("Optional per-instance config; empty = Resources/BellSoundWaveConfig.")]
        [SerializeField] private BellSoundWaveConfig configOverride;
        [Tooltip("Persistent distortion quads triggered round-robin, one per audible strike.")]
        [SerializeField] private SpaceWarpEffect[] distortionQuads;
        [Tooltip("The ring material — band width/blur are pushed into it from config every frame.")]
        [SerializeField] private Material distortionMaterial;

        // Baked into the reveal shader's global array (BELL_WAVE_COUNT) — not a tuning knob.
        public const int MaxWaves = 8;

        static readonly int IdWaveData    = Shader.PropertyToID("_BellWaveData");
        static readonly int IdWaveParams  = Shader.PropertyToID("_BellWaveParams");
        static readonly int IdWaveParams2 = Shader.PropertyToID("_BellWaveParams2");
        static readonly int IdRippleWidth = Shader.PropertyToID("_PulseRippleWidth");
        static readonly int IdBlurSize    = Shader.PropertyToID("_BlurSize");

        BellSoundWaveConfig cfg;
        Rigidbody2D bellRb;
        // Same array instance every push — Unity locks a global array's length on first set.
        readonly Vector4[] waveData = new Vector4[MaxWaves];
        readonly Vector2[] centers  = new Vector2[MaxWaves];
        readonly float[]   starts   = new float[MaxWaves];
        readonly bool[]    active   = new bool[MaxWaves];
        int nextSlot;
        int nextQuad;
        // The wave uniforms are shader globals — two enabled broadcasters would overwrite
        // each other's waves every frame, so this component assumes one active instance.
        static int enabledCount;

        public BellSoundWaveConfig Config => cfg;

        void Awake()
        {
            cfg = BellSoundWaveConfig.Resolve(configOverride);
            if (bell != null) bellRb = bell.GetComponent<Rigidbody2D>();
        }

        void OnEnable()
        {
            enabledCount++;
            if (enabledCount > 1)
                Debug.LogWarning("BellSoundWave: multiple enabled instances push the same shader globals — only one bell's waves will show.", this);
            if (bell != null) bell.Struck += OnStruck;
            for (int i = 0; i < MaxWaves; i++) active[i] = false;
            PushGlobals();
        }

        void OnDisable()
        {
            enabledCount--;
            if (bell != null) bell.Struck -= OnStruck;
            // Stale globals would freeze every reveal platform mid-wave.
            for (int i = 0; i < MaxWaves; i++) active[i] = false;
            PushGlobals();
        }

        void OnStruck(float closingSpeed)
        {
            // Struck is raised after Ring(), so LastRingResult is this strike's own outcome.
            // Only strikes that actually made a sound send a wave.
            if (!bell.LastRingResult.StartsWith("PLAYED")) return;

            // PlatformMotor requires a Rigidbody2D, so bellRb is guaranteed once bell is assigned.
            Vector2 center = bellRb.position + centerOffset;
            int slot = nextSlot;
            nextSlot = (nextSlot + 1) % MaxWaves;
            centers[slot] = center;
            starts[slot]  = Time.time;
            active[slot]  = true;

            if (distortionQuads != null && distortionQuads.Length > 0)
            {
                var quad = distortionQuads[nextQuad];
                nextQuad = (nextQuad + 1) % distortionQuads.Length;
                if (quad != null)
                {
                    var t = quad.transform;
                    t.position = new Vector3(center.x, center.y, t.position.z);
                    quad.Trigger();
                }
            }
        }

        void Update()
        {
            float speed    = Mathf.Max(cfg.waveSpeed, 0.0001f);
            float lifetime = cfg.maxRadius / speed + cfg.frontWidth / speed + cfg.holdSeconds + cfg.fadeSeconds;
            for (int i = 0; i < MaxWaves; i++)
            {
                if (!active[i]) continue;
                if (Time.time - starts[i] > lifetime) active[i] = false;
            }
            PushGlobals();
            PushDistortion();
        }

        // Config is the single source of truth for the ring look — pushed every frame so the
        // user can tune the asset live in play mode without re-running the setup rig.
        void PushDistortion()
        {
            if (distortionQuads != null)
            {
                float duration = Mathf.Max(0.05f, cfg.maxRadius / Mathf.Max(cfg.waveSpeed, 0.0001f));
                float size = 2f * cfg.maxRadius;
                for (int i = 0; i < distortionQuads.Length; i++)
                {
                    var quad = distortionQuads[i];
                    if (quad == null) continue;
                    quad.PulseStrength = cfg.distortionStrength;
                    quad.PulseDuration = duration;
                    var t = quad.transform;
                    if (!Mathf.Approximately(t.localScale.x, size))
                        t.localScale = new Vector3(size, size, 1f);
                }
            }
            if (distortionMaterial != null)
            {
                distortionMaterial.SetFloat(IdRippleWidth,
                    Mathf.Clamp(cfg.distortionRingWidthWorld / Mathf.Max(0.1f, cfg.maxRadius), 0.01f, 0.6f));
                distortionMaterial.SetFloat(IdBlurSize, cfg.distortionBlurSize);
            }
        }

        void OnDrawGizmosSelected()
        {
            var c = cfg != null ? cfg : BellSoundWaveConfig.Resolve(configOverride);
            Vector2 center = centerOffset;
            if (bell != null)
            {
                var rb = bell.GetComponent<Rigidbody2D>();
                center += rb != null ? rb.position : (Vector2)bell.transform.position;
            }
            else center += (Vector2)transform.position;

            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            DrawGizmoCircle(center, c.maxRadius);
            Gizmos.DrawLine(center + Vector2.left * 0.5f, center + Vector2.right * 0.5f);
            Gizmos.DrawLine(center + Vector2.down * 0.5f, center + Vector2.up * 0.5f);
            // Inner edge of the ring at the moment it dies — the band's final footprint.
            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.3f);
            DrawGizmoCircle(center, Mathf.Max(0f, c.maxRadius - c.distortionRingWidthWorld));
        }

        static void DrawGizmoCircle(Vector2 center, float radius)
        {
            if (radius <= 0f) return;
            const int segments = 48;
            Vector3 prev = center + Vector2.right * radius;
            for (int i = 1; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        void PushGlobals()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                float age = active[i] ? Time.time - starts[i] : -1f;
                waveData[i] = new Vector4(centers[i].x, centers[i].y, age, 0f);
            }
            Shader.SetGlobalVectorArray(IdWaveData, waveData);
            Shader.SetGlobalVector(IdWaveParams,
                new Vector4(cfg.waveSpeed, cfg.frontWidth, cfg.holdSeconds, cfg.fadeSeconds));
            Shader.SetGlobalVector(IdWaveParams2, new Vector4(cfg.maxRadius, 0f, 0f, 0f));
        }
    }
}
