using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Interaction
{
    /// <summary>Rocket visual flies up, explodes at apex (VFX + slime launch), hides, respawns.</summary>
    public class FireworkInteractable_V2 : InteractableBase
    {
        [Header("Rocket Visual")]
        [Tooltip("Child transform of the rocket (mesh/sprite). Will move, hide, and respawn. Keep the collider on a different child or the root.")]
        [SerializeField] private Transform rocketVisual;
        [Tooltip("Offset from rocketVisual's pivot to the visual center of the mesh, in rocketVisual's local space. If the sprite pivot is at its bottom, set +Y so the particle explosion aligns with the mesh's visual center instead of its pivot.")]
        [SerializeField] private Vector2 visualPivotOffset = Vector2.zero;

        [Header("Flight")]
        [Tooltip("Local-space direction the rocket flies in. Will be normalized.")]
        [SerializeField] private Vector2 flightDirection = Vector2.up;
        [Tooltip("Distance (units) the rocket travels before exploding.")]
        [SerializeField] private float flightDistance = 5f;
        [Tooltip("Seconds from ignition to explosion.")]
        [SerializeField] private float flightDuration = 0.35f;
        [Tooltip("Curve shaping the flight (0 = origin, 1 = explosion point).")]
        [SerializeField] private AnimationCurve flightCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Slime Launch (fires at explosion moment)")]
        [SerializeField] private Vector2 launchVelocity = new Vector2(0f, 18f);
        [Tooltip("If true, direction is (slime - explosionPoint).normalized * launchVelocity.magnitude.")]
        [SerializeField] private bool launchAwayFromExplosion = false;

        [Header("FX")]
        [Tooltip("Explosion particle. Moved to the apex at explosion time, then Played. Should survive rocket hide (don't child to rocketVisual). Ignored in VFX-sync mode — SF_1 handles its own explosion via sub-emitters.")]
        [SerializeField] private ParticleSystem explosionVFX;
        [Tooltip("Optional trail/jet VFX that plays during flight. Usually childed to rocketVisual so it follows the rocket automatically. Works in both Transform and VFX-sync modes.")]
        [SerializeField] private ParticleSystem trailVFX;
        [SerializeField] private SlimeJumping.Audio.SoundDef launchSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef explosionSound;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private float impulseForce = 0f;

        [Header("VFX-Sync Flight Mode (overrides Flight section if assigned)")]
        [Tooltip("Optional. Prefab whose root ParticleSystem emits a single self-propelled particle (e.g. NamuFX SF_1). If set, the rocket mesh is teleported to that particle's position each frame; on particle death, SF_1's sub-emitters fire the burst and this script fires slime launch + impulse + SFX. The root particle's renderer is hidden at runtime.")]
        [SerializeField] private GameObject flightVfxPrefab;
        [Tooltip("Seconds after ignition before the spawned VFX instance is destroyed. Must exceed SF_1's sub-emitter burst duration so the explosion finishes.")]
        [SerializeField] private float flightVfxCleanupDelay = 5f;
        [Tooltip("Force loop=false on the spawned root ParticleSystem so only one rocket particle is emitted. SF_1's default is looping which would spam rockets.")]
        [SerializeField] private bool forceNonLooping = true;
        [Tooltip("Disable the spawned root particle's renderer so only the rocket mesh + sub-emitter bursts are visible.")]
        [SerializeField] private bool hideRootRenderer = true;
        [Tooltip("Safety cap: seconds before giving up if no particle is emitted (prevents infinite coroutine if prefab is misconfigured).")]
        [SerializeField] private float vfxSyncWaitTimeout = 0.5f;

        [Header("Respawn")]
        [Tooltip("Seconds after explosion until the rocket respawns at origin.")]
        [SerializeField] private float respawnDelay = 3f;
        [Tooltip("Seconds for the pop-in scale animation on respawn. Set 0 to skip.")]
        [SerializeField] private float respawnDuration = 0.35f;
        [Tooltip("Scale curve during respawn (0 -> 1 over respawnDuration). Overshoot above 1 for a pop feel.")]
        [SerializeField] private AnimationCurve respawnScaleCurve =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1.15f), new Keyframe(1f, 1f));
        [Tooltip("Optional trigger name fired on this.animator when rocket respawns (leave empty to skip).")]
        [SerializeField] private string respawnAnimatorTrigger = "";

        private Vector3 rocketOriginLocal;
        private Vector3 rocketBaseScale = Vector3.one;
        private bool busy;

        protected override void Awake()
        {
            base.Awake();
            if (rocketVisual != null)
            {
                rocketOriginLocal = rocketVisual.localPosition;
                rocketBaseScale = rocketVisual.localScale;
            }
        }

        protected override bool ExtraCanInteract(SlimeController slime) => !busy;

        protected override void OnInteract(SlimeController slime)
        {
            if (flightVfxPrefab != null)
                StartCoroutine(FireSequenceVfxSync(slime));
            else
                StartCoroutine(FireSequenceTransformFlight(slime));
        }

        private IEnumerator FireSequenceTransformFlight(SlimeController slime)
        {
            busy = true;

            SoundService.Play(launchSound, transform.position);
            if (trailVFX != null) trailVFX.Play();

            Vector3 dir3 = new Vector3(flightDirection.x, flightDirection.y, 0f);
            if (dir3.sqrMagnitude < 0.0001f) dir3 = Vector3.up;
            dir3 = dir3.normalized;

            Vector3 startLocal = rocketOriginLocal;
            Vector3 endLocal = startLocal + dir3 * flightDistance;

            float t = 0f;
            while (t < flightDuration)
            {
                t += Time.deltaTime;
                float p = flightCurve.Evaluate(Mathf.Clamp01(t / flightDuration));
                if (rocketVisual != null)
                    rocketVisual.localPosition = Vector3.LerpUnclamped(startLocal, endLocal, p);
                yield return null;
            }
            if (rocketVisual != null)
                rocketVisual.localPosition = endLocal;

            Vector3 pivotWorld = rocketVisual != null
                ? rocketVisual.position
                : transform.TransformPoint(endLocal);
            Vector3 visualOffsetWorld = transform.TransformVector(new Vector3(visualPivotOffset.x, visualPivotOffset.y, 0f));
            Vector3 explosionWorld = pivotWorld + visualOffsetWorld;

            if (explosionVFX != null)
            {
                explosionVFX.transform.position = explosionWorld;
                explosionVFX.Play();
            }
            if (trailVFX != null) trailVFX.Stop();

            SoundService.Play(explosionSound, explosionWorld);
            if (impulseEmitter != null && impulseForce > 0f) impulseEmitter.Emit(impulseForce);

            LaunchSlime(slime, explosionWorld);

            if (rocketVisual != null) rocketVisual.gameObject.SetActive(false);

            yield return new WaitForSeconds(respawnDelay);

            if (rocketVisual != null)
            {
                rocketVisual.localPosition = rocketOriginLocal;
                float p0 = respawnDuration > 0f ? respawnScaleCurve.Evaluate(0f) : 1f;
                rocketVisual.localScale = rocketBaseScale * p0;
                rocketVisual.gameObject.SetActive(true);
            }
            if (animator != null && !string.IsNullOrEmpty(respawnAnimatorTrigger))
                animator.SetTrigger(respawnAnimatorTrigger);

            if (respawnDuration > 0f && rocketVisual != null)
            {
                t = 0f;
                while (t < respawnDuration)
                {
                    t += Time.deltaTime;
                    float p = respawnScaleCurve.Evaluate(Mathf.Clamp01(t / respawnDuration));
                    rocketVisual.localScale = rocketBaseScale * p;
                    yield return null;
                }
                rocketVisual.localScale = rocketBaseScale;
            }

            busy = false;
        }

        private IEnumerator FireSequenceVfxSync(SlimeController slime)
        {
            busy = true;
            SoundService.Play(launchSound, transform.position);
            if (trailVFX != null) trailVFX.Play();

            Vector3 pivotOriginWorld = transform.TransformPoint(rocketOriginLocal);
            Vector3 visualOffsetWorld = transform.TransformVector(new Vector3(visualPivotOffset.x, visualPivotOffset.y, 0f));
            Vector3 visualCenterOriginWorld = pivotOriginWorld + visualOffsetWorld;
            var vfxGo = Instantiate(flightVfxPrefab, visualCenterOriginWorld, flightVfxPrefab.transform.rotation);
            var rootPs = vfxGo.GetComponent<ParticleSystem>();
            if (rootPs == null)
            {
                Debug.LogWarning($"[FireworkInteractable_V2] flightVfxPrefab '{flightVfxPrefab.name}' root has no ParticleSystem.", this);
                Destroy(vfxGo);
                busy = false;
                yield break;
            }

            {
                var main = rootPs.main;
                if (forceNonLooping) main.loop = false;
                // World sim + explicit EmitParams: direction survives the prefab's shape/rotation/randomization.
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = flightDuration;
            }
            // Disable shape so its Cone spread (SF_1: 50°) doesn't randomize direction.
            {
                var shape = rootPs.shape;
                shape.enabled = false;
            }
            // Suppress all continuous/burst emission; we emit exactly one particle manually.
            // SF_1's default rateOverTime=10/s × duration=5s would fire ~50 rockets otherwise.
            {
                var emission = rootPs.emission;
                emission.rateOverTime = 0f;
                emission.rateOverDistance = 0f;
                emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            }

            if (hideRootRenderer)
            {
                var rend = rootPs.GetComponent<ParticleSystemRenderer>();
                if (rend != null) rend.enabled = false;
            }

            Vector3 dir3 = new Vector3(flightDirection.x, flightDirection.y, 0f);
            if (dir3.sqrMagnitude < 0.0001f) dir3 = Vector3.up;
            Vector3 worldDir = transform.TransformDirection(dir3.normalized);
            float flightSpeed = flightDistance / Mathf.Max(0.01f, flightDuration);

            rootPs.Clear(true);
            rootPs.Play(true);
            var emitParams = new ParticleSystem.EmitParams
            {
                position = visualCenterOriginWorld,
                velocity = worldDir * flightSpeed,
                applyShapeToPosition = false,
            };
            rootPs.Emit(emitParams, 1);

            Transform psTf = rootPs.transform;
            ParticleSystemSimulationSpace simSpace = rootPs.main.simulationSpace;
            var buf = new ParticleSystem.Particle[1];

            float waited = 0f;
            while (rootPs.particleCount == 0 && waited < vfxSyncWaitTimeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            if (rootPs.particleCount == 0)
            {
                Debug.LogWarning("[FireworkInteractable_V2] VFX prefab emitted no particle within timeout.", this);
                Destroy(vfxGo);
                yield return StartCoroutine(RespawnRocket());
                busy = false;
                yield break;
            }

            Vector3 lastWorldPos = visualCenterOriginWorld;
            while (rootPs.particleCount > 0)
            {
                int n = rootPs.GetParticles(buf);
                if (n > 0)
                {
                    // Particle tracks the mesh's VISUAL CENTER; set the pivot so pivot + offset = visual center.
                    Vector3 world = simSpace == ParticleSystemSimulationSpace.World
                        ? buf[0].position
                        : psTf.TransformPoint(buf[0].position);
                    if (rocketVisual != null)
                        rocketVisual.position = world - visualOffsetWorld;
                    lastWorldPos = world;
                }
                yield return null;
            }

            // Particle died — SF_1's Death sub-emitters are now firing the burst.
            SoundService.Play(explosionSound, lastWorldPos);
            if (impulseEmitter != null && impulseForce > 0f) impulseEmitter.Emit(impulseForce);
            LaunchSlime(slime, lastWorldPos);

            // StopEmittingAndClear so leftover smoke doesn't freeze mid-air when rocketVisual deactivates.
            if (trailVFX != null) trailVFX.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (rocketVisual != null) rocketVisual.gameObject.SetActive(false);
            Destroy(vfxGo, flightVfxCleanupDelay);

            yield return new WaitForSeconds(respawnDelay);
            yield return StartCoroutine(RespawnRocket());

            busy = false;
        }

        private IEnumerator RespawnRocket()
        {
            if (rocketVisual != null)
            {
                rocketVisual.localPosition = rocketOriginLocal;
                float p0 = respawnDuration > 0f ? respawnScaleCurve.Evaluate(0f) : 1f;
                rocketVisual.localScale = rocketBaseScale * p0;
                rocketVisual.gameObject.SetActive(true);
            }
            if (animator != null && !string.IsNullOrEmpty(respawnAnimatorTrigger))
                animator.SetTrigger(respawnAnimatorTrigger);

            if (respawnDuration > 0f && rocketVisual != null)
            {
                float t = 0f;
                while (t < respawnDuration)
                {
                    t += Time.deltaTime;
                    float p = respawnScaleCurve.Evaluate(Mathf.Clamp01(t / respawnDuration));
                    rocketVisual.localScale = rocketBaseScale * p;
                    yield return null;
                }
                rocketVisual.localScale = rocketBaseScale;
            }
        }

        private void LaunchSlime(SlimeController slime, Vector3 explosionWorld)
        {
            Vector2 v = launchVelocity;
            if (launchAwayFromExplosion)
            {
                Vector2 dir = slime.Position - (Vector2)explosionWorld;
                v = dir.sqrMagnitude > 0.0001f
                    ? dir.normalized * launchVelocity.magnitude
                    : launchVelocity;
            }
            slime.ForceAirborneWithVelocity(v);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 dir3 = new Vector3(flightDirection.x, flightDirection.y, 0f);
            if (dir3.sqrMagnitude < 0.0001f) dir3 = Vector3.up;
            dir3 = dir3.normalized;

            Vector3 pivotWorld = rocketVisual != null
                ? (Application.isPlaying ? transform.TransformPoint(rocketOriginLocal) : rocketVisual.position)
                : transform.position;
            Vector3 visualOffsetWorld = transform.TransformVector(new Vector3(visualPivotOffset.x, visualPivotOffset.y, 0f));
            Vector3 visualStart = pivotWorld + visualOffsetWorld;
            Vector3 worldDir = transform.TransformVector(dir3);
            Vector3 visualEnd = visualStart + worldDir * flightDistance;

            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.9f);
            Gizmos.DrawLine(visualStart, visualEnd);
            Gizmos.DrawWireSphere(visualEnd, 0.3f);
            Gizmos.DrawWireSphere(visualStart, 0.12f);

            if (visualPivotOffset.sqrMagnitude > 1e-6f)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.8f, 0.9f);
                Gizmos.DrawLine(pivotWorld, visualStart);
                Gizmos.DrawSphere(pivotWorld, 0.06f);
            }
        }
    }
}
