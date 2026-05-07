using UnityEngine;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- External controller override ---
        private IExternalController _override;
        public bool HasOverride => _override != null;

        // --- External Controller Override ---

        /// <summary>
        /// Direct velocity write. Only callable while HasOverride==true.
        /// Use AddExternalVelocity / MultiplyVelocity for normal velocity changes.
        /// NOTE: OverridePosition is not yet needed — add when #5/#27/#31 require
        /// path-based slime movement during override.
        /// </summary>
        public void OverrideVelocity(Vector2 v)
        {
            if (!HasOverride)
            {
                Debug.LogError("[SlimeController] OverrideVelocity called outside of override context");
                return;
            }
            velocity = v;
        }

        /// <summary>
        /// Direct position write. Only callable while HasOverride==true. Used by
        /// path-based traversal controllers (#31 pipe transit) that lerp the slime
        /// along an external path while suspending normal physics.
        /// </summary>
        public void OverridePosition(Vector2 p)
        {
            if (!HasOverride)
            {
                Debug.LogError("[SlimeController] OverridePosition called outside of override context");
                return;
            }
            rb.position = p;
        }

        public void SetOverride(IExternalController controller)
        {
            if (_override != null)
            {
                Debug.LogWarning("[SlimeController] Override already set. Releasing previous before setting new.");
                _override.OnReleaseControl(this);
            }

            // Clean up current state
            if (currentState == SlimeState.Charging)
            {
                isVolumeCharging = false;
            }
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();
            blockAttachCount = 0;
            inFluidCount = 0;
            surfaceMaterialOverrideStack.Clear();
            hasOverrideMods = false;
            stunTimer = 0f;
            // Pending energy-conversion coyote would otherwise survive the override
            // (timer only ticks in FixedUpdateAttached) and fire a spurious energy
            // conversion on the next Attached landing after release.
            ClearPendingEnergyConversion(executeDeferredReset: false);

            // Unity doesn't fire OnTriggerExit2D after override — clear stale candidates
            interactionDetector.ClearCandidates();

            _override = controller;
            _override.OnTakeControl(this);
        }

        public void ReleaseOverride(IExternalController controller)
        {
            if (_override != controller) return;

            _override.OnReleaseControl(this);
            _override = null;

            // SetOverride cleared detector candidates; if we're still overlapping
            // the same interactable (e.g. standing up from a bench), Unity won't
            // re-fire OnTriggerEnter2D. Rescan current overlaps so W works again.
            interactionDetector.Rescan();

            // Clear stale gravity fields (slime may have moved during override).
            // NOTE: If slime is still physically inside a GravityField after release,
            // that zone won't re-fire OnTriggerEnter2D. The IExternalController
            // implementation is responsible for moving the slime outside gravity zones
            // before releasing, or the zone will be silently orphaned until re-entry.
            gravityFieldStack.Clear();
            gravityUp = Vector2.up;
            gravityRight = Vector2.right;

            // Resume as Airborne
            launchSpeed = 0f;
            launchPosition = rb.position;
            launchedByHop = false;
            conversionCount = 0;
            stunTimer = 0f;
            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
