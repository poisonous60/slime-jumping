using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using Unity.Cinemachine;

namespace SlimeJumping.Optimization
{
    /// <summary>
    /// Per-FixedUpdate proximity gate that toggles registered <see cref="OffscreenSleepable"/>
    /// GameObjects on/off based on the slime's distance to each one's bounds. Goal: reclaim
    /// idle simulation cost (e.g. TautLine.SyncShapes / WaterZone_v2.SimulateWaves) for
    /// platforms the player can't see.
    ///
    /// Hysteresis: enable radius is the camera viewport diagonal × marginMultiplier; disable
    /// radius is enable × hysteresisFactor. Slime entering the smaller circle wakes a sleeper;
    /// only leaves the larger circle puts it back to sleep, so jitter near the boundary
    /// doesn't thrash SetActive.
    ///
    /// Drop one of these into MainScene as a GameObject. Sleepers self-register via OnEnable.
    /// Manager listens to <see cref="SlimeController.OnSpawned"/> / OnDespawned for slime
    /// reference rather than per-frame polling.
    ///
    /// Range Out of Scope:
    ///   - Does NOT address the SurfaceProbe 33ms regression (that fires for ON-screen curve
    ///     platforms, beyond this manager's reach).
    ///   - Single-scene assumption — extending to additive scenes requires lifecycle review
    ///     (sleeper list cleanup, DontDestroyOnLoad, etc.).
    /// </summary>
    [DefaultExecutionOrder(-9999)]
    public class OffscreenSleeperManager : MonoBehaviour
    {
        [Header("Radii")]
        [Tooltip("Camera viewport diagonal × this = enableRadius (sleeper wakes when slime closer than this).")]
        [Min(0.1f)] [SerializeField] float defaultMarginMultiplier = 1.3f;
        [Tooltip("disableRadius = enableRadius × this. Must be > 1 to give a hysteresis band.")]
        [Min(1.01f)] [SerializeField] float hysteresisFactor = 1.5f;
        [Tooltip("Used when no camera can be resolved (Camera.main and CinemachineBrain both null).")]
        [Min(1f)] [SerializeField] float fallbackRadius = 30f;
        [Tooltip("Z plane the slime is assumed to live on, for perspective camera diagonal calc.")]
        [SerializeField] float slimePlaneZ = 0f;

        [Header("Tick scheduling")]
        [Tooltip("Spread the proximity check across this many FUs. 1 = check every sleeper every FU (simplest, costs ~0.3µs/sleeper). N>1 = check 1/N each FU, taking up to N FUs to react to a hysteresis-boundary crossing. The hysteresis band already provides margin against thrashing, so 5 (~0.1s lag at 50Hz) is safe and cuts the manager's per-FU cost roughly N×.")]
        [Min(1)] [SerializeField] int tickStride = 5;

        [Header("Debug")]
        [SerializeField] bool drawDebugGizmos = true;
        [SerializeField] Color enableRadiusColor = new Color(0.3f, 1f, 0.4f, 0.6f);
        [SerializeField] Color disableRadiusColor = new Color(1f, 0.5f, 0.2f, 0.5f);

        public static OffscreenSleeperManager Instance { get; private set; }
        public float DefaultMarginMultiplier => defaultMarginMultiplier;
        public float HysteresisFactor => hysteresisFactor;

        readonly List<OffscreenSleepable> sleepers = new List<OffscreenSleepable>();
        SlimeController cachedSlime;
        UnityEngine.Camera cachedCam;
        float cachedCameraDiagonal;
        int tickPhase;

        // Inspector observability — counts updated each FU.
        public int SleeperCount => sleepers.Count;
        public int ActiveSleeperCount { get; private set; }
        public int SleepingSleeperCount { get; private set; }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning($"[OffscreenSleeperManager] Duplicate manager on '{name}', destroying.", this);
                Destroy(this);
                return;
            }
            Instance = this;

            SlimeController.OnSpawned += HandleSlimeSpawned;
            SlimeController.OnDespawned += HandleSlimeDespawned;

            // Slime may already be live by the time this manager wakes (DefaultExecutionOrder
            // doesn't guarantee order across MonoBehaviour types in the same scene). Catch any
            // already-spawned slime so we don't wait an extra frame for the next OnEnable.
            cachedSlime = FindFirstObjectByType<SlimeController>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            SlimeController.OnSpawned -= HandleSlimeSpawned;
            SlimeController.OnDespawned -= HandleSlimeDespawned;
        }

        void HandleSlimeSpawned(SlimeController s) => cachedSlime = s;

        void HandleSlimeDespawned(SlimeController s)
        {
            if (cachedSlime == s) cachedSlime = null;
        }

        public void Register(OffscreenSleepable s)
        {
            if (s != null && !sleepers.Contains(s)) sleepers.Add(s);
        }

        public void Unregister(OffscreenSleepable s)
        {
            sleepers.Remove(s);
        }

        void FixedUpdate()
        {
            // Slime null = freeze toggle state (we don't know who's "near" anything). Better
            // to leave content visible than to flip half the scene off when the player is
            // mid-respawn.
            if (cachedSlime == null) return;

            if (cachedCam == null) ResolveCamera();
            cachedCameraDiagonal = ComputeCameraDiagonal();

            Vector2 slimePos = cachedSlime.Position;
            int active = 0, sleeping = 0;

            int phase = tickPhase;
            tickPhase = (tickPhase + 1) % Mathf.Max(1, tickStride);
            int stride = Mathf.Max(1, tickStride);

            // Backward iteration: SetSleeping(true) → SetActive(false) → OnDisable →
            // Unregister → sleepers.Remove(s) mutates the list mid-loop. Forward iteration
            // would shift unvisited elements left and skip one each toggle. Going backward
            // means removed indices are always behind us — already visited.
            //
            // Tick striding: each sleeper is owned by one of `stride` phases (i % stride).
            // We only run the toggle decision for sleepers in this FU's phase, then visit
            // every other sleeper just to count its current sleep state for stats. The
            // toggle path is the bulk of the work (bounds query + distance compare); the
            // counting branch is effectively free.
            for (int i = sleepers.Count - 1; i >= 0; i--)
            {
                var s = sleepers[i];
                if (s == null) continue;

                bool ourPhase = (i % stride) == phase;

                if (ourPhase)
                {
                    if (s.ForceAlwaysActive)
                    {
                        if (s.IsSleeping) s.SetSleeping(false);
                    }
                    else
                    {
                        float margin = s.MarginMultiplierOverride > 0f
                            ? s.MarginMultiplierOverride
                            : defaultMarginMultiplier;
                        float enableR = cachedCameraDiagonal * margin;
                        float disableR = enableR * hysteresisFactor;

                        Bounds b = s.GetActiveWorldBounds();
                        Vector2 closest = b.ClosestPoint(slimePos);
                        float sqrDist = (slimePos - closest).sqrMagnitude;

                        if (s.IsSleeping)
                        {
                            if (sqrDist < enableR * enableR) s.SetSleeping(false);
                        }
                        else
                        {
                            if (sqrDist > disableR * disableR) s.SetSleeping(true);
                        }
                    }
                }

                if (s.IsSleeping) sleeping++;
                else active++;
            }

            ActiveSleeperCount = active;
            SleepingSleeperCount = sleeping;
        }

        float ComputeCameraDiagonal()
        {
            if (cachedCam == null) return fallbackRadius;
            float halfH = cachedCam.orthographic
                ? cachedCam.orthographicSize
                : Mathf.Abs(cachedCam.transform.position.z - slimePlaneZ)
                  * Mathf.Tan(cachedCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * cachedCam.aspect;
            return Mathf.Sqrt(halfW * halfW + halfH * halfH);
        }

        void ResolveCamera()
        {
            cachedCam = UnityEngine.Camera.main;
            if (cachedCam != null) return;

            // Cinemachine setups sometimes don't tag the brain camera "MainCamera" — fall back
            // to grabbing the brain's output camera directly.
            var brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain != null) cachedCam = brain.GetComponent<UnityEngine.Camera>();
        }

        void OnDrawGizmos()
        {
            if (!drawDebugGizmos) return;

            // Center on slime if available (play mode + edit mode auto-find), otherwise on
            // this manager's transform so the radius is at least previewable in edit mode.
            // Edit mode: SlimeController.Awake hasn't run so `rb` is null and reading
            // `Position` (= rb.position) throws — fall back to transform.position.
            Vector3 center;
            SlimeController slime = cachedSlime;
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (slime == null) center = transform.position;
            else center = Application.isPlaying ? (Vector3)slime.Position : slime.transform.position;

            // Camera resolution may be stale in edit mode (cachedCam set during play). Resolve
            // a fresh camera ref so the diagonal calc uses current ortho size / fov.
            UnityEngine.Camera cam = cachedCam;
            if (cam == null)
            {
                cam = UnityEngine.Camera.main;
                if (cam == null)
                {
                    var brain = FindAnyObjectByType<CinemachineBrain>();
                    if (brain != null) cam = brain.GetComponent<UnityEngine.Camera>();
                }
            }

            float diag;
            if (cam == null) diag = fallbackRadius;
            else
            {
                float halfH = cam.orthographic
                    ? cam.orthographicSize
                    : Mathf.Abs(cam.transform.position.z - slimePlaneZ)
                      * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
                float halfW = halfH * cam.aspect;
                diag = Mathf.Sqrt(halfW * halfW + halfH * halfH);
            }

            float enableR = diag * defaultMarginMultiplier;
            float disableR = enableR * hysteresisFactor;

            Gizmos.color = enableRadiusColor;
            Gizmos.DrawWireSphere(center, enableR);
            Gizmos.color = disableRadiusColor;
            Gizmos.DrawWireSphere(center, disableR);
        }

        /// <summary>
        /// Safety net for any candidate GameObject that the editor batch tool didn't catch
        /// (e.g. authored after the last batch run, or from a runtime-instantiated prefab).
        /// Adds a default-config OffscreenSleepable so the proximity gate still applies.
        ///
        /// Per-instance overrides (margin, manual bounds, force-active) are NOT preserved by
        /// this path — they only exist if the marker was authored explicitly. Editor batch
        /// tool remains the primary attach mechanism.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RuntimeFallbackScan()
        {
            // No manager in the loaded scene → no point scanning. Adding markers without a
            // manager just adds inert components (their OnEnable would early-out anyway since
            // Instance is null). Saves 13 FindObjectsByType calls in any scene without a manager.
            if (Instance == null) return;

            int added = 0;
            for (int i = 0; i < OffscreenSleeperCandidates.CandidateTypes.Length; i++)
            {
                var type = OffscreenSleeperCandidates.CandidateTypes[i];
                var instances = FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int j = 0; j < instances.Length; j++)
                {
                    var comp = instances[j] as Component;
                    if (comp == null) continue;
                    if (comp.GetComponent<OffscreenSleepable>() != null) continue;
                    comp.gameObject.AddComponent<OffscreenSleepable>();
                    added++;
                }
            }
            if (added > 0)
                Debug.Log($"[OffscreenSleeperManager] Runtime fallback added {added} OffscreenSleepable marker(s).");
        }
    }
}
