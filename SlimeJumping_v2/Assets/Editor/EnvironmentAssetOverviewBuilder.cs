using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace SlimeJumping.EditorTools
{
    public static class EnvironmentAssetOverviewBuilder
    {
        const string SourceScenePath = "Assets/Scenes/MainScene.unity";
        const string OverviewScenePath = "Assets/Scenes/EnvironmentAssetOverview.unity";
        const float CellSize = 100f;
        const int Columns = 24;

        static readonly string[] EnvironmentRoots =
        {
            "stage1_floor1", "stage1_floor2", "stage1_background",
            "stage2_floor1", "stage2_floor2", "stage2_background",
            "stage3_floor1", "stage3_floor2_left", "stage3_floor2_right", "stage3_floor3", "stage3_right_hidden", "stage3_background",
            "stage4_floor1", "stage4_background", "etc"
        };

        [MenuItem("Tools/Slime Jumping/Create Environment Asset Overview")]
        public static void CreateOverview()
        {
            var sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
            var openedSourceScene = false;
            if (!sourceScene.isLoaded)
            {
                sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);
                openedSourceScene = true;
            }

            var originalActiveScene = SceneManager.GetActiveScene();
            var lighting = LightingSettings.Capture();
            var overviewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(overviewScene);
            lighting.Apply();
            var entries = CollectEntries();
            var overviewRoot = BuildOverview(entries);
            CopyMainSceneLighting(overviewRoot.transform);
            EditorSceneManager.SaveScene(overviewScene, OverviewScenePath);

            SceneManager.SetActiveScene(originalActiveScene);
            EditorSceneManager.CloseScene(overviewScene, true);
            if (openedSourceScene)
                EditorSceneManager.CloseScene(sourceScene, true);

        }

        static List<OverviewEntry> CollectEntries()
        {
            var entries = new Dictionary<string, OverviewEntry>();
            foreach (var rootName in EnvironmentRoots)
            {
                var root = GameObject.Find(rootName);
                if (root == null)
                    continue;

                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(false))
                {
                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (!IsOverviewMesh(filter, renderer))
                        continue;

                    var meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
                    var key = string.IsNullOrEmpty(meshPath)
                        ? $"scene:{filter.sharedMesh.GetInstanceID()}"
                        : $"mesh:{meshPath}";

                    if (entries.TryGetValue(key, out var existing))
                    {
                        existing.useCount++;
                        continue;
                    }

                    entries.Add(key, new OverviewEntry
                    {
                        source = filter,
                        sourceRenderer = renderer,
                        stage = StageName(rootName),
                        meshPath = meshPath,
                        useCount = 1
                    });
                }
            }

            return entries.Values.ToList();
        }

        static bool IsOverviewMesh(MeshFilter filter, MeshRenderer renderer)
        {
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled)
                return false;
            if (filter.GetComponent("TerrainShape2D") != null)
                return false;

            var meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
            if (meshPath.StartsWith("Assets/GeneratedMeshes/"))
                return filter.sharedMesh.name.StartsWith("TerrainShape_TerrainMaker");
            return meshPath.StartsWith("Assets/Prebs/") || meshPath.StartsWith("Assets/Art/");
        }

        static GameObject BuildOverview(List<OverviewEntry> entries)
        {
            var root = new GameObject($"Environment Asset Overview ({entries.Count})");
            CreateLabel(root.transform, "Environment Asset Overview", new Vector3(0f, 3f, 0f), 3f, Color.white);
            CreateLabel(root.transform, "TerrainMaker results and project-made Blender meshes used in MainScene · one reference per unique mesh", new Vector3(0f, 0.5f, 0f), 1.2f, new Color(0.75f, 0.8f, 0.85f));

            var stageOrder = new[] { "Stage 1", "Stage 2", "Stage 3", "Stage 4", "Other" };
            var columnOffset = 0;
            foreach (var stage in stageOrder)
            {
                var stageEntries = entries
                    .Where(entry => entry.stage == stage)
                    .OrderBy(entry => entry.source.sharedMesh.name)
                    .ToList();
                if (stageEntries.Count == 0)
                    continue;

                var stageRoot = new GameObject($"{stage} ({stageEntries.Count})");
                stageRoot.transform.SetParent(root.transform);
                var rowCount = Mathf.CeilToInt(stageEntries.Count / (float)Columns);
                CreateStageBackdrop(stageRoot.transform, stage, columnOffset, rowCount);
                var headerX = (columnOffset + (Columns - 1) * 0.5f) * CellSize;
                CreateLabel(stageRoot.transform, stage, new Vector3(headerX, 0f, 0f), 2.2f, new Color(1f, 0.82f, 0.3f));

                for (var index = 0; index < stageEntries.Count; index++)
                {
                    var column = index % Columns;
                    var row = index / Columns;
                    var position = new Vector3((columnOffset + column) * CellSize, -(row + 1) * CellSize, 0f);
                    CreateMeshReference(stageRoot.transform, stageEntries[index], position);
                }

                columnOffset += Columns + 3;
            }

            return root;
        }

        static void CreateStageBackdrop(Transform parent, string stage, int columnOffset, int rowCount)
        {
            var material = GetStageBackgroundMaterial(stage);
            if (material == null)
                return;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Stage Background";
            panel.transform.SetParent(parent);
            panel.transform.position = new Vector3(
                (columnOffset + (Columns - 1) * 0.5f) * CellSize,
                -(rowCount * CellSize * 0.5f + 10f),
                20f);
            panel.transform.localScale = new Vector3(Columns * CellSize + 12f, rowCount * CellSize + 24f, 1f);
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material GetStageBackgroundMaterial(string stage)
        {
            var sourceRoot = stage switch
            {
                "Stage 1" => GameObject.Find("stage1_background"),
                "Stage 2" => GameObject.Find("stage2_background"),
                "Stage 3" => GameObject.Find("stage3_background"),
                "Stage 4" => GameObject.Find("stage4_background"),
                _ => null
            };
            if (sourceRoot == null)
                return null;

            var childName = stage switch
            {
                "Stage 1" => "bg7",
                "Stage 2" => "outside (1)",
                "Stage 3" => "bg7 (6)",
                "Stage 4" => "Cube (5)",
                _ => null
            };
            var source = sourceRoot.transform.Find(childName);
            var renderer = source != null ? source.GetComponent<Renderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        static void CopyMainSceneLighting(Transform overviewRoot)
        {
            var lightingRoot = new GameObject("MainScene Lighting");
            lightingRoot.transform.SetParent(overviewRoot);
            var sun = CopySceneObject("Directional Light", lightingRoot.transform);
            CopySceneObject("PostFX_Stage1", lightingRoot.transform);
            if (sun != null)
                RenderSettings.sun = sun.GetComponent<Light>();

            var cameraRoot = new GameObject("MainScene Cameras");
            cameraRoot.transform.SetParent(overviewRoot);
            CopySceneObject("Main Camera", cameraRoot.transform);
            CopySceneObject("SlimeVCam", cameraRoot.transform);
            CopySceneObject("3d Camera (1)", cameraRoot.transform);
        }

        static GameObject CopySceneObject(string name, Transform parent)
        {
            var source = GameObject.Find(name);
            if (source == null)
                return null;
            var copy = Object.Instantiate(source, parent);
            copy.name = source.name;
            return copy;
        }

        static void CreateMeshReference(Transform parent, OverviewEntry entry, Vector3 position)
        {
            var item = new GameObject($"{DisplayName(entry)} · refs {entry.useCount}");
            item.transform.SetParent(parent);
            item.transform.position = position;

            var visual = new GameObject("Visual");
            visual.transform.SetParent(item.transform);
            CopyStandaloneMesh(entry, visual.transform);

            CenterAndFit(item.transform, visual.transform);
            CreateLabel(item.transform, DisplayName(entry), new Vector3(0f, -7.5f, -0.5f), 0.7f, Color.white);
            CreateLabel(item.transform, SourceLabel(entry), new Vector3(0f, -8.6f, -0.5f), 0.45f, new Color(0.65f, 0.75f, 0.85f));
        }

        static void CopyStandaloneMesh(OverviewEntry entry, Transform parent)
        {
            parent.localRotation = entry.source.transform.rotation;
            parent.localScale = entry.source.transform.lossyScale;
            var filter = parent.gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = entry.source.sharedMesh;
            var renderer = parent.gameObject.AddComponent<MeshRenderer>();
            CopyRendererSettings(entry.sourceRenderer, renderer);
        }

        static void CopyRendererSettings(MeshRenderer source, MeshRenderer destination)
        {
            destination.sharedMaterials = source.sharedMaterials;
            destination.shadowCastingMode = ShadowCastingMode.Off;
            destination.receiveShadows = false;
        }

        static void CenterAndFit(Transform item, Transform visual)
        {
            var renderers = visual.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                return;

            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            var centerOffset = item.position - bounds.center;
            visual.position += centerOffset;
        }

        static void CreateLabel(Transform parent, string text, Vector3 localPosition, float fontSize, Color color)
        {
            var label = new GameObject(text);
            label.transform.SetParent(parent);
            label.transform.localPosition = localPosition;
            var textMesh = label.AddComponent<TextMeshPro>();
            textMesh.text = text;
            textMesh.fontSize = fontSize;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.color = color;
        }

        static string StageName(string rootName)
        {
            if (rootName.StartsWith("stage1")) return "Stage 1";
            if (rootName.StartsWith("stage2")) return "Stage 2";
            if (rootName.StartsWith("stage3")) return "Stage 3";
            if (rootName.StartsWith("stage4")) return "Stage 4";
            return "Other";
        }

        static string SourceLabel(OverviewEntry entry)
        {
            if (string.IsNullOrEmpty(entry.meshPath))
                return "scene-generated mesh";
            return entry.meshPath.Replace("Assets/", "");
        }

        static string DisplayName(OverviewEntry entry)
        {
            return entry.source.sharedMesh.name;
        }

        class OverviewEntry
        {
            public MeshFilter source;
            public MeshRenderer sourceRenderer;
            public string stage;
            public string meshPath;
            public int useCount;
        }

        class LightingSettings
        {
            Material skybox;
            AmbientMode ambientMode;
            Color ambientSkyColor;
            Color ambientEquatorColor;
            Color ambientGroundColor;
            float ambientIntensity;
            float reflectionIntensity;
            int reflectionBounces;
            bool fog;
            FogMode fogMode;
            Color fogColor;
            float fogDensity;
            float fogStartDistance;
            float fogEndDistance;

            public static LightingSettings Capture()
            {
                return new LightingSettings
                {
                    skybox = RenderSettings.skybox,
                    ambientMode = RenderSettings.ambientMode,
                    ambientSkyColor = RenderSettings.ambientSkyColor,
                    ambientEquatorColor = RenderSettings.ambientEquatorColor,
                    ambientGroundColor = RenderSettings.ambientGroundColor,
                    ambientIntensity = RenderSettings.ambientIntensity,
                    reflectionIntensity = RenderSettings.reflectionIntensity,
                    reflectionBounces = RenderSettings.reflectionBounces,
                    fog = RenderSettings.fog,
                    fogMode = RenderSettings.fogMode,
                    fogColor = RenderSettings.fogColor,
                    fogDensity = RenderSettings.fogDensity,
                    fogStartDistance = RenderSettings.fogStartDistance,
                    fogEndDistance = RenderSettings.fogEndDistance
                };
            }

            public void Apply()
            {
                RenderSettings.skybox = skybox;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.reflectionBounces = reflectionBounces;
                RenderSettings.fog = fog;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStartDistance;
                RenderSettings.fogEndDistance = fogEndDistance;
            }
        }
    }
}
