using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeJumping.Input
{
    public class SlimeInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActionAsset;

        public Vector2 AimInput { get; private set; }
        /// <summary>Last-input-wins variant. Use for air control so charging→airborne transitions feel responsive.</summary>
        public Vector2 LastInputMove { get; private set; }
        public bool IsJumpHeld { get; private set; }
        public bool VerticalUpHeld { get; private set; }
        public bool VerticalDownHeld { get; private set; }

        public event Action OnJumpPressed;
        public event Action OnJumpReleased;
        public event Action OnVerticalUpPressed;
        public event Action OnVerticalDownPressed;

        private InputAction aimAction;
        private InputAction jumpAction;
        private InputAction verticalUpAction;
        private InputAction verticalDownAction;

        // Last-input-wins tracking
        private bool prevLeft, prevRight, prevUp, prevDown;
        private float lastHDir, lastVDir;

        private void OnEnable()
        {
            var playerMap = inputActionAsset.FindActionMap("Player");
            aimAction = playerMap.FindAction("Aim");
            jumpAction = playerMap.FindAction("Jump");
            verticalUpAction = playerMap.FindAction("VerticalUp");
            verticalDownAction = playerMap.FindAction("VerticalDown");

            jumpAction.started += HandleJumpStarted;
            jumpAction.canceled += HandleJumpCanceled;
            verticalUpAction.started += HandleVerticalUpStarted;
            verticalDownAction.started += HandleVerticalDownStarted;

            playerMap.Enable();
        }

        private void OnDisable()
        {
            jumpAction.started -= HandleJumpStarted;
            jumpAction.canceled -= HandleJumpCanceled;
            verticalUpAction.started -= HandleVerticalUpStarted;
            verticalDownAction.started -= HandleVerticalDownStarted;

            inputActionAsset.FindActionMap("Player").Disable();
        }

        private void Update()
        {
            AimInput = aimAction.ReadValue<Vector2>();
            VerticalUpHeld = verticalUpAction.IsPressed();
            VerticalDownHeld = verticalDownAction.IsPressed();

            var kb = Keyboard.current;
            if (kb == null) { LastInputMove = Vector2.zero; return; }

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

        private void HandleVerticalUpStarted(InputAction.CallbackContext ctx)
        {
            OnVerticalUpPressed?.Invoke();
        }

        private void HandleVerticalDownStarted(InputAction.CallbackContext ctx)
        {
            OnVerticalDownPressed?.Invoke();
        }
    }
}
