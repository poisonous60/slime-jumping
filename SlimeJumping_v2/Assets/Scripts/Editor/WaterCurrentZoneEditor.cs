using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(WaterCurrentZone))]
    public class WaterCurrentZoneEditor : UnityEditor.Editor
    {
        const float HandleScreenSize = 0.08f;
        const float PickPxThreshSq = 144f;  // 12 px

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var wz = (WaterCurrentZone)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene view: drag yellow handles to move centerline points.\n" +
                "Shift-click an edge to insert a point. Ctrl-click a point to delete (min 2 points).",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Vertical Line"))
                {
                    Undo.RecordObject(wz, "Generate Vertical Line");
                    wz.GenerateVerticalLine();
                    ApplyShapeChange(wz);
                }
                if (GUILayout.Button("Slope (45°)"))
                {
                    Undo.RecordObject(wz, "Generate Slope");
                    wz.GenerateSlope45();
                    ApplyShapeChange(wz);
                }
                if (GUILayout.Button("Clear"))
                {
                    Undo.RecordObject(wz, "Clear Centerline");
                    wz.ClearCenterline();
                    ApplyShapeChange(wz);
                }
            }
        }

        static void ApplyShapeChange(WaterCurrentZone wz)
        {
            wz.Rebuild();
            EditorUtility.SetDirty(wz);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wz.gameObject.scene);
        }

        void OnSceneGUI()
        {
            var wz = (WaterCurrentZone)target;
            Transform t = wz.transform;
            int n = wz.CenterlinePointCount;
            if (n == 0) return;

            Event e = Event.current;

            Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = t.TransformPoint(wz.GetCenterlinePointLocal(i));
                Vector3 b = t.TransformPoint(wz.GetCenterlinePointLocal(i + 1));
                Handles.DrawLine(a, b, 2f);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                if (TryPickEdge(wz, e.mousePosition, out int edgeIdx, out Vector2 localInsert))
                {
                    Undo.RecordObject(wz, "Insert Centerline Point");
                    wz.InsertCenterlinePointLocal(edgeIdx + 1, localInsert);
                    ApplyShapeChange(wz);
                    e.Use();
                    return;
                }
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 local = wz.GetCenterlinePointLocal(i);
                Vector3 world = t.TransformPoint(local);
                float handleSize = HandleUtility.GetHandleSize(world) * HandleScreenSize;

                if (e.type == EventType.MouseDown && e.button == 0 && e.control)
                {
                    Vector2 guiPos = HandleUtility.WorldToGUIPoint(world);
                    if ((guiPos - e.mousePosition).sqrMagnitude < PickPxThreshSq && n > 2)
                    {
                        Undo.RecordObject(wz, "Delete Centerline Point");
                        wz.RemoveCenterlinePointAt(i);
                        ApplyShapeChange(wz);
                        e.Use();
                        return;
                    }
                }

                Handles.color = Color.yellow;
                EditorGUI.BeginChangeCheck();
                Vector3 newWorld = Handles.FreeMoveHandle(
                    world, handleSize, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(wz, "Move Centerline Point");
                    Vector2 newLocal = t.InverseTransformPoint(newWorld);
                    wz.SetCenterlinePointLocal(i, newLocal);
                    ApplyShapeChange(wz);
                }

                Handles.Label(world + Vector3.up * handleSize * 1.5f, i.ToString());
            }
        }

        static bool TryPickEdge(WaterCurrentZone wz, Vector2 mouseGui,
            out int edgeIdx, out Vector2 localInsert)
        {
            edgeIdx = -1;
            localInsert = Vector2.zero;
            Transform t = wz.transform;
            int n = wz.CenterlinePointCount;

            float bestDist = float.MaxValue;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = t.TransformPoint(wz.GetCenterlinePointLocal(i));
                Vector3 b = t.TransformPoint(wz.GetCenterlinePointLocal(i + 1));
                Vector2 ag = HandleUtility.WorldToGUIPoint(a);
                Vector2 bg = HandleUtility.WorldToGUIPoint(b);

                Vector2 ab = bg - ag;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 1e-4f) continue;
                float tt = Mathf.Clamp01(Vector2.Dot(mouseGui - ag, ab) / lenSq);
                Vector2 proj = ag + ab * tt;
                float d = Vector2.Distance(mouseGui, proj);
                if (d < bestDist && d < 12f)
                {
                    bestDist = d;
                    edgeIdx = i;
                    Vector2 midLocal = Vector2.Lerp(
                        wz.GetCenterlinePointLocal(i),
                        wz.GetCenterlinePointLocal(i + 1), tt);
                    localInsert = midLocal;
                }
            }
            return edgeIdx >= 0;
        }
    }
}
