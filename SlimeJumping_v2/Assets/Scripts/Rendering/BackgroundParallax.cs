using UnityEngine;

namespace SlimeJumping.Rendering
{
    [ExecuteAlways]
    public class BackgroundParallax : MonoBehaviour
    {
        [Tooltip("0 = camera-locked (infinitely far), 1 = world-locked (same as collision), >1 = foreground")]
        [Range(-0.5f, 2f)]
        public float parallaxFactorX = 0.1f;

        [Range(-0.5f, 2f)]
        public float parallaxFactorY = 0.1f;

        [Tooltip("Leave empty to use Camera.main")]
        public Transform cameraTransform;

        Vector3 _initialPosition;
        Vector3 _initialCameraPosition;
        bool _captured;

        void OnEnable()
        {
            if (cameraTransform == null)
            {
                var cam = UnityEngine.Camera.main;
                if (cam != null) cameraTransform = cam.transform;
            }
            CaptureBase();
        }

        public void CaptureBase()
        {
            _initialPosition = transform.position;
            if (cameraTransform != null)
            {
                _initialCameraPosition = cameraTransform.position;
                _captured = true;
            }
        }

        void LateUpdate()
        {
            if (cameraTransform == null || !_captured) return;
            Vector3 camDelta = cameraTransform.position - _initialCameraPosition;
            Vector3 newPos = _initialPosition;
            newPos.x += camDelta.x * (1f - parallaxFactorX);
            newPos.y += camDelta.y * (1f - parallaxFactorY);
            newPos.z = _initialPosition.z;
            transform.position = newPos;
        }

        void OnValidate()
        {
            if (Application.isPlaying) return;
            _initialPosition = transform.position;
            if (cameraTransform != null)
            {
                _initialCameraPosition = cameraTransform.position;
                _captured = true;
            }
        }
    }
}
