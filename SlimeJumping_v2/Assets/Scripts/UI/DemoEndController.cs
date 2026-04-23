using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Drives the EndScene demo-end page.
    /// Optional "Return to Menu" button; any key press also returns to the main menu.
    public class DemoEndController : MonoBehaviour
    {
        [SerializeField] private Button returnButton;
        [Tooltip("Seconds before any-key-to-return is accepted, so the scene-reveal fade can complete first.")]
        [SerializeField] private float inputDelay = 1.0f;

        private float acceptInputAt;

        private void Awake()
        {
            if (returnButton != null)
                returnButton.onClick.AddListener(ReturnToMenu);
        }

        private void Start()
        {
            acceptInputAt = Time.unscaledTime + inputDelay;
        }

        private void Update()
        {
            if (Time.unscaledTime < acceptInputAt) return;
            if (Input.anyKeyDown) ReturnToMenu();
        }

        public void ReturnToMenu()
        {
            GameFlow.LoadMainMenu();
        }
    }
}
