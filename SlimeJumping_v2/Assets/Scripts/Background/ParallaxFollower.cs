using UnityEngine;

namespace SlimeJumping.Background
{
    // Moves this transform as the target moves, scaled by `factor` per axis.
    //   factor = 0  → locked to target (stuck on screen, no parallax illusion)
    //   factor = 1  → world-fixed (no follow at all)
    //   factor = 0.4 → moves at 40% of target speed (looks "far away")
    //
    // Z is intentionally ignored by default: in a 2.5D scene we want
    // background depth to stay constant, not slide away as the camera dollies.
    //
    // Edit-mode is intentionally a no-op (no [ExecuteAlways]) so the scene-view
    // user can drag this transform freely; the runtime captures whatever
    // position the user left it at as the parallax anchor.
    public class ParallaxFollower : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3   factor = new Vector3(0.4f, 0.4f, 0f);

        Vector3 anchorSelf;
        Vector3 anchorTarget;

        void Start()
        {
            if (target == null) return;
            anchorSelf = transform.position;
            anchorTarget = target.position;
        }

        void LateUpdate()
        {
            if (target == null) return;
            Vector3 delta = target.position - anchorTarget;
            transform.position = anchorSelf + Vector3.Scale(delta, factor);
        }
    }
}
