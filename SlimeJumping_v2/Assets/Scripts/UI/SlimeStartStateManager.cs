using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.UI
{
    /// Puts the slime into <see cref="startInteraction"/> (BoxSleep, SitPose…) at scene load:
    /// teleported to its sitAnchor and driven by its IExternalController.
    public class SlimeStartStateManager : MonoBehaviour
    {
        public enum TriggerMode
        {
            /// Fire only when GameFlow.StartGame() brought us here (real menu→play).
            OnlyFromMenu,
            /// Fire every scene load — useful for designer iteration in the editor.
            Always,
            /// Never fire — keep the reference wired but stay inert.
            Disabled,
        }

        [SerializeField] private SlimeController slime;
        [Tooltip("Interactable (BoxSleep, SitPose, etc.) the slime begins the scene inside. Leave null to disable.")]
        [SerializeField] private InteractableBase startInteraction;
        [SerializeField] private TriggerMode triggerMode = TriggerMode.OnlyFromMenu;

        private IEnumerator Start()
        {
            if (!ShouldTrigger()) yield break;
            if (startInteraction == null)
            {
                Debug.LogWarning("[SlimeStartStateManager] No startInteraction assigned — nothing to do.", this);
                yield break;
            }

            // One-frame grace: every Awake/OnEnable must settle before we push an override.
            yield return null;

            if (slime == null) slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[SlimeStartStateManager] No SlimeController found in scene.", this);
                yield break;
            }
            if (slime.HasOverride) yield break;

            // Interact() bypasses CanInteract; at scene start the slime isn't inside the trigger yet.
            startInteraction.Interact(slime);
        }

        private bool ShouldTrigger()
        {
            switch (triggerMode)
            {
                case TriggerMode.Always: return true;
                case TriggerMode.OnlyFromMenu: return GameFlow.WasStartedFromMenu;
                default: return false;
            }
        }
    }
}
