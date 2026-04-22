using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Scene-view handles for editing a WaypointSnakePath's route. Drag each waypoint in 3D,
    /// insert/remove via the inspector header buttons. Waypoint numeric values (X/Y/Z, speedAfter,
    /// loop, useCatmullRom, samplesPerSegment) remain editable via the default inspector.
    /// </summary>
    [CustomEditor(typeof(WaypointSnakePath))]
    public class WaypointSnakePathEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var path = (WaypointSnakePath)target;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Route editing", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag the colored handles in the Scene view to move waypoints in 3D.\n" +
                "Use the list above to set per-waypoint speedAfter (head speed while traveling " +
                "from this waypoint to the next).",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Append WP at end"))
                {
                    AppendWaypoint(path, atEnd: true);
                }
                if (GUILayout.Button("Insert WP at start"))
                {
                    AppendWaypoint(path, atEnd: false);
                }
                if (GUILayout.Button("Rebuild"))
                {
                    path.Rebuild();
                    EditorUtility.SetDirty(path);
                    SceneView.RepaintAll();
                }
            }
        }

        void OnSceneGUI()
        {
            var path = (WaypointSnakePath)target;
            var so = new SerializedObject(path);
            var wpProp = so.FindProperty("waypoints");
            if (wpProp == null || !wpProp.isArray) return;

            bool anyChanged = false;

            for (int i = 0; i < wpProp.arraySize; i++)
            {
                var el = wpProp.GetArrayElementAtIndex(i);
                var posProp = el.FindPropertyRelative("localPos");
                Vector3 localPos = posProp.vector3Value;
                Vector3 worldPos = path.transform.TransformPoint(localPos);

                // Label + sphere at the waypoint.
                Handles.color = new Color(0.2f, 0.8f, 1f, 1f);
                Handles.SphereHandleCap(0, worldPos, Quaternion.identity, 0.15f, EventType.Repaint);
                Handles.Label(worldPos + new Vector3(0.2f, 0.25f, 0f), $"WP {i}");

                // 3D position handle — lets the user drag in X/Y/Z.
                EditorGUI.BeginChangeCheck();
                Vector3 newWorld = Handles.PositionHandle(worldPos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(path, "Move Snake Waypoint");
                    Vector3 newLocal = path.transform.InverseTransformPoint(newWorld);
                    posProp.vector3Value = newLocal;
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                so.ApplyModifiedProperties();
                path.Rebuild();
                EditorUtility.SetDirty(path);
            }
        }

        void AppendWaypoint(WaypointSnakePath path, bool atEnd)
        {
            var so = new SerializedObject(path);
            var wpProp = so.FindProperty("waypoints");
            int oldSize = wpProp.arraySize;
            Undo.RecordObject(path, atEnd ? "Append Snake Waypoint" : "Insert Snake Waypoint");

            // Seed the new waypoint by duplicating the edge (or default offset if empty).
            Vector3 seedLocal = Vector3.zero;
            float   seedSpeed = 2f;
            if (oldSize >= 1)
            {
                int ref1 = atEnd ? oldSize - 1 : 0;
                int ref2 = atEnd ? Mathf.Max(0, oldSize - 2) : Mathf.Min(oldSize - 1, 1);
                var p1 = wpProp.GetArrayElementAtIndex(ref1).FindPropertyRelative("localPos").vector3Value;
                var p2 = wpProp.GetArrayElementAtIndex(ref2).FindPropertyRelative("localPos").vector3Value;
                seedLocal = p1 + (p1 - p2);
                seedSpeed = wpProp.GetArrayElementAtIndex(ref1).FindPropertyRelative("speedAfter").floatValue;
            }

            int insertAt = atEnd ? oldSize : 0;
            wpProp.InsertArrayElementAtIndex(insertAt);
            var el = wpProp.GetArrayElementAtIndex(insertAt);
            el.FindPropertyRelative("localPos").vector3Value = seedLocal;
            el.FindPropertyRelative("speedAfter").floatValue = seedSpeed;

            so.ApplyModifiedProperties();
            path.Rebuild();
            EditorUtility.SetDirty(path);
            SceneView.RepaintAll();
        }
    }
}
