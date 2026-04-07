using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeJumping.Input
{
    public class SlimeInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;

        public Vector2 MoveInput { get; private set; }
        /// <summary>Last-input-wins variant. Use for air control so charging→airborne transitions feel responsive.</summary>
        public Vector2 LastInputMove { get; private set; }
        public bool IsJumpHeld { get; private set; }

        public event Action OnJumpPressed;
        public event Action OnJumpReleased;
        public event Action OnGroundDashPressed;

        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction groundDashAction;

        // Last-input-wins tracking
        private bool prevLeft, prevRight, prevUp, prevDown;
        private float lastHDir, lastVDir;

        private void OnEnable()
        {
            var playerMap = inputActionAsset.FindActionMap("Player");
            moveAction = playerMap.FindAction("Move");
            jumpAction = playerMap.FindAction("Jump");
            groundDashAction = playerMap.FindAction("GroundDash");

            jumpAction.started += HandleJumpStarted;
            jumpAction.canceled += HandleJumpCanceled;
            groundDashAction.started += HandleGroundDashStarted;

            playerMap.Enable();
        }

        private void OnDisable()
        {
            jumpAction.started -= HandleJumpStarted;
            jumpAction.canceled -= HandleJumpCanceled;
            groundDashAction.started -= HandleGroundDashStarted;

            inputActionAsset.FindActionMap("Player").Disable();
        }

        private void Update()
        {
            MoveInput = moveAction.ReadValue<Vector2>();

            var kb = Keyboard.current;
            if (kb == null) { LastInputMove = MoveInput; return; }

            bool left = kb.aKey.isPressed, right = kb.dKey.isPressed;
            bool up = kb.wKey.isPressed, down = kb.sKey.isPressed;

            if (left && !prevLeft) lastHDir = -1f;
            if (right && !prevRight) lastHDir = 1f;
            if (down && !prevDown) lastVDir = -1f;
            if (up && !prevUp) lastVDir = 1f;

            float hx = (left && right) ? lastHDir : left ? -1f : right ? 1f : 0f;
            float vy = (down && up) ? lastVDir : down ? -1f : up ? 1f : 0f;
            LastInputMove = new Vector2(hx, vy);

            prevLeft = left; prevRight = right; prevUp = up; prevDown = down;
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

        private void HandleGroundDashStarted(InputAction.CallbackContext ctx)
        {
            OnGroundDashPressed?.Invoke();
        }
    }
}
