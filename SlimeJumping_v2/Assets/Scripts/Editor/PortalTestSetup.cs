using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class PortalTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Portal Fling Test")]
        public static void SetupPortalFlingTest()
        {
            var root = new GameObject("PortalFlingTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Portal Fling Test");

            float shaftX = -12f;
            float shaftWidth = 4f;
            float wallHeight = 14f;
            float wallY = 6f;

            var leftWall = CreatePlatform("ShaftLeftWall", root.transform,
                new Vector3(shaftX - shaftWidth / 2f - 0.5f, wallY, 0f),
                new Vector3(1f, wallHeight, 1f));

            var rightWall = CreatePlatform("ShaftRightWall", root.transform,
                new Vector3(shaftX + shaftWidth / 2f + 0.5f, wallY, 0f),
                new Vector3(1f, wallHeight, 1f));

            var floor = CreatePlatform("ShaftFloor", root.transform,
                new Vector3(shaftX, wallY - wallHeight / 2f - 0.5f, 0f),
                new Vector3(shaftWidth + 2f, 1f, 1f));

            float portalY_top = wallY + wallHeight / 2f - 2f;
            float portalY_bottom = wallY - wallHeight / 2f + 2f;

            // Portal OutNormal = transform.right, so the Z rotation aims the exit.
            var portalBlue = new GameObject("Portal_Blue");
            portalBlue.transform.parent = root.transform;
            portalBlue.transform.position = new Vector3(shaftX - shaftWidth / 2f + 0.1f, portalY_top, 0f);
            portalBlue.transform.rotation = Quaternion.Euler(0f, 0f, 0f); // right = +X (into shaft)
            var blueCol = portalBlue.AddComponent<BoxCollider2D>();
            blueCol.isTrigger = true;
            blueCol.size = new Vector2(0.3f, 1.5f);
            var bluePortal = portalBlue.AddComponent<Portal>();

            var blueVis = GameObject.CreatePrimitive(PrimitiveType.Quad);
            blueVis.name = "BlueVisual";
            blueVis.transform.parent = portalBlue.transform;
            blueVis.transform.localPosition = Vector3.zero;
            blueVis.transform.localScale = new Vector3(0.2f, 1.5f, 1f);
            Object.DestroyImmediate(blueVis.GetComponent<MeshCollider>());
            blueVis.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(0.2f, 0.4f, 1f));

            var portalOrange = new GameObject("Portal_Orange");
            portalOrange.transform.parent = root.transform;
            portalOrange.transform.position = new Vector3(shaftX + shaftWidth / 2f - 0.1f, portalY_bottom, 0f);
            portalOrange.transform.rotation = Quaternion.Euler(0f, 0f, 180f); // right = -X (into shaft)
            var orangeCol = portalOrange.AddComponent<BoxCollider2D>();
            orangeCol.isTrigger = true;
            orangeCol.size = new Vector2(0.3f, 1.5f);
            var orangePortal = portalOrange.AddComponent<Portal>();

            var orangeVis = GameObject.CreatePrimitive(PrimitiveType.Quad);
            orangeVis.name = "OrangeVisual";
            orangeVis.transform.parent = portalOrange.transform;
            orangeVis.transform.localPosition = Vector3.zero;
            orangeVis.transform.localScale = new Vector3(0.2f, 1.5f, 1f);
            Object.DestroyImmediate(orangeVis.GetComponent<MeshCollider>());
            orangeVis.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1f, 0.5f, 0.1f));

            var soBlue = new SerializedObject(bluePortal);
            soBlue.FindProperty("exit").objectReferenceValue = orangePortal;
            soBlue.ApplyModifiedProperties();

            var soOrange = new SerializedObject(orangePortal);
            soOrange.FindProperty("exit").objectReferenceValue = bluePortal;
            soOrange.ApplyModifiedProperties();

            float buttonX = shaftX;
            float buttonY = wallY + wallHeight / 2f + 2f;

            var buttonPlatform = new GameObject("ButtonPlatform");
            buttonPlatform.transform.parent = root.transform;
            buttonPlatform.transform.position = new Vector3(buttonX, buttonY, 0f);
            buttonPlatform.layer = 7; // ground layer

            var buttonRb = buttonPlatform.AddComponent<Rigidbody2D>();
            buttonRb.bodyType = RigidbodyType2D.Kinematic;
            var buttonCol2 = buttonPlatform.AddComponent<BoxCollider2D>();
            buttonCol2.size = new Vector2(3f, 0.5f);

            var buttonSr = buttonPlatform.AddComponent<SpriteRenderer>();
            buttonSr.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            buttonSr.drawMode = SpriteDrawMode.Sliced;
            buttonSr.size = new Vector2(3f, 0.5f);
            buttonSr.color = new Color(0.6f, 0.3f, 0.3f);

            var weightPlatform = buttonPlatform.AddComponent<WeightDescendPlatform>();

            var sensorGo = new GameObject("RiderSensor");
            sensorGo.transform.parent = buttonPlatform.transform;
            sensorGo.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            var sensorCol = sensorGo.AddComponent<BoxCollider2D>();
            sensorCol.isTrigger = true;
            sensorCol.size = new Vector2(3f, 0.3f);
            var sensor = sensorGo.AddComponent<PlatformRiderSensor>();

            var soWeight = new SerializedObject(weightPlatform);
            soWeight.FindProperty("sensor").objectReferenceValue = sensor;
            soWeight.ApplyModifiedProperties();

            var swapper = new GameObject("PortalSwapper");
            swapper.transform.parent = root.transform;
            swapper.transform.position = Vector3.zero;
            var portalSwapper = swapper.AddComponent<PortalSwapper>();

            var slotA = new GameObject("SlotA_BottomRight");
            slotA.transform.parent = swapper.transform;
            slotA.transform.position = portalOrange.transform.position;
            slotA.transform.rotation = portalOrange.transform.rotation;

            var slotB = new GameObject("SlotB_FloorFling");
            slotB.transform.parent = swapper.transform;
            slotB.transform.position = new Vector3(shaftX, wallY - wallHeight / 2f + 1f, 0f);
            slotB.transform.rotation = Quaternion.Euler(0f, 0f, 90f); // right = +Y (up)

            var soSwapper = new SerializedObject(portalSwapper);
            soSwapper.FindProperty("target").objectReferenceValue = orangePortal;
            soSwapper.FindProperty("slotA").objectReferenceValue = slotA.transform;
            soSwapper.FindProperty("slotB").objectReferenceValue = slotB.transform;
            soSwapper.ApplyModifiedProperties();

            UnityEditor.Events.UnityEventTools.AddPersistentListener(
                weightPlatform.onActivate,
                new UnityEngine.Events.UnityAction(portalSwapper.Toggle));

            CreatePlatform("Ramp", root.transform,
                new Vector3(shaftX + 4f, buttonY - 1f, 0f),
                new Vector3(6f, 0.5f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Portal Fling Test setup complete! Drop slime on the ramp or above the shaft to test.");
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7; // ground layer

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.5f, 0.5f, 0.5f));

            return go;
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
