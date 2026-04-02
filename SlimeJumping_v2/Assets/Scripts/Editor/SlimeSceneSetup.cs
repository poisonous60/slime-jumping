using UnityEngine;
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
            // Set default ground layer to Environment (layer 7)
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

            // Clean missing scripts from Slime and children
            CleanMissingScripts(slimeGo);

            // Remove SlimeVisual child if it exists (no longer needed)
            var visual = slimeGo.transform.Find("SlimeVisual");
            if (visual != null)
            {
                CleanMissingScripts(visual.gameObject);
                Object.DestroyImmediate(visual.gameObject);
            }

            // --- Rigidbody2D ---
            var rb = slimeGo.GetComponent<Rigidbody2D>();
            if (rb == null) rb = slimeGo.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            // --- CircleCollider2D ---
            var col = slimeGo.GetComponent<CircleCollider2D>();
            if (col == null) col = slimeGo.AddComponent<CircleCollider2D>();
            col.radius = 0.4f;

            // Zero-friction physics material
            var mat = new PhysicsMaterial2D("SlimeFrictionless") { friction = 0f, bounciness = 0f };
            col.sharedMaterial = mat;

            // --- SpriteRenderer (green circle) ---
            var sr = slimeGo.GetComponent<SpriteRenderer>();
            if (sr == null) sr = slimeGo.AddComponent<SpriteRenderer>();
            sr.sprite = MakeCircleSprite();
            sr.color = new Color(0.2f, 0.9f, 0.3f, 0.9f);
            sr.sortingOrder = 1;
            // Scale sprite to match collider radius (sprite is 1x1 unit, radius 0.4 -> diameter 0.8)
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
            else
            {
                Debug.LogWarning("InputActionAsset not found at " + InputActionsPath);
            }

            // --- SlimeChargeVisual ---
            var chargeVis = slimeGo.GetComponent<SlimeChargeVisual>();
            if (chargeVis == null) chargeVis = slimeGo.AddComponent<SlimeChargeVisual>();

            // Create or find AimLine child
            var aimLineTransform = slimeGo.transform.Find("AimLine");
            if (aimLineTransform == null)
            {
                var aimLineGo = new GameObject("AimLine");
                aimLineGo.transform.SetParent(slimeGo.transform, false);
                aimLineTransform = aimLineGo.transform;
            }

            var lineRenderer = aimLineTransform.GetComponent<LineRenderer>();
            if (lineRenderer == null) lineRenderer = aimLineTransform.gameObject.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.useWorldSpace = true;
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.sortingOrder = 2;

            var soVis = new SerializedObject(chargeVis);
            soVis.FindProperty("aimLine").objectReferenceValue = lineRenderer;
            soVis.ApplyModifiedProperties();

            // --- SlimeController ---
            var controller = slimeGo.GetComponent<SlimeController>();
            if (controller == null) controller = slimeGo.AddComponent<SlimeController>();

            var slimeSettings = AssetDatabase.LoadAssetAtPath<SlimeSettings>(SettingsPath);
            if (slimeSettings == null)
            {
                Debug.LogWarning("SlimeSettings not found. Run 'SlimeJumping/Create Slime Settings' first.");
            }

            var soCtrl = new SerializedObject(controller);
            soCtrl.FindProperty("inputReader").objectReferenceValue = inputReader;
            soCtrl.FindProperty("settings").objectReferenceValue = slimeSettings;
            soCtrl.FindProperty("chargeVisual").objectReferenceValue = chargeVis;
            soCtrl.ApplyModifiedProperties();

            // Mark scene dirty
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Debug.Log("Simple Slime setup complete!");
            Selection.activeGameObject = slimeGo;
        }

        private static void CleanMissingScripts(GameObject go)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            foreach (Transform child in go.transform)
                CleanMissingScripts(child.gameObject);
        }

        private static Sprite MakeCircleSprite()
        {
            // Try to use Unity's built-in Knob sprite
            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            if (knob != null) return knob;

            // Fallback: create a simple white circle texture
            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = center - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    tex.SetPixel(x, y, dist <= radius ? Color.white : Color.clear);
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }
    }
}
