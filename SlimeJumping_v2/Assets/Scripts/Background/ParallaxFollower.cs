using UnityEngine;

namespace SlimeJumping.Background
{
    // Follows target scaled by `factor` per axis: 0 = world-fixed, 1 = locked to target.
    // Z factor stays 0 so background depth doesn't slide as the camera dollies.
    // No [ExecuteAlways] on purpose — the scene-view position the user leaves is the anchor.
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
