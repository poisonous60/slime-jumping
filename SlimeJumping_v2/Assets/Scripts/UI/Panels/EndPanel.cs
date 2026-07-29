using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Any-key-to-return after a short input delay, so the scene-reveal fade finishes before a stray keypress fires.
    public class EndPanel : UIPanel
    {
        [SerializeField, Min(0f)] private float inputDelay = 1.0f;

        private Button returnButton;
        private float acceptInputAt;

        protected override void OnAfterAwake()
        {
            returnButton = FindSlot<Button>(UISlotType.ReturnButton);
            if (returnButton != null) returnButton.onClick.AddListener(ReturnToMenu);
        }

        protected override void OnAfterOpen()
        {
            acceptInputAt = Time.unscaledTime + inputDelay;
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (Time.unscaledTime < acceptInputAt) return;
            if (AnyKeyPressedThisFrame()) ReturnToMenu();
        }

        private static bool AnyKeyPressedThisFrame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse != null && (
                mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame ||
                mouse.middleButton.wasPressedThisFrame)) return true;

            var pad = Gamepad.current;
            if (pad != null)
            {
                foreach (var ctrl in pad.allControls)
                    if (ctrl is ButtonControl btn && btn.wasPressedThisFrame) return true;
            }
            return false;
        }

        public void ReturnToMenu() => GameFlow.LoadMainMenu();

        protected override void OnDestroy()
        {
            if (returnButton != null) returnButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
