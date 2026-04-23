using System.IO;
using Unified.UniversalBlur.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimeJumping.EditorTools
{
    public static class BackgroundBlurSetup
    {
        const string RendererAssetPath = "Assets/Settings/BackgroundBlurRenderer.asset";
        const string BackgroundLayerName = "BackgroundBlur";
        const string BackgroundCameraName = "BackgroundBlurCamera";

        [MenuItem("Tools/SlimeJumping/Setup Background Blur Camera")]
        public static void Setup()
        {
            int bgLayer = LayerMask.NameToLayer(BackgroundLayerName);
            if (bgLayer < 0)
            {
                Debug.LogError($"[BackgroundBlurSetup] Layer '{BackgroundLayerName}' does not exist. Add it in Project Settings > Tags and Layers.");
                return;
            }

            var rendererData = EnsureBlurRendererData();
            if (rendererData == null) return;

            int rendererIndex = EnsureRegisteredInActiveUrpAsset(rendererData);
            if (rendererIndex < 0) return;

            var mainCam = UnityEngine.Camera.main;
            if (mainCam == null)
            {
                Debug.LogError("[BackgroundBlurSetup] No camera tagged MainCamera found in the active scene.");
                return;
            }

            SetupCameraStack(mainCam, bgLayer, rendererIndex);

            EditorSceneManager.MarkSceneDirty(mainCam.gameObject.scene);
            Debug.Log($"[BackgroundBlurSetup] Done. Renderer '{rendererData.name}' at index {rendererIndex}. BackgroundBlurCamera configured.");
        }

        static ScriptableRendererData EnsureBlurRendererData()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath);
            if (existing != null) return existing;

            var dir = Path.GetDirectoryName(RendererAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Debug.LogError($"[BackgroundBlurSetup] Folder '{dir}' missing.");
                return null;
            }

            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            data.name = "BackgroundBlurRenderer";
            AssetDatabase.CreateAsset(data, RendererAssetPath);

            AddRendererFeature(data, typeof(UniversalBlurFeature));

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RendererAssetPath);
            return AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererAssetPath);
        }

        // Mirrors URP's ScriptableRendererDataEditor.AddComponent so both m_RendererFeatures
        // and m_RendererFeatureMap stay in sync.
        static void AddRendererFeature(ScriptableRendererData target, System.Type featureType)
        {
            var feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(featureType);
            feature.name = featureType.Name;
            AssetDatabase.AddObjectToAsset(feature, target);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(target);
            var featuresProp = so.FindProperty("m_RendererFeatures");
            var mapProp = so.FindProperty("m_RendererFeatureMap");

            featuresProp.arraySize++;
            featuresProp.GetArrayElementAtIndex(featuresProp.arraySize - 1).objectReferenceValue = feature;

            mapProp.arraySize++;
            mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue = localId;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        static int EnsureRegisteredInActiveUrpAsset(ScriptableRendererData rendererData)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                Debug.LogError("[BackgroundBlurSetup] Active render pipeline is not URP.");
                return -1;
            }

            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
                    return i;
            }

            int newIndex = list.arraySize;
            list.arraySize++;
            list.GetArrayElementAtIndex(newIndex).objectReferenceValue = rendererData;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            return newIndex;
        }

        static void SetupCameraStack(UnityEngine.Camera mainCam, int bgLayer, int rendererIndex)
        {
            var mainData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (mainData == null) mainData = mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            UnityEngine.Camera bgCam = FindChildCamera(mainCam.transform, BackgroundCameraName);
            if (bgCam == null)
            {
                var go = new GameObject(BackgroundCameraName);
                Undo.RegisterCreatedObjectUndo(go, "Create Background Blur Camera");
                go.transform.SetParent(mainCam.transform, false);
                bgCam = go.AddComponent<UnityEngine.Camera>();
            }

            var bgData = bgCam.GetComponent<UniversalAdditionalCameraData>();
            if (bgData == null) bgData = bgCam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            Undo.RecordObject(bgCam, "Configure Background Blur Camera");
            Undo.RecordObject(bgData, "Configure Background Blur Camera");
            Undo.RecordObject(mainCam, "Configure Main Camera For Stack");
            Undo.RecordObject(mainData, "Configure Main Camera For Stack");

            // Match projection from main cam (keeps parallax/scale identical).
            bgCam.orthographic = mainCam.orthographic;
            bgCam.orthographicSize = mainCam.orthographicSize;
            bgCam.fieldOfView = mainCam.fieldOfView;
            bgCam.nearClipPlane = mainCam.nearClipPlane;
            bgCam.farClipPlane = mainCam.farClipPlane;

            bgCam.cullingMask = 1 << bgLayer;
            bgCam.clearFlags = CameraClearFlags.SolidColor;
            bgCam.backgroundColor = mainCam.backgroundColor;
            bgCam.depth = mainCam.depth - 1;
            bgCam.allowMSAA = mainCam.allowMSAA;
            bgCam.allowHDR = mainCam.allowHDR;

            bgData.renderType = CameraRenderType.Base;
            bgData.SetRenderer(rendererIndex);
            bgData.renderPostProcessing = false;
            bgData.renderShadows = false;
            bgData.cameraStack.Clear();
            bgData.cameraStack.Add(mainCam);

            // Main camera becomes the Overlay on top; it must not render the background layer.
            int bgMask = 1 << bgLayer;
            mainCam.cullingMask &= ~bgMask;
            mainData.renderType = CameraRenderType.Overlay;
            // renderPostProcessing left as-is so existing PostFX volumes still apply.
        }

        static UnityEngine.Camera FindChildCamera(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child.GetComponent<UnityEngine.Camera>();
            }
            return null;
        }
    }
}
