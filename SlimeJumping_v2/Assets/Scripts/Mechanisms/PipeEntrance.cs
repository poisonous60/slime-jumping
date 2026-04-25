using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.InputHandling;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// One end of a pipe shortcut. Pair-wires to another PipeEntrance via
    /// pairedExit. Entry conditions: slime is in trigger AND aiming inward AND
    /// attached to a wall (the stick mechanic doubles as the "I'm pushing into
    /// the wall" check). When all three pass, hands the slime to the shared
    /// PipeTraversalController.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class PipeEntrance : MonoBehaviour
    {
        [Tooltip("Other end of this pipe.")]
        [SerializeField] private PipeEntrance pairedExit;

        [Tooltip("Local-space inward direction (into the wall). Normalized at runtime.")]
        [SerializeField] private Vector2 pipeInwardDirLocal = Vector2.right;

        [Tooltip("Path component on this GameObject (or in pair) that defines the in-wall route. If null, a straight line between entry and exit is used.")]
        [SerializeField] private PipePath path;

        [Tooltip("Traversal controller. If unassigned, one is created on this GameObject at Awake.")]
        [SerializeField] private PipeTraversalController controller;

        [Tooltip("Push-into-wall threshold. Dot(playerInputMove, pipeInwardDir) must exceed this to trigger entry. 0.6 ≈ within ~53° of straight in.")]
        [SerializeField] private float enterDotThreshold = 0.6f;

        [Tooltip("Cooldown after an exit during which both endpoints ignore entry attempts. Prevents immediate re-entry loops at the destination.")]
        [SerializeField] private float reentryCooldown = 0.5f;

        [Tooltip("Initial speed handed to the slime at exit, projected outward along this end's pipeInwardDir (negated). Should be modest — pipes are shortcuts, not launchers.")]
        [SerializeField] private float exitImpulse = 4f;

        private BoxCollider2D triggerCollider;
        private float cooldownUntil;
        private SlimeController overlappingSlime;
        private SlimeInputReader overlappingReader;

        public PipeEntrance PairedExit => pairedExit;
        public PipePath Path => path;
        public Vector2 PipeInwardDir => transform.TransformDirection(pipeInwardDirLocal).normalized;
        public float ExitImpulse => exitImpulse;
        public PipeTraversalController Controller => controller;

        private void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        private void Awake()
        {
            triggerCollider = GetComponent<BoxCollider2D>();
            triggerCollider.isTrigger = true;

            if (controller == null) controller = GetComponent<PipeTraversalController>();
            if (controller == null) controller = gameObject.AddComponent<PipeTraversalController>();
        }

        public void NotifyExit(float exitCooldownDuration)
        {
            cooldownUntil = Time.time + exitCooldownDuration;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            overlappingSlime = slime;
            overlappingReader = slime.GetComponent<SlimeInputReader>();
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != overlappingSlime) return;
            overlappingSlime = null;
            overlappingReader = null;
        }

        private void Update()
        {
            if (overlappingSlime == null) return;
            if (pairedExit == null) return;
            if (Time.time < cooldownUntil) return;
            if (overlappingSlime.HasOverride) return;
            if (overlappingSlime.CurrentState != SlimeController.SlimeState.Attached) return;

            // Player must be holding a movement input that pushes into the wall.
            // While Attached the slime's AimDirection collapses to surfaceNormal
            // (which points OUT of the wall), so we read the raw input instead.
            if (overlappingReader == null) return;
            Vector2 input = overlappingReader.LastInputMove;
            if (input.sqrMagnitude < 0.01f) return;
            float dot = Vector2.Dot(input.normalized, PipeInwardDir);
            if (dot < enterDotThreshold) return;

            BeginTraversal(overlappingSlime);
        }

        private void BeginTraversal(SlimeController slime)
        {
            // Lock both endpoints during traversal — re-entry check uses cooldownUntil
            // but the controller will also cooldown the exit on completion.
            cooldownUntil = Time.time + reentryCooldown;
            pairedExit.cooldownUntil = Time.time + reentryCooldown;

            // Resolve the path: prefer this end's, then the other end's, else fall
            // back to a virtual two-point straight line (controller handles null path).
            PipePath usedPath = path != null ? path : pairedExit.path;
            controller.Begin(slime, this, pairedExit, usedPath);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col != null)
            {
                Gizmos.matrix = transform.localToWorldMatrix;
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.2f);
                Gizmos.DrawCube(col.offset, col.size);
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
                Gizmos.DrawWireCube(col.offset, col.size);
                Gizmos.matrix = Matrix4x4.identity;
            }

            // Inward direction arrow.
            Vector3 origin = transform.position;
            Vector3 inward = transform.TransformDirection(pipeInwardDirLocal.normalized);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(origin, origin + inward * 1.0f);
            Gizmos.DrawSphere(origin + inward * 1.0f, 0.08f);

            if (pairedExit != null)
            {
                Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.4f);
                Gizmos.DrawLine(transform.position, pairedExit.transform.position);
            }
        }
#endif
    }
}
