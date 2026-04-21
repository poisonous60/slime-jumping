using UnityEngine;
using UnityEditor;
using MegaFiers;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // Builds a prefab variant of BendTestPlatform where the 2D collider deforms
    // in sync with the mesh (via MegaDeform2DCollider), instead of staying rigid.
    // Uses an EdgeCollider2D hugging the top-front edge — the walkable surface.
    public static class BendTestDeformingSetup
    {
        const string PrefabAssetPath = "Assets/Prebs/Platform/BendTestDeformingPlatform.prefab";

        [MenuItem("SlimeJumping/Platform/Setup BendTest Deforming Platform")]
        public static void SetupBendTestDeformingPlatform()
        {
            var mesh     = BendTestSetup.EnsureMeshAsset();
            var material = BendTestSetup.EnsureMaterialAsset();
            if (mesh == null || material == null) return;

            var prefab = EnsurePrefabAsset(mesh, material);
            if (prefab == null) return;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Setup BendTest Deforming Platform");

            var view = SceneView.lastActiveSceneView;
            if (view != null) instance.transform.position = view.pivot;
            Selection.activeGameObject = instance;
            SceneView.FrameLastActiveSceneView();
        }

        static GameObject EnsurePrefabAsset(Mesh mesh, Material material)
        {
            EnsureFolder("Assets/Prebs/Platform");

            var root = new GameObject("BendTestDeformingPlatform");
            try
            {
                var mf = root.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;

                var modify = root.AddComponent<MegaModifyObject>();
                modify.originalMesh = mesh;
                modify.UpdateMode = MegaUpdateMode.LateUpdate;
                modify.recalcbounds = true;
                modify.NormalMethod = MegaNormalMethod.Mega;

                var bend = root.AddComponent<MegaBend>();
                bend.axis = MegaAxis.X;
                bend.angle = 0f;
                bend.dir = 0f;

                var driver = root.AddComponent<BendTestPlatform>();
                var drvSo = new SerializedObject(driver);
                drvSo.FindProperty("bend").objectReferenceValue = bend;
                drvSo.ApplyModifiedPropertiesWithoutUndo();

                // Edge collider — points populated each frame by MegaDeform2DCollider.
                // Initial points: the rest-pose top edge so the collider is visible
                // before the first LateUpdate runs (important for edit-mode gizmos).
                var edge = root.AddComponent<EdgeCollider2D>();

                var deform = root.AddComponent<MegaDeform2DCollider>();
                deform.Source = modify;
                deform.Target = edge;
                deform.ClosedLoop = false;

                // Bake top-edge contour indices at the front face (z = max z).
                // Epsilons are generous relative to the 0.3 m box height / 0.4 m depth.
                int[] indices = MegaDeform2DColliderBaker.BakeTopEdge(
                    mesh,
                    zTarget: mesh.bounds.max.z,
                    yEps: 1e-3f,
                    zEps: 1e-3f,
                    xDedupeEps: 1e-4f);

                if (indices == null || indices.Length < 2)
                {
                    Debug.LogWarning(
                        "[BendTestDeformingSetup] TopEdge bake produced < 2 indices. " +
                        "EdgeCollider2D will remain empty until re-baked from the inspector.");
                }
                else
                {
                    deform.ContourIndices = indices;

                    // Seed EdgeCollider2D with the rest-pose positions so the collider
                    // reads correctly in edit mode before MegaDeform2DCollider runs once.
                    var verts = mesh.vertices;
                    var pts = new Vector2[indices.Length];
                    for (int i = 0; i < indices.Length; i++)
                        pts[i] = new Vector2(verts[indices[i]].x, verts[indices[i]].y);
                    edge.points = pts;

                    Debug.Log($"[BendTestDeformingSetup] Baked {indices.Length} top-edge indices.");
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string curr = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = curr + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(curr, parts[i]);
                curr = next;
            }
        }
    }
}
