using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WaterZoneVfxTestSetup
    {
        const string WaterSurfacePrefabPath =
            "Assets/ThirdParty/Realistic Water VFX URP/Water VFX/Water Materials/Particles/VFX_Water_Surface_Calm_01.prefab";
        const string SplashPrefabPath =
            "Assets/ThirdParty/Realistic Water VFX URP/Water VFX/Splashes/Particles/VFX_Generic_Splash_01.prefab";

        [MenuItem("SlimeJumping/Setup Water Zone VFX Test")]
        public static void SetupWaterZoneVfxTest()
        {
            var root = new GameObject("WaterZoneVfxTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Water Zone VFX Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("PoolFloor", root.transform,
                new Vector3(baseX, baseY - 4f, 0f),
                new Vector3(12f, 1f, 1f));
            CreatePlatform("PoolWallLeft", root.transform,
                new Vector3(baseX - 6.5f, baseY - 1f, 0f),
                new Vector3(1f, 7f, 1f));
            CreatePlatform("PoolWallRight", root.transform,
                new Vector3(baseX + 6.5f, baseY - 1f, 0f),
                new Vector3(1f, 7f, 1f));

            CreatePlatform("DryPlatform_Left", root.transform,
                new Vector3(baseX - 8.5f, baseY + 1f, 0f),
                new Vector3(3f, 0.5f, 1f));
            CreatePlatform("DryPlatform_Right", root.transform,
                new Vector3(baseX + 8.5f, baseY + 1f, 0f),
                new Vector3(3f, 0.5f, 1f));

            var waterGo = new GameObject("WaterZone");
            waterGo.transform.parent = root.transform;
            waterGo.transform.position = new Vector3(baseX, baseY - 1f, 0f);

            var waterCol = waterGo.AddComponent<BoxCollider2D>();
            waterCol.isTrigger = true;
            waterCol.size = new Vector2(12f, 5f);

            var water = waterGo.AddComponent<WaterZone>();

            var splashPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SplashPrefabPath);
            if (splashPrefab == null)
                Debug.LogWarning($"[WaterZoneVfxTestSetup] Splash prefab not found at {SplashPrefabPath}");

            var so = new SerializedObject(water);
            so.FindProperty("entryMultiplier").floatValue = 0.4f;
            so.FindProperty("exitMultiplier").floatValue = 2.5f;
            so.FindProperty("dragFactor").floatValue = 0.4f;
            so.FindProperty("buoyancy").floatValue = 12f;
            so.FindProperty("maxFallSpeed").floatValue = 4f;
            so.FindProperty("blockAttach").boolValue = true;
            so.FindProperty("enterSplashPrefab").objectReferenceValue = splashPrefab;
            so.FindProperty("exitSplashPrefab").objectReferenceValue = splashPrefab;
            so.ApplyModifiedProperties();

            // Legacy placeholder sprite — kept disabled for rollback/compare.
            var waterVis = waterGo.AddComponent<SpriteRenderer>();
            waterVis.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            waterVis.drawMode = SpriteDrawMode.Sliced;
            waterVis.size = new Vector2(12f, 5f);
            waterVis.color = new Color(0.2f, 0.5f, 1f, 0.2f);
            waterVis.sortingOrder = 1;
            waterVis.enabled = false;

            // Water surface VFX (Realistic Water VFX URP — ParticleSystem-based).
            var surfacePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WaterSurfacePrefabPath);
            if (surfacePrefab != null)
            {
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(surfacePrefab, waterGo.transform);
                visual.name = "WaterVisual";
                visual.transform.localPosition = new Vector3(0f, 0f, -0.1f);
                visual.transform.localScale = new Vector3(12f, 5f, 1f);
            }
            else
            {
                Debug.LogWarning($"[WaterZoneVfxTestSetup] Water surface prefab not found at {WaterSurfacePrefabPath}");
            }

            CreatePlatform("SubmergedPlatform", root.transform,
                new Vector3(baseX, baseY - 2f, 0f),
                new Vector3(4f, 0.5f, 1f));

            var spikeGo = new GameObject("Spike_InWater");
            spikeGo.transform.parent = root.transform;
            spikeGo.transform.position = new Vector3(baseX + 3f, baseY - 3.5f, 0f);

            var spikeCol = spikeGo.AddComponent<BoxCollider2D>();
            spikeCol.isTrigger = true;
            spikeCol.size = new Vector2(1.5f, 0.3f);

            var spike = spikeGo.AddComponent<SpikeHazard>();
            var soSpike = new SerializedObject(spike);
            soSpike.FindProperty("knockbackForce").floatValue = 8f;
            soSpike.FindProperty("stunDuration").floatValue = 0.5f;
            soSpike.FindProperty("useLocalUp").boolValue = true;
            soSpike.ApplyModifiedProperties();

            var spikeVis = spikeGo.AddComponent<SpriteRenderer>();
            spikeVis.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            spikeVis.drawMode = SpriteDrawMode.Sliced;
            spikeVis.size = new Vector2(1.5f, 0.3f);
            spikeVis.color = new Color(1f, 0.2f, 0.2f, 0.4f);
            spikeVis.sortingOrder = 2;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Water Zone VFX Test setup complete!\n" +
                "- Same physics as the plain Water Zone Test\n" +
                "- WaterVisual child uses VFX_Water_Surface_Calm_01 (ParticleSystem-based)\n" +
                "- Enter/Exit spawn VFX_Generic_Splash_01 via WaterZone splash fields\n" +
                "- Legacy SpriteRenderer is disabled on WaterZone (rollback reference)");
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

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.5f, 0.5f, 0.5f));

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
