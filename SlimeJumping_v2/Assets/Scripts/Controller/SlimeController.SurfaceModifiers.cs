using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;
using SlimeJumping.Platforms.Snow;
using System.Collections.Generic;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        private SurfaceModifiers surfaceMod = SurfaceModifiers.Default;

        // Snow gameplay coupling (read-only pull from SnowFieldRegistry).
        // hostSurfaceMod: the unblended host SurfaceTag → modifiers, captured at attach.
        // cachedSnowField + cachedSnowMods: pre-resolved snow side, refreshed at attach.
        // Each FixedUpdate while attached, RefreshSnowBlend samples whether the slime's
        // contact xy is over a snow patch; if yes, surfaceMod = snow tag, otherwise host.
        // Pure binary — no depth-graduated blend, no plow drag.
        private SurfaceModifiers hostSurfaceMod = SurfaceModifiers.Default;
        private SurfaceMaterial hostSurfaceMaterial;
        private bool snowBlendActive;
        private SnowField cachedSnowField;
        private SurfaceModifiers cachedSnowMods;
        private bool hasCachedSnowMods;
        // Footprint hit captured by RefreshSnowBlend each FU and read by
        // ApplySnowDrag in the same FU — lets the drag path skip its own
        // nearest-vertex sweep and just index into the strip arrays. Null when
        // the slime isn't on snow this FU. Lifetime: one FU only.
        private SnowStrip cachedFootprintStrip;
        private int cachedFootprintVertIdx;

        // Resolved gameplay-active SurfaceMaterial: override when a zone has pushed one,
        // else snow when the contact-point depth gate has flipped surfaceMod to snow,
        // otherwise the host SurfaceTag's material. Null while airborne / no surface.
        public SurfaceMaterial ActiveSurfaceMaterial => !isTouchingSurface
            ? null
            : (ActiveSurfaceMaterialOverride != null
                ? ActiveSurfaceMaterialOverride
                : (snowBlendActive && cachedSnowField != null && cachedSnowField.settings != null
                    ? cachedSnowField.settings.snowSurfaceMaterial
                    : hostSurfaceMaterial));

        // Plow-drag depth-ratio floor. Below this, snow is too thin to be worth
        // applying drag — avoids the exponential-decay asymptote twitching forever
        // on a slime sitting just inside a barely-tapered patch edge.
        private const float SnowSurfaceTagThreshold = 0.05f;

        // Stack of SurfaceMaterial overrides pushed by trigger zones (e.g. WaterCurrentZone).
        // When non-empty, the top of the stack wins over both snow blend and host SurfaceTag in
        // RefreshSnowBlend. Mirrors snow's surfaceMod swap pattern but driven by zone presence
        // rather than per-FU position sampling. Last-in-wins; LIFO removal handles overlapping
        // zones that exit out of order.
        private readonly List<SurfaceMaterial> surfaceMaterialOverrideStack = new();
        // Cached top-of-stack modifiers, recomputed only on Push/Pop. Avoids per-FU ToModifiers()
        // allocation in RefreshSnowBlend.
        private SurfaceModifiers cachedOverrideMods;
        private bool hasOverrideMods;
        public SurfaceMaterial ActiveSurfaceMaterialOverride => surfaceMaterialOverrideStack.Count > 0
            ? surfaceMaterialOverrideStack[surfaceMaterialOverrideStack.Count - 1]
            : null;

        public void PushSurfaceMaterialOverride(SurfaceMaterial mat)
        {
            if (mat == null) return;
            bool wasInertial = surfaceMod.useInertialSlide;
            surfaceMaterialOverrideStack.Add(mat);
            cachedOverrideMods = mat.ToModifiers();
            hasOverrideMods = true;
            if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
            {
                RefreshSnowBlend();
                MigrateSlideChannelOnInertialFlip(wasInertial);
            }
        }

        public void PopSurfaceMaterialOverride(SurfaceMaterial mat)
        {
            if (mat == null) return;
            bool wasInertial = surfaceMod.useInertialSlide;
            // Remove last occurrence (LIFO). Searching from the end is correct for both
            // single-zone push/pop and the stacked-zone case where the most recently entered
            // zone is the most likely to exit first.
            for (int i = surfaceMaterialOverrideStack.Count - 1; i >= 0; i--)
            {
                if (surfaceMaterialOverrideStack[i] == mat)
                {
                    surfaceMaterialOverrideStack.RemoveAt(i);
                    break;
                }
            }
            int n = surfaceMaterialOverrideStack.Count;
            if (n > 0)
            {
                cachedOverrideMods = surfaceMaterialOverrideStack[n - 1].ToModifiers();
                hasOverrideMods = true;
            }
            else
            {
                hasOverrideMods = false;
            }
            if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
            {
                RefreshSnowBlend();
                MigrateSlideChannelOnInertialFlip(wasInertial);
            }
        }

        // Mirrors UpdateSurfaceModifiers' channel migration: when the active slide model flips
        // (legacy ↔ inertial), move the existing tangent speed into the destination channel so
        // the slime's velocity isn't stranded. Without this, pushing a water override on a
        // slime crawling on a non-inertial wall would leave crawlVelocity populated but
        // surfaceMod.useInertialSlide=true → the inertial path reads slideVelocity=0 and the
        // slime appears to dead-stop on water entry.
        private void MigrateSlideChannelOnInertialFlip(bool wasInertial)
        {
            bool isInertial = surfaceMod.useInertialSlide;
            if (wasInertial == isInertial) return;
            if (wasInertial && !isInertial)
            {
                crawlVelocity += slideVelocity;
                slideVelocity = 0f;
            }
            else
            {
                slideVelocity += crawlVelocity;
                crawlVelocity = 0f;
            }
        }

        // --- Surface Modifiers ---

        private void UpdateSurfaceModifiers(Collider2D col)
        {
            SurfaceTag tag = null;
            if (col != null)
            {
                var tags = col.GetComponentsInParent<SurfaceTag>();
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i].enabled) { tag = tags[i]; break; }
                }
            }
            bool wasInertial = surfaceMod.useInertialSlide;
            hostSurfaceMaterial = (tag != null) ? tag.material : null;
            hostSurfaceMod = (hostSurfaceMaterial != null)
                ? hostSurfaceMaterial.ToModifiers()
                : settings.BuildDefaultModifiers();

            // Snow side: cache the field + pre-resolved snow modifiers so the per-FU
            // blend doesn't re-walk the registry / re-allocate ToModifiers each tick.
            cachedSnowField = null;
            hasCachedSnowMods = false;
            if (col != null && SnowFieldRegistry.TryGet(col, out var sf) && sf != null)
            {
                cachedSnowField = sf;
                var snowSettings = sf.settings;
                if (snowSettings != null && snowSettings.snowSurfaceMaterial != null)
                {
                    cachedSnowMods = snowSettings.snowSurfaceMaterial.ToModifiers();
                    hasCachedSnowMods = true;
                }
            }

            RefreshSnowBlend();

            // Transition between inertial and legacy slide surfaces: keep tangent speed
            // consistent by moving the inertial channel into crawl when leaving inertial,
            // and seeding the inertial channel from crawl when entering inertial.
            if (wasInertial && !surfaceMod.useInertialSlide)
            {
                crawlVelocity += slideVelocity;
                slideVelocity = 0f;
            }
            else if (!wasInertial && surfaceMod.useInertialSlide)
            {
                slideVelocity += crawlVelocity;
                crawlVelocity = 0f;
            }
        }

        /// <summary>
        /// Per-FU surface-tag refresh while attached/charging on a snow host.
        /// Samples whether the slime's contact xy is over a snow patch — if yes,
        /// surfaceMod = snow tag (Snow.asset modifiers), otherwise = host. Pure
        /// binary; no depth-graduated blend, no plow drag.
        /// Discrete fields (useInertialSlide, override flags, curves) stay pinned
        /// to host so the slide-model can't flip mid-attach and strand velocity
        /// in the wrong channel.
        /// </summary>
        private void RefreshSnowBlend()
        {
            cachedFootprintStrip = null;
            cachedFootprintVertIdx = -1;

            // Snow blend (when host has snow). Mesh-local nearest-vertex hit-test against
            // heightRest > 0 is the authoritative check — SnowTopSurfaceExtractor already
            // applied the maxSnowSlopeDeg filter at build time, so any vert with heightRest
            // > 0 is by definition snow-eligible. We do NOT pre-gate by surfaceNormal angle:
            // physics contact normal (collider) and mesh face normal can diverge (simplified
            // colliders, edge/corner averaging) and that mismatch was causing snow-deposited
            // regions to misread as host tag. The strip's xy-AABB pre-filter inside
            // TryFindFootprintHit is the real perf guard. Caches the (strip, vertIdx) so
            // ApplySnowDrag can read heightCurrent[idx] without re-running the sweep.
            if (hasCachedSnowMods && cachedSnowField != null)
            {
                bool onSnow = false;
                if (isTouchingSurface)
                {
                    Vector2 sample = rb.position - surfaceNormal * settings.slimeRadius;
                    onSnow = cachedSnowField.TryFindFootprintHit(
                        sample, out cachedFootprintStrip, out cachedFootprintVertIdx);
                }

                if (onSnow)
                {
                    var snow = cachedSnowMods;
                    snow.useInertialSlide        = hostSurfaceMod.useInertialSlide;
                    snow.overrideCeilingAngle    = hostSurfaceMod.overrideCeilingAngle;
                    snow.enableLandingTangentSpeedRemap = hostSurfaceMod.enableLandingTangentSpeedRemap;
                    snow.landingTangentSpeedRemapCurve = hostSurfaceMod.landingTangentSpeedRemapCurve;
                    surfaceMod = snow;
                    snowBlendActive = true;
                }
                else
                {
                    surfaceMod = hostSurfaceMod;
                    snowBlendActive = false;
                }
            }
            else
            {
                surfaceMod = hostSurfaceMod;
                snowBlendActive = false;
            }

            // Zone override (e.g. WaterCurrentZone) wins over both snow blend and host. Runs
            // unconditionally — must NOT be gated by hasCachedSnowMods, because the most
            // common case is "slime in water on a non-snow wall" and the override has to
            // apply there too. Discrete fields (useInertialSlide etc.) are NOT pinned to host,
            // unlike snow — water explicitly wants inertial slide so jump momentum is preserved
            // instead of replaced by the legacy equilibrium-slide formula.
            // Push/PopSurfaceMaterialOverride handles the channel migration when
            // useInertialSlide changes mid-attach.
            if (hasOverrideMods)
            {
                surfaceMod = cachedOverrideMods;
                // Override implies non-snow gameplay (drag/footprint paths shouldn't apply).
                snowBlendActive = false;
                cachedFootprintStrip = null;
                cachedFootprintVertIdx = -1;
            }
        }

        // Drag-only plow. Depth gate matches SnowSurfaceTagThreshold so this
        // engages exactly when the surface-tag swap engages. Stop speed snaps
        // to 0 below this floor to prevent the exponential decay from
        // asymptote-twitching forever.
        private const float SnowDragStopSpeed = 0.3f;
        // Minimum per-frame decay magnitude (1 - decay) required for the stop-speed snap
        // to engage. 0.05 = drag must remove ≥5%/frame (= K·depth·dt ≥ ~0.05) before snap
        // is meaningful. Below this, snap is gated off so slope-faded K (near-vertical
        // walls) doesn't freeze persistent slideVelocity that gravity is still pumping.
        private const float SnowDragSnapMinDecay = 0.05f;

        /// <summary>
        /// Apply exponential velocity decay while the slime is attached/charging
        /// on a snow patch. No continuous carve, no airborne probe — drag is the
        /// only physical effect snow has on the slime now. Runs after the state
        /// handler so the decay applies to whatever crawl/slide velocity the
        /// state just produced.
        /// </summary>
        private void ApplySnowDrag(float dt)
        {
            if (currentState != SlimeState.Attached && currentState != SlimeState.Charging) return;
            if (!hasCachedSnowMods || cachedSnowField == null) return;
            var snowSettings = cachedSnowField.settings;
            if (snowSettings == null || snowSettings.plowDragK <= 0f) return;

            // Reuse RefreshSnowBlend's nearest-vertex result from earlier this FU
            // instead of running a second SampleDepthRatio01 sweep. Null cache means
            // RefreshSnowBlend already determined the contact isn't on snow — drag
            // can't engage either.
            var s = cachedFootprintStrip;
            int idx = cachedFootprintVertIdx;
            if (s == null || idx < 0) return;
            if (s.heightRest == null || s.heightCurrent == null) return;
            if (idx >= s.heightRest.Length || idx >= s.heightCurrent.Length) return;
            float rest = s.heightRest[idx];
            if (rest <= 1e-5f) return;
            float depth = Mathf.Clamp01(s.heightCurrent[idx] / rest);
            if (depth <= SnowSurfaceTagThreshold) return;

            float k = snowSettings.plowDragK;
            if (snowSettings.slopeToDragScale != null)
            {
                float angleDeg = SurfaceProbe.GetSurfaceAngle(surfaceNormal, gravityUp);
                k *= snowSettings.slopeToDragScale.Evaluate(angleDeg);
            }
            if (k <= 0f) return;

            float decay = Mathf.Exp(-k * depth * dt);
            if (decay < 0f) decay = 0f;

            slideVelocity *= decay;
            crawlVelocity *= decay;
            velocity *= decay;
            // Stop-speed snap only when this frame's drag was strong enough to be in the
            // exponential-decay asymptote. With slope-faded K (e.g. near-vertical walls)
            // decay can be ~1.0 per frame — the slime isn't being meaningfully damped, and
            // gravity is still building velocity up; snapping here would freeze the slime
            // permanently (especially in inertial slide where slideVelocity is persistent).
            if (1f - decay >= SnowDragSnapMinDecay)
            {
                if (Mathf.Abs(slideVelocity) < SnowDragStopSpeed) slideVelocity = 0f;
                if (Mathf.Abs(crawlVelocity) < SnowDragStopSpeed) crawlVelocity = 0f;
                if (velocity.sqrMagnitude < SnowDragStopSpeed * SnowDragStopSpeed) velocity = Vector2.zero;
            }
        }
    }
}
