using UnityEngine;
using UnityEditor;
using SlimeJumping.Visual;

namespace SlimeJumping.Editor
{
    // Temporary setup: hangs a swaying Apple decoration off Branch_Bouncy_DecoCube.
    // Run via menu once; delete this file if you don't need it afterwards.
    //
    // Design note: the apple's stem is pinned to DecoCube via *transform parenting*, not a
    // HingeJoint2D. A joint between two Dynamic bodies (branch→cube is already dynamic→dynamic)
    // has visible Box2D compliance, which showed up as the apple jittering up and down. Parenting
    // the stem keeps it rigidly at DecoCube's origin; the body rotates via AppleSwayPendulum.
    public static class AppleDecorationSetup
    {
        const string AppleName = "Branch_Bouncy_Apple";

        [MenuItem("SlimeJumping/Decoration/Add Apple To Branch_Bouncy")]
        public static void AddAppleToBranchBouncy()
        {
            var decoCube = GameObject.Find("Branch_Bouncy_DecoCube");
            if (decoCube == null)
            {
                EditorUtility.DisplayDialog("Apple Setup",
                    "Couldn't find 'Branch_Bouncy_DecoCube' in the active scene.", "OK");
                return;
            }

            // Blow away any previous attempt so repeated runs are idempotent.
            var existing = GameObject.Find(AppleName);
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            // Pivot/stem — child of DecoCube so it tracks the cube's world position exactly.
            var apple = new GameObject(AppleName);
            Undo.RegisterCreatedObjectUndo(apple, "Create Apple");
            apple.transform.SetParent(decoCube.transform, false);
            apple.transform.localPosition = Vector3.zero;
            apple.transform.localRotation = Quaternion.identity;
            apple.transform.localScale = Vector3.one;

            apple.AddComponent<AppleSwayPendulum>();

            // Visual — sphere primitive, offset below the stem so it hangs. Swap the mesh later
            // for a real apple model; keep its origin at the stem point (or tweak localPosition).
            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            Undo.RegisterCreatedObjectUndo(visual, "Create Apple Visual");
            Object.DestroyImmediate(visual.GetComponent<SphereCollider>());
            visual.transform.SetParent(apple.transform, false);
            visual.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
            visual.GetComponent<MeshRenderer>().sharedMaterial =
                MakeMaterial(new Color(0.85f, 0.15f, 0.15f));

            Selection.activeGameObject = apple;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log("Apple decoration rebuilt. Stem is parented rigidly to DecoCube; apple body " +
                      "swings via AppleSwayPendulum. Tune 'length'/'damping' on Branch_Bouncy_Apple " +
                      "to taste. Replace child 'Visual' with your real apple mesh when ready.");
        }

        static Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            return mat;
        }
    }
}
