using UnityEngine;
using UnityEditor;

namespace SlimeJumping.Editor
{
    /// <summary>Assigns Mat_SlimeFaceSlice to Slime.prefab face sprites so wound planes can clip them. Idempotent, edit mode only.</summary>
    public static class SlimeFaceSliceSetup
    {
        private const string ShaderName = "SlimeJumping/SlimeFaceSlice";
        private const string MatPath = "Assets/Art/Shader/Mat_SlimeFaceSlice.mat";
        private const string SlimePrefabPath = "Assets/Prebs/Slime.prefab";

        [MenuItem("SlimeJumping/Effects/Wire Face Slice Material")]
        public static void Wire()
        {
            var mat = GetOrCreateMaterial();
            if (mat == null) return;

            var root = PrefabUtility.LoadPrefabContents(SlimePrefabPath);
            if (root == null)
            {
                Debug.LogError("[FaceSlice] Slime.prefab not found at " + SlimePrefabPath);
                return;
            }
            try
            {
                int n = 0;
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (!HasAncestorNamed(sr.transform, "Face")) continue;
                    sr.sharedMaterial = mat;
                    n++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, SlimePrefabPath);
                Debug.Log($"[FaceSlice] Assigned {mat.name} to {n} face SpriteRenderer(s) in Slime.prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool HasAncestorNamed(Transform t, string name)
        {
            for (var p = t; p != null; p = p.parent)
                if (p.name == name) return true;
            return false;
        }

        private static Material GetOrCreateMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (existing != null) return existing;

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[FaceSlice] Shader not found (still compiling?): " + ShaderName);
                return null;
            }
            var mat = new Material(shader) { name = "Mat_SlimeFaceSlice" };
            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
