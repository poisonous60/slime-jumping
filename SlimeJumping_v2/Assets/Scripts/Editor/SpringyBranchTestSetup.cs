using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;
using SlimeJumping.Visual;

namespace SlimeJumping.Editor
{
    public static class SpringyBranchTestSetup
    {
        [MenuItem("SlimeJumping/Platform/Setup SpringyBranch Test")]
        public static void SetupSpringyBranchTest()
        {
            var root = new GameObject("SpringyBranchTest");
            Undo.RegisterCreatedObjectUndo(root, "Create SpringyBranch Test");

            float baseX = 0f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 5f, 0f),
                new Vector3(60f, 1f, 1f));

            CreatePlatform("LaunchPad_Left", root.transform,
                new Vector3(baseX - 24f, baseY + 2f, 0f),
                new Vector3(3f, 1f, 1f));

            CreatePlatform("LaunchPad_Right", root.transform,
                new Vector3(baseX + 24f, baseY + 2f, 0f),
                new Vector3(3f, 1f, 1f));

            CreateSpringyBranch("Branch_Bouncy", root.transform,
                pivotWorldPos: new Vector3(baseX - 15f, baseY + 3f, 0f),
                tint: new Color(0.9f, 0.5f, 0.3f),
                branchLength: 4f,
                naturalFreqHz: 1.2f,
                dampingRatio: 0.15f,
                riderPumpGain: 1.5f,
                chargePumpScale: 1.0f,
                passiveDecayRate: 2.0f,
                riderDecayRate: 0.5f);

            CreateSpringyBranch("Branch_Heavy", root.transform,
                pivotWorldPos: new Vector3(baseX - 5f, baseY + 3f, 0f),
                tint: new Color(0.4f, 0.3f, 0.2f),
                branchLength: 4f,
                naturalFreqHz: 0.6f,
                dampingRatio: 0.25f,
                riderPumpGain: 0.7f,
                chargePumpScale: 0.5f,
                passiveDecayRate: 0.2f,
                riderDecayRate: 2.0f);

            CreateSpringyBranch("Branch_PureTiming", root.transform,
                pivotWorldPos: new Vector3(baseX + 5f, baseY + 3f, 0f),
                tint: new Color(0.3f, 0.7f, 0.9f),
                branchLength: 4f,
                naturalFreqHz: 1.0f,
                dampingRatio: 0.2f,
                riderPumpGain: 1.0f,
                chargePumpScale: 0.0f,
                passiveDecayRate: 0.5f,
                riderDecayRate: 0.5f);

            CreateSpringyBranch("Branch_Rhythm", root.transform,
                pivotWorldPos: new Vector3(baseX + 15f, baseY + 3f, 0f),
                tint: new Color(0.6f, 0.3f, 0.8f),
                branchLength: 4f,
                naturalFreqHz: 1.0f,
                dampingRatio: 0.2f,
                riderPumpGain: 1.2f,
                chargePumpScale: 1.5f,
                passiveDecayRate: 0.3f,
                riderDecayRate: 0.3f);

            // Tuned to peg maxTipSpeed quickly — reference for the launch-boost ceiling.
            CreateSpringyBranch("Branch_BoostGauge", root.transform,
                pivotWorldPos: new Vector3(baseX + 22f, baseY + 3f, 0f),
                tint: new Color(1.0f, 0.85f, 0.2f),
                branchLength: 4f,
                naturalFreqHz: 1.5f,
                dampingRatio: 0.1f,
                riderPumpGain: 2.0f,
                chargePumpScale: 1.5f,
                passiveDecayRate: 0.2f,
                riderDecayRate: 0.2f);

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log("SpringyBranch Test setup complete!\n" +
                "- Branch_Bouncy (orange): pumps fast, dies fast when empty\n" +
                "- Branch_Heavy (brown): hard to pump, rider weight kills bounce\n" +
                "- Branch_PureTiming (blue): body shifts during charge do nothing\n" +
                "- Branch_Rhythm (purple): charging-while-pumping rewarded\n" +
                "- Branch_BoostGauge (yellow): tuned to peg maxTipSpeed — reference for launch-boost ceiling\n" +
                "Slingshot from LaunchPad_Left/Right onto branches. Time launches to catch the upswing.");
        }

        static GameObject CreateSpringyBranch(string name, Transform parent, Vector3 pivotWorldPos,
            Color tint, float branchLength,
            float naturalFreqHz, float dampingRatio,
            float riderPumpGain, float chargePumpScale,
            float passiveDecayRate, float riderDecayRate)
        {
            var pivot = new GameObject(name);
            pivot.transform.parent = parent;
            pivot.transform.position = pivotWorldPos;

            var rb = pivot.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var motor = pivot.AddComponent<SpringyBranchPlatform>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "BranchBody";
            body.transform.SetParent(pivot.transform, false);
            body.transform.localPosition = new Vector3(branchLength * 0.5f, 0f, 0f);
            body.transform.localScale = new Vector3(branchLength, 0.3f, 1f);
            body.layer = 7;

            Object.DestroyImmediate(body.GetComponent<BoxCollider>());
            var col2d = body.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            body.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(tint);

            // Bones hang off the unscaled pivot, not the non-uniformly scaled body — else steps aren't world units and child art shears.
            int boneCount = 5;
            float segmentWorld = branchLength / boneCount;
            Transform[] bones = new Transform[boneCount];
            Transform boneParent = pivot.transform;
            for (int i = 0; i < boneCount; i++)
            {
                var bone = new GameObject($"Bone_{i}");
                bone.transform.SetParent(boneParent, false);
                bone.transform.localPosition = (i == 0)
                    ? Vector3.zero
                    : new Vector3(segmentWorld, 0f, 0f);
                bones[i] = bone.transform;
                boneParent = bone.transform;
            }

            var visual = pivot.AddComponent<SpringyBranchVisual>();
            var visualSo = new SerializedObject(visual);
            visualSo.FindProperty("motor").objectReferenceValue = motor;
            var bonesProp = visualSo.FindProperty("bones");
            bonesProp.arraySize = bones.Length;
            for (int i = 0; i < bones.Length; i++)
                bonesProp.GetArrayElementAtIndex(i).objectReferenceValue = bones[i];
            visualSo.ApplyModifiedProperties();

            var so = new SerializedObject(motor);
            so.FindProperty("branchLength").floatValue = branchLength;
            so.FindProperty("naturalFreqHz").floatValue = naturalFreqHz;
            so.FindProperty("dampingRatio").floatValue = dampingRatio;
            so.FindProperty("riderPumpGain").floatValue = riderPumpGain;
            so.FindProperty("chargePumpScale").floatValue = chargePumpScale;
            so.FindProperty("passiveDecayRate").floatValue = passiveDecayRate;
            so.FindProperty("riderDecayRate").floatValue = riderDecayRate;
            so.ApplyModifiedProperties();

            return pivot;
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
