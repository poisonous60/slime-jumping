using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    public class CameraProjectionToggle : MonoBehaviour
    {
        [SerializeField] private CinemachineCamera vcam;
        [SerializeField] private float orthographicSize = 10f;
        [SerializeField] private float perspectiveFov = 40f;
        private readonly Key _toggleKey = Key.F5;

        private bool _isOrthographic;

        private void Update()
        {
            if (Keyboard.current[_toggleKey].wasPressedThisFrame)
                Toggle();
        }

        private void Toggle()
        {
            _isOrthographic = !_isOrthographic;
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
        }
    }
}
