using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    public static class CameraFlashTestSetup
    {
        [MenuItem("SlimeJumping/Setup Camera Flash Test")]
        public static void Setup()
        {
            var root = new GameObject("CameraFlashTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Camera Flash Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            // studio_camera prefab host
            var prefabPath = "Assets/Prebs/building/studio_camera.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject flashHost;
            if (prefab != null)
            {
                flashHost = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                PrefabUtility.UnpackPrefabInstance(flashHost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                flashHost.name = "StudioCamera";
                flashHost.transform.position = new Vector3(baseX + 3f, baseY + 2f, 0f);
                foreach (var c in flashHost.GetComponentsInChildren<Collider2D>(true))
                    Object.DestroyImmediate(c);
                foreach (var c in flashHost.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                // Keep MeshRenderer/MeshFilter — studio_camera mesh is the visual reference.
            }
            else
            {
                Debug.LogWarning($"[CameraFlashTestSetup] {prefabPath} not found — using empty host.");
                flashHost = new GameObject("StudioCamera");
                flashHost.transform.parent = root.transform;
                flashHost.transform.position = new Vector3(baseX + 3f, baseY + 2f, 0f);
            }

            // Flash hazard on host
            var hazard = flashHost.AddComponent<CameraFlashHazard>();
            var audio = flashHost.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;
            flashHost.AddComponent<CinemachineImpulseSource>();
            var emitter = flashHost.AddComponent<ImpulseEmitter>();

            // Hazard area child
            var areaGo = new GameObject("HazardArea");
            areaGo.transform.parent = flashHost.transform;
            areaGo.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            var areaCol = areaGo.AddComponent<BoxCollider2D>();
            areaCol.isTrigger = true;
            areaCol.size = new Vector2(4f, 3f);
            var area = areaGo.AddComponent<CameraFlashHazardArea>();
            var areaSo = new SerializedObject(area);
            areaSo.FindProperty("owner").objectReferenceValue = hazard;
            areaSo.ApplyModifiedProperties();

            // FlashCanvas
            var canvasGo = CreateOverlayCanvas("FlashCanvas", 9998, root.transform);
            var flashCanvas = canvasGo.AddComponent<FlashCanvasController>();
            WireCanvas(canvasGo, flashCanvas);

            // Wire hazard
            var hazardSo = new SerializedObject(hazard);
            hazardSo.FindProperty("flashCanvas").objectReferenceValue = flashCanvas;
            hazardSo.FindProperty("shutterAudio").objectReferenceValue = audio;
            hazardSo.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            hazardSo.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Camera Flash Test setup complete!\n" +
                "- HazardArea 4x3 below studio camera. Slime inside at flash moment gets stunned.\n" +
                "- flashInterval 4s. Assign shutterClip on StudioCamera CameraFlashHazard for audio.\n" +
                "- bulbLight Light2D optional — assign in Inspector if URP 2D Renderer configured.");
        }

        static GameObject CreateOverlayCanvas(string name, int sortingOrder, Transform parent)
        {
            var canvasGo = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(parent, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var imgGo = new GameObject("FullscreenImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var rect = imgGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            var img = imgGo.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;

            // FlashCanvas는 audio/impulse를 본체에서 처리하므로 Canvas에는 audio/impulse 미부착.
            return canvasGo;
        }

        static void WireCanvas(GameObject canvasGo, FullscreenOverlayCanvas controller)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("fullscreenImage").objectReferenceValue =
                canvasGo.GetComponentInChildren<Image>();
            // audioSource/impulseEmitter는 FlashCanvas에서 사용 안 함. 비워둠.
            so.ApplyModifiedProperties();
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;
            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;
            return go;
        }
    }
}
