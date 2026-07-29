using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    /// <summary>One sub-room in a RainWorldZone; its BoxCollider2D is both the contains-test surface and the CinemachineConfiner2D bounding shape.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class RainWorldSubRoom : MonoBehaviour
    {
        [Tooltip("CinemachineCamera that activates while the slime is inside this sub-room. Configure its Confiner2D to point at this object's BoxCollider2D.")]
        [SerializeField] private CinemachineCamera vcam;

        private BoxCollider2D bounds;

        public CinemachineCamera VCam => vcam;
        public BoxCollider2D Bounds
        {
            get
            {
                if (bounds == null) bounds = GetComponent<BoxCollider2D>();
                return bounds;
            }
        }

        private void Reset()
        {
            // Confiner2D reads the shape either way; a non-trigger would physically block the slime.
            var col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
        }

        private void Awake()
        {
            bounds = GetComponent<BoxCollider2D>();
            bounds.isTrigger = true;
        }

        public bool Contains(Vector2 worldPos)
        {
            return Bounds != null && Bounds.OverlapPoint(worldPos);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            var col = GetComponent<BoxCollider2D>();
            if (col == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.15f);
            Gizmos.DrawCube(col.offset, col.size);
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(col.offset, col.size);
        }
#endif
    }
}
