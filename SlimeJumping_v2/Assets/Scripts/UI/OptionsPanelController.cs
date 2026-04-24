using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Settings;

namespace SlimeJumping.UI
{
    /// Toggles an in-scene Options panel with ESC (without pausing time) and
    /// binds its slider/cycler controls to the global GameOptions store.
    public class OptionsPanelController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Button frameLimitPrevButton;
        [SerializeField] private Button frameLimitNextButton;
        [SerializeField] private TextMeshProUGUI frameLimitValueLabel;
        [FormerlySerializedAs("sfxVolumeSlider")]
        [SerializeField] private Slider playerSfxVolumeSlider;
        [FormerlySerializedAs("sfxVolumeValueLabel")]
        [SerializeField] private TextMeshProUGUI playerSfxVolumeValueLabel;
        [FormerlySerializedAs("bgmVolumeSlider")]
        [SerializeField] private Slider worldSfxVolumeSlider;
        [FormerlySerializedAs("bgmVolumeValueLabel")]
        [SerializeField] private TextMeshProUGUI worldSfxVolumeValueLabel;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private TextMeshProUGUI bgmVolumeValueLabel;
        [SerializeField] private Slider uiVolumeSlider;
        [SerializeField] private TextMeshProUGUI uiVolumeValueLabel;
        [SerializeField] private Button windowModePrevButton;
        [SerializeField] private Button windowModeNextButton;
        [SerializeField] private TextMeshProUGUI windowModeValueLabel;
        [SerializeField] private TMP_Dropdown resolutionDropdown;
        [SerializeField] private Button backgroundCameraPrevButton;
        [SerializeField] private Button backgroundCameraNextButton;
        [SerializeField] private TextMeshProUGUI backgroundCameraValueLabel;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button quitButton;

        [Header("Behavior")]
        [Tooltip("Toggle open/close with the Escape key while this component is enabled.")]
        [SerializeField] private bool toggleWithEscape = true;

        private int frameLimitIndex;
        private int windowModeIndex;
        private int resolutionIndex;
        private int backgroundCameraIndex;
        private static readonly WindowMode[] WindowModes = { WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen };
        private static readonly bool[] BackgroundCameraStates = { false, true };

        private void Awake()
        {
            if (panel != null) panel.SetActive(false);

            frameLimitIndex = IndexOfFrameLimit(GameOptions.FrameLimit);
            UpdateFrameLimitLabel();

            if (frameLimitPrevButton != null)
                frameLimitPrevButton.onClick.AddListener(() => CycleFrameLimit(-1));
            if (frameLimitNextButton != null)
                frameLimitNextButton.onClick.AddListener(() => CycleFrameLimit(+1));

            if (playerSfxVolumeSlider != null)
            {
                playerSfxVolumeSlider.minValue = 0f;
                playerSfxVolumeSlider.maxValue = 1f;
                playerSfxVolumeSlider.SetValueWithoutNotify(GameOptions.PlayerSfxVolume);
                playerSfxVolumeSlider.onValueChanged.AddListener(OnPlayerSfxVolumeChanged);
                UpdatePlayerSfxVolumeLabel(GameOptions.PlayerSfxVolume);
            }

            if (worldSfxVolumeSlider != null)
            {
                worldSfxVolumeSlider.minValue = 0f;
                worldSfxVolumeSlider.maxValue = 1f;
                worldSfxVolumeSlider.SetValueWithoutNotify(GameOptions.WorldSfxVolume);
                worldSfxVolumeSlider.onValueChanged.AddListener(OnWorldSfxVolumeChanged);
                UpdateWorldSfxVolumeLabel(GameOptions.WorldSfxVolume);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.minValue = 0f;
                bgmVolumeSlider.maxValue = 1f;
                bgmVolumeSlider.SetValueWithoutNotify(GameOptions.BgmVolume);
                bgmVolumeSlider.onValueChanged.AddListener(OnBgmVolumeChanged);
                UpdateBgmVolumeLabel(GameOptions.BgmVolume);
            }

            if (uiVolumeSlider != null)
            {
                uiVolumeSlider.minValue = 0f;
                uiVolumeSlider.maxValue = 1f;
                uiVolumeSlider.SetValueWithoutNotify(GameOptions.UiVolume);
                uiVolumeSlider.onValueChanged.AddListener(OnUiVolumeChanged);
                UpdateUiVolumeLabel(GameOptions.UiVolume);
            }

            windowModeIndex = IndexOfWindowMode(GameOptions.WindowMode);
            UpdateWindowModeLabel();
            if (windowModePrevButton != null)
                windowModePrevButton.onClick.AddListener(() => CycleWindowMode(-1));
            if (windowModeNextButton != null)
                windowModeNextButton.onClick.AddListener(() => CycleWindowMode(+1));

            resolutionIndex = GameOptions.ResolutionIndex;
            if (resolutionDropdown != null)
            {
                resolutionDropdown.ClearOptions();
                var opts = new System.Collections.Generic.List<TMP_Dropdown.OptionData>(
                    GameOptions.ResolutionOptions.Length);
                for (int i = 0; i < GameOptions.ResolutionOptions.Length; i++)
                {
                    var r = GameOptions.ResolutionOptions[i];
                    opts.Add(new TMP_Dropdown.OptionData(r.x + " x " + r.y));
                }
                resolutionDropdown.AddOptions(opts);
                resolutionDropdown.SetValueWithoutNotify(resolutionIndex);
                resolutionDropdown.RefreshShownValue();
                resolutionDropdown.onValueChanged.AddListener(OnResolutionDropdownChanged);
            }

            backgroundCameraIndex = IndexOfBackgroundCamera(GameOptions.BackgroundCameraEnabled);
            UpdateBackgroundCameraLabel();
            if (backgroundCameraPrevButton != null)
                backgroundCameraPrevButton.onClick.AddListener(() => CycleBackgroundCamera(-1));
            if (backgroundCameraNextButton != null)
                backgroundCameraNextButton.onClick.AddListener(() => CycleBackgroundCamera(+1));

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (quitButton != null)
                quitButton.onClick.AddListener(OnQuitClicked);
        }

        public void OnQuitClicked() => GameFlow.QuitGame();

        private void Update()
        {
            if (!toggleWithEscape) return;
            if (Input.GetKeyDown(KeyCode.Escape)) Toggle();
        }

        public void Toggle()
        {
            if (panel == null) return;
            if (panel.activeSelf) Close();
            else Open();
        }

        public void Open()
        {
            if (panel != null) panel.SetActive(true);
        }

        public void Close()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void CycleFrameLimit(int dir)
        {
            int len = GameOptions.FrameLimitOptions.Length;
            frameLimitIndex = ((frameLimitIndex + dir) % len + len) % len;
            GameOptions.SetFrameLimit(GameOptions.FrameLimitOptions[frameLimitIndex]);
            UpdateFrameLimitLabel();
        }

        private void OnPlayerSfxVolumeChanged(float v)
        {
            GameOptions.SetPlayerSfxVolume(v);
            UpdatePlayerSfxVolumeLabel(v);
        }

        private void OnWorldSfxVolumeChanged(float v)
        {
            GameOptions.SetWorldSfxVolume(v);
            UpdateWorldSfxVolumeLabel(v);
        }

        private void OnBgmVolumeChanged(float v)
        {
            GameOptions.SetBgmVolume(v);
            UpdateBgmVolumeLabel(v);
        }

        private void OnUiVolumeChanged(float v)
        {
            GameOptions.SetUiVolume(v);
            UpdateUiVolumeLabel(v);
        }

        private void UpdateFrameLimitLabel()
        {
            if (frameLimitValueLabel == null) return;
            int fps = GameOptions.FrameLimitOptions[frameLimitIndex];
            frameLimitValueLabel.text = fps <= 0 ? "Unlimited" : fps + " FPS";
        }

        private void UpdatePlayerSfxVolumeLabel(float v)
        {
            if (playerSfxVolumeValueLabel == null) return;
            playerSfxVolumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void UpdateWorldSfxVolumeLabel(float v)
        {
            if (worldSfxVolumeValueLabel == null) return;
            worldSfxVolumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void UpdateBgmVolumeLabel(float v)
        {
            if (bgmVolumeValueLabel == null) return;
            bgmVolumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private void UpdateUiVolumeLabel(float v)
        {
            if (uiVolumeValueLabel == null) return;
            uiVolumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
        }

        private static int IndexOfFrameLimit(int fps)
        {
            for (int i = 0; i < GameOptions.FrameLimitOptions.Length; i++)
                if (GameOptions.FrameLimitOptions[i] == fps) return i;
            return 1; // 60 default
        }

        private void CycleWindowMode(int dir)
        {
            int len = WindowModes.Length;
            windowModeIndex = ((windowModeIndex + dir) % len + len) % len;
            GameOptions.SetWindowMode(WindowModes[windowModeIndex]);
            UpdateWindowModeLabel();
        }

        private void UpdateWindowModeLabel()
        {
            if (windowModeValueLabel == null) return;
            switch (WindowModes[windowModeIndex])
            {
                case WindowMode.Windowed:   windowModeValueLabel.text = "Windowed";   break;
                case WindowMode.Borderless: windowModeValueLabel.text = "Borderless"; break;
                default:                    windowModeValueLabel.text = "Fullscreen"; break;
            }
        }

        private static int IndexOfWindowMode(WindowMode m)
        {
            for (int i = 0; i < WindowModes.Length; i++)
                if (WindowModes[i] == m) return i;
            return 1; // Fullscreen default
        }

        private void OnResolutionDropdownChanged(int index)
        {
            resolutionIndex = Mathf.Clamp(index, 0, GameOptions.ResolutionOptions.Length - 1);
            GameOptions.SetResolutionIndex(resolutionIndex);
        }

        private void CycleBackgroundCamera(int dir)
        {
            int len = BackgroundCameraStates.Length;
            backgroundCameraIndex = ((backgroundCameraIndex + dir) % len + len) % len;
            GameOptions.SetBackgroundCameraEnabled(BackgroundCameraStates[backgroundCameraIndex]);
            UpdateBackgroundCameraLabel();
        }

        private void UpdateBackgroundCameraLabel()
        {
            if (backgroundCameraValueLabel == null) return;
            backgroundCameraValueLabel.text = BackgroundCameraStates[backgroundCameraIndex] ? "On" : "Off";
        }

        private static int IndexOfBackgroundCamera(bool on)
        {
            for (int i = 0; i < BackgroundCameraStates.Length; i++)
                if (BackgroundCameraStates[i] == on) return i;
            return 1; // On default
        }
    }
}
