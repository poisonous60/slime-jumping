using UnityEngine;

namespace SlimeJumping.UI
{
    /// <summary>
    /// The run timer behind CLEAR TIME. Counts real seconds, not game seconds, so the game-speed
    /// setting can neither inflate nor deflate a record. The pause menu holds it explicitly rather
    /// than reading timeScale, because a full hitstop also parks timeScale at zero.
    /// Frozen once by <see cref="Capture"/> at the stele; the value survives the load into EndScene,
    /// so the credits room shows the same record instead of its own scene clock.
    /// Seeded by LoadSaveManager on scene entry — a fresh run from 0, a loaded run from the save.
    /// </summary>
    public static class RunClock
    {
        private static float elapsed;

        public static float Elapsed => elapsed;
        public static bool Captured { get; private set; }
        public static int CapturedSeconds { get; private set; }

        /// Held while the pause menu owns the screen.
        public static bool Paused { get; set; }

        /// Held by SlimeStartStateManager for the wake-up intro — the record starts at first control.
        public static bool IntroHold { get; set; }

        // Domain Reload 를 끄면 statics 가 Play 세션을 넘어 살아남아 다음 진입의 기록을 오염시킨다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            elapsed = 0f;
            Paused = false;
            IntroHold = false;
            Captured = false;
            CapturedSeconds = 0;
        }

        /// Starts the run at `seconds` — 0 for a new game, the saved total for a loaded one.
        /// Safe to call before the intro claims its hold: SlimeStartStateManager waits a frame first.
        public static void Restart(float seconds)
        {
            elapsed = Mathf.Max(0f, seconds);
            Paused = false;
            IntroHold = false;
            Captured = false;
            CapturedSeconds = 0;
        }

        public static void Tick(float unscaledDelta)
        {
            if (Paused || IntroHold || Captured) return;
            elapsed += unscaledDelta;
        }

        /// Freezes the record and returns it; later calls return the same number.
        public static int Capture()
        {
            if (!Captured)
            {
                Captured = true;
                CapturedSeconds = Mathf.FloorToInt(elapsed);
            }
            return CapturedSeconds;
        }

        private const int OverflowSeconds = 100 * 3600;

        public static string Format(int seconds)
        {
            if (seconds >= OverflowSeconds) return "99:99:99";
            return seconds >= 3600
                ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}"
                : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }
}
