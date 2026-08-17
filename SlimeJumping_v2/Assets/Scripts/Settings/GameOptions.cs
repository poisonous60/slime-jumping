using System;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Settings
{
    public enum WindowMode { Windowed, Fullscreen, Borderless }

    /// PlayerPrefs-backed user options applied on boot; audio is re-applied after scene load.
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
        private const string KeyChargeVisualOn        = "opt.chargeVisualEnabled";
        private const string KeyMouseTeleportDebugOn  = "opt.mouseTeleportDebugEnabled";
        private const string KeySavepointOn           = "opt.savepointEnabled";
        private const string KeyGameSpeed             = "opt.gameSpeed";
        private const string KeyImplantOn             = "opt.implantEnabled";

        // Pre-split combined SFX slider; seeds both SFX volumes on first load.
        private const string KeyLegacySfxVolume       = "opt.sfxVolume";

        public const int        DefaultFrameLimit            = 60;
        public const float      DefaultPlayerSfxVolume       = 0.8f;
        public const float      DefaultWorldSfxVolume        = 0.8f;
        public const float      DefaultBgmVolume             = 0.8f;
        public const float      DefaultUiVolume              = 0.8f;
        public const WindowMode DefaultWindowMode            = WindowMode.Fullscreen;
        public const bool       DefaultBackgroundCameraOn    = true;
        public const bool       DefaultVSyncOn               = true;
        public const bool       DefaultChargeVisualOn        = false;
        // 디버그 도구라 기본 Off — 빌드를 처음 켠 기기에서 저절로 켜져 있으면 안 된다.
        public const bool       DefaultMouseTeleportDebugOn  = false;
        public const bool       DefaultSavepointOn           = false;
        public const float      DefaultGameSpeed             = 1f;
        public const float      MinGameSpeed                 = 0.5f;
        public const float      MaxGameSpeed                 = 2f;
        public const bool       DefaultImplantOn             = false;

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
        public static bool       ChargeVisualEnabled     { get; private set; } = DefaultChargeVisualOn;
        public static bool       MouseTeleportDebugEnabled { get; private set; } = DefaultMouseTeleportDebugOn;
        public static bool       SavepointEnabled        { get; private set; } = DefaultSavepointOn;
        public static float      GameSpeed               { get; private set; } = DefaultGameSpeed;
        public static bool       ImplantEnabled          { get; private set; } = DefaultImplantOn;

        public static Vector2Int Resolution => ResolutionOptions[ResolutionIndex];

        /// Raised when BackgroundCameraEnabled is changed. Scene components
        /// subscribe to toggle the BG camera + overlay stack live.
        public static event Action<bool> BackgroundCameraEnabledChanged;

        /// Debug-only. Raised when ChargeVisualEnabled is changed so the
        /// SlimeChargeVisual can hide/show without restart.
        public static event Action<bool> ChargeVisualEnabledChanged;

        /// Debug-only. Raised when MouseTeleportDebugEnabled changes so the
        /// MouseClickTeleportDebugger can enable/disable click-to-teleport.
        public static event Action<bool> MouseTeleportDebugEnabledChanged;

        /// Raised when ImplantEnabled changes. SandevistanManager subscribes to fade its
        /// look in and out; the setting is cosmetic only and never reaches gameplay.
        public static event Action<bool> ImplantEnabledChanged;

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
            ChargeVisualEnabled     = PlayerPrefs.GetInt(KeyChargeVisualOn, DefaultChargeVisualOn ? 1 : 0) != 0;
            MouseTeleportDebugEnabled = PlayerPrefs.GetInt(KeyMouseTeleportDebugOn, DefaultMouseTeleportDebugOn ? 1 : 0) != 0;
            SavepointEnabled        = PlayerPrefs.GetInt(KeySavepointOn, DefaultSavepointOn ? 1 : 0) != 0;
            // Clamp01 아니다 — 여기 다른 float 들과 달리 상한이 1 을 넘는다.
            GameSpeed              = Mathf.Clamp(PlayerPrefs.GetFloat(KeyGameSpeed, DefaultGameSpeed),
                                                 MinGameSpeed, MaxGameSpeed);
            ImplantEnabled         = PlayerPrefs.GetInt(KeyImplantOn, DefaultImplantOn ? 1 : 0) != 0;
            ApplyDisplayPacing();
            ApplyDisplay(ResolutionOptions[ResolutionIndex], WindowMode);

            // Cap FixedUpdate catchup: the 0.333s default lets ~16 ticks pile into one frame after a
            // stall, and at 5–20ms per tick that death-spirals. 0.1s caps it at ~5 ticks; the rest is
            // dropped (a skip, not slow-mo — nothing here depends on continuous time).
            Time.maximumDeltaTime = 0.1f;
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

        /// 저장만 한다 — 적용은 OptionsPanel 이 정지를 푸는 시점. 창이 열려 있는 동안은
        /// 그 백스톱이 매 프레임 timeScale 을 0 으로 되돌려서 즉시 적용이 의미가 없다.
        public static void SetGameSpeed(float speed)
        {
            GameSpeed = Mathf.Clamp(speed, MinGameSpeed, MaxGameSpeed);
            PlayerPrefs.SetFloat(KeyGameSpeed, GameSpeed);
            PlayerPrefs.Save();
        }

        public static void SetImplantEnabled(bool on)
        {
            ImplantEnabled = on;
            PlayerPrefs.SetInt(KeyImplantOn, on ? 1 : 0);
            PlayerPrefs.Save();
            var handler = ImplantEnabledChanged;
            if (handler != null) handler(on);
        }

        /// TeleportDebugger 가 매 프레임 읽어가므로 이벤트가 없다.
        public static void SetSavepointEnabled(bool on)
        {
            SavepointEnabled = on;
            PlayerPrefs.SetInt(KeySavepointOn, on ? 1 : 0);
            PlayerPrefs.Save();
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

        public static void SetChargeVisualEnabled(bool on)
        {
            ChargeVisualEnabled = on;
            PlayerPrefs.SetInt(KeyChargeVisualOn, on ? 1 : 0);
            PlayerPrefs.Save();
            var handler = ChargeVisualEnabledChanged;
            if (handler != null) handler(on);
        }

        public static void SetMouseTeleportDebugEnabled(bool on)
        {
            MouseTeleportDebugEnabled = on;
            PlayerPrefs.SetInt(KeyMouseTeleportDebugOn, on ? 1 : 0);
            PlayerPrefs.Save();
            var handler = MouseTeleportDebugEnabledChanged;
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
            // Windowed resizes the window; the fullscreen modes set render resolution only.
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
