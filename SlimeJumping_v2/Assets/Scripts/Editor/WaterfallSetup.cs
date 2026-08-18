using UnityEditor;
using UnityEngine;
using SlimeJumping.VFX.Waterfall;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// 물레방아 폭포 하류(턱 중앙 웅덩이 + 낙수 방울) 셋업.
    ///
    /// 재실행해도 안전하다 — 이미 있는 루트·머티리얼·config 는 다시 만들지 않고 참조만 다시 잇는다.
    /// 기존 씬 오브젝트는 건드리지 않는다.
    ///
    /// 컴포넌트의 Rebuild 를 직접 부른다. OnValidate 는 delayCall 로 미뤄지므로
    /// 셋업 직후 같은 호출에서 검증하면 메시가 아직 없다.
    /// </summary>
    static class WaterfallSetup
    {
        const string RootName = "Waterfall_Downstream";
        const string ConfigPath = "Assets/Resources/WaterfallConfig.asset";
        const string MatDir = "Assets/Art/Materials/Waterfall";
        const string ShaderName = "SlimeJumping/Water_FlowingRefraction";
        const string MudPath = "stone/m6 1/Mud_BézierCurve";

        [MenuItem("SlimeJumping/Mechanism/Setup Waterfall (Downstream)")]
        static void Setup()
        {
            var cfg = EnsureConfig();
            var noise = FindNoise();

            var matPuddle = EnsureMaterial("M_WaterLedgePuddle", noise, m =>
            {
                m.SetColor("_BaseColor", new Color(0.07f, 0.24f, 0.46f, 0.70f));
                m.SetFloat("_TintStrength", 0.90f);
                m.SetFloat("_FlowSpeed", 0.10f);
                m.SetVector("_NoiseScale", new Vector4(2.2f, 2.2f, 0f, 0f));
                m.SetFloat("_DistortionStrength", 0.02f);
                m.SetFloat("_EdgeFadePower", 1.4f);
                m.SetFloat("_EdgeNoise", 0.22f);
                m.SetFloat("_Turbulence", 0.18f);
                m.SetFloat("_CrestAmplitude", 0f);
            });

            var root = GameObject.Find(RootName);
            if (root == null)
            {
                root = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(root, "Setup Waterfall");
            }
            root.transform.position = Vector3.zero;

            var puddle = EnsureChild<LedgePuddle>(root, "LedgePuddle",
                new Vector3(cfg.puddleAreaXMin, cfg.puddleWaterY, cfg.puddleAreaZMin), 1, matPuddle);
            if (puddle.GetComponent<BoxCollider2D>() == null)
                puddle.gameObject.AddComponent<BoxCollider2D>().isTrigger = true;

            var mud = GameObject.Find(MudPath);
            var pso = new SerializedObject(puddle);
            pso.FindProperty("terrainSource").objectReferenceValue =
                mud != null ? mud.GetComponent<MeshFilter>() : null;
            pso.ApplyModifiedPropertiesWithoutUndo();

            puddle.Rebuild();

            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
        }

        static T EnsureChild<T>(GameObject root, string name, Vector3 pos, int sortingOrder,
                                params Material[] mats) where T : Component
        {
            var tr = root.transform.Find(name);
            GameObject go;
            if (tr == null)
            {
                go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Setup Waterfall");
            }
            else go = tr.gameObject;

            go.transform.position = pos;
            if (go.GetComponent<MeshFilter>() == null) go.AddComponent<MeshFilter>();

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.sortingOrder = sortingOrder;

            var comp = go.GetComponent<T>();
            if (comp == null) comp = go.AddComponent<T>();
            return comp;
        }

        static WaterfallConfig EnsureConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<WaterfallConfig>(ConfigPath);
            if (cfg != null) return cfg;

            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            cfg = ScriptableObject.CreateInstance<WaterfallConfig>();
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            AssetDatabase.SaveAssets();
            return cfg;
        }

        static Material EnsureMaterial(string name, Texture noise, System.Action<Material> init)
        {
            string path = $"{MatDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(MatDir))
                AssetDatabase.CreateFolder("Assets/Art/Materials", "Waterfall");

            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[WaterfallSetup] shader not found: {ShaderName}"); return null; }

            var mat = new Material(shader) { name = name };
            init(mat);
            if (noise != null) mat.SetTexture("_NoiseTex", noise);
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Texture FindNoise()
        {
            var guids = AssetDatabase.FindAssets("Foliage_Fill_PerlinNoiseCloud_Tiling t:Texture2D");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }
    }
}
