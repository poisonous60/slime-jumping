using UnityEditor;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    // Scene-view authoring tool for GraniteBlastNavGraph. Patterned on
    // TerrainShape2DEditor:
    //   Nodes tool — drag to move, Shift+click empty to add, Ctrl+click node to delete.
    //   Edges tool — drag from node to node to create an edge, Ctrl+click an edge to delete.
    // All mutation goes through SerializedObject so Undo and dirty state are automatic.
    [CustomEditor(typeof(GraniteBlastNavGraph))]
    public class GraniteBlastNavGraphEditor : UnityEditor.Editor
    {
        enum Tool { Nodes, Edges }

        Tool _tool = Tool.Nodes;
        bool _edgeDragging;
        int _edgeDragFromIdx = -1;

        const float NODE_HANDLE_SIZE = 0.10f;
        const float CLICK_RADIUS_PX = 14f;
        const float SNAP_RADIUS_PX = 18f;
        const float EDGE_CLICK_RADIUS_PX = 8f;

        SerializedProperty _nodesProp;
        SerializedProperty _edgesProp;

        void OnEnable()
        {
            _nodesProp = serializedObject.FindProperty("nodes");
            _edgesProp = serializedObject.FindProperty("edges");
        }

        // ──────────────── Inspector ────────────────

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Active Tool", EditorStyles.boldLabel);
            var newTool = (Tool)GUILayout.Toolbar((int)_tool, new[] { "Nodes", "Edges" });
            if (newTool != _tool)
            {
                _tool = newTool;
                _edgeDragging = false;
                _edgeDragFromIdx = -1;
                SceneView.RepaintAll();
            }

            EditorGUILayout.HelpBox(
                _tool == Tool.Nodes
                    ? "Nodes:\n• Drag a node to move it.\n• Shift+click empty space to add a node.\n• Ctrl+click a node to delete it (and its edges)."
                    : "Edges:\n• Drag from a node to another node to create an edge.\n• Ctrl+click an edge to delete it.",
                MessageType.None);

            EditorGUILayout.LabelField($"Nodes: {_nodesProp.arraySize}    Edges: {_edgesProp.arraySize}");

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_edgesProp.arraySize == 0))
            {
                if (GUILayout.Button("Clear Edges")) _edgesProp.ClearArray();
            }
            using (new EditorGUI.DisabledScope(_nodesProp.arraySize == 0))
            {
                if (GUILayout.Button("Clear All"))
                {
                    if (EditorUtility.DisplayDialog("Clear NavGraph", "Remove all nodes and edges?", "Clear", "Cancel"))
                    {
                        _nodesProp.ClearArray();
                        _edgesProp.ClearArray();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject, "m_Script", "nodes", "edges");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Raw Data (advanced)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_nodesProp, true);
            EditorGUILayout.PropertyField(_edgesProp, true);

            serializedObject.ApplyModifiedProperties();
        }

        // ──────────────── Scene ────────────────

        void OnSceneGUI()
        {
            var src = (GraniteBlastNavGraph)target;
            var t = src.transform;
            serializedObject.Update();

            int passiveID = GUIUtility.GetControlID(FocusType.Passive);
            if (Event.current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(passiveID);

            DrawEdges(t);
            DrawNodeIndices(t);

            switch (_tool)
            {
                case Tool.Nodes: HandleNodesTool(src, t); break;
                case Tool.Edges: HandleEdgesTool(src, t); break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ──────────────── Drawing ────────────────

        void DrawEdges(Transform t)
        {
            int n = _nodesProp.arraySize;
            Handles.color = new Color(0.4f, 0.85f, 1f, 0.85f);
            for (int i = 0; i < _edgesProp.arraySize; i++)
            {
                var pair = _edgesProp.GetArrayElementAtIndex(i);
                int a = pair.FindPropertyRelative("x").intValue;
                int b = pair.FindPropertyRelative("y").intValue;
                if (a < 0 || b < 0 || a >= n || b >= n || a == b) continue;
                Vector3 wa = t.TransformPoint(GetNodeLocal(a));
                Vector3 wb = t.TransformPoint(GetNodeLocal(b));
                Handles.DrawLine(wa, wb, 2f);
            }
        }

        void DrawNodeIndices(Transform t)
        {
            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
            for (int i = 0; i < _nodesProp.arraySize; i++)
            {
                Vector3 wp = t.TransformPoint(GetNodeLocal(i));
                Handles.Label(wp + new Vector3(0.12f, 0.12f, 0f), i.ToString(), style);
            }
        }

        // ──────────────── Nodes tool ────────────────

        void HandleNodesTool(GraniteBlastNavGraph src, Transform t)
        {
            var e = Event.current;

            // Modifier+click is checked BEFORE FreeMoveHandle, otherwise the handle
            // claims the hot control on Layout and eats the MouseDown.
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (e.control && TryFindClickedNode(t, e.mousePosition, out int delIdx))
                {
                    DeleteNode(delIdx);
                    e.Use();
                    return;
                }
                if (e.shift && !TryFindClickedNode(t, e.mousePosition, out _))
                {
                    if (TryProjectMouseToTransformPlane(t, e.mousePosition, out Vector3 localHit))
                    {
                        AddNode(localHit);
                    }
                    e.Use();
                    return;
                }
            }

            // Suppress FreeMoveHandle while a modifier is held — Layout fires under
            // the handle even before MouseDown, and the handle would claim the click.
            bool suppressHandles = (e.shift || e.control)
                                && (e.type == EventType.Layout || e.type == EventType.MouseDown);

            for (int i = 0; i < _nodesProp.arraySize; i++)
            {
                Vector3 localPos = GetNodeLocal(i);
                Vector3 wp = t.TransformPoint(localPos);
                float size = HandleUtility.GetHandleSize(wp) * NODE_HANDLE_SIZE;
                Handles.color = new Color(0.4f, 0.85f, 1f, 0.95f);

                if (!suppressHandles)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 nw = Handles.FreeMoveHandle(wp, size, Vector3.zero, Handles.SphereHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Vector3 lp = t.InverseTransformPoint(nw);
                        SetNodeLocal(i, lp);
                    }
                }
                else if (e.type == EventType.Repaint)
                {
                    Handles.SphereHandleCap(0, wp, t.rotation, size, EventType.Repaint);
                }
            }
        }

        // ──────────────── Edges tool ────────────────

        void HandleEdgesTool(GraniteBlastNavGraph src, Transform t)
        {
            var e = Event.current;

            // Picker dots for every node; highlight the drag source.
            for (int i = 0; i < _nodesProp.arraySize; i++)
            {
                Vector3 wp = t.TransformPoint(GetNodeLocal(i));
                float size = HandleUtility.GetHandleSize(wp) * 0.06f;
                Handles.color = (_edgeDragging && i == _edgeDragFromIdx)
                    ? Color.cyan
                    : new Color(0.4f, 0.85f, 1f, 0.95f);
                Handles.SphereHandleCap(0, wp, t.rotation, size, EventType.Repaint);
            }

            int snapIdx = -1;
            if (_edgeDragging)
            {
                snapIdx = TryFindNodeNear(t, e.mousePosition, _edgeDragFromIdx, SNAP_RADIUS_PX);
                DrawEdgeDragPreview(t, e.mousePosition, snapIdx);
                SceneView.RepaintAll();
            }

            if (e.type == EventType.MouseDown && e.button == 0 && !_edgeDragging)
            {
                if (e.control)
                {
                    if (TryFindClickedEdge(t, e.mousePosition, out int eIdx))
                    {
                        DeleteEdge(eIdx);
                        e.Use();
                    }
                }
                else if (TryFindClickedNode(t, e.mousePosition, out int srcIdx))
                {
                    _edgeDragging = true;
                    _edgeDragFromIdx = srcIdx;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 && _edgeDragging)
            {
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _edgeDragging)
            {
                if (snapIdx >= 0 && snapIdx != _edgeDragFromIdx)
                    AddEdge(_edgeDragFromIdx, snapIdx);
                _edgeDragging = false;
                _edgeDragFromIdx = -1;
                e.Use();
                SceneView.RepaintAll();
            }
        }

        void DrawEdgeDragPreview(Transform t, Vector2 mouseGUI, int snapIdx)
        {
            if (_edgeDragFromIdx < 0 || _edgeDragFromIdx >= _nodesProp.arraySize) return;
            Vector3 fromW = t.TransformPoint(GetNodeLocal(_edgeDragFromIdx));
            Vector3 toW;
            if (snapIdx >= 0)
            {
                toW = t.TransformPoint(GetNodeLocal(snapIdx));
            }
            else if (TryProjectMouseToTransformPlane(t, mouseGUI, out Vector3 localHit))
            {
                toW = t.TransformPoint(localHit);
            }
            else
            {
                toW = fromW;
            }
            Handles.color = (snapIdx >= 0)
                ? new Color(0.4f, 1f, 0.4f, 0.95f)
                : new Color(1f, 1f, 0.2f, 0.95f);
            Handles.DrawLine(fromW, toW, 3f);
            if (snapIdx >= 0)
            {
                float r = HandleUtility.GetHandleSize(toW) * 0.12f;
                Handles.DrawWireDisc(toW, t.forward, r);
                Handles.DrawWireDisc(toW, t.forward, r * 0.6f);
            }
        }

        // ──────────────── Mutation ────────────────

        void AddNode(Vector3 localPos)
        {
            int newIdx = _nodesProp.arraySize;
            _nodesProp.arraySize = newIdx + 1;
            _nodesProp.GetArrayElementAtIndex(newIdx).vector3Value = localPos;
        }

        void SetNodeLocal(int i, Vector3 lp)
        {
            _nodesProp.GetArrayElementAtIndex(i).vector3Value = lp;
        }

        void DeleteNode(int idx)
        {
            // Drop edges referencing idx, and shift higher indices down by one.
            for (int i = _edgesProp.arraySize - 1; i >= 0; i--)
            {
                var pair = _edgesProp.GetArrayElementAtIndex(i);
                var xProp = pair.FindPropertyRelative("x");
                var yProp = pair.FindPropertyRelative("y");
                int a = xProp.intValue;
                int b = yProp.intValue;
                if (a == idx || b == idx)
                {
                    _edgesProp.DeleteArrayElementAtIndex(i);
                    continue;
                }
                if (a > idx) xProp.intValue = a - 1;
                if (b > idx) yProp.intValue = b - 1;
            }
            _nodesProp.DeleteArrayElementAtIndex(idx);
        }

        void AddEdge(int a, int b)
        {
            // Reject duplicates — edges are undirected.
            for (int i = 0; i < _edgesProp.arraySize; i++)
            {
                var pair = _edgesProp.GetArrayElementAtIndex(i);
                int x = pair.FindPropertyRelative("x").intValue;
                int y = pair.FindPropertyRelative("y").intValue;
                if ((x == a && y == b) || (x == b && y == a)) return;
            }
            int newIdx = _edgesProp.arraySize;
            _edgesProp.arraySize = newIdx + 1;
            var added = _edgesProp.GetArrayElementAtIndex(newIdx);
            added.FindPropertyRelative("x").intValue = a;
            added.FindPropertyRelative("y").intValue = b;
        }

        void DeleteEdge(int idx)
        {
            _edgesProp.DeleteArrayElementAtIndex(idx);
        }

        // ──────────────── Picking ────────────────

        Vector3 GetNodeLocal(int i) => _nodesProp.GetArrayElementAtIndex(i).vector3Value;

        bool TryFindClickedNode(Transform t, Vector2 mouseGUI, out int idx)
        {
            idx = -1;
            float best = CLICK_RADIUS_PX;
            for (int i = 0; i < _nodesProp.arraySize; i++)
            {
                Vector3 wp = t.TransformPoint(GetNodeLocal(i));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                float d = Vector2.Distance(g, mouseGUI);
                if (d < best) { best = d; idx = i; }
            }
            return idx >= 0;
        }

        int TryFindNodeNear(Transform t, Vector2 mouseGUI, int excludeIdx, float radiusPx)
        {
            int idx = -1;
            float best = radiusPx;
            for (int i = 0; i < _nodesProp.arraySize; i++)
            {
                if (i == excludeIdx) continue;
                Vector3 wp = t.TransformPoint(GetNodeLocal(i));
                Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                float d = Vector2.Distance(g, mouseGUI);
                if (d < best) { best = d; idx = i; }
            }
            return idx;
        }

        bool TryFindClickedEdge(Transform t, Vector2 mouseGUI, out int idx)
        {
            idx = -1;
            int n = _nodesProp.arraySize;
            float best = EDGE_CLICK_RADIUS_PX;
            for (int i = 0; i < _edgesProp.arraySize; i++)
            {
                var pair = _edgesProp.GetArrayElementAtIndex(i);
                int a = pair.FindPropertyRelative("x").intValue;
                int b = pair.FindPropertyRelative("y").intValue;
                if (a < 0 || b < 0 || a >= n || b >= n || a == b) continue;
                Vector2 ga = HandleUtility.WorldToGUIPoint(t.TransformPoint(GetNodeLocal(a)));
                Vector2 gb = HandleUtility.WorldToGUIPoint(t.TransformPoint(GetNodeLocal(b)));
                float d = HandleUtility.DistancePointToLineSegment(mouseGUI, ga, gb);
                if (d < best) { best = d; idx = i; }
            }
            return idx >= 0;
        }

        // Project the mouse onto the transform's local XY plane (local Z = 0) and
        // return the local-space hit. Matches NavGraph nodes' "front face" — the
        // user can edit Z afterwards in the inspector for depth-swerving paths.
        bool TryProjectMouseToTransformPlane(Transform t, Vector2 mouseGUI, out Vector3 localHit)
        {
            Plane plane = new Plane(t.forward, t.position);
            Ray ray = HandleUtility.GUIPointToWorldRay(mouseGUI);
            if (plane.Raycast(ray, out float dist))
            {
                Vector3 worldHit = ray.GetPoint(dist);
                Vector3 lp = t.InverseTransformPoint(worldHit);
                lp.z = 0f;
                localHit = lp;
                return true;
            }
            localHit = default;
            return false;
        }
    }
}
