using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    /// <summary>GameObject-menu spawners that drop a fully-wired DeformablePlatform at the scene view pivot; sizes are encoded in the menu path.</summary>
    public static class DeformablePlatformSpawner
    {
        const int GroundLayer = 7;
        const string ClayMaterialPath = "Assets/Prebs/Platform/Materials/ClayPlatform.mat";

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Rectangle — Short (3 × 1.5)", priority = 10)]
        static void CreateRectShort(MenuCommand cmd) =>
            Spawn(cmd, "ClayPlatform_Short", width: 3f, height: 1.5f, segments: 8, ShapePreset.Rectangle);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Rectangle — Medium (1.5 × 3)", priority = 11)]
        static void CreateRectMed(MenuCommand cmd) =>
            Spawn(cmd, "ClayPlatform_Medium", width: 1.5f, height: 3f, segments: 12, ShapePreset.Rectangle);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Rectangle — Tall Pillar (1.5 × 7)", priority = 12)]
        static void CreateRectTall(MenuCommand cmd) =>
            Spawn(cmd, "ClayPillar_Tall", width: 1.5f, height: 7f, segments: 22, ShapePreset.Rectangle);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Rectangle — Wide Slab (6 × 1)", priority = 13)]
        static void CreateRectWide(MenuCommand cmd) =>
            Spawn(cmd, "ClaySlab_Wide", width: 6f, height: 1f, segments: 6, ShapePreset.Rectangle);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Hourglass (2 × 6)", priority = 30)]
        static void CreateHourglass(MenuCommand cmd) =>
            Spawn(cmd, "ClayHourglass", width: 2f, height: 6f, segments: 22, ShapePreset.Hourglass);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Taper (2 × 6)", priority = 31)]
        static void CreateTaper(MenuCommand cmd) =>
            Spawn(cmd, "ClayTaper", width: 2f, height: 6f, segments: 22, ShapePreset.Taper);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform/Dome Top (2 × 6)", priority = 32)]
        static void CreateDome(MenuCommand cmd) =>
            Spawn(cmd, "ClayDome", width: 2f, height: 6f, segments: 22, ShapePreset.DomeTop);

        [MenuItem("GameObject/SlimeJumping/Deformable Platform Activator (Manager)", priority = 60)]
        static void CreateActivator(MenuCommand cmd)
        {
            // Only one per scene — warn instead of silently duplicating.
            var existing = Object.FindAnyObjectByType<DeformablePlatformActivator>();
            if (existing != null)
            {
                Debug.LogWarning($"[DeformablePlatformSpawner] An activator already exists: '{existing.name}'. " +
                                 "Only one is needed per scene.", existing);
                Selection.activeGameObject = existing.gameObject;
                return;
            }

            var go = new GameObject("DeformableActivator");
            Undo.RegisterCreatedObjectUndo(go, "Create DeformableActivator");
            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            if (cmd.context == null)
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null) go.transform.position = sv.pivot;
            }

            var rb  = go.AddComponent<Rigidbody2D>();
            var col = go.AddComponent<CircleCollider2D>();
            go.AddComponent<DeformablePlatformActivator>();
            // AddComponent from script skips Reset(), so apply the config explicitly.
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.useFullKinematicContacts = true;
            rb.gravityScale = 0f;
            col.isTrigger = true;
            col.radius = 15f;

            Selection.activeGameObject = go;
        }

        // --- Shared spawn pipeline ---------------------------------------------

        public enum ShapePreset { Rectangle, Hourglass, Taper, DomeTop }

        static void Spawn(MenuCommand cmd, string name, float width, float height, int segments, ShapePreset preset)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

            GameObjectUtility.SetParentAndAlign(go, cmd.context as GameObject);
            if (cmd.context == null)
            {
                var sv = SceneView.lastActiveSceneView;
                if (sv != null)
                    go.transform.position = sv.pivot;
            }

            go.layer = GroundLayer;
            go.AddComponent<PolygonCollider2D>();
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureClayMaterial();

            var dp = go.AddComponent<DeformablePlatform>();
            var so = new SerializedObject(dp);
            so.FindProperty("genWidth").floatValue        = width;
            so.FindProperty("genHeight").floatValue       = height;
            so.FindProperty("genSegmentCount").intValue   = segments;
            so.ApplyModifiedPropertiesWithoutUndo();

            switch (preset)
            {
                case ShapePreset.Hourglass: dp.GenerateHourglass(); break;
                case ShapePreset.Taper:     dp.GenerateTaper();     break;
                case ShapePreset.DomeTop:   dp.GenerateDomeTop();   break;
                default:                    dp.GenerateRectangle(); break;
            }
            dp.BuildRuntime();

            Selection.activeGameObject = go;
        }

        public static Material EnsureClayMaterial()
        {
            EnsureFolder("Assets/Prebs/Platform/Materials");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ClayMaterialPath);
            if (existing != null) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var mat = new Material(shader) { name = "ClayPlatform" };
            mat.SetColor("_BaseColor", new Color(0.62f, 0.42f, 0.28f));
            mat.SetFloat("_Smoothness", 0.08f);
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Cull", 0f);          // double-sided: procedural mesh winding isn't Unity-convention on all faces
            mat.doubleSidedGI = true;
            AssetDatabase.CreateAsset(mat, ClayMaterialPath);
            return mat;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string curr = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = curr + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(curr, parts[i]);
                curr = next;
            }
        }
    }
}
