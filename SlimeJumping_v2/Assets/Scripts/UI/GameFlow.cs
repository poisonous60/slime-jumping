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

        // LoadSaveManager consumes this once in Start.
        public static bool PendingLoad { get; private set; }

        // Domain Reload 를 끄면 statics 가 Play 세션을 넘어 살아남아 다음 진입의 시작 상태를 오염시킨다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            WasStartedFromMenu = false;
            PendingLoad = false;
        }

        public static void LoadMainMenu()
        {
            LoadSaveManager.SaveNow();
            WasStartedFromMenu = false;
            PendingLoad = false;
            SceneManager.LoadScene(MainMenuScene);
        }

        public static void StartGame()
        {
            WasStartedFromMenu = true;
            PendingLoad = false;
            SceneTransitionController.Play(GameplayScene);
        }

        /// WasStartedFromMenu 를 세우지 않는다 — 시작 인터랙션이 복원된 위치를 덮으면 안 된다.
        public static void LoadGame()
        {
            WasStartedFromMenu = false;
            PendingLoad = true;
            SceneTransitionController.Play(GameplayScene);
        }

        public static bool ConsumePendingLoad()
        {
            bool pending = PendingLoad;
            PendingLoad = false;
            return pending;
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
