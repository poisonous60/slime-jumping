using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(WaterZone_v2))]
    public class WaterZoneV2Editor : UnityEditor.Editor
    {
        const float HandleScreenSize = 0.08f;
        const float PickDist = 0.25f;

        int   _lineCount   = 8;
        float _lineSpacing = 0.5f;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var wz = (WaterZone_v2)target;

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Scene view: drag yellow handles to move points.\n" +
                "Shift-click an edge to insert a point. Ctrl-click a point to delete.",
                MessageType.Info);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Circle"))
                {
                    Undo.RecordObject(wz, "Generate Circle");
                    wz.GenerateCirclePoints();
                    ApplyShapeChange(wz);
                }
                if (GUILayout.Button("Generate Star"))
                {
                    Undo.RecordObject(wz, "Generate Star");
                    wz.GenerateStarPoints();
                    ApplyShapeChange(wz);
                }
                if (GUILayout.Button("Clear"))
                {
                    Undo.RecordObject(wz, "Clear Points");
                    wz.ClearPoints();
                    ApplyShapeChange(wz);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Generate Line", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Count", GUILayout.Width(50));
                _lineCount = Mathf.Max(2, EditorGUILayout.IntField(_lineCount, GUILayout.Width(50)));
                EditorGUILayout.LabelField("Spacing", GUILayout.Width(55));
                _lineSpacing = Mathf.Max(0.01f, EditorGUILayout.FloatField(_lineSpacing, GUILayout.Width(50)));
                if (GUILayout.Button("Generate Line"))
                {
                    Undo.RecordObject(wz, "Generate Line");
                    wz.GenerateLinePoints(_lineCount, _lineSpacing);
                    ApplyShapeChange(wz);
                }
            }
        }

        static void ApplyShapeChange(WaterZone_v2 wz)
        {
            wz.SyncColliderToRestShape();
            if (Application.isPlaying) wz.RebuildRuntime();
            EditorUtility.SetDirty(wz);
        }

        void OnSceneGUI()
        {
            var wz = (WaterZone_v2)target;
            if (!wz.ShowAuthoringGizmos) return;
            Transform t = wz.transform;
            int n = wz.OutlinePointCount;
            if (n == 0) return;

            Event e = Event.current;

            Handles.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < n; i++)
            {
                Vector3 a = t.TransformPoint(wz.GetOutlinePointLocal(i));
                Vector3 b = t.TransformPoint(wz.GetOutlinePointLocal((i + 1) % n));
                Handles.DrawLine(a, b, 2f);
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                if (TryPickEdge(wz, e.mousePosition, out int edgeIdx, out Vector2 localInsert))
                {
                    Undo.RecordObject(wz, "Insert Water Point");
                    wz.InsertOutlinePointLocal(edgeIdx + 1, localInsert);
                    ApplyShapeChange(wz);
                    e.Use();
                    return;
                }
            }

            for (int i = 0; i < n; i++)
            {
                Vector2 local = wz.GetOutlinePointLocal(i);
                Vector3 world = t.TransformPoint(local);
                float handleSize = HandleUtility.GetHandleSize(world) * HandleScreenSize;

                if (e.type == EventType.MouseDown && e.button == 0 && e.control)
                {
                    Vector2 guiPos = HandleUtility.WorldToGUIPoint(world);
                    if ((guiPos - e.mousePosition).sqrMagnitude < 144f && n > 3)
                    {
                        Undo.RecordObject(wz, "Delete Water Point");
                        wz.RemoveOutlinePointAt(i);
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
                    Undo.RecordObject(wz, "Move Water Point");
                    Vector2 newLocal = t.InverseTransformPoint(newWorld);
                    wz.SetOutlinePointLocal(i, newLocal);
                    ApplyShapeChange(wz);
                }

                Handles.Label(world + Vector3.up * handleSize * 1.5f, i.ToString());
            }
        }

        static bool TryPickEdge(WaterZone_v2 wz, Vector2 mouseGui,
            out int edgeIdx, out Vector2 localInsert)
        {
            edgeIdx = -1;
            localInsert = Vector2.zero;
            Transform t = wz.transform;
            int n = wz.OutlinePointCount;

            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 a = t.TransformPoint(wz.GetOutlinePointLocal(i));
                Vector3 b = t.TransformPoint(wz.GetOutlinePointLocal((i + 1) % n));
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
                        wz.GetOutlinePointLocal(i),
                        wz.GetOutlinePointLocal((i + 1) % n), tt);
                    localInsert = midLocal;
                }
            }
            return edgeIdx >= 0;
        }
    }
}
