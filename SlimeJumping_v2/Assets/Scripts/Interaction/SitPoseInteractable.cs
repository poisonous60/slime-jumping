using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Input;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    public class SitPoseInteractable : InteractableBase, IExternalController
    {
        [Header("Sit Pose")]
        [SerializeField] private Transform sitAnchor;
        [SerializeField] private bool freezePhysics = true;
        [Tooltip("Placeholder; current implementation relies on prefab layout to avoid adjacent interactables. Toggle has no effect yet.")]
        [SerializeField] private bool consumeWInput = true;

        private SlimeController boundSlime;
        private SlimeInputReader boundReader;
        private SlimeVisual boundVisual;
        private bool releaseRequested;

        protected override void OnInteract(SlimeController slime)
        {
            if (sitAnchor == null)
            {
                Debug.LogWarning($"[SitPoseInteractable] {name} missing sitAnchor.", this);
                return;
            }
            slime.SetOverride(this);
        }

        public void OnTakeControl(SlimeController slime)
        {
            boundSlime = slime;
            boundReader = slime.GetComponent<SlimeInputReader>();
            boundVisual = slime.GetComponentInChildren<SlimeVisual>();

            if (boundVisual != null) boundVisual.IsExternallyAnimated = true;

            slime.OverrideVelocity(Vector2.zero);
            // CLAUDE.md: use rb.position (not transform.position). Rigidbody2D has FreezeRotation
            // constraint on slime, so rotation is a no-op at physics level — skip it.
            var rb = slime.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = sitAnchor.position;

            releaseRequested = false;
            if (boundReader != null) boundReader.OnVerticalUpPressed += HandleVerticalUpPressed;
        }

        public void OnReleaseControl(SlimeController slime)
        {
            if (boundReader != null) boundReader.OnVerticalUpPressed -= HandleVerticalUpPressed;
            if (boundVisual != null) boundVisual.IsExternallyAnimated = false;

            boundSlime = null;
            boundReader = null;
            boundVisual = null;
            releaseRequested = false;
        }

        public void ExternalUpdate(SlimeController slime)
        {
            if (releaseRequested)
            {
                releaseRequested = false;
                slime.ReleaseOverride(this);
            }
        }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            if (freezePhysics) slime.OverrideVelocity(Vector2.zero);
        }

        private void HandleVerticalUpPressed()
        {
            releaseRequested = true;
        }
    }
}
