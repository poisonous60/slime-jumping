using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Drops lightning bolts straight down from anchor points, in list order, each a `gap` behind the previous one.</summary>
    public class LightningStrikeField : MonoBehaviour
    {
        [System.Serializable]
        public class Strike
        {
            [Tooltip("Sky point the bolt falls from. Empty = this field's own transform.")]
            public Transform anchor;
            [Tooltip("Seconds after the previous strike in the list. 0 = fires together with it.")]
            public float gap = 0.3f;
        }

        [SerializeField] private LightningStrikeConfig config;
        [Tooltip("Cinemachine impulse source emitted on slime hit. Leave null to skip. Scene ref, so it lives here instead of the config.")]
        [SerializeField] private ImpulseEmitter hitShakeEmitter;

        [Tooltip("Seconds before the first cycle starts.")]
        [SerializeField] private float startDelay;
        [Tooltip("Off = the sequence plays once and stops.")]
        [SerializeField] private bool repeat = true;
        [Tooltip("Pause after the last strike before the sequence restarts. Cycle length = sum of gaps + this.")]
        [SerializeField] private float cycleGap = 1f;
        [SerializeField] private List<Strike> strikes = new List<Strike>();

        // Own clock so a WorldTimeFreeze does not dump every pending bolt at once on resume.
        private float clock;
        private int cursor;
        private float nextTime;

        // Bolts of one strike after the first, waiting out their stagger.
        private struct PendingBolt
        {
            public float time;
            public Vector3 origin;
            public Vector3 landing;
        }
        private readonly List<PendingBolt> pending = new List<PendingBolt>(8);
        private bool telegraphed;

        void OnEnable()
        {
            if (config == null)
            {
                Debug.LogError($"{name}: LightningStrikeField has no config assigned — disabling.", this);
                enabled = false;
                return;
            }

            clock = 0f;
            cursor = 0;
            telegraphed = false;
            pending.Clear();
            nextTime = startDelay + GapAt(0);
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            if (strikes.Count == 0) return;
            clock += Time.deltaTime;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (clock < pending[i].time) continue;
                SpawnBolt(pending[i].origin, pending[i].landing, false);
                pending.RemoveAt(i);
            }

            TryTelegraph();

            // Loop, not if — gap 0 rows must all go off on the same frame as their predecessor.
            while (cursor < strikes.Count && clock >= nextTime)
            {
                Fire(cursor);
                cursor++;

                if (cursor < strikes.Count)
                {
                    nextTime += GapAt(cursor);
                    // A gap-0 row was already telegraphed with its predecessor's group.
                    if (GapAt(cursor) > 0f) telegraphed = false;
                }
                else if (repeat)
                {
                    // Clamped so an all-zero pattern still advances the clock instead of spinning.
                    nextTime += Mathf.Max(0.02f, cycleGap) + GapAt(0);
                    cursor = 0;
                    telegraphed = false;
                }
                else
                {
                    nextTime = float.MaxValue;
                }
            }
        }

        private float GapAt(int i) => Mathf.Max(0f, strikes[i].gap);

        // Marks where the next strike lands, `telegraphLead` ahead of it. A run of gap-0 rows
        // shares one fire time, so they all light up together.
        // ponytail: lead longer than the gap just spawns late; no borrowing from the previous strike's slot.
        private void TryTelegraph()
        {
            if (telegraphed || cursor >= strikes.Count) return;
            if (config.telegraphFilamentCount <= 0) return;
            if (clock < nextTime - Mathf.Max(0f, config.telegraphLead)) return;

            for (int i = cursor; i < strikes.Count; i++)
            {
                SpawnTelegraph(LandingPoint(OriginAt(i)));
                if (i + 1 < strikes.Count && GapAt(i + 1) > 0f) break;
            }
            telegraphed = true;
        }

        private void SpawnTelegraph(Vector3 point)
        {
            var go = new GameObject("LightningTelegraph");
            go.AddComponent<LightningTelegraphSpark>().Init(config,
                new Vector3(point.x, point.y, point.z + config.telegraphZOffset), config.telegraphLead);
        }

        // One strike = a short burst of bolts off the same anchor; only the first goes out now.
        private void Fire(int index)
        {
            Vector3 origin = OriginAt(index);
            int bolts = Mathf.Max(1, config.boltsPerStrike);
            float stagger = Mathf.Max(0f, config.boltStagger);
            float t = clock;

            for (int k = 0; k < bolts; k++)
            {
                Vector3 landing = LandingPoint(origin + Vector3.right * Random.Range(-config.boltSpread, config.boltSpread));
                // Only the leading bolt carries impact VFX/SFX/push; the rest are visual.
                if (k == 0)
                {
                    SpawnBolt(origin, landing, true);
                    continue;
                }

                float step = stagger * (1f + Random.Range(-config.boltStaggerJitter, config.boltStaggerJitter));
                t += Mathf.Max(0f, step);
                pending.Add(new PendingBolt { time = t, origin = origin, landing = landing });
            }
        }

        private void SpawnBolt(Vector3 origin, Vector3 landing, bool canHit)
        {
            var go = new GameObject("LightningBolt");
            go.transform.position = origin;
            go.AddComponent<LightningBolt>().Init(config, hitShakeEmitter, origin, landing, canHit);
        }

        // Landing point only shapes the path; the bolt's own tip sweep decides what it hits,
        // so a slime standing under the column halts it above the ground.
        public Vector3 LandingPoint(Vector3 origin)
        {
            float len = Mathf.Max(0.1f, config.maxLength);
            var probe = Physics2D.Raycast(origin, Vector2.down, len, config.groundMask);
            // distance 0 = the anchor sits inside that collider; its reported point is not below us.
            return probe.collider != null && probe.distance > 0f
                ? new Vector3(probe.point.x, probe.point.y, origin.z)
                : origin + Vector3.down * len;
        }

        // --- Authoring surface (custom inspector / scene labels) ---

        public LightningStrikeConfig Config => config;
        public IReadOnlyList<Strike> Strikes => strikes;
        public float StartDelay => startDelay;

        /// Absolute fire time (sec from enable) of strike i, following the list order.
        public float FireTimeAt(int index)
        {
            float t = startDelay;
            for (int i = 0; i <= index && i < strikes.Count; i++) t += GapAt(i);
            return t;
        }

        public Vector3 OriginAt(int index)
        {
            Transform a = strikes[index].anchor;
            return a != null ? a.position : transform.position;
        }

        void OnDrawGizmosSelected()
        {
            if (config == null) return;
            float r = Mathf.Max(0.01f, config.hitRadius);

            for (int i = 0; i < strikes.Count; i++)
            {
                Vector3 origin = OriginAt(i);
                Vector3 end = LandingPoint(origin);

                Gizmos.color = new Color(0.7f, 0.95f, 1f, 0.9f);
                Gizmos.DrawLine(origin, end);
                Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
                Gizmos.DrawWireSphere(origin, r);
                Gizmos.DrawWireSphere(end, r);

                // Audio falloff is identical for every strike, so preview it on the first one only.
                if (i != 0) continue;
                Gizmos.color = new Color(0.4f, 1f, 0.5f, 0.5f);
                Gizmos.DrawWireSphere(end, Mathf.Max(0f, config.impactAudioFullDistance));
                Gizmos.color = new Color(1f, 0.85f, 0.3f, 0.35f);
                Gizmos.DrawWireSphere(end, Mathf.Max(0f, config.impactAudioCutoffDistance));
            }
        }
    }
}
