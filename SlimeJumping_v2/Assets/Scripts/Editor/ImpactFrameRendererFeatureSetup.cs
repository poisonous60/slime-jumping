using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SlimeJumping.EditorTools
{
    /// One-shot menu utility: adds the B&amp;W impact-frame FullScreenPassRendererFeature
    /// to Assets/Settings/UniversalRenderer.asset and wires it to
    /// Assets/Art/Materials/M_ImpactFrameGrayscale.mat at the "After Rendering Post
    /// Processing" injection point. Idempotent: re-running just reports "already wired".
    public static class ImpactFrameRendererFeatureSetup
    {
        private const string RendererPath = "Assets/Settings/UniversalRenderer.asset";
        private const string MaterialPath = "Assets/Art/Materials/M_ImpactFrameGrayscale.mat";
        private const string FeatureName  = "FullScreenPass_ImpactFrame";

        [MenuItem("Tools/SlimeJumping/Setup Impact Frame Renderer Feature")]
        public static void SetupMenu() => Setup(true);

        public static void Setup(bool verbose)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null)
            {
                Debug.LogError($"[ImpactFrameSetup] URP renderer not found at {RendererPath}");
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogError($"[ImpactFrameSetup] Material not found at {MaterialPath}");
                return;
            }

            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing is FullScreenPassRendererFeature fp &&
                    fp.passMaterial == material)
                {
                    if (verbose) Debug.Log("[ImpactFrameSetup] Already wired — no-op.");
                    return;
                }
            }

            var feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            feature.name = FeatureName;
            feature.passMaterial = material;
            feature.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            feature.hideFlags = HideFlags.HideInHierarchy;

            AssetDatabase.AddObjectToAsset(feature, rendererData);
            AssetDatabase.SaveAssets();

            long localId;
            string guid;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out guid, out localId))
            {
                Debug.LogError("[ImpactFrameSetup] Failed to resolve local file id for new feature.");
                Object.DestroyImmediate(feature, true);
                return;
            }

            var so = new SerializedObject(rendererData);
            var listProp = so.FindProperty("m_RendererFeatures");
            var mapProp  = so.FindProperty("m_RendererFeatureMap");
            if (listProp == null || mapProp == null)
            {
                Debug.LogError("[ImpactFrameSetup] Renderer data serialized fields missing (m_RendererFeatures / m_RendererFeatureMap).");
                Object.DestroyImmediate(feature, true);
                return;
            }

            int idx = listProp.arraySize;
            listProp.arraySize = idx + 1;
            listProp.GetArrayElementAtIndex(idx).objectReferenceValue = feature;

            mapProp.arraySize = idx + 1;
            mapProp.GetArrayElementAtIndex(idx).longValue = localId;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RendererPath);

            Debug.Log($"[ImpactFrameSetup] Added '{FeatureName}' to {RendererPath} (material={material.name}, injection=AfterRenderingPostProcessing). Feature count is now {rendererData.rendererFeatures.Count}.");
        }
    }
}
