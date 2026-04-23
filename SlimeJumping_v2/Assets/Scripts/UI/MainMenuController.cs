using UnityEngine;

namespace SlimeJumping.UI
{
    public class MainMenuController : MonoBehaviour
    {
        public void OnStartClicked() => GameFlow.StartGame();

        public void OnQuitClicked() => GameFlow.QuitGame();
    }
}
