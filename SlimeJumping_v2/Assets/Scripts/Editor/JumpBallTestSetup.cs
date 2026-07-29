using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Editor
{
    public static class JumpBallTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Jump Ball Test")]
        public static void SetupJumpBallTest()
        {
            var root = new GameObject("JumpBallTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Jump Ball Test");

            float baseX = 0f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(40f, 1f, 1f));

            CreateJumpBall("JumpBall_FixedOnly_Persistent", root.transform,
                new Vector3(baseX - 12f, baseY + 2.5f, 0f),
                radius: 0.6f,
                fixedJumpSpeed: 20f,
                incomingSpeedPercent: 0.0f,
                bonusSpeed: 0f,
                bobAmplitude: 0.25f,
                bobFrequency: 0.6f,
                tint: new Color(1f, 0.85f, 0.3f),
                consumable: false,
                respawnDelay: 2.5f);

            CreateJumpBall("JumpBall_Bouncy_FastRespawn", root.transform,
                new Vector3(baseX - 4f, baseY + 2.5f, 0f),
                radius: 0.6f,
                fixedJumpSpeed: 16f,
                incomingSpeedPercent: 0.7f,
                bonusSpeed: 5f,
                bobAmplitude: 0.3f,
                bobFrequency: 0.7f,
                tint: new Color(0.9f, 0.45f, 0.25f),
                consumable: true,
                respawnDelay: 1.0f);

            CreateJumpBall("JumpBall_HighAmp_SlowRespawn", root.transform,
                new Vector3(baseX + 4f, baseY + 2.5f, 0f),
                radius: 0.6f,
                fixedJumpSpeed: 14f,
                incomingSpeedPercent: 1.2f,
                bonusSpeed: 8f,
                bobAmplitude: 0.35f,
                bobFrequency: 0.9f,
                tint: new Color(0.3f, 0.9f, 0.5f),
                consumable: true,
                respawnDelay: 4.0f);

            CreateJumpBall("JumpBall_Stack_Low", root.transform,
                new Vector3(baseX + 12f, baseY + 2.5f, 0f),
                radius: 0.6f,
                fixedJumpSpeed: 18f,
                incomingSpeedPercent: 0.8f,
                bonusSpeed: 3f,
                bobAmplitude: 0.2f,
                bobFrequency: 1.0f,
                tint: new Color(0.4f, 0.7f, 1f),
                consumable: true,
                respawnDelay: 2.5f);

            CreateJumpBall("JumpBall_Stack_High", root.transform,
                new Vector3(baseX + 12f, baseY + 8f, 0f),
                radius: 0.6f,
                fixedJumpSpeed: 18f,
                incomingSpeedPercent: 0.8f,
                bonusSpeed: 3f,
                bobAmplitude: 0.2f,
                bobFrequency: 1.0f,
                tint: new Color(0.4f, 0.7f, 1f),
                consumable: true,
                respawnDelay: 2.5f);

            CreatePlatform("Landing_Mid", root.transform,
                new Vector3(baseX - 8f, baseY + 6f, 0f),
                new Vector3(3f, 0.5f, 1f));

            CreatePlatform("Landing_High", root.transform,
                new Vector3(baseX + 6f, baseY + 12f, 0f),
                new Vector3(3f, 0.5f, 1f));

            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 20.5f, baseY + 6f, 0f),
                new Vector3(1f, 13f, 1f));

            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 20.5f, baseY + 6f, 0f),
                new Vector3(1f, 13f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("Jump Ball Test setup complete!\n" +
                "- FixedOnly_Persistent: non-consumable (bounces forever), pure fixed speed.\n" +
                "- Bouncy_FastRespawn: consumable, 1s respawn, moderate retention.\n" +
                "- HighAmp_SlowRespawn: consumable, 4s respawn, amplifier formula.\n" +
                "- Stack_Low + Stack_High: consumable chain — hit low, bounce onto high, both respawn.\n" +
                "Assign Consume/Respawn SFX on each JumpBall to hear audio cues.");
        }

        static GameObject CreateJumpBall(string name, Transform parent, Vector3 pos,
            float radius, float fixedJumpSpeed, float incomingSpeedPercent, float bonusSpeed,
            float bobAmplitude, float bobFrequency, Color tint,
            bool consumable, float respawnDelay)
        {
            var rootGo = new GameObject(name);
            rootGo.transform.parent = parent;
            rootGo.transform.position = pos;

            var col = rootGo.AddComponent<CircleCollider2D>();
            col.isTrigger = true;
            col.radius = radius;

            var visualGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visualGo.name = "Visual";
            visualGo.transform.parent = rootGo.transform;
            visualGo.transform.localPosition = Vector3.zero;
            visualGo.transform.localScale = Vector3.one * (radius * 2f);
            Object.DestroyImmediate(visualGo.GetComponent<SphereCollider>());
            visualGo.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(tint);

            var ball = rootGo.AddComponent<JumpBall>();
            var so = new SerializedObject(ball);
            so.FindProperty("fixedJumpSpeed").floatValue = fixedJumpSpeed;
            so.FindProperty("incomingSpeedPercent").floatValue = incomingSpeedPercent;
            so.FindProperty("bonusSpeed").floatValue = bonusSpeed;
            so.FindProperty("cooldown").floatValue = 0.3f;
            so.FindProperty("preserveHorizontal").boolValue = true;
            so.FindProperty("visual").objectReferenceValue = visualGo.transform;
            so.FindProperty("bobAmplitude").floatValue = bobAmplitude;
            so.FindProperty("bobFrequency").floatValue = bobFrequency;
            so.FindProperty("randomizePhase").boolValue = true;
            so.FindProperty("consumable").boolValue = consumable;
            so.FindProperty("respawnDelay").floatValue = respawnDelay;
            so.FindProperty("consumeDuration").floatValue = 0.25f;
            so.FindProperty("respawnDuration").floatValue = 0.3f;
            so.FindProperty("sfxVolume").floatValue = 1f;
            so.ApplyModifiedProperties();

            return rootGo;
        }

        static GameObject CreatePlatform(string name, Transform parent, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.parent = parent;
            go.transform.position = pos;
            go.transform.localScale = scale;
            go.layer = 7;

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
