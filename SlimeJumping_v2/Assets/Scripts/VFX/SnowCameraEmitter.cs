using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace SlimeJumping.VFX
{
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("SlimeJumping/VFX/Snow Camera Emitter")]
    public sealed class SnowCameraEmitter : MonoBehaviour
    {
        [Tooltip("Camera to follow. Falls back to Camera.main if null.")]
        [SerializeField] private UnityCamera targetCamera;

        [Tooltip("Emission box width = viewport width × this multiplier.")]
        [Min(1f)][SerializeField] private float widthMultiplier = 1.8f;

        [Tooltip("Emission box height = viewport height × this multiplier.")]
        [Min(1f)][SerializeField] private float heightMultiplier = 1.8f;

        [Tooltip("Polygon defining where snow is allowed to be visible. Particles outside this polygon are killed every frame via manual OverlapPoint test (the ParticleSystem Trigger module's polygon test was unreliable, hence the manual cull). Use multi-path PolygonCollider2D to carve out roof holes.")]
        [SerializeField] private PolygonCollider2D emitterArea;

        private ParticleSystem _ps;
        private ParticleSystem.Particle[] _particleBuffer;

        private void OnEnable()
        {
            _ps = GetComponent<ParticleSystem>();
            ResolveCamera();
            ApplyMain();
            ApplyShape();
        }

        private void OnValidate()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;
            ApplyMain();
            ApplyShape();
        }

        private void LateUpdate()
        {
            if (_ps == null) return;
            ResolveCamera();
            UpdateTransform();
            UpdateShapeSize();
            CullOutsidePolygon();
        }

        private void ResolveCamera()
        {
            if (targetCamera == null) targetCamera = UnityCamera.main;
        }

        private void ApplyMain()
        {
            var main = _ps.main;
            // World simulation: existing particles stay in their world position when
            // the camera moves. Only the spawn region follows the camera (via the GO
            // transform); the snow itself does not drag along with the slime.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }

        private void ApplyShape()
        {
            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;
        }

        private void UpdateTransform()
        {
            if (targetCamera == null) return;
            Vector3 cam = targetCamera.transform.position;
            transform.SetPositionAndRotation(new Vector3(cam.x, cam.y, 0f), Quaternion.identity);
        }

        private void UpdateShapeSize()
        {
            if (targetCamera == null) return;
            float halfH = GetViewportHalfHeight();
            float halfW = halfH * targetCamera.aspect;
            var shape = _ps.shape;
            shape.scale = new Vector3(halfW * 2f * widthMultiplier, halfH * 2f * heightMultiplier, 0f);
        }

        private float GetViewportHalfHeight()
        {
            if (targetCamera == null) return 5f;
            if (targetCamera.orthographic) return targetCamera.orthographicSize;
            float dist = Mathf.Abs(targetCamera.transform.position.z);
            return Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(dist, 0.01f);
        }

        // Manual polygon cull. Replaces the previous Trigger-module approach, which
        // didn't reliably kill particles spawned outside the polygon — visible snow
        // ended up filling the whole camera viewport regardless of the assigned area.
        // OverlapPoint on the PolygonCollider2D handles arbitrary multi-path polygons
        // exactly and runs every LateUpdate over the ~hundreds of live particles.
        private void CullOutsidePolygon()
        {
            if (emitterArea == null) return;
            if (_ps == null) return;
            int alive = _ps.particleCount;
            if (alive <= 0) return;

            if (_particleBuffer == null || _particleBuffer.Length < alive)
                _particleBuffer = new ParticleSystem.Particle[Mathf.Max(alive, 64)];

            int n = _ps.GetParticles(_particleBuffer);
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                Vector3 pos = _particleBuffer[i].position;
                if (!emitterArea.OverlapPoint(new Vector2(pos.x, pos.y)))
                {
                    _particleBuffer[i].remainingLifetime = -1f;
                    changed = true;
                }
            }
            if (changed) _ps.SetParticles(_particleBuffer, n);
        }

        private void OnDrawGizmosSelected()
        {
            if (targetCamera != null)
            {
                float halfH = GetViewportHalfHeight();
                float halfW = halfH * targetCamera.aspect;
                Vector3 cam = targetCamera.transform.position;
                cam.z = 0f;

                Gizmos.color = new Color(0.8f, 0.8f, 0.2f, 0.6f);
                DrawRect(cam, halfW * 2f, halfH * 2f);

                Gizmos.color = new Color(0.7f, 0.9f, 1f, 0.9f);
                DrawRect(cam, halfW * 2f * widthMultiplier, halfH * 2f * heightMultiplier);
            }

            DrawPoly(emitterArea, new Color(0.4f, 1f, 0.6f, 0.9f));
        }

        private static void DrawRect(Vector3 center, float w, float h)
        {
            Vector3 p0 = center + new Vector3(-w * 0.5f, -h * 0.5f, 0f);
            Vector3 p1 = center + new Vector3(+w * 0.5f, -h * 0.5f, 0f);
            Vector3 p2 = center + new Vector3(+w * 0.5f, +h * 0.5f, 0f);
            Vector3 p3 = center + new Vector3(-w * 0.5f, +h * 0.5f, 0f);
            Gizmos.DrawLine(p0, p1);
            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p2, p3);
            Gizmos.DrawLine(p3, p0);
        }

        private static void DrawPoly(PolygonCollider2D pc, Color c)
        {
            if (pc == null) return;
            Gizmos.color = c;
            var t = pc.transform;
            int paths = pc.pathCount;
            for (int p = 0; p < paths; p++)
            {
                var pts = pc.GetPath(p);
                if (pts == null || pts.Length < 2) continue;
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector3 a = t.TransformPoint(pts[i]);
                    Vector3 b = t.TransformPoint(pts[(i + 1) % pts.Length]);
                    Gizmos.DrawLine(a, b);
                }
            }
        }
    }
}
