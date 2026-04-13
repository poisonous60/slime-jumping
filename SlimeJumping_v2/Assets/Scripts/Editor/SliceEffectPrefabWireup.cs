#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.EditorTools
{
    public static class SliceEffectPrefabWireup
    {
        private const string PrefabPath = "Assets/Prebs/VFX/Slice_Hit.prefab";

        [MenuItem("SlimeJumping/VFX/Assign Slice SFX Clips")]
        public static void AssignClips()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[SliceEffectPrefabWireup] Prefab not found at {PrefabPath}");
                return;
            }

            var clips = new System.Collections.Generic.List<AudioClip>();
            for (int i = 0; i < 5; i++)
            {
                var c = AssetDatabase.LoadAssetAtPath<AudioClip>($"Assets/Sound/Slice/impactSoft_medium_00{i}.ogg");
                if (c != null) clips.Add(c);
            }
            if (clips.Count == 0)
            {
                Debug.LogError("[SliceEffectPrefabWireup] No clips found in Assets/Sound/Slice/");
                return;
            }

            var slice = prefab.GetComponent<SliceEffect>();
            if (slice == null)
            {
                Debug.LogError("[SliceEffectPrefabWireup] Prefab has no SliceEffect component");
                return;
            }

            var so = new SerializedObject(slice);
            var arr = so.FindProperty("hitClips");
            arr.arraySize = clips.Count;
            for (int i = 0; i < clips.Count; i++)
                arr.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(prefab);
            PrefabUtility.SavePrefabAsset(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SliceEffectPrefabWireup] Assigned {clips.Count} clips to {PrefabPath}");
        }
    }
}
#endif
