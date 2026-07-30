using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Drops lightning bolts straight down from anchor points on a shared loop, each anchor with its own phase offset.</summary>
    public class LightningStrikeField : MonoBehaviour
    {
        [System.Serializable]
        public class Strike
        {
            [Tooltip("Sky point the bolt falls from. Empty = this field's own transform.")]
            public Transform anchor;
            [Tooltip("Phase offset (sec) within the cycle. Two anchors with the same value fire together.")]
            public float delay;
        }

        [SerializeField] private LightningStrikeConfig config;
        [Tooltip("Cinemachine impulse source emitted on slime hit. Leave null to skip. Scene ref, so it lives here instead of the config.")]
        [SerializeField] private ImpulseEmitter hitShakeEmitter;
        [Tooltip("Seconds before the first cycle starts.")]
        [SerializeField] private float startDelay;
        [SerializeField] private List<Strike> strikes = new List<Strike>();

        // Own clock so a WorldTimeFreeze does not dump every pending bolt at once on resume.
        private float clock;
        private float[] nextTime;

        void OnEnable()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: LightningStrikeField has no config assigned — disabling.", this);
                enabled = false;
                return;
            }

            clock = 0f;
            nextTime = new float[strikes.Count];
            for (int i = 0; i < strikes.Count; i++)
                nextTime[i] = startDelay + Mathf.Max(0f, strikes[i].delay);
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            clock += Time.deltaTime;

            for (int i = 0; i < nextTime.Length; i++)
            {
                if (clock < nextTime[i]) continue;
                Fire(i);
                nextTime[i] = config.repeat
                    ? nextTime[i] + Mathf.Max(0.02f, config.period)
                    : float.MaxValue;
            }
        }

        private void Fire(int i)
        {
            Transform a = strikes[i].anchor;
            Vector3 origin = a != null ? a.position : transform.position;
            float len = Mathf.Max(0.1f, config.maxLength);

            // Landing point only shapes the path; the bolt's own tip sweep decides what it hits,
            // so a slime standing under the column halts it above the ground.
            var probe = Physics2D.Raycast(origin, Vector2.down, len, config.groundMask);
            Vector3 end = probe.collider != null
                ? new Vector3(probe.point.x, probe.point.y, origin.z)
                : origin + Vector3.down * len;

            var go = new GameObject("LightningBolt");
            go.transform.position = origin;
            go.AddComponent<LightningBolt>().Init(config, hitShakeEmitter, origin, end);
        }

        void OnDrawGizmosSelected()
        {
            if (config == null) return;
            float len = Mathf.Max(0.1f, config.maxLength);
            float r = Mathf.Max(0.01f, config.hitRadius);

            for (int i = 0; i < strikes.Count; i++)
            {
                Transform a = strikes[i].anchor;
                Vector3 origin = a != null ? a.position : transform.position;
                var probe = Physics2D.Raycast(origin, Vector2.down, len, config.groundMask);
                Vector3 end = probe.collider != null
                    ? new Vector3(probe.point.x, probe.point.y, origin.z)
                    : origin + Vector3.down * len;

                Gizmos.color = new Color(0.7f, 0.95f, 1f, 0.9f);
                Gizmos.DrawLine(origin, end);
                Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
                Gizmos.DrawWireSphere(origin, r);
                Gizmos.DrawWireSphere(end, r);
            }
        }
    }
}
