using UnityEngine;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        private IExternalController _override;
        public bool HasOverride => _override != null;


        /// <summary>Direct velocity write; override context only. Normal changes go through AddExternalVelocity/MultiplyVelocity.</summary>
        public void OverrideVelocity(Vector2 v)
        {
            if (!HasOverride)
            {
                Debug.LogError("[SlimeController] OverrideVelocity called outside of override context");
                return;
            }
            velocity = v;
        }

        /// <summary>Direct position write; override context only. For path-based traversal that suspends normal physics.</summary>
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

            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();
            blockAttachCount = 0;
            inFluidCount = 0;
            surfaceMaterialOverrideStack.Clear();
            hasOverrideMods = false;
            stunTimer = 0f;
            // Coyote timer only ticks in FixedUpdateAttached, so it survives the override
            // and fires a spurious energy conversion on the next landing.
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

            // Still overlapping the same interactable? Unity won't re-fire
            // OnTriggerEnter2D, so without a rescan W stops working.
            interactionDetector.Rescan();

            // Same trap: a slime left inside a GravityField gets no fresh OnTriggerEnter2D,
            // so the IExternalController must move it out before releasing.
            gravityFieldStack.Clear();
            gravityUp = Vector2.up;
            gravityRight = Vector2.right;

            // 저중력존은 존 쪽 추적까지 리셋하고 현재 위치 기준으로 다시 잡는다.
            ResyncLowGravityZones();

            launchSpeed = 0f;
            launchPosition = rb.position;
            conversionCount = 0;
            stunTimer = 0f;
            ceilingBumpLatched = false;
            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
