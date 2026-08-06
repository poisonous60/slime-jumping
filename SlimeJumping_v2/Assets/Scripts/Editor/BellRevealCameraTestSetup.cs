using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Camera;

namespace SlimeJumping.EditorTools
{
    /// <summary>Builds the skeleton for a bell-triggered reveal: a static VCam plus the component that
    /// raises it. Reads existing scene objects only as templates and never edits them. The bell is left
    /// unassigned on purpose — pick which one reveals in the inspector.</summary>
    public static class BellRevealCameraTestSetup
    {
        private const string RootName = "BellRevealCameraTest";
        private const float CameraDistance = 12f;

        [MenuItem("SlimeJumping/Mechanism/Setup Bell Reveal Camera")]
        public static void SetupBellRevealCamera()
        {
            var brain = Object.FindAnyObjectByType<CinemachineBrain>();
            if (brain == null)
            {
                Debug.LogError("[BellRevealCameraTestSetup] No CinemachineBrain in the active scene.");
                return;
            }

            Bounds region = FindRevealRegion();

            // Keep the designer-drawn region on re-run.
            var prior = GameObject.Find(RootName);
            if (prior != null)
            {
                var priorBox = prior.GetComponent<BoxCollider2D>();
                if (priorBox != null) region = priorBox.bounds;
                // Destroying the Inspector's current target throws inside GameObjectInspector.OnEnable.
                if (Selection.activeGameObject != null
                    && Selection.activeGameObject.transform.IsChildOf(prior.transform))
                    Selection.activeObject = null;
                Undo.DestroyObjectImmediate(prior);
            }

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Bell Reveal Camera");
            root.transform.position = new Vector3(region.center.x, region.center.y, 0f);

            // The box IS the authoring surface for "which region" — drag/resize it with the collider tool.
            var regionBox = root.AddComponent<BoxCollider2D>();
            regionBox.isTrigger = true;
            regionBox.size = new Vector2(region.size.x, region.size.y);

            var vcamGo = new GameObject("BellRevealVCam");
            vcamGo.transform.SetParent(root.transform, worldPositionStays: false);
            vcamGo.transform.position = new Vector3(region.center.x, region.center.y, -CameraDistance);

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Priority = -100;
            vcam.Lens = CloneSceneLens();
            // No Follow/LookAt and no composer: the shot is fixed, BellRevealCamera fits it to the box.

            var reveal = root.AddComponent<BellRevealCamera>();
            var so = new SerializedObject(reveal);
            so.FindProperty("revealVCam").objectReferenceValue = vcam;
            so.FindProperty("revealRegion").objectReferenceValue = regionBox;
            so.FindProperty("brain").objectReferenceValue = brain;
            // The proxy is where the camera normally looks, so the ears keep their usual vantage point.
            var proxy = Object.FindAnyObjectByType<SlimeCameraTargetProxy>();
            Transform anchor = proxy != null ? proxy.transform : null;
            if (anchor == null)
            {
                var slime = Object.FindAnyObjectByType<SlimeJumping.Controller.SlimeController>();
                if (slime != null) anchor = slime.transform;
            }
            if (anchor != null) so.FindProperty("audioAnchor").objectReferenceValue = anchor;
            var reader = Object.FindAnyObjectByType<SlimeJumping.InputHandling.SlimeInputReader>();
            if (reader != null) so.FindProperty("inputReader").objectReferenceValue = reader;
            else Debug.LogWarning("[BellRevealCameraTestSetup] No slime found — Audio Anchor left empty, "
                                + "the listener will park where the camera stood at the strike.");
            so.ApplyModifiedProperties();
            EnsureConfigAsset();
            reveal.FitVCamToRegion();

            Selection.activeGameObject = root;
            Debug.Log("[BellRevealCameraTestSetup] Created " + RootName + " over " + region
                    + ". Assign the Bell field, then resize the box to pick the region.", root);
        }

        // Never overwritten: an existing asset carries the designer's tuning.
        private static void EnsureConfigAsset()
        {
            const string path = "Assets/Resources/BellRevealCameraConfig.asset";
            if (AssetDatabase.LoadAssetAtPath<BellRevealCameraConfig>(path) != null) return;

            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<BellRevealCameraConfig>(), path);
            AssetDatabase.SaveAssets();
            Debug.Log("[BellRevealCameraTestSetup] Created " + path);
        }

        // Temple is the region the bells sit under, so it is the useful starting box.
        private static Bounds FindRevealRegion()
        {
            var fallback = new Bounds(new Vector3(117.3f, 408.6f, 0f), new Vector3(28f, 18f, 0f));

            var temple = GameObject.Find("stage4_floor1/temple");
            if (temple == null) return fallback;

            var renderers = temple.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return fallback;

            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            b.center = new Vector3(b.center.x, b.center.y, 0f);
            return b;
        }

        private static LensSettings CloneSceneLens()
        {
            var slimeVCamGo = GameObject.Find("SlimeVCam");
            if (slimeVCamGo != null)
            {
                var slimeVCam = slimeVCamGo.GetComponent<CinemachineCamera>();
                if (slimeVCam != null) return slimeVCam.Lens;
            }

            var lens = LensSettings.Default;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = 40f;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 5000f;
            return lens;
        }
    }
}
