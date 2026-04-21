using System.Collections.Generic;
using UnityEngine;
using MegaFiers;

namespace SlimeJumping.Platforms
{
    // Keeps a PolygonCollider2D / EdgeCollider2D in sync with a MegaFiers-deformed mesh.
    // Strategy: at bake time, `contourIndices` stores an ordered list of source-mesh
    // vertex indices that describe the desired 2D silhouette (closed polygon OR open
    // polyline). Each frame we read those deformed verts out of `source.mesh` and
    // push them into the collider's points in the collider's local space.
    //
    // Runs with DefaultExecutionOrder = int.MaxValue so its LateUpdate fires AFTER
    // MegaModifyObject.LateUpdate in the same frame — MegaFiers' own jobs all
    // Complete() inside SetMesh() before returning, so mesh.GetVertices is safe.
    [ExecuteAlways]
    [DefaultExecutionOrder(int.MaxValue)]
    [AddComponentMenu("SlimeJumping/Platforms/Mega Deform 2D Collider")]
    public class MegaDeform2DCollider : MonoBehaviour
    {
        [SerializeField] MegaModifyObject source;
        [SerializeField] Collider2D target;
        [SerializeField] int[] contourIndices;
        [SerializeField] bool closedLoop = false;
        [SerializeField] int pathIndex = 0;
        [SerializeField] bool reverseOnNegativeScale = true;

        readonly List<Vector3> vertBuf = new List<Vector3>();
        readonly List<Vector2> pointBuf = new List<Vector2>();
        bool onRenderWarned;

        public MegaModifyObject Source { get => source; set => source = value; }
        public Collider2D Target { get => target; set => target = value; }
        public int[] ContourIndices { get => contourIndices; set => contourIndices = value; }
        public bool ClosedLoop { get => closedLoop; set => closedLoop = value; }
        public int PathIndex { get => pathIndex; set => pathIndex = value; }

        void LateUpdate()
        {
            if (source == null || target == null) return;
            if (contourIndices == null || contourIndices.Length < 2) return;

            var mesh = source.mesh;
            if (mesh == null)
            {
                // MegaModifyObject binds `mesh` in Start / MeshChanged. Before that
                // point (e.g. first edit-mode frame after domain reload) we fall back
                // to originalMesh so the collider at least tracks the rest pose.
                mesh = source.originalMesh;
                if (mesh == null) return;
            }

            if (source.UpdateMode == MegaUpdateMode.OnRender && !onRenderWarned)
            {
                Debug.LogWarning(
                    $"[MegaDeform2DCollider] {source.name} uses UpdateMode.OnRender; 2D collider will lag by one frame.",
                    this);
                onRenderWarned = true;
            }

            mesh.GetVertices(vertBuf);
            pointBuf.Clear();

            var srcTr = source.transform;
            var tgtTr = target.transform;
            bool sameTransform = srcTr == tgtTr;

            int vertCount = vertBuf.Count;
            for (int i = 0; i < contourIndices.Length; i++)
            {
                int idx = contourIndices[i];
                if ((uint)idx >= (uint)vertCount) continue;

                Vector3 v = vertBuf[idx];
                if (!sameTransform)
                {
                    Vector3 w = srcTr.TransformPoint(v);
                    v = tgtTr.InverseTransformPoint(w);
                }
                pointBuf.Add(new Vector2(v.x, v.y));
            }

            if (pointBuf.Count < 2) return;

            if (reverseOnNegativeScale && HasNegativeScale2D(tgtTr))
                pointBuf.Reverse();

            switch (target)
            {
                case EdgeCollider2D edge:
                    edge.SetPoints(pointBuf);
                    break;
                case PolygonCollider2D poly:
                    if (closedLoop && poly.pathCount <= pathIndex)
                        poly.pathCount = pathIndex + 1;
                    poly.SetPath(pathIndex, pointBuf);
                    break;
            }
        }

        static bool HasNegativeScale2D(Transform tr)
        {
            var s = tr.lossyScale;
            return (s.x * s.y) < 0f;
        }
    }
}
