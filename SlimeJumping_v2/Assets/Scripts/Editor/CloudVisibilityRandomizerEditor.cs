using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SlimeJumping.Background;

namespace SlimeJumping.Editor
{
    [CustomEditor(typeof(CloudVisibilityRandomizer))]
    public class CloudVisibilityRandomizerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var target = (CloudVisibilityRandomizer)this.target;

            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Randomize", GUILayout.Height(28)))
                {
                    Apply(target, () => target.Randomize(), "Randomize Cloud Visibility");
                }

                if (GUILayout.Button("All On", GUILayout.Height(28)))
                {
                    Apply(target, () => target.SetAllActive(true), "Show All Clouds");
                }

                if (GUILayout.Button("All Off", GUILayout.Height(28)))
                {
                    Apply(target, () => target.SetAllActive(false), "Hide All Clouds");
                }
            }
        }

        static void Apply(CloudVisibilityRandomizer t, System.Action mutate, string undoLabel)
        {
            t.CollectLeaves();
            if (t.Leaves.Count == 0)
            {
                Debug.LogWarning($"[{nameof(CloudVisibilityRandomizer)}] No leaves found at the configured depth.");
                return;
            }

            // Recording each leaf captures its m_IsActive serialized field for Undo.
            foreach (var leaf in t.Leaves)
            {
                Undo.RecordObject(leaf, undoLabel);
            }

            mutate();

            foreach (var leaf in t.Leaves)
            {
                EditorUtility.SetDirty(leaf);
            }
            EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
        }
    }
}
