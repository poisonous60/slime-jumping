using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// Trial group of DeformablePlatform that all restore to rest when the slime exits this trigger.
    /// Shape = any Collider2D on this GO; members = children collected on Awake + <see cref="explicitMembers"/>.
    public class DeformableRespawnZone : MonoBehaviour
    {
        [Tooltip("Auto-collect DeformablePlatform components from this GO's children at Awake.")]
        [SerializeField] bool autoCollectChildren = true;

        [Tooltip("Extra members outside this GO's hierarchy (or instead of auto-collect).")]
        [SerializeField] DeformablePlatform[] explicitMembers;

        [SerializeField] Color gizmoColor = new Color(1f, 0.75f, 0.35f, 0.65f);

        Collider2D shape;
        readonly List<DeformablePlatform> members = new List<DeformablePlatform>();
        int slimeOverlapCount;

        public IReadOnlyList<DeformablePlatform> Members => members;

        void Reset()
        {
            var c = GetComponent<Collider2D>();
            if (c == null) c = gameObject.AddComponent<PolygonCollider2D>();
            c.isTrigger = true;
        }

        void Awake()
        {
            shape = GetComponent<Collider2D>();
            if (shape == null)
            {
                Debug.LogError("[DeformableRespawnZone] No Collider2D on this GameObject — zone disabled.", this);
                enabled = false;
                return;
            }
            shape.isTrigger = true;
            // No Rigidbody2D here on purpose: the slime's dynamic body already drives the
            // trigger callbacks, and a body on this GO would adopt child platform colliders.

            CollectMembers();
        }

        void CollectMembers()
        {
            members.Clear();
            if (autoCollectChildren) GetComponentsInChildren(true, members);
            if (explicitMembers != null)
            {
                for (int i = 0; i < explicitMembers.Length; i++)
                {
                    var m = explicitMembers[i];
                    if (m != null && !members.Contains(m)) members.Add(m);
                }
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount++;
        }

        void OnTriggerExit2D(Collider2D other)
        {
            if (other.GetComponentInParent<SlimeController>() == null) return;
            slimeOverlapCount = Mathf.Max(0, slimeOverlapCount - 1);
            if (slimeOverlapCount == 0)
            {
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null) members[i].RequestRegen();
            }
        }

        void OnDrawGizmos()
        {
            var c = GetComponent<Collider2D>();
            if (c == null) return;
            Gizmos.color = gizmoColor;

            var poly = c as PolygonCollider2D;
            var box  = c as BoxCollider2D;
            if (poly != null || box != null)
            {
                Matrix4x4 prev = Gizmos.matrix;
                Gizmos.matrix = transform.localToWorldMatrix;
                if (box != null) Gizmos.DrawWireCube(box.offset, box.size);
                else DrawPolygonPaths(poly);
                Gizmos.matrix = prev;
            }
            else
            {
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
            }

            // members is empty in edit mode (Awake hasn't run), so scan for the gizmo.
            IEnumerable<DeformablePlatform> drawList = members.Count > 0
                ? (IEnumerable<DeformablePlatform>)members
                : GetComponentsInChildren<DeformablePlatform>(true);
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
