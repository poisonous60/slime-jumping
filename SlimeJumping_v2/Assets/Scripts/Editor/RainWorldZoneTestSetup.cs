using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Camera;

namespace SlimeJumping.EditorTools
{
    /// <summary>Builds a Rain World style camera zone (two sub-rooms, hard cut between them) into the active SampleScene.</summary>
    public static class RainWorldZoneTestSetup
    {
        // Anchor far from existing SampleScene props so the test doesn't overlap the slime spawn.
        private const float OriginX = 60f;
        private const float OriginY = 5f;

        [MenuItem("SlimeJumping/Camera/Setup Rain World Zone Test")]
        public static void SetupRainWorldZoneTest()
        {
            var brain = Object.FindAnyObjectByType<CinemachineBrain>();
            if (brain == null)
            {
                Debug.LogError("[RainWorldZoneTestSetup] No CinemachineBrain in the active scene. Open SampleScene first.");
                return;
            }

            // Keep the designer-nudged position on re-run, and destroy the old root so VCams don't accumulate.
            Vector3 spawnPos = new Vector3(OriginX, OriginY, 0f);
            var prior = GameObject.Find("RainWorldZoneTest");
            if (prior != null)
            {
                spawnPos = prior.transform.position;
                Undo.DestroyObjectImmediate(prior);
            }

            var root = new GameObject("RainWorldZoneTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Rain World Zone Test");
            root.transform.position = spawnPos;

            float roomW = 14f;
            float roomH = 12f;
            Vector2 leftCenter = new Vector2(-roomW / 2f, 0f);
            Vector2 rightCenter = new Vector2(roomW / 2f, 0f);
            Vector2 roomSize = new Vector2(roomW, roomH);

            var leftRoomGo = BuildSubRoom("SubRoom_Left", root.transform, leftCenter, roomSize, brain);
            var rightRoomGo = BuildSubRoom("SubRoom_Right", root.transform, rightCenter, roomSize, brain);

            // Zone trigger must span both rooms' union, else its OnTriggerExit fires while the
            // slime is still inside a room and the camera pops back to the global VCam.
            var zoneGo = new GameObject("RainWorldZone");
            zoneGo.transform.SetParent(root.transform, worldPositionStays: false);
            zoneGo.transform.localPosition = Vector3.zero;
            var zoneCol = zoneGo.AddComponent<BoxCollider2D>();
            zoneCol.isTrigger = true;
            zoneCol.size = new Vector2(roomW * 2f, roomH);

            var zone = zoneGo.AddComponent<RainWorldZone>();
            var soZone = new SerializedObject(zone);
            soZone.FindProperty("brain").objectReferenceValue = brain;
            var subRoomsProp = soZone.FindProperty("subRooms");
            subRoomsProp.arraySize = 2;
            subRoomsProp.GetArrayElementAtIndex(0).objectReferenceValue = leftRoomGo.GetComponent<RainWorldSubRoom>();
            subRoomsProp.GetArrayElementAtIndex(1).objectReferenceValue = rightRoomGo.GetComponent<RainWorldSubRoom>();
            soZone.ApplyModifiedProperties();

            float floorY = -roomH / 2f - 0.5f;
            BuildPlatform("Floor", root.transform, new Vector3(0f, floorY, 0f), new Vector3(roomW * 2f + 2f, 1f, 1f));

            float wallX = roomW + 0.5f;
            float wallH = roomH + 2f;
            BuildPlatform("WallLeft", root.transform, new Vector3(-wallX, 0f, 0f), new Vector3(1f, wallH, 1f));
            BuildPlatform("WallRight", root.transform, new Vector3(wallX, 0f, 0f), new Vector3(1f, wallH, 1f));
            BuildPlatform("Roof", root.transform, new Vector3(0f, roomH / 2f + 0.5f, 0f), new Vector3(roomW * 2f + 2f, 1f, 1f));

            // Divider forces a climb at the seam, so the hard cut happens at a readable moment.
            BuildPlatform("Divider", root.transform, new Vector3(0f, -roomH / 2f + 1.5f, 0f), new Vector3(0.6f, 3f, 1f));

            // Per-side floor tint: the framing is identical, so only color shows the room change.
            BuildPlatform("FloorTintLeft", root.transform,
                new Vector3(-roomW / 2f, floorY + 1.0f, 0f),
                new Vector3(roomW - 0.5f, 0.05f, 1f),
                new Color(0.45f, 0.55f, 0.85f));
            BuildPlatform("FloorTintRight", root.transform,
                new Vector3(roomW / 2f, floorY + 1.0f, 0f),
                new Vector3(roomW - 0.5f, 0.05f, 1f),
                new Color(0.85f, 0.55f, 0.45f));

            BuildPlatform("EntryRamp", root.transform,
                new Vector3(-wallX - 4f, floorY, 0f),
                new Vector3(7f, 1f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log($"[RainWorldZoneTestSetup] Created at ({OriginX}, {OriginY}). " +
                      "Walk slime in from the entry ramp on the left, then jump over the divider — " +
                      "the camera should hard-cut between sub-rooms and fade out when leaving the zone.");
        }

        private static GameObject BuildSubRoom(string name, Transform parent, Vector2 localCenter, Vector2 size, CinemachineBrain brain)
        {
            // VCam lives on a child, not the room GO, so its pivot can be offset without moving the bounds.
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = (Vector3)localCenter;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = size;

            var subRoom = go.AddComponent<RainWorldSubRoom>();

            // Mirrors SlimeVCam's composer / impulse-listener setup, just confined.
            var vcamGo = new GameObject(name + "_VCam");
            vcamGo.transform.SetParent(go.transform, worldPositionStays: false);
            vcamGo.transform.localPosition = new Vector3(0f, 0f, -12f);

            var vcam = vcamGo.AddComponent<CinemachineCamera>();
            vcam.Priority = 0;

            var lens = vcam.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = 38f;
            lens.NearClipPlane = 0.1f;
            lens.FarClipPlane = 5000f;
            vcam.Lens = lens;

            var composer = vcamGo.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = 12f;
            composer.TargetOffset = new Vector3(0f, 0.5f, 0f);
            // Damping > 0 required here (unlike the global SlimeVCam): the Confiner2D stops the
            // camera at the room edge and Damping=0 snaps the derivative to zero — visibly jerky.
            composer.Damping = new Vector3(0.5f, 0.4f, 0f);

            var confiner = vcamGo.AddComponent<CinemachineConfiner2D>();
            confiner.BoundingShape2D = col;

            vcamGo.AddComponent<CinemachineImpulseListener>();

            // Prefer the proxy: it carries SlimeVCam's low-pass + snap pipeline.
            var proxy = Object.FindAnyObjectByType<SlimeCameraTargetProxy>();
            if (proxy != null)
            {
                vcam.Target.TrackingTarget = proxy.transform;
            }
            else
            {
                var slime = Object.FindAnyObjectByType<SlimeJumping.Controller.SlimeController>();
                if (slime != null) vcam.Target.TrackingTarget = slime.transform;
                else Debug.LogWarning("[RainWorldZoneTestSetup] No slime found — assign Tracking Target on " + name + "_VCam manually.");
            }

            var soSub = new SerializedObject(subRoom);
            soSub.FindProperty("vcam").objectReferenceValue = vcam;
            soSub.ApplyModifiedProperties();

            return go;
        }

        private static GameObject BuildPlatform(string name, Transform parent, Vector3 worldPos, Vector3 scale)
        {
            return BuildPlatform(name, parent, worldPos, scale, new Color(0.5f, 0.5f, 0.5f));
        }

        private static GameObject BuildPlatform(string name, Transform parent, Vector3 worldPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = worldPos;
            go.transform.localScale = scale;
            go.layer = 7; // ground layer

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MakeMaterial(color);
            return go;
        }

        private static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
