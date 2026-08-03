using UnityEngine;
using UnityEditor;
using SlimeJumping.VFX;

namespace SlimeJumping.Editor
{
    /// Builds a SnowfallVolume by instancing Snow.prefab and swapping its camera anchor for a fixed
    /// spawn box. The prefab link is kept, so the snow look stays authored in one place.
    public static class SnowfallVolumeSetup
    {
        const string SnowPrefabPath = "Assets/Prebs/VFX/Snow.prefab";
        static readonly Vector2 DefaultSpawnSize = new Vector2(8f, 30f);

        [MenuItem("GameObject/SlimeJumping/Snowfall Volume", false, 10)]
        public static void CreateSnowfallVolume(MenuCommand command)
        {
            var parent = command.context as GameObject;
            var go = Build("SnowfallVolume", DefaultSpawnSize, 1f);
            if (go == null) return;

            if (parent != null)
            {
                go.transform.SetParent(parent.transform, true);
                go.transform.position = new Vector3(
                    HorizontalCenter(parent),
                    BottomOf(parent) - DefaultSpawnSize.y * 0.5f,
                    parent.transform.position.z);
            }
            else
            {
                var view = SceneView.lastActiveSceneView;
                Vector3 pivot = view != null ? view.pivot : Vector3.zero;
                go.transform.position = new Vector3(pivot.x, pivot.y, 0f);
            }

            Selection.activeGameObject = go;
        }

        [MenuItem("SlimeJumping/VFX/Setup Snowfall Volume Test")]
        public static void SetupSnowfallVolumeTest()
        {
            var origin = new Vector3(-60f, 300f, 0f);

            var root = new GameObject("SnowfallVolumeTest");
            root.transform.position = origin;
            Undo.RegisterCreatedObjectUndo(root, "Create Snowfall Volume Test");

            CreateBlock("Floor", root.transform, origin + new Vector3(0f, -34f, 0f), new Vector3(120f, 1f, 1f));

            // Cloud stays 10 wide in all three; only the column widens. The top edge is what reads as
            // "the cloud is snowing", so the width is free to overshoot the cloud and blend outward.
            AddCase(root.transform, origin + new Vector3(-38f, 0f, 0f), "Width_10", 10f, new Vector2(10f, 30f), 3f);
            AddCase(root.transform, origin + new Vector3(0f, 0f, 0f), "Width_20", 10f, new Vector2(20f, 30f), 3f);
            AddCase(root.transform, origin + new Vector3(50f, 0f, 0f), "Width_40", 10f, new Vector2(40f, 30f), 3f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null) sceneView.Frame(new Bounds(origin + new Vector3(6f, -16f, 0f), new Vector3(130f, 44f, 4f)), false);
            Debug.Log($"Snowfall volume test rig built at {origin}. Cases: Width_10 / Width_20 / Width_40, all densityScale 3.");
        }

        static void AddCase(Transform parent, Vector3 cloudPos, string name, float cloudWidth, Vector2 spawnSize, float densityScale)
        {
            var cloud = CreateBlock($"Cloud_{name}", parent, cloudPos, new Vector3(cloudWidth, 1.2f, 1f));

            var volume = Build($"Snowfall_{name}", spawnSize, densityScale);
            if (volume == null) return;
            // Parent without keeping world scale: the cloud is non-uniformly scaled and scalingMode
            // Local would read the compensating localScale into flake size and fall speed.
            volume.transform.SetParent(cloud.transform, false);
            volume.transform.position = new Vector3(cloudPos.x, cloudPos.y - 0.6f - spawnSize.y * 0.5f, 0f);
        }

        // Snow.prefab already carries every visual and motion trait (World sim, prewarm, lifetime
        // 5-9, fall -3.5..-2, drift, noise, limit velocity, alpha ramp, material, wind ForceField
        // mask). Only the camera anchor has to go, so the spawn box can stay put under a cloud.
        static GameObject Build(string name, Vector2 spawnSize, float densityScale)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"Snow prefab missing at {SnowPrefabPath} — cannot build a snowfall volume.");
                return null;
            }

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            go.name = name;
            // Register before removing the component, else the undo entries land out of order.
            Undo.RegisterCreatedObjectUndo(go, "Create Snowfall Volume");

            var cameraEmitter = go.GetComponent<SnowCameraEmitter>();
            if (cameraEmitter != null) Undo.DestroyObjectImmediate(cameraEmitter);

            // The EmitterArea child only exists to bound the camera-following snow. The volume has
            // no boundary, so it would just be a stray collider drawing a green box in the scene.
            var emitterArea = go.transform.Find("EmitterArea");
            if (emitterArea != null) Undo.DestroyObjectImmediate(emitterArea.gameObject);

            var volume = go.AddComponent<SnowfallVolume>();
            var so = new SerializedObject(volume);
            so.FindProperty("spawnSize").vector2Value = spawnSize;
            so.FindProperty("densityScale").floatValue = densityScale;
            so.ApplyModifiedProperties();
            // AddComponent already ran OnEnable against the defaults; re-enable so the rate is
            // rebuilt from the values just written.
            volume.enabled = false;
            volume.enabled = true;
            return go;
        }

        static float HorizontalCenter(GameObject go)
        {
            var col = go.GetComponentInChildren<Collider2D>();
            if (col != null) return col.bounds.center.x;
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.center.x;
            return go.transform.position.x;
        }

        static float BottomOf(GameObject go)
        {
            var col = go.GetComponentInChildren<Collider2D>();
            if (col != null) return col.bounds.min.y;
            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.min.y;
            return go.transform.position.y;
        }

        static GameObject CreateBlock(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.localScale = scale;

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = new Color(0.75f, 0.78f, 0.85f) };
            else
                Debug.LogWarning("URP Lit shader not found — test blocks will use Unity's default magenta material.");
            return go;
        }
    }
}
