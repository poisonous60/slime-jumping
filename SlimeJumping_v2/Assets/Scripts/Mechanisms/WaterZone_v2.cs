using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Closed-polygon dynamic water. Each outline point has an outward normal and a
    /// scalar displacement along that normal. Waves propagate around the loop; slime
    /// impacts push nearby points inward. Physics (buoyancy/drag/fall-cap/blockAttach)
    /// is inherited from the original WaterZone contract.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public partial class WaterZone_v2 : EnvironmentalZone
    {
        [Header("Entry/Exit")]
        [Range(0.01f, 1f)] [SerializeField] float entryMultiplier = 0.4f;
        [Min(0.01f)]       [SerializeField] float exitMultiplier = 2.5f;

        [Header("Internal Physics")]
        [Range(0.01f, 1f)] [SerializeField] float dragFactor = 0.4f;
        [SerializeField]                    float buoyancy = 12f;
        [Min(0f)]          [SerializeField] float maxFallSpeed = 4f;
        [SerializeField]                    bool  blockAttach = true;

        [Header("Shape Generator (context menu defaults)")]
        [Tooltip("Used by 'Generate Circle/Star Points' context menu. Not a live parameter — " +
                 "actual outline is the restPointsLocal list.")]
        [Min(6)]    [SerializeField] int   pointCount = 24;
        [Tooltip("Used by shape generator context menus.")]
        [Min(0.1f)] [SerializeField] float radius = 2f;

        [Header("Wave Physics")]
        [Tooltip("Hooke restore coefficient. ~1.25 matches Hoffman's 0.025/frame at 50Hz. Higher = stiffer, shorter period.")]
        [Min(0f)] [SerializeField] float springK = 1.25f;
        [Tooltip("Velocity damping (per second). ~2 matches Hoffman's 0.04/frame at 50Hz.")]
        [Min(0f)] [SerializeField] float damping = 2f;
        [Tooltip("Neighbor propagation coefficient. ~2.5 matches Hoffman's 0.05/frame at 50Hz. Higher = wave travels faster around loop.")]
        [Min(0f)] [SerializeField] float spread = 2.5f;

        [Header("Slime Interaction")]
        [Tooltip("Slime velocity -> point velocity change per second. ~25 matches 0.5/frame at 50Hz.")]
        [Min(0f)] [SerializeField] float impulseFactor = 25f;
        [Tooltip("Max slime-center-to-surface distance for impulse. ~slimeRadius + push depth.")]
        [Min(0f)] [SerializeField] float contactRadius = 1.2f;
        [Tooltip("Number of neighbor points on each side that also receive a weakened impulse.")]
        [Range(0, 5)]      [SerializeField] int   spreadNeighbors = 2;
        [Tooltip("Impulse falloff per neighbor step. 0.5 = each step halves.")]
        [Range(0f, 0.99f)] [SerializeField] float spreadFalloff = 0.5f;

        [Header("Visual")]
        [Tooltip("Center vertex color (deep interior). Interpolated to edgeColor toward the perimeter.")]
        [SerializeField] Color centerColor = new Color(0.05f, 0.25f, 0.55f, 1f);
        [Tooltip("Edge vertex color (shallow/surface). Alpha acts as rim-glow strength in the Water_v2 shader.")]
        [SerializeField] Color edgeColor   = new Color(0.45f, 0.85f, 1.00f, 1f);
        [SerializeField] LineRenderer outlineLine;

        [Header("Audio")]
        [Tooltip("Played on entry when impact speed >= hardEnterThreshold (e.g. fall-splash).")]
        [SerializeField] AudioClip hardEnterClip;
        [Tooltip("Played on entry when impact speed < hardEnterThreshold (e.g. gentle dip).")]
        [SerializeField] AudioClip softEnterClip;
        [Tooltip("Played once when the slime leaves the water.")]
        [SerializeField] AudioClip exitClip;
        [Tooltip("Looped while the slime is submerged. Leave empty to disable the loop.")]
        [SerializeField] AudioClip underwaterLoopClip;

        [Tooltip("Speed (m/s) at or above which the hard entry clip plays instead of the soft one.")]
        [Min(0f)] [SerializeField] float hardEnterThreshold = 6f;
        [Tooltip("Slime speed (m/s) that maps to full one-shot volume. Slower impacts scale down linearly.")]
        [Min(0.1f)] [SerializeField] float audioRefSpeed = 8f;

        // Owned by WaterZoneAudioManager — not serialized per-zone. Defaults below are used
        // only if no manager is present in the scene.
        float splashVolume = 0.9f;
        float splashPitchJitter = 0.08f;
        float underwaterLoopVolume = 0.7f;

        SoundHandle underwaterLoopHandle;

        Vector2 centerLocal;

        /// <summary>
        /// Overwrites clips + splash/loop volume params. Used by WaterZoneAudioManager
        /// to broadcast a shared audio set to every zone in the scene.
        /// </summary>
        public void ApplyAudioSettings(AudioClip hardEnter, AudioClip softEnter, AudioClip exit, AudioClip underwaterLoop,
                                       float splashVolume, float splashPitchJitter, float underwaterLoopVolume)
        {
            hardEnterClip = hardEnter;
            softEnterClip = softEnter;
            exitClip = exit;
            underwaterLoopClip = underwaterLoop;
            this.splashVolume = splashVolume;
            this.splashPitchJitter = splashPitchJitter;
            this.underwaterLoopVolume = underwaterLoopVolume;
        }

        protected override void Reset()
        {
            base.Reset();
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void Awake()
        {
            RebuildRuntime();
        }

        /// <summary>
        /// Rebuild simulation arrays, collider, and mesh from the current restPointsLocal.
        /// Call after mutating outline at runtime (editor buttons during Play, live scripting).
        /// </summary>
        public void RebuildRuntime()
        {
            BuildShape();
            SyncColliderToRestShape();
            BuildMesh();
        }

        void OnDrawGizmosSelected()
        {
            if (restPointsLocal == null || restPointsLocal.Count < 2) return;

            // Outline (rest shape) in world space.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            int n = restPointsLocal.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = transform.TransformPoint(restPointsLocal[i]);
                Vector3 b = transform.TransformPoint(restPointsLocal[(i + 1) % n]);
                Gizmos.DrawLine(a, b);
            }

            // Vertices.
            Gizmos.color = Color.yellow;
            for (int i = 0; i < n; i++)
            {
                Vector3 w = transform.TransformPoint(restPointsLocal[i]);
                Gizmos.DrawSphere(w, HandleSize(w) * 0.06f);
            }
        }

        static float HandleSize(Vector3 worldPos)
        {
            var cam = UnityEngine.Camera.current;
            if (cam == null) return 1f;
            return Vector3.Distance(cam.transform.position, worldPos) * 0.1f;
        }

        protected override void OnSlimeEnter(SlimeController slime)
        {
            // Splash with the pre-entry velocity. OnTriggerEnter2D fires mid-physics-step,
            // so by the next FixedUpdate the entryMultiplier below has already drained the
            // impact velocity — fast slams would otherwise read as gentle taps. isEntry also
            // bypasses the contactRadius + approach-side checks, which both break when the
            // slime tunnels fully across the surface in a single fixed step.
            ApplySlimeImpulse(slime, Time.fixedDeltaTime, isEntry: true);

            slime.MultiplyVelocity(entryMultiplier);
            if (blockAttach) slime.PushBlockAttach();
            slime.PushInFluid();

            PlayEnterSplash(slime);
            StartUnderwaterLoop(slime);
        }

        protected override void OnSlimeExit(SlimeController slime)
        {
            slime.MultiplyVelocity(exitMultiplier);
            if (blockAttach) slime.PopBlockAttach();
            slime.PopInFluid();
            slime.ResetLaunchReference();

            StopUnderwaterLoop();
            PlaySplash(exitClip, slime);
        }

        void PlayEnterSplash(SlimeController slime)
        {
            float speed = slime.Velocity.magnitude;
            AudioClip clip = speed >= hardEnterThreshold ? hardEnterClip : softEnterClip;
            PlaySplash(clip, slime, speed);
        }

        void PlaySplash(AudioClip clip, SlimeController slime)
        {
            PlaySplash(clip, slime, slime.Velocity.magnitude);
        }

        void PlaySplash(AudioClip clip, SlimeController slime, float speed)
        {
            if (clip == null) return;
            float vol = splashVolume * Mathf.Clamp01(speed / audioRefSpeed);
            if (vol <= 0.001f) return;
            float pitch = 1f + Random.Range(-splashPitchJitter, splashPitchJitter);
            SoundService.PlayAt(clip, slime.transform.position,
                                SoundCategory.SFX_Environment, vol, pitch);
        }

        void StartUnderwaterLoop(SlimeController slime)
        {
            if (underwaterLoopClip == null) return;
            if (underwaterLoopHandle.IsValid) return;
            underwaterLoopHandle = SoundService.PlayLooping(
                underwaterLoopClip,
                follow: slime.transform,
                category: SoundCategory.SFX_Environment,
                volume: underwaterLoopVolume,
                spatial3D: true);
        }

        void StopUnderwaterLoop()
        {
            if (!underwaterLoopHandle.IsValid) return;
            SoundService.StopLoop(underwaterLoopHandle);
            underwaterLoopHandle = default;
        }

        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            if (dragFactor < 1f)
                slime.MultiplyVelocity(Mathf.Pow(dragFactor, dt));

            slime.AddExternalVelocity(slime.GravityUp * buoyancy * dt);

            if (maxFallSpeed > 0f)
                slime.ClampFallSpeed(maxFallSpeed);
        }

        protected override void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // Impulse must read the slime's velocity BEFORE ApplyEffect alters it
            // (drag multiplier + buoyancy would bias impactSpeed). Order matters:
            //   1) sample slime velocity -> surface impulse
            //   2) base.FixedUpdate -> ApplyEffect (drag/buoyancy/cap)
            //   3) simulate wave physics
            if (trackedSlime != null)
                ApplySlimeImpulse(trackedSlime, dt);

            base.FixedUpdate();

            SimulateWaves(dt);
        }

        void LateUpdate()
        {
            UpdateMeshVerts();
            UpdateOutlineLine();
        }
    }
}
