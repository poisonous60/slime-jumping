using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Platforms.Bell
{
    /// <summary>Turns each audible bell strike into an expanding sound wave: pushes wave centers/ages
    /// as shader globals (read by the Trail Receiver Local Reveal shader to fade hidden platforms in
    /// and out as the front passes) and fires a heat-haze distortion ring per strike.
    ///
    /// Which platforms a bell reveals is set by the hierarchy: hidden platforms sitting under the same
    /// parent as the bell are its bundle, and only its waves reveal them. Bells sharing a parent share
    /// the bundle, so any of them reveals the same platforms. Drag a platform into another bell's
    /// parent to hand it over; one left outside every bell's parent is never revealed.</summary>
    [AddComponentMenu("SlimeJumping/Bell/Bell Sound Wave")]
    public class BellSoundWave : MonoBehaviour
    {
        [Tooltip("The bell whose audible strikes spawn waves. Empty = the SwingingBell on this " +
                 "GameObject or a parent — put this component on the bell and copies just work.")]
        [SerializeField] private SwingingBell bell;
        [Tooltip("Wave origin relative to the bell body's rigidbody position (world units).")]
        [SerializeField] private Vector2 centerOffset;
        [Tooltip("Optional per-instance config; empty = Resources/BellSoundWaveConfig. Wave shape " +
                 "rides along with each wave, so bells may run different speeds and hold times.")]
        [SerializeField] private BellSoundWaveConfig configOverride;
        [Tooltip("Persistent distortion quads triggered round-robin, one per audible strike.")]
        [SerializeField] private SpaceWarpEffect[] distortionQuads;
        [Tooltip("The ring material — band width/blur are pushed into it from config every frame.")]
        [SerializeField] private Material distortionMaterial;

        // Baked into the reveal shader's global array (BELL_WAVE_COUNT) — not a tuning knob.
        public const int MaxWaves = 8;

        static readonly int IdWaveData    = Shader.PropertyToID("_BellWaveData");
        static readonly int IdWaveShape   = Shader.PropertyToID("_BellWaveShape");
        static readonly int IdWaveShape2  = Shader.PropertyToID("_BellWaveShape2");
        static readonly int IdRippleWidth = Shader.PropertyToID("_PulseRippleWidth");
        static readonly int IdBlurSize    = Shader.PropertyToID("_BlurSize");
        static readonly int IdWaveGroup   = Shader.PropertyToID("_BellWaveGroup");

        BellSoundWaveConfig cfg;
        Rigidbody2D bellRb;
        readonly System.Collections.Generic.List<Rendering.LocalTrailReceiver> bundleReceivers = new();
        // The wave uniforms are shader globals, so every bell in the scene shares one slot pool
        // and one push per frame — otherwise two broadcasters overwrite each other's waves.
        // Same array instance every push — Unity locks a global array's length on first set.
        static readonly Vector4[] waveData   = new Vector4[MaxWaves];
        static readonly Vector4[] waveShape  = new Vector4[MaxWaves];
        static readonly Vector4[] waveShape2 = new Vector4[MaxWaves];
        static readonly Vector2[] centers  = new Vector2[MaxWaves];
        static readonly float[]   starts   = new float[MaxWaves];
        static readonly bool[]    active   = new bool[MaxWaves];
        static readonly float[]   groups   = new float[MaxWaves];
        // Kept per slot rather than snapshotted, so retuning a config still reshapes its live waves.
        static readonly BellSoundWaveConfig[] slotCfg = new BellSoundWaveConfig[MaxWaves];
        static int nextSlot;
        static int enabledCount;
        static int lastPushFrame = -1;
        static int nextGroup;
        // Group ids are per bundle root, so bells under one parent answer for the same platforms.
        static readonly System.Collections.Generic.Dictionary<Transform, float> bundleGroups = new();
        int nextQuad;
        int lastWaveImpact = -1;
        float group;

        public BellSoundWaveConfig Config => cfg;

        /// <summary>Holds every wave whose front has finished expanding at that moment, so revealed
        /// platforms stop counting down to invisible. Shared like the slot pool. The caller must
        /// clear it — left on, the reveal never fades.</summary>
        public static bool PauseSettledWaves { get; set; }

        void Awake()
        {
            cfg = BellSoundWaveConfig.Resolve(configOverride);
            if (bell == null) bell = GetComponentInParent<SwingingBell>();
            if (bell != null) bellRb = bell.GetComponent<Rigidbody2D>();

            // The quads are placed in world space per strike; riding a swinging bell would drag
            // the live ring along with it, so they leave the hierarchy at startup.
            if (distortionQuads != null)
                for (int i = 0; i < distortionQuads.Length; i++)
                    if (distortionQuads[i] != null) distortionQuads[i].transform.SetParent(null, true);

            var bundle = transform.parent != null ? transform.parent : transform;
            if (!bundleGroups.TryGetValue(bundle, out group)) bundleGroups[bundle] = group = ++nextGroup;
            StampGroup(bundle);
        }

        // Hierarchy is the bundle, resolved once on load: every reveal renderer anywhere under this
        // bell's parent — nested in sub-groups or under the bell itself, depth does not matter — is
        // stamped with the bundle's group id and from then on answers only this bundle's waves.
        // Anything under another bundle stays hidden. Runs after the quads leave the hierarchy; they
        // carry no group property, so the material filter skips them either way.
        void StampGroup(Transform bundle)
        {
            var block = new MaterialPropertyBlock();
            var renderers = bundle.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i].sharedMaterial;
                if (mat == null || !mat.HasProperty(IdWaveGroup)) continue;
                renderers[i].GetPropertyBlock(block);
                block.SetFloat(IdWaveGroup, group);
                renderers[i].SetPropertyBlock(block);

                // A receiver counts as a hidden platform's only via the reveal renderer under it.
                var receiver = renderers[i].GetComponentInParent<Rendering.LocalTrailReceiver>();
                if (receiver != null && !bundleReceivers.Contains(receiver)) bundleReceivers.Add(receiver);
            }
        }

        void OnEnable()
        {
            enabledCount++;
            if (bell != null) bell.Struck += OnStruck;
            // Only the first broadcaster clears — a later one enabling must not wipe live waves.
            if (enabledCount == 1)
            {
                for (int i = 0; i < MaxWaves; i++) active[i] = false;
                PushGlobals();
            }
        }

        void OnDisable()
        {
            enabledCount--;
            if (bell != null) bell.Struck -= OnStruck;
            // Stale globals would freeze every reveal platform mid-wave.
            if (enabledCount <= 0)
            {
                for (int i = 0; i < MaxWaves; i++) active[i] = false;
                PushGlobals();
            }
        }

        void OnStruck(float closingSpeed)
        {
            // Struck is raised after Ring(), so LastRingResult is this strike's own outcome.
            // Only strikes that actually made a sound send a wave.
            if (!bell.LastRingResult.StartsWith("PLAYED")) return;

            // One wave per slime hit, not per ring.
            if (bell.ImpactCount == lastWaveImpact) return;
            lastWaveImpact = bell.ImpactCount;

            // PlatformMotor requires a Rigidbody2D, so bellRb is guaranteed once bell is assigned.
            Vector2 center = bellRb.position + centerOffset;
            int slot = nextSlot;
            nextSlot = (nextSlot + 1) % MaxWaves;
            centers[slot] = center;
            starts[slot]  = Time.time;
            active[slot]  = true;
            groups[slot]  = group;
            slotCfg[slot] = cfg;

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
            // Shared slot pool: the first bell to tick each frame ages and pushes it for everyone,
            // each slot on the config of the bell that filled it.
            if (lastPushFrame != Time.frameCount)
            {
                lastPushFrame = Time.frameCount;
                for (int i = 0; i < MaxWaves; i++)
                {
                    if (!active[i]) continue;
                    var c = SlotConfig(i);
                    float speed = Mathf.Max(c.waveSpeed, 0.0001f);
                    // Pushing the start forward by the frame keeps age fixed, so the reveal holds.
                    if (PauseSettledWaves && Time.time - starts[i] >= (c.maxRadius + c.frontWidth) / speed)
                    {
                        starts[i] += Time.deltaTime;
                        continue;
                    }
                    if (Time.time - starts[i] > c.maxRadius / speed + c.frontWidth / speed + c.holdSeconds + c.fadeSeconds)
                        active[i] = false;
                }
                PushGlobals();
            }
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
            for (int i = 0; i < bundleReceivers.Count; i++)
            {
                var r = bundleReceivers[i];
                if (r == null) continue;
                r.StampStrengthMultiplier = cfg.hiddenPlatformTrailStrength;
                r.StampRadiusMultiplier   = cfg.hiddenPlatformTrailRadiusScale;
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
                waveData[i] = new Vector4(centers[i].x, centers[i].y, age, groups[i]);
                var c = SlotConfig(i);
                waveShape[i]  = new Vector4(c.waveSpeed, c.frontWidth, c.holdSeconds, c.fadeSeconds);
                waveShape2[i] = new Vector4(c.maxRadius, 0f, 0f, 0f);
            }
            Shader.SetGlobalVectorArray(IdWaveData, waveData);
            Shader.SetGlobalVectorArray(IdWaveShape, waveShape);
            Shader.SetGlobalVectorArray(IdWaveShape2, waveShape2);
        }

        BellSoundWaveConfig SlotConfig(int slot)
        {
            return slotCfg[slot] != null ? slotCfg[slot] : cfg;
        }
    }
}
