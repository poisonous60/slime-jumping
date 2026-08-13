using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Buttons resolved by UISlot tag, so prefab hierarchy changes don't break wiring.
    public class MainMenuPanel : UIPanel
    {
        private Button startButton;
        private Button loadButton;
        private Button optionsButton;
        private Button quitButton;

        protected override void OnAfterAwake()
        {
            startButton = FindSlot<Button>(UISlotType.StartButton);
            loadButton = FindSlot<Button>(UISlotType.LoadButton);
            optionsButton = FindSlot<Button>(UISlotType.OptionsButton);
            quitButton = FindSlot<Button>(UISlotType.QuitButton);

            if (startButton != null) startButton.onClick.AddListener(HandleStart);
            if (loadButton != null)
            {
                loadButton.gameObject.SetActive(LoadSaveManager.HasSave);
                loadButton.onClick.AddListener(HandleLoad);
            }
            if (optionsButton != null) optionsButton.onClick.AddListener(HandleOptions);
            if (quitButton != null) quitButton.onClick.AddListener(HandleQuit);
        }

        private static void HandleStart() => GameFlow.NewGame();

        private static void HandleLoad() => GameFlow.LoadGame();
        private static void HandleQuit() => GameFlow.QuitGame();

        private void HandleOptions()
        {
            var options = UIManager.Instance != null ? UIManager.Instance.Get<OptionsPanel>() : null;
            if (options != null) UIManager.Instance.Open(options);
        }

        protected override void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveAllListeners();
            if (loadButton != null) loadButton.onClick.RemoveAllListeners();
            if (optionsButton != null) optionsButton.onClick.RemoveAllListeners();
            if (quitButton != null) quitButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }
    }
}
