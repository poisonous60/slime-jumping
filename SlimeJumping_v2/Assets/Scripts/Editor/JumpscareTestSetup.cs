using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.UI;

namespace SlimeJumping.Editor
{
    public static class JumpscareTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Jumpscare Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("JumpscareTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

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

            // Shared FullscreenEffectCanvas → JumpscareEffect child
            var canvasRoot = FullscreenCanvasSetupUtility.GetOrCreateRoot();
            var effectGo = FullscreenCanvasSetupUtility.GetOrCreateEffectChild(canvasRoot, "JumpscareEffect");
            var img = FullscreenCanvasSetupUtility.AddFullscreenImage(effectGo, new Color(1f, 1f, 1f, 0f));

            var audio = effectGo.GetComponent<AudioSource>();
            if (audio == null) audio = effectGo.AddComponent<AudioSource>();
            audio.spatialBlend = 0f;
            audio.playOnAwake = false;

            if (effectGo.GetComponent<CinemachineImpulseSource>() == null)
                effectGo.AddComponent<CinemachineImpulseSource>();
            var emitter = effectGo.GetComponent<ImpulseEmitter>();
            if (emitter == null) emitter = effectGo.AddComponent<ImpulseEmitter>();

            var jumpscareCanvas = effectGo.GetComponent<JumpscareCanvasController>();
            if (jumpscareCanvas == null) jumpscareCanvas = effectGo.AddComponent<JumpscareCanvasController>();
            var jumpscareSo = new SerializedObject(jumpscareCanvas);
            jumpscareSo.FindProperty("canvasGroup").objectReferenceValue = effectGo.GetComponent<CanvasGroup>();
            jumpscareSo.FindProperty("fullscreenImage").objectReferenceValue = img;
            jumpscareSo.FindProperty("audioSource").objectReferenceValue = audio;
            jumpscareSo.FindProperty("impulseEmitter").objectReferenceValue = emitter;
            jumpscareSo.ApplyModifiedProperties();

            // Wire trigger → canvas
            var triggerSo = new SerializedObject(trigger);
            triggerSo.FindProperty("canvas").objectReferenceValue = jumpscareCanvas;
            triggerSo.FindProperty("oneShot").boolValue = false; // repeat for testing
            triggerSo.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Jumpscare Test setup complete!\n" +
                "- Trigger 3x3 on scareHost. Walk the slime into it.\n" +
                "- White fullscreen fade + ImpulseEmitter force 1.5.\n" +
                "- oneShot=false for testing; toggle in Inspector if needed.\n" +
                "- Shared FullscreenEffectCanvas/JumpscareEffect child added (reused across tests).\n" +
                "- Assign scareImage / scareSound on the Trigger in Inspector for full effect.");
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
