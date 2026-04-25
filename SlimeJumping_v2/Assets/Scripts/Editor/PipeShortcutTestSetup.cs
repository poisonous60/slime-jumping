using UnityEngine;
using UnityEditor;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.EditorTools
{
    /// <summary>
    /// Adds a pipe shortcut into the active SampleScene. Layout: a tall room with
    /// two walls; one PipeEntrance is mounted on the left wall near the floor,
    /// the other on the right wall near the ceiling. The slime sticks to a wall
    /// next to an entrance and pushes the input toward the wall to enter; it
    /// emerges from the paired exit on the opposite wall after a short in-wall
    /// traversal animated by the PipeDotTrail.
    /// </summary>
    public static class PipeShortcutTestSetup
    {
        private const float OriginX = 90f;
        private const float OriginY = 5f;

        [MenuItem("SlimeJumping/Mechanism/Setup Pipe Shortcut Test")]
        public static void SetupPipeShortcutTest()
        {
            // Remove any prior copy so the menu can be re-run during iteration
            // without duplicating geometry or stale field values. Preserve the
            // prior root world position so a designer who has already nudged the
            // test setup into a custom spot doesn't have to re-aim it after every
            // rebuild.
            Vector3 spawnPos = new Vector3(OriginX, OriginY, 0f);
            var prior = GameObject.Find("PipeShortcutTest");
            if (prior != null)
            {
                spawnPos = prior.transform.position;
                Undo.DestroyObjectImmediate(prior);
            }

            var root = new GameObject("PipeShortcutTest");
            Undo.RegisterCreatedObjectUndo(root, "Create Pipe Shortcut Test");
            root.transform.position = spawnPos;

            // ---- Geometry: vertical shaft (two walls + floor + roof) ----
            // Two layers per side: a thin inner wall (slime sticks to its inner face)
            // plus a thick outer back-wall, with a 3-unit gap between them. The pipe
            // path runs through that gap so it visibly snakes through wall material
            // rather than empty void.
            float shaftWidth = 8f;
            float wallH = 18f;
            float wallGap = 3f;             // gap between inner and outer wall faces (= visible wall thickness)
            float outerWallThickness = 1.5f; // thickness of the back wall slab

            // Inner walls (1 unit thick, slime surface). Inner face at ±shaftWidth/2.
            BuildPlatform("InnerWallLeft", root.transform,
                new Vector3(-shaftWidth / 2f - 0.5f, 0f, 0f),
                new Vector3(1f, wallH, 1f),
                new Color(0.55f, 0.55f, 0.6f));
            BuildPlatform("InnerWallRight", root.transform,
                new Vector3(shaftWidth / 2f + 0.5f, 0f, 0f),
                new Vector3(1f, wallH, 1f),
                new Color(0.55f, 0.55f, 0.6f));

            // Outer back walls — visually fill the wall thickness so the pipe path
            // reads as "inside the wall". Slightly darker shade so the layering reads.
            float outerWallCenterX = shaftWidth / 2f + 1f + wallGap + outerWallThickness / 2f;
            BuildPlatform("OuterWallLeft", root.transform,
                new Vector3(-outerWallCenterX, 0f, 0f),
                new Vector3(outerWallThickness, wallH + wallGap * 2f + outerWallThickness * 2f, 1f),
                new Color(0.32f, 0.32f, 0.36f));
            BuildPlatform("OuterWallRight", root.transform,
                new Vector3(outerWallCenterX, 0f, 0f),
                new Vector3(outerWallThickness, wallH + wallGap * 2f + outerWallThickness * 2f, 1f),
                new Color(0.32f, 0.32f, 0.36f));

            // Inner floor / roof. Slime sticks to their inner face.
            BuildPlatform("InnerFloor", root.transform,
                new Vector3(0f, -wallH / 2f - 0.5f, 0f),
                new Vector3(shaftWidth + 2f, 1f, 1f),
                new Color(0.55f, 0.55f, 0.6f));
            BuildPlatform("InnerRoof", root.transform,
                new Vector3(0f, wallH / 2f + 0.5f, 0f),
                new Vector3(shaftWidth + 2f, 1f, 1f),
                new Color(0.55f, 0.55f, 0.6f));

            // Outer back floor / roof — enclose the under-floor and over-roof path
            // segments so they sit inside material instead of open space.
            float outerSlabCenterY = wallH / 2f + 1f + wallGap + outerWallThickness / 2f;
            float outerSlabWidth = shaftWidth + 2f + (1f + wallGap + outerWallThickness) * 2f;
            BuildPlatform("OuterFloor", root.transform,
                new Vector3(0f, -outerSlabCenterY, 0f),
                new Vector3(outerSlabWidth, outerWallThickness, 1f),
                new Color(0.32f, 0.32f, 0.36f));
            BuildPlatform("OuterRoof", root.transform,
                new Vector3(0f, outerSlabCenterY, 0f),
                new Vector3(outerSlabWidth, outerWallThickness, 1f),
                new Color(0.32f, 0.32f, 0.36f));

            // Connecting wall-thickness slabs at the corners — fills the gap
            // between OuterWall and OuterFloor/Roof so the back-wall reads as one
            // continuous "wall material" surrounding the shaft.
            float cornerSlabH = wallGap + outerWallThickness;
            float cornerSlabW = wallGap + 1f; // bridges from inner wall outer face to outer wall inner face
            float cornerSlabCenterX = shaftWidth / 2f + 1f + (wallGap) / 2f;
            float cornerSlabCenterY = wallH / 2f + 1f + (wallGap + outerWallThickness) / 2f;
            BuildPlatform("CornerTL", root.transform,
                new Vector3(-cornerSlabCenterX, cornerSlabCenterY, 0f),
                new Vector3(cornerSlabW, cornerSlabH, 1f),
                new Color(0.32f, 0.32f, 0.36f));
            BuildPlatform("CornerTR", root.transform,
                new Vector3(cornerSlabCenterX, cornerSlabCenterY, 0f),
                new Vector3(cornerSlabW, cornerSlabH, 1f),
                new Color(0.32f, 0.32f, 0.36f));
            BuildPlatform("CornerBL", root.transform,
                new Vector3(-cornerSlabCenterX, -cornerSlabCenterY, 0f),
                new Vector3(cornerSlabW, cornerSlabH, 1f),
                new Color(0.32f, 0.32f, 0.36f));
            BuildPlatform("CornerBR", root.transform,
                new Vector3(cornerSlabCenterX, -cornerSlabCenterY, 0f),
                new Vector3(cornerSlabW, cornerSlabH, 1f),
                new Color(0.32f, 0.32f, 0.36f));

            // Entry ramp from outside the shaft so the slime can walk in.
            // Now sits below the outer floor level so it leads UP into the shaft.
            BuildPlatform("EntryRamp", root.transform,
                new Vector3(-shaftWidth / 2f - 5f, -wallH / 2f, 0f),
                new Vector3(8f, 1f, 1f),
                new Color(0.4f, 0.4f, 0.4f));

            // ---- Pipe path: left-bottom → (down a bit, across, up) → right-top ----
            // Runs inside the wall thickness, with a couple of bends so the dot
            // trail visibly snakes through the wall.
            float wallSurfaceXLeft = -shaftWidth / 2f;        // left wall's inner face (slime side)
            float wallSurfaceXRight = shaftWidth / 2f;        // right wall's inner face
            float entranceLeftY = -wallH / 2f + 3f;
            float entranceRightY = wallH / 2f - 3f;

            // Entrance positions sit slightly INSIDE the wall so the trigger box
            // overlaps the wall surface — slime sticks to the wall while the
            // trigger fires.
            float entranceInset = 0.4f;
            Vector3 entranceLeftWorld = new Vector3(
                wallSurfaceXLeft - entranceInset, entranceLeftY, 0f);
            Vector3 entranceRightWorld = new Vector3(
                wallSurfaceXRight + entranceInset, entranceRightY, 0f);

            var pipeRootGo = new GameObject("Pipe");
            pipeRootGo.transform.SetParent(root.transform, worldPositionStays: false);

            // PipePath lives on the Pipe root. Path goes from the left entrance,
            // dips down inside the left wall, runs across UNDER the floor at the
            // shaft midline, comes up inside the right wall, lands at the right
            // entrance.
            var pathGo = new GameObject("PipePath");
            pathGo.transform.SetParent(pipeRootGo.transform, worldPositionStays: false);
            var path = pathGo.AddComponent<PipePath>();

            // Path waypoints are kept inside the wall corridor (the gap between
            // inner wall and outer back wall) so the dot trail is visibly drawn
            // ON wall material rather than crossing the inner floor / inner wall.
            // Stored in PipePath's LOCAL space (PipePath sits at root.position with
            // identity scale), so the whole path follows when designers move the
            // PipeShortcutTest root in the editor.
            float bottomCorridorY = -(wallH / 2f + 1f + wallGap * 0.5f);
            float leftCorridorX = -(shaftWidth / 2f + 1f + wallGap * 0.5f);
            float rightCorridorX = shaftWidth / 2f + 1f + wallGap * 0.5f;
            Vector2[] localPoints = new Vector2[]
            {
                entranceLeftWorld,                                              // 1. entry endpoint (in wall, near surface)
                (Vector2)entranceLeftWorld + new Vector2(-1.0f, 0f),            // 2. step deeper into wall corridor
                new Vector2(leftCorridorX, bottomCorridorY),                    // 3. dive down to bottom corridor
                new Vector2(0f, bottomCorridorY),                               // 4. across the bottom corridor
                new Vector2(rightCorridorX, bottomCorridorY),                   // 5. start climbing right wall
                (Vector2)entranceRightWorld + new Vector2(1.0f, 0f),            // 6. step toward exit (still in wall)
                entranceRightWorld,                                             // 7. exit endpoint
            };
            path.SetWaypoints(localPoints);

            // Dot trail on the same GameObject, will Awake-build at play time.
            var trail = pathGo.AddComponent<PipeDotTrail>();

            // ---- Two endpoints ----
            // Convert local entrance offsets to world by adding the (possibly user-moved)
            // root position so entrances spawn at the right place.
            Vector3 origin = root.transform.position;
            var leftEntrance = BuildEntrance(
                "PipeEntrance_Left", pipeRootGo.transform,
                origin + entranceLeftWorld,
                Vector2.left,        // inward = -X (into the left wall)
                path);
            var rightEntrance = BuildEntrance(
                "PipeEntrance_Right", pipeRootGo.transform,
                origin + entranceRightWorld,
                Vector2.right,       // inward = +X (into the right wall)
                path);

            // Pair them.
            var soLeft = new SerializedObject(leftEntrance);
            soLeft.FindProperty("pairedExit").objectReferenceValue = rightEntrance;
            soLeft.ApplyModifiedProperties();
            var soRight = new SerializedObject(rightEntrance);
            soRight.FindProperty("pairedExit").objectReferenceValue = leftEntrance;
            soRight.ApplyModifiedProperties();

            // Mark each entrance with three horizontal stripes (Rain World pipe
            // mouth motif). Outward direction is opposite of the inward dir, so
            // the bars poke out from the wall face into the shaft — visible to
            // the player approaching the wall.
            BuildEntranceMarker(leftEntrance.transform, Vector2.right);
            BuildEntranceMarker(rightEntrance.transform, Vector2.left);

            // Force the dot trail to build NOW so the dots show up in the editor
            // before play. PipeDotTrail.RebuildEditor handles re-init safely.
            trail.RebuildEditor();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = root;
            Debug.Log($"[PipeShortcutTestSetup] Created at ({OriginX}, {OriginY}). " +
                      "Walk in via the entry ramp, climb the left wall, stick to the wall next to the lower marker, " +
                      "and hold input toward the wall (←). The slime should fade into the wall, the dot trail lights up across the path, " +
                      "and the slime emerges from the upper-right marker.");
        }

        private static PipeEntrance BuildEntrance(string name, Transform parent, Vector3 worldPos, Vector2 inwardDirLocal, PipePath path)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = worldPos;

            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(1.2f, 2.5f);

            var entrance = go.AddComponent<PipeEntrance>();
            // PipeEntrance.Awake() will create the controller component if missing,
            // but adding it explicitly here makes the inspector cleaner and lets us
            // surface its tunables to the designer right away.
            var controller = go.AddComponent<PipeTraversalController>();

            var so = new SerializedObject(entrance);
            so.FindProperty("pipeInwardDirLocal").vector2Value = inwardDirLocal;
            so.FindProperty("path").objectReferenceValue = path;
            so.FindProperty("controller").objectReferenceValue = controller;
            so.ApplyModifiedProperties();

            return entrance;
        }

        private static void BuildEntranceMarker(Transform parent, Vector2 outwardDir)
        {
            // Three horizontal stripes stacked vertically, poking OUT from the wall
            // face into the shaft so the player sees them when approaching. Each bar
            // is wide along the wall-tangent (X here, since walls are vertical) and
            // short along the stack axis (Y).
            const float stripeWidth = 0.7f;
            const float stripeHeight = 0.18f;
            const float stripeSpacing = 0.45f;
            const float outwardOffset = 0.5f;

            for (int i = -1; i <= 1; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bar.name = "Marker_" + i;
                bar.transform.SetParent(parent, worldPositionStays: false);
                bar.transform.localPosition = new Vector3(outwardDir.x * outwardOffset, i * stripeSpacing, 0f);
                bar.transform.localScale = new Vector3(stripeWidth, stripeHeight, 1f);
                Object.DestroyImmediate(bar.GetComponent<MeshCollider>());
                bar.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(new Color(1.6f, 1.9f, 2.6f, 1f));
            }
        }

        private static GameObject BuildPlatform(string name, Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.layer = 7; // ground

            Object.DestroyImmediate(go.GetComponent<BoxCollider>());
            var col2d = go.AddComponent<BoxCollider2D>();
            col2d.size = Vector2.one;

            go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(color);
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
