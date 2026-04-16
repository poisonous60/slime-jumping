using UnityEngine;
using UnityEditor;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.Editor
{
    public static class SitPoseTestSetup
    {
        const int InteractableLayer = 10;
        const string DefaultProfilePath = "Assets/Settings/SitPose/Default.asset";

        [MenuItem("SlimeJumping/Interaction/Setup SitPose Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("SitPoseTest");
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            var profile = LoadOrCreateDefaultProfile();
            EnsureSlimeHasAnimator();

            var root = new GameObject("SitPoseTest");
            Undo.RegisterCreatedObjectUndo(root, "Create SitPose Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Ground", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            // Bench 1 — right side
            CreateSitPose("Bench_A", root.transform,
                hostPos: new Vector3(baseX + 2f, baseY + 0.25f, 0f),
                hostSize: new Vector2(1.5f, 0.5f),
                anchorLocal: new Vector3(0f, 0.5f, 0f),
                triggerSize: new Vector2(1.5f, 1.2f),
                color: new Color(0.7f, 0.5f, 0.3f),
                profile: profile);

            // Bench 2 — left side, further
            CreateSitPose("Bench_B", root.transform,
                hostPos: new Vector3(baseX - 4f, baseY + 0.25f, 0f),
                hostSize: new Vector2(2f, 0.5f),
                anchorLocal: new Vector3(0f, 0.5f, 0f),
                triggerSize: new Vector2(2f, 1.2f),
                color: new Color(0.3f, 0.5f, 0.7f),
                profile: profile);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("SitPose Test setup complete!\n" +
                "- Walk up to a bench, press W to sit (OutBack squash → spring-damper breathing idle).\n" +
                "- Press W again to stand up (anticipation stretch → elastic settle → release).");
        }

        static GameObject CreateSitPose(string name, Transform parent,
            Vector3 hostPos, Vector2 hostSize,
            Vector3 anchorLocal, Vector2 triggerSize, Color color,
            SitPoseProfile profile)
        {
            // Root — visual bench + trigger collider + SitPoseInteractable
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.position = hostPos;
            go.layer = InteractableLayer;

            // Visual
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<BoxCollider>());
            visual.transform.parent = go.transform;
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = new Vector3(hostSize.x, hostSize.y, 0.3f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);

            // Trigger (for InteractionDetector)
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = triggerSize;

            // Anchor child — where slime sits. Visualized via SitPoseInteractable.OnDrawGizmos
            // (Scene view only, hidden in Game view / builds).
            var anchor = new GameObject("SitAnchor");
            anchor.transform.parent = go.transform;
            anchor.transform.localPosition = anchorLocal;

            // Component
            var sit = go.AddComponent<SitPoseInteractable>();
            var so = new SerializedObject(sit);
            so.FindProperty("sitAnchor").objectReferenceValue = anchor.transform;
            so.FindProperty("freezePhysics").boolValue = true;
            so.FindProperty("interactPrompt").stringValue = "Sit";
            if (profile != null)
                so.FindProperty("profile").objectReferenceValue = profile;
            so.ApplyModifiedProperties();

            return go;
        }

        static SitPoseProfile LoadOrCreateDefaultProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<SitPoseProfile>(DefaultProfilePath);
            if (profile != null) return profile;

            // Ensure folder chain exists. AssetDatabase requires forward slashes.
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

            profile = ScriptableObject.CreateInstance<SitPoseProfile>();
            AssetDatabase.CreateAsset(profile, DefaultProfilePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SitPoseTestSetup] Created default SitPoseProfile at {DefaultProfilePath}");
            return profile;
        }

        static void EnsureSlimeHasAnimator()
        {
            var slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[SitPoseTestSetup] No SlimeController in scene — attach SitPoseAnimator to the slime prefab manually.");
                return;
            }
            if (slime.GetComponent<SitPoseAnimator>() == null)
            {
                Undo.AddComponent<SitPoseAnimator>(slime.gameObject);
                Debug.Log("[SitPoseTestSetup] Added SitPoseAnimator to slime.");
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
