using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Settings;
using SlimeJumping.UI.Animation;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Options UI. Every setting reads/writes through GameOptions; controls are
    /// direct [SerializeField] refs populated by the setup script.
    public class OptionsPanel : UIPanel
    {
        [Header("Frame Limit")]
        [SerializeField] private Button frameLimitPrevButton;
        [SerializeField] private Button frameLimitNextButton;
        [SerializeField] private BumpableLabel frameLimitValueLabel;

        [Header("VSync")]
        [SerializeField] private Button vsyncPrevButton;
        [SerializeField] private Button vsyncNextButton;
        [SerializeField] private BumpableLabel vsyncValueLabel;

        [Header("Audio")]
        [SerializeField] private Slider playerSfxVolumeSlider;
        [SerializeField] private BumpableLabel playerSfxVolumeValueLabel;
        [SerializeField] private Slider worldSfxVolumeSlider;
        [SerializeField] private BumpableLabel worldSfxVolumeValueLabel;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private BumpableLabel bgmVolumeValueLabel;
        [SerializeField] private Slider uiVolumeSlider;
        [SerializeField] private BumpableLabel uiVolumeValueLabel;

        [Header("Display")]
        [SerializeField] private Button windowModePrevButton;
        [SerializeField] private Button windowModeNextButton;
        [SerializeField] private BumpableLabel windowModeValueLabel;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Button backgroundCameraPrevButton;
        [SerializeField] private Button backgroundCameraNextButton;
        [SerializeField] private BumpableLabel backgroundCameraValueLabel;

        [Header("Actions")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button newGameButton;

        [Header("Backdrop")]
        [SerializeField] private Graphic boxGraphic;

        private int frameLimitIndex;
        private int windowModeIndex;
        private int backgroundCameraIndex;
        private int vsyncIndex;
        private static readonly WindowMode[] WindowModes = { WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen };
        private static readonly bool[] ToggleStates = { false, true };

        protected override void OnAfterAwake()
        {
            frameLimitIndex = IndexOfFrameLimit(GameOptions.FrameLimit);
            UpdateFrameLimitLabel();
            if (frameLimitPrevButton != null) frameLimitPrevButton.onClick.AddListener(() => CycleFrameLimit(-1));
            if (frameLimitNextButton != null) frameLimitNextButton.onClick.AddListener(() => CycleFrameLimit(+1));

            vsyncIndex = GameOptions.VSyncEnabled ? 1 : 0;
            UpdateVSyncLabel();
            if (vsyncPrevButton != null) vsyncPrevButton.onClick.AddListener(() => CycleVSync(-1));
            if (vsyncNextButton != null) vsyncNextButton.onClick.AddListener(() => CycleVSync(+1));

            BindSlider(playerSfxVolumeSlider, GameOptions.PlayerSfxVolume, OnPlayerSfxVolumeChanged);
            UpdatePercentLabel(playerSfxVolumeValueLabel, GameOptions.PlayerSfxVolume);

            BindSlider(worldSfxVolumeSlider, GameOptions.WorldSfxVolume, OnWorldSfxVolumeChanged);
            UpdatePercentLabel(worldSfxVolumeValueLabel, GameOptions.WorldSfxVolume);

            BindSlider(bgmVolumeSlider, GameOptions.BgmVolume, OnBgmVolumeChanged);
            UpdatePercentLabel(bgmVolumeValueLabel, GameOptions.BgmVolume);

            BindSlider(uiVolumeSlider, GameOptions.UiVolume, OnUiVolumeChanged);
            UpdatePercentLabel(uiVolumeValueLabel, GameOptions.UiVolume);

            windowModeIndex = IndexOfWindowMode(GameOptions.WindowMode);
            UpdateWindowModeLabel();
            if (windowModePrevButton != null) windowModePrevButton.onClick.AddListener(() => CycleWindowMode(-1));
            if (windowModeNextButton != null) windowModeNextButton.onClick.AddListener(() => CycleWindowMode(+1));

            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                var opts = new List<TMP_Dropdown.OptionData>(GameOptions.ResolutionOptions.Length);
                for (int i = 0; i < GameOptions.ResolutionOptions.Length; i++)
                {
                    var r = GameOptions.ResolutionOptions[i];
                    opts.Add(new TMP_Dropdown.OptionData(r.x + " x " + r.y));
                }
                resolutionDropdown.AddOptions(opts);
                resolutionDropdown.SetValueWithoutNotify(GameOptions.ResolutionIndex);
                resolutionDropdown.RefreshShownValue();
                resolutionDropdown.onValueChanged.AddListener(OnResolutionDropdownChanged);
            }

            backgroundCameraIndex = GameOptions.BackgroundCameraEnabled ? 1 : 0;
            UpdateBackgroundCameraLabel();
            if (backgroundCameraPrevButton != null) backgroundCameraPrevButton.onClick.AddListener(() => CycleBackgroundCamera(-1));
            if (backgroundCameraNextButton != null) backgroundCameraNextButton.onClick.AddListener(() => CycleBackgroundCamera(+1));

            bool inMenuScene = SceneManager.GetActiveScene().name == GameFlow.MainMenuScene;

            if (resumeButton != null) resumeButton.onClick.AddListener(HandleResume);
            if (quitButton != null)
            {
                // 메인 메뉴에서 연 옵션창엔 무의미한 같은 씬 재로드다.
                quitButton.gameObject.SetActive(!inMenuScene);
                quitButton.onClick.AddListener(GameFlow.LoadMainMenu);
            }
            if (newGameButton != null) newGameButton.onClick.AddListener(GameFlow.NewGame);

            // 메뉴 씬은 판 뒤가 게임이 아니라 메뉴 버튼이라, 비치면 겹쳐 읽힌다.
            if (inMenuScene && boxGraphic != null)
            {
                var c = boxGraphic.color;
                c.a = 1f;
                boxGraphic.color = c;
            }
        }

        private void HandleResume()
        {
            if (UIManager.Instance != null) UIManager.Instance.Close(this);
            else Close();
        }

        protected override void OnDestroy()
        {
            // OnAfterAwake listeners are never cleared elsewhere; leaving them retains `this`.
            if (frameLimitPrevButton != null) frameLimitPrevButton.onClick.RemoveAllListeners();
            if (frameLimitNextButton != null) frameLimitNextButton.onClick.RemoveAllListeners();
            if (vsyncPrevButton != null) vsyncPrevButton.onClick.RemoveAllListeners();
            if (vsyncNextButton != null) vsyncNextButton.onClick.RemoveAllListeners();
            if (playerSfxVolumeSlider != null) playerSfxVolumeSlider.onValueChanged.RemoveAllListeners();
            if (worldSfxVolumeSlider != null) worldSfxVolumeSlider.onValueChanged.RemoveAllListeners();
            if (bgmVolumeSlider != null) bgmVolumeSlider.onValueChanged.RemoveAllListeners();
            if (uiVolumeSlider != null) uiVolumeSlider.onValueChanged.RemoveAllListeners();
            if (windowModePrevButton != null) windowModePrevButton.onClick.RemoveAllListeners();
            if (windowModeNextButton != null) windowModeNextButton.onClick.RemoveAllListeners();
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.RemoveAllListeners();
            if (backgroundCameraPrevButton != null) backgroundCameraPrevButton.onClick.RemoveAllListeners();
            if (backgroundCameraNextButton != null) backgroundCameraNextButton.onClick.RemoveAllListeners();
            if (resumeButton != null) resumeButton.onClick.RemoveAllListeners();
            if (quitButton != null) quitButton.onClick.RemoveAllListeners();
            if (newGameButton != null) newGameButton.onClick.RemoveAllListeners();
            base.OnDestroy();
        }

        private static void BindSlider(Slider slider, float initial, UnityEngine.Events.UnityAction<float> onChanged)
        {
            if (slider == null) return;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(initial);
            slider.onValueChanged.AddListener(onChanged);
        }

        private void OnPlayerSfxVolumeChanged(float v)
        {
            GameOptions.SetPlayerSfxVolume(v);
            UpdatePercentLabel(playerSfxVolumeValueLabel, v);
        }

        private void OnWorldSfxVolumeChanged(float v)
        {
            GameOptions.SetWorldSfxVolume(v);
            UpdatePercentLabel(worldSfxVolumeValueLabel, v);
        }

        private void OnBgmVolumeChanged(float v)
        {
            GameOptions.SetBgmVolume(v);
            UpdatePercentLabel(bgmVolumeValueLabel, v);
        }

        private void OnUiVolumeChanged(float v)
        {
            GameOptions.SetUiVolume(v);
            UpdatePercentLabel(uiVolumeValueLabel, v);
        }

        private void CycleFrameLimit(int dir)
        {
            int len = GameOptions.FrameLimitOptions.Length;
            frameLimitIndex = ((frameLimitIndex + dir) % len + len) % len;
            GameOptions.SetFrameLimit(GameOptions.FrameLimitOptions[frameLimitIndex]);
            UpdateFrameLimitLabel();
        }

        private void CycleWindowMode(int dir)
        {
            int len = WindowModes.Length;
            windowModeIndex = ((windowModeIndex + dir) % len + len) % len;
            GameOptions.SetWindowMode(WindowModes[windowModeIndex]);
            UpdateWindowModeLabel();
        }

        private void CycleBackgroundCamera(int dir)
        {
            int len = ToggleStates.Length;
            backgroundCameraIndex = ((backgroundCameraIndex + dir) % len + len) % len;
            GameOptions.SetBackgroundCameraEnabled(ToggleStates[backgroundCameraIndex]);
            UpdateBackgroundCameraLabel();
        }

        private void CycleVSync(int dir)
        {
            int len = ToggleStates.Length;
            vsyncIndex = ((vsyncIndex + dir) % len + len) % len;
            GameOptions.SetVSyncEnabled(ToggleStates[vsyncIndex]);
            UpdateVSyncLabel();
        }

        private void OnResolutionDropdownChanged(int index)
        {
            int clamped = Mathf.Clamp(index, 0, GameOptions.ResolutionOptions.Length - 1);
            GameOptions.SetResolutionIndex(clamped);
        }

        private void UpdateFrameLimitLabel()
        {
            if (frameLimitValueLabel == null) return;
            int fps = GameOptions.FrameLimitOptions[frameLimitIndex];
            frameLimitValueLabel.Set(fps <= 0 ? "Unlimited" : fps + " FPS");
        }

        private static void UpdatePercentLabel(BumpableLabel label, float v)
        {
            if (label == null) return;
            label.Set(Mathf.RoundToInt(v * 100f) + "%");
        }

        private void UpdateWindowModeLabel()
        {
            if (windowModeValueLabel == null) return;
            string text;
            switch (WindowModes[windowModeIndex])
            {
                case WindowMode.Windowed: text = "Windowed"; break;
                case WindowMode.Borderless: text = "Borderless"; break;
                default: text = "Fullscreen"; break;
            }
            windowModeValueLabel.Set(text);
        }

        private void UpdateBackgroundCameraLabel()
        {
            if (backgroundCameraValueLabel == null) return;
            backgroundCameraValueLabel.Set(ToggleStates[backgroundCameraIndex] ? "On" : "Off");
        }

        private void UpdateVSyncLabel()
        {
            if (vsyncValueLabel == null) return;
            vsyncValueLabel.Set(ToggleStates[vsyncIndex] ? "On" : "Off");
        }

        private static int IndexOfFrameLimit(int fps)
        {
            for (int i = 0; i < GameOptions.FrameLimitOptions.Length; i++)
                if (GameOptions.FrameLimitOptions[i] == fps) return i;
            return 1; // 60 default
        }

        private static int IndexOfWindowMode(WindowMode m)
        {
            for (int i = 0; i < WindowModes.Length; i++)
                if (WindowModes[i] == m) return i;
            return 1;
        }
    }
}