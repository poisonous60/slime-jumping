using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Global hitstop. Overlapping Freeze calls extend to the latest end and keep the stronger
    /// scale — never stack, never cut short. timeScale is restored on quit / play-mode exit.
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

            public void Cancel()
            {
                if (active == null) return;
                StopCoroutine(active);
                active = null;
                Time.timeScale = baseTimeScale;
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

        /// Fired when a Freeze passes the guards; arg is realtime seconds.
        public static event Action<float> OnFreezeStarted;

        /// Fired when a PlayImpactFrame passes the guards; independent of OnFreezeStarted.
        public static event Action<float> OnImpactFrameStarted;

        /// Veto predicate; while any returns true Freeze is ignored. Unregister in OnDisable,
        /// else the captured reference leaks across scene reloads.
        public static void AddGuard(Func<bool> guard)
        {
            if (guard != null && !guards.Contains(guard)) guards.Add(guard);
        }

        public static void RemoveGuard(Func<bool> guard)
        {
            if (guard != null) guards.Remove(guard);
        }

        /// End any in-flight freeze now, restoring the base scale — callers about to own timeScale themselves.
        public static void CancelFreeze()
        {
            if (runner != null) runner.Cancel();
        }

        /// Freeze timeScale for `duration` realtime seconds (typical 0.04-0.15s, frozenScale 0-0.05).
        public static void Freeze(float duration, float frozenScale = 0f)
        {
            if (duration <= 0f) return;
            if (IsBlocked()) return;
            RaiseFreezeStarted(duration);
            EnsureRunner();
            runner.Request(duration, Mathf.Max(0f, frozenScale));
        }

        /// Freeze plus camera impulse; the guard skips both together so shake and freeze stay in sync.
        public static void Freeze(float duration, ImpulseEmitter shakeEmitter, float shakeForce = 1f, float frozenScale = 0f)
        {
            if (IsBlocked()) return;
            if (shakeEmitter != null && shakeForce > 0f) shakeEmitter.Emit(shakeForce);
            if (duration <= 0f) return;
            RaiseFreezeStarted(duration);
            EnsureRunner();
            runner.Request(duration, Mathf.Max(0f, frozenScale));
        }

        /// B&amp;W impact-frame visual only, no timeScale change; same guards as Freeze.
        public static void PlayImpactFrame(float duration)
        {
            if (duration <= 0f) return;
            if (IsBlocked()) return;
            RaiseImpactFrameStarted(duration);
        }

        /// Hitstop + impact frame at the same duration; the guard blocks both atomically.
        public static void ImpactHit(float duration, float frozenScale = 0f)
        {
            if (duration <= 0f) return;
            if (IsBlocked()) return;
            RaiseFreezeStarted(duration);
            RaiseImpactFrameStarted(duration);
            EnsureRunner();
            runner.Request(duration, Mathf.Max(0f, frozenScale));
        }

        private static void RaiseFreezeStarted(float duration) => Raise(OnFreezeStarted, duration);
        private static void RaiseImpactFrameStarted(float duration) => Raise(OnImpactFrameStarted, duration);

        private static void Raise(Action<float> handler, float duration)
        {
            if (handler == null) return;
            // Isolate handlers: one throwing subscriber must not block the others.
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
            // With "Reload Domain" off, statics survive play sessions and leak guards + runner.
            runner = null;
            guards.Clear();
            OnFreezeStarted = null;
            OnImpactFrameStarted = null;
        }
#endif
    }
}
