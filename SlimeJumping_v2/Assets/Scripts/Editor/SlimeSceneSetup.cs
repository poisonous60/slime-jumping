using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using SlimeJumping.Settings;
using SlimeJumping.Input;
using SlimeJumping.Controller;
using SlimeJumping.Debugging;

namespace SlimeJumping.Editor
{
    public static class SlimeSceneSetup
    {
        private const string SettingsPath = "Assets/Settings/SlimeSettings.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [MenuItem("SlimeJumping/Create Slime Settings")]
        public static void CreateSlimeSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SlimeSettings>(SettingsPath);
            if (existing != null)
            {
                Debug.Log("SlimeSettings already exists at " + SettingsPath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var settings = ScriptableObject.CreateInstance<SlimeSettings>();
            var serialized = new SerializedObject(settings);
            var layerProp = serialized.FindProperty("groundLayer");
            layerProp.intValue = 1 << 7;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(settings, SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Created SlimeSettings at " + SettingsPath);
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("SlimeJumping/Setup Simple Slime")]
        public static void SetupSimpleSlime()
        {
            // Find or create Slime object
            var slimeGo = GameObject.Find("Slime");
            if (slimeGo == null)
            {
                slimeGo = new GameObject("Slime");
                slimeGo.transform.position = new Vector3(0f, 1f, 0f);
            }

            CleanMissingScripts(slimeGo);

            // Remove old children (SlimeVisual, AimLine, CircleOutline)
            string[] oldChildren = { "SlimeVisual", "AimLine", "CircleOutline" };
            foreach (var name in oldChildren)
            {
                var t = slimeGo.transform.Find(name);
                if (t != null)
                {
                    CleanMissingScripts(t.gameObject);
                    Object.DestroyImmediate(t.gameObject);
                }
            }

            // --- Rigidbody2D ---
            var rb = slimeGo.GetComponent<Rigidbody2D>();
            if (rb == null) rb = slimeGo.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // --- CircleCollider2D ---
            var col = slimeGo.GetComponent<CircleCollider2D>();
            if (col == null) col = slimeGo.AddComponent<CircleCollider2D>();
            col.radius = 0.4f;
            var phyMat = new PhysicsMaterial2D("SlimeFrictionless") { friction = 0f, bounciness = 0f };
            col.sharedMaterial = phyMat;

            // --- SpriteRenderer ---
            var sr = slimeGo.GetComponent<SpriteRenderer>();
            if (sr == null) sr = slimeGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeCircleSprite();
            sr.color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
            sr.sortingOrder = 1;
            slimeGo.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            // --- SlimeInputReader ---
            var inputReader = slimeGo.GetComponent<SlimeInputReader>();
            if (inputReader == null) inputReader = slimeGo.AddComponent<SlimeInputReader>();

            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(InputActionsPath);
            if (inputAsset != null)
            {
                var soInput = new SerializedObject(inputReader);
                soInput.FindProperty("inputActionAsset").objectReferenceValue = inputAsset;
                soInput.ApplyModifiedProperties();
            }

            // --- Charge Visual Canvas (Screen Space Overlay) ---
            var chargeVis = slimeGo.GetComponent<SlimeChargeVisual>();
            if (chargeVis == null) chargeVis = slimeGo.AddComponent<SlimeChargeVisual>();

            // Remove old LineRenderer components if any
            foreach (var lr in slimeGo.GetComponentsInChildren<LineRenderer>())
                Object.DestroyImmediate(lr.gameObject == slimeGo ? (Object)lr : lr.gameObject);

            // Find or create Canvas
            var canvasGo = GameObject.Find("ChargeVisualCanvas");
            if (canvasGo == null)
            {
                canvasGo = new GameObject("ChargeVisualCanvas");
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;
                canvasGo.AddComponent<CanvasScaler>();
            }
            var canvasComp = canvasGo.GetComponent<Canvas>();

            // Circle outline (ring image)
            var circleRT = FindOrCreateUI(canvasGo.transform, "ChargeCircle");
            var circleImg = circleRT.GetComponent<Image>();
            circleImg.sprite = MakeRingSprite();
            circleImg.color = new Color(1f, 1f, 1f, 0.4f);
            circleImg.raycastTarget = false;
            circleRT.sizeDelta = new Vector2(300f, 300f);
            circleRT.gameObject.SetActive(false);

            // Aim dot
            var aimDotRT = FindOrCreateUI(canvasGo.transform, "AimDot");
            var aimDotImg = aimDotRT.GetComponent<Image>();
            aimDotImg.sprite = MakeCircleSpriteUI();
            aimDotImg.color = Color.white;
            aimDotImg.raycastTarget = false;
            aimDotRT.sizeDelta = new Vector2(10f, 10f);
            aimDotRT.gameObject.SetActive(false);

            // Aim line (stretched rect)
            var aimLineRT = FindOrCreateUI(canvasGo.transform, "AimLine");
            var aimLineImg = aimLineRT.GetComponent<Image>();
            aimLineImg.color = Color.green;
            aimLineImg.raycastTarget = false;
            aimLineRT.pivot = new Vector2(0f, 0.5f);
            aimLineRT.sizeDelta = new Vector2(0f, 3f);
            aimLineRT.gameObject.SetActive(false);

            // Wire references
            var soVis = new SerializedObject(chargeVis);
            soVis.FindProperty("canvas").objectReferenceValue = canvasComp;
            soVis.FindProperty("circleImage").objectReferenceValue = circleRT;
            soVis.FindProperty("aimDotImage").objectReferenceValue = aimDotRT;
            soVis.FindProperty("aimLineImage").objectReferenceValue = aimLineImg;
            soVis.ApplyModifiedProperties();

            // --- SlimeController ---
            var controller = slimeGo.GetComponent<SlimeController>();
            if (controller == null) controller = slimeGo.AddComponent<SlimeController>();

            var slimeSettings = AssetDatabase.LoadAssetAtPath<SlimeSettings>(SettingsPath);
            if (slimeSettings == null)
                Debug.LogWarning("SlimeSettings not found. Run 'SlimeJumping/Create Slime Settings' first.");

            var soCtrl = new SerializedObject(controller);
            soCtrl.FindProperty("inputReader").objectReferenceValue = inputReader;
            soCtrl.FindProperty("settings").objectReferenceValue = slimeSettings;
            soCtrl.FindProperty("chargeVisual").objectReferenceValue = chargeVis;
            soCtrl.ApplyModifiedProperties();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Debug.Log("Simple Slime setup complete!");
            Selection.activeGameObject = slimeGo;
        }

        private static RectTransform FindOrCreateUI(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t.GetComponent<RectTransform>();

            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private static void CleanMissingScripts(GameObject go)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            foreach (Transform child in go.transform)
                CleanMissingScripts(child.gameObject);
        }

        private static Sprite MakeCircleSprite()
        {
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (knob != null) return knob;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radius ? Color.white : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite MakeCircleSpriteUI()
        {
            int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radius ? Color.white : Color.clear);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private static Sprite MakeRingSprite()
        {
            int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float outerR = center - 1f;
            float innerR = outerR - 3f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    tex.SetPixel(x, y, dist >= innerR && dist <= outerR ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
