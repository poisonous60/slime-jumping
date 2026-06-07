using System.IO;
using UnityEngine;
using UnityEditor;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Mechanisms;
using SlimeJumping.Settings;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// Wires up the slime-split reaction so it can be seen immediately, and a
    /// one-click "trigger now" so you don't have to land a real high-speed hit.
    ///
    /// Setup (edit mode): creates the SlimeSplitSettings asset + a Mat_SlimeChunk
    /// material variant (so chunk look is tunable independently of the live slime),
    /// adds a SlimeSplitManager with SlimeSplitSequence, and points every Knife/Saw
    /// hazard in the open scene at it.
    ///
    /// Trigger (play mode): slices the live slime on the spot.
    /// </summary>
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
            // proxyMeshOverride / meshScaleSource left null → auto-found from the slime's 3D body mesh.
            so.ApplyModifiedProperties();

            int wired = 0;
            foreach (var saw in Object.FindObjectsByType<SawBladeHazard>(FindObjectsSortMode.None))
                wired += WireHazard(saw, seq);
            foreach (var knife in Object.FindObjectsByType<KnifeHazard>(FindObjectsSortMode.None))
                wired += WireHazard(knife, seq);

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

        // DynamicMeshCutter reads CPU mesh data at runtime, which requires Read/Write
        // enabled on the cut source mesh. This only changes the import flag (keeps a CPU
        // copy) — rendering is byte-for-byte identical, so the live slime is unaffected.
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
