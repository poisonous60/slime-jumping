using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using SlimeJumping.Camera;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// Adds a Rain World style camera zone into the active SampleScene.
    /// Layout: outer zone trigger covering two sub-rooms side-by-side, with floor /
    /// walls / ceiling so the slime can navigate between them. Each sub-room owns
    /// a CinemachineCamera + Confiner2D pointing at its bounds, so when the slime
    /// crosses from the left room into the right room the camera hard-cuts to the
    /// new framing. Walking back out of the zone fades to the global SlimeVCam.
    /// </summary>
    public static class RainWorldZoneTestSetup
    {
        // Anchor far from the existing SampleScene props so the test doesn't overlap
        // with the slime spawn or other test setups.
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

            // Preserve a designer-nudged position when re-running the menu, and
            // tear down the old instance so we don't accumulate duplicate VCams.
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

            // Two sub-rooms side-by-side, 14 wide x 12 tall each, sharing a vertical edge at x=0 (local).
            float roomW = 14f;
            float roomH = 12f;
            Vector2 leftCenter = new Vector2(-roomW / 2f, 0f);
            Vector2 rightCenter = new Vector2(roomW / 2f, 0f);
            Vector2 roomSize = new Vector2(roomW, roomH);

            var leftRoomGo = BuildSubRoom("SubRoom_Left", root.transform, leftCenter, roomSize, brain);
            var rightRoomGo = BuildSubRoom("SubRoom_Right", root.transform, rightCenter, roomSize, brain);

            // Outer zone trigger: covers both rooms with a small margin so OnTriggerEnter
            // fires before OnTriggerExit on the world boundary.
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

            // Geometry: floor under both rooms, walls on the outer edges + a roof
            // so the slime can climb around but can't fall out of the zone.
            // Centers are world-space because BuildPlatform parents under root and
            // we want the user to be able to reposition the whole RainWorldZoneTest
            // by moving the root.
            float floorY = -roomH / 2f - 0.5f;
            BuildPlatform("Floor", root.transform, new Vector3(0f, floorY, 0f), new Vector3(roomW * 2f + 2f, 1f, 1f));

            float wallX = roomW + 0.5f;
            float wallH = roomH + 2f;
            BuildPlatform("WallLeft", root.transform, new Vector3(-wallX, 0f, 0f), new Vector3(1f, wallH, 1f));
            BuildPlatform("WallRight", root.transform, new Vector3(wallX, 0f, 0f), new Vector3(1f, wallH, 1f));
            BuildPlatform("Roof", root.transform, new Vector3(0f, roomH / 2f + 0.5f, 0f), new Vector3(roomW * 2f + 2f, 1f, 1f));

            // A short divider wall between the two rooms so the slime has to climb
            // it to cross the seam — that gives a clean moment to see the hard cut.
            BuildPlatform("Divider", root.transform, new Vector3(0f, -roomH / 2f + 1.5f, 0f), new Vector3(0.6f, 3f, 1f));

            // Color the floor differently per side so the visual room change is obvious
            // even with identical camera framing.
            BuildPlatform("FloorTintLeft", root.transform,
                new Vector3(-roomW / 2f, floorY + 1.0f, 0f),
                new Vector3(roomW - 0.5f, 0.05f, 1f),
                new Color(0.45f, 0.55f, 0.85f));
            BuildPlatform("FloorTintRight", root.transform,
                new Vector3(roomW / 2f, floorY + 1.0f, 0f),
                new Vector3(roomW - 0.5f, 0.05f, 1f),
                new Color(0.85f, 0.55f, 0.45f));

            // Ramp from outside the zone so the slime can walk in. Placed just left
            // of the left wall, sloping down from the world ground.
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
            // The sub-room GO holds the BoxCollider2D bounds. The CinemachineCamera
            // is a child with its own transform so designers can offset the camera
            // pivot independently of the room bounds (e.g. composer target offset).
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = (Vector3)localCenter;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = size;

            var subRoom = go.AddComponent<RainWorldSubRoom>();

            // CinemachineCamera child. Use the same composer / impulse-listener setup
            // as the existing SlimeVCam so behavior matches the global camera (just
            // confined and at higher priority while active).
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
            // Sub-room VCams need damping that the global SlimeVCam doesn't:
            // the Confiner2D clamps the camera to the room box, so when the slime
            // moves toward an edge the camera hits the wall and "sticks" while the
            // slime keeps going. With Damping=0 the derivative snaps to zero on
            // contact (jerky). 0.4~0.6 eases the camera into the confiner edge
            // and also soaks up slime per-frame normal jitter inside the room.
            composer.Damping = new Vector3(0.5f, 0.4f, 0f);

            var confiner = vcamGo.AddComponent<CinemachineConfiner2D>();
            confiner.BoundingShape2D = col;

            vcamGo.AddComponent<CinemachineImpulseListener>();

            // Tracking target: reuse the global SlimeCameraTargetProxy if present,
            // else fall back to the slime root. Either way the sub-room VCam follows
            // the same low-pass + snap pipeline as SlimeVCam.
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

            // Wire the sub-room's vcam reference.
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
