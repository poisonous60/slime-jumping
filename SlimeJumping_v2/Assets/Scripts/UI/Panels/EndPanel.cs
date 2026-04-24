using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Replaces DemoEndController. Any-key-to-return after a short input delay
    /// so the scene-reveal fade can finish before a stray keypress fires.
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
            if (Input.anyKeyDown) ReturnToMenu();
        }

        public void ReturnToMenu() => GameFlow.LoadMainMenu();
    }
}
