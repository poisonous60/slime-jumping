using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Rendering;
using SlimeJumping.Settings;
using SlimeJumping.UI.Animation;
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

        [Header("Debug")]
        [SerializeField] private Button chargeVisualPrevButton;
        [SerializeField] private Button chargeVisualNextButton;
        [SerializeField] private BumpableLabel chargeVisualValueLabel;
        [SerializeField] private Button mouseTeleportDebugPrevButton;
        [SerializeField] private Button mouseTeleportDebugNextButton;
        [SerializeField] private BumpableLabel mouseTeleportDebugValueLabel;
        [SerializeField] private Button clearTrailButton;

        [Header("Tabs")]
        [SerializeField] private Button graphicsTabButton;
        [SerializeField] private Button audioTabButton;
        [SerializeField] private Button displayTabButton;
        [SerializeField] private Button debugTabButton;
        [SerializeField] private GameObject graphicsPage;
        [SerializeField] private GameObject audioPage;
        [SerializeField] private GameObject displayPage;
        [SerializeField] private GameObject debugPage;
        [SerializeField] private TextMeshProUGUI graphicsTabLabel;
        [SerializeField] private TextMeshProUGUI audioTabLabel;
        [SerializeField] private TextMeshProUGUI displayTabLabel;
        [SerializeField] private TextMeshProUGUI debugTabLabel;

        [Header("Actions")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button quitButton;

        private int frameLimitIndex;
        private int windowModeIndex;
        private int backgroundCameraIndex;
        private int vsyncIndex;
        private int chargeVisualIndex;
        private int mouseTeleportDebugIndex;
        private static readonly WindowMode[] WindowModes = { WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen };
        private static readonly bool[] BackgroundCameraStates = { false, true };
        private static readonly bool[] VSyncStates = { false, true };
        private static readonly bool[] ChargeVisualStates = { false, true };
        private static readonly bool[] MouseTeleportDebugStates = { false, true };

        protected override void OnAfterAwake()
        {
            frameLimitIndex = IndexOfFrameLimit(GameOptions.FrameLimit);
            UpdateFrameLimitLabel();
            if (frameLimitPrevButton != null) frameLimitPrevButton.onClick.AddListener(() => CycleFrameLimit(-1));
            if (frameLimitNextButton != null) frameLimitNextButton.onClick.AddListener(() => CycleFrameLimit(+1));

            vsyncIndex = IndexOfVSync(GameOptions.VSyncEnabled);
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

            backgroundCameraIndex = IndexOfBackgroundCamera(GameOptions.BackgroundCameraEnabled);
            UpdateBackgroundCameraLabel();
            if (backgroundCameraPrevButton != null) backgroundCameraPrevButton.onClick.AddListener(() => CycleBackgroundCamera(-1));
            if (backgroundCameraNextButton != null) backgroundCameraNextButton.onClick.AddListener(() => CycleBackgroundCamera(+1));

            chargeVisualIndex = IndexOfChargeVisual(GameOptions.ChargeVisualEnabled);
            UpdateChargeVisualLabel();
            if (chargeVisualPrevButton != null) chargeVisualPrevButton.onClick.AddListener(() => CycleChargeVisual(-1));
            if (chargeVisualNextButton != null) chargeVisualNextButton.onClick.AddListener(() => CycleChargeVisual(+1));

            mouseTeleportDebugIndex = IndexOfMouseTeleportDebug(GameOptions.MouseTeleportDebugEnabled);
            UpdateMouseTeleportDebugLabel();
            if (mouseTeleportDebugPrevButton != null) mouseTeleportDebugPrevButton.onClick.AddListener(() => CycleMouseTeleportDebug(-1));
            if (mouseTeleportDebugNextButton != null) mouseTeleportDebugNextButton.onClick.AddListener(() => CycleMouseTeleportDebug(+1));

            if (clearTrailButton != null) clearTrailButton.onClick.AddListener(HandleClearTrail);

            if (graphicsTabButton != null) graphicsTabButton.onClick.AddListener(() => SelectTab(0));
            if (audioTabButton != null)    audioTabButton.onClick.AddListener(() => SelectTab(1));
            if (displayTabButton != null)  displayTabButton.onClick.AddListener(() => SelectTab(2));
            if (debugTabButton != null)    debugTabButton.onClick.AddListener(() => SelectTab(3));
            SelectTab(0);

            if (closeButton != null) closeButton.onClick.AddListener(HandleClose);
            if (quitButton != null) quitButton.onClick.AddListener(GameFlow.QuitGame);
        }

        private void HandleClose()
        {
            if (UIManager.Instance != null) UIManager.Instance.Close(this);
            else Close();
        }

        private void HandleClearTrail()
        {
            var mgr = WorldTrailManager.Instance;
            if (mgr != null) mgr.ClearAllTrails();
        }

        private static readonly Color TabActiveColor   = new Color(0.02f, 0.06f, 0.04f, 1f);
        private static readonly Color TabInactiveColor = new Color(0.20f, 0.26f, 0.22f, 1f);

        private void SelectTab(int index)
        {
            if (graphicsPage != null) graphicsPage.SetActive(index == 0);
            if (audioPage != null)    audioPage.SetActive(index == 1);
            if (displayPage != null)  displayPage.SetActive(index == 2);
            if (debugPage != null)    debugPage.SetActive(index == 3);
            if (graphicsTabLabel != null) graphicsTabLabel.color = index == 0 ? TabActiveColor : TabInactiveColor;
            if (audioTabLabel != null)    audioTabLabel.color    = index == 1 ? TabActiveColor : TabInactiveColor;
            if (displayTabLabel != null)  displayTabLabel.color  = index == 2 ? TabActiveColor : TabInactiveColor;
            if (debugTabLabel != null)    debugTabLabel.color    = index == 3 ? TabActiveColor : TabInactiveColor;
        }

        protected override void OnDestroy()
        {
            // Drop all editor-wired listeners so the delegates don't retain `this`.
            // These are attached in OnAfterAwake and never otherwise cleared.
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
            if (chargeVisualPrevButton != null) chargeVisualPrevButton.onClick.RemoveAllListeners();
            if (chargeVisualNextButton != null) chargeVisualNextButton.onClick.RemoveAllListeners();
            if (mouseTeleportDebugPrevButton != null) mouseTeleportDebugPrevButton.onClick.RemoveAllListeners();
            if (mouseTeleportDebugNextButton != null) mouseTeleportDebugNextButton.onClick.RemoveAllListeners();
            if (clearTrailButton != null) clearTrailButton.onClick.RemoveAllListeners();
            if (graphicsTabButton != null) graphicsTabButton.onClick.RemoveAllListeners();
            if (audioTabButton != null)    audioTabButton.onClick.RemoveAllListeners();
            if (displayTabButton != null)  displayTabButton.onClick.RemoveAllListeners();
            if (debugTabButton != null)    debugTabButton.onClick.RemoveAllListeners();
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

        private void CycleVSync(int dir)
        {
            int len = VSyncStates.Length;
            vsyncIndex = ((vsyncIndex + dir) % len + len) % len;
            GameOptions.SetVSyncEnabled(VSyncStates[vsyncIndex]);
            UpdateVSyncLabel();
        }

        private void CycleChargeVisual(int dir)
        {
            int len = ChargeVisualStates.Length;
            chargeVisualIndex = ((chargeVisualIndex + dir) % len + len) % len;
            GameOptions.SetChargeVisualEnabled(ChargeVisualStates[chargeVisualIndex]);
            UpdateChargeVisualLabel();
        }

        private void CycleMouseTeleportDebug(int dir)
        {
            int len = MouseTeleportDebugStates.Length;
            mouseTeleportDebugIndex = ((mouseTeleportDebugIndex + dir) % len + len) % len;
            GameOptions.SetMouseTeleportDebugEnabled(MouseTeleportDebugStates[mouseTeleportDebugIndex]);
            UpdateMouseTeleportDebugLabel();
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
            backgroundCameraValueLabel.Set(BackgroundCameraStates[backgroundCameraIndex] ? "On" : "Off");
        }

        private void UpdateVSyncLabel()
        {
            if (vsyncValueLabel == null) return;
            vsyncValueLabel.Set(VSyncStates[vsyncIndex] ? "On" : "Off");
        }

        private void UpdateChargeVisualLabel()
        {
            if (chargeVisualValueLabel == null) return;
            chargeVisualValueLabel.Set(ChargeVisualStates[chargeVisualIndex] ? "On" : "Off");
        }

        private void UpdateMouseTeleportDebugLabel()
        {
            if (mouseTeleportDebugValueLabel == null) return;
            mouseTeleportDebugValueLabel.Set(MouseTeleportDebugStates[mouseTeleportDebugIndex] ? "On" : "Off");
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

        private static int IndexOfVSync(bool on)
        {
            for (int i = 0; i < VSyncStates.Length; i++)
                if (VSyncStates[i] == on) return i;
            return 1;
        }

        private static int IndexOfChargeVisual(bool on)
        {
            for (int i = 0; i < ChargeVisualStates.Length; i++)
                if (ChargeVisualStates[i] == on) return i;
            return 1;
        }

        private static int IndexOfMouseTeleportDebug(bool on)
        {
            for (int i = 0; i < MouseTeleportDebugStates.Length; i++)
                if (MouseTeleportDebugStates[i] == on) return i;
            return 1;
        }
    }
}
