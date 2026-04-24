using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Static impact-frame service. Call Freeze(duration) from anywhere to briefly drop
    /// Time.timeScale and restore it. Overlapping calls extend to the latest end time and
    /// keep the stronger freeze; they never stack or cut short. Register a Func&lt;bool&gt;
    /// via AddGuard to veto invocations (charging, menu open, cutscene, etc.).
    ///
    /// The service bootstraps a hidden DontDestroyOnLoad runner on first Freeze() call,
    /// so callers never need to wire anything. Time.timeScale is always restored on app
    /// quit and editor play-mode exit, so a runaway freeze cannot leave the game paused.
    public static class HitStopService
    {
        private class Runner : MonoBehaviour
        {
            private Coroutine active;
            private float endRealtime;
            private float baseTimeScale = 1f;

            public void Request(float duration, float frozenScale)
            {
                float newEnd = Time.realtimeSinceStartup + duration;

                if (active == null)
                {
                    baseTimeScale = Time.timeScale;
                    Time.timeScale = frozenScale;
                    endRealtime = newEnd;
                    active = StartCoroutine(Run());
                }
                else
                {
                    if (newEnd > endRealtime) endRealtime = newEnd;
                    // Stronger freeze (lower scale) wins for the remaining duration.
                    if (frozenScale < Time.timeScale) Time.timeScale = frozenScale;
                }
            }

            private IEnumerator Run()
            {
                while (Time.realtimeSinceStartup < endRealtime)
                    yield return null;
                Time.timeScale = baseTimeScale;
                active = null;
            }

            private void OnDisable()
            {
                if (active != null) { Time.timeScale = baseTimeScale; active = null; }
            }

            private void OnApplicationQuit()
            {
                if (active != null) { Time.timeScale = baseTimeScale; active = null; }
            }
        }

        private static Runner runner;
        private static readonly List<Func<bool>> guards = new List<Func<bool>>();

        /// Fired every time a Freeze call passes the guard check. Argument is the freeze
        /// duration in realtime seconds. Subscribers should use Time.unscaledDeltaTime for
        /// any animation since Time.timeScale is (or will be) clamped low during the freeze.
        /// Intended hook for layered effects: B&amp;W post-process, fullscreen flash, audio
        /// pitch dip, etc. Handlers must not throw — exceptions would break the freeze.
        public static event Action<float> OnFreezeStarted;

        /// Register a veto predicate. While any registered predicate returns true, Freeze
        /// calls are silently ignored. Always unregister in OnDisable to avoid captured
        /// references leaking across scene reloads.
        public static void AddGuard(Func<bool> guard)
        {
            if (guard != null && !guards.Contains(guard)) guards.Add(guard);
        }

        public static void RemoveGuard(Func<bool> guard)
        {
            if (guard != null) guards.Remove(guard);
        }

        /// Freeze Time.timeScale for `duration` realtime seconds, restoring afterward.
        /// Typical values: duration 0.04-0.15s for hit feedback, frozenScale 0-0.05.
        /// No-op if any registered guard returns true.
        public static void Freeze(float duration, float frozenScale = 0f)
        {
            if (duration <= 0f) return;
            if (IsBlocked()) return;
            RaiseStarted(duration);
            EnsureRunner();
            runner.Request(duration, Mathf.Max(0f, frozenScale));
        }

        /// Convenience overload: emit a camera impulse alongside the freeze. Either both
        /// fire or both are skipped by the guard check, keeping shake and freeze in sync.
        public static void Freeze(float duration, ImpulseEmitter shakeEmitter, float shakeForce = 1f, float frozenScale = 0f)
        {
            if (IsBlocked()) return;
            if (shakeEmitter != null && shakeForce > 0f) shakeEmitter.Emit(shakeForce);
            if (duration <= 0f) return;
            RaiseStarted(duration);
            EnsureRunner();
            runner.Request(duration, Mathf.Max(0f, frozenScale));
        }

        private static void RaiseStarted(float duration)
        {
            var handler = OnFreezeStarted;
            if (handler == null) return;
            // Isolate handlers: one throwing subscriber must not block the others or the freeze.
            foreach (Action<float> h in handler.GetInvocationList())
            {
                try { h(duration); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private static bool IsBlocked()
        {
            for (int i = 0; i < guards.Count; i++)
            {
                var g = guards[i];
                if (g != null && g()) return true;
            }
            return false;
        }

        private static void EnsureRunner()
        {
            if (runner != null) return;
            var go = new GameObject("[HitStopService]");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            runner = go.AddComponent<Runner>();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnEnterPlayMode]
        private static void OnEnterPlayMode()
        {
            // Reset static state when "Reload Domain" is disabled in Project Settings so
            // guards and the runner reference don't leak from a prior play session.
            runner = null;
            guards.Clear();
            OnFreezeStarted = null;
        }
#endif
    }
}
