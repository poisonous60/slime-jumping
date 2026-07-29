using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using Unity.Cinemachine;

namespace SlimeJumping.Optimization
{
    /// <summary>Proximity gate that SetActive-toggles registered OffscreenSleepable objects by slime distance.</summary>
    /// Enable radius = camera diagonal × margin, disable = enable × hysteresisFactor — the band
    /// keeps boundary jitter from thrashing SetActive. Single-scene assumption.
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

            // Slime may already be live: DefaultExecutionOrder doesn't guarantee we Awake
            // before it, so the OnSpawned event alone would cost an extra frame.
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
            if (s == null) return;
            // Stale markers survive (nothing removes them), and sleeping e.g. a wrapper above a
            // SnowField wipes its strips on OnDisable. Reject gated objects so they stay inert.
            if (OffscreenSleeperCandidates.ShouldSkipAttach(s.gameObject)) return;
            if (!sleepers.Contains(s)) sleepers.Add(s);
        }

        public void Unregister(OffscreenSleepable s)
        {
            sleepers.Remove(s);
        }

        void FixedUpdate()
        {
            // Slime null (mid-respawn) = freeze toggle state, else half the scene flips off.
            if (cachedSlime == null) return;

            if (cachedCam == null) ResolveCamera();
            cachedCameraDiagonal = ComputeCameraDiagonal();

            Vector2 slimePos = cachedSlime.Position;
            int active = 0, sleeping = 0;

            int phase = tickPhase;
            tickPhase = (tickPhase + 1) % Mathf.Max(1, tickStride);
            int stride = Mathf.Max(1, tickStride);

            // Must iterate backward: SetSleeping(true) → OnDisable → Unregister removes from
            // this list mid-loop, and forward iteration would skip one sleeper per toggle.
            // Striding: only phase-owned sleepers run the toggle decision; the rest just count.
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

            // Cinemachine setups sometimes don't tag the brain camera "MainCamera".
            var brain = FindAnyObjectByType<CinemachineBrain>();
            if (brain != null) cachedCam = brain.GetComponent<UnityEngine.Camera>();
        }

        void OnDrawGizmos()
        {
            if (!drawDebugGizmos) return;

            // Edit mode: SlimeController.Awake hasn't run, so `rb` is null and reading
            // `Position` (= rb.position) throws — use transform.position there.
            Vector3 center;
            SlimeController slime = cachedSlime;
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
            if (slime == null) center = transform.position;
            else center = Application.isPlaying ? (Vector3)slime.Position : slime.transform.position;

            // cachedCam is only set during play; resolve fresh so edit-mode ortho size is current.
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

        /// <summary>Attaches default-config markers to candidates the editor batch tool missed; per-instance overrides are lost on this path.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RuntimeFallbackScan()
        {
            // No manager = markers would be inert anyway; skips 13 FindObjectsByType calls.
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
                    if (OffscreenSleeperCandidates.ShouldSkipAttach(comp.gameObject)) continue;
                    comp.gameObject.AddComponent<OffscreenSleepable>();
                    added++;
                }
            }
            if (added > 0)
                Debug.Log($"[OffscreenSleeperManager] Runtime fallback added {added} OffscreenSleepable marker(s).");
        }
    }
}
