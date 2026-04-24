using UnityEngine;

namespace SlimeJumping.VFX
{
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("SlimeJumping/VFX/Snow Polygon Area")]
    public sealed class SnowPolygonArea : MonoBehaviour
    {
        [Tooltip("Polygon where snow is allowed to spawn (emission area). Edit vertices in the Scene view.")]
        [SerializeField] private PolygonCollider2D emitterArea;

        [Tooltip("Optional polygon (trigger) where falling snow is killed — place under the roof to exclude that zone.")]
        [SerializeField] private PolygonCollider2D roofKillArea;

        private Mesh _runtimeMesh;
        private int _lastEmitterHash;
        private int _lastRoofHash;

        private void OnEnable() { Rebuild(true); }
        private void OnValidate() { Rebuild(true); }

        private void Update()
        {
            if (Application.isPlaying) return;
            int eh = HashPoly(emitterArea);
            int rh = HashPoly(roofKillArea);
            if (eh != _lastEmitterHash || rh != _lastRoofHash) Rebuild(false);
        }

        private static int HashPoly(PolygonCollider2D pc)
        {
            if (!pc) return 0;
            unchecked
            {
                int h = 17;
                var pts = pc.points;
                for (int i = 0; i < pts.Length; i++) h = h * 31 + pts[i].GetHashCode();
                var t = pc.transform;
                h = h * 31 + t.localPosition.GetHashCode();
                h = h * 31 + t.localRotation.GetHashCode();
                h = h * 31 + t.localScale.GetHashCode();
                return h;
            }
        }

        [ContextMenu("Rebuild")]
        public void RebuildMenu() { Rebuild(true); }

        private void Rebuild(bool forceMesh)
        {
            var ps = GetComponent<ParticleSystem>();
            if (!ps) return;

            // --- emitter polygon -> mesh baked in particle-system-local space ---
            if (emitterArea)
            {
                // World-space mesh from the collider
                var worldMesh = emitterArea.CreateMesh(true, true);
                if (worldMesh)
                {
                    var verts = worldMesh.vertices;
                    var tris = worldMesh.triangles;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        // Bake into this ParticleSystem's local space so shape offset = 0.
                        var local = transform.InverseTransformPoint(verts[i]);
                        local.z = 0f;
                        verts[i] = local;
                    }
                    if (Application.isPlaying) Destroy(worldMesh);
                    else DestroyImmediate(worldMesh);

                    if (_runtimeMesh)
                    {
                        if (Application.isPlaying) Destroy(_runtimeMesh);
                        else DestroyImmediate(_runtimeMesh);
                    }
                    _runtimeMesh = new Mesh
                    {
                        name = "SnowPolygonMesh",
                        hideFlags = HideFlags.DontSave,
                        vertices = verts,
                        triangles = tris
                    };
                    _runtimeMesh.RecalculateBounds();

                    var s = ps.shape;
                    s.enabled = true;
                    s.shapeType = ParticleSystemShapeType.Mesh;
                    s.meshShapeType = ParticleSystemMeshShapeType.Triangle;
                    s.mesh = _runtimeMesh;
                    s.position = Vector3.zero;
                    s.rotation = Vector3.zero;
                    s.scale = Vector3.one;
                }
            }
            _lastEmitterHash = HashPoly(emitterArea);

            // --- roof kill polygon -> trigger module ---
            var trig = ps.trigger;
            if (roofKillArea)
            {
                trig.enabled = true;
                for (int i = trig.colliderCount - 1; i >= 0; i--) trig.RemoveCollider(i);
                trig.AddCollider(roofKillArea);
                trig.inside = ParticleSystemOverlapAction.Kill;
                trig.enter = ParticleSystemOverlapAction.Kill;
                trig.outside = ParticleSystemOverlapAction.Ignore;
                trig.exit = ParticleSystemOverlapAction.Ignore;
            }
            else
            {
                trig.enabled = false;
            }
            _lastRoofHash = HashPoly(roofKillArea);
        }

        private void OnDrawGizmosSelected()
        {
            DrawPoly(emitterArea, new Color(0.7f, 0.9f, 1f, 0.9f));
            DrawPoly(roofKillArea, new Color(1f, 0.4f, 0.3f, 0.9f));
        }

        private static void DrawPoly(PolygonCollider2D pc, Color c)
        {
            if (!pc) return;
            Gizmos.color = c;
            var t = pc.transform;
            var pts = pc.points;
            if (pts == null || pts.Length < 2) return;
            for (int i = 0; i < pts.Length; i++)
            {
                Vector3 a = t.TransformPoint(pts[i]);
                Vector3 b = t.TransformPoint(pts[(i + 1) % pts.Length]);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
