using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(DeformablePlatform))]
    public class DeformablePlatformEditor : UnityEditor.Editor
    {
        static readonly Color LeftHandleColor  = new Color(1.0f, 0.85f, 0.25f, 1f);
        static readonly Color RightHandleColor = new Color(0.25f, 0.95f, 1.0f, 1f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var dp = (DeformablePlatform)target;
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Shape Presets (reads genWidth / genHeight / genSegmentCount above)",
                                       EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rectangle")) ApplyPreset(dp, dp.GenerateRectangle);
                if (GUILayout.Button("Taper"))     ApplyPreset(dp, dp.GenerateTaper);
                if (GUILayout.Button("Hourglass")) ApplyPreset(dp, dp.GenerateHourglass);
                if (GUILayout.Button("Dome Top"))  ApplyPreset(dp, dp.GenerateDomeTop);
            }

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Rebuild Runtime"))
                {
                    Undo.RecordObject(dp, "Rebuild DeformablePlatform");
                    dp.BuildRuntime();
                    EditorUtility.SetDirty(dp);
                }
                if (GUILayout.Button("Subdivide"))
                {
                    Undo.RecordObject(dp, "Subdivide DeformablePlatform");
                    dp.Subdivide();
                    dp.BuildRuntime();
                    EditorUtility.SetDirty(dp);
                }
            }
            EditorGUILayout.HelpBox(
                "Scene view: drag yellow/cyan dots to move chain points.\n" +
                "Hold Shift and click on a chain edge to insert a new level.\n" +
                "Hold Ctrl/Cmd and click a dot to delete its level.",
                MessageType.None);
        }

        static void ApplyPreset(DeformablePlatform dp, System.Action generator)
        {
            Undo.RecordObject(dp, "Apply DeformablePlatform preset");
            generator();
            dp.BuildRuntime();
            EditorUtility.SetDirty(dp);
        }

        void OnSceneGUI()
        {
            var dp = (DeformablePlatform)target;
            var so = new SerializedObject(dp);
            var listProp = so.FindProperty("restLevels");
            if (listProp == null || listProp.arraySize == 0) return;

            Transform t = dp.transform;
            Event e = Event.current;

            // --- Drag existing points ---
            for (int i = 0; i < listProp.arraySize; i++)
            {
                var elem = listProp.GetArrayElementAtIndex(i);
                var yProp = elem.FindPropertyRelative("y");
                var leftProp = elem.FindPropertyRelative("leftX");
                var rightProp = elem.FindPropertyRelative("rightX");

                float y = yProp.floatValue;
                float lx = leftProp.floatValue;
                float rx = rightProp.floatValue;

                Vector3 leftWorld  = t.TransformPoint(new Vector3(lx, y, 0f));
                Vector3 rightWorld = t.TransformPoint(new Vector3(rx, y, 0f));
                float szL = HandleUtility.GetHandleSize(leftWorld)  * 0.07f;
                float szR = HandleUtility.GetHandleSize(rightWorld) * 0.07f;

                // Ctrl-click → delete the whole level (only if enough remain).
                if (e.control || e.command)
                {
                    if (listProp.arraySize > 3)
                    {
                        Handles.color = Color.red;
                        if (Handles.Button(leftWorld,  Quaternion.identity, szL, szL, Handles.SphereHandleCap) ||
                            Handles.Button(rightWorld, Quaternion.identity, szR, szR, Handles.SphereHandleCap))
                        {
                            listProp.DeleteArrayElementAtIndex(i);
                            so.ApplyModifiedProperties();
                            dp.BuildRuntime();
                            return;
                        }
                    }
                    continue;
                }

                // Left handle
                EditorGUI.BeginChangeCheck();
                Handles.color = LeftHandleColor;
                Vector3 newLeftWorld = Handles.FreeMoveHandle(
                    leftWorld, szL, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 local = t.InverseTransformPoint(newLeftWorld);
                    leftProp.floatValue = local.x;
                    yProp.floatValue    = local.y;
                    so.ApplyModifiedProperties();
                    dp.BuildRuntime();
                }

                // Right handle
                EditorGUI.BeginChangeCheck();
                Handles.color = RightHandleColor;
                Vector3 newRightWorld = Handles.FreeMoveHandle(
                    rightWorld, szR, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Vector3 local = t.InverseTransformPoint(newRightWorld);
                    rightProp.floatValue = local.x;
                    yProp.floatValue     = local.y;
                    so.ApplyModifiedProperties();
                    dp.BuildRuntime();
                }
            }

            // --- Shift-click insert new level at midpoint of nearest chain edge ---
            if (e.shift && e.type == EventType.MouseDown && e.button == 0)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                Plane p = new Plane(t.forward, t.position);
                if (p.Raycast(ray, out float dist))
                {
                    Vector3 hitWorld = ray.origin + ray.direction * dist;
                    Vector3 hitLocal = t.InverseTransformPoint(hitWorld);
                    int insertBefore = FindInsertionIndex(dp, hitLocal.y);
                    if (insertBefore > 0 && insertBefore < dp.RestLevelCount)
                    {
                        var a = dp.GetRestLevel(insertBefore - 1);
                        var b = dp.GetRestLevel(insertBefore);
                        float f = Mathf.InverseLerp(a.y, b.y, hitLocal.y);
                        var mid = new DeformablePlatform.RestLevel
                        {
                            y      = Mathf.Lerp(a.y,      b.y,      f),
                            leftX  = Mathf.Lerp(a.leftX,  b.leftX,  f),
                            rightX = Mathf.Lerp(a.rightX, b.rightX, f),
                        };
                        listProp.InsertArrayElementAtIndex(insertBefore);
                        var newElem = listProp.GetArrayElementAtIndex(insertBefore);
                        newElem.FindPropertyRelative("y").floatValue      = mid.y;
                        newElem.FindPropertyRelative("leftX").floatValue  = mid.leftX;
                        newElem.FindPropertyRelative("rightX").floatValue = mid.rightX;
                        so.ApplyModifiedProperties();
                        dp.BuildRuntime();
                        e.Use();
                    }
                }
            }
        }

        // restLevels is sorted top→bottom (Y descending) by BuildRuntime.
        // Return the index of the first level whose Y < hitY; 0 or Count means "above/below all".
        static int FindInsertionIndex(DeformablePlatform dp, float hitY)
        {
            for (int i = 0; i < dp.RestLevelCount; i++)
                if (dp.GetRestLevel(i).y < hitY) return i;
            return dp.RestLevelCount;
        }
    }
}
