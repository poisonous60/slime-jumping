using System;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Settings
{
    public enum WindowMode { Windowed, Fullscreen }

    /// Persists demo-facing user options (frame limit, SFX volume, window mode,
    /// background camera) via PlayerPrefs and applies them on boot. Audio is
    /// re-applied after scene load so the mixer picks it up once SoundService
    /// has initialized. Background camera changes fire an event so scene-level
    /// components (see BackgroundCameraOption) can react live.
    public static class GameOptions
    {
        private const string KeyFrameLimit            = "opt.frameLimit";
        private const string KeySfxVolume             = "opt.sfxVolume";
        private const string KeyWindowMode            = "opt.windowMode";
        private const string KeyBackgroundCameraOn    = "opt.backgroundCameraEnabled";

        public const int        DefaultFrameLimit            = 60;
        public const float      DefaultSfxVolume             = 0.8f;
        public const WindowMode DefaultWindowMode            = WindowMode.Fullscreen;
        public const bool       DefaultBackgroundCameraOn    = true;

        // -1 = Unlimited (Application.targetFrameRate = -1)
        public static readonly int[] FrameLimitOptions = { 30, 60, 120, -1 };

        public static int        FrameLimit             { get; private set; } = DefaultFrameLimit;
        public static float      SfxVolume              { get; private set; } = DefaultSfxVolume;
        public static WindowMode WindowMode             { get; private set; } = DefaultWindowMode;
        public static bool       BackgroundCameraEnabled { get; private set; } = DefaultBackgroundCameraOn;

        /// Raised when BackgroundCameraEnabled is changed. Scene components
        /// subscribe to toggle the BG camera + overlay stack live.
        public static event Action<bool> BackgroundCameraEnabledChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void BootstrapLoadAndApplyFrame()
        {
            FrameLimit             = PlayerPrefs.GetInt(KeyFrameLimit, DefaultFrameLimit);
            SfxVolume              = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfxVolume, DefaultSfxVolume));
            WindowMode             = (WindowMode)PlayerPrefs.GetInt(KeyWindowMode, (int)DefaultWindowMode);
            BackgroundCameraEnabled = PlayerPrefs.GetInt(KeyBackgroundCameraOn, DefaultBackgroundCameraOn ? 1 : 0) != 0;
            ApplyFrameLimit(FrameLimit);
            ApplyWindowMode(WindowMode);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapApplyAudio()
        {
            // Mixer params are only valid once SoundService has built its pools
            // (which routes the AudioMixer). AfterSceneLoad runs after all Awakes.
            ApplySfxVolume(SfxVolume);
        }

        public static void SetFrameLimit(int fps)
        {
            FrameLimit = fps;
            PlayerPrefs.SetInt(KeyFrameLimit, fps);
            PlayerPrefs.Save();
            ApplyFrameLimit(fps);
        }

        public static void SetSfxVolume(float linear01)
        {
            SfxVolume = Mathf.Clamp01(linear01);
            PlayerPrefs.SetFloat(KeySfxVolume, SfxVolume);
            PlayerPrefs.Save();
            ApplySfxVolume(SfxVolume);
        }

        public static void SetWindowMode(WindowMode mode)
        {
            WindowMode = mode;
            PlayerPrefs.SetInt(KeyWindowMode, (int)mode);
            PlayerPrefs.Save();
            ApplyWindowMode(mode);
        }

        public static void SetBackgroundCameraEnabled(bool on)
        {
            BackgroundCameraEnabled = on;
            PlayerPrefs.SetInt(KeyBackgroundCameraOn, on ? 1 : 0);
            PlayerPrefs.Save();
            var handler = BackgroundCameraEnabledChanged;
            if (handler != null) handler(on);
        }

        private static void ApplyFrameLimit(int fps)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = fps <= 0 ? -1 : fps;
        }

        private static void ApplySfxVolume(float linear01)
        {
            // Linear 0..1 -> dB, with -80dB floor for silence.
            float db = linear01 <= 0.0001f ? -80f : Mathf.Log10(linear01) * 20f;
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Slime,       db);
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Environment, db);
            SoundService.SetCategoryVolumeDb(SoundCategory.SFX_Object,      db);
        }

        private static void ApplyWindowMode(WindowMode mode)
        {
            Screen.fullScreenMode = mode == WindowMode.Windowed
                ? FullScreenMode.Windowed
                : FullScreenMode.FullScreenWindow;
        }
    }
}
