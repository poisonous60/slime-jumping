using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms.ThinIceV2.EditorOnly
{
    /// One-click generator for the canonical ThinIce v2 config + the two VFX prefabs
    /// it references. Idempotent: re-running picks up existing assets at the canonical
    /// paths and only re-creates what's missing, so designer-tweaked prefabs survive.
    ///
    /// Outputs:
    ///   - Assets/Resources/ThinIceV2Config.asset      (auto-loaded by ThinIcePlatformV2)
    ///   - Assets/Prebs/ThinIceV2/DebrisVFX.prefab
    ///   - Assets/Prebs/ThinIceV2/FrostCloudVFX.prefab
    ///   - Assets/GeneratedShards/SharedThinIceShardSet.asset (delegated)
    ///   - Assets/Prebs/ThinIceV2_ShardRuntime.prefab          (preexisting, just picked up)
    public static class ThinIceV2ConfigGenerator
    {
        const string ConfigPath          = "Assets/Resources/ThinIceV2Config.asset";
        const string DebrisPrefabPath    = "Assets/Prebs/ThinIceV2/DebrisVFX.prefab";
        const string FrostPrefabPath     = "Assets/Prebs/ThinIceV2/FrostCloudVFX.prefab";
        const string ShardPrefabPath     = "Assets/Prebs/ThinIceV2_ShardRuntime.prefab";
        const string ShardSetPath        = "Assets/GeneratedShards/SharedThinIceShardSet.asset";
        const string MaterialPath        = "Assets/Shaders/ThinIceV2.mat";
        const string LegacyRampPngPath   = "Assets/Settings/ThinIceV2/ThinIceV2_DefaultRamp.png";
        const string LegacyRampFolder    = "Assets/Settings/ThinIceV2";

        [MenuItem("SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs")]
        public static void GenerateMenu()
        {
            EnsureFolder("Assets/Resources");
            EnsureFolder("Assets/Prebs/ThinIceV2");

            var debrisPrefab = LoadOrCreatePrefab(DebrisPrefabPath, "DebrisVFX", ConfigureDebrisVFX);
            var frostPrefab  = LoadOrCreatePrefab(FrostPrefabPath,  "FrostCloudVFX", ConfigureFrostCloudVFX);

            var shardSet = AssetDatabase.LoadAssetAtPath<ThinIceShardSet>(ShardSetPath);
            if (shardSet == null)
            {
                ThinIceSharedShardSetGenerator.GenerateMenu();
                shardSet = AssetDatabase.LoadAssetAtPath<ThinIceShardSet>(ShardSetPath);
            }

            var shardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShardPrefabPath);
            if (shardPrefab == null)
            {
                Debug.LogWarning(
                    $"[ThinIceV2ConfigGenerator] Shard runtime prefab missing at {ShardPrefabPath}. " +
                    "Config will reference null — create the prefab manually (MeshFilter + MeshRenderer + " +
                    "Rigidbody2D + ThinIceShardRuntime) or recover from version control.");
            }

            var config = AssetDatabase.LoadAssetAtPath<ThinIceV2Config>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ThinIceV2Config>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.shardSet            = shardSet;
            config.shardRuntimePrefab  = shardPrefab;
            config.debrisVFXPrefab     = debrisPrefab;
            config.frostCloudVFXPrefab = frostPrefab;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            // Quibli toon shading needs a ramp texture in `_GradientRamp` to produce
            // the signature stepped shadow — without one the shader falls back to
            // Unity's white default texture and looks flat-lit.
            EnsureDefaultRampAssigned();

            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
            string shardSetName    = shardSet    != null ? shardSet.name    : "<missing>";
            string shardPrefabName = shardPrefab != null ? shardPrefab.name : "<missing>";
            Debug.Log(
                $"[ThinIceV2ConfigGenerator] Config ready at {ConfigPath}.\n" +
                $"- shardSet            = {shardSetName}\n" +
                $"- shardRuntimePrefab  = {shardPrefabName}\n" +
                $"- debrisVFXPrefab     = {debrisPrefab.name}\n" +
                $"- frostCloudVFXPrefab = {frostPrefab.name}", config);
        }

        // --------------------------------------------------------------------

        // Idempotent prefab create-or-load. If the prefab exists, return it untouched —
        // designers may have hand-tuned the curves/colors and we don't want to stomp.
        static GameObject LoadOrCreatePrefab(string path, string goName, System.Action<ParticleSystem> configure)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;

            var go = new GameObject(goName);
            var ps = go.AddComponent<ParticleSystem>();
            configure(ps);

            var saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved;
        }

        // Telegraph debris: small mesh chips emerging from across the platform face.
        // The platform itself reconfigures the emit shape to a Box matching the collider
        // bounds in Awake, so we just set the renderer + base curves here.
        static void ConfigureDebrisVFX(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.08f);
            main.startColor = new Color(0.7f, 0.92f, 1f, 1f);
            main.gravityModifier = 1.0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;

            var emission = ps.emission;
            emission.rateOverTime = 0f; // ThinIcePlatformV2 drives this dynamically.

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale     = new Vector3(1f, 0.2f, 0.05f);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
                if (mat != null) renderer.sharedMaterial = mat;
                renderer.sortingOrder = 1;
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                var cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                if (cubeMesh != null) renderer.SetMeshes(new[] { cubeMesh });
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void ConfigureFrostCloudVFX(ParticleSystem ps)
        {
            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startColor = new Color(0.9f, 0.97f, 1f, 0.7f);
            main.gravityModifier = -0.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Particle.mat");
                if (mat != null) renderer.sharedMaterial = mat;
                renderer.sortingOrder = 2;
            }

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf   = Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        // --------------------------------------------------------------------
        // Default toon gradient. The shader uses Quibli's [Gradient] attribute to
        // expose an inline Unity Gradient picker — the picker bakes its result into
        // a sub-asset of the .mat with a name encoding "z_<prop>Tex<json>" (Quibli's
        // MaterialGradientDrawer convention). To give the user a sensible starting
        // point we mirror that convention here: bake a cool-blue→white step gradient
        // as a sub-asset of the .mat the first time the generator runs. After that
        // the inspector's gradient picker takes over — the user can edit inline and
        // the drawer reuses the same sub-asset.
        //
        // Older versions of this generator wrote a separate PNG at
        // Assets/Settings/ThinIceV2/ThinIceV2_DefaultRamp.png. We migrate by
        // detecting that asset reference and clearing it (the PNG file itself is
        // left on disk so designers can recover it, but the folder is removed if
        // empty).
        // --------------------------------------------------------------------

        const string GradientPropName = "_GradientRamp";

        static void EnsureDefaultRampAssigned()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                Debug.LogWarning($"[ThinIceV2ConfigGenerator] Material missing at {MaterialPath} — skipping ramp wiring.");
                return;
            }
            if (!mat.HasProperty(GradientPropName)) return;

            MigrateLegacyRampPng(mat);

            // Skip if the material already points at a sub-asset gradient — leaves
            // designer-tweaked gradients alone on re-runs (idempotent).
            var existing = mat.GetTexture(GradientPropName) as Texture2D;
            if (existing != null && AssetDatabase.IsSubAsset(existing)) return;

            var grad = BuildDefaultIceGradient();
            BakeGradientIntoMaterialSubAsset(mat, GradientPropName, grad);
        }

        // If the material currently references the legacy generator-written PNG,
        // clear that reference and remove the (now-empty) folder so the gradient
        // drawer's first-paint state is clean.
        static void MigrateLegacyRampPng(Material mat)
        {
            var legacy = AssetDatabase.LoadAssetAtPath<Texture2D>(LegacyRampPngPath);
            if (legacy == null) return;

            if (mat.GetTexture(GradientPropName) == legacy)
            {
                mat.SetTexture(GradientPropName, null);
                EditorUtility.SetDirty(mat);
            }
            // Delete the PNG outright — fully replaced by the inline gradient picker.
            AssetDatabase.DeleteAsset(LegacyRampPngPath);
            if (AssetDatabase.IsValidFolder(LegacyRampFolder) &&
                AssetDatabase.FindAssets(string.Empty, new[] { LegacyRampFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(LegacyRampFolder);
            }
        }

        static Gradient BuildDefaultIceGradient()
        {
            // Cool-blue → near-white step. Two pairs of tightly-spaced keys at 0.47
            // and 0.53 produce the characteristic Quibli toon "step" — the linear
            // interpolation between them looks like a hard edge at NdotL ≈ 0.5.
            var g = new Gradient { mode = GradientMode.Blend };
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(108f/255f, 148f/255f, 195f/255f), 0.00f),
                    new GradientColorKey(new Color(170f/255f, 202f/255f, 232f/255f), 0.47f),
                    new GradientColorKey(new Color(245f/255f, 250f/255f, 254f/255f), 0.53f),
                    new GradientColorKey(Color.white,                                  1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                });
            return g;
        }

        // Bake gradient into a Texture2D and add it as a sub-asset of `mat`.
        // The texture's name encodes the gradient JSON in the exact format
        // Quibli's MaterialGradientDrawer uses, so the inspector's gradient picker
        // can decode and display the current state.
        static void BakeGradientIntoMaterialSubAsset(Material mat, string propName, Gradient grad)
        {
            const int W = 256;
            var tex = new Texture2D(W, 1, TextureFormat.ARGB32, mipChain: false, linear: false)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = grad.mode == GradientMode.Blend ? FilterMode.Bilinear : FilterMode.Point,
            };
            var pixels = new Color[W];
            for (int x = 0; x < W; x++) pixels[x] = grad.Evaluate(x / (float)(W - 1));
            tex.SetPixels(pixels);
            tex.Apply();

            string json = JsonUtility.ToJson(new GradientRepresentation(grad));
            tex.name = $"z_{propName}Tex{json}";

            string matPath = AssetDatabase.GetAssetPath(mat);
            AssetDatabase.AddObjectToAsset(tex, matPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(matPath);

            mat.SetTexture(propName, tex);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssetIfDirty(mat);
        }

        // Mirror of MaterialGradientDrawer.GradientRepresentation. JsonUtility
        // matches by field shape, so as long as the names + types align the
        // drawer's Decode() will recover the gradient on inspector open.
        [System.Serializable]
        class GradientRepresentation
        {
            public GradientMode mode;
            public ColorKey[] colorKeys;
            public AlphaKey[] alphaKeys;

            public GradientRepresentation() { }
            public GradientRepresentation(Gradient source)
            {
                mode = source.mode;
                colorKeys = source.colorKeys.Select(k => new ColorKey { color = k.color, time = k.time }).ToArray();
                alphaKeys = source.alphaKeys.Select(k => new AlphaKey { alpha = k.alpha, time = k.time }).ToArray();
            }

            [System.Serializable]
            public struct ColorKey { public Color color; public float time; }

            [System.Serializable]
            public struct AlphaKey { public float alpha; public float time; }
        }
    }
}
