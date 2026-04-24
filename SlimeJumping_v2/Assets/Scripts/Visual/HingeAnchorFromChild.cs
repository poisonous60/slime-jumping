using UnityEngine;

namespace SlimeJumping.Visual
{
    // Attach next to a HingeJoint2D. Drag a child Transform placed at the desired pivot point
    // (e.g. the apple's stem). The hinge's Anchor is re-synced to that Transform's position
    // in this GameObject's local space whenever the inspector changes, so the body swings
    // around the marked point instead of its origin.
    [RequireComponent(typeof(HingeJoint2D))]
    [ExecuteAlways]
    public class HingeAnchorFromChild : MonoBehaviour
    {
        [SerializeField] Transform pivot;

        void OnValidate()
        {
            SyncAnchor();
        }

        void Update()
        {
            if (!Application.isPlaying) SyncAnchor();
        }

        void SyncAnchor()
        {
            if (pivot == null) return;
            var hj = GetComponent<HingeJoint2D>();
            if (hj == null) return;
            hj.anchor = transform.InverseTransformPoint(pivot.position);
        }
    }
}
