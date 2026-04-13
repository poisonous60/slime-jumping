#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.EditorTools
{
    public static class SliceHazardSceneWireup
    {
        private const string PrefabPath = "Assets/Prebs/VFX/Slice_Hit.prefab";

        [MenuItem("SlimeJumping/VFX/Wire Slice FX into Scene Hazards")]
        public static void Wire()
        {
            var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefabGo == null)
            {
                Debug.LogError($"[SliceHazardSceneWireup] Prefab not found: {PrefabPath}");
                return;
            }
            var sliceEffect = prefabGo.GetComponent<SliceEffect>();
            if (sliceEffect == null)
            {
                Debug.LogError("[SliceHazardSceneWireup] Prefab missing SliceEffect component");
                return;
            }

            int wiredKnife = 0, wiredSaw = 0;

            foreach (var knife in Object.FindObjectsByType<KnifeHazard>(FindObjectsSortMode.None))
            {
                WireCommon(knife.gameObject, knife, sliceEffect);
                wiredKnife++;
            }
            foreach (var saw in Object.FindObjectsByType<SawBladeHazard>(FindObjectsSortMode.None))
            {
                WireCommon(saw.gameObject, saw, sliceEffect);
                wiredSaw++;
            }

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[SliceHazardSceneWireup] Wired {wiredKnife} Knife(s) and {wiredSaw} Saw(s).");
        }

        private static void WireCommon(GameObject host, Component hazard, SliceEffect sliceEffectPrefab)
        {
            ImpulseEmitter emitter = host.GetComponent<ImpulseEmitter>();
            if (emitter == null)
            {
                if (host.GetComponent<CinemachineImpulseSource>() == null)
                    host.AddComponent<CinemachineImpulseSource>();
                emitter = host.AddComponent<ImpulseEmitter>();
            }

            var so = new SerializedObject(hazard);
            var slicePrefabProp = so.FindProperty("sliceEffectPrefab");
            if (slicePrefabProp != null && slicePrefabProp.objectReferenceValue == null)
                slicePrefabProp.objectReferenceValue = sliceEffectPrefab;

            var emitterProp = so.FindProperty("impulseEmitter");
            if (emitterProp != null && emitterProp.objectReferenceValue == null)
                emitterProp.objectReferenceValue = emitter;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(hazard);
        }
    }
}
#endif
