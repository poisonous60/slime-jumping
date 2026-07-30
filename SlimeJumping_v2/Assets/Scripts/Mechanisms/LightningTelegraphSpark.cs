using UnityEngine;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Telegraph for a strike: static electricity crawling over the ground the bolt will hit.
    /// Arcs lift off the surface along a band and drop back onto it, each drawn twice — wide blue halo under a thin core.</summary>
    public class LightningTelegraphSpark : MonoBehaviour
    {
        private const int MinPoints = 4;
        private const int MaxPoints = 7;

        private LightningStrikeConfig cfg;
        private LineRenderer[] cores;
        private LineRenderer[] halos;
        private Vector3[] buffer = new Vector3[MaxPoints];
        private float life, age, nextFlicker;

        public void Init(LightningStrikeConfig config, Vector3 center, float lifeTime)
        {
            cfg = config;
            life = Mathf.Max(0.05f, lifeTime);
            transform.position = center;

            int n = Mathf.Max(1, cfg.telegraphFilamentCount);
            cores = new LineRenderer[n];
            halos = new LineRenderer[n];

            // Both ends taper to a point where the arc meets the ground; the lifted middle carries the width.
            var taper = new AnimationCurve(
                new Keyframe(0f, 0.12f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.12f));
            float width = Mathf.Max(0.002f, cfg.telegraphWidth);

            for (int i = 0; i < n; i++)
            {
                halos[i] = CreateLine("Halo", 100, width * Mathf.Max(1f, cfg.telegraphGlowWidthScale), taper);
                cores[i] = CreateLine("Core", 101, width, taper);
            }

            Rebuild();
        }

        private LineRenderer CreateLine(string label, int order, float width, AnimationCurve taper)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);

            var lr = go.AddComponent<LineRenderer>();
            // Local space so the set stays in the anchor's Z plane and moves as one.
            lr.useWorldSpace = false;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.sortingOrder = order;
            lr.widthCurve = taper;
            lr.widthMultiplier = width;
            lr.material = LightningBolt.SharedMaterial;
            return lr;
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;

            age += Time.deltaTime;
            if (age >= life)
            {
                Destroy(gameObject);
                return;
            }

            nextFlicker -= Time.deltaTime;
            if (nextFlicker <= 0f) Rebuild();
        }

        // Every filament is redrawn from scratch each flicker, and some sit the round out so the
        // burst reads as crackling rather than as a set of lines blinking in place.
        private void Rebuild()
        {
            nextFlicker = Mathf.Max(0.01f, cfg.telegraphFlickerInterval);
            // Ramps toward the strike, so the last crackle before it lands is the loudest.
            float intensity = Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(age / life));

            Color core = cfg.telegraphColor;
            core.a *= intensity;
            Color glow = cfg.telegraphGlowColor;
            glow.a *= intensity;

            for (int i = 0; i < cores.Length; i++)
            {
                if (Random.value > intensity)
                {
                    cores[i].enabled = false;
                    halos[i].enabled = false;
                    continue;
                }

                int count = BuildFilament(i, cores.Length);
                Apply(cores[i], count, core);
                Apply(halos[i], count, glow);
            }
        }

        private void Apply(LineRenderer lr, int count, Color c)
        {
            lr.enabled = true;
            lr.positionCount = count;
            for (int i = 0; i < count; i++) lr.SetPosition(i, buffer[i]);
            lr.startColor = c;
            lr.endColor = c;
        }

        // One arc: rooted at its own spot on the ground band, lifts off and drops back down.
        // Each gets its own root instead of a shared hub, so the set reads as electricity crawling
        // over the surface rather than as a starburst.
        private int BuildFilament(int index, int total)
        {
            int count = Random.Range(MinPoints, MaxPoints + 1);
            float span = Mathf.Max(0.01f, cfg.telegraphFilamentLength) * Random.Range(0.6f, 1.4f);

            // Roots walk the band in slots so the arcs stay spread out instead of clumping,
            // and each one is tilted on its own so the set does not read as one flat row of bows.
            float half = Mathf.Max(0f, cfg.telegraphSpread);
            float rootX = Mathf.Lerp(-half, half, (index + Random.Range(0.1f, 0.9f)) / total);
            float lift = Mathf.Max(0f, cfg.telegraphArcHeight);
            float rootY = Random.value < 0.35f ? Random.Range(0f, lift * 0.5f) : 0f;

            float tilt = Random.Range(-45f, 45f) * Mathf.Deg2Rad;
            float cos = Mathf.Cos(tilt), sin = Mathf.Sin(tilt);
            float run = Random.value < 0.5f ? -span : span;

            float height = lift * Random.Range(0.6f, 1.4f);
            // A few arcs let go of the ground and end in the air, the way a spark whips off a surface.
            float phase = Random.value < 0.3f ? 0.5f : 1f;
            float jitter = span * 0.16f;

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                // sin bow, so the arc leaves and meets the surface flat instead of spiking off it.
                float sag = Mathf.Sin(Mathf.PI * t);
                float lx = run * t + Random.Range(-jitter, jitter) * sag;
                float ly = height * Mathf.Sin(Mathf.PI * t * phase) + Random.Range(-jitter, jitter) * sag;

                float x = rootX + lx * cos - ly * sin;
                float y = rootY + lx * sin + ly * cos;
                buffer[i] = new Vector3(x, Mathf.Max(0f, y), 0f);
            }

            return count;
        }
    }
}
