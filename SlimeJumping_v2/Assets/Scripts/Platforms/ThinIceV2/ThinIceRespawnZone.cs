using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// Trial group of ThinIcePlatformV2 that all regenerate when the slime exits this trigger.
    /// Shape = any Collider2D on this GO; members = children collected on Awake + <see cref="explicitMembers"/>.
    [RequireComponent(typeof(Rigidbody2D))]
    public class ThinIceRespawnZone : MonoBehaviour
    {
        [Tooltip("Auto-collect ThinIcePlatformV2 components from this GO's children at Awake.")]
        [SerializeField] bool autoCollectChildren = true;

        [Tooltip("Extra members outside this GO's hierarchy (or instead of auto-collect).")]
        [SerializeField] ThinIcePlatformV2[] explicitMembers;

        [Header("Gizmo")]
        [SerializeField] bool drawMemberLines = true;
        [SerializeField] Color gizmoColor = new Color(0.4f, 0.85f, 1f, 0.65f);

        Collider2D shape;
        Rigidbody2D   rb;
        readonly List<ThinIcePlatformV2> members = new List<ThinIcePlatformV2>();
        int slimeOverlapCount;

        /// Authored trigger shape. Test against this collider, never its bounds.
        public Collider2D Shape
        {
            get
            {
                if (shape == null) shape = GetComponent<Collider2D>();
                return shape;
            }
        }

        void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c == null) c = gameObject.AddComponent<BoxCollider2D>();
            var r = GetComponent<Rigidbody2D>();
            c.isTrigger = true;
            if (r != null)
            {
                r.bodyType = RigidbodyType2D.Kinematic;
                r.gravityScale = 0f;
                r.useFullKinematicContacts = true;
            }
        }

        void Awake()
        {
            shape = GetComponent<Collider2D>();
            rb  = GetComponent<Rigidbody2D>();
            if (shape == null)
            {
                Debug.LogError("[ThinIceRespawnZone] No Collider2D on this GameObject — zone disabled.", this);
                enabled = false;
                return;
            }
            shape.isTrigger = true;
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

        public IReadOnlyList<ThinIcePlatformV2> Members => members;

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount++;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount = Mathf.Max(0, slimeOverlapCount - 1);
            if (slimeOverlapCount == 0) RegenAllMembers();
        }

        void RegenAllMembers()
        {
            for (int i = 0; i < members.Count; i++)
                if (members[i] != null) members[i].RequestRegen();
        }

        void OnDrawGizmos()
        {
            var c = GetComponent<Collider2D>();
            if (c == null) return;
            Gizmos.color = gizmoColor;

            var box  = c as BoxCollider2D;
            var poly = c as PolygonCollider2D;
            if (box != null || poly != null)
            {
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                if (box != null) Gizmos.DrawWireCube(box.offset, box.size);
                else DrawPolygonPaths(poly);
                Gizmos.matrix = prev;
            }
            else
            {
                // Other shapes: AABB hint only; runtime checks still use the real collider.
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
            }

            if (!drawMemberLines) return;
            // members is empty in edit mode (Awake hasn't run), so scan for the gizmo.
            IEnumerable<ThinIcePlatformV2> drawList = members.Count > 0
                ? (IEnumerable<ThinIcePlatformV2>)members
                : GetComponentsInChildren<ThinIcePlatformV2>(true);
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.35f);
            foreach (var m in drawList)
            {
                if (m == null) continue;
                Gizmos.DrawLine(transform.position, m.transform.position);
            }
        }

        static void DrawPolygonPaths(PolygonCollider2D poly)
        {
            for (int path = 0; path < poly.pathCount; path++)
            {
                var pts = poly.GetPath(path);
                for (int i = 0; i < pts.Length; i++)
                    Gizmos.DrawLine(poly.offset + pts[i], poly.offset + pts[(i + 1) % pts.Length]);
            }
        }
    }
}
