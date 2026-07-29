#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SlimeJumping.EditorTools
{
    /// <summary>MeshToCollider2DGenerator, then collapse to the single largest path — mirrors PolygonCollider2DOptimizer's "Keep largest path only" (bbox area).</summary>
    public static class MeshToCollider2DGeneratorLargestOnly
    {
        private const string MenuPath =
            "SlimeJumping/Generate PolygonCollider2D from Mesh (Z=0, largest path only)";

        [MenuItem(MenuPath, false, 201)]
        public static void Generate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;

            int pathsBefore = 0;
            // After the first run, the existing collider lives on the wrapper parent ({name}_Collider2D).
            var wrapperCandidate = go.transform.parent;
            PolygonCollider2D existingPc = null;
            if (wrapperCandidate != null && wrapperCandidate.name == go.name + "_Collider2D")
                existingPc = wrapperCandidate.GetComponent<PolygonCollider2D>();
            if (existingPc != null) pathsBefore = existingPc.pathCount;

            var pc = MeshToCollider2DGenerator.Generate();
            if (pc == null) return; // generator bailed out (no intersection, user cancelled, etc.)
            if (pc.pathCount <= 1) return;

            KeepLargestPath(pc, pathsBefore);
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            var go = Selection.activeGameObject;
            if (go == null) return false;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;
            return true;
        }

        // Mirrors PolygonCollider2DOptimizer.KeepLargestPath: largest bbox wins, rest dropped.
        private static void KeepLargestPath(PolygonCollider2D pc, int pathsBeforeGeneration)
        {
            int droppedCount = pc.pathCount - 1;
            int biggestPath = 0;
            float biggestSize = -1f;

            for (int i = 0; i < pc.pathCount; i++)
            {
                var path = pc.GetPath(i);
                float xMin = Mathf.Infinity, yMin = Mathf.Infinity;
                float xMax = -Mathf.Infinity, yMax = -Mathf.Infinity;
                for (int j = 0; j < path.Length; j++)
                {
                    xMin = Mathf.Min(xMin, path[j].x);
                    xMax = Mathf.Max(xMax, path[j].x);
                    yMin = Mathf.Min(yMin, path[j].y);
                    yMax = Mathf.Max(yMax, path[j].y);
                }
                float area = (xMax - xMin) * (yMax - yMin);
                if (area > biggestSize)
                {
                    biggestSize = area;
                    biggestPath = i;
                }
            }

            Undo.RecordObject(pc, "Keep largest path only");
            var kept = pc.GetPath(biggestPath);
            pc.pathCount = 1;
            pc.SetPath(0, kept);
            EditorUtility.SetDirty(pc);

            Debug.Log($"[MeshToCollider2D] Keep largest path only: dropped {droppedCount} smaller loop(s), kept path #{biggestPath}.");
        }
    }
}
#endif
