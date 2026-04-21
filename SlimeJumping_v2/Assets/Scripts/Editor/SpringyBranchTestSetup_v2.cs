using UnityEngine;
using UnityEditor;
using MegaFiers;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    // SpringyBranch v2 test harness. Mirrors BendTestDeformingSetup but swaps the
    // manual-angle BendTestPlatform driver for SpringyBranchPlatform_v2 — i.e. the
    // full spring-damper + rider pump dynamics driving a bending+colliding branch.
    // Reuses BendTestSetup's mesh and material so we're testing v2 physics on the
    // same proven geometry the Phase-A straight-platform validation used.
    public static class SpringyBranchTestSetup_v2
    {
        const string PrefabAssetPath = "Assets/Prebs/Platform/SpringyBranchV2_Test.prefab";

        // Must match BendTestSetup.BoxLength — used to set branchLength so the spring
        // dynamics know the beam's length without reading the private mesh bounds.
        const float BoxLength = 3.0f;

        [MenuItem("SlimeJumping/Platform/Setup SpringyBranch V2 Test")]
        public static void SetupSpringyBranchV2Test()
        {
            var mesh     = BendTestSetup.EnsureMeshAsset();
            var material = BendTestSetup.EnsureMaterialAsset();
            if (mesh == null || material == null) return;

            var prefab = EnsurePrefabAsset(mesh, material);
            if (prefab == null) return;

            var root = new GameObject("SpringyBranchV2_Test");
            Undo.RegisterCreatedObjectUndo(root, "Setup SpringyBranch V2 Test");
            var view = SceneView.lastActiveSceneView;
            if (view != null) root.transform.position = view.pivot;

            // Three variants tuned differently so designers can feel the knobs.
            PlaceVariant(prefab, root.transform, new Vector3(-4f, 0f, 0f), "Bouncy",
                naturalFreqHz: 1.2f, dampingRatio: 0.15f, riderPumpGain: 1.5f,
                riderTorqueScale: 1.0f, tint: new Color(0.9f, 0.5f, 0.3f));

            PlaceVariant(prefab, root.transform, new Vector3(0f, 0f, 0f), "Standard",
                naturalFreqHz: 1.0f, dampingRatio: 0.2f, riderPumpGain: 1.0f,
                riderTorqueScale: 1.0f, tint: new Color(0.85f, 0.75f, 0.55f));

            PlaceVariant(prefab, root.transform, new Vector3(4f, 0f, 0f), "Heavy",
                naturalFreqHz: 0.6f, dampingRatio: 0.25f, riderPumpGain: 0.7f,
                riderTorqueScale: 1.3f, tint: new Color(0.4f, 0.3f, 0.2f));

            Selection.activeGameObject = root;
            SceneView.FrameLastActiveSceneView();
        }

        static GameObject EnsurePrefabAsset(Mesh mesh, Material material)
        {
            EnsureFolder("Assets/Prebs/Platform");

            var root = new GameObject("SpringyBranchV2");
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

                // Physics pivot anchor — a child Transform at MegaBend's rendered
                // fixed point (mesh origin for default gizmoPos + axis=X). The
                // motor reads this Transform's world position as the torque origin,
                // so the physics pivot is explicit and independent of MegaBend config.
                var pivotGo = new GameObject("Pivot");
                pivotGo.transform.SetParent(root.transform, false);
                pivotGo.transform.localPosition = Vector3.zero;

                // Rigidbody2D is required by PlatformMotor — it's added automatically
                // via [RequireComponent], but adding the subclass explicitly ensures
                // the bodyType/interpolation fields get the base Awake's defaults when
                // the prefab is instantiated into a scene.
                var motor = root.AddComponent<SpringyBranchPlatform_v2>();
                var motorSo = new SerializedObject(motor);
                motorSo.FindProperty("bend").objectReferenceValue = bend;
                motorSo.FindProperty("branchLength").floatValue = BoxLength;
                motorSo.FindProperty("pivotAnchor").objectReferenceValue = pivotGo.transform;
                motorSo.FindProperty("bendDownWorld").vector2Value = Vector2.down;
                motorSo.ApplyModifiedPropertiesWithoutUndo();

                // Ensure rb mass is non-trivial — springs stiffness scales with it.
                var rb = root.GetComponent<Rigidbody2D>();
                if (rb != null) rb.mass = 2f;

                var edge = root.AddComponent<EdgeCollider2D>();

                var deform = root.AddComponent<MegaDeform2DCollider>();
                deform.Source = modify;
                deform.Target = edge;
                deform.ClosedLoop = false;

                int[] indices = MegaDeform2DColliderBaker.BakeTopEdge(
                    mesh,
                    zTarget: mesh.bounds.max.z,
                    yEps: 1e-3f,
                    zEps: 1e-3f,
                    xDedupeEps: 1e-4f);

                if (indices != null && indices.Length >= 2)
                {
                    deform.ContourIndices = indices;
                    var verts = mesh.vertices;
                    var pts = new Vector2[indices.Length];
                    for (int i = 0; i < indices.Length; i++)
                        pts[i] = new Vector2(verts[indices[i]].x, verts[indices[i]].y);
                    edge.points = pts;
                }
                else
                {
                    Debug.LogWarning("[SpringyBranchTestSetup_v2] TopEdge bake produced < 2 indices; collider empty.");
                }

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static void PlaceVariant(GameObject prefab, Transform parent, Vector3 localOffset, string variantName,
            float naturalFreqHz, float dampingRatio, float riderPumpGain, float riderTorqueScale, Color tint)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            inst.name = $"SpringyBranchV2_{variantName}";
            inst.transform.localPosition = localOffset;

            // Tune this instance without breaking the prefab link.
            var motor = inst.GetComponent<SpringyBranchPlatform_v2>();
            var so = new SerializedObject(motor);
            so.FindProperty("naturalFreqHz").floatValue    = naturalFreqHz;
            so.FindProperty("dampingRatio").floatValue     = dampingRatio;
            so.FindProperty("riderPumpGain").floatValue    = riderPumpGain;
            so.FindProperty("riderTorqueScale").floatValue = riderTorqueScale;
            so.ApplyModifiedPropertiesWithoutUndo();

            // Per-instance tint using a new MaterialPropertyBlock on the renderer —
            // doesn't modify the shared material.
            var mr = inst.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var mpb = new MaterialPropertyBlock();
                mr.GetPropertyBlock(mpb);
                mpb.SetColor("_BaseColor", tint);
                mr.SetPropertyBlock(mpb);
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
