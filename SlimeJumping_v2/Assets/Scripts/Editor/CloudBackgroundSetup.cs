using UnityEngine;
using UnityEditor;
using SlimeJumping.Background;

namespace SlimeJumping.Editor
{
    public static class CloudBackgroundSetup
    {
        const int   CloudCount     = 14;
        const float ZDepth         = 10f;
        const float XRange         = 30f;
        const float YMin           = -2f;
        const float YMax           = 14f;
        const float ScaleMin       = 1.0f;
        const float ScaleMax       = 2.5f;
        const float ParallaxFactor = 0.4f;   // 1 = world-fixed, 0 = locked to camera

        // Quibli ships these as pre-shaped Mesh assets meant for the Cloud3D shader.
        static readonly string[] CloudMeshPaths =
        {
            "Assets/ThirdParty/Quibli/Demos/[Common]/Models/Foliage Generator Exported Meshes/Cloud3D-MeshCarrier_Cloud_01.asset",
            "Assets/ThirdParty/Quibli/Demos/[Common]/Models/Foliage Generator Exported Meshes/Cloud3D-MeshCarrier_Cloud_02.asset",
            "Assets/ThirdParty/Quibli/Demos/[Common]/Models/Foliage Generator Exported Meshes/Cloud3D-MeshCarrier_Cloud_03.asset",
        };

        // Cloned to seed the runtime material with a working _ShadingGradientTexture.
        // Without it, Cloud3D renders flat white because the shading lookup is null.
        const string TemplateMaterialPath =
            "Assets/ThirdParty/Quibli/Demos/Nature/Materials/NatureScene_Cloud3D.mat";

        [MenuItem("SlimeJumping/Background/Setup Cloud Background")]
        public static void SetupCloudBackground()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null)
            {
                Debug.LogError("No MainCamera found. Tag a camera as MainCamera first.");
                return;
            }

            var meshes = LoadCloudMeshes();
            if (meshes.Length == 0)
            {
                Debug.LogError("No Quibli Cloud3D meshes found at expected paths. Re-import Quibli demos.");
                return;
            }

            var root = new GameObject("CloudBackground");
            Undo.RegisterCreatedObjectUndo(root, "Create Cloud Background");

            var follower = root.AddComponent<ParallaxFollower>();
            var so = new SerializedObject(follower);
            so.FindProperty("target").objectReferenceValue = cam.transform;
            so.FindProperty("factor").vector3Value = new Vector3(ParallaxFactor, ParallaxFactor, 0f);
            so.ApplyModifiedProperties();

            var mat = MakeCloudMaterial();

            for (int i = 0; i < CloudCount; i++)
            {
                CreateCloud(i, root.transform, mat, meshes[Random.Range(0, meshes.Length)]);
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log($"Cloud Background setup complete: {CloudCount} 3D clouds at z={ZDepth}, parallax factor={ParallaxFactor}.");
        }

        static void CreateCloud(int index, Transform parent, Material mat, Mesh mesh)
        {
            var go = new GameObject($"Cloud_{index:D2}");
            Undo.RegisterCreatedObjectUndo(go, "Create Cloud");
            go.transform.parent = parent;

            float x = Random.Range(-XRange, XRange);
            float y = Random.Range(YMin, YMax);
            go.transform.position = new Vector3(x, y, ZDepth);

            // Random Y rotation so identical meshes don't read as duplicates.
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            float s = Random.Range(ScaleMin, ScaleMax);
            go.transform.localScale = Vector3.one * s;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Mesh[] LoadCloudMeshes()
        {
            var list = new System.Collections.Generic.List<Mesh>();
            foreach (var p in CloudMeshPaths)
            {
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
                if (m != null) list.Add(m);
            }
            return list.ToArray();
        }

        static Material MakeCloudMaterial()
        {
            var template = AssetDatabase.LoadAssetAtPath<Material>(TemplateMaterialPath);
            if (template != null)
            {
                return new Material(template) { name = "Mat_Cloud3D_Generated" };
            }

            Debug.LogWarning($"Cloud3D template material not found at {TemplateMaterialPath} — clouds will look flat.");
            var shader = Shader.Find("Quibli/Cloud3D") ?? Shader.Find("Universal Render Pipeline/Unlit");
            return new Material(shader) { name = "Mat_Cloud3D_Generated" };
        }
    }
}
