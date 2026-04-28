using System;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Settings
{
    public enum WindowMode { Windowed, Fullscreen, Borderless }

    /// Persists demo-facing user options (frame limit, Player/World SFX / BGM /
    /// UI volume, window mode, background camera) via PlayerPrefs and applies
    /// them on boot. Audio is re-applied after scene load so the mixer picks it
    /// up once SoundService has initialized. Background camera changes fire an
    /// event so scene-level components (see BackgroundCameraOption) can react live.
    public static class GameOptions
    {
        private const string KeyFrameLimit            = "opt.frameLimit";
        private const string KeyPlayerSfxVolume       = "opt.playerSfxVolume";
        private const string KeyWorldSfxVolume        = "opt.worldSfxVolume";
        private const string KeyBgmVolume             = "opt.bgmVolume";
        private const string KeyUiVolume              = "opt.uiVolume";
        private const string KeyWindowMode            = "opt.windowMode";
        private const string KeyResolutionIndex       = "opt.resolutionIndex";
        private const string KeyBackgroundCameraOn    = "opt.backgroundCameraEnabled";
        private const string KeyVSyncOn               = "opt.vsyncEnabled";

        // Legacy key used before the SFX split (single combined slider).
        // Migrated into PlayerSfxVolume on first load so existing users don't lose their setting.
        private const string KeyLegacySfxVolume       = "opt.sfxVolume";

        public const int        DefaultFrameLimit            = 60;
        public const float      DefaultPlayerSfxVolume       = 0.8f;
        public const float      DefaultWorldSfxVolume        = 0.8f;
        public const float      DefaultBgmVolume             = 0.8f;
        public const float      DefaultUiVolume              = 0.8f;
        public const WindowMode DefaultWindowMode            = WindowMode.Fullscreen;
        public const bool       DefaultBackgroundCameraOn    = true;
        public const bool       DefaultVSyncOn               = true;

        // -1 = Unlimited (Application.targetFrameRate = -1)
        public static readonly int[] FrameLimitOptions = { 30, 60, 120, -1 };

        public static readonly Vector2Int[] ResolutionOptions =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1366, 768),
            new Vector2Int(1600, 900),
            new Vector2Int(1920, 1080),
            new Vector2Int(2560, 1440),
            new Vector2Int(3840, 2160),
        };

        public static int        FrameLimit              { get; private set; } = DefaultFrameLimit;
        public static float      PlayerSfxVolume         { get; private set; } = DefaultPlayerSfxVolume;
        public static float      WorldSfxVolume          { get; private set; } = DefaultWorldSfxVolume;
        public static float      BgmVolume               { get; private set; } = DefaultBgmVolume;
        public static float      UiVolume                { get; private set; } = DefaultUiVolume;
        public static WindowMode WindowMode              { get; private set; } = DefaultWindowMode;
        public static int        ResolutionIndex         { get; private set; }
        public static bool       BackgroundCameraEnabled { get; private set; } = DefaultBackgroundCameraOn;
        public static bool       VSyncEnabled            { get; private set; } = DefaultVSyncOn;

        public static Vector2Int Resolution => ResolutionOptions[ResolutionIndex];

        /// Raised when BackgroundCameraEnabled is changed. Scene components
        /// subscribe to toggle the BG camera + overlay stack live.
        public static event Action<bool> BackgroundCameraEnabledChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapLoadAndApplyFrame()
        {
            FrameLimit             = PlayerPrefs.GetInt(KeyFrameLimit, DefaultFrameLimit);

            float legacy = PlayerPrefs.GetFloat(KeyLegacySfxVolume, -1f);
            PlayerSfxVolume        = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyPlayerSfxVolume,
                                                    legacy >= 0f ? legacy : DefaultPlayerSfxVolume));
            WorldSfxVolume         = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyWorldSfxVolume,
                                                    legacy >= 0f ? legacy : DefaultWorldSfxVolume));
            BgmVolume              = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyBgmVolume, DefaultBgmVolume));
            UiVolume               = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyUiVolume, DefaultUiVolume));

            WindowMode             = (WindowMode)PlayerPrefs.GetInt(KeyWindowMode, (int)DefaultWindowMode);
            ResolutionIndex        = Mathf.Clamp(
                PlayerPrefs.GetInt(KeyResolutionIndex, FindNativeResolutionIndex()),
                0, ResolutionOptions.Length - 1);
            BackgroundCameraEnabled = PlayerPrefs.GetInt(KeyBackgroundCameraOn, DefaultBackgroundCameraOn ? 1 : 0) != 0;
            VSyncEnabled            = PlayerPrefs.GetInt(KeyVSyncOn, DefaultVSyncOn ? 1 : 0) != 0;
            ApplyDisplayPacing();
            ApplyDisplay(ResolutionOptions[ResolutionIndex], WindowMode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapApplyAudio()
        {
            // Mixer params are only valid once SoundService has built its pools
            // (which routes the AudioMixer). AfterSceneLoad runs after all Awakes.
            ApplyPlayerSfxVolume(PlayerSfxVolume);
            ApplyWorldSfxVolume(WorldSfxVolume);
            ApplyBgmVolume(BgmVolume);
            ApplyUiVolume(UiVolume);
        }

        public static void SetFrameLimit(int fps)
        {
            FrameLimit = fps;
            PlayerPrefs.SetInt(KeyFrameLimit, fps);
            PlayerPrefs.Save();
            ApplyDisplayPacing();
        }

        public static void SetVSyncEnabled(bool on)
        {
            VSyncEnabled = on;
            PlayerPrefs.SetInt(KeyVSyncOn, on ? 1 : 0);
            PlayerPrefs.Save();
            ApplyDisplayPacing();
        }

        public static void SetPlayerSfxVolume(float linear01)
        {
            PlayerSfxVolume = Mathf.Clamp01(linear01);
            PlayerPrefs.SetFloat(KeyPlayerSfxVolume, PlayerSfxVolume);
            PlayerPrefs.Save();
            ApplyPlayerSfxVolume(PlayerSfxVolume);
        }

        public static void SetWorldSfxVolume(float linear01)
        {
            WorldSfxVolume = Mathf.Clamp01(linear01);
            PlayerPrefs.SetFloat(KeyWorldSfxVolume, WorldSfxVolume);
            PlayerPrefs.Save();
            ApplyWorldSfxVolume(WorldSfxVolume);
        }

        public static void SetBgmVolume(float linear01)
        {
            BgmVolume = Mathf.Clamp01(linear01);
            PlayerPrefs.SetFloat(KeyBgmVolume, BgmVolume);
            PlayerPrefs.Save();
            ApplyBgmVolume(BgmVolume);
        }

        public static void SetUiVolume(float linear01)
        {
            UiVolume = Mathf.Clamp01(linear01);
            PlayerPrefs.SetFloat(KeyUiVolume, UiVolume);
            PlayerPrefs.Save();
            ApplyUiVolume(UiVolume);
        }

        public static void SetWindowMode(WindowMode mode)
        {
            WindowMode = mode;
            PlayerPrefs.SetInt(KeyWindowMode, (int)mode);
            PlayerPrefs.Save();
            ApplyDisplay(ResolutionOptions[ResolutionIndex], mode);
        }

        public static void SetResolutionIndex(int index)
        {
            ResolutionIndex = Mathf.Clamp(index, 0, ResolutionOptions.Length - 1);
            PlayerPrefs.SetInt(KeyResolutionIndex, ResolutionIndex);
            PlayerPrefs.Save();
            ApplyDisplay(ResolutionOptions[ResolutionIndex], WindowMode);
        }

        public static void SetBackgroundCameraEnabled(bool on)
        {
            BackgroundCameraEnabled = on;
            PlayerPrefs.SetInt(KeyBackgroundCameraOn, on ? 1 : 0);
            PlayerPrefs.Save();
            var handler = BackgroundCameraEnabledChanged;
            if (handler != null) handler(on);
        }

        // VSync and frame limit interact: when vsync is on Unity ignores
        // targetFrameRate, so the meaningful pacing comes from vsync alone.
        // When vsync is off, targetFrameRate is the soft cap.
        private static void ApplyDisplayPacing()
        {
            if (VSyncEnabled)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = FrameLimit <= 0 ? -1 : FrameLimit;
            }
        }

        private static void ApplyPlayerSfxVolume(float linear01)
        {
            // Linear 0..1 -> dB, with -80dB floor for silence.
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Slime, db);
        }

        private static void ApplyWorldSfxVolume(float linear01)
        {
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Environment, db);
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Object,      db);
        }

        private static void ApplyBgmVolume(float linear01)
        {
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            SoundService.SetCategoryVolumeDb(SoundCategory.BGM, db);
        }

        private static void ApplyUiVolume(float linear01)
        {
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            SoundService.SetCategoryVolumeDb(SoundCategory.UI, db);
        }

        private static void ApplyDisplay(Vector2Int res, WindowMode mode)
        {
            FullScreenMode fsMode;
            switch (mode)
            {
                case WindowMode.Windowed:   fsMode = FullScreenMode.Windowed;            break;
                case WindowMode.Borderless: fsMode = FullScreenMode.FullScreenWindow;    break;
                default:                    fsMode = FullScreenMode.ExclusiveFullScreen; break;
            }
            // In Windowed mode this resizes the window; in FullScreenWindow/
            // ExclusiveFullScreen it sets the render resolution while the window
            // covers the display.
            Screen.SetResolution(res.x, res.y, fsMode);
        }

        private static int FindNativeResolutionIndex()
        {
            int w = Screen.currentResolution.width;
            int h = Screen.currentResolution.height;
            int bestIdx = 3; // 1920x1080 fallback
            int bestDelta = int.MaxValue;
            for (int i = 0; i < ResolutionOptions.Length; i++)
            {
                if (ResolutionOptions[i].x == w && ResolutionOptions[i].y == h)
                    return i;
                int d = Mathf.Abs(ResolutionOptions[i].x - w) + Mathf.Abs(ResolutionOptions[i].y - h);
                if (d < bestDelta) { bestDelta = d; bestIdx = i; }
            }
            return bestIdx;
        }
    }
}
