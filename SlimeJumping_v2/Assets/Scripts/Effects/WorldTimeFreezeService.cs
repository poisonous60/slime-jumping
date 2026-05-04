using System;
using System.Collections;
using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Selective world freeze. Unlike <see cref="HitStopService"/> this never touches
    /// <c>Time.timeScale</c>; it only flips a flag and raises events. Cooperators
    /// (mechanism scripts that should pause during the freeze) early-return on
    /// <see cref="IsFrozen"/> in their Update; non-cooperators (slime, warp FX) keep
    /// running. ParticleSystems opt in via <see cref="FreezableParticleSystem"/>.
    ///
    /// Overlapping <see cref="Freeze"/> calls extend to the latest end time. The
    /// service bootstraps a hidden DontDestroyOnLoad runner on first call; flag and
    /// events are reset on app quit and editor play-mode exit so a stuck freeze
    /// cannot leak across sessions.
    public static class WorldTimeFreezeService
    {
        public static bool IsFrozen { get; private set; }
        public static event Action OnFreezeStarted;
        public static event Action OnFreezeEnded;

        private class Runner : MonoBehaviour
        {
            private Coroutine active;
            private float endRealtime;

            public void Request(float realtimeSeconds)
            {
                float newEnd = Time.realtimeSinceStartup + realtimeSeconds;
                if (active == null)
                {
                    endRealtime = newEnd;
                    SetFrozen(true);
                    active = StartCoroutine(Run());
                }
                else if (newEnd > endRealtime)
                {
                    endRealtime = newEnd;
                }
            }

            private IEnumerator Run()
            {
                while (Time.realtimeSinceStartup < endRealtime)
                    yield return null;
                SetFrozen(false);
                active = null;
            }

            private void OnDisable()
            {
                if (active != null) { SetFrozen(false); active = null; }
            }

            private void OnApplicationQuit()
            {
                if (active != null) { SetFrozen(false); active = null; }
            }
        }

        private static Runner runner;

        public static void Freeze(float realtimeSeconds)
        {
            if (realtimeSeconds <= 0f) return;
            EnsureRunner();
            runner.Request(realtimeSeconds);
        }

        private static void SetFrozen(bool frozen)
        {
            if (IsFrozen == frozen) return;
            IsFrozen = frozen;
            Action handler = frozen ? OnFreezeStarted : OnFreezeEnded;
            if (handler == null) return;
            foreach (Action h in handler.GetInvocationList())
            {
                try { h(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("[WorldTimeFreezeService]");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode()
        {
            runner = null;
            IsFrozen = false;
            OnFreezeStarted = null;
            OnFreezeEnded = null;
        }
#endif
    }
}
