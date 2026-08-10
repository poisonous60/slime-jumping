using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SlimeJumping.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace SlimeJumping.EditorTools
{
    /// Every sound the project actually wires up, in one list: loaded scenes, prefabs and config
    /// assets are swept for AudioClip references, grouped by the component that owns them, with the
    /// clip and its volume editable in place. Requirements it exists to serve:
    ///   - find a sound without knowing which object holds it (a raw AudioSource has no script to grep)
    ///   - see which mixer category each sound plays through, so UI SFX landing in BGM is visible
    ///   - list clip slots that are still empty — the sounds not added yet
    ///   - keep working as sounds are added, without a registry to maintain
    /// The scan is reflection-driven, so a new [SerializeField] AudioClip shows up with no edit here.
    public class SoundInventoryWindow : EditorWindow
    {
        const string FolderPrefKey = "SlimeJumping.SoundInventory.Folders";
        const string DefaultFolders = "Assets/Resources,Assets/Settings,Assets/Prefabs";
        static readonly string[] ExcludedPathParts = { "/ThirdParty/", "/Plugins/", "/AssetVault/" };

        class Slot
        {
            public string label;
            public string propPath;
            public AudioClip clip;
        }

        class VolumeField
        {
            public string label;
            public string propPath;
            public float value;
        }

        class Entry
        {
            public UnityEngine.Object owner;
            public string ownerLabel;
            public string origin;
            public AudioSource source;          // set when the owner IS an AudioSource
            public List<Slot> slots = new List<Slot>();
            public List<VolumeField> volumes = new List<VolumeField>();
            public string category;
            public bool warnNoGroup;
            public bool warnRaw;
            public bool warnMissing;
        }

        class TypeGroup
        {
            public string typeName;
            public string category;
            public List<Entry> entries = new List<Entry>();
            public bool expanded;
            public bool AnyWarning => entries.Any(e => e.warnNoGroup || e.warnRaw || e.warnMissing);
        }

        readonly List<TypeGroup> groups = new List<TypeGroup>();
        readonly List<string> unusedClipPaths = new List<string>();
        readonly HashSet<string> usedClipPaths = new HashSet<string>();

        static readonly Dictionary<Type, bool> hasClipCache = new Dictionary<Type, bool>();
        static readonly Dictionary<Type, List<FieldInfo>> volumeFieldCache = new Dictionary<Type, List<FieldInfo>>();
        static readonly Dictionary<Type, string> categoryCache = new Dictionary<Type, string>();

        SoundSettings settings;
        Vector2 scroll;
        string search = "";
        bool onlyMissing, onlyNoGroup, onlyRaw;
        bool unusedExpanded;
        bool scanned;

        [MenuItem("Tools/Sound Inventory")]
        static void Open() => GetWindow<SoundInventoryWindow>("Sound Inventory");

        void OnEnable()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
        }

        void OnDisable()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
        }

        // Entries hold live scene object references; a scene swap leaves them dangling.
        void OnSceneOpened(Scene scene, OpenSceneMode mode) => scanned = false;
        void OnSceneClosed(Scene scene) => scanned = false;

        void OnGUI()
        {
            if (!scanned) Rescan();

            DrawSettingsPanel();
            DrawToolbar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            for (int i = 0; i < groups.Count; i++) DrawGroup(groups[i]);
            DrawUnused();
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
                string warn = "";
                if (group == null) warn = "  ⚠ 미지정 → Master 직결";
                else if (seen.ContainsKey(name)) warn = $"  ⚠ {seen[name]} 와 같은 그룹";
                else seen[name] = cat;

                EditorGUILayout.LabelField($"    {cat}", name + warn);
            }
            EditorGUILayout.Space(4);
        }

        void DrawToolbar()
        {
            // Leftmost so a narrow window never pushes it off the toolbar.
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("⟳ 다시 검색", EditorStyles.toolbarButton, GUILayout.Width(84)))
                Rescan();
            if (GUILayout.Button("■ 정지", EditorStyles.toolbarButton, GUILayout.Width(52)))
                AudioPreviewShortcut.StopAll();
            GUILayout.Space(8);
            onlyMissing = GUILayout.Toggle(onlyMissing, "미배정", EditorStyles.toolbarButton, GUILayout.Width(56));
            onlyNoGroup = GUILayout.Toggle(onlyNoGroup, "그룹없음", EditorStyles.toolbarButton, GUILayout.Width(64));
            onlyRaw = GUILayout.Toggle(onlyRaw, "생 AudioSource", EditorStyles.toolbarButton, GUILayout.Width(100));
            GUILayout.FlexibleSpace();
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField,
                                               GUILayout.MinWidth(80), GUILayout.MaxWidth(200));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Scan folders", GUILayout.Width(80));
            string folders = EditorGUILayout.TextField(FolderList);
            if (folders != FolderList) EditorPrefs.SetString(FolderPrefKey, folders);
            EditorGUILayout.EndHorizontal();
        }

        static string FolderList => EditorPrefs.GetString(FolderPrefKey, DefaultFolders);

        // ---------- rows ----------

        void DrawGroup(TypeGroup g)
        {
            var visible = g.entries.Where(Passes).ToList();
            if (visible.Count == 0) return;

            string title = visible.Count > 1 ? $"{g.typeName}  ×{visible.Count}" : g.typeName;
            string suffix = (g.AnyWarning ? "  ⚠" : "") + "        " + g.category;

            g.expanded = EditorGUILayout.Foldout(g.expanded, title + suffix, true, EditorStyles.foldoutHeader);
            if (!g.expanded) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < visible.Count; i++) DrawEntry(visible[i], g);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2);
        }

        void DrawEntry(Entry e, TypeGroup g)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(e.ownerLabel, EditorStyles.miniBoldLabel);
            GUILayout.Label(e.origin, EditorStyles.miniLabel, GUILayout.Width(50));
            if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
            {
                Selection.activeObject = e.owner;
                EditorGUIUtility.PingObject(e.owner);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;

            for (int i = 0; i < e.slots.Count; i++) DrawSlot(e, e.slots[i]);

            if (e.source != null) DrawAudioSourceKnobs(e, g);
            else
                for (int i = 0; i < e.volumes.Count; i++) DrawVolume(e, e.volumes[i]);

            if (e.warnRaw)
                EditorGUILayout.LabelField(" ", "⚠ 생 AudioSource — 믹서 그룹이 손으로 꽂혀 있음");
            if (e.warnNoGroup)
                EditorGUILayout.LabelField(" ", "⚠ Output 그룹 없음 → Master 직결, 볼륨 슬라이더 무시됨");

            EditorGUI.indentLevel--;
        }

        void DrawSlot(Entry e, Slot s)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUI.BeginChangeCheck();
            var next = (AudioClip)EditorGUILayout.ObjectField(s.label, s.clip, typeof(AudioClip), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (e.source != null && string.IsNullOrEmpty(s.propPath))
                {
                    Undo.RecordObject(e.source, "Set Clip");
                    e.source.clip = next;
                    MarkDirty(e.owner);
                }
                else WriteProperty(e.owner, s.propPath, p => p.objectReferenceValue = next);
                s.clip = next;
            }

            using (new EditorGUI.DisabledScope(s.clip == null))
            {
                if (GUILayout.Button("▶", GUILayout.Width(24)))
                    AudioPreviewShortcut.Play(s.clip);
            }
            if (s.clip == null) GUILayout.Label("미배정 ⚠", EditorStyles.miniLabel, GUILayout.Width(60));
            else GUILayout.Space(64);

            EditorGUILayout.EndHorizontal();
        }

        void DrawVolume(Entry e, VolumeField v)
        {
            EditorGUI.BeginChangeCheck();
            float next = EditorGUILayout.Slider(v.label, v.value, 0f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                WriteProperty(e.owner, v.propPath, p => p.floatValue = next);
                v.value = next;
            }
        }

        void DrawAudioSourceKnobs(Entry e, TypeGroup g)
        {
            var src = e.source;
            if (src == null) return;

            EditorGUI.BeginChangeCheck();
            float vol = EditorGUILayout.Slider("volume", src.volume, 0f, 1f);
            var group = (AudioMixerGroup)EditorGUILayout.ObjectField(
                "output group", src.outputAudioMixerGroup, typeof(AudioMixerGroup), false);
            float min = EditorGUILayout.FloatField("min distance", src.minDistance);
            float max = EditorGUILayout.FloatField("max distance", src.maxDistance);
            if (EditorGUI.EndChangeCheck())
            {
                // ponytail: one undo entry per change tick. Unity's own inspector does the same on drag.
                Undo.RecordObject(src, "Edit AudioSource");
                src.volume = vol;
                src.outputAudioMixerGroup = group;
                src.minDistance = min;
                src.maxDistance = max;
                MarkDirty(e.owner);
                e.category = CategoryForGroup(group, out e.warnNoGroup);
                RefreshGroupCategory(g);
            }
        }

        static void RefreshGroupCategory(TypeGroup g)
        {
            g.category = g.entries.Count > 0 ? g.entries[0].category : "—";
            for (int i = 1; i < g.entries.Count; i++)
                if (g.entries[i].category != g.category) { g.category = "혼합"; return; }
        }

        void DrawUnused()
        {
            if (unusedClipPaths.Count == 0) return;

            EditorGUILayout.Space(6);
            unusedExpanded = EditorGUILayout.Foldout(
                unusedExpanded, $"미사용 클립  ×{unusedClipPaths.Count}", true, EditorStyles.foldoutHeader);
            if (!unusedExpanded) return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < unusedClipPaths.Count; i++)
            {
                string path = unusedClipPaths[i];
                if (search.Length > 0 && path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(Path.GetFileNameWithoutExtension(path), EditorStyles.miniLabel);
                if (GUILayout.Button("▶", GUILayout.Width(24)))
                    AudioPreviewShortcut.Play(AssetDatabase.LoadAssetAtPath<AudioClip>(path));
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

        bool Passes(Entry e)
        {
            if (onlyMissing && !e.warnMissing) return false;
            if (onlyNoGroup && !e.warnNoGroup) return false;
            if (onlyRaw && !e.warnRaw) return false;
            if (search.Length == 0) return true;

            if (Contains(e.ownerLabel) || Contains(e.category)) return true;
            for (int i = 0; i < e.slots.Count; i++)
            {
                if (Contains(e.slots[i].label)) return true;
                if (e.slots[i].clip != null && Contains(e.slots[i].clip.name)) return true;
            }
            return false;
        }

        bool Contains(string s) =>
            !string.IsNullOrEmpty(s) && s.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        // ---------- writes ----------

        static void WriteProperty(UnityEngine.Object owner, string propPath, Action<SerializedProperty> set)
        {
            if (owner == null || string.IsNullOrEmpty(propPath)) return;
            var so = new SerializedObject(owner);
            var p = so.FindProperty(propPath);
            if (p == null) return;
            set(p);
            so.ApplyModifiedProperties();   // registers its own undo entry
            MarkDirty(owner);
        }

        static void MarkDirty(UnityEngine.Object owner)
        {
            EditorUtility.SetDirty(owner);
            var comp = owner as Component;
            if (comp != null && comp.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(comp.gameObject.scene);
        }

        // ---------- scan ----------

        void Rescan()
        {
            scanned = true;
            groups.Clear();
            unusedClipPaths.Clear();
            usedClipPaths.Clear();
            settings = LoadSettings();

            var byType = new Dictionary<string, TypeGroup>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    ScanHierarchy(root, "Scene", byType);
            }

            var folders = FolderList.Split(',')
                                    .Select(f => f.Trim())
                                    .Where(f => f.Length > 0 && AssetDatabase.IsValidFolder(f))
                                    .ToArray();
            if (folders.Length > 0)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", folders))
                {
                    var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (go != null) ScanHierarchy(go, "Prefab", byType);
                }
                foreach (var guid in AssetDatabase.FindAssets("t:ScriptableObject", folders))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                    if (asset != null) AddEntry(asset, asset.name, "Asset", null, byType);
                }
            }

            CollectUnusedClips();

            groups.AddRange(byType.Values.OrderBy(g => g.typeName));
        }

        void ScanHierarchy(GameObject root, string origin, Dictionary<string, TypeGroup> byType)
        {
            var components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                var c = components[i];
                if (c == null) continue;

                var src = c as AudioSource;
                if (src != null) AddEntry(src, HierarchyPath(src.transform), origin, src, byType);
                else if (TypeHasClipField(c.GetType())) AddEntry(c, HierarchyPath(c.transform), origin, null, byType);
            }
        }

        void AddEntry(UnityEngine.Object owner, string label, string origin, AudioSource src,
                      Dictionary<string, TypeGroup> byType)
        {
            var e = new Entry { owner = owner, ownerLabel = label, origin = origin, source = src };

            if (src != null)
            {
                e.slots.Add(new Slot { label = "clip", propPath = "", clip = src.clip });
                e.category = CategoryForGroup(src.outputAudioMixerGroup, out e.warnNoGroup);
                e.warnRaw = true;
                Mark(src.clip);
            }
            else
            {
                CollectSlots(owner, e);
                if (e.slots.Count == 0) return;
                CollectVolumes(owner, e);
                e.category = CategoryFromScript(owner);
            }

            e.warnMissing = e.slots.Any(s => s.clip == null);

            string typeName = src != null ? TypeLabelForSource(src) : owner.GetType().Name;
            if (!byType.TryGetValue(typeName, out var g))
            {
                g = new TypeGroup { typeName = typeName, category = e.category };
                byType[typeName] = g;
            }
            if (g.category != e.category) g.category = "혼합";
            g.entries.Add(e);
        }

        void CollectSlots(UnityEngine.Object owner, Entry e)
        {
            var so = new SerializedObject(owner);
            var p = so.GetIterator();
            bool enter = true;
            while (p.NextVisible(enter))
            {
                enter = p.depth < 3;
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (p.type != "PPtr<$AudioClip>") continue;

                var clip = p.objectReferenceValue as AudioClip;
                e.slots.Add(new Slot { label = p.displayName, propPath = p.propertyPath, clip = clip });
                Mark(clip);
            }
        }

        void CollectVolumes(UnityEngine.Object owner, Entry e)
        {
            foreach (var f in VolumeFields(owner.GetType()))
            {
                object raw = f.GetValue(owner);
                if (raw is float value)
                    e.volumes.Add(new VolumeField { label = f.Name, propPath = f.Name, value = value });
            }
        }

        void Mark(AudioClip clip)
        {
            if (clip == null) return;
            string path = AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(path)) usedClipPaths.Add(path);
        }

        void CollectUnusedClips()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (usedClipPaths.Contains(path)) continue;
                if (ExcludedPathParts.Any(part => path.Contains(part))) continue;
                unusedClipPaths.Add(path);
            }
            unusedClipPaths.Sort(StringComparer.OrdinalIgnoreCase);
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

        string CategoryForGroup(AudioMixerGroup group, out bool noGroup)
        {
            noGroup = group == null;
            if (noGroup) return "⚠ 없음";

            foreach (SoundCategory cat in Enum.GetValues(typeof(SoundCategory)))
                if (GroupFor(cat) == group) return cat.ToString();

            return "? " + group.name;
        }

        /// The category is a call-site literal, not serialized data, so it is read out of the owner's
        /// source file. File-level, so a script playing two categories reports both.
        static string CategoryFromScript(UnityEngine.Object owner)
        {
            var type = owner.GetType();
            if (categoryCache.TryGetValue(type, out var cached)) return cached;

            string result = "—";
            var mb = owner as MonoBehaviour;
            var so = owner as ScriptableObject;
            MonoScript script = mb != null ? MonoScript.FromMonoBehaviour(mb)
                              : so != null ? MonoScript.FromScriptableObject(so) : null;

            string path = script != null ? AssetDatabase.GetAssetPath(script) : null;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                string text = File.ReadAllText(path);
                var found = Regex.Matches(text, @"SoundCategory\.(\w+)")
                                 .Cast<Match>()
                                 .Select(m => m.Groups[1].Value)
                                 .Distinct()
                                 .ToList();
                // PlayBgm takes no category argument, so BGM leaves no SoundCategory token behind.
                if (Regex.IsMatch(text, @"\b(PlayBgm|StopBgm)\b") && !found.Contains("BGM"))
                    found.Add("BGM");

                if (found.Count > 0) result = string.Join(", ", found) + " (script)";
            }

            categoryCache[type] = result;
            return result;
        }

        // Colliders and renderers sit on nearly everything; the sibling script is what names the feature.
        static string TypeLabelForSource(AudioSource src)
        {
            var siblings = src.GetComponents<Component>();
            for (int i = 0; i < siblings.Length; i++)
                if (siblings[i] is MonoBehaviour) return siblings[i].GetType().Name;

            return "AudioSource (raw)";
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            var cur = t.parent;
            while (cur != null)
            {
                path = cur.name + "/" + path;
                cur = cur.parent;
            }
            return path;
        }

        // ---------- reflection cache ----------

        static bool TypeHasClipField(Type type)
        {
            if (hasClipCache.TryGetValue(type, out bool cached)) return cached;
            bool result = HasClipField(type, 2);
            hasClipCache[type] = result;
            return result;
        }

        static bool HasClipField(Type type, int depth)
        {
            foreach (var f in SerializedFields(type))
            {
                var ft = f.FieldType;
                if (ft == typeof(AudioClip)) return true;
                if (ft.IsArray && ft.GetElementType() == typeof(AudioClip)) return true;
                if (ft.IsGenericType && ft.GetGenericArguments().FirstOrDefault() == typeof(AudioClip)) return true;

                if (depth > 0 && !ft.IsPrimitive && ft.IsSerializable &&
                    !typeof(UnityEngine.Object).IsAssignableFrom(ft) && ft.Namespace != "System" &&
                    HasClipField(ft, depth - 1))
                    return true;
            }
            return false;
        }

        static List<FieldInfo> VolumeFields(Type type)
        {
            if (volumeFieldCache.TryGetValue(type, out var cached)) return cached;

            var list = SerializedFields(type)
                .Where(f => f.FieldType == typeof(float) &&
                            f.Name.IndexOf("volume", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            volumeFieldCache[type] = list;
            return list;
        }

        static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            var t = type;
            while (t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object))
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                         BindingFlags.DeclaredOnly);
                for (int i = 0; i < fields.Length; i++)
                {
                    var f = fields[i];
                    if (f.IsNotSerialized) continue;
                    if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                    yield return f;
                }
                t = t.BaseType;
            }
        }
    }
}
