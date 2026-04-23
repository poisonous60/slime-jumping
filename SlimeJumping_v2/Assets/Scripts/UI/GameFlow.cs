using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimeJumping.UI
{
    public static class GameFlow
    {
        public const string MainMenuScene = "MainMenuScene";
        public const string GameplayScene = "MainScene";

        public static void LoadMainMenu() => SceneManager.LoadScene(MainMenuScene);

        public static void StartGame() => SceneManager.LoadScene(GameplayScene);

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
