#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using SlimeJumping.Effects;
using SlimeJumping.Rendering;

namespace SlimeJumping.EditorTools
{
    public static class SliceEffectPrefabSetup
    {
        private const string PrefabPath = "Assets/Prebs/VFX/Slice_Hit.prefab";
        private const string PaletteAssetPath = "Assets/Settings/VFX/DefaultVFXPalette.asset";

        [MenuItem("SlimeJumping/VFX/Create or Update Slice_Hit Prefab")]
        public static void CreateOrUpdate()
        {
            GameObject root = new GameObject("Slice_Hit");
            try
            {
                var ps = root.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 0.1f;
                main.loop = false;
                main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
                main.gravityModifier = 0.8f;
                main.stopAction = ParticleSystemStopAction.None;
                main.maxParticles = 64;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 15, 25, 1, 0.01f) });

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 35f;
                shape.radius = 0.05f;
                shape.rotation = new Vector3(-90f, 0f, 0f); // cone axis along local +Y

                var col = ps.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) }
                );
                col.color = new ParticleSystem.MinMaxGradient(grad);

                var renderer = root.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-ParticleSystem.mat");
                if (mat != null) renderer.sharedMaterial = mat;

                var binder = root.AddComponent<ParticlePaletteBinder>();
                var paletteProp = new SerializedObject(binder);
                var pal = AssetDatabase.LoadAssetAtPath<VFXColorPalette>(PaletteAssetPath);
                paletteProp.FindProperty("palette").objectReferenceValue = pal;
                paletteProp.FindProperty("vfxType").enumValueIndex = (int)VFXType.Slice;
                paletteProp.ApplyModifiedPropertiesWithoutUndo();

                var audio = root.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 1f;
                audio.rolloffMode = AudioRolloffMode.Linear;
                audio.minDistance = 3f;
                audio.maxDistance = 25f;

                var slice = root.AddComponent<SliceEffect>();
                var sliceSo = new SerializedObject(slice);
                sliceSo.FindProperty("particles").objectReferenceValue = ps;
                sliceSo.FindProperty("audioSource").objectReferenceValue = audio;
                sliceSo.ApplyModifiedPropertiesWithoutUndo();

                var dir = System.IO.Path.GetDirectoryName(PrefabPath);
                if (!AssetDatabase.IsValidFolder(dir))
                    System.IO.Directory.CreateDirectory(dir);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[SliceEffectPrefabSetup] Saved {PrefabPath}");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif
