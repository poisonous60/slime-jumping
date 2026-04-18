using UnityEngine;
using UnityEditor;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class BoxSleepTestSetup
    {
        const int InteractableLayer = 10;
        const string DefaultProfilePath = "Assets/Settings/BoxSleep/Default.asset";

        [MenuItem("SlimeJumping/Interaction/Setup BoxSleep Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("BoxSleepTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var profile = LoadOrCreateDefaultProfile();
            EnsureSlimeHasAnimator();

            var root = new GameObject("BoxSleepTest");
            Undo.RegisterCreatedObjectUndo(root, "Create BoxSleep Test");

            float baseX = -4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            CreateBox("Box_A", root.transform,
                hostPos: new Vector3(baseX, baseY + 0.5f, 0f),
                hostScale: new Vector3(1.5f, 1.0f, 1.0f),
                hostEulerZ: -15f,
                anchorLocal: new Vector3(0f, 0.2f, 0f),
                triggerSize: new Vector2(2.0f, 1.5f),
                color: new Color(0.7f, 0.55f, 0.4f),
                profile: profile);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("BoxSleep Test setup complete!\n" +
                "- Walk to the box, press W to climb in (Y rotation + depth shift, face fades).\n" +
                "- Slime settles to outward yaw with closed eyes; Z particles spawn while sleeping.\n" +
                "- Press W again to wake, slime climbs back out and control returns.");
        }

        static GameObject CreateBox(string name, Transform parent,
            Vector3 hostPos, Vector3 hostScale, float hostEulerZ,
            Vector3 anchorLocal, Vector2 triggerSize, Color color,
            BoxSleepProfile profile)
        {
            // Root carries the trigger collider + interactable. Visual mesh is a child
            // so its rotation/scale don't fight with the trigger transform.
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.transform.rotation = Quaternion.Euler(0f, 0f, hostEulerZ);
            go.layer = InteractableLayer;

            // 3D mesh visual — Z-tilted cube
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.transform.parent = go.transform;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = hostScale;
            visual.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            // Anchor child — sits along box's local axes (inherits box Z-roll).
            var anchor = new GameObject("SitAnchor");
            anchor.transform.parent = go.transform;
            anchor.transform.localPosition = anchorLocal;

            // Z emitter as a child so spawnLocalOffset is relative to box, not slime.
            var emitterGo = new GameObject("ZEmitter");
            emitterGo.transform.parent = go.transform;
            emitterGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var emitter = emitterGo.AddComponent<SleepZEmitter>();

            var sleep = go.AddComponent<BoxSleepInteractable>();
            var so = new SerializedObject(sleep);
            so.FindProperty("sitAnchor").objectReferenceValue = anchor.transform;
            so.FindProperty("freezePhysics").boolValue = true;
            so.FindProperty("interactPrompt").stringValue = "Sleep";
            so.FindProperty("zEmitter").objectReferenceValue = emitter;
            if (profile != null)
                so.FindProperty("profile").objectReferenceValue = profile;
            so.ApplyModifiedProperties();

            return go;
        }

        static BoxSleepProfile LoadOrCreateDefaultProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<BoxSleepProfile>(DefaultProfilePath);
            if (profile != null) return profile;

            string dir = DefaultProfilePath.Substring(0, DefaultProfilePath.LastIndexOf('/'));
            if (!AssetDatabase.IsValidFolder(dir))
            {
                string[] parts = dir.Split('/');
                string accum = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = accum + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                        AssetDatabase.CreateFolder(accum, parts[i]);
                    accum = next;
                }
            }

            profile = ScriptableObject.CreateInstance<BoxSleepProfile>();
            AssetDatabase.CreateAsset(profile, DefaultProfilePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[BoxSleepTestSetup] Created default BoxSleepProfile at {DefaultProfilePath}");
            return profile;
        }

        static void EnsureSlimeHasAnimator()
        {
            var slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[BoxSleepTestSetup] No SlimeController in scene — attach BoxSleepAnimator to the slime prefab manually.");
                return;
            }
            if (slime.GetComponent<BoxSleepAnimator>() == null)
            {
                Undo.AddComponent<BoxSleepAnimator>(slime.gameObject);
                Debug.Log("[BoxSleepTestSetup] Added BoxSleepAnimator to slime.");
            }
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

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.5f, 0.5f, 0.5f));
            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
