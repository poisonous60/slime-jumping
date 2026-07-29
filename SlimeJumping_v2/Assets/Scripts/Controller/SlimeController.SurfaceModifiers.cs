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

        // Snow coupling (read-only pull from SnowFieldRegistry): host tag captured at attach,
        // snow side pre-resolved; RefreshSnowBlend swaps surfaceMod per FU. Binary, no blend.
        private SurfaceModifiers hostSurfaceMod = SurfaceModifiers.Default;
        private SurfaceMaterial hostSurfaceMaterial;
        private bool snowBlendActive;
        private SnowField cachedSnowField;
        private SurfaceModifiers cachedSnowMods;
        private bool hasCachedSnowMods;
        // Footprint hit written by RefreshSnowBlend and read by ApplySnowDrag in the same FU;
        // null when not on snow. Lifetime: one FU only.
        private SnowStrip cachedFootprintStrip;
        private int cachedFootprintVertIdx;

        // Precedence: zone override > snow > host SurfaceTag. Null while airborne.
        public SurfaceMaterial ActiveSurfaceMaterial => !isTouchingSurface
            ? null
            : (ActiveSurfaceMaterialOverride != null
                ? ActiveSurfaceMaterialOverride
                : (snowBlendActive && cachedSnowField != null && cachedSnowField.settings != null
                    ? cachedSnowField.settings.snowSurfaceMaterial
                    : hostSurfaceMaterial));

        // Plow-drag depth floor — below it the exponential decay twitches on its asymptote
        // forever for a slime sitting just inside a barely-tapered patch edge.
        private const float SnowSurfaceTagThreshold = 0.05f;

        // SurfaceMaterial overrides pushed by trigger zones; top of stack wins over snow and
        // host. LIFO removal so overlapping zones exiting out of order stay consistent.
        private readonly List<SurfaceMaterial> surfaceMaterialOverrideStack = new();
        // Recomputed only on Push/Pop — avoids a per-FU ToModifiers() allocation.
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
            // Remove last occurrence (LIFO).
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

        // Move tangent speed into the destination channel when the slide model flips
        // (legacy ↔ inertial); without it the slime dead-stops on water entry.
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

            // Cached so the per-FU blend skips the registry walk and the ToModifiers alloc.
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

            // Keep tangent speed across the inertial ↔ legacy flip, else velocity is stranded.
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

        /// <summary>Per-FU host/snow surfaceMod swap; discrete fields stay pinned to host so the slide model can't flip mid-attach and strand velocity in the wrong channel.</summary>
        private void RefreshSnowBlend()
        {
            cachedFootprintStrip = null;
            cachedFootprintVertIdx = -1;

            // Nearest-vertex hit-test on heightRest > 0 is authoritative. Do NOT pre-gate by
            // surfaceNormal angle: contact normal and mesh face normal diverge on simplified
            // colliders, which makes snow-deposited regions misread as host tag.
            if (hasCachedSnowMods && cachedSnowField != null)
            {
                bool onSnow = false;
                if (isTouchingSurface)
                {
                    Vector2 sample = rb.position - surfaceNormal * SlimeRadius;
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

            // Zone override wins over snow and host. Must NOT be gated by hasCachedSnowMods —
            // the common case is water on a non-snow wall. Discrete fields are not pinned here
            // (water wants inertial slide); Push/Pop migrates the channel on that flip.
            if (hasOverrideMods)
            {
                surfaceMod = cachedOverrideMods;
                // Override implies non-snow gameplay (drag/footprint paths shouldn't apply).
                snowBlendActive = false;
                cachedFootprintStrip = null;
                cachedFootprintVertIdx = -1;
            }
        }

        // Stop-speed floor: snap to 0 so the exponential decay doesn't twitch forever.
        private const float SnowDragStopSpeed = 0.3f;
        // Minimum per-frame decay (1 - decay) for the snap to engage. Below it, slope-faded K
        // on near-vertical walls would freeze slideVelocity that gravity is still pumping.
        private const float SnowDragSnapMinDecay = 0.05f;

        /// <summary>Exponential velocity decay while attached/charging on snow; runs after the state handler so it damps whatever velocity that produced.</summary>
        private void ApplySnowDrag(float dt)
        {
            if (currentState != SlimeState.Attached && currentState != SlimeState.Charging) return;
            if (!hasCachedSnowMods || cachedSnowField == null) return;
            var snowSettings = cachedSnowField.settings;
            if (snowSettings == null || snowSettings.plowDragK <= 0f) return;

            // Reuses RefreshSnowBlend's hit from this FU; a null cache means "not on snow".
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
            // Snap only when this frame's drag was strong enough; with decay ~1.0 (slope-faded
            // K on near-vertical walls) snapping would freeze the slime permanently.
            if (1f - decay >= SnowDragSnapMinDecay)
            {
                if (Mathf.Abs(slideVelocity) < SnowDragStopSpeed) slideVelocity = 0f;
                if (Mathf.Abs(crawlVelocity) < SnowDragStopSpeed) crawlVelocity = 0f;
                if (velocity.sqrMagnitude < SnowDragStopSpeed * SnowDragStopSpeed) velocity = Vector2.zero;
            }
        }
    }
}
