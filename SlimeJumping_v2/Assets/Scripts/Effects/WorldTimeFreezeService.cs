using System;
using System.Collections;
using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Selective world freeze: never touches <c>Time.timeScale</c>, only flips <see cref="IsFrozen"/>
    /// and raises events — cooperators early-return on the flag, everything else keeps running.
    /// Overlapping <see cref="Freeze"/> calls extend to the latest end time; static state is reset on
    /// quit and play-mode exit so a stuck freeze cannot leak across sessions.
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
