using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// Diagnose why SnowField's overlap mask is dropping snow tris in regions that
    /// don't appear to overlap anything visually. Mirrors SnowField.RebuildNow's
    /// extract → subdivide → MaskOutOverlap pipeline 1:1, but instead of building
    /// the snow mesh it records per-tri:
    ///   - whether the tri was dropped by the mask
    ///   - which Collider2D was hit, at which of the 4 sample points
    /// and aggregates how many tris each masker is responsible for. Log appears in
    /// Console; OnDrawGizmosSelected draws kept (green) / dropped (red) tris.
    [ExecuteAlways]
    [RequireComponent(typeof(SnowField))]
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Mask Debug")]
    public sealed class SnowMaskDebug : MonoBehaviour
    {
        [Header("Output")]
        [Tooltip("Print per-collider hit counts and sample dropped-tri positions to the Console.")]
        public bool logToConsole = true;

        [Tooltip("Per-collider, log up to this many example dropped tris (centroid + sample index).")]
        [Range(0, 20)] public int sampleDroppedTrisPerCollider = 3;

        [Header("Gizmo")]
        [Tooltip("Draw kept / dropped tris in scene view while this component is selected.")]
        public bool drawGizmos = true;

        [Tooltip("Yellow dot at the offending sample point of each dropped tri.")]
        public bool drawHitMarkers = true;

        [Range(0f, 1f)] public float keptAlpha = 0.18f;
        [Range(0f, 1f)] public float droppedAlpha = 0.7f;

        struct TriRec
        {
            public Vector3 v0, v1, v2;
            public bool dropped;
            public Collider2D hitCollider;
            public int hitSampleIndex; // 0=v0, 1=v1, 2=v2, 3=centroid
            public Vector2 hitWorldXY;
        }

        [System.NonSerialized] List<TriRec> lastTris;

        [ContextMenu("Diagnose Mask")]
        public void Diagnose()
        {
            var field = GetComponent<SnowField>();
            if (field == null) { Debug.LogWarning("[SnowMaskDebug] No SnowField on this GO."); return; }
            if (field.sourceMeshFilter == null || field.sourceMeshFilter.sharedMesh == null)
            {
                Debug.LogWarning("[SnowMaskDebug] SnowField has no source mesh.");
                return;
            }

            var mesh = field.sourceMeshFilter.sharedMesh;
            var meshT = field.sourceMeshFilter.transform;
            var patches = SnowTopSurfaceExtractor.Extract(mesh, field.MaxSnowSlopeDeg);

            var filter = ContactFilter2D.noFilter;
            filter.SetLayerMask(field.overlapLayerMask);
            filter.useTriggers = false;

            var hits = new Collider2D[16];
            int myId = field.hostCollider != null
                ? field.hostCollider.GetInstanceID()
                : field.GetInstanceID();

            var tris = new List<TriRec>();
            var byCollider = new Dictionary<Collider2D, int>();

            for (int s = 0; s < patches.Count; s++)
            {
                var patch = patches[s];
                if (patch.verts == null || patch.verts.Length < 3) continue;
                int triCount0 = patch.tris != null ? patch.tris.Length / 3 : 0;
                if (triCount0 < field.MinPatchTris) continue;

                int subdiv = field.SubdivLevels;
                if (subdiv > 0)
                    patch = SnowPatchSubdivider.Subdivide(patch, subdiv);

                int triCount = patch.tris.Length / 3;
                for (int t = 0; t < triCount; t++)
                {
                    int ia = patch.tris[t * 3];
                    int ib = patch.tris[t * 3 + 1];
                    int ic = patch.tris[t * 3 + 2];
                    Vector3 wa = meshT.TransformPoint(patch.verts[ia]);
                    Vector3 wb = meshT.TransformPoint(patch.verts[ib]);
                    Vector3 wc = meshT.TransformPoint(patch.verts[ic]);
                    Vector2 p0 = new Vector2(wa.x, wa.y);
                    Vector2 p1 = new Vector2(wb.x, wb.y);
                    Vector2 p2 = new Vector2(wc.x, wc.y);
                    Vector2 pc = new Vector2(
                        (wa.x + wb.x + wc.x) / 3f,
                        (wa.y + wb.y + wc.y) / 3f);

                    var rec = new TriRec { v0 = wa, v1 = wb, v2 = wc, dropped = false };

                    for (int sIdx = 0; sIdx < 4; sIdx++)
                    {
                        Vector2 sp = sIdx == 0 ? p0 : sIdx == 1 ? p1 : sIdx == 2 ? p2 : pc;
                        Collider2D hit = FirstMaskerAt(sp, field, filter, hits, myId);
                        if (hit != null)
                        {
                            rec.dropped = true;
                            rec.hitCollider = hit;
                            rec.hitSampleIndex = sIdx;
                            rec.hitWorldXY = sp;
                            if (byCollider.TryGetValue(hit, out int c)) byCollider[hit] = c + 1;
                            else byCollider[hit] = 1;
                            break;
                        }
                    }

                    tris.Add(rec);
                }
            }

            lastTris = tris;

            if (logToConsole)
            {
                int droppedCount = 0;
                for (int i = 0; i < tris.Count; i++) if (tris[i].dropped) droppedCount++;
                int keptCount = tris.Count - droppedCount;

                int hostId = field.hostCollider != null ? field.hostCollider.GetInstanceID() : 0;
                string hostName = field.hostCollider != null ? field.hostCollider.gameObject.name : "<null>";
                Debug.Log($"[SnowMaskDebug] '{field.name}' host='{hostName}' hostId={hostId}, " +
                          $"layerMask=0x{(int)field.overlapLayerMask:X8}, priority={field.overlapPriority}\n" +
                          $"  tris kept={keptCount}, dropped={droppedCount}, total={tris.Count}", field);

                if (byCollider.Count == 0)
                {
                    Debug.Log("  (no maskers — nothing was dropped, mask is innocent)", field);
                }
                else
                {
                    Debug.Log($"  Maskers (sample-hit count, descending):", field);
                    foreach (var kv in byCollider.OrderByDescending(k => k.Value))
                    {
                        var c = kv.Key;
                        string nm = "<null>";
                        int lyr = -1;
                        string typeName = "<null>";
                        int iid = 0;
                        GameObject ctx = null;
                        bool isOther = false;
                        if (c != null)
                        {
                            nm = c.gameObject.name;
                            lyr = c.gameObject.layer;
                            typeName = c.GetType().Name;
                            iid = c.GetInstanceID();
                            ctx = c.gameObject;
                            isOther = SnowFieldRegistry.TryGet(c, out _);
                        }
                        Debug.Log($"    '{nm}' (layer {lyr}, {typeName}, id {iid}{(isOther ? ", SnowField host" : "")}): {kv.Value} hits", ctx);
                    }

                    if (sampleDroppedTrisPerCollider > 0)
                    {
                        var perColliderShown = new Dictionary<Collider2D, int>();
                        for (int i = 0; i < tris.Count; i++)
                        {
                            var d = tris[i];
                            if (!d.dropped) continue;
                            int shown = perColliderShown.TryGetValue(d.hitCollider, out var n) ? n : 0;
                            if (shown >= sampleDroppedTrisPerCollider) continue;
                            perColliderShown[d.hitCollider] = shown + 1;

                            Vector2 cen = new Vector2(
                                (d.v0.x + d.v1.x + d.v2.x) / 3f,
                                (d.v0.y + d.v1.y + d.v2.y) / 3f);
                            string sampleName = d.hitSampleIndex == 3 ? "centroid" : $"v{d.hitSampleIndex}";
                            string maskerName = d.hitCollider != null ? d.hitCollider.gameObject.name : "<null>";
                            Debug.Log($"      example: tri@({cen.x:F2},{cen.y:F2}) dropped — '{maskerName}' " +
                                      $"hit at {sampleName}=({d.hitWorldXY.x:F2},{d.hitWorldXY.y:F2})",
                                      d.hitCollider != null ? d.hitCollider.gameObject : null);
                        }
                    }
                }
            }

#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        [ContextMenu("Clear Gizmos")]
        public void ClearGizmos()
        {
            lastTris = null;
#if UNITY_EDITOR
            UnityEditor.SceneView.RepaintAll();
#endif
        }

        // Mirrors SnowField.SamplePointHitsOther exactly. Returns the first masker
        // (or null if none). Uses the same priority arbitration / InstanceID tiebreak.
        static Collider2D FirstMaskerAt(Vector2 worldXY, SnowField field,
            ContactFilter2D filter, Collider2D[] hits, int myId)
        {
            int n = Physics2D.OverlapPoint(worldXY, filter, hits);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h == null) continue;
                if (h == field.hostCollider) continue;
                if (SnowFieldRegistry.TryGet(h, out var otherField) && otherField != null && otherField != field)
                {
                    if (otherField.overlapPriority > field.overlapPriority) return h;
                    if (otherField.overlapPriority < field.overlapPriority) continue;
                    if (h.GetInstanceID() > myId) return h;
                    continue;
                }
                return h;
            }
            return null;
        }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || lastTris == null) return;
            for (int i = 0; i < lastTris.Count; i++)
            {
                var d = lastTris[i];
                Gizmos.color = d.dropped
                    ? new Color(1f, 0.15f, 0.15f, droppedAlpha)
                    : new Color(0.2f, 1f, 0.3f, keptAlpha);
                Gizmos.DrawLine(d.v0, d.v1);
                Gizmos.DrawLine(d.v1, d.v2);
                Gizmos.DrawLine(d.v2, d.v0);
            }
            if (drawHitMarkers)
            {
                Gizmos.color = new Color(1f, 0.95f, 0.1f, 0.95f);
                for (int i = 0; i < lastTris.Count; i++)
                {
                    var d = lastTris[i];
                    if (!d.dropped) continue;
                    Gizmos.DrawSphere(new Vector3(d.hitWorldXY.x, d.hitWorldXY.y, 0f), 0.04f);
                }
            }
        }
    }
}
