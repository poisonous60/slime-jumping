#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using g3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// Editor-only tool: generates a PolygonCollider2D matching the Z=0 cross-section of a 3D mesh.
    /// Uses geometry3Sharp's MeshPlaneCut for robust slicing (half-edge data structure; handles
    /// degenerate triangles, vertices on the plane, and topology junctions).
    /// </summary>
    public static class MeshToCollider2DGenerator
    {
        [MenuItem("SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)", false, 200)]
        public static void Generate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("No Mesh",
                    "Selected object has no MeshFilter or sharedMesh.", "OK");
                return;
            }

            var mesh = mf.sharedMesh;
            var bounds = mesh.bounds;
            if (bounds.min.z > 0f || bounds.max.z < 0f)
            {
                EditorUtility.DisplayDialog("No Intersection",
                    $"Mesh does not intersect Z=0 in local space.\n" +
                    $"Mesh Z range: [{bounds.min.z:F3}, {bounds.max.z:F3}]", "OK");
                return;
            }

            // Unity forbids mixing 3D colliders with 2D colliders on the same GameObject.
            // Just gather consent here — actual destruction happens after the cut succeeds,
            // so a failed cut doesn't leave the object with no collider at all.
            var existing3D = go.GetComponents<Collider>();
            if (existing3D.Length > 0)
            {
                string names = string.Join(", ", System.Array.ConvertAll(existing3D, c => c.GetType().Name));
                bool ok = EditorUtility.DisplayDialog(
                    "Remove 3D collider(s)?",
                    $"'{go.name}' has 3D collider(s): {names}. Unity does not allow 2D and 3D colliders on the same GameObject. Remove them and continue?",
                    "Remove and continue", "Cancel");
                if (!ok) return;
            }

            // ── Convert Unity Mesh → DMesh3 ───────────────────────────────
            var dmesh = ToDMesh3(mesh);

            // ── Cut at Z=0 plane ──────────────────────────────────────────
            var cut = new MeshPlaneCut(dmesh, Vector3d.Zero, Vector3d.AxisZ)
            {
                CollapseDegenerateEdgesOnCut = true
            };
            bool cutOk = cut.Cut();

            if (!cutOk || cut.CutLoopsFailed)
                Debug.LogWarning("[MeshToCollider2D] MeshPlaneCut failed to extract cut loops. Result may be incomplete.");
            if (cut.FoundOpenSpans)
                Debug.LogWarning("[MeshToCollider2D] Mesh is not watertight at Z=0 — found open spans. Check mesh in Blender (Select → All by Trait → Non Manifold Edges).");

            if (cut.CutLoops == null || cut.CutLoops.Count == 0)
            {
                Debug.LogWarning("[MeshToCollider2D] No cross-section loops found at Z=0.");
                return;
            }

            // ── Extract loops as Vector2 polygons ─────────────────────────
            var loops = new List<List<Vector2>>(cut.CutLoops.Count);
            foreach (var edgeLoop in cut.CutLoops)
            {
                int n = edgeLoop.Vertices.Length;
                if (n < 3) continue;
                var poly = new List<Vector2>(n);
                for (int i = 0; i < n; i++)
                {
                    Vector3d v = edgeLoop.GetVertex(i);
                    poly.Add(new Vector2((float)v.x, (float)v.y));
                }
                loops.Add(poly);
            }

            if (loops.Count == 0)
            {
                Debug.LogWarning("[MeshToCollider2D] All cut loops were degenerate (< 3 vertices).");
                return;
            }

            // ── Sort by |signed area| descending: largest = outer ─────────
            loops.Sort((x, y) => Mathf.Abs(SignedArea(y)).CompareTo(Mathf.Abs(SignedArea(x))));

            // ── Winding normalization: outer CCW, holes CW ────────────────
            for (int i = 0; i < loops.Count; i++)
            {
                bool isOuter = (i == 0);
                float sa = SignedArea(loops[i]);
                if (isOuter && sa < 0f) loops[i].Reverse();
                if (!isOuter && sa > 0f) loops[i].Reverse();
            }

            if (loops.Count > 1)
                Debug.LogWarning(
                    "[MeshToCollider2D] Multiple loops detected. Non-largest loops are treated as holes. " +
                    "Disconnected outer contours would be misclassified.");

            // ── Commit: destroy 3D colliders, add PolygonCollider2D ───────
            // Grouped as a single undo operation now that the cut succeeded.
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generate PolygonCollider2D from Mesh");
            int undoGroup = Undo.GetCurrentGroup();

            foreach (var c3d in existing3D)
                Undo.DestroyObjectImmediate(c3d);

            var col = go.GetComponent<PolygonCollider2D>();
            if (col == null)
                col = Undo.AddComponent<PolygonCollider2D>(go);
            else
                Undo.RecordObject(col, "Generate PolygonCollider2D from Mesh");

            col.pathCount = loops.Count;
            for (int i = 0; i < loops.Count; i++)
                col.SetPath(i, loops[i].ToArray());

            EditorUtility.SetDirty(col);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Undo.CollapseUndoOperations(undoGroup);

            // ── Console summary ───────────────────────────────────────────
            var log = new StringBuilder();
            log.Append($"[MeshToCollider2D] {loops.Count} loop(s): ");
            for (int i = 0; i < loops.Count; i++)
            {
                if (i > 0) log.Append(", ");
                log.Append(i == 0 ? "outer" : $"hole{i}");
                log.Append($": {loops[i].Count} verts");
            }
            Debug.Log(log.ToString());
        }

        [MenuItem("SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)", true)]
        private static bool GenerateValidate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return false;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var b = mf.sharedMesh.bounds;
            return b.min.z <= 0f && b.max.z >= 0f;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        // Welds spatially-coincident vertices so the mesh is topologically watertight.
        // Unity primitives (and FBX exports) split vertices per-face for normals/UVs,
        // which leaves geometry3Sharp seeing disconnected quads and reporting open spans.
        private static DMesh3 ToDMesh3(UnityEngine.Mesh src)
        {
            const float quant = 1e5f; // 5-decimal precision for position welding

            var dm      = new DMesh3();
            var verts   = src.vertices;
            var remap   = new int[verts.Length];
            var posMap  = new Dictionary<(int, int, int), int>(verts.Length);

            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var key = (
                    Mathf.RoundToInt(v.x * quant),
                    Mathf.RoundToInt(v.y * quant),
                    Mathf.RoundToInt(v.z * quant));

                if (posMap.TryGetValue(key, out int existing))
                {
                    remap[i] = existing;
                }
                else
                {
                    int newId = dm.AppendVertex(new Vector3d(v.x, v.y, v.z));
                    posMap[key] = newId;
                    remap[i] = newId;
                }
            }

            var tris = src.triangles;
            int degenerateSkipped = 0;
            int appendRejected = 0;
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = remap[tris[i]];
                int b = remap[tris[i + 1]];
                int c = remap[tris[i + 2]];
                if (a == b || b == c || a == c) { degenerateSkipped++; continue; }
                int tid = dm.AppendTriangle(a, b, c);
                if (tid < 0) appendRejected++; // non-manifold edge, duplicate tri, etc.
            }

            if (degenerateSkipped > 0 || appendRejected > 0)
                Debug.LogWarning(
                    $"[MeshToCollider2D] Welded {verts.Length} → {dm.VertexCount} verts; " +
                    $"skipped {degenerateSkipped} degenerate tri(s), " +
                    $"{appendRejected} non-manifold tri(s) rejected by DMesh3. " +
                    "Non-zero rejects usually mean the mesh has non-manifold edges.");

            return dm;
        }

        private static float SignedArea(List<Vector2> poly)
        {
            float area = 0f;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }
    }
}
#endif
