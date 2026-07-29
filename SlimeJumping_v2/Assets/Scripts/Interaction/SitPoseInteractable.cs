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
            // FreezeRotation on the slime's Rigidbody2D makes a rotation write a no-op — position only.
            var rb = slime.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = sitAnchor.position;

            if (boundAnimator != null)
            {
                // Order: gate ON before BeginSitPose (its t=0 PushDeform must land), and
                // ApplySitDepthShift after the gate — the gate's reset would clobber the Z write.
                if (boundVisual != null)
                {
                    boundVisual.IsExternallyAnimated = true;
                    // Force world-up surface normal so the 3D shader dip faces into the seat.
                    boundVisual.SetExternalSurfaceNormal(Vector2.up);
                    // Depth shift covers face sprites and body3d both, so they stay Z-aligned in the sit.
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
