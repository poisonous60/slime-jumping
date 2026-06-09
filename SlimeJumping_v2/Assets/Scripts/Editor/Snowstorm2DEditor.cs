using UnityEditor;
using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(Snowstorm2D))]
    public sealed class Snowstorm2DEditor : UnityEditor.Editor
    {
        const string ShelterPrefix = "WindShelterArea";

        SerializedProperty windShelterAreasProp;

        void OnEnable()
        {
            windShelterAreasProp = serializedObject.FindProperty("windShelterAreas");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "m_Script", "windShelterAreas");
            DrawWindShelterAreas();

            serializedObject.ApplyModifiedProperties();
        }

        void DrawWindShelterAreas()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Wind Shelter Areas", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use these colliders for interiors or behind-wall pockets where snowstorm visuals remain visible but slime wind push is blocked.",
                MessageType.Info);

            if (windShelterAreasProp == null)
            {
                EditorGUILayout.HelpBox("Missing serialized property: windShelterAreas", MessageType.Warning);
                return;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < windShelterAreasProp.arraySize; i++)
            {
                SerializedProperty element = windShelterAreasProp.GetArrayElementAtIndex(i);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(element, new GUIContent("Element " + i));
                    if (GUILayout.Button("-", GUILayout.Width(24f)))
                    {
                        RemoveShelterAt(i);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUI.indentLevel--;

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Box", GUILayout.Width(80f)))
                {
                    CreateShelter(usePolygon: false);
                    GUIUtility.ExitGUI();
                }
                if (GUILayout.Button("+ Polygon", GUILayout.Width(90f)))
                {
                    CreateShelter(usePolygon: true);
                    GUIUtility.ExitGUI();
                }
            }
        }

        void CreateShelter(bool usePolygon)
        {
            serializedObject.ApplyModifiedProperties();

            var storm = (Snowstorm2D)target;
            GameObject go = new GameObject(NextShelterName(storm.transform));
            Undo.RegisterCreatedObjectUndo(go, "Create Wind Shelter Area");
            Undo.SetTransformParent(go.transform, storm.transform, "Parent Wind Shelter Area");

            go.transform.localPosition = DefaultShelterLocalPosition(storm);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            Collider2D collider;
            if (usePolygon)
            {
                var poly = go.AddComponent<PolygonCollider2D>();
                poly.isTrigger = true;
                poly.points = DefaultPolygonPoints(storm);
                collider = poly;
            }
            else
            {
                var box = go.AddComponent<BoxCollider2D>();
                box.isTrigger = true;
                box.size = DefaultShelterSize(storm);
                collider = box;
            }

            serializedObject.Update();
            int index = windShelterAreasProp.arraySize;
            windShelterAreasProp.InsertArrayElementAtIndex(index);
            SerializedProperty element = windShelterAreasProp.GetArrayElementAtIndex(index);
            element.objectReferenceValue = collider;
            serializedObject.ApplyModifiedProperties();

            Selection.activeGameObject = go;
            EditorUtility.SetDirty(storm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(storm.gameObject.scene);
        }

        void RemoveShelterAt(int index)
        {
            SerializedProperty element = windShelterAreasProp.GetArrayElementAtIndex(index);
            Collider2D collider = element.objectReferenceValue as Collider2D;
            GameObject generatedObject = GetGeneratedShelterObject(collider);

            element.objectReferenceValue = null;
            windShelterAreasProp.DeleteArrayElementAtIndex(index);
            serializedObject.ApplyModifiedProperties();

            if (generatedObject != null)
                Undo.DestroyObjectImmediate(generatedObject);

            var storm = (Snowstorm2D)target;
            EditorUtility.SetDirty(storm);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(storm.gameObject.scene);
        }

        GameObject GetGeneratedShelterObject(Collider2D collider)
        {
            if (collider == null) return null;

            var storm = (Snowstorm2D)target;
            Transform colliderTransform = collider.transform;
            if (colliderTransform.parent != storm.transform) return null;
            if (!IsGeneratedShelterName(colliderTransform.name)) return null;
            return collider.gameObject;
        }

        static bool IsGeneratedShelterName(string objectName)
        {
            if (string.IsNullOrEmpty(objectName)) return false;
            string prefix = ShelterPrefix + "_";
            if (!objectName.StartsWith(prefix)) return false;

            for (int i = prefix.Length; i < objectName.Length; i++)
            {
                if (!char.IsDigit(objectName[i])) return false;
            }
            return objectName.Length > prefix.Length;
        }

        static string NextShelterName(Transform parent)
        {
            int index = 0;
            while (parent.Find(ShelterPrefix + "_" + index) != null)
                index++;
            return ShelterPrefix + "_" + index;
        }

        static Vector2 DefaultShelterSize(Snowstorm2D storm)
        {
            Collider2D stormCol = storm.GetComponent<Collider2D>();
            if (stormCol == null) return new Vector2(4f, 3f);
            Vector2 worldSize = stormCol.bounds.size;
            return new Vector2(
                Mathf.Max(1f, worldSize.x * 0.25f),
                Mathf.Max(1f, worldSize.y * 0.25f));
        }

        static Vector3 DefaultShelterLocalPosition(Snowstorm2D storm)
        {
            Collider2D stormCol = storm.GetComponent<Collider2D>();
            return stormCol != null
                ? storm.transform.InverseTransformPoint(stormCol.bounds.center)
                : Vector3.zero;
        }

        static Vector2[] DefaultPolygonPoints(Snowstorm2D storm)
        {
            Vector2 size = DefaultShelterSize(storm);
            float hx = size.x * 0.5f;
            float hy = size.y * 0.5f;
            return new[]
            {
                new Vector2(-hx, -hy),
                new Vector2(hx, -hy),
                new Vector2(hx, hy),
                new Vector2(-hx, hy)
            };
        }
    }
}
