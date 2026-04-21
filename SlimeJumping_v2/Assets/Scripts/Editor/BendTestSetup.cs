using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using MegaFiers;
using SlimeJumping.Platforms;
using SlimeJumping.Rendering;

namespace SlimeJumping.Editor
{
    // One-click setup for the MegaFiers-bend + LocalTrail validation harness.
    // Generates a subdivided flat plane mesh (UV0 = rest-pose normalized to [0,1]),
    // a material using Platform_Trail_Bend shader, and a GameObject prefab with the
    // full bend+trail stack. Dropped into the active scene at the scene-view pivot.
    public static class BendTestSetup
    {
        const string MeshAssetPath     = "Assets/Prebs/Platform/Meshes/BendTestBox.asset";
        const string MaterialAssetPath = "Assets/Prebs/Platform/Materials/BendTestBox.mat";
        const string PrefabAssetPath   = "Assets/Prebs/Platform/BendTestPlatform.prefab";
        const string ShaderName        = "SlimeJumping/Platform Trail Bend";

        const float  BoxLength   = 3.0f;
        const float  BoxHeight   = 0.3f;
        const float  BoxDepth    = 0.4f;
        const int    SegmentsX   = 21;

        [MenuItem("SlimeJumping/Platform/Setup BendTest Platform")]
        public static void SetupBendTestPlatform()
        {
            var mesh     = EnsureMeshAsset();
            var material = EnsureMaterialAsset();
            var prefab   = EnsurePrefabAsset(mesh, material);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Setup BendTest Platform");

            // Park in front of camera / scene pivot for visibility.
            var view = SceneView.lastActiveSceneView;
            if (view != null) instance.transform.position = view.pivot + new Vector3(0f, 0f, 0f);
            Selection.activeGameObject = instance;
            SceneView.FrameLastActiveSceneView();
        }

        // --- Mesh -----------------------------------------------------------

        internal static Mesh EnsureMeshAsset()
        {
            EnsureFolder("Assets/Prebs/Platform/Meshes");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
            var mesh = existing != null ? existing : new Mesh();
            mesh.name = "BendTestBox";
            PopulateBox(mesh, BoxLength, BoxHeight, BoxDepth, SegmentsX);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, MeshAssetPath);
            }
            else
            {
                EditorUtility.SetDirty(mesh);
            }
            return mesh;
        }

        // 3D box mesh. X∈[0,L] (bend pivot at left), Y∈[-h/2,h/2], Z∈[-d/2,d/2].
        // Long faces (front/back/top/bottom) are subdivided along X so the bend
        // reads as a smooth curve; end caps are plain quads. UV0 on every face
        // = rest-pose XY normalized to [0,1] so Platform_Trail_Bend shader samples
        // the trail mask at the same coord LocalTrailReceiver paints to. This
        // means top/bottom/side faces all sample the *edge* strip of the RT —
        // which is where the slime actually paints when it touches the surface.
        static void PopulateBox(Mesh mesh, float length, float height, float depth, int segX)
        {
            int vx = Mathf.Max(2, segX + 1);
            float minY = -height * 0.5f, maxY = +height * 0.5f;
            float minZ = -depth  * 0.5f, maxZ = +depth  * 0.5f;

            var v  = new List<Vector3>(8 * vx);
            var uv = new List<Vector2>(8 * vx);
            var n  = new List<Vector3>(8 * vx);
            var t  = new List<int>();

            // Long subdivided face (two rows in a secondary axis). uvV is the
            // constant UV.y to stamp on every vertex of this face; the trail's
            // "band" for this face ends up at v=uvV on the mask.
            void AddLongFace(Vector3 normal, System.Func<float, int, Vector3> posFn, float uvVTop, float uvVBot, bool flipWinding)
            {
                int start = v.Count;
                for (int row = 0; row < 2; row++)
                for (int i = 0; i < vx; i++)
                {
                    float u = i / (float)(vx - 1);
                    v.Add(posFn(u, row));
                    uv.Add(new Vector2(u, row == 0 ? uvVBot : uvVTop));
                    n.Add(normal);
                }
                for (int i = 0; i < vx - 1; i++)
                {
                    int a = start + i, b = a + 1, c = a + vx, d = c + 1;
                    if (!flipWinding) { t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d); }
                    else              { t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c); }
                }
            }

            // Front (+Z, facing camera) — trail is clearly visible here. UV.y
            // spans 0..1 so the shader can also show trails painted anywhere
            // in the receiver rect (not just the top/bottom edges).
            AddLongFace(
                new Vector3(0f, 0f, 1f),
                (u, row) => new Vector3(u * length, row == 0 ? minY : maxY, maxZ),
                uvVTop: 1f, uvVBot: 0f, flipWinding: false);

            // Back (-Z)
            AddLongFace(
                new Vector3(0f, 0f, -1f),
                (u, row) => new Vector3(u * length, row == 0 ? minY : maxY, minZ),
                uvVTop: 1f, uvVBot: 0f, flipWinding: true);

            // Top (+Y) — slime walks on this. UV.y = 1 across the whole face
            // (the top edge of the receiver rect).
            AddLongFace(
                new Vector3(0f, 1f, 0f),
                (u, row) => new Vector3(u * length, maxY, row == 0 ? maxZ : minZ),
                uvVTop: 1f, uvVBot: 1f, flipWinding: false);

            // Bottom (-Y) — UV.y = 0
            AddLongFace(
                new Vector3(0f, -1f, 0f),
                (u, row) => new Vector3(u * length, minY, row == 0 ? maxZ : minZ),
                uvVTop: 0f, uvVBot: 0f, flipWinding: true);

            // Left end cap (X=0) — quad with normal -X
            int ls = v.Count;
            v.Add(new Vector3(0f, minY, maxZ)); uv.Add(new Vector2(0f, 0f)); n.Add(new Vector3(-1f, 0f, 0f));
            v.Add(new Vector3(0f, minY, minZ)); uv.Add(new Vector2(0f, 0f)); n.Add(new Vector3(-1f, 0f, 0f));
            v.Add(new Vector3(0f, maxY, maxZ)); uv.Add(new Vector2(0f, 1f)); n.Add(new Vector3(-1f, 0f, 0f));
            v.Add(new Vector3(0f, maxY, minZ)); uv.Add(new Vector2(0f, 1f)); n.Add(new Vector3(-1f, 0f, 0f));
            t.Add(ls+0); t.Add(ls+2); t.Add(ls+1);
            t.Add(ls+1); t.Add(ls+2); t.Add(ls+3);

            // Right end cap (X=L) — quad with normal +X
            int rs = v.Count;
            v.Add(new Vector3(length, minY, maxZ)); uv.Add(new Vector2(1f, 0f)); n.Add(new Vector3(1f, 0f, 0f));
            v.Add(new Vector3(length, minY, minZ)); uv.Add(new Vector2(1f, 0f)); n.Add(new Vector3(1f, 0f, 0f));
            v.Add(new Vector3(length, maxY, maxZ)); uv.Add(new Vector2(1f, 1f)); n.Add(new Vector3(1f, 0f, 0f));
            v.Add(new Vector3(length, maxY, minZ)); uv.Add(new Vector2(1f, 1f)); n.Add(new Vector3(1f, 0f, 0f));
            t.Add(rs+0); t.Add(rs+1); t.Add(rs+2);
            t.Add(rs+1); t.Add(rs+3); t.Add(rs+2);

            mesh.Clear();
            mesh.vertices  = v.ToArray();
            mesh.uv        = uv.ToArray();
            mesh.normals   = n.ToArray();
            mesh.triangles = t.ToArray();
            mesh.RecalculateBounds();
        }

        // --- Material -------------------------------------------------------

        internal static Material EnsureMaterialAsset()
        {
            EnsureFolder("Assets/Prebs/Platform/Materials");
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                UnityEngine.Debug.LogError($"[BendTestSetup] Shader '{ShaderName}' not found. Platform_Trail_Bend.shader must compile first.");
                return null;
            }
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
            if (existing != null) { existing.shader = shader; return existing; }
            var mat = new Material(shader) { name = "BendTestBox" };
            mat.SetColor("_BaseColor", new Color(0.85f, 0.75f, 0.55f, 1f));
            mat.SetColor("_TrailTint", new Color(0.15f, 0.08f, 0.04f, 1f));
            AssetDatabase.CreateAsset(mat, MaterialAssetPath);
            return mat;
        }

        // --- Prefab ---------------------------------------------------------

        static GameObject EnsurePrefabAsset(Mesh mesh, Material material)
        {
            EnsureFolder("Assets/Prebs/Platform");

            var root = new GameObject("BendTestPlatform");
            try
            {
                // Rigid collider — bend is purely visual, slime walks on this flat box.
                var col = root.AddComponent<BoxCollider2D>();
                col.size = new Vector2(BoxLength, BoxHeight);
                col.offset = new Vector2(BoxLength * 0.5f, 0f);

                var mf = root.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = root.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;

                // MegaModifyObject wants the source mesh reference. cachedMesh is set
                // internally on Awake — in the editor we just provide originalMesh.
                var modify = root.AddComponent<MegaModifyObject>();
                modify.originalMesh = mesh;
                modify.UpdateMode = MegaUpdateMode.LateUpdate;
                modify.recalcbounds = true;
                modify.NormalMethod = MegaNormalMethod.Mega;

                // MegaBend.CalcR treats `axis` as the mesh's LENGTH axis (bbox span
                // along it determines radius). Our plane spans X∈[0,L] → axis=X.
                var bend = root.AddComponent<MegaBend>();
                bend.axis = MegaAxis.X;
                bend.angle = 0f;
                bend.dir = 0f;

                var driver = root.AddComponent<BendTestPlatform>();
                var drvSo = new SerializedObject(driver);
                drvSo.FindProperty("bend").objectReferenceValue = bend;
                drvSo.ApplyModifiedPropertiesWithoutUndo();

                // Receiver's (localMin, localSize) == mesh rest-pose extents, so
                // StampAt's `(lp - localMin)/localSize` produces the same normalized
                // [0,1] UV the mesh carries in UV0 → paint and sample share a frame.
                var receiver = root.AddComponent<LocalTrailReceiver>();
                var recSo = new SerializedObject(receiver);
                recSo.FindProperty("localMin").vector2Value  = new Vector2(0f, -BoxHeight * 0.5f);
                recSo.FindProperty("localSize").vector2Value = new Vector2(BoxLength, BoxHeight);
                recSo.FindProperty("pixelsPerUnit").intValue = 64;
                recSo.FindProperty("contactCollider").objectReferenceValue = col;
                recSo.FindProperty("targetRenderer").objectReferenceValue  = mr;
                recSo.ApplyModifiedPropertiesWithoutUndo();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabAssetPath);
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        // --- Utils ----------------------------------------------------------

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
