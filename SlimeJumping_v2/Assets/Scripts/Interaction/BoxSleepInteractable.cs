using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using SlimeJumping.Controller;
using SlimeJumping.InputHandling;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    public class BoxSleepInteractable : InteractableBase, IExternalController
    {
        [Header("Box Sleep")]
        [SerializeField] private Transform sitAnchor;
        [SerializeField] private BoxSleepProfile profile;
        [SerializeField] private bool freezePhysics = true;
        [SerializeField] private SleepZEmitter zEmitter;

        private SlimeController boundSlime;
        private SlimeInputReader boundReader;
        private SlimeVisual boundVisual;
        private BoxSleepAnimator boundAnimator;

        protected override void Awake()
        {
            base.Awake();
            if (zEmitter == null) zEmitter = GetComponentInChildren<SleepZEmitter>();
        }

        protected override void OnInteract(SlimeController slime)
        {
            if (sitAnchor == null)
            {
                Debug.LogWarning($"[BoxSleepInteractable] {name} missing sitAnchor.", this);
                return;
            }
            if (profile == null)
            {
                Debug.LogError($"[BoxSleepInteractable] {name} missing BoxSleepProfile — assign one in the inspector.", this);
                return;
            }
            slime.SetOverride(this);
        }

        public void OnTakeControl(SlimeController slime)
        {
            boundSlime = slime;
            boundReader = slime.GetComponent<SlimeInputReader>();
            boundVisual = slime.GetComponentInChildren<SlimeVisual>();
            boundAnimator = slime.GetComponent<BoxSleepAnimator>();
            if (boundAnimator == null) boundAnimator = slime.GetComponentInChildren<BoxSleepAnimator>();

            slime.OverrideVelocity(Vector2.zero);
            // rb.position is Vector2 so Z stays put; ApplySitDepthShift handles visual depth.
            var rb = slime.GetComponent<Rigidbody2D>();
            if (rb != null) rb.position = sitAnchor.position;

            if (boundAnimator != null && boundVisual != null)
            {
                // Open the gate BEFORE ApplySitDepthShift — the gate resets body3d.localPosition
                // to rest, so a Z written earlier is lost.
                boundVisual.IsExternallyAnimated = true;
                boundVisual.SetExternalSurfaceNormal(Vector2.up);
                boundVisual.ApplySitDepthShift(sitAnchor.position.z);

                if (zEmitter != null) zEmitter.Configure(profile);
                boundAnimator.OnFullyReleased += HandleFullyReleased;
                boundAnimator.BeginBoxSleep(profile);
            }
            else if (boundAnimator == null)
            {
                Debug.LogError($"[BoxSleepInteractable] Slime has no BoxSleepAnimator component; attach it to the slime prefab.", this);
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
            if (zEmitter != null) zEmitter.IsEmitting = false;

            boundSlime = null;
            boundReader = null;
            boundVisual = null;
            boundAnimator = null;
        }

        public void ExternalUpdate(SlimeController slime)
        {
            if (zEmitter != null && boundAnimator != null)
                zEmitter.IsEmitting = boundAnimator.IsSleeping;

            // Sleeping only, else the E press that started the sit-down wakes it during Settling.
            if (boundAnimator != null && boundAnimator.IsSleeping && AnyButtonPressedThisFrame())
                boundAnimator.RequestWake();
        }

        internal static bool AnyButtonPressedThisFrame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.anyKey.wasPressedThisFrame) return true;

            var mouse = Mouse.current;
            if (mouse != null && (
                mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame ||
                mouse.middleButton.wasPressedThisFrame)) return true;

            var pad = Gamepad.current;
            if (pad != null)
            {
                foreach (var ctrl in pad.allControls)
                {
                    if (ctrl is ButtonControl btn && btn.wasPressedThisFrame)
                        return true;
                }
            }
            return false;
        }

        public void ExternalFixedUpdate(SlimeController slime, float dt)
        {
            if (freezePhysics) slime.OverrideVelocity(Vector2.zero);
        }

        private void HandleInteractPressed()
        {
            if (boundAnimator != null) boundAnimator.RequestWake();
        }

        private void HandleFullyReleased()
        {
            if (boundSlime != null) boundSlime.ReleaseOverride(this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (sitAnchor == null) return;
            Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.9f);
            Gizmos.DrawSphere(sitAnchor.position, 0.08f);
            Gizmos.color = new Color(0.5f, 0.7f, 1f, 0.3f);
            Gizmos.DrawWireSphere(sitAnchor.position, 0.15f);
        }
#endif
    }
}
