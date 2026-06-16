using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// Authored trigger box that commands a group of <see cref="WaypointPlatformV2"/> to return
    /// when the slime leaves the box. Direct mirror of <c>ThinIceV2.ThinIceRespawnZone</c>: the
    /// platforms still depart on their own (slime mounts the leader), but RETURN is owned by this
    /// single zone so the whole group retracts together the instant the slime is out — no per-deck
    /// proximity poll, no destination asymmetry.
    ///
    /// Membership: child WaypointPlatformV2 components are auto-collected on Awake. Use
    /// <see cref="explicitMembers"/> to add platforms outside this GO's hierarchy (e.g. a sandwich
    /// partner sitting elsewhere in the scene).
    ///
    /// Layer policy: stays on the same layer as the rest of the scene; isTrigger guarantees no
    /// physical collision. OnTriggerEnter/Exit filter to the slime via GetComponentInParent.
    [RequireComponent(typeof(BoxCollider2D))]
    [RequireComponent(typeof(Rigidbody2D))]
    public class WaypointReturnZone : MonoBehaviour
    {
        [Tooltip("Auto-collect WaypointPlatformV2 components from this GO's children at Awake.")]
        [SerializeField] bool autoCollectChildren = true;

        [Tooltip("Extra members outside this GO's hierarchy (or instead of auto-collect). " +
                 "For a sandwich, add BOTH the leader and the partner.")]
        [SerializeField] WaypointPlatformV2[] explicitMembers;

        [Header("Gizmo")]
        [SerializeField] bool drawMemberLines = true;
        [SerializeField] Color gizmoColor = new Color(1f, 0.6f, 0.2f, 0.65f);

        BoxCollider2D box;
        Rigidbody2D   rb;
        readonly List<WaypointPlatformV2> members = new List<WaypointPlatformV2>();
        int slimeOverlapCount;

        void Reset()
        {
            var b = GetComponent<BoxCollider2D>();
            var r = GetComponent<Rigidbody2D>();
            if (b != null) b.isTrigger = true;
            if (r != null)
            {
                r.bodyType = RigidbodyType2D.Kinematic;
                r.gravityScale = 0f;
                r.useFullKinematicContacts = true;
            }
        }

        void Awake()
        {
            box = GetComponent<BoxCollider2D>();
            rb  = GetComponent<Rigidbody2D>();
            box.isTrigger = true;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            // Ensure trigger events fire even against kinematic-bodied colliders (defensive).
            rb.useFullKinematicContacts = true;

            CollectMembers();
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null) members[i].RegisterParentZone(this);
        }

        void CollectMembers()
        {
            members.Clear();
            if (autoCollectChildren)
            {
                GetComponentsInChildren(true, members);
            }
            if (explicitMembers != null)
            {
                for (int i = 0; i < explicitMembers.Length; i++)
                {
                    var m = explicitMembers[i];
                    if (m != null && !members.Contains(m)) members.Add(m);
                }
            }
        }

        public IReadOnlyList<WaypointPlatformV2> Members => members;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount++;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount = Mathf.Max(0, slimeOverlapCount - 1);
            if (slimeOverlapCount == 0) ReturnAllMembers();
        }

        void ReturnAllMembers()
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null) members[i].RequestReturn();
        }

        // --- Gizmos --------------------------------------------------------------

        void OnDrawGizmos()
        {
            var b = GetComponent<BoxCollider2D>();
            if (b == null) return;
            Gizmos.color = gizmoColor;
            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(b.offset, b.size);
            Gizmos.matrix = prev;

            if (!drawMemberLines) return;
            // Cached members may be empty in edit mode (Awake hasn't run); fall back to a
            // shallow scan so the gizmo is useful before Play.
            IEnumerable<WaypointPlatformV2> drawList = members.Count > 0
                ? (IEnumerable<WaypointPlatformV2>)members
                : GetComponentsInChildren<WaypointPlatformV2>(true);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.35f);
            foreach (var m in drawList)
            {
                if (m == null) continue;
                Gizmos.DrawLine(transform.position, m.transform.position);
            }
        }
    }
}
