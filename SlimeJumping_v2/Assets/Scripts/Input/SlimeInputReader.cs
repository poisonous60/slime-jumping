using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeJumping.Input
{
    public class SlimeInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;

        public Vector2 MoveInput { get; private set; }
        public bool IsJumpHeld { get; private set; }

        public event Action OnJumpPressed;
        public event Action OnJumpReleased;

        private InputAction moveAction;
        private InputAction jumpAction;

        private void OnEnable()
        {
            var playerMap = inputActionAsset.FindActionMap("Player");
            moveAction = playerMap.FindAction("Move");
            jumpAction = playerMap.FindAction("Jump");

            jumpAction.started += HandleJumpStarted;
            jumpAction.canceled += HandleJumpCanceled;

            playerMap.Enable();
        }

        private void OnDisable()
        {
            jumpAction.started -= HandleJumpStarted;
            jumpAction.canceled -= HandleJumpCanceled;

            inputActionAsset.FindActionMap("Player").Disable();
        }

        private void Update()
        {
            MoveInput = moveAction.ReadValue<Vector2>();
        }

        private void HandleJumpStarted(InputAction.CallbackContext ctx)
        {
            IsJumpHeld = true;
            OnJumpPressed?.Invoke();
        }

        private void HandleJumpCanceled(InputAction.CallbackContext ctx)
        {
            IsJumpHeld = false;
            OnJumpReleased?.Invoke();
        }
    }
}
