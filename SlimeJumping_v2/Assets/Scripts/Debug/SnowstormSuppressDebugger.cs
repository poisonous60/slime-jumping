using System.Reflection;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Debugging
{
    // Read-only probe for the "stage2 snow visible inside the snowstorm" bug.
    // Pulls Snowstorm2D private state via reflection (writes nothing back).
    //
    // Fingerprints promoted to LogWarning:
    //   <<< INSIDE-MISMATCH  zone polygon contains the slime but slimeInside=false
    //                        (early OnTriggerExit / multi-collider flicker)
    //   <<< FLICKER          slimeInside flipped while the polygon-inside state didn't
    //   <<< ALPHA-LEAK       presence==1 (fully inside) but a suppressed renderer's
    //                        MPB alpha is not 0, or its emission rate is not 0
    //   <<< STRAY-SNOW       a snow-material PS that is NOT the storm PS and NOT a
    //                        suppressed PS has alive particles while inside the zone
    [DefaultExecutionOrder(3002)]
    public sealed class SnowstormSuppressDebugger : MonoBehaviour
    {
        const string Tag = "[StormSuppress]";
        const int SteadyLogEveryNFrames = 30;

        SlimeController slime;
        Snowstorm2D storm;
        PolygonCollider2D zonePoly;

        FieldInfo fiPresence;
        FieldInfo fiInside;
        FieldInfo fiSuppressed;

        ParticleSystem stormPs;
        ParticleSystem[] suppressed = new ParticleSystem[0];
        ParticleSystemRenderer[] suppressedRenderers = new ParticleSystemRenderer[0];

        MaterialPropertyBlock mpb;
        static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

        bool lastInside;
        bool hasLastInside;
        int frame;

        void Awake()
        {
            slime = GetComponent<SlimeController>();
            storm = FindFirstObjectByType<Snowstorm2D>(FindObjectsInactive.Exclude);
            mpb = new MaterialPropertyBlock();

            var t = typeof(Snowstorm2D);
            const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
            fiPresence = t.GetField("slimePresenceWeight", F);
            fiInside = t.GetField("slimeInside", F);
            fiSuppressed = t.GetField("suppressedSnowParticles", F);

            if (storm != null)
            {
                zonePoly = storm.GetComponent<PolygonCollider2D>();
                var fiStormPs = t.GetField("stormParticles", F);
                stormPs = fiStormPs != null ? fiStormPs.GetValue(storm) as ParticleSystem : null;
                var arr = fiSuppressed != null ? fiSuppressed.GetValue(storm) as ParticleSystem[] : null;
                if (arr != null)
                {
                    suppressed = arr;
                    suppressedRenderers = new ParticleSystemRenderer[arr.Length];
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null) suppressedRenderers[i] = arr[i].GetComponent<ParticleSystemRenderer>();
                }
            }

            Debug.Log($"{Tag} awake storm={(storm != null ? storm.name : "NULL")} suppressedCount={suppressed.Length} stormPs={(stormPs != null ? stormPs.name : "NULL")}");
        }

        void LateUpdate()
        {
            if (storm == null || slime == null) return;
            frame++;

            float presence = fiPresence != null ? (float)fiPresence.GetValue(storm) : -1f;
            bool inside = fiInside != null && (bool)fiInside.GetValue(storm);
            bool polyInside = zonePoly != null && zonePoly.OverlapPoint(slime.Position);

            bool flipped = hasLastInside && inside != lastInside;
            lastInside = inside;
            hasLastInside = true;

            bool mismatch = polyInside && !inside;
            bool steadyTick = frame % SteadyLogEveryNFrames == 0;

            string suppInfo = "";
            bool alphaLeak = false;
            for (int i = 0; i < suppressed.Length; i++)
            {
                var ps = suppressed[i];
                if (ps == null) continue;
                float rate = ps.emission.rateOverTime.constant;
                int alive = ps.particleCount;
                float alpha = -1f;
                if (suppressedRenderers[i] != null)
                {
                    suppressedRenderers[i].GetPropertyBlock(mpb);
                    alpha = mpb.GetColor(s_BaseColorId).a;
                }
                suppInfo += $" supp{i}({ps.name}) rate={rate:F1} alive={alive} mpbA={alpha:F3};";
                if (presence >= 0.999f && (alpha > 0.001f || rate > 0.001f) && alive > 0)
                    alphaLeak = true;
            }

            if (mismatch)
                Debug.LogWarning($"{Tag} #{frame} <<< INSIDE-MISMATCH polyInside=true slimeInside=false presence={presence:F3} pos={slime.Position}");
            if (flipped && polyInside)
                Debug.LogWarning($"{Tag} #{frame} <<< FLICKER slimeInside {!inside}->{inside} polyInside={polyInside} presence={presence:F3} pos={slime.Position}");
            if (alphaLeak)
                Debug.LogWarning($"{Tag} #{frame} <<< ALPHA-LEAK presence={presence:F3}{suppInfo}");

            if (steadyTick || mismatch || flipped || alphaLeak)
                Debug.Log($"{Tag} #{frame} inside={inside} poly={polyInside} presence={presence:F3}{suppInfo} stormAlive={(stormPs != null ? stormPs.particleCount : -1)}");

            // Once per ~2s while inside: any OTHER snow-material PS with alive particles.
            if (polyInside && frame % 120 == 0)
            {
                foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                {
                    if (ps.particleCount <= 0) continue;
                    if (ps == stormPs) continue;
                    bool isSuppressed = false;
                    for (int i = 0; i < suppressed.Length; i++) if (suppressed[i] == ps) isSuppressed = true;
                    if (isSuppressed) continue;
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r == null || r.sharedMaterial == null) continue;
                    if (!r.sharedMaterial.name.Contains("Snow")) continue;
                    Debug.LogWarning($"{Tag} #{frame} <<< STRAY-SNOW '{GetPath(ps.transform)}' alive={ps.particleCount} mat={r.sharedMaterial.name}");
                }
            }
        }

        static string GetPath(Transform t)
        {
            string p = t.name;
            while (t.parent != null) { t = t.parent; p = t.parent != null || true ? t.name + "/" + p : p; }
            return p;
        }
    }
}
