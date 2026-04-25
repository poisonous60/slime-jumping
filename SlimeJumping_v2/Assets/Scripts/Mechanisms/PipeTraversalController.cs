using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Drives the slime through a pipe shortcut as a 5-phase sequence:
    ///   1. Absorb  — input/physics locked, slime lerps from entry edge to entry center.
    ///   2. Hidden traversal — slime alpha = 0, position lerps along PipePath at uniform speed,
    ///      PipeDotTrail brightens a sliding window so the player sees the in-wall journey.
    ///   3. Emerge  — slime appears at exit center, lerps outward to exit edge.
    ///   4. Teleport — final position + outward velocity.
    ///   5. Release — control returns.
    /// commitment: once Begin() runs the slime cannot cancel until Release fires.
    /// </summary>
    public class PipeTraversalController : MonoBehaviour, IExternalController
    {
        [Tooltip("Seconds for the slime to be pulled from the entry trigger edge into the wall.")]
        [SerializeField] private float absorbDuration = 0.45f;

        [Tooltip("World units per second along the path. Short pipes (~5 units) take ~0.5s, long pipes (~20 units) ~2s.")]
        [SerializeField] private float traversalSpeed = 10f;

        [Tooltip("Seconds for the slime to lerp from the exit center back to the surface edge before control is returned.")]
        [SerializeField] private float emergeDuration = 0.25f;

        [Tooltip("Cooldown applied to both endpoints after the traversal completes.")]
        [SerializeField] private float postTransitCooldown = 0.5f;

        [Tooltip("How far inside the wall (along pipeInwardDir) the absorb/emerge anchors are placed. Roughly the slime radius.")]
        [SerializeField] private float wallPenetration = 0.5f;

        private SlimeController slime;
        private PipeEntrance entry;
        private PipeEntrance exit;
        private PipePath path;
        private SlimeVisual slimeVisual;
        private SpriteRenderer slimeSprite;
        private MeshRenderer slimeMesh;
        private PipeDotTrail trail;
        private bool active;

        public bool IsActive => active;

        public void Begin(SlimeController slimeController, PipeEntrance entryEnd, PipeEntrance exitEnd, PipePath pipePath)
        {
            if (active)
            {
                Debug.LogWarning("[PipeTraversalController] Begin called while already traversing — ignoring.");
                return;
            }

            slime = slimeController;
            entry = entryEnd;
            exit = exitEnd;
            path = pipePath;

            slimeVisual = slime.GetComponentInChildren<SlimeVisual>();
            slimeSprite = slime.GetComponent<SpriteRenderer>();
            slimeMesh = slime.GetComponentInChildren<MeshRenderer>();

            trail = path != null ? path.GetComponent<PipeDotTrail>() : null;

            slime.SetOverride(this);
        }

        public void OnTakeControl(SlimeController s)
        {
            active = true;
            if (slimeVisual != null) slimeVisual.IsExternallyAnimated = true;
            slime.OverrideVelocity(Vector2.zero);
            StartCoroutine(RunSequence());
        }

        public void OnReleaseControl(SlimeController s)
        {
            active = false;
            if (slimeVisual != null) slimeVisual.IsExternallyAnimated = false;
            SetSlimeVisible(true);
            SetSlimeAlpha(1f);
            if (trail != null) trail.ResetIdle();
        }

        public void ExternalUpdate(SlimeController s) { }

        public void ExternalFixedUpdate(SlimeController s, float dt)
        {
            slime.OverrideVelocity(Vector2.zero);
        }

        private IEnumerator RunSequence()
        {
            Vector2 entryCenter = (Vector2)entry.transform.position;
            Vector2 exitCenter = (Vector2)exit.transform.position;
            Vector2 entryInward = entry.PipeInwardDir;
            Vector2 exitInward = exit.PipeInwardDir;

            Vector2 entryEdge = slime.Position;
            Vector2 entryDeep = entryCenter + entryInward * wallPenetration;
            Vector2 exitDeep = exitCenter + exitInward * wallPenetration;
            Vector2 exitEdge = exitCenter - exitInward * wallPenetration;

            // ---- Phase 1: absorb ----
            float t = 0f;
            while (t < absorbDuration)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / absorbDuration);
                Vector2 p = Vector2.Lerp(entryEdge, entryDeep, a);
                slime.OverridePosition(p);
                SetSlimeAlpha(Mathf.Clamp01(1f - a));
                yield return null;
            }

            // ---- Phase 2: hidden in-wall traversal ----
            SetSlimeVisible(false);

            float pathLen = (path != null) ? path.TotalLength : Vector2.Distance(entryDeep, exitDeep);
            float traversalDuration = traversalSpeed > 0.01f ? pathLen / traversalSpeed : 0f;
            float traversed = 0f;

            while (traversed < traversalDuration)
            {
                traversed += Time.deltaTime;
                float a = traversalDuration > 0.001f ? Mathf.Clamp01(traversed / traversalDuration) : 1f;

                Vector2 p;
                if (path != null && path.WaypointCount >= 2)
                {
                    Vector2 _;
                    p = path.SampleAtDistance(a * pathLen, out _);
                }
                else
                {
                    p = Vector2.Lerp(entryDeep, exitDeep, a);
                }
                slime.OverridePosition(p);
                if (trail != null) trail.SetProgress(a);
                yield return null;
            }
            if (trail != null) trail.SetProgress(1f);

            // ---- Phase 3: emerge ----
            SetSlimeVisible(true);
            slime.OverridePosition(exitDeep);
            t = 0f;
            while (t < emergeDuration)
            {
                t += Time.deltaTime;
                float a = Mathf.Clamp01(t / emergeDuration);
                Vector2 p = Vector2.Lerp(exitDeep, exitEdge, a);
                slime.OverridePosition(p);
                SetSlimeAlpha(a);
                yield return null;
            }
            SetSlimeAlpha(1f);
            if (trail != null) trail.ResetIdle();

            // ---- Phase 4-5: teleport + release ----
            Vector2 outVel = -exitInward * exit.ExitImpulse;
            Vector2 finalPos = exitEdge;

            entry.NotifyExit(postTransitCooldown);
            exit.NotifyExit(postTransitCooldown);

            slime.ReleaseOverride(this);
            slime.TeleportTo(finalPos, outVel);
        }

        private void SetSlimeVisible(bool visible)
        {
            if (slimeSprite != null) slimeSprite.enabled = visible;
            if (slimeMesh != null) slimeMesh.enabled = visible;
        }

        private void SetSlimeAlpha(float a)
        {
            if (slimeSprite != null)
            {
                var c = slimeSprite.color;
                c.a = a;
                slimeSprite.color = c;
            }
        }
    }
}
