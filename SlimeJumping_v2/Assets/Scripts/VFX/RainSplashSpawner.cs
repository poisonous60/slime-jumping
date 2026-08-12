using UnityEngine;
using UnityCamera = UnityEngine.Camera;

namespace SlimeJumping.VFX
{
    /// <summary>
    /// Puts rain splashes on whatever ground is under the camera, without any of it being placed by hand.
    /// Each splash casts one ray straight down across the viewport and emits a particle at the first hit,
    /// so a stage with dozens of separate floors needs no per-floor setup and new platforms wet themselves.
    /// Casting from the top of the viewport also gives overhangs for free: the ray stops on the roof, so
    /// ground under cover stays dry. Particles come from one World-space system via Emit, not from pooled
    /// clones, because the splash look is one particle and a GameObject per splash would be all overhead.
    /// The particle system's own emission stays off — this component is its only source.
    /// </summary>
    [AddComponentMenu("SlimeJumping/VFX/Rain Splash Spawner")]
    public sealed class RainSplashSpawner : MonoBehaviour
    {
        [SerializeField] private ParticleSystem splashParticles;

        [SerializeField] private UnityCamera targetCamera;

        [Tooltip("Rain layer whose altitude band gates the splashes, so they stop where the rain stops.")]
        [SerializeField] private RainCameraEmitter rain;

        [Tooltip("Splashes per second per world unit of viewport width.")]
        [Min(0f)] [SerializeField] private float splashesPerUnitWidth = 16.6f;

        [Tooltip("Ray band width = viewport width × this.")]
        [Min(1f)] [SerializeField] private float widthMultiplier = 1.2f;

        [Tooltip("How far above and below the camera the down-ray searches, as a multiple of viewport height.")]
        [Min(0.5f)] [SerializeField] private float heightMultiplier = 1.2f;

        [Tooltip("Layers counted as ground. Left empty it resolves to Environment.")]
        [SerializeField] private LayerMask groundLayers;

        [Tooltip("Splashes sit this far along the surface normal so they are not half-buried.")]
        [SerializeField] private float surfaceOffset = 0.02f;

        private UnityCamera _cam;
        private float _pending;

        // Resolved here, not in the field initializer: LayerMask.GetMask there throws at type init.
        private void Awake()
        {
            if (groundLayers.value == 0) groundLayers = LayerMask.GetMask("Environment");
        }

        private void Update()
        {
            if (splashParticles == null) return;
            _cam = targetCamera != null ? targetCamera : UnityCamera.main;
            if (_cam == null) return;

            float band = rain != null ? rain.BandFactor : 1f;
            if (band <= 0f) { _pending = 0f; return; }

            float halfH = ViewHalfHeight();
            float halfW = halfH * _cam.aspect * widthMultiplier;
            float reach = halfH * heightMultiplier;

            _pending += splashesPerUnitWidth * halfW * 2f * band * Time.deltaTime;
            int count = Mathf.FloorToInt(_pending);
            _pending -= count;

            Vector3 cam = _cam.transform.position;
            for (int i = 0; i < count; i++)
            {
                var origin = new Vector2(cam.x + Random.Range(-halfW, halfW), cam.y + reach);
                var hit = Physics2D.Raycast(origin, Vector2.down, reach * 2f, groundLayers);
                // A zero-distance hit means the ray began inside a collider, where there is no surface to wet.
                if (hit.collider == null || hit.distance <= 0.01f) continue;
                splashParticles.Emit(new ParticleSystem.EmitParams
                {
                    position = hit.point + hit.normal * surfaceOffset,
                    applyShapeToPosition = false
                }, 1);
            }
        }

        private float ViewHalfHeight()
        {
            if (_cam.orthographic) return _cam.orthographicSize;
            float dist = Mathf.Max(-_cam.transform.position.z, 0.01f);
            return Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * dist;
        }

        private void OnDrawGizmosSelected()
        {
            _cam = targetCamera != null ? targetCamera : UnityCamera.main;
            if (_cam == null) return;
            float halfH = ViewHalfHeight();
            float halfW = halfH * _cam.aspect * widthMultiplier;
            float reach = halfH * heightMultiplier;
            Vector3 cam = _cam.transform.position;

            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.7f);
            Gizmos.DrawLine(new Vector3(cam.x - halfW, cam.y + reach, 0f), new Vector3(cam.x + halfW, cam.y + reach, 0f));
            Gizmos.DrawLine(new Vector3(cam.x - halfW, cam.y - reach, 0f), new Vector3(cam.x + halfW, cam.y - reach, 0f));
            Gizmos.DrawLine(new Vector3(cam.x - halfW, cam.y + reach, 0f), new Vector3(cam.x - halfW, cam.y - reach, 0f));
            Gizmos.DrawLine(new Vector3(cam.x + halfW, cam.y + reach, 0f), new Vector3(cam.x + halfW, cam.y - reach, 0f));
        }
    }
}
