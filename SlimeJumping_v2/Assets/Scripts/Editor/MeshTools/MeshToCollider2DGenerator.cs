#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using SlimeJumping.MeshTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    /// <summary>Editor-only: PolygonCollider2D from a mesh's world Z=0 cross-section, placed on an identity-rotated wrapper parent so Box2D sees no 3D-rotation distortion.</summary>
    public static class MeshToCollider2DGenerator
    {
        [MenuItem("SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)", false, 200)]
        public static void GenerateMenu() { Generate(); }

        /// <summary>Cuts the selected mesh at world Z=0 into a PolygonCollider2D on an identity-rotated wrapper; null if it did not complete.</summary>
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

            // Per-triangle segment slicing in baked world space: no watertight/manifold requirement,
            // so open-shell meshes cut too; gaps where the plane crosses shell openings get bridged.
            var worldLoops = SliceMeshAtZ0(mesh, meshTr.localToWorldMatrix, out int bridgedGaps);

            if (worldLoops.Count == 0)
            {
                Debug.LogWarning("[MeshToCollider2D] No cross-section loops found at world Z=0.");
                return null;
            }

            if (bridgedGaps > 0)
                Debug.Log($"[MeshToCollider2D] Open shell at the cut plane — bridged {bridgedGaps} gap(s).");

            // Nesting depth, not area rank: even depth = outer, odd = hole, so disjoint outer contours survive.
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

            // InverseTransformPoint, not localScale=1: cancels lossyScale/rotation inherited from the parent chain.
            // RDP at 1mm chord error is geometrically a no-op but cuts Box2D broadphase cost ~80% on dense terrain.
            const float simplifyEpsilon = 0.001f;
            col.pathCount = worldLoops.Count;
            int totalRawVerts = 0, totalSimpVerts = 0;
            for (int i = 0; i < worldLoops.Count; i++)
            {
                var p = worldLoops[i];
                var arr = new Vector2[p.Count];
                for (int j = 0; j < p.Count; j++)
                {
                    Vector3 local = wrapper.InverseTransformPoint(new Vector3(p[j].x, p[j].y, 0f));
                    arr[j] = new Vector2(local.x, local.y);
                }
                totalRawVerts += arr.Length;
                arr = TerrainColliderOptimizer.SimplifyClosedPath(arr, simplifyEpsilon);
                totalSimpVerts += arr.Length;
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
            log.Append($"[MeshToCollider2D] '{wrapper.name}' — {worldLoops.Count} loop(s), verts {totalRawVerts}→{totalSimpVerts} (RDP eps={simplifyEpsilon}m): ");
            for (int i = 0; i < worldLoops.Count; i++)
            {
                if (i > 0) log.Append(", ");
                log.Append(isHole[i] ? "hole" : "outer");
                log.Append($": {worldLoops[i].Count}→{col.GetPath(i).Length} verts");
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

        // Inserts an identity-rotated parent above meshGO, or reuses an existing one (idempotent).
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

        // SelectionBase marker so Scene-view clicks on the child mesh select the wrapper.
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

        // Slices every triangle crossing world z=0 into a 2D segment, chains segments into loops,
        // and bridges gaps left where the plane crosses open-shell boundaries.
        private static List<List<Vector2>> SliceMeshAtZ0(UnityEngine.Mesh src, Matrix4x4 toWorld, out int bridgedGaps)
        {
            var verts = src.vertices;
            var world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(verts[i]);
                if (w.z == 0f) w.z = 1e-7f;
                world[i] = w;
            }

            var tris = src.triangles;
            var segs = new List<(Vector2 a, Vector2 b)>();
            var pts = new List<Vector2>(2);
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 p0 = world[tris[i]], p1 = world[tris[i + 1]], p2 = world[tris[i + 2]];
                bool s0 = p0.z > 0f, s1 = p1.z > 0f, s2 = p2.z > 0f;
                if (s0 == s1 && s1 == s2) continue;
                pts.Clear();
                AddEdgeCut(p0, p1, pts);
                AddEdgeCut(p1, p2, pts);
                AddEdgeCut(p2, p0, pts);
                if (pts.Count == 2 && (pts[0] - pts[1]).sqrMagnitude > 1e-12f)
                    segs.Add((pts[0], pts[1]));
            }

            bridgedGaps = 0;
            var loops = new List<List<Vector2>>();
            if (segs.Count == 0) return loops;

            var chains = ChainSegments(segs, out var closed);
            bridgedGaps = BridgeOpenChains(chains, closed);

            foreach (var chain in chains)
            {
                if (chain.Count < 3) continue;
                if (System.Math.Abs(LoopArea(chain)) < 1e-4) continue;
                loops.Add(chain);
            }
            return loops;
        }

        private static void AddEdgeCut(Vector3 a, Vector3 b, List<Vector2> pts)
        {
            if ((a.z > 0f) == (b.z > 0f)) return;
            float t = a.z / (a.z - b.z);
            pts.Add(new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t));
        }

        // Double before rounding: float*1e4 loses integer precision past ~1.6km coordinates.
        private static (int, int) SnapKey(Vector2 v)
            => ((int)System.Math.Round(v.x * 10000.0), (int)System.Math.Round(v.y * 10000.0));

        // Walks segments end-to-end via snapped-endpoint adjacency into closed loops / open chains.
        private static List<List<Vector2>> ChainSegments(List<(Vector2 a, Vector2 b)> segs, out List<bool> closed)
        {
            var adj = new Dictionary<(int, int), List<int>>(segs.Count * 2);
            for (int i = 0; i < segs.Count; i++)
            {
                RegisterEndpoint(adj, SnapKey(segs[i].a), i);
                RegisterEndpoint(adj, SnapKey(segs[i].b), i);
            }

            var used = new bool[segs.Count];
            var chains = new List<List<Vector2>>();
            closed = new List<bool>();

            for (int i = 0; i < segs.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                var chain = new List<Vector2> { segs[i].a, segs[i].b };
                bool isClosed = ExtendChain(chain, segs, adj, used, atEnd: true)
                             || ExtendChain(chain, segs, adj, used, atEnd: false);
                chains.Add(chain);
                closed.Add(isClosed);
            }
            return chains;
        }

        private static void RegisterEndpoint(Dictionary<(int, int), List<int>> adj, (int, int) key, int segIndex)
        {
            if (!adj.TryGetValue(key, out var list)) { list = new List<int>(2); adj[key] = list; }
            list.Add(segIndex);
        }

        // Appends unused segments at one chain end until stuck; true if the chain met its own other end.
        private static bool ExtendChain(List<Vector2> chain, List<(Vector2 a, Vector2 b)> segs,
            Dictionary<(int, int), List<int>> adj, bool[] used, bool atEnd)
        {
            for (int guard = 0; guard < segs.Count; guard++)
            {
                var tipKey = SnapKey(atEnd ? chain[chain.Count - 1] : chain[0]);
                int next = -1;
                if (adj.TryGetValue(tipKey, out var cands))
                    foreach (var si in cands)
                        if (!used[si]) { next = si; break; }
                if (next < 0) return false;
                used[next] = true;
                var s = segs[next];
                Vector2 tail = SnapKey(s.a) == tipKey ? s.b : s.a;
                var otherTipKey = SnapKey(atEnd ? chain[0] : chain[chain.Count - 1]);
                if (SnapKey(tail) == otherTipKey) return true;
                if (atEnd) chain.Add(tail); else chain.Insert(0, tail);
            }
            return false;
        }

        private static readonly bool[] EndSides = { false, true };

        // Repeatedly joins the closest pair of open endpoints; a chain whose own start-end pair is
        // closest closes onto itself, so distant disjoint parts are not force-merged. Returns the
        // number of cross-chain joins.
        private static int BridgeOpenChains(List<List<Vector2>> chains, List<bool> closed)
        {
            int bridges = 0;
            while (true)
            {
                int openIdx = -1;
                for (int i = 0; i < chains.Count; i++)
                    if (!closed[i]) { openIdx = i; break; }
                if (openIdx < 0) break;

                var ci = chains[openIdx];
                float best = (ci[0] - ci[ci.Count - 1]).sqrMagnitude;
                int bestJ = -1;
                bool fromEnd = false, toEnd = false;
                foreach (bool ie in EndSides)
                {
                    Vector2 pi = ie ? ci[ci.Count - 1] : ci[0];
                    for (int j = 0; j < chains.Count; j++)
                    {
                        if (closed[j] || j == openIdx) continue;
                        var cj = chains[j];
                        foreach (bool je in EndSides)
                        {
                            Vector2 pj = je ? cj[cj.Count - 1] : cj[0];
                            float d = (pi - pj).sqrMagnitude;
                            if (d < best) { best = d; bestJ = j; fromEnd = ie; toEnd = je; }
                        }
                    }
                }

                if (bestJ < 0) { closed[openIdx] = true; continue; }

                var a = chains[openIdx];
                var b = chains[bestJ];
                if (!fromEnd) a.Reverse();
                if (toEnd) b.Reverse();
                a.AddRange(b);
                chains.RemoveAt(bestJ);
                closed.RemoveAt(bestJ);
                bridges++;
            }
            return bridges;
        }

        // Double-precision shoelace: float cancellation at large world coords swamps tiny areas.
        private static double LoopArea(List<Vector2> poly)
        {
            double area = 0;
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = poly[i], q = poly[(i + 1) % n];
                area += (double)p.x * q.y - (double)q.x * p.y;
            }
            return area * 0.5;
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
