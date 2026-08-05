using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms.Afterimage
{
    /// <summary>Records afterimage stamps along the slime's path while it is inside the
    /// trigger volume, and fires the shockwave (erase + record-silence + ripple) when the
    /// slime launches off a stamp. Trigger collider must NOT be on the ground layer.</summary>
    [AddComponentMenu("SlimeJumping/Mechanisms/Afterimage Zone")]
    public class AfterimageZone : EnvironmentalZone
    {
        [SerializeField] private AfterimageConfig configOverride;
        [Tooltip("SpaceWarpEffect quads reused round-robin for the shockwave ripple.")]
        [SerializeField] private SpaceWarpEffect[] rippleQuads;
        [Tooltip("Author the harden-block region with this polygon (isTrigger, NOT on the " +
                 "ground layer). It follows the slime; stamps touching it stay ghost. " +
                 "Unassigned = the config box knobs are used instead.")]
        [SerializeField] private PolygonCollider2D hardenBlockShape;

        AfterimageConfig cfg;
        float nextStampTime;
        Vector2 lastStampPos;
        bool hasStamped;
        int rippleIndex;

        struct Silence
        {
            public Vector2 center;
            public float radius;
            public float until;
        }

        // Static so overlapping zones share one silence field.
        static readonly List<Silence> silences = new List<Silence>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => silences.Clear();

        void Awake()
        {
            cfg = AfterimageConfig.Resolve(configOverride);
        }

        protected override void OnSlimeEnter(SlimeController slime)
        {
            nextStampTime = 0f;
            hasStamped = false;
        }

        /// <summary>True while the stamp (circle at pos, radius r) touches the harden-block
        /// region around the tracked slime — polygon shape if assigned, else the config box.</summary>
        public bool BlocksHarden(Vector2 pos, float r)
        {
            var slime = trackedSlime;
            if (slime == null) return false;

            if (hardenBlockShape != null)
            {
                Vector2 closest = hardenBlockShape.ClosestPoint(pos);
                return (closest - pos).sqrMagnitude <= r * r;
            }

            Vector2 up = slime.GravityUp;
            Vector2 right = new Vector2(up.y, -up.x);
            Vector2 to = pos - slime.Position;
            float upComp = Vector2.Dot(to, up);
            float sideComp = Vector2.Dot(to, right);
            return upComp >= -(cfg.hardenBlockBoxBelow + r)
                && upComp <= cfg.hardenBlockBoxAbove + r
                && Mathf.Abs(sideComp) <= cfg.hardenBlockBoxWidth * 0.5f + r;
        }

        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            if (hardenBlockShape != null)
                hardenBlockShape.transform.position = slime.Position;

            if (Time.time < nextStampTime) return;
            Vector2 pos = slime.Position;
            float spacing = cfg.minStampSpacing * slime.SlimeRadius * 2f;
            if (hasStamped && (pos - lastStampPos).sqrMagnitude < spacing * spacing) return;
            if (IsSilenced(pos)) return;

            if (AfterimageStamp.Spawn(this, slime, cfg) == null) return;
            nextStampTime = Time.time + cfg.stampIntervalSeconds;
            lastStampPos = pos;
            hasStamped = true;
        }

        public void TriggerShockwave(Vector2 center)
        {
            if (!cfg.shockwaveEnabled) return;
            float r2 = cfg.shockwaveRadius * cfg.shockwaveRadius;
            for (int i = AfterimageStamp.Active.Count - 1; i >= 0; i--)
            {
                var stamp = AfterimageStamp.Active[i];
                if (((Vector2)stamp.transform.position - center).sqrMagnitude <= r2)
                    stamp.Erase();
            }

            silences.Add(new Silence
            {
                center = center,
                radius = cfg.shockwaveRadius,
                until = Time.time + cfg.silenceDurationSeconds
            });
            PlayRipple(center);
        }

        static bool IsSilenced(Vector2 pos)
        {
            for (int i = silences.Count - 1; i >= 0; i--)
            {
                if (Time.time >= silences[i].until)
                {
                    silences.RemoveAt(i);
                    continue;
                }
                if ((silences[i].center - pos).sqrMagnitude <= silences[i].radius * silences[i].radius)
                    return true;
            }
            return false;
        }

        // Harden-block region: stamps touching it stay ghost. Cyan outline = the region
        // (authored polygon, or the config box fallback); green circle = the slime anchor.
        void OnDrawGizmosSelected()
        {
            var c = cfg != null ? cfg : AfterimageConfig.Resolve(configOverride);
            if (c == null) return;
            Vector3 center = trackedSlime != null ? (Vector3)trackedSlime.Position
                : (hardenBlockShape != null ? hardenBlockShape.transform.position : transform.position);
            Vector2 up = trackedSlime != null ? trackedSlime.GravityUp : Vector2.up;
            Vector2 right = new Vector2(up.y, -up.x);

            Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.9f);
            if (hardenBlockShape != null)
            {
                var t = hardenBlockShape.transform;
                for (int p = 0; p < hardenBlockShape.pathCount; p++)
                {
                    var path = hardenBlockShape.GetPath(p);
                    for (int i = 0; i < path.Length; i++)
                    {
                        Vector3 a = t.TransformPoint(path[i] + hardenBlockShape.offset);
                        Vector3 b = t.TransformPoint(path[(i + 1) % path.Length] + hardenBlockShape.offset);
                        Gizmos.DrawLine(a, b);
                    }
                }
            }
            else
            {
                Vector3 bottomL = center + (Vector3)(-right * (c.hardenBlockBoxWidth * 0.5f) - up * c.hardenBlockBoxBelow);
                Vector3 bottomR = center + (Vector3)(right * (c.hardenBlockBoxWidth * 0.5f) - up * c.hardenBlockBoxBelow);
                Vector3 topL = bottomL + (Vector3)(up * (c.hardenBlockBoxBelow + c.hardenBlockBoxAbove));
                Vector3 topR = bottomR + (Vector3)(up * (c.hardenBlockBoxBelow + c.hardenBlockBoxAbove));
                Gizmos.DrawLine(bottomL, bottomR);
                Gizmos.DrawLine(bottomR, topR);
                Gizmos.DrawLine(topR, topL);
                Gizmos.DrawLine(topL, bottomL);
            }

            float slimeR = trackedSlime != null ? trackedSlime.SlimeRadius : 0.4f;
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
            Gizmos.DrawWireSphere(center, slimeR);
        }

        void PlayRipple(Vector2 center)
        {
            if (rippleQuads == null || rippleQuads.Length == 0) return;
            var quad = rippleQuads[rippleIndex % rippleQuads.Length];
            rippleIndex++;
            if (quad == null) return;

            var t = quad.transform;
            t.position = new Vector3(center.x, center.y, t.position.z);
            float size = 2f * cfg.shockwaveRadius;
            t.localScale = new Vector3(size, size, 1f);
            quad.PulseStrength = cfg.rippleStrength;
            quad.PulseDuration = cfg.rippleDurationSeconds;
            quad.Trigger();
        }
    }
}
