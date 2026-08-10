using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SlimeJumping.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace SlimeJumping.EditorTools
{
    /// Every sound in the project, one row per SoundDef asset. Because a SoundDef carries its own clips,
    /// level, pitch and bus, the list needs no guessing: the volume shown is the volume that plays, and
    /// ▶ auditions at exactly that volume. Two extra sections keep the picture honest — clips that no
    /// SoundDef uses, and the components still holding a raw AudioClip or AudioSource.
    public class SoundInventoryWindow : EditorWindow
    {
        static readonly string[] ExcludedPathParts = { "/ThirdParty/", "/Plugins/", "/AssetVault/" };

        readonly List<SoundDef> defs = new List<SoundDef>();
        readonly List<string> unusedClipPaths = new List<string>();
        readonly List<string> legacyOwners = new List<string>();
        readonly HashSet<string> usedClipPaths = new HashSet<string>();

        AudioSource previewSource;
        SoundSettings settings;
        Vector2 scroll;
        string search = "";
        bool onlyMissing;
        bool unusedExpanded, legacyExpanded;
        bool scanned;

        [MenuItem("Tools/Sound Inventory")]
        static void Open() => GetWindow<SoundInventoryWindow>("Sound Inventory");

        void OnEnable()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorApplication.projectChanged += OnProjectChanged;

            var go = new GameObject("SoundInventoryPreviewPlayer") { hideFlags = HideFlags.HideAndDontSave };
            previewSource = go.AddComponent<AudioSource>();
            previewSource.playOnAwake = false;
            previewSource.loop = false;
            previewSource.spatialBlend = 0f;
        }

        void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorApplication.projectChanged -= OnProjectChanged;
            if (previewSource != null) DestroyImmediate(previewSource.gameObject);
        }

        void OnSceneOpened(Scene scene, OpenSceneMode mode) => scanned = false;
        void OnSceneClosed(Scene scene) => scanned = false;
        void OnProjectChanged() => scanned = false;   // a SoundDef added/deleted outside this window

        void OnGUI()
        {
            if (!scanned) Rescan();

            // The editor audio engine only advances while the loop is being pumped.
            if (previewSource != null && previewSource.isPlaying) Repaint();

            DrawSettingsPanel();
            DrawToolbar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (SoundCategory cat in Enum.GetValues(typeof(SoundCategory))) DrawCategory(cat);
            DrawUnusedClips();
            DrawLegacy();
            EditorGUILayout.EndScrollView();
        }

        // ---------- header ----------

        void DrawSettingsPanel()
        {
            EditorGUILayout.LabelField("Category → Mixer Group", EditorStyles.boldLabel);
            if (settings == null)
            {
                EditorGUILayout.HelpBox("SoundSettings asset not found.", MessageType.Error);
                return;
            }

            var seen = new Dictionary<string, SoundCategory>();
            foreach (SoundCategory cat in Enum.GetValues(typeof(SoundCategory)))
            {
                var group = GroupFor(cat);
                string name = group != null ? group.name : "NULL";
                string warn = group == null ? "  ⚠ 미지정 → Master 직결"
                            : seen.ContainsKey(name) ? $"  ⚠ {seen[name]} 와 같은 그룹" : "";
                if (group != null && !seen.ContainsKey(name)) seen[name] = cat;
                EditorGUILayout.LabelField($"    {cat}", name + warn);
            }
            EditorGUILayout.Space(4);
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("⟳ 다시 검색", EditorStyles.toolbarButton, GUILayout.Width(84))) Rescan();
            if (GUILayout.Button("■ 정지", EditorStyles.toolbarButton, GUILayout.Width(52)) && previewSource != null)
                previewSource.Stop();
            GUILayout.Space(8);
            onlyMissing = GUILayout.Toggle(onlyMissing, "클립 미배정", EditorStyles.toolbarButton, GUILayout.Width(76));
            GUILayout.Label($"{defs.Count} sounds", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 새 사운드", EditorStyles.toolbarButton, GUILayout.Width(78))) CreateNewDef();
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField,
                                               GUILayout.MinWidth(80), GUILayout.MaxWidth(200));
            EditorGUILayout.EndHorizontal();
        }

        // ---------- rows ----------

        void DrawCategory(SoundCategory cat)
        {
            var rows = defs.Where(d => d.category == cat).Where(Passes).ToList();
            if (rows.Count == 0) return;

            EditorGUILayout.LabelField($"{cat}   ×{rows.Count}", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            for (int i = 0; i < rows.Count; i++) DrawDef(rows[i]);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
        }

        // No SerializedObject per row — OnGUI runs on every editor repaint.
        void DrawDef(SoundDef def)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(def.name, EditorStyles.miniBoldLabel, GUILayout.MinWidth(120));

            using (new EditorGUI.DisabledScope(!def.HasClip))
            {
                if (GUILayout.Button(new GUIContent("▶", $"volume {def.volumeRange.x:0.00} 로 재생"),
                                     GUILayout.Width(24)))
                    PlayPreview(def.PickClip(), def.PickVolume());
            }

            EditorGUI.BeginChangeCheck();
            float lo = EditorGUILayout.Slider(def.volumeRange.x, 0f, 1f, GUILayout.MaxWidth(160));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(def, "Sound Volume");
                bool wasFixed = Mathf.Approximately(def.volumeRange.x, def.volumeRange.y);
                def.volumeRange = new Vector2(lo, wasFixed ? lo : Mathf.Max(lo, def.volumeRange.y));
                EditorUtility.SetDirty(def);
            }

            GUILayout.Label(ClipSummary(def), EditorStyles.miniLabel, GUILayout.MinWidth(140));
            if (!def.HasClip) GUILayout.Label("미배정 ⚠", EditorStyles.miniLabel, GUILayout.Width(56));

            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                Selection.activeObject = def;
                EditorGUIUtility.PingObject(def);
            }
            EditorGUILayout.EndHorizontal();
        }

        static string ClipSummary(SoundDef def)
        {
            if (!def.HasClip) return "(클립 없음)";
            if (def.clips.Length == 1) return def.clips[0] != null ? def.clips[0].name : "(null)";
            return $"{def.clips[0].name} 외 {def.clips.Length - 1}개 · {def.clipOrder}";
        }

        void DrawUnusedClips()
        {
            if (unusedClipPaths.Count == 0) return;
            EditorGUILayout.Space(6);
            unusedExpanded = EditorGUILayout.Foldout(
                unusedExpanded, $"어느 SoundDef 에도 안 쓰인 클립  ×{unusedClipPaths.Count}", true, EditorStyles.foldoutHeader);
            if (!unusedExpanded) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < unusedClipPaths.Count; i++)
            {
                string path = unusedClipPaths[i];
                if (search.Length > 0 && path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(path), EditorStyles.miniLabel);
                if (GUILayout.Button("▶", GUILayout.Width(24)))
                    PlayPreview(AssetDatabase.LoadAssetAtPath<AudioClip>(path), 1f);
                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    Selection.activeObject = clip;
                    EditorGUIUtility.PingObject(clip);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        // Kept off the main Rescan: it builds a SerializedObject per component in every loaded scene.
        void DrawLegacy()
        {
            EditorGUILayout.Space(6);
            legacyExpanded = EditorGUILayout.Foldout(
                legacyExpanded, "SoundDef 를 안 쓰는 잔여 오브젝트", true, EditorStyles.foldoutHeader);
            if (!legacyExpanded) return;

            EditorGUI.indentLevel++;
            if (GUILayout.Button($"씬 검사 ({legacyOwners.Count} found)", GUILayout.Width(180)))
                ScanLegacy();
            for (int i = 0; i < legacyOwners.Count; i++)
                EditorGUILayout.LabelField(legacyOwners[i], EditorStyles.miniLabel);
            EditorGUI.indentLevel--;
        }

        bool Passes(SoundDef def)
        {
            if (onlyMissing && def.HasClip) return false;
            if (search.Length == 0) return true;
            if (def.name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (def.clips == null) return false;
            for (int i = 0; i < def.clips.Length; i++)
                if (def.clips[i] != null &&
                    def.clips[i].name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ---------- preview ----------

        /// Plays through a window-owned AudioSource, not AudioUtil.PlayPreviewClip — that API takes no
        /// volume. Copied from IntuitiveAudioAdjust, which previews the same way.
        void PlayPreview(AudioClip clip, float volume)
        {
            if (clip == null || previewSource == null) return;
            previewSource.Stop();
            previewSource.clip = clip;
            previewSource.volume = Mathf.Clamp01(volume);
            previewSource.Play();
            Repaint();
        }

        // ---------- scan ----------

        void Rescan()
        {
            scanned = true;
            defs.Clear();
            unusedClipPaths.Clear();
            legacyOwners.Clear();
            usedClipPaths.Clear();
            settings = LoadSettings();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SoundDef)))
            {
                var def = AssetDatabase.LoadAssetAtPath<SoundDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;
                defs.Add(def);
                if (def.clips == null) continue;
                for (int i = 0; i < def.clips.Length; i++)
                {
                    if (def.clips[i] == null) continue;
                    string p = AssetDatabase.GetAssetPath(def.clips[i]);
                    if (!string.IsNullOrEmpty(p)) usedClipPaths.Add(p);
                }
            }
            defs.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (usedClipPaths.Contains(path)) continue;
                if (ExcludedPathParts.Any(part => path.Contains(part))) continue;
                unusedClipPaths.Add(path);
            }
            unusedClipPaths.Sort(StringComparer.OrdinalIgnoreCase);
        }

        // Anything still holding a raw AudioClip or its own AudioSource — the migration's remainder.
        void ScanLegacy()
        {
            legacyOwners.Clear();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    foreach (var c in root.GetComponentsInChildren<Component>(true))
                    {
                        if (c == null) continue;
                        if (c is AudioSource) { legacyOwners.Add($"AudioSource — {HierarchyPath(c.transform)}"); continue; }
                        var type = c.GetType();
                        if (type.Namespace == null || !type.Namespace.StartsWith("SlimeJumping")) continue;
                        if (HoldsRawClip(c)) legacyOwners.Add($"{type.Name} — {HierarchyPath(c.transform)}");
                    }
            }
            legacyOwners.Sort(StringComparer.OrdinalIgnoreCase);
        }

        static bool HoldsRawClip(UnityEngine.Object owner)
        {
            var so = new SerializedObject(owner);
            var p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = p.depth < 3;
                if (p.propertyType == SerializedPropertyType.ObjectReference && p.type == "PPtr<$AudioClip>")
                    return true;
            }
            return false;
        }

        void CreateNewDef()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings/Sounds"))
                AssetDatabase.CreateFolder("Assets/Settings", "Sounds");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Settings/Sounds/Sound_New.asset");
            var def = CreateInstance<SoundDef>();
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            scanned = false;
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
        }

        // ---------- resolution ----------

        static SoundSettings LoadSettings()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(SoundSettings));
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<SoundSettings>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        AudioMixerGroup GroupFor(SoundCategory cat)
        {
            if (settings == null) return null;
            switch (cat)
            {
                case SoundCategory.BGM: return settings.bgmGroup;
                case SoundCategory.SFX_Slime: return settings.sfxSlimeGroup;
                case SoundCategory.SFX_Environment: return settings.sfxEnvironmentGroup;
                case SoundCategory.SFX_Object: return settings.sfxObjectGroup;
                case SoundCategory.UI: return settings.uiGroup;
            }
            return null;
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            var cur = t.parent;
            while (cur != null) { path = cur.name + "/" + path; cur = cur.parent; }
            return path;
        }
    }
}
