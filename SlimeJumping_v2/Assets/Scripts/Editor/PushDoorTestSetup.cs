using UnityEngine;
using UnityEditor;
using SlimeJumping.Platforms;

namespace SlimeJumping.Editor
{
    public static class PushDoorTestSetup
    {
        [MenuItem("SlimeJumping/Mechanism/Setup PushDoor Test")]
        public static void Setup()
        {
            SetupInternal("PushDoorTest", useV2: false);
        }

        [MenuItem("SlimeJumping/Mechanism/Setup PushDoorV2 Test")]
        public static void SetupV2()
        {
            SetupInternal("PushDoorV2Test", useV2: true);
        }

        [MenuItem("SlimeJumping/Mechanism/Convert Selection to PushDoorV2")]
        public static void ConvertSelection()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("Select a root GameObject that contains Frame/Leaf/Hinge children.");
                return;
            }
            ConvertToPushDoorV2(target);
        }

        // Wraps the selected door instance in an empty parent with the same world
        // transform. The door's local rotation resets to (0,0,0) so the BoxCollider2D
        // shape lives axis-aligned (2D physics only honors Z rotation; Y/X rotations
        // collapse the collider footprint in the X-Y play plane).
        [MenuItem("SlimeJumping/Mechanism/Wrap Selection in Axis-Aligned Parent")]
        public static void WrapInAxisAlignedParent()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("Select the door GameObject to wrap.");
                return;
            }
            if (target.transform.parent != null && target.transform.parent.gameObject.name.EndsWith("_Wrapper"))
            {
                Debug.LogWarning("Already wrapped. Skip.");
                return;
            }

            var wrapper = new GameObject(target.name + "_Wrapper");
            Undo.RegisterCreatedObjectUndo(wrapper, "Wrap Door");

            // Wrapper inherits target's transform (so visual placement stays put)
            wrapper.transform.SetParent(target.transform.parent, worldPositionStays: false);
            wrapper.transform.localPosition = target.transform.localPosition;
            wrapper.transform.localRotation = target.transform.localRotation;
            wrapper.transform.localScale = target.transform.localScale;

            // Re-parent target under wrapper, keeping world transform
            Undo.SetTransformParent(target.transform, wrapper.transform, "Reparent into wrapper");

            // Reset target's local transform: identity rotation, zero offset.
            // Wrapper holds the original placement; target is now axis-aligned.
            target.transform.localPosition = Vector3.zero;
            target.transform.localRotation = Quaternion.identity;
            target.transform.localScale = Vector3.one;

            Selection.activeGameObject = target;
            Debug.Log($"Wrapped '{target.name}' under '{wrapper.name}'. " +
                $"Door is now axis-aligned for 2D physics. Apply scene placement (rotation/position) on the wrapper.");
        }

        // For 3D door asset packs (frame+leaf+handle as nested mesh children) where
        // child names don't match the Leaf/Hinge convention. Detects the leaf by
        // picking the smaller-bounds MeshRenderer child (frame is usually larger),
        // computes hinge as one edge of the leaf bounds, and wires PushDoorV2.
        [MenuItem("SlimeJumping/Mechanism/Convert 3D Door Prefab to PushDoorV2")]
        public static void Convert3DDoorPrefab()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("Select a 3D door prefab root (e.g. Door2_C1).");
                return;
            }
            Convert3DDoorToPushDoorV2(target);
        }

        static void Convert3DDoorToPushDoorV2(GameObject root)
        {
            Undo.RegisterFullObjectHierarchyUndo(root, "Convert 3D Door to PushDoorV2");

            // 0. Warn about non-Z rotation. BoxCollider2D's shape lives in the GO's
            //    local X-Y; rotation around X or Y collapses the collider footprint
            //    in world X-Y plane (2D physics ignores depth). Use the
            //    'Wrap Selection in Axis-Aligned Parent' menu before running this if
            //    the door inherits non-Z rotation from its scene/prefab placement.
            Vector3 worldEuler = root.transform.eulerAngles;
            float ex = Mathf.DeltaAngle(0f, worldEuler.x);
            float ey = Mathf.DeltaAngle(0f, worldEuler.y);
            if (Mathf.Abs(ex) > 0.5f || Mathf.Abs(ey) > 0.5f)
            {
                Debug.LogWarning($"[PushDoorV2 converter] '{root.name}' has non-Z world rotation " +
                    $"(X={ex:F1}°, Y={ey:F1}°). 2D collider will be skewed in world space and may " +
                    $"miss the slime. Run 'Wrap Selection in Axis-Aligned Parent' first, then " +
                    $"convert the inner GameObject.");
            }

            // 1. Remove ALL 3D colliders first. Unity refuses to add Rigidbody2D to a
            //    GameObject that has any 3D collider (MeshCollider/BoxCollider/etc.),
            //    even on the same GO. We delete them on root and disable them on
            //    children so 2.5D physics is the sole source of truth.
            var rootMC = root.GetComponent<MeshCollider>();
            if (rootMC != null) Undo.DestroyObjectImmediate(rootMC);
            foreach (var c3 in root.GetComponents<Collider>())
                Undo.DestroyObjectImmediate(c3);

            foreach (var c3 in root.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (c3.gameObject == root) continue;
                c3.enabled = false;
            }

            // 2. Find the leaf: largest child MeshRenderer (frame is at root, skipped;
            //    handle is small, deprioritized). Fall back to "most offset from root".
            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            Transform leafTransform = null;
            float bestScore = -1f;
            foreach (var mr in meshRenderers)
            {
                if (mr.transform == root.transform) continue; // skip frame on root
                // Use mesh's local bounds (independent of rotation/world placement).
                var mf = mr.GetComponent<MeshFilter>();
                Vector3 size = (mf != null && mf.sharedMesh != null)
                    ? mf.sharedMesh.bounds.size
                    : mr.bounds.size;
                float score = size.magnitude;
                if (score > bestScore)
                {
                    bestScore = score;
                    leafTransform = mr.transform;
                }
            }

            if (leafTransform == null)
            {
                Debug.LogError($"Convert failed: no child MeshRenderer found under {root.name}. " +
                    $"This menu expects a 3D door prefab where the leaf is a child mesh, distinct from the frame on root.");
                return;
            }

            // 3. Read leaf mesh bounds (mesh-local space)
            Vector3 leafBoundsCenter = Vector3.zero;
            Vector3 leafBoundsSize = Vector3.one;
            var leafMF = leafTransform.GetComponent<MeshFilter>();
            if (leafMF != null && leafMF.sharedMesh != null)
            {
                leafBoundsCenter = leafMF.sharedMesh.bounds.center;
                leafBoundsSize = leafMF.sharedMesh.bounds.size;
            }
            else
            {
                var leafMR = leafTransform.GetComponent<MeshRenderer>();
                if (leafMR != null) leafBoundsSize = leafMR.bounds.size;
            }

            // 4. Hinge = leaf pivot. (FBX door assets typically place origin at hinge edge,
            //    so bounds.center is offset from origin in the leaf's "forward" direction.)
            Vector3 hingeLocal = leafTransform.localPosition;

            // 5. Detect leafForwardLocal: direction from pivot to mesh center.
            //    Pick the dominant axis (X or Z, since rotation is around Y by default).
            //    If pivot is at edge: bounds.center is offset (e.g. (0.45, 0, 0)) → +X.
            //    If pivot is at center: bounds.center is near zero → fall back to +X.
            Vector3 leafForwardLocal;
            if (leafBoundsCenter.sqrMagnitude > 0.001f)
            {
                // Use the dominant horizontal axis (zero out Y so leaf is in horizontal plane)
                Vector3 horiz = new Vector3(leafBoundsCenter.x, 0f, leafBoundsCenter.z);
                if (horiz.sqrMagnitude > 0.0001f)
                    leafForwardLocal = horiz.normalized;
                else
                    leafForwardLocal = leafBoundsCenter.normalized;
            }
            else
            {
                leafForwardLocal = Vector3.right; // fallback
            }

            // 6. doorDepth: distance from pivot to far end of leaf along leafForwardLocal.
            //    If pivot is at edge: depth ≈ 2 * |bounds.center along leafForward|.
            //    If pivot is at center: depth ≈ projected size along leafForward.
            float depthFromCenter = Mathf.Abs(Vector3.Dot(leafBoundsCenter, leafForwardLocal));
            float depthFromSize = Mathf.Abs(Vector3.Dot(
                new Vector3(leafBoundsSize.x, 0f, leafBoundsSize.z),
                new Vector3(Mathf.Abs(leafForwardLocal.x), 0f, Mathf.Abs(leafForwardLocal.z))));
            float doorDepth = depthFromCenter > 0.01f ? depthFromCenter * 2f : depthFromSize;
            if (doorDepth < 0.01f) doorDepth = 1f;

            // 7. Now safe to add 2D physics components
            var rb = root.GetComponent<Rigidbody2D>();
            if (rb == null) rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = root.GetComponent<BoxCollider2D>();
            if (col == null) col = root.AddComponent<BoxCollider2D>();

            // 8. BoxCollider2D placed at leaf's WORLD center.
            Vector3 leafCenterWorld = leafTransform.TransformPoint(leafBoundsCenter);
            Vector2 colliderOffsetXY = new Vector2(
                leafCenterWorld.x - root.transform.position.x,
                leafCenterWorld.y - root.transform.position.y);
            col.offset = colliderOffsetXY;
            col.size = new Vector2(
                Mathf.Max(Mathf.Abs(leafBoundsSize.x), 0.1f),
                Mathf.Max(Mathf.Abs(leafBoundsSize.y), 0.1f));

            var existingV1 = root.GetComponent<PushDoor>();
            if (existingV1 != null) Undo.DestroyObjectImmediate(existingV1);

            var v2 = root.GetComponent<PushDoorV2>();
            if (v2 == null) v2 = root.AddComponent<PushDoorV2>();

            // 9. Wire PushDoorV2 fields. Default rotationAxis = Z (out-of-screen) for
            //    2.5D doors so the leaf swings IN the X-Y play plane (slime can push it
            //    via X velocity). User can change to Y for salloon-style depth swing.
            var so = new SerializedObject(v2);
            so.FindProperty("visualTransform").objectReferenceValue = leafTransform;
            so.FindProperty("hingeOffset").vector3Value = hingeLocal;
            so.FindProperty("rotationAxis").vector3Value = Vector3.forward; // (0,0,1)
            so.FindProperty("leafForwardLocal").vector3Value = leafForwardLocal;
            so.FindProperty("doorDepthOverride").floatValue = doorDepth;
            so.ApplyModifiedProperties();

            root.layer = 7;
            EditorUtility.SetDirty(root);

            Debug.Log($"Converted 3D door '{root.name}' to PushDoorV2.\n" +
                $"  leaf = {leafTransform.name}\n" +
                $"  bounds: center={leafBoundsCenter}, size={leafBoundsSize}\n" +
                $"  hingeOffset = {hingeLocal}\n" +
                $"  leafForwardLocal = {leafForwardLocal} (auto-detected from bounds.center)\n" +
                $"  doorDepthOverride = {doorDepth:F3}\n" +
                $"  rotationAxis = (0,0,1) [Z, for X-Y plane swing — change if needed]\n" +
                $"  BoxCollider2D: size={col.size}, offset={col.offset}\n" +
                $"  Removed root MeshCollider, disabled child 3D colliders.");
        }

        // V3 (angular model) converter — for FBX doors that swing by changing the
        // leaf's localRotation directly. Collider stays put, just toggles enabled.
        [MenuItem("SlimeJumping/Mechanism/Convert 3D Door Prefab to PushDoorV3")]
        public static void Convert3DDoorPrefabV3()
        {
            var target = Selection.activeGameObject;
            if (target == null)
            {
                Debug.LogWarning("Select a 3D door prefab root.");
                return;
            }
            Convert3DDoorToPushDoorV3(target);
        }

        static void Convert3DDoorToPushDoorV3(GameObject target)
        {
            Undo.RegisterFullObjectHierarchyUndo(target, "Convert 3D Door to PushDoorV3");

            // BoxCollider2D's footprint lives in the host GO's local X-Y plane and 2D
            // physics only honors Z rotation; X/Y rotation skews / collapses the
            // collider in world space. If target has X/Y rotation, build an
            // axis-aligned parent wrapper and host physics on it so the FBX mesh keeps
            // its authoring rotation untouched.
            Vector3 worldEuler = target.transform.eulerAngles;
            float ex = Mathf.DeltaAngle(0f, worldEuler.x);
            float ey = Mathf.DeltaAngle(0f, worldEuler.y);
            float ez = Mathf.DeltaAngle(0f, worldEuler.z);
            bool needsWrapper = Mathf.Abs(ex) > 0.5f || Mathf.Abs(ey) > 0.5f;

            GameObject root;
            if (needsWrapper)
            {
                root = new GameObject(target.name + "_v3");
                Undo.RegisterCreatedObjectUndo(root, "Create axis-aligned wrapper");

                root.transform.SetParent(target.transform.parent, worldPositionStays: false);
                root.transform.position = target.transform.position;
                // Z-only rotation is safe for 2D physics; preserve it from target.
                root.transform.rotation = Quaternion.Euler(0f, 0f, ez);
                root.transform.localScale = Vector3.one;
                root.layer = target.layer;

                Undo.SetTransformParent(target.transform, root.transform, "Reparent target under wrapper");
            }
            else
            {
                root = target;
            }

            // Remove 3D colliders on root, disable on children.
            var rootMC = root.GetComponent<MeshCollider>();
            if (rootMC != null) Undo.DestroyObjectImmediate(rootMC);
            foreach (var c3 in root.GetComponents<Collider>())
                Undo.DestroyObjectImmediate(c3);
            foreach (var c3 in root.GetComponentsInChildren<Collider>(includeInactive: true))
            {
                if (c3.gameObject == root) continue;
                c3.enabled = false;
            }

            // Find the leaf (largest child mesh).
            var meshRenderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            Transform leafTransform = null;
            float bestScore = -1f;
            foreach (var mr in meshRenderers)
            {
                if (mr.transform == root.transform) continue;
                var mf = mr.GetComponent<MeshFilter>();
                Vector3 size = (mf != null && mf.sharedMesh != null)
                    ? mf.sharedMesh.bounds.size
                    : mr.bounds.size;
                float score = size.magnitude;
                if (score > bestScore)
                {
                    bestScore = score;
                    leafTransform = mr.transform;
                }
            }

            if (leafTransform == null)
            {
                Debug.LogError($"Convert failed: no child MeshRenderer found under {root.name}.");
                return;
            }

            // Read leaf bounds for collider sizing + leafForwardLocal auto-detect.
            Vector3 leafBoundsCenter = Vector3.zero;
            Vector3 leafBoundsSize = Vector3.one;
            var leafMF = leafTransform.GetComponent<MeshFilter>();
            if (leafMF != null && leafMF.sharedMesh != null)
            {
                leafBoundsCenter = leafMF.sharedMesh.bounds.center;
                leafBoundsSize = leafMF.sharedMesh.bounds.size;
            }

            // Detect leafForwardLocal from bounds.center (gizmo only).
            Vector3 leafForwardLocal = Vector3.zero;
            if (leafBoundsCenter.sqrMagnitude > 0.001f)
            {
                Vector3 horiz = new Vector3(leafBoundsCenter.x, 0f, leafBoundsCenter.z);
                if (horiz.sqrMagnitude > 0.0001f)
                    leafForwardLocal = horiz.normalized;
                else
                    leafForwardLocal = leafBoundsCenter.normalized;
            }

            // Add 2D physics components.
            var rb = root.GetComponent<Rigidbody2D>();
            if (rb == null) rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = root.GetComponent<BoxCollider2D>();
            if (col == null) col = root.AddComponent<BoxCollider2D>();

            // Collider at leaf's CLOSED-state world center (in root local space).
            Vector3 leafCenterWorld = leafTransform.TransformPoint(leafBoundsCenter);
            Vector2 colliderOffsetXY = new Vector2(
                leafCenterWorld.x - root.transform.position.x,
                leafCenterWorld.y - root.transform.position.y);
            col.offset = colliderOffsetXY;
            col.size = new Vector2(
                Mathf.Max(Mathf.Abs(leafBoundsSize.x), 0.1f),
                Mathf.Max(Mathf.Abs(leafBoundsSize.y), 0.1f));

            // Remove old V1/V2 if present so V3 is the active door behaviour.
            var existingV1 = root.GetComponent<PushDoor>();
            if (existingV1 != null) Undo.DestroyObjectImmediate(existingV1);
            var existingV2 = root.GetComponent<PushDoorV2>();
            if (existingV2 != null) Undo.DestroyObjectImmediate(existingV2);

            var v3 = root.GetComponent<PushDoorV3>();
            if (v3 == null) v3 = root.AddComponent<PushDoorV3>();

            // V3 fields: visualTransform + rotationAxis (Y for FBX standard) + leafForwardLocal (gizmo).
            var so = new SerializedObject(v3);
            so.FindProperty("visualTransform").objectReferenceValue = leafTransform;
            so.FindProperty("rotationAxis").vector3Value = Vector3.up; // (0,1,0) Y rotation
            so.FindProperty("leafForwardLocal").vector3Value = leafForwardLocal;
            so.ApplyModifiedProperties();

            root.layer = 7;
            EditorUtility.SetDirty(root);

            Debug.Log($"Converted 3D door '{root.name}' to PushDoorV3 (angular model).\n" +
                $"  leaf = {leafTransform.name} @ local {leafTransform.localPosition}\n" +
                $"  bounds: center={leafBoundsCenter}, size={leafBoundsSize}\n" +
                $"  leafForwardLocal = {leafForwardLocal} (gizmo only, auto-detected)\n" +
                $"  rotationAxis = (0,1,0) [Y, standard FBX door swing]\n" +
                $"  BoxCollider2D: size={col.size}, offset={col.offset}\n" +
                $"  Door rotates leaf in place (no translation). Collider toggles when |angle| >= openAngleDeg.");
        }

        static void SetupInternal(string rootName, bool useV2)
        {
            var root = new GameObject(rootName);
            Undo.RegisterCreatedObjectUndo(root, "Create " + rootName);

            float baseX = 4f;
            float baseY = 0f;

            CreatePlatform("Floor", root.transform,
                new Vector3(baseX, baseY - 0.5f, 0f),
                new Vector3(14f, 1f, 1f));

            CreatePlatform("LeftWall", root.transform,
                new Vector3(baseX - 7.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            CreatePlatform("RightWall", root.transform,
                new Vector3(baseX + 7.5f, baseY + 4f, 0f),
                new Vector3(1f, 9f, 1f));

            float doorHeight = 2f;
            if (useV2)
            {
                CreatePushDoorV2("PushDoorV2_01", root.transform,
                    new Vector3(baseX, baseY + doorHeight * 0.5f, 0f),
                    doorHeight: doorHeight, doorDepth: 2f);
            }
            else
            {
                CreatePushDoor("PushDoor_01", root.transform,
                    new Vector3(baseX, baseY + doorHeight * 0.5f, 0f),
                    doorHeight: doorHeight, doorDepth: 2f);
            }

            // --- Left ramp (approach from left) ---
            var leftRamp = CreatePlatform("LeftRamp", root.transform,
                new Vector3(baseX - 3f, baseY + 0.25f, 0f),
                new Vector3(3f, 0.5f, 1f));

            // --- Right ramp (approach from right) ---
            var rightRamp = CreatePlatform("RightRamp", root.transform,
                new Vector3(baseX + 3f, baseY + 0.25f, 0f),
                new Vector3(3f, 0.5f, 1f));

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = root;
            Debug.Log(rootName + " setup complete!\n" +
                "- Door in center: slam into it to push open (V2 uses impulse curve)\n" +
                "- Ramps on both sides for approach");
        }

        static void CreatePushDoorV2(string name, Transform parent, Vector3 hingePos,
            float doorHeight, float doorDepth)
        {
            float doorThickness = 0.15f;

            var door = new GameObject(name);
            door.transform.parent = parent;
            door.transform.position = hingePos;
            door.layer = 7;

            var rb = door.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = door.AddComponent<BoxCollider2D>();
            col.size = new Vector2(doorThickness, doorHeight);
            col.offset = Vector2.zero;

            door.AddComponent<PushDoorV2>();

            // Frame (decorative, no collider) — shows where door sits
            var frame = GameObject.CreatePrimitive(PrimitiveType.Cube);
            frame.name = "Frame";
            Object.DestroyImmediate(frame.GetComponent<BoxCollider>());
            frame.transform.parent = door.transform;
            frame.transform.localPosition = new Vector3(0f, doorHeight * 0.5f + 0.1f, 0f);
            frame.transform.localScale = new Vector3(doorThickness * 2f, 0.15f, doorDepth * 1.1f);
            frame.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.35f, 0.2f, 0.08f) };

            // Hinge (empty marker, picked up by Reset() of PushDoorV2)
            var hinge = new GameObject("Hinge");
            hinge.transform.parent = door.transform;
            hinge.transform.localPosition = new Vector3(0f, 0f, doorDepth);
            hinge.transform.localRotation = Quaternion.identity;

            // Leaf — the moving door panel (visualTransform target)
            var leaf = new GameObject("Leaf");
            leaf.transform.parent = door.transform;
            leaf.transform.localPosition = Vector3.zero;
            leaf.transform.localRotation = Quaternion.identity;

            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "Mesh";
            Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
            mesh.transform.parent = leaf.transform;
            mesh.transform.localPosition = new Vector3(0f, 0f, -doorDepth * 0.5f);
            mesh.transform.localScale = new Vector3(doorThickness, doorHeight, doorDepth);
            mesh.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.6f, 0.35f, 0.15f) };

            var so = new SerializedObject(door.GetComponent<PushDoorV2>());
            so.FindProperty("visualTransform").objectReferenceValue = leaf.transform;
            so.FindProperty("hingeOffset").vector3Value = new Vector3(0f, 0f, doorDepth);
            so.ApplyModifiedProperties();
        }

        static void ConvertToPushDoorV2(GameObject root)
        {
            Undo.RegisterFullObjectHierarchyUndo(root, "Convert to PushDoorV2");

            var leaf = root.transform.Find("Leaf");
            if (leaf == null) leaf = root.transform.Find("DoorLeaf");
            if (leaf == null) leaf = root.transform.Find("VisualPivot");
            if (leaf == null)
            {
                Debug.LogError("Conversion failed: no child named Leaf/DoorLeaf/VisualPivot found under " + root.name);
                return;
            }

            var hinge = root.transform.Find("Hinge");

            var rb = root.GetComponent<Rigidbody2D>();
            if (rb == null) rb = root.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            var col = root.GetComponent<BoxCollider2D>();
            if (col == null)
            {
                col = root.AddComponent<BoxCollider2D>();
                col.size = new Vector2(0.15f, 2f);
            }

            var existingV1 = root.GetComponent<PushDoor>();
            if (existingV1 != null) Undo.DestroyObjectImmediate(existingV1);

            var v2 = root.GetComponent<PushDoorV2>();
            if (v2 == null) v2 = root.AddComponent<PushDoorV2>();

            var so = new SerializedObject(v2);
            so.FindProperty("visualTransform").objectReferenceValue = leaf;
            if (hinge != null)
                so.FindProperty("hingeOffset").vector3Value = hinge.localPosition;
            so.ApplyModifiedProperties();

            Debug.Log($"Converted '{root.name}' to PushDoorV2. Leaf={leaf.name}, Hinge={(hinge != null ? hinge.name : "(default offset)")}");
        }

        static void CreatePushDoor(string name, Transform parent, Vector3 hingePos,
            float doorHeight, float doorDepth)
        {
            float doorThickness = 0.15f;

            // Parent = collider position (Z=0 plane, vertically centered on door)
            var door = new GameObject(name);
            door.transform.parent = parent;
            door.transform.position = hingePos;
            door.layer = 7;

            // Rigidbody2D (Kinematic)
            var rb = door.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            // BoxCollider2D — thin cross-section at Z=0 (the door edge the slime sees)
            var col = door.AddComponent<BoxCollider2D>();
            col.size = new Vector2(doorThickness, doorHeight);
            col.offset = Vector2.zero;

            // PushDoor component
            door.AddComponent<PushDoor>();

            // VisualPivot — PushDoor.cs positions this via hingeOffset each frame
            var pivot = new GameObject("VisualPivot");
            pivot.transform.parent = door.transform;
            pivot.transform.localPosition = Vector3.zero;
            pivot.transform.localRotation = Quaternion.identity;

            // Mesh — extends from hinge toward camera (Z- direction)
            var mesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            mesh.name = "Mesh";
            Object.DestroyImmediate(mesh.GetComponent<BoxCollider>());
            mesh.transform.parent = pivot.transform;
            mesh.transform.localPosition = new Vector3(0f, 0f, -doorDepth * 0.5f);
            mesh.transform.localScale = new Vector3(doorThickness, doorHeight, doorDepth);

            mesh.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.6f, 0.35f, 0.15f) };

            // Wire PushDoor fields
            var so = new SerializedObject(door.GetComponent<PushDoor>());
            so.FindProperty("visualTransform").objectReferenceValue = pivot.transform;
            so.FindProperty("hingeOffset").vector3Value = new Vector3(0f, 0f, doorDepth);
            so.ApplyModifiedProperties();
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
            go.AddComponent<BoxCollider2D>().size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.5f, 0.5f, 0.5f) };
            return go;
        }
    }
}
