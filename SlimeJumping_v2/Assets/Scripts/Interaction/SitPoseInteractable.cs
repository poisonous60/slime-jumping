using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.InputHandling;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    public class SitPoseInteractable : InteractableBase, IExternalController
    {
        [Header("Sit Pose")]
        [SerializeField] private Transform sitAnchor;
        [SerializeField] private SitPoseProfile profile;
        [SerializeField] private bool freezePhysics = true;
        [Tooltip("Placeholder; current implementation relies on prefab layout to avoid adjacent interactables. Toggle has no effect yet.")]
        [SerializeField] private bool consumeWInput = true;

        private SlimeController boundSlime;
        private SlimeInputReader boundReader;
        private SlimeVisual boundVisual;
        private SitPoseAnimator boundAnimator;

        protected override void OnInteract(SlimeController slime)
        {
            if (sitAnchor == null)
            {
                Debug.LogWarning($"[SitPoseInteractable] {name} missing sitAnchor.", this);
                return;
            }
            if (profile == null)
            {
                Debug.LogError($"[SitPoseInteractable] {name} missing SitPoseProfile — assign one in the inspector.", this);
                return;
            }
            slime.SetOverride(this);
        }

        public void OnTakeControl(SlimeController slime)
        {
            boundSlime = slime;
            boundReader = slime.GetComponent<SlimeInputReader>();
            boundVisual = slime.GetComponentInChildren<SlimeVisual>();
            boundAnimator = slime.GetComponent<SitPoseAnimator>();
            if (boundAnimator == null) boundAnimator = slime.GetComponentInChildren<SitPoseAnimator>();

            slime.OverrideVelocity(Vector2.zero);
            // CLAUDE.md: use rb.position (not transform.position). Rigidbody2D has FreezeRotation
            // constraint on slime, so rotation is a no-op at physics level — skip it.
            var rb = slime.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = sitAnchor.position;

            if (boundAnimator != null)
            {
                // Gate must open BEFORE BeginSitPose so its immediate t=0 PushDeform lands.
                // Order matters: set gate first (triggers ResetDeformationState which writes
                // body3d.localPosition = rest), THEN ApplySitDepthShift so the Z write survives
                // into the first SetExternalDeform call (which preserves body3d.localPosition.z).
                if (boundVisual != null)
                {
                    boundVisual.IsExternallyAnimated = true;
                    // Force world-up surface normal so the 3D shader dip faces into the seat.
                    boundVisual.SetExternalSurfaceNormal(Vector2.up);
                    // Depth shift covers both SlimeVisual (face sprites) and body3d (sibling
                    // mesh) so the 3D mesh and face stay Z-aligned during the sit.
                    boundVisual.ApplySitDepthShift(sitAnchor.position.z);
                }
                boundAnimator.OnFullyReleased += HandleFullyReleased;
                boundAnimator.BeginSitPose(profile);
            }
            else
            {
                Debug.LogError($"[SitPoseInteractable] Slime has no SitPoseAnimator component; attach it to the slime prefab.", this);
            }

            if (boundReader != null) boundReader.OnInteractPressed += HandleInteractPressed;
        }

        public void OnReleaseControl(SlimeController slime)
        {
            if (boundReader != null) boundReader.OnInteractPressed -= HandleInteractPressed;
            if (boundAnimator != null) boundAnimator.OnFullyReleased -= HandleFullyReleased;
            if (boundVisual != null)
            {
                boundVisual.IsExternallyAnimated = false;
                boundVisual.RestoreSitDepthShift();
            }

            boundSlime = null;
            boundReader = null;
            boundVisual = null;
            boundAnimator = null;
        }

        public void ExternalUpdate(SlimeController slime)
        {
            // Release is driven by the animator's OnFullyReleased event — no polling here.
        }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            if (freezePhysics) slime.OverrideVelocity(Vector2.zero);
        }

        private void HandleInteractPressed()
        {
            if (boundAnimator != null) boundAnimator.RequestGetUp();
        }

        private void HandleFullyReleased()
        {
            if (boundSlime != null) boundSlime.ReleaseOverride(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (sitAnchor == null) return;
            Gizmos.color = new Color(1f, 1f, 0f, 0.9f);
            Gizmos.DrawSphere(sitAnchor.position, 0.08f);
            Gizmos.color = new Color(1f, 1f, 0f, 0.3f);
            Gizmos.DrawWireSphere(sitAnchor.position, 0.15f);
        }
#endif
    }
}
