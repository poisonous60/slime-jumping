using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.Settings;

namespace SlimeJumping.Editor
{
    /// <summary>Edit mode: builds the split settings/chunk material and wires every Knife/Saw hazard.
    /// Play mode: slices the live slime on demand.</summary>
    public static class SlimeSplitTestSetup
    {
        private const string SettingsPath = "Assets/Settings/SlimeSplitSettings.asset";
        private const string ChunkMatPath = "Assets/Art/Shader/Mat_SlimeChunk.mat";
        private const string SlimeBodyMatPath = "Assets/Art/Shader/Mat_SlimeBody3D_Quibli.mat";

        [MenuItem("SlimeJumping/Effects/Setup Slime Split Test")]
        public static void Setup()
        {
            var settings = GetOrCreateSettings();
            var chunkMat = GetOrCreateChunkMaterial();

            var slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogError("[SlimeSplitTest] No SlimeController in the open scene. Open the gameplay scene first.");
                return;
            }

            EnsureSlimeMeshReadable(slime);

            var managerGo = GameObject.Find("SlimeSplitManager");
            if (managerGo == null)
            {
                managerGo = new GameObject("SlimeSplitManager");
                Undo.RegisterCreatedObjectUndo(managerGo, "Create SlimeSplitManager");
            }
            var seq = managerGo.GetComponent<SlimeSplitSequence>();
            if (seq == null) seq = Undo.AddComponent<SlimeSplitSequence>(managerGo);

            var so = new SerializedObject(seq);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.FindProperty("chunkMaterial").objectReferenceValue = chunkMat;
            // Body mesh, face renderers and SlimeVisual are resolved from the slime in Begin().
            so.ApplyModifiedProperties();

            // Body-scale spring tuning shares this asset; without it the displayed size snaps.
            var visual = slime.GetComponentInChildren<SlimeVisual>(true);
            if (visual != null)
            {
                var vso = new SerializedObject(visual);
                var sp = vso.FindProperty("splitSettings");
                if (sp != null)
                {
                    sp.objectReferenceValue = settings;
                    vso.ApplyModifiedProperties();
                }
            }

            int wired = 0;
            foreach (var saw in Object.FindObjectsByType<SawBladeHazard>(FindObjectsSortMode.None))
                wired += WireHazard(saw, seq);
            foreach (var knife in Object.FindObjectsByType<KnifeHazard>(FindObjectsSortMode.None))
                wired += WireHazard(knife, seq);

            // Face slice material on eyes + mouth so the face clips with the wound.
            SlimeFaceSliceSetup.Wire();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = managerGo;

            Debug.Log($"[SlimeSplitTest] Setup complete. Settings={SettingsPath}, ChunkMat={ChunkMatPath}, hazards wired={wired}.\n" +
                      "Tune the cut in the SlimeSplitSettings asset. Press Play, then run\n" +
                      "'SlimeJumping/Effects/Trigger Split Now (Play Mode)' to see it instantly.");
        }

        [MenuItem("SlimeJumping/Effects/Trigger Split Now (Play Mode)")]
        public static void TriggerNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SlimeSplitTest] Enter Play Mode first, then trigger.");
                return;
            }
            var slime = Object.FindFirstObjectByType<SlimeController>();
            var seq = Object.FindFirstObjectByType<SlimeSplitSequence>();
            if (slime == null || seq == null)
            {
                Debug.LogError("[SlimeSplitTest] Need a SlimeController and a SlimeSplitSequence in the scene (run Setup first).");
                return;
            }
            // Cut angle follows entry velocity; use a synthetic one if the slime is at rest.
            Vector2 entry = slime.Velocity.sqrMagnitude > 0.5f ? slime.Velocity : Vector2.right * 8f;
            seq.Begin(slime, entry);
            Debug.Log("[SlimeSplitTest] Split triggered.");
        }

        // Volume/visual side of the mid-air self-split only; the recoil impulse is gameplay's job.
        [MenuItem("SlimeJumping/Effects/Trigger Self Split Now (Play Mode)")]
        public static void TriggerSelfSplitNow()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SlimeSplitTest] Enter Play Mode first, then trigger.");
                return;
            }
            var slime = Object.FindFirstObjectByType<SlimeController>();
            var seq = Object.FindFirstObjectByType<SlimeSplitSequence>();
            if (slime == null || seq == null)
            {
                Debug.LogError("[SlimeSplitTest] Need a SlimeController and a SlimeSplitSequence in the scene (run Setup first).");
                return;
            }
            seq.BeginSelfSplit(slime, Vector2.down);
            Debug.Log("[SlimeSplitTest] Self split triggered.");
        }

        // Forces the physics ratio directly, bypassing the split sequence and heal timeline.
        [MenuItem("SlimeJumping/Effects/Body Scale 0.6 (Play Mode)")]
        public static void ForceBodyScaleSmall() => ForceBodyScale(0.6f);

        [MenuItem("SlimeJumping/Effects/Body Scale 1.0 (Play Mode)")]
        public static void ForceBodyScaleFull() => ForceBodyScale(1f);

        private static void ForceBodyScale(float ratio)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SlimeSplitTest] Enter Play Mode first.");
                return;
            }
            var slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogError("[SlimeSplitTest] No SlimeController in the scene.");
                return;
            }
            slime.SetBodyScale(ratio);
            Debug.Log($"[SlimeSplitTest] BodyScale forced to {ratio:F2}.");
        }

        // DynamicMeshCutter reads CPU mesh data at runtime — the source mesh import must have
        // Read/Write enabled or the cut fails.
        private static void EnsureSlimeMeshReadable(SlimeController slime)
        {
            var mf = slime.GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null) return;
            string path = AssetDatabase.GetAssetPath(mf.sharedMesh);
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) return;   // not an imported model (e.g. a baked .asset mesh)
            if (!imp.isReadable)
            {
                imp.isReadable = true;
                imp.SaveAndReimport();
                Debug.Log($"[SlimeSplitTest] Enabled Read/Write on {path} (required by DynamicMeshCutter; render-identical).");
            }
        }

        private static int WireHazard(Object hazard, SlimeSplitSequence seq)
        {
            var so = new SerializedObject(hazard);
            var p = so.FindProperty("splitSequence");
            if (p == null) return 0;
            p.objectReferenceValue = seq;
            var thr = so.FindProperty("splitSpeedThreshold");
            if (thr != null) thr.floatValue = 4f;   // reachable in casual testing
            so.ApplyModifiedProperties();
            return 1;
        }

        private static SlimeSplitSettings GetOrCreateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<SlimeSplitSettings>(SettingsPath);
            if (existing != null) return existing;

            EnsureFolder(Path.GetDirectoryName(SettingsPath));
            var s = ScriptableObject.CreateInstance<SlimeSplitSettings>();
            AssetDatabase.CreateAsset(s, SettingsPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SlimeSplitTest] Created {SettingsPath}");
            return s;
        }

        private static Material GetOrCreateChunkMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(ChunkMatPath);
            if (existing != null) return existing;

            var src = AssetDatabase.LoadAssetAtPath<Material>(SlimeBodyMatPath);
            if (src == null)
            {
                Debug.LogWarning($"[SlimeSplitTest] {SlimeBodyMatPath} not found; chunks will fall back to the live slime material.");
                return null;
            }
            EnsureFolder(Path.GetDirectoryName(ChunkMatPath));
            var variant = new Material(src) { name = "Mat_SlimeChunk" };
            AssetDatabase.CreateAsset(variant, ChunkMatPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SlimeSplitTest] Created {ChunkMatPath} (copy of Mat_SlimeBody3D_Quibli)");
            return variant;
        }

        private static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
