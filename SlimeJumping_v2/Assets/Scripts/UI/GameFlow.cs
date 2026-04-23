using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.UI
{
    public static class GameFlow
    {
        public const string MainMenuScene = "MainMenuScene";
        public const string GameplayScene = "MainScene";

        // Read by SlimeStartStateManager so the start-interaction only fires after
        // a real menu → gameplay transition, not when MainScene is played directly
        // from the editor for testing.
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
