using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SlimeJumping.Editor
{
    public static class PrototypeSceneSetup
    {
        [MenuItem("SlimeJumping/Setup Prototype Scene (Box)")]
        public static void SetupScene()
        {
            SetupLayers();
            CreateEnvironment();
            CreateSlime();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Prototype scene setup complete! Now:\n" +
                      "1. Menu: SlimeJumping > Create Settings Assets\n" +
                      "2. Assign both Settings assets to the Slime GameObject components\n" +
                      "3. Set Environment Layer mask on SlimeSoftBody and SlimeController");
        }

        [MenuItem("SlimeJumping/Setup Greybox Level")]
        public static void SetupGreyboxLevel()
        {
            SetupLayers();
            CreateGreyboxLevel();
            CreateSlimeFullSetup();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("Greybox level created! Assign Settings assets, then press Play.");
        }

        private static void SetupLayers()
        {
            SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");

            SetLayer(layers, 6, "SlimeNode");
            SetLayer(layers, 7, "Environment");

            tagManager.ApplyModifiedProperties();
        }

        private static void SetLayer(SerializedProperty layers, int index, string name)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(layer.stringValue))
                layer.stringValue = name;
        }

        private static void CreateEnvironment()
        {
            var env = new GameObject("Environment");

            CreateWall(env.transform, "Floor", new Vector3(0, -0.5f, 0), new Vector3(12, 1, 1));
            CreateWall(env.transform, "Ceiling", new Vector3(0, 10.5f, 0), new Vector3(12, 1, 1));
            CreateWall(env.transform, "LeftWall", new Vector3(-5.5f, 5, 0), new Vector3(1, 12, 1));
            CreateWall(env.transform, "RightWall", new Vector3(5.5f, 5, 0), new Vector3(1, 12, 1));
            CreateWall(env.transform, "Platform", new Vector3(0, 4, 0), new Vector3(2, 0.3f, 1));
        }

        private static void CreateGreyboxLevel()
        {
            var env = new GameObject("GreyboxLevel");

            CreateWall(env.transform, "A_Floor", new Vector3(0, -0.5f, 0), new Vector3(22, 1, 1));
            CreateWall(env.transform, "B_RightWall", new Vector3(11f, 7.5f, 0), new Vector3(1, 16, 1));
            CreateWall(env.transform, "B_Ledge1", new Vector3(9.5f, 5f, 0), new Vector3(1, 0.3f, 1));
            CreateWall(env.transform, "B_Ledge2", new Vector3(9.5f, 10f, 0), new Vector3(1, 0.3f, 1));
            CreateWall(env.transform, "C_Ceiling_L", new Vector3(7f, 15.5f, 0), new Vector3(5, 1, 1));
            CreateWall(env.transform, "C_Ceiling_R", new Vector3(13f, 15.5f, 0), new Vector3(5, 1, 1));
            CreateWall(env.transform, "D_Plat1", new Vector3(16f, 13f, 0), new Vector3(1.5f, 0.3f, 1));
            CreateWall(env.transform, "D_Plat2", new Vector3(18f, 10f, 0), new Vector3(1.5f, 0.3f, 1));
            CreateWall(env.transform, "D_Plat3", new Vector3(16f, 7f, 0), new Vector3(2f, 0.3f, 1));
            CreateWall(env.transform, "D_Plat4", new Vector3(19f, 4f, 0), new Vector3(1.5f, 0.3f, 1));
            CreateWall(env.transform, "E_Finish", new Vector3(20f, -0.5f, 0), new Vector3(4, 1, 1));
            CreateWall(env.transform, "BoundaryLeft", new Vector3(-11.5f, 5, 0), new Vector3(1, 32, 1));
            CreateWall(env.transform, "BoundaryRight", new Vector3(22.5f, 5, 0), new Vector3(1, 32, 1));
            CreateWall(env.transform, "BoundaryBottom", new Vector3(5, -2.5f, 0), new Vector3(36, 1, 1));
        }

        private static void CreateSlime()
        {
            var slime = new GameObject("Slime");
            slime.transform.position = new Vector3(0, 3, 0);

            var softBody = slime.AddComponent<SlimeJumping.Physics.SlimeSoftBody>();
            var adhesion = slime.AddComponent<SlimeJumping.Physics.AdhesionSystem>();
            slime.AddComponent<SlimeJumping.Debugging.SlimeDebugDraw>();

            WireUpReferences(slime, softBody, adhesion, null, null);
            CreateSlimeVisual(slime.transform);
        }

        private static void CreateSlimeFullSetup()
        {
            var slime = new GameObject("Slime");
            slime.transform.position = new Vector3(-8, 2, 0);

            var softBody = slime.AddComponent<SlimeJumping.Physics.SlimeSoftBody>();
            var adhesion = slime.AddComponent<SlimeJumping.Physics.AdhesionSystem>();
            var inputReader = slime.AddComponent<SlimeJumping.Input.SlimeInputReader>();
            var controller = slime.AddComponent<SlimeJumping.Controller.SlimeController>();
            slime.AddComponent<SlimeJumping.Debugging.SlimeDebugDraw>();

            WireUpReferences(slime, softBody, adhesion, inputReader, controller);
            CreateSlimeVisual(slime.transform);

            var camTarget = new GameObject("SlimeCameraTarget");
            camTarget.transform.position = slime.transform.position;
            var camComp = camTarget.AddComponent<SlimeJumping.Camera.SlimeCameraTarget>();

            var camSo = new SerializedObject(camComp);
            camSo.FindProperty("softBody").objectReferenceValue = softBody;
            camSo.ApplyModifiedProperties();
        }

        private static void WireUpReferences(GameObject slime,
            SlimeJumping.Physics.SlimeSoftBody softBody,
            SlimeJumping.Physics.AdhesionSystem adhesion,
            SlimeJumping.Input.SlimeInputReader inputReader,
            SlimeJumping.Controller.SlimeController controller)
        {
            var physSettings = AssetDatabase.LoadAssetAtPath<SlimeJumping.Settings.SlimePhysicsSettings>("Assets/Settings/SlimePhysicsSettings.asset");
            var gameSettings = AssetDatabase.LoadAssetAtPath<SlimeJumping.Settings.SlimeGameplaySettings>("Assets/Settings/SlimeGameplaySettings.asset");
            var inputAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/InputSystem_Actions.inputactions");

            if (physSettings == null || gameSettings == null)
            {
                Debug.LogWarning("Settings assets not found! Run 'SlimeJumping > Create Settings Assets' first, then re-run scene setup.");
            }

            int envLayerMask = 1 << LayerMask.NameToLayer("Environment");

            // SoftBody
            var sbSo = new SerializedObject(softBody);
            sbSo.FindProperty("settings").objectReferenceValue = physSettings;
            sbSo.FindProperty("environmentLayer").intValue = envLayerMask;
            sbSo.ApplyModifiedProperties();

            // AdhesionSystem
            var adSo = new SerializedObject(adhesion);
            adSo.FindProperty("softBody").objectReferenceValue = softBody;
            adSo.FindProperty("settings").objectReferenceValue = gameSettings;
            adSo.ApplyModifiedProperties();

            // InputReader
            if (inputReader != null)
            {
                var irSo = new SerializedObject(inputReader);
                irSo.FindProperty("inputActionAsset").objectReferenceValue = inputAsset;
                irSo.ApplyModifiedProperties();
            }

            // Controller
            if (controller != null)
            {
                var ctSo = new SerializedObject(controller);
                ctSo.FindProperty("softBody").objectReferenceValue = softBody;
                ctSo.FindProperty("adhesionSystem").objectReferenceValue = adhesion;
                ctSo.FindProperty("inputReader").objectReferenceValue = inputReader;
                ctSo.FindProperty("gameplaySettings").objectReferenceValue = gameSettings;
                ctSo.FindProperty("physicsSettings").objectReferenceValue = physSettings;
                ctSo.FindProperty("environmentLayer").intValue = envLayerMask;
                ctSo.ApplyModifiedProperties();
            }
        }

        private static void CreateSlimeVisual(Transform parent)
        {
            var meshChild = new GameObject("SlimeVisual");
            meshChild.transform.parent = parent;
            meshChild.transform.localPosition = Vector3.zero;

            meshChild.AddComponent<MeshFilter>();
            var renderer = meshChild.AddComponent<MeshRenderer>();

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = new Color(0.2f, 0.9f, 0.3f, 0.7f);
                mat.SetFloat("_Surface", 1);
                mat.SetFloat("_Blend", 0);
                mat.renderQueue = 3000;
                renderer.material = mat;
            }

            meshChild.AddComponent<SlimeJumping.Rendering.SlimeMeshGenerator>();
        }

        private static void CreateWall(Transform parent, string name, Vector3 position, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = position;
            go.transform.localScale = scale;
            go.layer = LayerMask.NameToLayer("Environment");

            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.AddComponent<BoxCollider2D>();
        }

        [MenuItem("SlimeJumping/Create Settings Assets")]
        public static void CreateSettingsAssets()
        {
            CreateAssetIfNotExists<SlimeJumping.Settings.SlimePhysicsSettings>("Assets/Settings/SlimePhysicsSettings.asset");
            CreateAssetIfNotExists<SlimeJumping.Settings.SlimeGameplaySettings>("Assets/Settings/SlimeGameplaySettings.asset");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("Settings assets created in Assets/Settings/. Assign them to the Slime components.");
        }

        private static void CreateAssetIfNotExists<T>(string path) where T : ScriptableObject
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
            {
                Debug.Log($"Asset already exists: {path}");
                return;
            }

            string dir = System.IO.Path.GetDirectoryName(path);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            Debug.Log($"Created: {path}");
        }
    }
}
