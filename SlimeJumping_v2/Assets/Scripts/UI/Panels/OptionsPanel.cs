using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Settings;
using SlimeJumping.UI.Framework;

namespace SlimeJumping.UI.Panels
{
    /// Replaces OptionsPanelController. All PlayerPrefs wiring is still routed
    /// through GameOptions — this is purely a presentation layer swap.
    /// Controls are direct [SerializeField] refs (the setup script populates
    /// them), which keeps the inspector wiring obvious and the Options layout
    /// free to rearrange rows without schema changes.
    public class OptionsPanel : UIPanel
    {
        [Header("Frame Limit")]
        [SerializeField] private Button frameLimitPrevButton;
        [SerializeField] private Button frameLimitNextButton;
        [SerializeField] private TextMeshProUGUI frameLimitValueLabel;

        [Header("Audio")]
        [SerializeField] private Slider playerSfxVolumeSlider;
        [SerializeField] private TextMeshProUGUI playerSfxVolumeValueLabel;
        [SerializeField] private Slider worldSfxVolumeSlider;
        [SerializeField] private TextMeshProUGUI worldSfxVolumeValueLabel;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private TextMeshProUGUI bgmVolumeValueLabel;
        [SerializeField] private Slider uiVolumeSlider;
        [SerializeField] private TextMeshProUGUI uiVolumeValueLabel;

        [Header("Display")]
        [SerializeField] private Button windowModePrevButton;
        [SerializeField] private Button windowModeNextButton;
        [SerializeField] private TextMeshProUGUI windowModeValueLabel;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Button backgroundCameraPrevButton;
        [SerializeField] private Button backgroundCameraNextButton;
        [SerializeField] private TextMeshProUGUI backgroundCameraValueLabel;

        [Header("Actions")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button quitButton;

        private int frameLimitIndex;
        private int windowModeIndex;
        private int backgroundCameraIndex;
        private static readonly WindowMode[] WindowModes = { WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen };
        private static readonly bool[] BackgroundCameraStates = { false, true };

        protected override void OnAfterAwake()
        {
            frameLimitIndex = IndexOfFrameLimit(GameOptions.FrameLimit);
            UpdateFrameLimitLabel();
            if (frameLimitPrevButton != null) frameLimitPrevButton.onClick.AddListener(() => CycleFrameLimit(-1));
            if (frameLimitNextButton != null) frameLimitNextButton.onClick.AddListener(() => CycleFrameLimit(+1));

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

            backgroundCameraIndex = IndexOfBackgroundCamera(GameOptions.BackgroundCameraEnabled);
            UpdateBackgroundCameraLabel();
            if (backgroundCameraPrevButton != null) backgroundCameraPrevButton.onClick.AddListener(() => CycleBackgroundCamera(-1));
            if (backgroundCameraNextButton != null) backgroundCameraNextButton.onClick.AddListener(() => CycleBackgroundCamera(+1));

            if (closeButton != null) closeButton.onClick.AddListener(HandleClose);
            if (quitButton != null) quitButton.onClick.AddListener(GameFlow.QuitGame);
        }

        private void HandleClose()
        {
            if (UIManager.Instance != null) UIManager.Instance.Close(this);
            else Close();
        }

        protected override void OnDestroy()
        {
            // Drop all editor-wired listeners so the delegates don't retain `this`.
            // These are attached in OnAfterAwake and never otherwise cleared.
            if (frameLimitPrevButton != null) frameLimitPrevButton.onClick.RemoveAllListeners();
            if (frameLimitNextButton != null) frameLimitNextButton.onClick.RemoveAllListeners();
            if (playerSfxVolumeSlider != null) playerSfxVolumeSlider.onValueChanged.RemoveAllListeners();
            if (worldSfxVolumeSlider != null) worldSfxVolumeSlider.onValueChanged.RemoveAllListeners();
            if (bgmVolumeSlider != null) bgmVolumeSlider.onValueChanged.RemoveAllListeners();
            if (uiVolumeSlider != null) uiVolumeSlider.onValueChanged.RemoveAllListeners();
            if (windowModePrevButton != null) windowModePrevButton.onClick.RemoveAllListeners();
            if (windowModeNextButton != null) windowModeNextButton.onClick.RemoveAllListeners();
            if (resolutionDropdown != null) resolutionDropdown.onValueChanged.RemoveAllListeners();
            if (backgroundCameraPrevButton != null) backgroundCameraPrevButton.onClick.RemoveAllListeners();
            if (backgroundCameraNextButton != null) backgroundCameraNextButton.onClick.RemoveAllListeners();
            if (closeButton != null) closeButton.onClick.RemoveAllListeners();
            if (quitButton != null) quitButton.onClick.RemoveAllListeners();
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
            int len = BackgroundCameraStates.Length;
            backgroundCameraIndex = ((backgroundCameraIndex + dir) % len + len) % len;
            GameOptions.SetBackgroundCameraEnabled(BackgroundCameraStates[backgroundCameraIndex]);
            UpdateBackgroundCameraLabel();
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
            frameLimitValueLabel.text = fps <= 0 ? "Unlimited" : fps + " FPS";
        }

        private static void UpdatePercentLabel(TextMeshProUGUI label, float v)
        {
            if (label == null) return;
            label.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void UpdateWindowModeLabel()
        {
            if (windowModeValueLabel == null) return;
            switch (WindowModes[windowModeIndex])
            {
                case WindowMode.Windowed: windowModeValueLabel.text = "Windowed"; break;
                case WindowMode.Borderless: windowModeValueLabel.text = "Borderless"; break;
                default: windowModeValueLabel.text = "Fullscreen"; break;
            }
        }

        private void UpdateBackgroundCameraLabel()
        {
            if (backgroundCameraValueLabel == null) return;
            backgroundCameraValueLabel.text = BackgroundCameraStates[backgroundCameraIndex] ? "On" : "Off";
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

        private static int IndexOfBackgroundCamera(bool on)
        {
            for (int i = 0; i < BackgroundCameraStates.Length; i++)
                if (BackgroundCameraStates[i] == on) return i;
            return 1;
        }
    }
}
