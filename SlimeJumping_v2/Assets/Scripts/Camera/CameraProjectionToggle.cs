using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    [DefaultExecutionOrder(10000)]
    public class CameraProjectionToggle : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera vcam;
        [SerializeField] private UnityEngine.Camera targetCamera;
        [SerializeField] private float orthographicSize = 10f;
        [SerializeField] private float perspectiveFov = 40f;
        [SerializeField] private float transitionDuration = 0.5f;
        [SerializeField] private Key toggleKey = Key.F5;

        private bool _isOrthographic;
        private bool _transitioning;
        private float _transitionT;

        private void Awake()
        {
            if (targetCamera == null)
            {
                var brain = FindAnyObjectByType<CinemachineBrain>();
                if (brain != null) targetCamera = brain.GetComponent<UnityEngine.Camera>();
                if (targetCamera == null) targetCamera = UnityEngine.Camera.main;
            }
        }

        private void Update()
        {
            if (Keyboard.current[toggleKey].wasPressedThisFrame)
                Toggle();
        }

        private void Toggle()
        {
            _isOrthographic = !_isOrthographic;

            var lens = vcam.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Perspective;
            lens.FieldOfView = perspectiveFov;
            vcam.Lens = lens;

            _transitioning = true;
            _transitionT = 0f;
        }

        private void LateUpdate()
        {
            if (!_transitioning || targetCamera == null) return;

            _transitionT += Time.deltaTime / Mathf.Max(0.0001f, transitionDuration);
            float t = Mathf.Clamp01(_transitionT);
            float s = Mathf.SmoothStep(0f, 1f, t);
            float blend = _isOrthographic ? s : 1f - s;

            float aspect = targetCamera.aspect;
            float near = targetCamera.nearClipPlane;
            float far = targetCamera.farClipPlane;

            Matrix4x4 pers = Matrix4x4.Perspective(perspectiveFov, aspect, near, far);
            Matrix4x4 ortho = Matrix4x4.Ortho(
                -orthographicSize * aspect, orthographicSize * aspect,
                -orthographicSize, orthographicSize, near, far);

            targetCamera.projectionMatrix = LerpMatrix(pers, ortho, blend);

            if (t >= 1f)
            {
                _transitioning = false;

                var lens = vcam.Lens;
                if (_isOrthographic)
                {
                    lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
                    lens.OrthographicSize = orthographicSize;
                }
                else
                {
                    lens.ModeOverride = LensSettings.OverrideModes.Perspective;
                    lens.FieldOfView = perspectiveFov;
                }
                vcam.Lens = lens;
                targetCamera.ResetProjectionMatrix();
            }
        }

        private static Matrix4x4 LerpMatrix(Matrix4x4 a, Matrix4x4 b, float t)
        {
            Matrix4x4 r = new Matrix4x4();
            for (int i = 0; i < 16; i++)
                r[i] = Mathf.Lerp(a[i], b[i], t);
            return r;
        }
    }
}
