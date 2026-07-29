using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Physics
{
    /// <summary>Abstract surface contact detector; subclasses supply PopulateContacts + DepenetratePass, the rest is shared.</summary>
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
        // Closest-point clamped to a polygon vertex (corner bisector, not edge-perpendicular).
        // Excluded from EffectiveNormal, else corner artefacts fake a 20–45° slope on flat ground.
        protected readonly bool[] contactClamped = new bool[16];
        // Subset of contactClamped: a low lip within smallLedgeStepHeight of the foot WITH open space
        // above (not a tall/tessellated wall). Skips the velocity clip and the teleport CCD sweep.
        protected readonly bool[] contactLowLedge = new bool[16];
        // Per-contact cos(ceilingAngle) from each contact's SurfaceTag, so mixed materials at one
        // corner each keep their own attachable arc instead of a single global threshold.
        protected readonly float[] contactCeilingThreshold = new float[16];
        protected int contactCount;

        // Diagnostic state
        protected int activeCount;
        protected int selectedIdx = -1;
        protected int phaseAIterCount;
        protected float phaseARemainPen;
        // Last applied Phase A push; SnapToSurface zeroes it before the iter loop.
        protected Vector2 lastDepenPushDir;
        protected float lastDepenPushMag;

        // Cached from last CheckSurface; EffectiveNormal / ProjectVelocityAgainstContacts run after it in the same frame.
        protected Vector2 lastGravityUp = Vector2.up;
        // Broadphase detect range of the last CheckSurface. Edge-enumerating probes filter to
        // (slimeRadius + this) so far edges of large polygons don't overflow the contact buffer.
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
        public bool IsContactLowLedge(int i) => contactLowLedge[i];
        // True if any contact this frame is a small-ledge lip on the given collider (teleport CCD exemption).
        public bool IsLowLedgeCollider(Collider2D c)
        {
            if (c == null) return false;
            for (int i = 0; i < contactCount; i++)
                if (contactLowLedge[i] && contactColliders[i] == c) return true;
            return false;
        }
        // Edge contacts rejected as "buried inside another collider" (Multi probe only; includes fallback-promoted ones).
        public virtual int RejectedBuriedCount => 0;
        // Clamped contacts whose velocity clip was skipped by the small-ledge passthrough.
        public int LowLedgePassthroughCount => lowLedgePassthroughCount;
        protected int lowLedgePassthroughCount;

        // Fallback normal for deep-penetration frames.
        protected Vector2 lastGoodNormal = Vector2.up;

        // --- Body scale (slime-cut: "a cut slime is a smaller slime") ---
        // SlimeController pushes bodyScaleRatio here; radius-derived lengths use EffRadius so collision matches the scaled CircleCollider2D.
        protected float radiusScale = 1f;
        public void SetRadiusScale(float s) { radiusScale = Mathf.Max(0.05f, s); }
        protected float EffRadius => settings.slimeRadius * radiusScale;

        // --- Cut SHAPE (directional reach) ---
        // LEGACY cut-plane slice design: the cut-as-smaller-slime redesign left NO caller for
        // SetCutShape, so cutPlaneCount stays 0 and Reach() is identically EffRadius. Kept because
        // contactReach[] threads through SelectPrimary / EffectiveNormal / ProjectVelocityAgainstContacts.
        protected readonly Vector3[] cutPlanes = new Vector3[3];  // xy = world unit normal (→ removed side), z = off (world, from center)
        protected int cutPlaneCount;
        public void SetCutShape(Vector3[] planes, int count)
        {
            cutPlaneCount = Mathf.Clamp(count, 0, cutPlanes.Length);
            for (int i = 0; i < cutPlaneCount; i++) cutPlanes[i] = planes[i];
        }
        public void ClearCutShape() { cutPlaneCount = 0; }

        // Support length of the cut shape toward a surface whose outward normal is contactNormal.
        protected float Reach(Vector2 contactNormal)
        {
            float R = EffRadius;
            if (cutPlaneCount == 0) return R;
            Vector2 d = -contactNormal;
            float h = R;
            for (int i = 0; i < cutPlaneCount; i++)
            {
                Vector2 n = new Vector2(cutPlanes[i].x, cutPlanes[i].y);
                float off = cutPlanes[i].z;
                float dn = Vector2.Dot(d, n);
                if (dn > 0f && R * dn > off)   // d points toward the removed side AND the cut limits it
                {
                    float planar = off * dn + Mathf.Sqrt(Mathf.Max(0f, R * R - off * off)) * Mathf.Sqrt(Mathf.Max(0f, 1f - dn * dn));
                    if (planar < h) h = planar;
                }
            }
            return Mathf.Max(h, R * 0.1f);
        }
        // Per-contact reach, filled in CheckSurface (parallel to contactNormals).
        protected readonly float[] contactReach = new float[16];

        protected SurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
        {
            this.rb = rb;
            this.settings = settings;
            this.ignoredColliders = ignoredColliders;
        }
        // NOTE: probe-mode swap needs no cut-shape transfer — the controller re-applies
        // splitCutPlanes right after DecideProbeMode() every FixedUpdate.

        // Called on runtime mode swap. Without it a new probe starts lastGoodNormal=up, so a
        // ceiling/wall-attached slime gets snapped off on the next deep-penetration frame.
        public void TransferStateFrom(SurfaceProbe other)
        {
            if (other == null) return;
            this.lastGoodNormal = other.lastGoodNormal;
            this.lastGravityUp = other.lastGravityUp;
            this.radiusScale = other.radiusScale;
        }

        public bool CheckSurface(out Vector2 normal, out Collider2D col,
            float distMultiplier, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
            float radius = EffRadius;   // broadphase / max reach
            float detectRange = settings.surfaceCheckDistance * distMultiplier;
            LayerMask mask = settings.groundLayer;
            Vector2 pos = rb.position;

            lastGravityUp = gravityUp;
            lastDetectRange = detectRange;

            normal = gravityUp;
            col = null;
            contactCount = 0;

            int overlapCount = Physics2D.OverlapCircleNonAlloc(pos, radius + detectRange, overlapBuffer, mask);

            bool hasNearby = PopulateContacts(pos, overlapCount);

            if (contactCount == 0)
            {
                activeCount = 0;
                selectedIdx = -1;
                // All nearby colliders deeply penetrated: fall back to prevCollider + last known normal.
                if (hasNearby && prevCollider != null)
                {
                    col = prevCollider;
                    normal = lastGoodNormal;
                    return true;
                }
                return false;
            }

            // Per-contact ceiling threshold from SurfaceTag, so a sticky 130° overhang can mix
            // with a normal-tagged 30° floor instead of one global answer.
            for (int i = 0; i < contactCount; i++)
            {
                SurfaceTag.ResolveCeilingThresholds(contactColliders[i], settings,
                    out contactCeilingThreshold[i], out _);
            }

            // Directional reach per contact — how far the cut shape extends toward each contact.
            for (int i = 0; i < contactCount; i++)
                contactReach[i] = Reach(contactNormals[i]);

            int selected = SelectPrimary(radius, settings.contactSkin, inputX, prevCollider, gravityUp, gravityRight);
            selectedIdx = selected;
            normal = contactNormals[selected];
            col = contactColliders[selected];
            lastGoodNormal = normal;
            return true;
        }

        /// <summary>Fill the contact arrays from overlapBuffer[0..overlapCount); returns true if any non-ignored collider was seen.</summary>
        protected abstract bool PopulateContacts(Vector2 pos, int overlapCount);

        private int SelectPrimary(float radius, float contactSkin, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
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
                bool isActive = contactDistances[i] <= contactReach[i] + contactSkin;
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

            // Step 1.5: Ceiling filter — when active contacts mix, exclude ceilings from steps 2-5,
            // else a marginally-deeper ceiling wins over a valid floor. Threshold is per-contact.
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

            // Step 1.7: IPlatformEffect priority — exactly one active non-ceiling effect contact wins,
            // so JumpPad etc. fire even when a floor is tied or deeper. Dedupe by effect instance
            // so Multi-probe samples of the same collider count once; multiple effects fall through.
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

            // Step 2: Continuity with penetration guard — keep the prev primary unless another
            // contact beats it by > primaryContactHysteresis; stops primary flipping at corners.
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
                        float pen = contactReach[i] - contactDistances[i];
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
                        float pen = contactReach[i] - contactDistances[i];
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
                float pen = contactReach[i] - contactDistances[i];
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

        // Scratch for Step 1.7 dedupe; instance-held so SelectPrimary stays allocation-free.
        private readonly IPlatformEffect[] selectPrimaryEffectsScratch = new IPlatformEffect[16];

        /// <summary>Penetration-weighted average of active non-ceiling normals (slide/crawl/aim axis); falls back to lastGoodNormal when the sum ≈ 0 (opposing walls).</summary>
        public Vector2 EffectiveNormal
        {
            get
            {
                float contactSkin = settings.contactSkin;
                // Smoothstep soft band instead of a hard active/inactive gate — the step
                // discontinuity drove the Ice-curve judder. multiplier=1 = legacy hard threshold.
                float fullBand = contactSkin * Mathf.Max(1f, settings.effectiveNormalSoftBandMultiplier);
                float rampWidth = fullBand * Mathf.Clamp01(settings.effectiveNormalWeightRampBand);
                Vector2 sum = Vector2.zero;
                for (int i = 0; i < contactCount; i++)
                {
                    if (contactClamped[i]) continue;
                    if (Vector2.Dot(contactNormals[i], lastGravityUp) < contactCeilingThreshold[i]) continue;
                    float w = contactReach[i] + fullBand - contactDistances[i];
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

        /// <summary>Clips v into fallbackNormal, then multi-clips only contacts more than
        /// acos(projectVelocitySameDirectionDot) off it — clipping same-surface samples steps vel as the
        /// active set toggles (Ice judder). contactLowLedge lips skip the clip.</summary>
        public void ProjectVelocityAgainstContacts(ref Vector2 v, Vector2 fallbackNormal, int passes = 2)
        {
            // Step 1: primary-axis clip (always).
            float vnFb = Vector2.Dot(v, fallbackNormal);
            if (vnFb < 0f) v -= fallbackNormal * vnFb;

            // Step 2: multi-clip against non-aligned contacts only.
            float contactSkin = settings.contactSkin;
            float fullBand = contactSkin * Mathf.Max(1f, settings.effectiveNormalSoftBandMultiplier);
            float rampWidth = fullBand * Mathf.Clamp01(settings.effectiveNormalWeightRampBand);
            float sameDirDot = Mathf.Clamp01(settings.projectVelocitySameDirectionDot);
            bool smoothLedges = settings.surfaceProbeSmoothSmallLedges;
            lowLedgePassthroughCount = 0;

            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < contactCount; i++)
                {
                    Vector2 n = contactNormals[i];
                    // Skip same-surface samples — fallbackNormal already constrained that axis.
                    if (Vector2.Dot(n, fallbackNormal) > sameDirDot) continue;

                    float vn = Vector2.Dot(v, n);
                    if (vn >= 0f) continue; // contact doesn't oppose this velocity — nothing to clip.

                    float w = contactReach[i] + fullBand - contactDistances[i];
                    if (w <= 0f) continue; // outside the soft band — wouldn't clip anyway.
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

                    // Small-ledge passthrough: skip the clip so the forward slide survives and Phase A's
                    // radial push lifts the slime over the lip. Counted once per actually-skipped clip.
                    if (smoothLedges && contactLowLedge[i])
                    {
                        if (pass == 0) lowLedgePassthroughCount++;
                        continue;
                    }

                    v -= n * (vn * weight);
                }
            }
        }

        public void SnapToSurface(Collider2D surfaceCollider, Vector2 surfaceNormal)
        {
            if (surfaceCollider == null) return;

            float radius = EffRadius;   // max reach (broadphase post-overlap)
            float contactSkin = settings.contactSkin;
            Vector2 pos = rb.position;

            // Phase A: depenetrate against nearby colliders (push only), via DepenetratePass.
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
                    float reach = Reach(d / dist);
                    if (dist < reach)
                        phaseARemainPen += reach - dist;
                }
            }

            // Phase B: primary pull — anchor to the chord-offset boundary every frame. Without it
            // smoothed Phase A leaves a 50 Hz oscillation between "penetrating" and "just outside".
            {
                Vector2 closest = surfaceCollider.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float dist = toSlime.magnitude;
                // Anchor at the reach, not the full radius, so the cut face rests on the surface.
                float reach = dist > 0.0001f ? Reach(toSlime / dist) : EffRadius;
                // Small-ledge climb: loosen the re-anchor tolerance by stepHeight while a passthrough
                // is active, else the slime can only rise ~contactSkin before Phase B yanks it back.
                float anchorTolerance = reach + contactSkin;
                if (lowLedgePassthroughCount > 0)
                    anchorTolerance += settings.smallLedgeStepHeight;
                if (dist > anchorTolerance && dist > 0.0001f)
                    pos = closest + (toSlime / dist) * reach;
            }

            rb.position = pos;
        }

        /// <summary>One Phase A depenetration iteration: re-query at pos, apply push vectors, return true if any was applied.</summary>
        protected abstract bool DepenetratePass(ref Vector2 pos);

        public static float GetSurfaceAngle(Vector2 surfaceNormal, Vector2 gravityUp)
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp))) * Mathf.Rad2Deg;
        }
    }
}
