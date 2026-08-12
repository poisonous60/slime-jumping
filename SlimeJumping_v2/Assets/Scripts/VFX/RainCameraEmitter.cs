using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace SlimeJumping.VFX
{
    /// <summary>
    /// Parks a rain layer's spawn box on the camera at a fixed world Z and thins emission outside an altitude band.
    /// The box is measured against the viewport on that Z plane, so layers at different Z get parallax from real
    /// perspective instead of a faked scroll rate. Rate is per unit of box area so every layer reads at one density.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(ParticleSystem))]
    [AddComponentMenu("SlimeJumping/VFX/Rain Camera Emitter")]
    public sealed class RainCameraEmitter : MonoBehaviour
    {
        [SerializeField] private UnityCamera targetCamera;

        [Tooltip("World Z this layer sits at.")]
        [SerializeField] private float layerZ;

        [Min(1f)][SerializeField] private float widthMultiplier = 1.4f;

        [Min(1f)][SerializeField] private float heightMultiplier = 1.6f;

        [Tooltip("Drops per second per square world unit of spawn box.")]
        [Min(0f)][SerializeField] private float dropsPerSquareUnit = 1.77f;

        [Header("Altitude Band")]
        [SerializeField] private float fadeInStartY = 252f;
        [SerializeField] private float fadeInEndY = 278f;
        [SerializeField] private float fadeOutStartY = 352f;
        [SerializeField] private float fadeOutEndY = 372f;

        private ParticleSystem _ps;
        private UnityCamera _cam;

        private void OnEnable()
        {
            _ps = GetComponent<ParticleSystem>();
            ApplyStatic();
        }

        private void OnValidate()
        {
            _ps = GetComponent<ParticleSystem>();
            if (_ps == null) return;
            ApplyStatic();
        }

        private void LateUpdate()
        {
            if (_ps == null) return;
            // Never written back to targetCamera: a serialized write per ExecuteAlways tick dirties the scene.
            _cam = targetCamera != null ? targetCamera : UnityCamera.main;
            if (_cam == null) return;
            UpdateTransform();
            UpdateBox();
        }

        // World space, else the drops ride the camera instead of only the spawn box moving.
        private void ApplyStatic()
        {
            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var shape = _ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;
        }

        private void UpdateTransform()
        {
            Vector3 cam = _cam.transform.position;
            transform.SetPositionAndRotation(new Vector3(cam.x, cam.y, layerZ), Quaternion.identity);
        }

        private void UpdateBox()
        {
            float halfH = ViewHalfHeight();
            float w = halfH * _cam.aspect * 2f * widthMultiplier;
            float h = halfH * 2f * heightMultiplier;

            var shape = _ps.shape;
            shape.scale = new Vector3(w, h, 0f);

            var emission = _ps.emission;
            float rate = dropsPerSquareUnit * w * h * BandKeep(transform.position.y, h);
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(rate);
        }

        // Signed distance: a layer behind the camera would otherwise mirror into a positive box size.
        private float ViewHalfHeight()
        {
            if (_cam.orthographic) return _cam.orthographicSize;
            float dist = layerZ - _cam.transform.position.z;
            return Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(dist, 0.01f);
        }

        // Averaged across the box: center sampling makes a box taller than a ramp jump between full and dead.
        private float BandKeep(float centerY, float boxHeight)
        {
            float sum = 0f;
            for (int i = 0; i < 5; i++)
                sum += KeepAt(centerY + boxHeight * (i * 0.25f - 0.5f));
            return sum * 0.2f;
        }

        private float KeepAt(float worldY)
        {
            float rise = Mathf.InverseLerp(fadeInStartY, fadeInEndY, worldY);
            float fall = 1f - Mathf.InverseLerp(fadeOutStartY, fadeOutEndY, worldY);
            return Mathf.Min(rise, fall);
        }

        private void OnDrawGizmosSelected()
        {
            _cam = targetCamera != null ? targetCamera : UnityCamera.main;
            if (_cam == null) return;
            float halfH = ViewHalfHeight();
            float halfW = halfH * _cam.aspect;
            Vector3 c = new Vector3(_cam.transform.position.x, _cam.transform.position.y, layerZ);

            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.8f);
            DrawRect(c, halfW * 2f * widthMultiplier, halfH * 2f * heightMultiplier);

            float spanX = halfW * 2f * widthMultiplier;
            DrawBandLine(c.x, spanX, fadeInStartY, new Color(1f, 0.4f, 0.35f, 0.9f));
            DrawBandLine(c.x, spanX, fadeInEndY, new Color(0.4f, 1f, 0.6f, 0.9f));
            DrawBandLine(c.x, spanX, fadeOutStartY, new Color(0.4f, 1f, 0.6f, 0.9f));
            DrawBandLine(c.x, spanX, fadeOutEndY, new Color(1f, 0.4f, 0.35f, 0.9f));
        }

        private static void DrawBandLine(float centerX, float span, float y, Color color)
        {
            Gizmos.color = color;
            Gizmos.DrawLine(new Vector3(centerX - span * 0.5f, y, 0f), new Vector3(centerX + span * 0.5f, y, 0f));
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
    }
}
