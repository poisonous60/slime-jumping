using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms.Snow;

namespace SlimeJumping.Platforms.Snow.EditorScripts
{
    [CustomEditor(typeof(SnowField))]
    public sealed class SnowFieldEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var sf = (SnowField)target;
            var mesh = sf.sourceMeshFilter != null ? sf.sourceMeshFilter.sharedMesh : null;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(mesh == null))
            {
                if (GUILayout.Button("Rebuild Now"))
                {
                    sf.RebuildNow();
                    EditorUtility.SetDirty(sf);
                }
            }

            if (GUILayout.Button("Rebuild All SnowFields In Scene"))
            {
                RebuildAllInScene();
            }

            // Auto-rebuild watcher: when the host mesh changes (different asset assigned, or
            // an in-place edit), poll a hash on inspector repaint and surface a reminder.
            if (mesh != null)
            {
                int currentHash = SnowField.ComputeHostMeshHash(mesh);
                if (currentHash != sf.lastHostMeshHash)
                {
                    EditorGUILayout.HelpBox(
                        "Host mesh changed. Click Rebuild Now to sync the snow.",
                        MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No source MeshFilter resolved. Add a MeshFilter to this GameObject (or a child) with a sharedMesh.",
                    MessageType.Warning);
            }
        }

        [MenuItem("Tools/SlimeJumping/Snow/Rebuild Selected")]
        public static void RebuildSelected()
        {
            int n = 0;
            foreach (var go in Selection.gameObjects)
            {
                var sf = go.GetComponent<SnowField>();
                if (sf == null) continue;
                sf.RebuildNow();
                EditorUtility.SetDirty(sf);
                n++;
            }
            Debug.Log($"[Snow] Rebuilt {n} SnowField(s).");
        }

        [MenuItem("Tools/SlimeJumping/Snow/Rebuild All In Scene")]
        public static void RebuildAllInScene()
        {
            var fields = Object.FindObjectsByType<SnowField>(FindObjectsSortMode.None);
            foreach (var sf in fields)
            {
                sf.RebuildNow();
                EditorUtility.SetDirty(sf);
            }
            Debug.Log($"[Snow] Rebuilt {fields.Length} SnowField(s) in scene.");
        }
    }
}
