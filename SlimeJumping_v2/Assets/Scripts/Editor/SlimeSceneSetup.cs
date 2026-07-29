using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using SlimeJumping.Settings;
using SlimeJumping.InputHandling;
using SlimeJumping.Controller;
using SlimeJumping.Debugging;

namespace SlimeJumping.Editor
{
    public static class SlimeSceneSetup
    {
        private const string SettingsPath = "Assets/Settings/SlimeSettings.asset";
        private const string VisualSettingsPath = "Assets/Settings/SlimeVisualSettings.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string ChargeRingMatPath = "Assets/Materials/ChargeRing.mat";
        private const string AimLineMatPath = "Assets/Materials/AimLine.mat";

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

        [MenuItem("SlimeJumping/Create Slime Visual Settings")]
        public static void CreateSlimeVisualSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SlimeVisualSettings>(VisualSettingsPath);
            if (existing != null)
            {
                Debug.Log("SlimeVisualSettings already exists at " + VisualSettingsPath);
                EditorGUIUtility.PingObject(existing);
                return;
            }

            var settings = ScriptableObject.CreateInstance<SlimeVisualSettings>();
            AssetDatabase.CreateAsset(settings, VisualSettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Created SlimeVisualSettings at " + VisualSettingsPath);
            EditorGUIUtility.PingObject(settings);
        }

        [MenuItem("SlimeJumping/Setup Simple Slime")]
        public static void SetupSimpleSlime()
        {
            var slimeGo = GameObject.Find("Slime");
            if (slimeGo == null)
            {
                slimeGo = new GameObject("Slime");
                slimeGo.transform.position = new Vector3(0f, 1f, 0f);
            }

            CleanMissingScripts(slimeGo);

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

            var rb = slimeGo.GetComponent<Rigidbody2D>();
            if (rb == null) rb = slimeGo.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = slimeGo.GetComponent<CircleCollider2D>();
            if (col == null) col = slimeGo.AddComponent<CircleCollider2D>();
            col.radius = 0.4f;
            var phyMat = new PhysicsMaterial2D("SlimeFrictionless") { friction = 0f, bounciness = 0f };
            col.sharedMaterial = phyMat;

            var sr = slimeGo.GetComponent<SpriteRenderer>();
            if (sr == null) sr = slimeGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeCircleSprite();
            sr.color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
            sr.sortingOrder = 1;
            slimeGo.transform.localScale = new Vector3(0.8f, 0.8f, 1f);

            var inputReader = slimeGo.GetComponent<SlimeInputReader>();
            if (inputReader == null) inputReader = slimeGo.AddComponent<SlimeInputReader>();

            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>(InputActionsPath);
            if (inputAsset != null)
            {
                var soInput = new SerializedObject(inputReader);
                soInput.FindProperty("inputActionAsset").objectReferenceValue = inputAsset;
                soInput.ApplyModifiedProperties();
            }

            var chargeVis = slimeGo.GetComponent<SlimeChargeVisual>();
            if (chargeVis == null) chargeVis = slimeGo.AddComponent<SlimeChargeVisual>();

            // Legacy canvas from older setups, removed if it lingers.
            var legacyCanvas = GameObject.Find("ChargeVisualCanvas");
            if (legacyCanvas != null) Object.DestroyImmediate(legacyCanvas);

            var ringT = slimeGo.transform.Find("ChargeRing");
            GameObject ringGo;
            if (ringT == null)
            {
                ringGo = new GameObject("ChargeRing");
                ringGo.transform.SetParent(slimeGo.transform, false);
            }
            else ringGo = ringT.gameObject;

            var ringSR = ringGo.GetComponent<SpriteRenderer>();
            if (ringSR == null) ringSR = ringGo.AddComponent<SpriteRenderer>();
            ringSR.sprite = MakeUnitSquareSprite();
            ringSR.sharedMaterial = LoadOrCreateChargeRingMaterial();
            ringSR.sortingOrder = 1;
            ringGo.SetActive(false);

            var aimLineT = slimeGo.transform.Find("ChargeAimLine");
            GameObject aimLineGo;
            if (aimLineT == null)
            {
                aimLineGo = new GameObject("ChargeAimLine");
                aimLineGo.transform.SetParent(slimeGo.transform, false);
            }
            else aimLineGo = aimLineT.gameObject;

            var aimLineLR = aimLineGo.GetComponent<LineRenderer>();
            if (aimLineLR == null) aimLineLR = aimLineGo.AddComponent<LineRenderer>();
            aimLineLR.useWorldSpace = true;
            aimLineLR.positionCount = 2;
            aimLineLR.startWidth = 0.025f;
            aimLineLR.endWidth = 0.025f;
            aimLineLR.numCapVertices = 4;
            aimLineLR.numCornerVertices = 0;
            aimLineLR.sortingOrder = 2;
            aimLineLR.sharedMaterial = LoadOrCreateAimLineMaterial();
            aimLineGo.SetActive(false);

            var controller = slimeGo.GetComponent<SlimeController>();
            if (controller == null) controller = slimeGo.AddComponent<SlimeController>();

            var slimeSettings = AssetDatabase.LoadAssetAtPath<SlimeSettings>(SettingsPath);
            if (slimeSettings == null)
                Debug.LogWarning("SlimeSettings not found. Run 'SlimeJumping/Create Slime Settings' first.");

            var soCtrl = new SerializedObject(controller);
            soCtrl.FindProperty("inputReader").objectReferenceValue = inputReader;
            soCtrl.FindProperty("settings").objectReferenceValue = slimeSettings;
            soCtrl.ApplyModifiedProperties();

            var soVis = new SerializedObject(chargeVis);
            soVis.FindProperty("controller").objectReferenceValue = controller;
            soVis.FindProperty("chargeRing").objectReferenceValue = ringGo.transform;
            soVis.FindProperty("chargeRingRenderer").objectReferenceValue = ringSR;
            soVis.FindProperty("aimLine").objectReferenceValue = aimLineLR;
            soVis.ApplyModifiedProperties();

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

        private static Material LoadOrCreateChargeRingMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ChargeRingMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("World/SDFRing");
            if (shader == null)
            {
                Debug.LogWarning("World/SDFRing shader not found; ChargeRing will fall back to default material.");
                return null;
            }

            var mat = new Material(shader) { name = "ChargeRing" };
            AssetDatabase.CreateAsset(mat, ChargeRingMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Material LoadOrCreateAimLineMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(AimLineMatPath);
            if (existing != null) return existing;

            var shader = Shader.Find("World/AimLine");
            if (shader == null)
            {
                Debug.LogWarning("World/AimLine shader not found; AimLine will fall back to Sprites/Default.");
                return new Material(Shader.Find("Sprites/Default"));
            }

            var mat = new Material(shader) { name = "AimLine" };
            AssetDatabase.CreateAsset(mat, AimLineMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        private static Sprite MakeUnitSquareSprite()
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            tex.filterMode = FilterMode.Bilinear;
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2);
        }
    }
}
