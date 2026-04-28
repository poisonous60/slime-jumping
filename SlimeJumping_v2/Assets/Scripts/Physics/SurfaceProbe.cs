using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Abstract surface contact detector. Subclasses implement two hooks:
    ///   - PopulateContacts: how per-collider contacts are produced from the broad-phase list.
    ///   - DepenetratePass:  one iter of Phase A push (how depenetration queries the colliders).
    /// The rest (SelectPrimary, EffectiveNormal, ProjectVelocityAgainstContacts, Phase B, diagnostics)
    /// is shared so swapping the mode at runtime does not drift from the shared behavior.
    /// </summary>
    public abstract class SurfaceProbe
    {
        protected readonly Rigidbody2D rb;
        protected readonly SlimeSettings settings;
        protected readonly List<Collider2D> ignoredColliders;

        protected readonly Collider2D[] overlapBuffer = new Collider2D[16];
        protected readonly Collider2D[] depenetrationBuffer = new Collider2D[16];

        // Per-frame contact data (reused to avoid GC)
        protected readonly Collider2D[] contactColliders = new Collider2D[16];
        protected readonly Vector2[] contactNormals = new Vector2[16];
        protected readonly float[] contactDistances = new float[16];
        protected readonly bool[] contactActive = new bool[16];
        // True for contacts whose closest-point clamped to a polygon vertex (i.e. the
        // contact is a corner-bisector, not a real edge-perpendicular). Excluded from
        // EffectiveNormal so corner artefacts don't fake a 20–45° slope on flat ground.
        protected readonly bool[] contactClamped = new bool[16];
        // Per-contact ceiling threshold (cos of ceilingAngle). Resolved from each contact's
        // SurfaceTag in CheckSurface so different materials at a multi-contact corner each
        // get their own attachable arc — Step 1.5 ceiling filter and EffectiveNormal use
        // this instead of a single global threshold.
        protected readonly float[] contactCeilingThreshold = new float[16];
        protected int contactCount;

        // Diagnostic state
        protected int activeCount;
        protected int selectedIdx = -1;
        protected int phaseAIterCount;
        protected float phaseARemainPen;
        // Last applied Phase A push (overwritten each DepenetratePass call). Reset
        // to zero by SnapToSurface before the iter loop. Diagnostics read these to
        // verify the smoothed depenetration converges to ~0 push in steady state.
        protected Vector2 lastDepenPushDir;
        protected float lastDepenPushMag;

        // Cached gravity context from last CheckSurface (used by EffectiveNormal
        // and ProjectVelocityAgainstContacts — they run after CheckSurface in the same frame).
        protected Vector2 lastGravityUp = Vector2.up;
        // Broadphase detect range used by the most recent CheckSurface call. Edge-enumerating
        // probes filter contacts to within (slimeRadius + lastDetectRange) so they match the
        // broadphase OverlapCircle radius and don't overflow the contact buffer with far edges
        // of large polygons.
        protected float lastDetectRange;

        // Diagnostic accessors
        public int ContactCount => contactCount;
        public int ActiveContactCount => activeCount;
        public int SelectedIndex => selectedIdx;
        public int PhaseAIterations => phaseAIterCount;
        public float PhaseARemainingPenetration => phaseARemainPen;
        public Vector2 LastDepenPushDir => lastDepenPushDir;
        public float LastDepenPushMag => lastDepenPushMag;
        public Collider2D GetContactCollider(int i) => contactColliders[i];
        public Vector2 GetContactNormal(int i) => contactNormals[i];
        public float GetContactDistance(int i) => contactDistances[i];
        public bool IsContactActive(int i) => contactActive[i];

        // Finding 1: preserve last valid normal for deep penetration fallback
        protected Vector2 lastGoodNormal = Vector2.up;

        protected SurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
        {
            this.rb = rb;
            this.settings = settings;
            this.ignoredColliders = ignoredColliders;
        }

        // Called on runtime mode swap. Without this, a new probe starts with
        // lastGoodNormal=Vector2.up — a slime attached to a ceiling/wall at swap
        // time would fall back to "up" on the next deep-penetration frame and
        // get snapped off its surface.
        public void TransferStateFrom(SurfaceProbe other)
        {
            if (other == null) return;
            this.lastGoodNormal = other.lastGoodNormal;
            this.lastGravityUp = other.lastGravityUp;
        }

        public bool CheckSurface(out Vector2 normal, out Collider2D col,
            float distMultiplier, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
            float radius = settings.slimeRadius;
            float detectRange = settings.surfaceCheckDistance * distMultiplier;
            LayerMask mask = settings.groundLayer;
            Vector2 pos = rb.position;

            lastGravityUp = gravityUp;
            lastDetectRange = detectRange;

            normal = gravityUp;
            col = null;
            contactCount = 0;

            // Find all nearby colliders
            int overlapCount = Physics2D.OverlapCircleNonAlloc(pos, radius + detectRange, overlapBuffer, mask);

            bool hasNearby = PopulateContacts(pos, overlapCount);

            if (contactCount == 0)
            {
                activeCount = 0;
                selectedIdx = -1;
                // Edge case: all nearby colliders deeply penetrated
                // Use prevCollider with last known normal; otherwise treat as no surface
                if (hasNearby && prevCollider != null)
                {
                    col = prevCollider;
                    normal = lastGoodNormal;
                    return true;
                }
                return false;
            }

            // Resolve per-contact ceiling threshold from each collider's SurfaceTag.
            // SelectPrimary's ceiling filter and EffectiveNormal both index into this array
            // so a sticky overhang at 130° can mix with a normal-tagged 30° floor without
            // the global threshold dictating a uniform answer.
            for (int i = 0; i < contactCount; i++)
            {
                SurfaceTag.ResolveCeilingThresholds(contactColliders[i], settings,
                    out contactCeilingThreshold[i], out _);
            }

            // Identify active contacts (within slimeRadius + contactSkin)
            // and select primary using 5-step fallback
            int selected = SelectPrimary(radius, settings.contactSkin, inputX, prevCollider, gravityUp, gravityRight);
            selectedIdx = selected;
            normal = contactNormals[selected];
            col = contactColliders[selected];
            lastGoodNormal = normal;
            return true;
        }

        /// <summary>
        /// Populate contactColliders/contactNormals/contactDistances from the broad-phase
        /// overlap list in overlapBuffer[0..overlapCount). Returns true if any non-ignored
        /// collider was seen (used to decide the deep-penetration fallback path).
        /// Subclasses decide how many contacts per collider to emit.
        /// </summary>
        protected abstract bool PopulateContacts(Vector2 pos, int overlapCount);

        private int SelectPrimary(float radius, float contactSkin, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
            // Count active contacts and find closest overall
            int localActiveCount = 0;
            int firstActive = -1;
            int closestIdx = 0;
            float closestDist = contactDistances[0];

            for (int i = 0; i < contactCount; i++)
            {
                if (contactDistances[i] < closestDist)
                {
                    closestDist = contactDistances[i];
                    closestIdx = i;
                }
                bool isActive = contactDistances[i] <= radius + contactSkin;
                contactActive[i] = isActive;
                if (isActive)
                {
                    localActiveCount++;
                    if (firstActive < 0) firstActive = i;
                }
            }
            activeCount = localActiveCount;

            // No active contacts — select closest (landing detection)
            if (localActiveCount == 0) return closestIdx;

            // Step 1: Single active contact — no choice needed
            if (localActiveCount == 1) return firstActive;

            // Step 1.5: Ceiling filter — when active contacts mix ceiling and
            // non-ceiling, exclude ceiling contacts from steps 2-5.
            // Prevents a marginally-deeper ceiling from winning over a valid floor.
            // "Ceiling" is per-contact: each collider's SurfaceTag may override the
            // attachable arc, so a sticky-tagged overhang stays attachable while an
            // adjacent normal-tagged surface still treats the same angle as ceiling.
            bool filterCeiling = false;
            {
                int attachableCount = 0;
                int firstAttachable = -1;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (Vector2.Dot(contactNormals[i], gravityUp) >= contactCeilingThreshold[i])
                    {
                        attachableCount++;
                        if (firstAttachable < 0) firstAttachable = i;
                    }
                }
                if (attachableCount > 0 && attachableCount < localActiveCount)
                {
                    filterCeiling = true;
                    firstActive = firstAttachable;
                }
            }

            // Step 1.7: IPlatformEffect priority — if exactly one active non-ceiling
            // contact has an effect on its parent, pick it so effects (JumpPad etc.) fire
            // even when a floor contact is tied or deeper. Multiple effects fall through
            // to normal selection to avoid arbitrary choice between them.
            // Dedupe by IPlatformEffect instance so Multi-probe samples of the same
            // effect-bearing collider count once.
            {
                int effectCount = 0;
                int effectIdx = -1;
                // Fixed 16-slot scratch to avoid GC; loop is O(N^2) but N<=16.
                IPlatformEffect[] seenEffects = selectPrimaryEffectsScratch;
                int seenCount = 0;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                    Collider2D c = contactColliders[i];
                    if (c == null) continue;
                    IPlatformEffect eff = c.GetComponentInParent<IPlatformEffect>();
                    if (eff == null) continue;
                    bool already = false;
                    for (int s = 0; s < seenCount; s++)
                    {
                        if (seenEffects[s] == eff) { already = true; break; }
                    }
                    if (already) continue;
                    if (seenCount < seenEffects.Length) seenEffects[seenCount++] = eff;
                    effectCount++;
                    effectIdx = i;
                }
                // Clear scratch refs to avoid pinning MonoBehaviours
                for (int s = 0; s < seenCount; s++) seenEffects[s] = null;
                if (effectCount == 1) return effectIdx;
            }

            // Step 2: Continuity with penetration guard — if prev primary is still in the
            // active non-ceiling set AND no other contact beats it in penetration by more
            // than primaryContactHysteresis, keep it. Eliminates frame-to-frame primary
            // flipping at corners without letting the primary lock onto a losing surface.
            // With multi-probe, a collider can appear multiple times: pick the deepest
            // prevCollider sample so the hysteresis check uses the best view of prev.
            float hysteresis = settings.primaryContactHysteresis;
            if (prevCollider != null)
            {
                int prevIdx = -1;
                float prevPen = float.MinValue;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                    if (contactColliders[i] == prevCollider)
                    {
                        float pen = radius - contactDistances[i];
                        if (pen > prevPen)
                        {
                            prevPen = pen;
                            prevIdx = i;
                        }
                    }
                }
                if (prevIdx >= 0)
                {
                    float maxOtherPen = float.MinValue;
                    for (int i = 0; i < contactCount; i++)
                    {
                        if (i == prevIdx) continue;
                        if (!contactActive[i]) continue;
                        if (contactColliders[i] == prevCollider) continue; // other samples of same collider don't count as "other"
                        if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                        float pen = radius - contactDistances[i];
                        if (pen > maxOtherPen) maxOtherPen = pen;
                    }
                    if (maxOtherPen - prevPen <= hysteresis) return prevIdx;
                }
            }

            // Step 3: Penetration wins — deepest penetrating contact
            int deepestIdx = -1;
            float deepestPen = 0f;
            for (int i = 0; i < contactCount; i++)
            {
                if (!contactActive[i]) continue;
                if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                float pen = radius - contactDistances[i];
                if (pen > deepestPen)
                {
                    deepestPen = pen;
                    deepestIdx = i;
                }
            }
            if (deepestIdx >= 0 && deepestPen > 0.001f) return deepestIdx;

            // Step 4: Input tiebreaker — surface slime is pressing into
            if (Mathf.Abs(inputX) > 0.01f)
            {
                Vector2 inputDir = (gravityRight * inputX).normalized;
                int bestIdx = -1;
                float bestDot = float.MinValue;
                float secondDot = float.MinValue;

                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                    float d = -Vector2.Dot(contactNormals[i], inputDir);
                    if (d > bestDot)
                    {
                        secondDot = bestDot;
                        bestDot = d;
                        bestIdx = i;
                    }
                    else if (d > secondDot)
                    {
                        secondDot = d;
                    }
                }

                if (bestIdx >= 0 && bestDot - secondDot > 0.01f) return bestIdx;
            }

            // Step 5: Gravity alignment — most floor-like
            // (Continuity check from previous design is now handled by Step 2 hysteresis.)
            {
                int bestIdx = firstActive;
                float bestGrav = float.MinValue;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < contactCeilingThreshold[i]) continue;
                    float g = Vector2.Dot(contactNormals[i], gravityUp);
                    if (g > bestGrav)
                    {
                        bestGrav = g;
                        bestIdx = i;
                    }
                }
                return bestIdx;
            }
        }

        // Scratch buffer for Step 1.7 effect-instance dedupe. Persisted on the instance
        // so SelectPrimary stays allocation-free.
        private readonly IPlatformEffect[] selectPrimaryEffectsScratch = new IPlatformEffect[16];

        /// <summary>
        /// Penetration-weighted average of active non-ceiling contact normals.
        /// Used as the slide/crawl/aim axis — smooth across polygon vertices and
        /// concave corners where primary selection would flip frame-to-frame.
        /// Returns the fallback (last good normal, typically the primary) when:
        /// no active non-ceiling contacts, or weighted sum ≈ 0 (opposing walls).
        /// </summary>
        public Vector2 EffectiveNormal
        {
            get
            {
                float radius = settings.slimeRadius;
                float contactSkin = settings.contactSkin;
                // Soft band: contacts within (radius + fullBand) contribute, weighted
                // by a smoothstep ramp. Replaces the old hard "active or not" gate.
                // Without the soft band, aC=0 returned stale lastGoodNormal and aC>=1
                // returned a fresh weighted sum — that step discontinuity drove the
                // Ice-curve judder (vel direction flipping every frame as the slime
                // overshot the curve and re-touched it). multiplier=1 reproduces the
                // legacy hard threshold.
                float fullBand = contactSkin * Mathf.Max(1f, settings.effectiveNormalSoftBandMultiplier);
                float rampWidth = fullBand * Mathf.Clamp01(settings.effectiveNormalWeightRampBand);
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < contactCount; i++)
                {
                    if (contactClamped[i]) continue;
                    if (Vector2.Dot(contactNormals[i], lastGravityUp) < contactCeilingThreshold[i]) continue;
                    float w = radius + fullBand - contactDistances[i];
                    if (w <= 0f) continue;
                    if (rampWidth > 1e-6f)
                    {
                        float t = Mathf.Clamp01(w / rampWidth);
                        // smoothstep(0,1,t) = t*t*(3-2t)
                        w *= t * t * (3f - 2f * t);
                    }
                    sum += contactNormals[i] * w;
                }
                if (sum.sqrMagnitude < 1e-6f) return lastGoodNormal;
                return sum.normalized;
            }
        }

        /// <summary>
        /// Velocity clipping for surface contacts.
        ///
        /// Step 1 (always): full clip into fallbackNormal. fallbackNormal is the
        ///   smoothed primary surface axis; this provides the stable "no into-surface"
        ///   constraint. Even when no contact is in the soft band (slime briefly
        ///   detached from curve at high speed), this still anchors velocity to be
        ///   tangent to the smoothed primary.
        ///
        /// Step 2: weighted multi-clip ONLY against contacts whose normal differs
        ///   from fallbackNormal by more than acos(projectVelocitySameDirectionDot)
        ///   degrees. Same-direction contacts are multi-probe samples of the same
        ///   surface — clipping against them produces step changes in vel as the
        ///   active set toggles (the Ice judder). Different-direction contacts
        ///   (true corners, opposing walls) still get multi-clipped, soft-banded.
        ///
        /// Two Gauss-Seidel passes converge for typical 2-3 contact corners.
        /// </summary>
        public void ProjectVelocityAgainstContacts(ref Vector2 v, Vector2 fallbackNormal, int passes = 2)
        {
            // Step 1: primary-axis clip (always).
            float vnFb = Vector2.Dot(v, fallbackNormal);
            if (vnFb < 0f) v -= fallbackNormal * vnFb;

            // Step 2: multi-clip against non-aligned contacts only.
            float radius = settings.slimeRadius;
            float contactSkin = settings.contactSkin;
            float fullBand = contactSkin * Mathf.Max(1f, settings.effectiveNormalSoftBandMultiplier);
            float rampWidth = fullBand * Mathf.Clamp01(settings.effectiveNormalWeightRampBand);
            float sameDirDot = Mathf.Clamp01(settings.projectVelocitySameDirectionDot);

            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < contactCount; i++)
                {
                    Vector2 n = contactNormals[i];
                    // Skip same-surface samples — fallbackNormal already constrained that axis.
                    if (Vector2.Dot(n, fallbackNormal) > sameDirDot) continue;

                    float w = radius + fullBand - contactDistances[i];
                    if (w <= 0f) continue;
                    float weight;
                    if (rampWidth > 1e-6f)
                    {
                        float t = Mathf.Clamp01(w / rampWidth);
                        weight = t * t * (3f - 2f * t);
                    }
                    else
                    {
                        weight = 1f;
                    }
                    if (weight < 1e-4f) continue;

                    float vn = Vector2.Dot(v, n);
                    if (vn < 0f) v -= n * (vn * weight);
                }
            }
        }

        public void SnapToSurface(Collider2D surfaceCollider, Vector2 surfaceNormal)
        {
            if (surfaceCollider == null) return;

            float radius = settings.slimeRadius;
            float contactSkin = settings.contactSkin;
            Vector2 pos = rb.position;

            // Phase A: Depenetrate against all nearby colliders (push only).
            // Implementation delegates to DepenetratePass for single- vs multi-probe style.
            lastDepenPushDir = Vector2.zero;
            lastDepenPushMag = 0f;
            bool anyPush = false;
            int iterationsRun = 0;
            for (int iter = 0; iter < 3; iter++)
            {
                iterationsRun++;
                bool pushed = DepenetratePass(ref pos);
                if (pushed) anyPush = true;
                if (!pushed) break;
            }

            // Diagnostic: measure remaining penetration after Phase A
            phaseAIterCount = iterationsRun;
            phaseARemainPen = 0f;
            if (anyPush)
            {
                int postCount = Physics2D.OverlapCircleNonAlloc(pos, radius + settings.depenetrationSkin, depenetrationBuffer, settings.groundLayer);
                for (int i = 0; i < postCount; i++)
                {
                    if (ignoredColliders.Contains(depenetrationBuffer[i])) continue;
                    Vector2 c = depenetrationBuffer[i].ClosestPoint(pos);
                    Vector2 d = pos - c;
                    float dist = d.magnitude;
                    if (dist < 0.0001f) continue;
                    if (dist < radius)
                        phaseARemainPen += radius - dist;
                }
            }

            // Phase B: Primary pull — anchor to primary surface every frame so the
            // slime sits on the chord-offset boundary instead of bouncing between
            // "barely penetrating (Phase A pushes)" and "just outside (no push)".
            // Without this, smoothed Phase A leaves a 50 Hz magnitude oscillation
            // because some frames have penetration to fix and some don't.
            // ClosestPoint on a PolygonCollider2D is continuous across edges (only
            // the edge-normal flips at vertices, not the closest point itself), so
            // pulling to closest+(pos-closest).normalized*radius gives a continuous
            // anchor. Skin tolerance keeps Phase B from undoing tiny Phase A pushes.
            {
                Vector2 closest = surfaceCollider.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float dist = toSlime.magnitude;

                if (dist > radius + contactSkin && dist > 0.0001f)
                {
                    pos = closest + (toSlime / dist) * radius;
                }
            }

            rb.position = pos;
        }

        /// <summary>
        /// One iteration of Phase A depenetration. Should re-query overlapping ground-layer
        /// colliders at the current pos, compute per-collider push vectors, and apply them
        /// (possibly combined across multi-probe samples). Returns true if any push was
        /// applied this iteration.
        /// </summary>
        protected abstract bool DepenetratePass(ref Vector2 pos);

        public static float GetSurfaceAngle(Vector2 surfaceNormal, Vector2 gravityUp)
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp))) * Mathf.Rad2Deg;
        }
    }
}
