using System.Collections;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Interaction
{
    /// <summary>Multi-shell firework: rockets stand on the launcher, each lifts off on its own timing and
    /// bursts at its own anchor, and every burst shoves the slime radially away from that point.</summary>
    public class FireworkInteractable_V3 : InteractableBase
    {
        [System.Serializable]
        public class Shell
        {
            [Tooltip("Rocket standing in the scene. Flies to the anchor, hides at the burst, respawns here. Rest rotation must be upright — the path tilt is applied on top of it.")]
            public Transform visual;
            [Tooltip("World point the shell flies to and bursts at. Drag an empty in the scene.")]
            public Transform targetAnchor;
            [Tooltip("Seconds after interact until this shell lifts off.")]
            [Min(0f)] public float igniteDelay = 0f;
            [Tooltip("Seconds from lift-off to burst.")]
            [Min(0.05f)] public float flightDuration = 0.8f;
        }

        [Header("Shells")]
        [SerializeField] private Shell[] shells = new Shell[3];

        [Header("Shell VFX")]
        [Tooltip("Prefab whose root ParticleSystem emits one self-propelled particle with Death sub-emitters for the burst (e.g. NamuFX SF_1). One instance per shell.")]
        [SerializeField] private GameObject shellVfxPrefab;
        [Tooltip("Rocket pivot → mesh centre, in world XY at rest (stage 1 uses -0.3 / 0.29). The particle rides the centre and the mesh hangs off it by this.")]
        [SerializeField] private Vector2 visualPivotOffset = Vector2.zero;
        [Tooltip("Seconds after burst before the spawned instance is destroyed. Must exceed the sub-emitter burst duration.")]
        [SerializeField] private float shellVfxCleanupDelay = 5f;

        [Header("Slime Launch (every burst)")]
        [SerializeField] private Vector2 launchVelocity = new Vector2(0f, 22f);
        [Tooltip("If true, direction is (slime - burstPoint).normalized * launchVelocity.magnitude.")]
        [SerializeField] private bool launchAwayFromExplosion = true;
        [Tooltip("How far a burst can throw the slime, in world units — the gizmo circle at each anchor. Outside it the burst does nothing; inside, the push is full strength (no falloff).")]
        [SerializeField, Min(0.1f)] private float launchRadius = 10f;

        [Header("Respawn")]
        [Tooltip("Seconds after the LAST shell bursts until every rocket reappears together.")]
        [SerializeField] private float respawnDelay = 3f;
        [Tooltip("Seconds for the pop-in scale animation. 0 skips it.")]
        [SerializeField] private float respawnDuration = 0.35f;
        [SerializeField] private AnimationCurve respawnScaleCurve =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.6f, 1.15f), new Keyframe(1f, 1f));

        [Header("FX")]
        [SerializeField] private SlimeJumping.Audio.SoundDef igniteSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef burstSound;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [SerializeField] private float impulseForce = 0f;

        // Rest pose captured before anything moves, so a hidden rocket can always be put back.
        private class ShellState
        {
            public Vector3 restLocalPos;
            public Quaternion restLocalRot;
            public Quaternion restWorldRot;
            public Vector3 restScale;
            public ParticleSystem[] trails;
        }

        // Fly() is a coroutine, so its outcome comes back through this.
        private class FlightResult
        {
            public bool burst;
            public Vector3 point;
        }

        private ShellState[] states;
        private bool busy;
        private int liveShells;

        // Give up waiting for the emitted particle rather than spinning forever on a misconfigured prefab.
        private const float VfxSpawnTimeout = 0.5f;
        private const float MinFlightDuration = 0.01f;

        protected override void Awake()
        {
            base.Awake();

            states = new ShellState[shells.Length];
            for (int i = 0; i < shells.Length; i++)
            {
                var shell = shells[i];
                if (shell == null) continue;
                if (shell.visual == null || shell.targetAnchor == null)
                {
                    Debug.LogWarning($"[{GetType().Name}] {name} shell {i} is missing its visual or target anchor.", this);
                    continue;
                }

                states[i] = new ShellState
                {
                    restLocalPos = shell.visual.localPosition,
                    restLocalRot = shell.visual.localRotation,
                    restWorldRot = shell.visual.rotation,
                    restScale = shell.visual.localScale,
                    trails = shell.visual.GetComponentsInChildren<ParticleSystem>(true),
                };
            }
        }

        // Coroutines die with the object, so a rocket hidden mid-volley would never come back.
        private void OnDisable()
        {
            if (!busy) return;

            StopAllCoroutines();
            for (int i = 0; i < shells.Length; i++)
            {
                if (shells[i] == null || states[i] == null || shells[i].visual == null) continue;
                StopTrails(states[i]);
                RestoreRestPose(shells[i], states[i]);
            }
            liveShells = 0;
            busy = false;
        }

        protected override bool ExtraCanInteract(SlimeController slime) => !busy;

        protected override void OnInteract(SlimeController slime)
        {
            int ready = 0;
            for (int i = 0; i < shells.Length; i++)
                if (IsShellReady(i)) ready++;

            if (ready == 0)
            {
                Debug.LogWarning($"[{GetType().Name}] {name} has no usable shell.", this);
                return;
            }

            // Counted up front: a shell finishing early must not end the volley while others still fly.
            busy = true;
            liveShells = ready;
            StartCoroutine(Volley(slime));
        }

        private IEnumerator Volley(SlimeController slime)
        {
            for (int i = 0; i < shells.Length; i++)
                if (IsShellReady(i)) StartCoroutine(FireShell(shells[i], states[i], slime));

            while (liveShells > 0) yield return null;

            // Whole rack reloads together once the last shell has burst.
            yield return new WaitForSeconds(respawnDelay);
            for (int i = 0; i < shells.Length; i++)
                if (shells[i] != null && states[i] != null) StartCoroutine(RespawnShell(shells[i], states[i]));
            if (respawnDuration > 0f) yield return new WaitForSeconds(respawnDuration);

            busy = false;
        }

        private bool IsShellReady(int i)
        {
            var shell = shells[i];
            return shell != null && states[i] != null
                && shell.visual != null && shell.targetAnchor != null;
        }

        private IEnumerator FireShell(Shell shell, ShellState state, SlimeController slime)
        {
            // finally, not a tail call: every exit (including StopAllCoroutines) must release the gate.
            try
            {
                if (shell.igniteDelay > 0f)
                    yield return new WaitForSeconds(shell.igniteDelay);
                if (shell.visual == null || shell.targetAnchor == null) yield break;

                // Rockets stand upright on the launcher and tilt into their path only at lift-off.
                Vector3 to = shell.targetAnchor.position;
                Quaternion align = Quaternion.identity;
                Vector3 heading = to - shell.visual.position;
                if (heading.sqrMagnitude > 0.0001f)
                    align = Quaternion.FromToRotation(Vector3.up, heading.normalized);
                shell.visual.rotation = align * state.restWorldRot;

                // V2's VFX-sync contract: the particle IS the shell and the mesh is hung off it by the
                // pivot→centre offset, so flight and burst can never drift apart. The offset tilts with
                // the rocket, and lift-off starts at the mesh centre so nothing jumps at ignition.
                Vector3 centreOffset = align * new Vector3(visualPivotOffset.x, visualPivotOffset.y, 0f);
                Vector3 from = shell.visual.position + centreOffset;

                var vfx = SpawnShellVfx(from, to, shell.flightDuration);
                foreach (var ps in state.trails) if (ps != null) ps.Play();
                SoundService.Play(igniteSound, from);

                var flight = new FlightResult { point = from };
                yield return Fly(vfx, shell.visual, from, to, centreOffset, shell.flightDuration, flight);

                StopTrails(state);
                if (shell.visual != null) shell.visual.gameObject.SetActive(false);

                // No burst when the VFX never produced its particle — SF_1 owns the explosion, so
                // firing sound and push here would be a burst the player never sees.
                if (flight.burst)
                {
                    SoundService.Play(burstSound, flight.point);
                    if (impulseEmitter != null && impulseForce > 0f) impulseEmitter.Emit(impulseForce);
                    LaunchSlime(slime, flight.point);
                }
            }
            finally
            {
                // Volley owns the reload; it waits for this count to hit zero.
                liveShells--;
            }
        }

        // Mesh follows the live particle every frame and the burst is that particle's death, so there
        // is no second clock to drift against. Without a VFX prefab the mesh flies on its own clock.
        private IEnumerator Fly(ParticleSystem vfx, Transform visual, Vector3 from, Vector3 to,
                                Vector3 centreOffset, float flightDuration, FlightResult result)
        {
            if (vfx != null)
            {
                float waited = 0f;
                while (vfx != null && vfx.particleCount == 0 && waited < VfxSpawnTimeout)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }

                if (vfx == null || vfx.particleCount == 0)
                {
                    Debug.LogWarning($"[{GetType().Name}] {name}: shell VFX emitted no particle.", this);
                    yield break;
                }

                var buf = new ParticleSystem.Particle[1];
                while (vfx != null && vfx.particleCount > 0)
                {
                    if (visual == null) yield break;
                    if (vfx.GetParticles(buf) > 0)
                    {
                        result.point = buf[0].position;
                        visual.position = result.point - centreOffset;
                    }
                    yield return null;
                }
                result.burst = true;
                yield break;
            }

            float duration = Mathf.Max(MinFlightDuration, flightDuration);
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (visual == null) yield break;
                visual.position = Vector3.Lerp(from - centreOffset, to - centreOffset, t / duration);
                yield return null;
            }
            result.point = to;
            result.burst = true;
        }

        private IEnumerator RespawnShell(Shell shell, ShellState state)
        {
            if (shell.visual == null) yield break;

            RestoreRestPose(shell, state);
            if (respawnDuration <= 0f) yield break;

            shell.visual.localScale = state.restScale * respawnScaleCurve.Evaluate(0f);
            float t = 0f;
            while (t < respawnDuration)
            {
                t += Time.deltaTime;
                if (shell.visual == null) yield break;
                shell.visual.localScale = state.restScale * respawnScaleCurve.Evaluate(Mathf.Clamp01(t / respawnDuration));
                yield return null;
            }
            shell.visual.localScale = state.restScale;
        }

        private static void RestoreRestPose(Shell shell, ShellState state)
        {
            shell.visual.localPosition = state.restLocalPos;
            shell.visual.localRotation = state.restLocalRot;
            shell.visual.localScale = state.restScale;
            shell.visual.gameObject.SetActive(true);
        }

        // Clear, not just stop: leftover trail particles would otherwise freeze mid-air on hide.
        private static void StopTrails(ShellState state)
        {
            foreach (var ps in state.trails)
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // One particle, straight to the anchor, dying exactly there — its Death sub-emitters are the burst.
        private ParticleSystem SpawnShellVfx(Vector3 from, Vector3 to, float flightDuration)
        {
            if (shellVfxPrefab == null) return null;

            var go = Instantiate(shellVfxPrefab, from, shellVfxPrefab.transform.rotation);
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                Debug.LogWarning($"[{GetType().Name}] shellVfxPrefab '{shellVfxPrefab.name}' root has no ParticleSystem.", this);
                Destroy(go);
                return null;
            }

            float duration = Mathf.Max(MinFlightDuration, flightDuration);

            var main = ps.main;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = duration;
            // Gravity and the limit module's dampen both bend the path, so the particle would die
            // short of the anchor; the authored burst point wins over the prefab's deceleration look.
            main.gravityModifier = 0f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = false;
            // The prefab's cone shape randomizes direction and its emission would spam shells.
            var shape = ps.shape;
            shape.enabled = false;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(System.Array.Empty<ParticleSystem.Burst>());
            // The rocket mesh is the shell, so don't draw the particle on top of it.
            var rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null) rend.enabled = false;

            ps.Clear(true);
            ps.Play(true);
            ps.Emit(new ParticleSystem.EmitParams
            {
                position = from,
                velocity = (to - from) / duration,
                applyShapeToPosition = false,
            }, 1);

            Destroy(go, duration + shellVfxCleanupDelay);
            return ps;
        }

        // V2.LaunchSlime verbatim, run once per burst, gated by launchRadius.
        private void LaunchSlime(SlimeController slime, Vector3 explosionWorld)
        {
            if (slime == null) return;
            if ((slime.Position - (Vector2)explosionWorld).magnitude > launchRadius) return;

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
            for (int i = 0; i < shells.Length; i++)
            {
                var shell = shells[i];
                if (shell == null || shell.visual == null || shell.targetAnchor == null) continue;

                Vector3 from = shell.visual.position;
                Vector3 to = shell.targetAnchor.position;
                Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
                Gizmos.DrawLine(from, to);
                Gizmos.DrawWireSphere(from, 0.12f);
                Gizmos.DrawWireSphere(to, 0.3f);
                Gizmos.color = new Color(1f, 0.45f, 0.15f, 0.9f);
                Gizmos.DrawWireSphere(to, launchRadius);
            }
        }
    }
}
