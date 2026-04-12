using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    public static class JumpscareTestSetup
    {
        [MenuItem("SlimeJumping/Setup Jumpscare Test")]
        public static void Setup()
        {
            var root = new GameObject("JumpscareTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Jumpscare Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            // Jumpscare prefab + trigger
            var prefabPath = "Assets/Prebs/building/Jumpscare.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject scareHost;
            if (prefab != null)
            {
                scareHost = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                PrefabUtility.UnpackPrefabInstance(scareHost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                scareHost.transform.position = new Vector3(baseX + 3f, baseY + 0.5f, 0f);
                foreach (var c in scareHost.GetComponentsInChildren<Collider2D>(true))
                    Object.DestroyImmediate(c);
                foreach (var c in scareHost.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
                foreach (var r in scareHost.GetComponentsInChildren<MeshRenderer>(true))
                    Object.DestroyImmediate(r);
                foreach (var f in scareHost.GetComponentsInChildren<MeshFilter>(true))
                    Object.DestroyImmediate(f);
            }
            else
            {
                Debug.LogWarning($"[JumpscareTestSetup] {prefabPath} not found — using empty host.");
                scareHost = new GameObject("JumpscareHost");
                scareHost.transform.parent = root.transform;
                scareHost.transform.position = new Vector3(baseX + 3f, baseY + 0.5f, 0f);
            }

            // Trigger collider on a child. Default layer (0) so it collides with slime's Default-layer physics collider.
            var triggerGo = new GameObject("Trigger");
            triggerGo.transform.SetParent(scareHost.transform, false);
            triggerGo.transform.localPosition = Vector3.zero;
            triggerGo.layer = 0;
            var triggerCol = triggerGo.AddComponent<BoxCollider2D>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector2(3f, 3f);
            var trigger = triggerGo.AddComponent<JumpscareTrigger>();

            // Canvas with image + audio + impulse
            var canvasGo = CreateOverlayCanvas("JumpscareCanvas", 9999, root.transform);
            var jumpscareCanvas = canvasGo.AddComponent<JumpscareCanvasController>();
            WireCanvas(canvasGo, jumpscareCanvas);

            // Wire trigger → canvas
            var triggerSo = new SerializedObject(trigger);
            triggerSo.FindProperty("canvas").objectReferenceValue = jumpscareCanvas;
            triggerSo.FindProperty("oneShot").boolValue = false; // repeat for testing
            triggerSo.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Jumpscare Test setup complete!\n" +
                "- Trigger is 2x2 on scareHost. Walk the slime into it.\n" +
                "- White fullscreen fade + ImpulseEmitter force 1.5.\n" +
                "- oneShot=false for testing; toggle in Inspector if needed.\n" +
                "- Assign scareImage / scareSound on the Trigger in Inspector for full effect.");
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

            // Fullscreen image child
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

            // AudioSource (2D)
            var audio = canvasGo.AddComponent<AudioSource>();
            audio.spatialBlend = 0f;
            audio.playOnAwake = false;

            // ImpulseSource + Emitter
            canvasGo.AddComponent<CinemachineImpulseSource>();
            canvasGo.AddComponent<ImpulseEmitter>();

            return canvasGo;
        }

        static void WireCanvas(GameObject canvasGo, FullscreenOverlayCanvas controller)
        {
            var so = new SerializedObject(controller);
            so.FindProperty("fullscreenImage").objectReferenceValue =
                canvasGo.GetComponentInChildren<Image>();
            so.FindProperty("audioSource").objectReferenceValue =
                canvasGo.GetComponent<AudioSource>();
            so.FindProperty("impulseEmitter").objectReferenceValue =
                canvasGo.GetComponent<ImpulseEmitter>();
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
