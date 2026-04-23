using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interaction;

namespace SlimeJumping.UI
{
    /// Scene-placed manager that puts the slime into a chosen Interaction
    /// (e.g. BoxSleep) when the gameplay scene loads.
    ///
    /// Place one in MainScene, drag the target InteractableBase (BoxSleep, SitPose,
    /// etc.) into <see cref="startInteraction"/>, and the slime will be teleported
    /// to its sitAnchor and driven by its IExternalController at scene start.
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

            // One-frame grace so every Awake/OnEnable in the scene has settled
            // before we push an override onto SlimeController.
            yield return null;

            if (slime == null) slime = Object.FindFirstObjectByType<SlimeController>();
            if (slime == null)
            {
                Debug.LogWarning("[SlimeStartStateManager] No SlimeController found in scene.", this);
                yield break;
            }
            if (slime.HasOverride) yield break;

            // Interact() bypasses CanInteract — good, because the slime isn't
            // necessarily touching the interactable's trigger at scene start.
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
