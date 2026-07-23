using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Piano
{
    /// Manager for a playable piano. Each FixedUpdate it figures out which white keys the slime is
    /// resting on and drives them. Detection is by HORIZONTAL OVERLAP: a key is pressed when the
    /// slime's body overlaps the key's x-span by more than overlapThreshold AND the slime is sitting
    /// near the key's top. That means a slime landing in the seam between two keys presses BOTH.
    /// The keys themselves are PlatformMotors that sink and carry the slime down (see PianoKey).
    ///
    /// Runs before the key motors (lower execution order) so each key reads fresh contact this frame.
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("SlimeJumping/Platforms/Piano Keyboard")]
    public partial class PianoKeyboard : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private PianoConfig config;
        [SerializeField] private List<PianoKey> keys = new List<PianoKey>();

        public PianoConfig Config => config;
        public List<PianoKey> Keys => keys;

        public void SetSlime(SlimeController s) { slime = s; }
        public void SetKeys(List<PianoKey> k) { keys = k; }
        public void SetConfig(PianoConfig c) { config = c; }

        private Collider2D slimeBody;
        private float slimeRadius = 0.2f;

        void Awake() => ResolveSlime();

        private void ResolveSlime()
        {
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (slime == null) return;
            var cols = slime.GetComponents<Collider2D>();
            for (int i = 0; i < cols.Length; i++)
                if (!cols[i].isTrigger) { slimeBody = cols[i]; slimeRadius = cols[i].bounds.extents.x; break; }
        }

        void FixedUpdate()
        {
            if (config == null) return;
            if (slime == null || slimeBody == null) { ResolveSlime(); if (slimeBody == null) return; }

            Vector2 sc = slime.Position;
            float sMinX = sc.x - slimeRadius;
            float sMaxX = sc.x + slimeRadius;
            float sBottom = sc.y - slimeRadius;
            float impact = Mathf.Max(slime.ImpactNormalSpeed, slime.Velocity.magnitude);

            var s = config.keys;
            float overlapThreshold = config.overlapThreshold;
            float verticalTolerance = config.verticalTolerance;

            bool harmonizeOn = config.harmonize.enabled;
            bool anyContacting = false;
            if (harmonizeOn) contactingKeys.Clear();

            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (key == null || key.Contact == null) continue;

                Bounds b = key.Contact.bounds;
                float overlap = Mathf.Min(sMaxX, b.max.x) - Mathf.Max(sMinX, b.min.x);
                bool contacting = overlap > overlapThreshold
                                  && Mathf.Abs(sBottom - b.max.y) < verticalTolerance;

                if (contacting)
                {
                    anyContacting = true;
                    if (harmonizeOn) contactingKeys.Add(key);
                }
                key.Drive(contacting, impact, s);
            }

            HarmonizeStep(anyContacting, impact);   // no-op unless config.harmonize.enabled
        }
    }
}
