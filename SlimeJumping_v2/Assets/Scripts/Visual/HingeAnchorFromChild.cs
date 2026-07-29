using UnityEngine;

namespace SlimeJumping.Visual
{
    // Re-syncs HingeJoint2D.anchor to a child Transform's position in local space, so the body
    // swings around that marked pivot instead of its origin.
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
