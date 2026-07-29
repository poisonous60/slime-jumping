using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(WaterfallImpactVFX))]
    public class WaterfallImpactVFXEditor : UnityEditor.Editor
    {
        const float HandleScreenSize = 0.08f;
        const float PickDist = 12f; // GUI-space pixels for shift-click edge insert

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene view:\n" +
                "• Drag yellow handles to move path points.\n" +
                "• Shift-click an edge to insert a point.\n" +
                "• Ctrl-click a point to delete (≥1 required).\n" +
                "• Dashed blue line = raycast from last point.",
                MessageType.Info);
        }

        void OnSceneGUI()
        {
            var vfx = (WaterfallImpactVFX)target;
            Transform t = vfx.transform;
            int n = vfx.PathPointCount;
            if (n == 0) return;

            Event e = Event.current;

            // Path polyline (open chain, not a loop).
            Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = t.TransformPoint(vfx.GetPathPointLocal(i));
                Vector3 b = t.TransformPoint(vfx.GetPathPointLocal(i + 1));
                Handles.DrawLine(a, b, 2f);
            }

            {
                Vector3 last = t.TransformPoint(vfx.GetPathPointLocal(n - 1));
                SerializedProperty rayDirProp = serializedObject.FindProperty("rayDirection");
                SerializedProperty maxDistProp = serializedObject.FindProperty("maxDistance");
                Vector2 rd = rayDirProp != null ? rayDirProp.vector2Value : Vector2.down;
                float md = maxDistProp != null ? maxDistProp.floatValue : 25f;
                if (rd.sqrMagnitude < 1e-6f) rd = Vector2.down;
                Vector3 end = last + (Vector3)(rd.normalized * md);
                Handles.color = new Color(0.3f, 0.8f, 1f, 0.5f);
                Handles.DrawDottedLine(last, end, 4f);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                if (TryPickEdge(vfx, e.mousePosition, out int edgeIdx, out Vector2 localInsert))
                {
                    Undo.RecordObject(vfx, "Insert Waterfall Point");
                    vfx.InsertPathPointLocal(edgeIdx + 1, localInsert);
                    EditorUtility.SetDirty(vfx);
                    e.Use();
                    return;
                }
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 local = vfx.GetPathPointLocal(i);
                Vector3 world = t.TransformPoint(local);
                float size = HandleUtility.GetHandleSize(world) * HandleScreenSize;

                if (e.type == EventType.MouseDown && e.button == 0 && e.control)
                {
                    Vector2 gui = HandleUtility.WorldToGUIPoint(world);
                    if ((gui - e.mousePosition).sqrMagnitude < 144f && n > 1)
                    {
                        Undo.RecordObject(vfx, "Delete Waterfall Point");
                        vfx.RemovePathPointAt(i);
                        EditorUtility.SetDirty(vfx);
                        e.Use();
                        return;
                    }
                }

                Handles.color = Color.yellow;
                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(vfx, "Move Waterfall Point");
                    Vector2 newLocal = t.InverseTransformPoint(moved);
                    vfx.SetPathPointLocal(i, newLocal);
                    EditorUtility.SetDirty(vfx);
                }

                Handles.Label(world + Vector3.up * size * 1.5f, i.ToString());
            }
        }

        static bool TryPickEdge(WaterfallImpactVFX vfx, Vector2 mouseGui,
            out int edgeIdx, out Vector2 localInsert)
        {
            edgeIdx = -1;
            localInsert = Vector2.zero;
            Transform t = vfx.transform;
            int n = vfx.PathPointCount;
            if (n < 2) return false;

            float bestDist = float.MaxValue;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = t.TransformPoint(vfx.GetPathPointLocal(i));
                Vector3 b = t.TransformPoint(vfx.GetPathPointLocal(i + 1));
                Vector2 ag = HandleUtility.WorldToGUIPoint(a);
                Vector2 bg = HandleUtility.WorldToGUIPoint(b);

                Vector2 ab = bg - ag;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 1e-4f) continue;
                float tt = Mathf.Clamp01(Vector2.Dot(mouseGui - ag, ab) / lenSq);
                Vector2 proj = ag + ab * tt;
                float d = Vector2.Distance(mouseGui, proj);
                if (d < bestDist && d < PickDist)
                {
                    bestDist = d;
                    edgeIdx = i;
                    Vector2 midLocal = Vector2.Lerp(
                        vfx.GetPathPointLocal(i),
                        vfx.GetPathPointLocal(i + 1), tt);
                    localInsert = midLocal;
                }
            }
            return edgeIdx >= 0;
        }
    }
}
