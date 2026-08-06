using UnityEditor;
using UnityEngine;
using SlimeJumping.Rendering;
using SlimeJumping.TerrainShape;
using SlimeJumping.TerrainShape.Editor;

namespace SlimeJumping.Editor
{
    /// <summary>Appends a "Bell Reveal Platform" section to the TerrainShape2D inspector: installs the
    /// drawn shape as a hidden platform for BellSoundWave — reveal material, Z=0 cross-section collider,
    /// goo overlay and trail receiver, the same wiring Spawn Sound Wave Test Set gives the FBX platforms.
    ///
    /// The shape's own "Install material" slot doubles as the per-shape material slot: any material on
    /// the reveal shader is used as-is, so a recoloured copy just gets dropped in there; otherwise the
    /// shared Mat_BellReveal_Terrain is created and assigned. Colour itself lives in the material's
    /// Quibli gradient, with Texture Impact at 0 because a generated mesh has no atlas UVs.
    ///
    /// Which bell owns the platform is hierarchy, not a field — move the installed wrapper under the
    /// bell's parent, or it stays hidden forever.</summary>
    [InitializeOnLoad]
    public static class BellRevealTerrainInstall
    {
        const string RevealShaderName = "Quibli/Stylized Lit (Bell Reveal)";
        const string RevealMatPath    = "Assets/Art/Shader/Mat_BellReveal_Terrain.mat";
        const string GooMatPath       = "Assets/Art/Shader/Mat_TrailGooOverlay.mat";
        const string GooOverlayName   = "TrailGooOverlay";
        const string ColliderMenu     = "SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0 cross-section)";
        const string MeshFolder       = "Assets/GeneratedMeshes";

        static BellRevealTerrainInstall()
        {
            TerrainShape2DEditor.OnAdditionalInspectorGUI += DrawSection;
        }

        static void DrawSection(TerrainShape2D src, UnityEditor.Editor editor)
        {
            EditorGUILayout.LabelField("Bell Reveal Platform", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            var slot = (Material)EditorGUILayout.ObjectField(
                new GUIContent("Reveal material",
                    "Material the installed platform gets. Same slot as 'Install material' above. " +
                    "Drop a recoloured copy here for a per-shape colour; empty falls back to the shared " + RevealMatPath + "."),
                src.installMaterial, typeof(Material), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(src, "Set reveal material");
                src.installMaterial = slot;
                EditorUtility.SetDirty(src);
            }
            if (src.installMaterial != null && src.installMaterial.shader != RevealShader())
                EditorGUILayout.HelpBox($"'{src.installMaterial.name}' is not on {RevealShaderName} — installing replaces it with the shared reveal material.", MessageType.Warning);

            var mesh = src.GetComponent<MeshFilter>().sharedMesh;
            using (new EditorGUI.DisabledScope(mesh == null || mesh.vertexCount < 3))
            {
                if (GUILayout.Button(new GUIContent("Install as Bell Reveal Platform",
                    "Save the mesh and spawn a hidden platform: reveal material + Z=0 collider wrapper + goo overlay + trail receiver.")))
                    Install(src, mesh);
            }

            EditorGUILayout.HelpBox(
                "Invisible until a bell's sound wave passes over it. Move the installed _Collider2D wrapper under the bell's parent — that shared parent is what binds it to that bell.\n" +
                "Colour: pick it in the reveal material's Quibli Gradient (right end = lit, left = shaded). Keep Texture Impact at 0.\n" +
                "Edit-mode preview alpha and wave timing live in Resources/BellSoundWaveConfig.",
                MessageType.Info);
        }

        static void Install(TerrainShape2D src, Mesh liveMesh)
        {
            var mat = ResolveRevealMaterial(src);
            if (mat == null) return;
            var gooMat = AssetDatabase.LoadAssetAtPath<Material>(GooMatPath);

            var meshAsset = SaveMesh(liveMesh, src.name);
            var go = new GameObject(meshAsset.name);
            Undo.RegisterCreatedObjectUndo(go, "Install bell reveal platform");
            go.transform.SetParent(src.transform.parent, worldPositionStays: false);
            go.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
            go.transform.localScale = src.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = meshAsset;
            var body = go.AddComponent<MeshRenderer>();
            body.sharedMaterial = mat;

            // Generate() takes its input from the selection and leaves the wrapper selected.
            Selection.activeGameObject = go;
            if (!EditorApplication.ExecuteMenuItem(ColliderMenu))
            {
                Debug.LogError($"[BellReveal] menu item '{ColliderMenu}' not found — collider, goo and receiver skipped.", go);
                return;
            }
            var wrapper = Selection.activeGameObject != go ? Selection.activeGameObject : null;
            var col = wrapper != null ? wrapper.GetComponent<PolygonCollider2D>() : null;
            if (col == null)
            {
                Debug.LogWarning("[BellReveal] no collider generated — the shape must cross world Z=0. Mesh installed without collider, goo or receiver.", go);
                return;
            }

            if (gooMat != null)
            {
                var goo = AddGooOverlay(wrapper, body, gooMat);
                AddReceiver(wrapper, col, goo);
            }
            else Debug.LogWarning($"[BellReveal] {GooMatPath} missing — installed without goo overlay and trail receiver.", wrapper);

            EditorUtility.SetDirty(wrapper);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(wrapper.scene);
            Debug.Log($"[BellReveal] '{wrapper.name}' installed. Move it under a bell's parent to bind it, then recolour {AssetDatabase.GetAssetPath(mat)}.", wrapper);
        }

        static Shader _revealShader;

        static Shader RevealShader()
        {
            if (_revealShader == null) _revealShader = Shader.Find(RevealShaderName);
            return _revealShader;
        }

        static Material ResolveRevealMaterial(TerrainShape2D src)
        {
            var shader = RevealShader();
            if (shader == null)
            {
                Debug.LogError($"[BellReveal] shader '{RevealShaderName}' not found (compile errors?).");
                return null;
            }
            if (src.installMaterial != null && src.installMaterial.shader == shader) return src.installMaterial;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(RevealMatPath);
            if (mat == null)
            {
                mat = new Material(shader);
                mat.SetFloat("_TextureImpact", 0f);
                AssetDatabase.CreateAsset(mat, RevealMatPath);
                AssetDatabase.SaveAssets();
            }
            Undo.RecordObject(src, "Set bell reveal material");
            src.installMaterial = mat;
            EditorUtility.SetDirty(src);
            return mat;
        }

        static Mesh SaveMesh(Mesh live, string shapeName)
        {
            if (!AssetDatabase.IsValidFolder(MeshFolder))
                AssetDatabase.CreateFolder("Assets", "GeneratedMeshes");
            var copy = Object.Instantiate(live);
            copy.name = $"TerrainShape_{shapeName}_BellReveal";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{copy.name}.asset");
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Mesh>(path);
        }

        // Goo rides its own renderer, nudged camera-ward, so trail marks stay off the reveal shader.
        static MeshRenderer AddGooOverlay(GameObject wrapper, MeshRenderer body, Material gooMat)
        {
            var go = new GameObject(GooOverlayName);
            Undo.RegisterCreatedObjectUndo(go, "Add goo overlay");
            go.transform.SetParent(wrapper.transform, worldPositionStays: false);
            var bodyT = body.transform;
            go.transform.localPosition = bodyT.localPosition + new Vector3(0f, 0f, -0.02f);
            go.transform.localRotation = bodyT.localRotation;
            go.transform.localScale    = bodyT.localScale;

            go.AddComponent<MeshFilter>().sharedMesh = body.GetComponent<MeshFilter>().sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = gooMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static void AddReceiver(GameObject wrapper, PolygonCollider2D col, MeshRenderer goo)
        {
            var receiver = Undo.AddComponent<LocalTrailReceiver>(wrapper);

            // Local paths, not collider.bounds — those are stale under autoSyncTransforms off.
            Vector2 lmin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 lmax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (int p = 0; p < col.pathCount; p++)
            {
                foreach (var pt in col.GetPath(p))
                {
                    Vector2 lp = pt + col.offset;
                    lmin = Vector2.Min(lmin, lp);
                    lmax = Vector2.Max(lmax, lp);
                }
            }

            Vector3 ls = wrapper.transform.lossyScale;
            lmin -= new Vector2(0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)));
            lmax += new Vector2(0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), 0.2f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)));

            var so = new SerializedObject(receiver);
            so.FindProperty("localMin").vector2Value  = lmin;
            so.FindProperty("localSize").vector2Value = lmax - lmin;
            so.FindProperty("pixelsPerUnit").intValue = 20;
            so.FindProperty("contactCollider").objectReferenceValue = col;
            so.FindProperty("targetRenderer").objectReferenceValue  = goo;
            so.FindProperty("overrideId").stringValue = System.Guid.NewGuid().ToString("N");
            so.ApplyModifiedProperties();
        }
    }
}
