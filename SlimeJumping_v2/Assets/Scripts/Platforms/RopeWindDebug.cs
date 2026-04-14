using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeJumping.Platforms
{
    // Stage-1 / stage-2d helper: inject impulses into a Rope's middle nodes
    // to inspect recovery behavior and tunneling risk.
    [RequireComponent(typeof(Rope))]
    public class RopeWindDebug : MonoBehaviour
    {
        [SerializeField] Rope rope;

        [Header("Impulse")]
        [SerializeField] float impulseMagnitude = 0.5f;
        [Tooltip("0 = only middle node, 1 = all free nodes get equal impulse.")]
        [Range(0f, 1f)] float spread = 0.6f;

        void Reset() { rope = GetComponent<Rope>(); }

        void Update()
        {
            if (rope == null) rope = GetComponent<Rope>();
            if (rope == null || rope.NodeCount == 0) return;

            var kb = Keyboard.current;
            if (kb == null) return;

            Vector2 impulse = Vector2.zero;
            if (kb.leftArrowKey.isPressed)  impulse += Vector2.left  * impulseMagnitude;
            if (kb.rightArrowKey.isPressed) impulse += Vector2.right * impulseMagnitude;
            if (kb.upArrowKey.isPressed)    impulse += Vector2.up    * impulseMagnitude;
            if (kb.downArrowKey.isPressed)  impulse += Vector2.down  * impulseMagnitude;

            if (impulse.sqrMagnitude < 1e-6f) return;

            int n = rope.NodeCount;
            int mid = n / 2;
            int halfSpread = Mathf.RoundToInt((n * 0.5f - 1f) * spread);
            int from = Mathf.Max(1, mid - halfSpread);
            int to = Mathf.Min(n - 2, mid + halfSpread);

            rope.ApplyWorldImpulseToRange(impulse * Time.deltaTime, from, to);
        }
    }
}
