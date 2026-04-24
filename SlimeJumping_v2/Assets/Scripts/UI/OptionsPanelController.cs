using UnityEngine;
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
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private TextMeshProUGUI sfxVolumeValueLabel;
        [SerializeField] private Button windowModePrevButton;
        [SerializeField] private Button windowModeNextButton;
        [SerializeField] private TextMeshProUGUI windowModeValueLabel;
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
        private int backgroundCameraIndex;
        private static readonly WindowMode[] WindowModes = { WindowMode.Windowed, WindowMode.Fullscreen };
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

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.minValue = 0f;
                sfxVolumeSlider.maxValue = 1f;
                sfxVolumeSlider.SetValueWithoutNotify(GameOptions.SfxVolume);
                sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
                UpdateSfxVolumeLabel(GameOptions.SfxVolume);
            }

            windowModeIndex = IndexOfWindowMode(GameOptions.WindowMode);
            UpdateWindowModeLabel();
            if (windowModePrevButton != null)
                windowModePrevButton.onClick.AddListener(() => CycleWindowMode(-1));
            if (windowModeNextButton != null)
                windowModeNextButton.onClick.AddListener(() => CycleWindowMode(+1));

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

        private void OnSfxVolumeChanged(float v)
        {
            GameOptions.SetSfxVolume(v);
            UpdateSfxVolumeLabel(v);
        }

        private void UpdateFrameLimitLabel()
        {
            if (frameLimitValueLabel == null) return;
            int fps = GameOptions.FrameLimitOptions[frameLimitIndex];
            frameLimitValueLabel.text = fps <= 0 ? "Unlimited" : fps + " FPS";
        }

        private void UpdateSfxVolumeLabel(float v)
        {
            if (sfxVolumeValueLabel == null) return;
            sfxVolumeValueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
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
            windowModeValueLabel.text = WindowModes[windowModeIndex] == WindowMode.Windowed
                ? "Windowed" : "Fullscreen";
        }

        private static int IndexOfWindowMode(WindowMode m)
        {
            for (int i = 0; i < WindowModes.Length; i++)
                if (WindowModes[i] == m) return i;
            return 1; // Fullscreen default
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
