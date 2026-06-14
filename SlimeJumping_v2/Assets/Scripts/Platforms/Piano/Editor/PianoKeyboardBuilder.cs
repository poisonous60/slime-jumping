using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Piano.EditorTools
{
    /// One-shot scene builder for the test piano. Instantiates the upright-piano FBX, scales it
    /// so each white key roughly fits the slime, generates a static BoxCollider2D + dedicated
    /// AudioSource per WHITE key (black keys are intentionally collider-less), maps each white key
    /// to its CC0 note sample by the MIDI number baked into the sample filename (pitch-shifting the
    /// top keys that have no sample), then wires up a PianoKeyboard. Re-runnable: deletes any prior
    /// build first. Tune feel afterward on the PianoKeyboard > Settings curves.
    public static class PianoKeyboardBuilder
    {
        const string FbxPath = "Assets/_PianoImportTest/Piano.fbx";
        const string AudioFolder = "Assets/Audio/Piano";
        const string SoundSettingsPath = "Assets/Settings/SoundSettings.asset";
        const string PedalSoundPath = "Assets/Sound/piano_pedal.wav";
        const string PianoConfigPath = "Assets/Settings/PianoConfig.asset";

        const float WhiteKeyLocalY = 5.61f;   // white keys sit above this local Y, black below
        const float TargetWhiteWidthWorld = 0.7f;
        const float DropBelowSlime = 1.5f;     // place keyboard top this far under the slime

        [MenuItem("Tools/SlimeJumping/Build Test Piano")]
        public static void Build()
        {
            var fbx = AssetDatabase.LoadMainAssetAtPath(FbxPath) as GameObject;
            if (fbx == null) { Debug.LogError($"[PianoBuilder] FBX not found at {FbxPath}"); return; }

            var slime = Object.FindAnyObjectByType<SlimeController>();
            if (slime == null) { Debug.LogError("[PianoBuilder] No SlimeController in scene."); return; }

            // remove a previous build
            foreach (var prev in Object.FindObjectsByType<PianoKeyboard>(FindObjectsSortMode.None))
                Object.DestroyImmediate(prev.gameObject);

            int keyLayer = ResolveGroundLayer(slime);
            var clipsByMidi = LoadClipsByMidi();
            var sfxGroup = LoadSfxObjectGroup();

            // instantiate + unpack to a fully editable copy
            var root = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Piano";
            var rt = root.transform;
            rt.rotation = Quaternion.identity;
            rt.position = Vector3.zero;
            rt.localScale = Vector3.one;

            // collect white keys (sorted left->right == ascending pitch)
            var whites = new List<Transform>();
            foreach (Transform c in rt)
                if (c.name.StartsWith("PianoKey") && c.localPosition.y > WhiteKeyLocalY)
                    whites.Add(c);
            whites.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));
            if (whites.Count == 0) { Debug.LogError("[PianoBuilder] No white keys found."); Object.DestroyImmediate(root); return; }

            // scale so the median white-key pitch ~= target world width
            var localXs = whites.Select(w => w.localPosition.x).ToList();
            float medianPitch = MedianGap(localXs);
            float scale = medianPitch > 1e-5f ? TargetWhiteWidthWorld / medianPitch : 4f;
            rt.localScale = Vector3.one * scale;

            // place: center on slime X, keyboard top DropBelowSlime under the slime, z = slime z
            Vector3 slimePos = slime.transform.position;
            ComputeWhiteWorldExtent(whites, out float curCenterX, out float curTopY, out float curKeyZ);
            float targetTopY = slimePos.y - DropBelowSlime;
            rt.position += new Vector3(slimePos.x - curCenterX, targetTopY - curTopY, slimePos.z - curKeyZ);

            var keyComps = BuildKeys(rt, slime, keyLayer, clipsByMidi, sfxGroup, out int missingClip, out int shifted);

            var config = LoadOrCreateConfig();
            var keyboard = root.AddComponent<PianoKeyboard>();
            keyboard.SetSlime(slime);
            keyboard.SetKeys(keyComps);
            keyboard.SetConfig(config);

            BuildPedals(rt, slime, keyLayer, config);

            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

            ComputeWhiteWorldExtent(whites, out float fcx, out float ftopY, out float fz);
            Debug.Log($"[PianoBuilder] Built piano: {keyComps.Count} white keys, scale {scale:F2}, " +
                      $"keyboard center X {fcx:F1}, top Y {ftopY:F1}, Z {fz:F2}, layer '{LayerMask.LayerToName(keyLayer)}'. " +
                      $"clips missing={missingClip}, pitch-shifted={shifted}. " +
                      $"Slime at {slimePos}. Move the 'Piano' root so the slime can land on it, then Play.");
        }

        // Rebuilds the white-key motors on an already-built piano WITHOUT touching the user's
        // placement/scale. Use after editing the key scripts. Resets the world-unit sink depths.
        [MenuItem("Tools/SlimeJumping/Rebuild Piano Keys (keep placement)")]
        public static void RebuildKeys()
        {
            var kb = Object.FindAnyObjectByType<PianoKeyboard>();
            if (kb == null) { Debug.LogError("[PianoBuilder] No PianoKeyboard in scene. Build the piano first."); return; }
            var slime = Object.FindAnyObjectByType<SlimeController>();
            int layer = slime != null ? ResolveGroundLayer(slime) : kb.gameObject.layer;

            var keys = BuildKeys(kb.transform, slime, layer, LoadClipsByMidi(), LoadSfxObjectGroup(),
                                 out int missing, out int shifted);
            kb.SetKeys(keys);
            if (slime != null) kb.SetSlime(slime);
            kb.SetConfig(LoadOrCreateConfig());

            EditorUtility.SetDirty(kb.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(kb.gameObject.scene);
            Debug.Log($"[PianoBuilder] Rebuilt {keys.Count} key motors (clips missing={missing}, shifted={shifted}). Placement preserved.");
        }

        // Builds (or rebuilds) a sinking PlatformMotor + collider + voice for every WHITE key, with
        // the key mesh parented under the motor so collider and visual sink together and carry the
        // slime. Tears down any prior key rig first (old static sensors or a prior motor rig).
        static List<PianoKey> BuildKeys(Transform root, SlimeController slime, int layer,
                                        Dictionary<int, AudioClip> clipsByMidi,
                                        UnityEngine.Audio.AudioMixerGroup sfxGroup,
                                        out int missing, out int shifted)
        {
            // teardown: move key meshes back under root, then destroy old rig GameObjects
            var rig = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name.StartsWith("KeyMotor_") || t.name.StartsWith("KeySensor_"))) rig.Add(t);
            foreach (var t in rig)
            {
                if (t == null) continue;
                var kids = new List<Transform>();
                foreach (Transform c in t) kids.Add(c);
                foreach (var c in kids) if (c.name.StartsWith("PianoKey")) c.SetParent(root, true);
            }
            foreach (var t in rig) if (t != null) Object.DestroyImmediate(t.gameObject);

            // gather white keys (meshes are direct children of root), sorted left->right
            var whites = new List<Transform>();
            foreach (Transform c in root)
                if (c.name.StartsWith("PianoKey") && c.localPosition.y > WhiteKeyLocalY) whites.Add(c);
            whites.Sort((a, b) => a.localPosition.x.CompareTo(b.localPosition.x));

            var worldCenters = whites.Select(w => Renderer(w).bounds.center.x).ToList();
            var keyComps = new List<PianoKey>(whites.Count);
            missing = 0; shifted = 0;

            for (int i = 0; i < whites.Count; i++)
            {
                Transform key = whites[i];
                var rend = Renderer(key);
                if (rend == null) continue;
                Bounds b = rend.bounds;

                float pitchW = i + 1 < worldCenters.Count ? worldCenters[i + 1] - worldCenters[i]
                                                          : (i > 0 ? worldCenters[i] - worldCenters[i - 1] : 0.3f);
                pitchW = Mathf.Max(0.05f, pitchW);
                float width = pitchW * 1.02f;
                float height = pitchW * 0.9f;

                // key motor: kinematic Rigidbody2D + BoxCollider2D that sinks DOWN to carry the rider
                var motorGO = new GameObject($"KeyMotor_{key.name}");
                motorGO.layer = layer;
                var mt = motorGO.transform;
                mt.SetParent(root, true);
                mt.position = new Vector3(b.center.x, b.max.y, b.center.z);   // origin at key top
                mt.rotation = Quaternion.identity;
                Vector3 ls = mt.lossyScale;
                var box = motorGO.AddComponent<BoxCollider2D>();
                box.size = new Vector2(width / Mathf.Max(1e-4f, Mathf.Abs(ls.x)),
                                       height / Mathf.Max(1e-4f, Mathf.Abs(ls.y)));
                box.offset = new Vector2(0f, -box.size.y * 0.5f);            // top edge at origin (key top)

                var src = motorGO.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                if (sfxGroup != null) src.outputAudioMixerGroup = sfxGroup;

                int midi = 20 + ParseIndex(key.name);   // PianoKey.001 -> A0 (MIDI 21)
                AudioClip clip = ResolveClip(clipsByMidi, midi, out float pitch);
                if (clip == null) missing++;
                else if (!Mathf.Approximately(pitch, 1f)) shifted++;
                src.clip = clip;

                var pk = motorGO.AddComponent<PianoKey>();               // RequireComponent adds Rigidbody2D
                motorGO.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
                key.SetParent(mt, true);                                 // mesh rides with the motor
                pk.Init(midi, src, pitch, box);
                keyComps.Add(pk);
            }
            return keyComps;
        }

        // Adds the 3 steppable pedals to an already-built piano WITHOUT touching the keys or the
        // user's placement/scale. Idempotent: tears down any prior pedal rig first.
        [MenuItem("Tools/SlimeJumping/Add or Rebuild Piano Pedals")]
        public static void AddPedals()
        {
            var kb = Object.FindAnyObjectByType<PianoKeyboard>();
            if (kb == null) { Debug.LogError("[PianoBuilder] No PianoKeyboard in scene. Build the piano first."); return; }
            var slime = Object.FindAnyObjectByType<SlimeController>();
            int layer = slime != null ? ResolveGroundLayer(slime) : kb.gameObject.layer;
            var config = LoadOrCreateConfig();
            kb.SetConfig(config);

            int n = BuildPedals(kb.transform, slime, layer, config);

            EditorUtility.SetDirty(kb.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(kb.gameObject.scene);
            Debug.Log($"[PianoBuilder] Pedals built: {n} (layer '{LayerMask.LayerToName(layer)}').");
        }

        static readonly string[] PedalNames = { "Pedal_Left", "Pedal_Centre", "Pedal_Right" };

        static int BuildPedals(Transform root, SlimeController slime, int layer, PianoConfig config)
        {
            // tear down any previous pedal rig (old sensor rig or a prior motor rig): move the pedal
            // meshes back under root first, then destroy the rig GameObjects.
            foreach (var name in PedalNames)
            {
                var mesh = FindDeep(root, name);
                if (mesh != null && mesh.parent != root) mesh.SetParent(root, true);
            }
            var stale = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && (t.name.StartsWith("PedalMotor_") || t.name.StartsWith("PedalPivot_") ||
                                  t.name.StartsWith("PedalSensor_") || t.name.StartsWith("PedalVisualPivot_")))
                    stale.Add(t);
            foreach (var t in stale) if (t != null) Object.DestroyImmediate(t.gameObject);

            var sfxGroup = LoadSfxObjectGroup();

            int built = 0;
            foreach (var name in PedalNames)
            {
                Transform pedal = FindDeep(root, name);
                if (pedal == null || Renderer(pedal) == null) continue;

                // pedal mesh = wide foot PAD + thin rod above; the pad top is the step surface
                PadInfo(pedal, out float padTopY, out float padCx, out float padZmin, out float padZmax, out float padW);
                float padZmid = (padZmin + padZmax) * 0.5f;

                // motor GO: kinematic Rigidbody2D + PolygonCollider2D that sinks DOWN to carry the rider
                var motorGO = new GameObject($"PedalMotor_{name}");
                motorGO.layer = layer;
                var mt = motorGO.transform;
                mt.SetParent(root, true);
                mt.position = new Vector3(padCx, padTopY, padZmid);
                mt.rotation = Quaternion.identity;
                Vector3 ls = mt.lossyScale;
                float w = padW * 0.95f / Mathf.Max(1e-4f, Mathf.Abs(ls.x));
                float h = 0.18f / Mathf.Max(1e-4f, Mathf.Abs(ls.y));
                var poly = motorGO.AddComponent<PolygonCollider2D>();   // reshape this to the pedal outline
                poly.pathCount = 1;
                poly.SetPath(0, new[]
                {
                    new Vector2(-w * 0.5f, 0f), new Vector2(w * 0.5f, 0f),
                    new Vector2(w * 0.5f, -h),  new Vector2(-w * 0.5f, -h)
                });
                var pp = motorGO.AddComponent<PianoPedal>();            // RequireComponent adds Rigidbody2D
                motorGO.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;

                // press-sound voice
                var src = motorGO.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = false;
                src.spatialBlend = 0f;
                if (sfxGroup != null) src.outputAudioMixerGroup = sfxGroup;

                // cosmetic tilt pivot at the pad back edge; pedal mesh parented under it (visual only)
                var pivotGO = new GameObject($"PedalVisualPivot_{name}");
                var pv = pivotGO.transform;
                pv.SetParent(mt, true);
                pv.position = new Vector3(padCx, padTopY, padZmin);
                pv.rotation = Quaternion.identity;
                pedal.SetParent(pv, true);

                pp.Init(config, pv, src);
                built++;
            }
            return built;
        }

        // Finds the pedal's foot pad (the wide bottom of the mesh) by scanning world-space vertex
        // Y bands bottom-up until the horizontal width drops off (the thin upper rod). Returns the
        // pad's top Y, X center, Z extent and X width.
        static void PadInfo(Transform pedal, out float topY, out float centerX,
                            out float zMin, out float zMax, out float width)
        {
            var mf = pedal.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null)
            {
                var b = Renderer(pedal).bounds;
                topY = b.min.y + b.size.y * 0.3f; centerX = b.center.x; zMin = b.min.z; zMax = b.max.z; width = b.size.x;
                return;
            }

            var verts = mesh.vertices;
            var m = pedal.localToWorldMatrix;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < verts.Length; i++) { float y = m.MultiplyPoint3x4(verts[i]).y; if (y < minY) minY = y; if (y > maxY) maxY = y; }

            const int bins = 16;
            var binN = new int[bins];
            var xmin = new float[bins]; var xmax = new float[bins];
            for (int i = 0; i < bins; i++) { xmin[i] = float.MaxValue; xmax[i] = float.MinValue; }
            float span = Mathf.Max(1e-4f, maxY - minY);
            for (int i = 0; i < verts.Length; i++)
            {
                var w = m.MultiplyPoint3x4(verts[i]);
                int bi = Mathf.Clamp((int)((w.y - minY) / span * bins), 0, bins - 1);
                binN[bi]++; if (w.x < xmin[bi]) xmin[bi] = w.x; if (w.x > xmax[bi]) xmax[bi] = w.x;
            }
            float maxWidth = 0f;
            for (int i = 0; i < bins; i++) { float bw = binN[i] > 0 ? xmax[i] - xmin[i] : 0f; if (bw > maxWidth) maxWidth = bw; }

            float padTop = minY + span / bins;   // at least the bottom band
            for (int i = 0; i < bins; i++)
            {
                float bw = binN[i] > 0 ? xmax[i] - xmin[i] : 0f;
                bool wide = binN[i] > 0 && bw >= 0.7f * maxWidth;
                if (wide) padTop = minY + span * (i + 1) / bins;
                else break;
            }

            float cxmin = float.MaxValue, cxmax = float.MinValue, czmin = float.MaxValue, czmax = float.MinValue;
            for (int i = 0; i < verts.Length; i++)
            {
                var w = m.MultiplyPoint3x4(verts[i]);
                if (w.y <= padTop)
                {
                    if (w.x < cxmin) cxmin = w.x; if (w.x > cxmax) cxmax = w.x;
                    if (w.z < czmin) czmin = w.z; if (w.z > czmax) czmax = w.z;
                }
            }
            topY = padTop; centerX = (cxmin + cxmax) * 0.5f; zMin = czmin; zMax = czmax; width = cxmax - cxmin;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        static Renderer Renderer(Transform t)
        {
            var r = t.GetComponent<MeshRenderer>();
            return r != null ? r : t.GetComponentInChildren<MeshRenderer>();
        }

        static void ComputeWhiteWorldExtent(List<Transform> whites, out float centerX, out float topY, out float keyZ)
        {
            float minX = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, zsum = 0f; int n = 0;
            foreach (var w in whites)
            {
                var r = Renderer(w); if (r == null) continue;
                var b = r.bounds;
                minX = Mathf.Min(minX, b.min.x); maxX = Mathf.Max(maxX, b.max.x);
                maxY = Mathf.Max(maxY, b.max.y); zsum += b.center.z; n++;
            }
            centerX = (minX + maxX) * 0.5f; topY = maxY; keyZ = n > 0 ? zsum / n : 0f;
        }

        static float MedianGap(List<float> sortedXs)
        {
            sortedXs = sortedXs.OrderBy(x => x).ToList();
            var gaps = new List<float>();
            for (int i = 1; i < sortedXs.Count; i++) gaps.Add(sortedXs[i] - sortedXs[i - 1]);
            if (gaps.Count == 0) return 0f;
            gaps.Sort();
            return gaps[gaps.Count / 2];
        }

        static int ParseIndex(string name)
        {
            int dot = name.LastIndexOf('.');
            if (dot < 0) return -1;
            return int.TryParse(name.Substring(dot + 1), out int v) ? v : -1;
        }

        static Dictionary<int, AudioClip> LoadClipsByMidi()
        {
            var dict = new Dictionary<int, AudioClip>();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string fn = Path.GetFileNameWithoutExtension(path);     // 277089__beskhu__21-a0
                int us = fn.LastIndexOf("__");
                if (us < 0) continue;
                string tail = fn.Substring(us + 2);                     // 21-a0
                int dash = tail.IndexOf('-');
                string numStr = dash > 0 ? tail.Substring(0, dash) : tail;
                if (!int.TryParse(numStr, out int midi)) continue;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip != null && !dict.ContainsKey(midi)) dict[midi] = clip;
            }
            return dict;
        }

        // exact sample if present, else nearest sample one or more octaves below + pitch up
        static AudioClip ResolveClip(Dictionary<int, AudioClip> byMidi, int midi, out float pitch)
        {
            pitch = 1f;
            if (byMidi.TryGetValue(midi, out var exact)) return exact;
            int src = midi - 12;
            while (src > 0 && !byMidi.ContainsKey(src)) src -= 12;
            if (src > 0 && byMidi.TryGetValue(src, out var lower))
            {
                pitch = Mathf.Pow(2f, (midi - src) / 12f);
                return lower;
            }
            return null;
        }

        static int ResolveGroundLayer(SlimeController slime)
        {
            try
            {
                var f = typeof(SlimeController).GetField("settings",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var settingsObj = f != null ? f.GetValue(slime) : null;
                if (settingsObj != null)
                {
                    var gf = settingsObj.GetType().GetField("groundLayer",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (gf != null)
                    {
                        LayerMask mask = (LayerMask)gf.GetValue(settingsObj);
                        for (int i = 0; i < 32; i++)
                            if ((mask.value & (1 << i)) != 0) return i;
                    }
                }
            }
            catch (System.Exception e) { Debug.LogWarning($"[PianoBuilder] groundLayer reflect failed: {e.Message}"); }
            return slime.gameObject.layer; // fallback: slime's own layer
        }

        static UnityEngine.Audio.AudioMixerGroup LoadSfxObjectGroup()
        {
            var ss = AssetDatabase.LoadAssetAtPath<SoundSettings>(SoundSettingsPath);
            return ss != null ? ss.sfxObjectGroup : null;
        }

        // Single tuning asset for the whole piano (keys + detection + pedals). Created on first build.
        static PianoConfig LoadOrCreateConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<PianoConfig>(PianoConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<PianoConfig>();
                cfg.pedalPressSound = AssetDatabase.LoadAssetAtPath<AudioClip>(PedalSoundPath);
                string dir = System.IO.Path.GetDirectoryName(PianoConfigPath);
                if (!AssetDatabase.IsValidFolder(dir)) System.IO.Directory.CreateDirectory(dir);
                AssetDatabase.CreateAsset(cfg, PianoConfigPath);
                AssetDatabase.SaveAssets();
            }
            else if (cfg.pedalPressSound == null)
            {
                cfg.pedalPressSound = AssetDatabase.LoadAssetAtPath<AudioClip>(PedalSoundPath);
                EditorUtility.SetDirty(cfg);
            }
            return cfg;
        }
    }
}
