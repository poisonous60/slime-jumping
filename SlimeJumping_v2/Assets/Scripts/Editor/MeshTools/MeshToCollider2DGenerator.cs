#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using g3;
using SlimeJumping.MeshTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// Editor-only tool: generates a PolygonCollider2D matching the WORLD Z=0 cross-section
    /// of a 3D mesh, regardless of how the mesh GameObject is rotated.
    ///
    /// To keep the 2D collider free of 3D-rotation distortion, the collider is attached to
    /// an identity-rotated wrapper parent (`{mesh.name}_Collider2D`) inserted above the mesh.
    /// The mesh moves/rotates with the wrapper via the hierarchy, but the collider's own
    /// transform has no X/Y rotation, so Box2D sees an un-distorted shape.
    /// </summary>
    public static class MeshToCollider2DGenerator
    {
        [MenuItem("SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)", false, 200)]
        public static void GenerateMenu() { Generate(); }

        /// <summary>
        /// Cuts the selected mesh at the world Z=0 plane and writes the cross-section
        /// to a PolygonCollider2D on an identity-rotated wrapper parent.
        /// Returns the collider, or null if the operation did not complete.
        /// </summary>
        public static PolygonCollider2D Generate()
        {
            var meshGO = Selection.activeGameObject;
            if (meshGO == null) return null;

            var mf = meshGO.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("No Mesh",
                    "Selected object has no MeshFilter or sharedMesh.", "OK");
                return null;
            }

            var mesh = mf.sharedMesh;
            var meshTr = meshGO.transform;

            var worldBounds = TransformBounds(meshTr, mesh.bounds);
            if (worldBounds.min.z > 0f || worldBounds.max.z < 0f)
            {
                EditorUtility.DisplayDialog("No Intersection",
                    $"Mesh does not intersect world Z=0.\n" +
                    $"World Z range: [{worldBounds.min.z:F3}, {worldBounds.max.z:F3}]\n\n" +
                    "Move the GameObject in Z until the slice plane crosses the mesh.", "OK");
                return null;
            }

            // Bake vertices into world space before cutting so the slice is taken in the
            // world XY plane regardless of the mesh object's local rotation/scale.
            var dmesh = ToDMesh3(mesh, meshTr.localToWorldMatrix);

            var cut = new MeshPlaneCut(dmesh,
                new Vector3d(0, 0, 0),
                new Vector3d(0, 0, 1))
            {
                CollapseDegenerateEdgesOnCut = true
            };
            bool cutOk = cut.Cut();

            if (!cutOk || cut.CutLoopsFailed)
                Debug.LogWarning("[MeshToCollider2D] MeshPlaneCut failed to extract cut loops. Result may be incomplete.");
            if (cut.FoundOpenSpans)
                Debug.LogWarning("[MeshToCollider2D] Mesh is not watertight at world Z=0 — found open spans. Check mesh in Blender (Select → All by Trait → Non Manifold Edges).");

            if (cut.CutLoops == null || cut.CutLoops.Count == 0)
            {
                Debug.LogWarning("[MeshToCollider2D] No cross-section loops found at world Z=0.");
                return null;
            }

            // Loop vertices are in world space (we baked before cutting) → take (x,y) as world XY.
            var worldLoops = new List<List<Vector2>>(cut.CutLoops.Count);
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
                worldLoops.Add(poly);
            }

            if (worldLoops.Count == 0)
            {
                Debug.LogWarning("[MeshToCollider2D] All cut loops were degenerate (< 3 vertices).");
                return null;
            }

            // Classify each loop by nesting depth (point-in-polygon): even depth = outer (CCW),
            // odd depth = hole (CW). This correctly handles disjoint outer contours AND true holes.
            // Sorting by |area| first is a stable presentation choice only.
            worldLoops.Sort((x, y) => Mathf.Abs(SignedArea(y)).CompareTo(Mathf.Abs(SignedArea(x))));

            bool[] isHole = new bool[worldLoops.Count];
            int outerCount = 0, holeCount = 0;
            for (int i = 0; i < worldLoops.Count; i++)
            {
                Vector2 probe = worldLoops[i][0];
                int depth = 0;
                for (int k = 0; k < worldLoops.Count; k++)
                {
                    if (k == i) continue;
                    if (IsPointInside(probe, worldLoops[k])) depth++;
                }
                bool hole = (depth % 2 != 0);
                isHole[i] = hole;
                if (hole) holeCount++; else outerCount++;

                float sa = SignedArea(worldLoops[i]);
                if (!hole && sa < 0f) worldLoops[i].Reverse();
                if (hole && sa > 0f) worldLoops[i].Reverse();
            }

            if (worldLoops.Count > 1)
                Debug.Log($"[MeshToCollider2D] {worldLoops.Count} loops classified by nesting: {outerCount} outer, {holeCount} hole(s).");

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Generate PolygonCollider2D from Mesh");
            int undoGroup = Undo.GetCurrentGroup();

            Transform wrapper = FindOrCreateWrapper(meshGO);

            var col = wrapper.GetComponent<PolygonCollider2D>();
            if (col == null)
                col = Undo.AddComponent<PolygonCollider2D>(wrapper.gameObject);
            else
                Undo.RecordObject(col, "Generate PolygonCollider2D from Mesh");

            // Convert world XY → wrapper-local via InverseTransformPoint so inherited
            // lossyScale / rotation from the wrapper's parent chain is cancelled correctly.
            // (Setting localScale=1 on the wrapper is not enough when its parent has scale.)
            col.pathCount = worldLoops.Count;
            for (int i = 0; i < worldLoops.Count; i++)
            {
                var p = worldLoops[i];
                var arr = new Vector2[p.Count];
                for (int j = 0; j < p.Count; j++)
                {
                    Vector3 local = wrapper.InverseTransformPoint(new Vector3(p[j].x, p[j].y, 0f));
                    arr[j] = new Vector2(local.x, local.y);
                }
                col.SetPath(i, arr);
            }

            Undo.RecordObject(wrapper.gameObject, "Set Environment layer");
            wrapper.gameObject.layer = 7;

            EditorUtility.SetDirty(col);
            EditorUtility.SetDirty(wrapper.gameObject);
            EditorSceneManager.MarkSceneDirty(wrapper.gameObject.scene);
            Undo.CollapseUndoOperations(undoGroup);

            Selection.activeGameObject = wrapper.gameObject;

            var log = new StringBuilder();
            log.Append($"[MeshToCollider2D] '{wrapper.name}' — {worldLoops.Count} loop(s): ");
            for (int i = 0; i < worldLoops.Count; i++)
            {
                if (i > 0) log.Append(", ");
                log.Append(isHole[i] ? "hole" : "outer");
                log.Append($": {worldLoops[i].Count} verts");
            }
            Debug.Log(log.ToString());

            return col;
        }

        [MenuItem("SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)", true)]
        private static bool GenerateValidate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return false;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            var wb = TransformBounds(go.transform, mf.sharedMesh.bounds);
            return wb.min.z <= 0f && wb.max.z >= 0f;
        }

        // Inserts an identity-rotated parent above meshGO, or reuses one that already exists
        // from a previous run so the tool is idempotent.
        private static Transform FindOrCreateWrapper(GameObject meshGO)
        {
            var meshTr = meshGO.transform;
            string wrapperName = meshGO.name + "_Collider2D";

            var currentParent = meshTr.parent;
            if (currentParent != null
                && currentParent.name == wrapperName
                && currentParent.GetComponent<PolygonCollider2D>() != null
                && Quaternion.Angle(currentParent.rotation, Quaternion.identity) < 0.01f)
            {
                EnsureSelectionBaseMarker(currentParent.gameObject);
                return currentParent;
            }

            var wrapperGO = new GameObject(wrapperName);
            Undo.RegisterCreatedObjectUndo(wrapperGO, "Create Collider2D wrapper");
            var wrapperTr = wrapperGO.transform;

            if (currentParent != null)
                Undo.SetTransformParent(wrapperTr, currentParent, "Parent wrapper");

            Undo.RecordObject(wrapperTr, "Position wrapper");
            wrapperTr.position = meshTr.position;
            wrapperTr.rotation = Quaternion.identity;
            wrapperTr.localScale = Vector3.one;

            Undo.SetTransformParent(meshTr, wrapperTr, "Reparent mesh into wrapper");

            EnsureSelectionBaseMarker(wrapperGO);

            return wrapperTr;
        }

        // Adds the SelectionBase marker so Scene-view clicks on the child mesh select the
        // wrapper instead of the mesh. Idempotent: safe on existing wrappers from prior runs.
        private static void EnsureSelectionBaseMarker(GameObject wrapperGO)
        {
            if (wrapperGO.GetComponent<MeshCollider2DWrapper>() == null)
                Undo.AddComponent<MeshCollider2DWrapper>(wrapperGO);
        }

        private static Bounds TransformBounds(Transform t, Bounds local)
        {
            var m = t.localToWorldMatrix;
            Vector3 center = m.MultiplyPoint3x4(local.center);
            Vector3 ex = local.extents;
            Vector3 wx = m.MultiplyVector(new Vector3(ex.x, 0f, 0f));
            Vector3 wy = m.MultiplyVector(new Vector3(0f, ex.y, 0f));
            Vector3 wz = m.MultiplyVector(new Vector3(0f, 0f, ex.z));
            Vector3 worldExtents = new Vector3(
                Mathf.Abs(wx.x) + Mathf.Abs(wy.x) + Mathf.Abs(wz.x),
                Mathf.Abs(wx.y) + Mathf.Abs(wy.y) + Mathf.Abs(wz.y),
                Mathf.Abs(wx.z) + Mathf.Abs(wy.z) + Mathf.Abs(wz.z));
            return new Bounds(center, worldExtents * 2f);
        }

        // Welds spatially-coincident vertices (in world space) so the mesh is topologically
        // watertight. Unity primitives and FBX exports split vertices per-face for normals/UVs,
        // which leaves geometry3Sharp seeing disconnected quads and reporting open spans.
        private static DMesh3 ToDMesh3(UnityEngine.Mesh src, Matrix4x4 toWorld)
        {
            const float quant = 1e5f;

            var dm = new DMesh3();
            var verts = src.vertices;
            var remap = new int[verts.Length];
            var posMap = new Dictionary<(int, int, int), int>(verts.Length);

            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(verts[i]);
                var key = (
                    Mathf.RoundToInt(w.x * quant),
                    Mathf.RoundToInt(w.y * quant),
                    Mathf.RoundToInt(w.z * quant));

                if (posMap.TryGetValue(key, out int existing))
                {
                    remap[i] = existing;
                }
                else
                {
                    int newId = dm.AppendVertex(new Vector3d(w.x, w.y, w.z));
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
                if (tid < 0) appendRejected++;
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

        // Standard ray-casting point-in-polygon (counts edge crossings on a +x ray from `point`).
        private static bool IsPointInside(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                Vector2 pi = polygon[i], pj = polygon[j];
                if (((pi.y > point.y) != (pj.y > point.y))
                    && (point.x < (pj.x - pi.x) * (point.y - pi.y) / (pj.y - pi.y) + pi.x))
                    inside = !inside;
            }
            return inside;
        }
    }
}
#endif
