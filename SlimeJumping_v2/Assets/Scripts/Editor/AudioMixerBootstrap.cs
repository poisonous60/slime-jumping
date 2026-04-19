// Editor-only one-shot utility: creates MainMixer.mixer + SoundSettings.asset + SoundService prefab
// at fixed paths, and adds the service instance to MainScene. Idempotent.
//
// Usage: invoke `SlimeJumping.EditorTools.AudioMixerBootstrap.BuildAll()` via unity-cli exec.

using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using SlimeJumping.Audio;

namespace SlimeJumping.EditorTools
{
    public static class AudioMixerBootstrap
    {
        const string MixerPath = "Assets/Audio/MainMixer.mixer";
        const string SettingsPath = "Assets/Settings/SoundSettings.asset";
        const string PrefabPath = "Assets/Prefabs/Systems/SoundService.prefab";
        const string MainScenePath = "Assets/Scenes/MainScene.unity";

        public static string BuildAll()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                EnsureFolder("Assets/Audio");
                EnsureFolder("Assets/Settings");
                EnsureFolder("Assets/Prefabs");
                EnsureFolder("Assets/Prefabs/Systems");

                var mixer = CreateOrLoadMixer(out var groupMaster, out var groupBgm,
                    out var groupSfx, out var groupSfxSlime, out var groupSfxEnv,
                    out var groupSfxObj, out var groupUI);

                var settings = CreateOrLoadSettings(mixer, groupBgm, groupSfxSlime,
                    groupSfxEnv, groupSfxObj, groupUI);

                CreateOrUpdatePrefab(settings);

                AddServiceToMainScene();

                AssetDatabase.SaveAssets();
                return "OK";
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            var leaf = System.IO.Path.GetFileName(path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static AudioMixer CreateOrLoadMixer(
            out AudioMixerGroup master, out AudioMixerGroup bgm, out AudioMixerGroup sfx,
            out AudioMixerGroup sfxSlime, out AudioMixerGroup sfxEnv, out AudioMixerGroup sfxObj,
            out AudioMixerGroup ui)
        {
            var editorAsm = typeof(UnityEditor.Editor).Assembly;
            var controllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerController");
            var groupControllerType = editorAsm.GetType("UnityEditor.Audio.AudioMixerGroupController");
            var pathType = editorAsm.GetType("UnityEditor.Audio.AudioGroupParameterPath");
            var exposedType = editorAsm.GetType("UnityEditor.Audio.ExposedAudioParameter");

            var existing = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            object controller;
            if (existing == null)
            {
                var createMethod = controllerType.GetMethod("CreateMixerControllerAtPath",
                    BindingFlags.Public | BindingFlags.Static);
                controller = createMethod.Invoke(null, new object[] { MixerPath });
            }
            else
            {
                controller = existing; // AudioMixerController inherits AudioMixer
            }

            // Children of master: BGM, SFX, UI. Children of SFX: Slime, Environment, Object.
            var masterGroupProp = controllerType.GetProperty("masterGroup");
            var masterCtrl = masterGroupProp.GetValue(controller);

            // Index existing child groups by name so this is idempotent.
            var childrenProp = groupControllerType.GetProperty("children");
            System.Func<object, string, object> findChild = (parent, name) =>
            {
                var arr = (System.Collections.IEnumerable)childrenProp.GetValue(parent);
                foreach (var c in arr)
                {
                    var obj = c as Object;
                    if (obj != null && obj.name == name) return c;
                }
                return null;
            };

            var createGroup = controllerType.GetMethod("CreateNewGroup",
                new[] { typeof(string), typeof(bool) });
            var addChild = controllerType.GetMethod("AddChildToParent");

            System.Func<string, object, object> getOrCreate = (name, parent) =>
            {
                var existingChild = findChild(parent, name);
                if (existingChild != null) return existingChild;
                var g = createGroup.Invoke(controller, new object[] { name, true });
                addChild.Invoke(controller, new[] { g, parent });
                return g;
            };

            var bgmCtrl = getOrCreate("BGM", masterCtrl);
            var sfxCtrl = getOrCreate("SFX", masterCtrl);
            var uiCtrl = getOrCreate("UI", masterCtrl);
            var sfxSlimeCtrl = getOrCreate("SFX_Slime", sfxCtrl);
            var sfxEnvCtrl = getOrCreate("SFX_Environment", sfxCtrl);
            var sfxObjCtrl = getOrCreate("SFX_Object", sfxCtrl);

            // Expose volume parameters with fixed names. Rebuild the exposedParameters array so
            // re-running the bootstrap produces the same 4 entries instead of accumulating dupes.
            var getGuid = groupControllerType.GetMethod("GetGUIDForVolume");
            var pathCtor = pathType.GetConstructor(new[] { groupControllerType, editorAsm.GetType("UnityEditor.GUID") });
            var nameField = exposedType.GetField("name");
            var guidField = exposedType.GetField("guid");

            System.Func<string, object, object> makeExposed = (paramName, groupCtrl) =>
            {
                var exposed = System.Activator.CreateInstance(exposedType);
                nameField.SetValue(exposed, paramName);
                guidField.SetValue(exposed, getGuid.Invoke(groupCtrl, null));
                return exposed;
            };

            var exposedArr = System.Array.CreateInstance(exposedType, 4);
            exposedArr.SetValue(makeExposed("MasterVolume", masterCtrl), 0);
            exposedArr.SetValue(makeExposed("BGMVolume", bgmCtrl), 1);
            exposedArr.SetValue(makeExposed("SFXVolume", sfxCtrl), 2);
            exposedArr.SetValue(makeExposed("UIVolume", uiCtrl), 3);
            controllerType.GetProperty("exposedParameters").SetValue(controller, exposedArr);

            EditorUtility.SetDirty((Object)controller);

            var asMixer = (AudioMixer)controller;
            master = (AudioMixerGroup)masterCtrl;
            bgm = (AudioMixerGroup)bgmCtrl;
            sfx = (AudioMixerGroup)sfxCtrl;
            sfxSlime = (AudioMixerGroup)sfxSlimeCtrl;
            sfxEnv = (AudioMixerGroup)sfxEnvCtrl;
            sfxObj = (AudioMixerGroup)sfxObjCtrl;
            ui = (AudioMixerGroup)uiCtrl;

            return asMixer;
        }

        static SoundSettings CreateOrLoadSettings(AudioMixer mixer,
            AudioMixerGroup bgm, AudioMixerGroup sfxSlime, AudioMixerGroup sfxEnv,
            AudioMixerGroup sfxObj, AudioMixerGroup ui)
        {
            var settings = AssetDatabase.LoadAssetAtPath<SoundSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<SoundSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }
            settings.mixer = mixer;
            settings.bgmGroup = bgm;
            settings.sfxSlimeGroup = sfxSlime;
            settings.sfxEnvironmentGroup = sfxEnv;
            settings.sfxObjectGroup = sfxObj;
            settings.uiGroup = ui;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        static void CreateOrUpdatePrefab(SoundSettings settings)
        {
            GameObject root;
            var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existingPrefab != null)
            {
                root = (GameObject)PrefabUtility.InstantiatePrefab(existingPrefab);
            }
            else
            {
                root = new GameObject("SoundService");
                root.AddComponent<SoundService>();
            }
            var svc = root.GetComponent<SoundService>() ?? root.AddComponent<SoundService>();
            var field = typeof(SoundService).GetField("settings",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(svc, settings);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
        }

        static void AddServiceToMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            bool alreadyPresent = false;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.GetComponent<SoundService>() != null) { alreadyPresent = true; break; }
            }

            if (!alreadyPresent)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.SetAsFirstSibling();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
