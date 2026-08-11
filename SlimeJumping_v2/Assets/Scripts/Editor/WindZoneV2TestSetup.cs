using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class WindZoneV2TestSetup
    {
        const string SnowPrefabPath = "Assets/Prebs/VFX/Snow.prefab";
        const string WindParticleMaterialPath = "Assets/Materials/WindParticle.mat";
        const string WindForceFieldLayerName = "WindForceField";

        [MenuItem("SlimeJumping/Mechanism/Setup Wind Zone V2 Test")]
        public static void SetupWindZoneV2Test()
        {
            int windFFLayer = LayerMask.NameToLayer(WindForceFieldLayerName);
            if (windFFLayer < 0)
            {
                Debug.LogError($"Layer '{WindForceFieldLayerName}' is missing from TagManager. Add it before running this setup.");
                return;
            }

            var root = new GameObject("WindZoneV2Test");
            Undo.RegisterCreatedObjectUndo(root, "Create Wind Zone V2 Test");

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(20f, 1f, 1f));

            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 10.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            CreatePlatform("Ceiling", root.transform,
                new Vector3(baseX + 3f, baseY + 8.5f, 0f),
                new Vector3(8f, 1f, 1f));

            CreatePlatform("LaunchStep", root.transform,
                new Vector3(baseX - 6f, baseY + 2f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Wind Zone V2 ---
            var windZone = new GameObject("WindZoneV2_Right");
            windZone.transform.parent = root.transform;
            windZone.transform.position = new Vector3(baseX + 3f, baseY + 4f, 0f);

            var windCol = windZone.AddComponent<BoxCollider2D>();
            windCol.isTrigger = true;
            windCol.size = new Vector2(8f, 8f);

            var wind = windZone.AddComponent<WindZone2D_v2>();
            var soWind = new SerializedObject(wind);
            soWind.FindProperty("windAcceleration").vector2Value = new Vector2(20f, 0f);
            soWind.FindProperty("terminalSpeed").floatValue = 12f;
            soWind.FindProperty("airborneMultiplier").floatValue = 1f;
            soWind.FindProperty("attachedMultiplier").floatValue = 0.3f;
            soWind.FindProperty("snowDrag").floatValue = 1.5f;
            soWind.ApplyModifiedProperties();

            var windVis = windZone.AddComponent<SpriteRenderer>();
            windVis.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            windVis.drawMode = SpriteDrawMode.Sliced;
            windVis.size = new Vector2(8f, 8f);
            windVis.color = new Color(0.4f, 0.7f, 1f, 0.08f);
            windVis.sortingOrder = -1;

            // --- Stripe Particles (in-zone visual) ---
            var particleGo = new GameObject("WindParticles");
            particleGo.transform.parent = windZone.transform;
            particleGo.transform.localPosition = Vector3.zero;

            var ps = particleGo.AddComponent<ParticleSystem>();
            var psRenderer = particleGo.GetComponent<ParticleSystemRenderer>();

            var main = ps.main;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            main.startColor = new Color(1f, 1f, 1f, 0.3f);
            main.maxParticles = 300;

            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.2f, 1f),
                new Keyframe(0.8f, 1f),
                new Keyframe(1f, 0f));
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;

            psRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            psRenderer.lengthScale = 3f;
            psRenderer.velocityScale = 0.1f;
            psRenderer.sharedMaterial = LoadOrCreateWindParticleMaterial();

            // --- Snow ForceField (drives snow particles globally) ---
            var ff = CreateForceFieldChild("SnowForceField", windZone.transform, Vector3.zero,
                new Vector3(8f, 8f, 100f), windFFLayer, 20f, 1.5f);

            // --- Side ForceFields (soft boundary on left/right) ---
            var ffLeft = CreateForceFieldChild("SnowForceField_Left", windZone.transform,
                new Vector3(-(8f * 0.5f + 4f * 0.5f), 0f, 0f),
                new Vector3(4f, 8f, 100f), windFFLayer, 20f * 0.3f, 1.5f);
            var ffRight = CreateForceFieldChild("SnowForceField_Right", windZone.transform,
                new Vector3(+(8f * 0.5f + 4f * 0.5f), 0f, 0f),
                new Vector3(4f, 8f, 100f), windFFLayer, 20f * 0.3f, 1.5f);

            soWind = new SerializedObject(wind);
            soWind.FindProperty("windParticles").objectReferenceValue = ps;
            soWind.FindProperty("snowForceField").objectReferenceValue = ff;
            soWind.FindProperty("sideForceFieldLeft").objectReferenceValue = ffLeft;
            soWind.FindProperty("sideForceFieldRight").objectReferenceValue = ffRight;
            soWind.ApplyModifiedProperties();

            CreatePlatform("LandingPlatform", root.transform,
                new Vector3(baseX + 9f, baseY + 2f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Snow prefab (camera-following snow) ---
            var snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefabPath);
            if (snowPrefab != null)
            {
                var snowInstance = (GameObject)PrefabUtility.InstantiatePrefab(snowPrefab, root.transform);
                snowInstance.transform.position = new Vector3(baseX, baseY + 6f, 0f);
            }
            else
            {
                Debug.LogWarning($"Snow prefab not found at {SnowPrefabPath} — Snow integration won't be visible until added manually.");
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Wind Zone V2 Test setup complete. Launch slime from the left step into the wind zone — snow inside the zone will drift in the wind direction.");
        }

        static ParticleSystemForceField CreateForceFieldChild(string name, Transform parent, Vector3 localPos, Vector3 localScale, int layer, float directionX, float drag)
        {
            var go = new GameObject(name);
            go.transform.parent = parent;
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = localScale;
            go.layer = layer;
            var ff = go.AddComponent<ParticleSystemForceField>();
            ff.shape = ParticleSystemForceFieldShape.Box;
            ff.endRange = 0.5f;
            ff.directionX = new ParticleSystem.MinMaxCurve(directionX);
            ff.directionY = new ParticleSystem.MinMaxCurve(0f);
            ff.directionZ = new ParticleSystem.MinMaxCurve(0f);
            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(drag);
            return ff;
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

        static Material LoadOrCreateWindParticleMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WindParticleMaterialPath);
            if (mat != null) return mat;

            if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                AssetDatabase.CreateFolder("Assets", "Materials");

            // Stencil-gated stripe shader: alpha blending alone stacks where stripes overlap.
            var shader = Shader.Find("SlimeJumping/WindStripe");
            mat = new Material(shader) { name = "WindParticle" };
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_StencilBit", 16f);

            AssetDatabase.CreateAsset(mat, WindParticleMaterialPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
