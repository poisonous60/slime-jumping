using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    /// On scene/prefab save, warns about AudioSource components with null
    /// Output AudioMixerGroup. Such sources route direct to Master and bypass
    /// the option sliders (BGM / Player SFX / World SFX / UI). Warning only —
    /// save proceeds regardless. Scenes are only inspected if already loaded
    /// (no forced loads, keeps Ctrl+S fast).
    public class AudioSourceMixerGroupValidator : UnityEditor.AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                var path = paths[i];
                if (path.EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                    ValidateScene(path);
                else if (path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    ValidatePrefab(path);
            }
            return paths;
        }

        static void ValidateScene(string path)
        {
            var scene = EditorSceneManager.GetSceneByPath(path);
            if (!scene.IsValid() || !scene.isLoaded) return;
            foreach (var root in scene.GetRootGameObjects())
                CheckRecursive(root, path);
        }

        static void ValidatePrefab(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null) CheckRecursive(go, path);
        }

        static void CheckRecursive(GameObject root, string assetPath)
        {
            var sources = root.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                if (src.outputAudioMixerGroup != null) continue;
                Debug.LogWarning(
                    $"[AudioValidator] {assetPath}:{GetHierarchyPath(src.transform)} " +
                    "has AudioSource with null Output AudioMixerGroup — routes direct " +
                    "to Master, bypasses volume sliders.", src);
            }
        }

        static string GetHierarchyPath(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cur = t.parent;
            while (cur != null)
            {
                sb.Insert(0, cur.name + "/");
                cur = cur.parent;
            }
            return sb.ToString();
        }
    }
}
