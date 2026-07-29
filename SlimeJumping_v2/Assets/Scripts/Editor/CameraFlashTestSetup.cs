using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    public static class CameraFlashTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Camera Flash Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("CameraFlashTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var root = new GameObject("CameraFlashTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Camera Flash Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(16f, 1f, 1f));

            // StudioCamera group — identity transform wrapper so 2D children aren't affected
            // by the prefab's 3D rotation (studio_camera.prefab has 270° X rotation for model orientation).
            var flashHost = new GameObject("StudioCamera");
            flashHost.transform.SetParent(root.transform, false);
            flashHost.transform.position = new Vector3(baseX + 3f, baseY + 2f, 0f);

            var prefabPath = "Assets/Prebs/building/studio_camera.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, flashHost.transform);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                visual.name = "Visual";
                visual.transform.localPosition = Vector3.zero;
                foreach (var c in visual.GetComponentsInChildren<Collider2D>(true))
                    Object.DestroyImmediate(c);
                foreach (var c in visual.GetComponentsInChildren<Collider>(true))
                    Object.DestroyImmediate(c);
            }
            else
            {
                Debug.LogWarning($"[CameraFlashTestSetup] {prefabPath} not found — no visual mesh.");
            }

            var audio = flashHost.AddComponent<AudioSource>();
            audio.spatialBlend = 1f;
            audio.playOnAwake = false;

            flashHost.AddComponent<CinemachineImpulseSource>();
            var emitter = flashHost.AddComponent<ImpulseEmitter>();

            // Light2D only renders when the URP 2D Renderer is configured; optional otherwise.
            var bulbGo = new GameObject("BulbLight");
            bulbGo.transform.SetParent(flashHost.transform, false);
            bulbGo.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            var bulb = bulbGo.AddComponent<Light2D>();
            bulb.lightType = Light2D.LightType.Point;
            bulb.color = new Color(1f, 0.95f, 0.8f);
            bulb.intensity = 0f;
            bulb.pointLightOuterRadius = 3f;
            bulb.pointLightInnerRadius = 0.3f;

            // Trigger child — JumpscareTrigger 패턴 (self-contained, OnTriggerEnter2D).
            var triggerGo = new GameObject("Trigger");
            triggerGo.transform.SetParent(flashHost.transform, false);
            triggerGo.transform.localPosition = new Vector3(0f, -1.5f, 0f);
            triggerGo.layer = 0;
            var triggerCol = triggerGo.AddComponent<BoxCollider2D>();
            triggerCol.isTrigger = true;
            triggerCol.size = new Vector2(4f, 3f);
            triggerCol.offset = Vector2.zero;
            var hazard = triggerGo.AddComponent<CameraFlashHazard>();

            var canvasRoot = FullscreenCanvasSetupUtility.GetOrCreateRoot();
            var effectGo = FullscreenCanvasSetupUtility.GetOrCreateEffectChild(canvasRoot, "FlashEffect");
            var img = FullscreenCanvasSetupUtility.AddFullscreenImage(effectGo, new Color(1f, 1f, 1f, 0.5f));
            BuildViewfinder(effectGo.transform);

            var flashCanvas = effectGo.GetComponent<FlashCanvasController>();
            if (flashCanvas == null) flashCanvas = effectGo.AddComponent<FlashCanvasController>();
            var flashSo = new SerializedObject(flashCanvas);
            flashSo.FindProperty("canvasGroup").objectReferenceValue = effectGo.GetComponent<CanvasGroup>();
            flashSo.FindProperty("fullscreenImage").objectReferenceValue = img;
            flashSo.ApplyModifiedProperties();

            var hazardSo = new SerializedObject(hazard);
            hazardSo.FindProperty("flashCanvas").objectReferenceValue = flashCanvas;
            hazardSo.FindProperty("shutterAudio").objectReferenceValue = audio;
            hazardSo.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            hazardSo.FindProperty("bulbLight").objectReferenceValue = bulb;
            hazardSo.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Camera Flash Test setup complete!\n" +
                "- Trigger GameObject has CameraFlashHazard (self-contained — JumpscareTrigger 패턴).\n" +
                "- 4x3 trigger below studio camera. Slime enters → 0.3s warmup → flash + stun + viewfinder.\n" +
                "- oneShot=true (fires once). Toggle on Trigger Inspector for re-fire during testing.\n" +
                "- Assign shutterClip on Trigger's CameraFlashHazard for audio.");
        }

        static void BuildViewfinder(Transform parent)
        {
            // Skip if already built (re-setup case)
            if (parent.Find("TL_H") != null) return;

            var color = new Color(0.1f, 0.1f, 0.1f, 1f);
            float inset = 80f;
            float armLen = 100f;
            float thick = 5f;

            AddBar(parent, "TL_H", new Vector2(0f, 1f), new Vector2(inset, -inset - thick), new Vector2(inset + armLen, -inset), color);
            AddBar(parent, "TL_V", new Vector2(0f, 1f), new Vector2(inset, -inset - armLen), new Vector2(inset + thick, -inset), color);

            AddBar(parent, "TR_H", new Vector2(1f, 1f), new Vector2(-inset - armLen, -inset - thick), new Vector2(-inset, -inset), color);
            AddBar(parent, "TR_V", new Vector2(1f, 1f), new Vector2(-inset - thick, -inset - armLen), new Vector2(-inset, -inset), color);

            AddBar(parent, "BL_H", new Vector2(0f, 0f), new Vector2(inset, inset), new Vector2(inset + armLen, inset + thick), color);
            AddBar(parent, "BL_V", new Vector2(0f, 0f), new Vector2(inset, inset), new Vector2(inset + thick, inset + armLen), color);

            AddBar(parent, "BR_H", new Vector2(1f, 0f), new Vector2(-inset - armLen, inset), new Vector2(-inset, inset + thick), color);
            AddBar(parent, "BR_V", new Vector2(1f, 0f), new Vector2(-inset - thick, inset), new Vector2(-inset, inset + armLen), color);

            float crossLen = 30f;
            AddBar(parent, "Cross_H", new Vector2(0.5f, 0.5f), new Vector2(-crossLen, -thick * 0.5f), new Vector2(crossLen, thick * 0.5f), color);
            AddBar(parent, "Cross_V", new Vector2(0.5f, 0.5f), new Vector2(-thick * 0.5f, -crossLen), new Vector2(thick * 0.5f, crossLen), color);
        }

        static Image AddBar(Transform parent, string name, Vector2 anchor, Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
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
