using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.UI
{
    public static class GameFlow
    {
        public const string MainMenuScene = "MainMenuScene";
        public const string GameplayScene = "MainScene";

        // SlimeStartStateManager reads this so the start interaction is skipped when
        // MainScene is played directly from the editor instead of via the menu.
        public static bool WasStartedFromMenu { get; private set; }

        public static void LoadMainMenu()
        {
            WasStartedFromMenu = false;
            SceneManager.LoadScene(MainMenuScene);
        }

        public static void StartGame()
        {
            WasStartedFromMenu = true;
            SceneTransitionController.Play(GameplayScene);
        }

        public static void QuitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
