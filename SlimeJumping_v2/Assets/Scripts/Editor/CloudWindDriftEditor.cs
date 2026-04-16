using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SlimeJumping.Background;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(CloudWindDrift))]
    public class CloudWindDriftEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var t = (CloudWindDrift)target;

            EditorGUILayout.Space(8);

            if (GUILayout.Button("Reset Distribution (random spread within bounds)", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(t.gameObject, "Reset Cloud Distribution");
                t.ResetDistribution();
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }
    }
}
