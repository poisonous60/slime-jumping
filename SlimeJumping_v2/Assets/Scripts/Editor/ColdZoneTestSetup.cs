using UnityEditor;
using UnityEngine;
using SlimeJumping.Mechanisms;
using SlimeJumping.Visual;

namespace SlimeJumping.Editor
{
    /// <summary>
    /// One-click test rig for ColdZone + SlimeFrostController. Builds a
    /// trigger box at a fixed scene position the user can walk into to
    /// observe the freeze tint, frost coverage, and dual-color trail
    /// composition. Re-runnable: deletes the prior "ColdZoneTest" root
    /// before rebuilding so iteration doesn't accumulate junk.
    /// </summary>
    public static class ColdZoneTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup Cold Zone Test")]
        public static void Setup()
        {
            var existing = GameObject.Find("ColdZoneTest");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("ColdZoneTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Cold Zone Test");

            // Fixed test position. Move the root if it overlaps existing geometry.
            float cx = -8f;
            float cy = 4f;
            float w = 8f;
            float h = 6f;

            var zone = new GameObject("ColdZone_Main");
            zone.transform.SetParent(root.transform);
            zone.transform.localPosition = new Vector3(cx, cy, 0f);
            var box = zone.AddComponent<BoxCollider2D>();
            box.size = new Vector2(w, h);
            box.isTrigger = true;
            zone.AddComponent<ColdZone>();

            // Visual hint — translucent quad so the user can see where the
            // trigger sits in the scene view. EditorOnly tag keeps it out of builds.
            var hint = GameObject.CreatePrimitive(PrimitiveType.Quad);
            hint.name = "ZoneHint";
            hint.tag = "EditorOnly";
            hint.transform.SetParent(zone.transform, worldPositionStays: false);
            hint.transform.localScale = new Vector3(w, h, 1f);
            Object.DestroyImmediate(hint.GetComponent<Collider>());
            var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(0.5f, 0.85f, 1f, 0.18f) };
            hint.GetComponent<MeshRenderer>().sharedMaterial = mat;

            // Ensure a SlimeFrostController is attached to the scene's slime root.
            var slime = Object.FindObjectOfType<SlimeJumping.Controller.SlimeController>();
            if (slime != null)
            {
                var frost = slime.GetComponent<SlimeFrostController>();
                if (frost == null)
                {
                    frost = Undo.AddComponent<SlimeFrostController>(slime.gameObject);
                    var so = new SerializedObject(frost);
                    var visual = slime.GetComponentInChildren<SlimeVisual>();
                    if (visual != null)
                    {
                        so.FindProperty("slimeVisual").objectReferenceValue = visual;
                        if (visual.Body3d != null) so.FindProperty("body3d").objectReferenceValue = visual.Body3d;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log("[ColdZoneTest] Attached SlimeFrostController to slime root.", slime);
                }
            }
            else
            {
                Debug.LogWarning("[ColdZoneTest] No SlimeController found in scene — open MainScene first if you want the freeze visual to drive.");
            }

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            Debug.Log("[ColdZoneTest] ColdZone trigger built at (" + cx + ", " + cy + "). Walk the slime through the cyan box to observe freeze.", root);
        }
    }
}
