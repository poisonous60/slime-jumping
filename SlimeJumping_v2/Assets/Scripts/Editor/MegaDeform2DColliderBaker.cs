#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // Bakes an ordered silhouette vertex-index list from the source mesh's REST pose
    // into contourIndices; runtime only re-reads those indices off the deformed mesh.
    [CustomEditor(typeof(MegaDeform2DCollider))]
    public class MegaDeform2DColliderBaker : UnityEditor.Editor
    {
        public enum BakeStrategy
        {
            TopEdge,
            ZSlice,
            VertexPicker,
        }

        public enum ZSide
        {
            MaxZ,
            MinZ,
            Custom,
        }

        SerializedProperty sourceProp;
        SerializedProperty targetProp;
        SerializedProperty contourIndicesProp;
        SerializedProperty closedLoopProp;
        SerializedProperty pathIndexProp;
        SerializedProperty reverseOnNegativeScaleProp;

        BakeStrategy strategy = BakeStrategy.TopEdge;
        ZSide zSide = ZSide.MaxZ;
        float customZ = 0f;
        float yEpsilon = 1e-3f;
        float zEpsilon = 1e-3f;
        float xDedupeEpsilon = 1e-4f;

        // ZSlice params
        ZSide zSliceSide = ZSide.MaxZ;
        float zSliceCustomZ = 0f;
        float zSliceEpsilon = 1e-3f;
        float zSliceDedupeEpsilon = 1e-4f;
        bool zSliceForceCcw = true;

        // Vertex-picker state
        bool pickingActive;
        bool pickingDedupeByPos = true;
        float pickingDedupeEps = 1e-4f;
        int lastPickedIndex = -1;

        void OnEnable()
        {
            sourceProp = serializedObject.FindProperty("source");
            targetProp = serializedObject.FindProperty("target");
            contourIndicesProp = serializedObject.FindProperty("contourIndices");
            closedLoopProp = serializedObject.FindProperty("closedLoop");
            pathIndexProp = serializedObject.FindProperty("pathIndex");
            reverseOnNegativeScaleProp = serializedObject.FindProperty("reverseOnNegativeScale");
        }

        void OnDisable()
        {
            if (pickingActive) StopPicking();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(sourceProp);
            EditorGUILayout.PropertyField(targetProp);
            EditorGUILayout.PropertyField(closedLoopProp);
            EditorGUILayout.PropertyField(pathIndexProp);
            EditorGUILayout.PropertyField(reverseOnNegativeScaleProp);

            EditorGUILayout.PropertyField(contourIndicesProp, new GUIContent(
                $"Contour Indices ({contourIndicesProp.arraySize})"), true);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Baker", EditorStyles.boldLabel);

            var newStrategy = (BakeStrategy)EditorGUILayout.EnumPopup("Strategy", strategy);
            if (newStrategy != strategy)
            {
                if (pickingActive && newStrategy != BakeStrategy.VertexPicker) StopPicking();
                strategy = newStrategy;
            }

            switch (strategy)
            {
                case BakeStrategy.TopEdge: DrawTopEdgeParams(); break;
                case BakeStrategy.ZSlice: DrawZSliceParams(); break;
                case BakeStrategy.VertexPicker: DrawVertexPickerParams(); break;
            }

            if (strategy == BakeStrategy.TopEdge || strategy == BakeStrategy.ZSlice)
            {
                using (new EditorGUI.DisabledScope(sourceProp.objectReferenceValue == null))
                {
                    if (GUILayout.Button("Bake Contour Indices")) Bake();
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ---- Inspector sections per strategy ----

        void DrawTopEdgeParams()
        {
            EditorGUILayout.HelpBox(
                "Picks vertices whose Y matches mesh bounds.max.y AND whose Z matches the chosen side, " +
                "sorts them along X, and writes the ordered indices. Result is an open polyline suited " +
                "for EdgeCollider2D.",
                MessageType.None);

            zSide = (ZSide)EditorGUILayout.EnumPopup("Z Side", zSide);
            if (zSide == ZSide.Custom)
                customZ = EditorGUILayout.FloatField("Custom Z", customZ);
            yEpsilon = EditorGUILayout.FloatField("Y Epsilon", yEpsilon);
            zEpsilon = EditorGUILayout.FloatField("Z Epsilon", zEpsilon);
            xDedupeEpsilon = EditorGUILayout.FloatField("X Dedupe Epsilon", xDedupeEpsilon);
        }

        void DrawZSliceParams()
        {
            EditorGUILayout.HelpBox(
                "Finds vertices lying on the chosen Z plane, keeps only mesh triangles whose ALL " +
                "three verts are on that plane, then walks the BOUNDARY of that slice sub-mesh as " +
                "a closed cycle. Suited for PolygonCollider2D on closed 3D meshes like the BendTest box.",
                MessageType.None);

            zSliceSide = (ZSide)EditorGUILayout.EnumPopup("Z Side", zSliceSide);
            if (zSliceSide == ZSide.Custom)
                zSliceCustomZ = EditorGUILayout.FloatField("Custom Z", zSliceCustomZ);
            zSliceEpsilon = EditorGUILayout.FloatField("Z Epsilon", zSliceEpsilon);
            zSliceDedupeEpsilon = EditorGUILayout.FloatField("Position Dedupe Epsilon", zSliceDedupeEpsilon);
            zSliceForceCcw = EditorGUILayout.Toggle("Force CCW Winding", zSliceForceCcw);
        }

        void DrawVertexPickerParams()
        {
            EditorGUILayout.HelpBox(
                "Click mesh vertices in the Scene view in outline order.\n" +
                "  • Click an unpicked vert: append to the contour.\n" +
                "  • Click a picked vert OR Alt+click: remove it.\n" +
                "  • For PolygonCollider2D, also tick 'Closed Loop' above.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(sourceProp.objectReferenceValue == null))
            {
                if (!pickingActive)
                {
                    if (GUILayout.Button("Start Picking")) StartPicking();
                }
                else
                {
                    GUI.backgroundColor = new Color(1.0f, 0.6f, 0.6f);
                    if (GUILayout.Button("Stop Picking")) StopPicking();
                    GUI.backgroundColor = Color.white;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("Clear Indices")) ClearPickedIndices();
                    if (GUILayout.Button("Reverse Order")) ReversePickedIndices();
                }
            }

            pickingDedupeByPos = EditorGUILayout.Toggle("Dedupe By Position", pickingDedupeByPos);
            if (pickingDedupeByPos)
                pickingDedupeEps = EditorGUILayout.FloatField("Dedupe Epsilon", pickingDedupeEps);
        }

        // ---- Bake (non-picker strategies) ----

        void Bake()
        {
            var comp = (MegaDeform2DCollider)base.target;
            var ctx = comp.Source;
            if (ctx == null)
            {
                EditorUtility.DisplayDialog("Bake", "Assign a MegaModifyObject source first.", "OK");
                return;
            }

            var mesh = ctx.originalMesh;
            if (mesh == null)
            {
                EditorUtility.DisplayDialog("Bake",
                    "source.originalMesh is null. Either assign it, or let the scene run once so " +
                    "MegaModifyObject.Start() auto-populates it from MeshFilter/SkinnedMeshRenderer.",
                    "OK");
                return;
            }

            int[] result = null;
            string bakeLabel = null;

            switch (strategy)
            {
                case BakeStrategy.TopEdge:
                {
                    float z = zSide switch
                    {
                        ZSide.MaxZ => mesh.bounds.max.z,
                        ZSide.MinZ => mesh.bounds.min.z,
                        _ => customZ,
                    };
                    result = BakeTopEdge(mesh, z, yEpsilon, zEpsilon, xDedupeEpsilon);
                    bakeLabel = $"TopEdge (y≈{mesh.bounds.max.y:F3}, z≈{z:F3})";
                    break;
                }
                case BakeStrategy.ZSlice:
                {
                    float z = zSliceSide switch
                    {
                        ZSide.MaxZ => mesh.bounds.max.z,
                        ZSide.MinZ => mesh.bounds.min.z,
                        _ => zSliceCustomZ,
                    };
                    result = BakeZSlice(mesh, z, zSliceEpsilon, zSliceDedupeEpsilon, zSliceForceCcw, out string diag);
                    bakeLabel = $"ZSlice (z≈{z:F3}, {diag})";
                    if (result == null)
                    {
                        EditorUtility.DisplayDialog("Bake", $"ZSlice failed: {diag}", "OK");
                        return;
                    }
                    break;
                }
                default:
                    return;
            }

            if (result == null || result.Length < 2)
            {
                EditorUtility.DisplayDialog("Bake",
                    "Baker returned fewer than 2 vertices. Adjust tolerances or Z side.",
                    "OK");
                return;
            }

            Undo.RecordObject(comp, "Bake Contour Indices");
            comp.ContourIndices = result;
            if (strategy == BakeStrategy.TopEdge) comp.ClosedLoop = false;
            if (strategy == BakeStrategy.ZSlice) comp.ClosedLoop = true;
            EditorUtility.SetDirty(comp);

            Debug.Log($"[MegaDeform2DColliderBaker] {bakeLabel}: baked {result.Length} vertex indices onto {comp.name}.", comp);
        }

        // ---- Vertex picker ----

        void StartPicking()
        {
            if (pickingActive) return;
            pickingActive = true;
            SceneView.duringSceneGui += OnSceneGUIPick;
            SceneView.RepaintAll();
        }

        void StopPicking()
        {
            if (!pickingActive) return;
            pickingActive = false;
            SceneView.duringSceneGui -= OnSceneGUIPick;
            lastPickedIndex = -1;
            SceneView.RepaintAll();
        }

        void ClearPickedIndices()
        {
            var comp = (MegaDeform2DCollider)base.target;
            Undo.RecordObject(comp, "Clear Contour Indices");
            comp.ContourIndices = new int[0];
            lastPickedIndex = -1;
            EditorUtility.SetDirty(comp);
            SceneView.RepaintAll();
        }

        void ReversePickedIndices()
        {
            var comp = (MegaDeform2DCollider)base.target;
            var cur = comp.ContourIndices;
            if (cur == null || cur.Length < 2) return;
            var rev = new int[cur.Length];
            for (int i = 0; i < cur.Length; i++) rev[i] = cur[cur.Length - 1 - i];
            Undo.RecordObject(comp, "Reverse Contour Indices");
            comp.ContourIndices = rev;
            EditorUtility.SetDirty(comp);
            SceneView.RepaintAll();
        }

        void OnSceneGUIPick(SceneView sv)
        {
            var comp = (MegaDeform2DCollider)base.target;
            if (comp == null || comp.Source == null) return;

            // Prefer the live (deformed) mesh so dots sit on what the user actually sees.
            var mesh = comp.Source.mesh != null ? comp.Source.mesh : comp.Source.originalMesh;
            if (mesh == null) return;

            var tr = comp.Source.transform;
            var verts = mesh.vertices;

            var picked = comp.ContourIndices ?? new int[0];
            var pickedOrder = new Dictionary<int, int>(picked.Length);
            for (int i = 0; i < picked.Length; i++) pickedOrder[picked[i]] = i;

            if (picked.Length >= 2)
            {
                Handles.color = new Color(0.2f, 1f, 1f, 0.8f);
                for (int i = 1; i < picked.Length; i++)
                {
                    Vector3 a = tr.TransformPoint(verts[picked[i - 1]]);
                    Vector3 b = tr.TransformPoint(verts[picked[i]]);
                    Handles.DrawAAPolyLine(3f, a, b);
                }
                if (comp.ClosedLoop && picked.Length >= 3)
                {
                    Vector3 a = tr.TransformPoint(verts[picked[picked.Length - 1]]);
                    Vector3 b = tr.TransformPoint(verts[picked[0]]);
                    Handles.DrawAAPolyLine(3f, a, b);
                }
            }

            var worldVerts = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) worldVerts[i] = tr.TransformPoint(verts[i]);

            for (int i = 0; i < worldVerts.Length; i++)
            {
                var wp = worldVerts[i];
                float size = HandleUtility.GetHandleSize(wp) * 0.04f;
                float pickSize = size * 1.6f;

                bool isPicked = pickedOrder.ContainsKey(i);
                bool isLast = i == lastPickedIndex;

                Handles.color = isLast
                    ? new Color(1f, 0.4f, 0.2f, 1f)
                    : (isPicked ? new Color(0.2f, 1f, 0.3f, 1f)
                                : new Color(1f, 1f, 1f, 0.45f));

                if (Handles.Button(wp, Quaternion.identity, size, pickSize, Handles.DotHandleCap))
                {
                    HandleVertexClick(comp, i, verts);
                    SceneView.RepaintAll();
                }

                if (isPicked)
                {
                    Handles.Label(wp + Vector3.up * size * 2f, pickedOrder[i].ToString(),
                        EditorStyles.whiteBoldLabel);
                }
            }
        }

        void HandleVertexClick(MegaDeform2DCollider comp, int index, Vector3[] verts)
        {
            var cur = comp.ContourIndices ?? new int[0];
            var list = new List<int>(cur.Length + 1);
            list.AddRange(cur);

            int existing = list.IndexOf(index);
            bool altClick = Event.current != null && Event.current.alt;

            if (existing >= 0 || altClick)
            {
                if (existing >= 0) list.RemoveAt(existing);
                // else alt-click on an un-picked vert → no-op
                if (lastPickedIndex == index) lastPickedIndex = -1;
            }
            else
            {
                if (pickingDedupeByPos)
                {
                    var vp = verts[index];
                    float eps2 = pickingDedupeEps * pickingDedupeEps;
                    for (int j = 0; j < list.Count; j++)
                    {
                        if ((verts[list[j]] - vp).sqrMagnitude < eps2)
                        {
                            // Twin vert at the same position already picked.
                            return;
                        }
                    }
                }
                list.Add(index);
                lastPickedIndex = index;
            }

            Undo.RecordObject(comp, "Pick Contour Vertex");
            comp.ContourIndices = list.ToArray();
            EditorUtility.SetDirty(comp);
        }

        // ---- Programmatic bake (TopEdge) ----

        // Verts at bounds.max.y and z≈zTarget, X-sorted and X-deduped. Public so other
        // editor tools (e.g. BendTestDeformingSetup) can bake without the inspector.
        public static int[] BakeTopEdge(Mesh mesh, float zTarget, float yEps, float zEps, float xDedupeEps)
        {
            var verts = mesh.vertices;
            float yMax = mesh.bounds.max.y;

            var candidates = new List<(int idx, float x)>(verts.Length);
            for (int i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                if (Mathf.Abs(v.y - yMax) > yEps) continue;
                if (Mathf.Abs(v.z - zTarget) > zEps) continue;
                candidates.Add((i, v.x));
            }

            candidates.Sort((a, b) => a.x.CompareTo(b.x));

            var deduped = new List<int>(candidates.Count);
            float lastX = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].x - lastX < xDedupeEps) continue;
                deduped.Add(candidates[i].idx);
                lastX = candidates[i].x;
            }

            return deduped.ToArray();
        }

        // ---- Programmatic bake (ZSlice) ----

        // Walks the boundary of the z≈zTarget slice sub-mesh as a closed cycle
        // (boundary edge = in exactly one slice tri, interior = two). Only valid when
        // the silhouette at that Z is a real chain of coplanar mesh edges. Null on failure.
        public static int[] BakeZSlice(
            Mesh mesh, float zTarget, float zEps, float dedupeEps, bool forceCcw, out string diag)
        {
            var verts = mesh.vertices;
            var tris = mesh.triangles;

            var inSlice = new bool[verts.Length];
            int sliceVertCount = 0;
            for (int i = 0; i < verts.Length; i++)
            {
                if (Mathf.Abs(verts[i].z - zTarget) < zEps)
                {
                    inSlice[i] = true;
                    sliceVertCount++;
                }
            }

            // Collapse duplicate-position verts to the lowest index, else the edge graph
            // is a multigraph and the boundary walk branches.
            var canon = new int[verts.Length];
            for (int i = 0; i < verts.Length; i++) canon[i] = i;
            float e2 = dedupeEps * dedupeEps;
            for (int i = 0; i < verts.Length; i++)
            {
                if (!inSlice[i]) continue;
                for (int j = 0; j < i; j++)
                {
                    if (!inSlice[j]) continue;
                    if (canon[j] != j) continue;
                    if ((verts[i] - verts[j]).sqrMagnitude < e2)
                    {
                        canon[i] = j;
                        break;
                    }
                }
            }

            // Keyed (min, max) so the dictionary compares edges undirected.
            var edgeCount = new Dictionary<(int a, int b), int>();
            int sliceTriCount = 0;

            void Bump(int u, int v)
            {
                if (u == v) return;
                var k = u < v ? (u, v) : (v, u);
                edgeCount.TryGetValue(k, out int c);
                edgeCount[k] = c + 1;
            }

            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (!inSlice[a] || !inSlice[b] || !inSlice[c]) continue;
                int ca = canon[a], cb = canon[b], cc = canon[c];
                if (ca == cb || cb == cc || cc == ca) continue;
                sliceTriCount++;
                Bump(ca, cb);
                Bump(cb, cc);
                Bump(cc, ca);
            }

            if (sliceTriCount == 0)
            {
                diag = $"no slice triangles (sliceVerts={sliceVertCount}); " +
                       "triangles need ALL 3 verts on the Z plane. Widen zEps or pick another zTarget.";
                return null;
            }

            // Boundary adjacency: edges that appear in exactly one slice triangle.
            var adj = new Dictionary<int, List<int>>();
            void AddAdj(int u, int v)
            {
                if (!adj.TryGetValue(u, out var list)) { list = new List<int>(); adj[u] = list; }
                if (!list.Contains(v)) list.Add(v);
            }

            int boundaryEdgeCount = 0;
            foreach (var kv in edgeCount)
            {
                if (kv.Value != 1) continue;
                AddAdj(kv.Key.a, kv.Key.b);
                AddAdj(kv.Key.b, kv.Key.a);
                boundaryEdgeCount++;
            }

            if (adj.Count == 0)
            {
                diag = $"no boundary edges (sliceTris={sliceTriCount}); slice region is a closed " +
                       "manifold with no edge-exposed boundary. Unlikely for a Z-plane slice.";
                return null;
            }

            // Pick a deterministic start: leftmost (lowest X), break tie on lowest Y.
            int start = -1;
            float bestX = float.PositiveInfinity, bestY = float.PositiveInfinity;
            foreach (var idx in adj.Keys)
            {
                var v = verts[idx];
                if (v.x < bestX - 1e-6f || (Mathf.Abs(v.x - bestX) <= 1e-6f && v.y < bestY))
                {
                    bestX = v.x; bestY = v.y; start = idx;
                }
            }

            var cycle = new List<int> { start };
            int prev = -1;
            int curr = start;
            int safety = adj.Count + 2;
            while (safety-- > 0)
            {
                var nbrs = adj[curr];
                int next = -1;
                for (int k = 0; k < nbrs.Count; k++) { if (nbrs[k] != prev) { next = nbrs[k]; break; } }
                if (next < 0) break;
                if (next == start) break;
                cycle.Add(next);
                prev = curr;
                curr = next;
            }

            if (cycle.Count != adj.Count)
            {
                diag = $"walked {cycle.Count}/{adj.Count} boundary verts — boundary is not a simple cycle " +
                       "(multiple loops or branching). Try adjusting zTarget/zEps.";
                return null;
            }

            if (forceCcw && SignedArea2D(cycle, verts) < 0f) cycle.Reverse();

            diag = $"{cycle.Count} verts, {boundaryEdgeCount} boundary edges, {sliceTriCount} slice tris";
            return cycle.ToArray();
        }

        static float SignedArea2D(List<int> cycle, Vector3[] verts)
        {
            float a = 0f;
            int n = cycle.Count;
            for (int i = 0; i < n; i++)
            {
                var p0 = verts[cycle[i]];
                var p1 = verts[cycle[(i + 1) % n]];
                a += (p1.x - p0.x) * (p1.y + p0.y);
            }
            return -0.5f * a;  // shoelace; sign flipped so CCW is positive in XY
        }
    }
}
#endif
