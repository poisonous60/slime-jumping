using System;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Effects
{
    /// Scene-level installer for the two vetoes every gameplay scene needs: hitstop
    /// must not fire while the slime is charging the slingshot (would warp physics of
    /// an in-progress aim) and must not fire while the options panel is open (panel
    /// policy is explicitly "do not touch Time.timeScale"). Leave a field unassigned
    /// to skip that guard. Custom vetoes can still call HitStopService.AddGuard
    /// directly without using this component.
    [DisallowMultipleComponent]
    public class HitStopGuards : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [Tooltip("Root GameObject of the options panel — active = menu open = hitstop blocked.")]
        [SerializeField] private GameObject optionsPanel;

        private Func<bool> chargingGuard;
        private Func<bool> optionsGuard;

        private void OnEnable()
        {
            if (slime != null)
            {
                chargingGuard = () => slime != null && slime.IsCharging;
                HitStopService.AddGuard(chargingGuard);
            }
            if (optionsPanel != null)
            {
                optionsGuard = () => optionsPanel != null && optionsPanel.activeSelf;
                HitStopService.AddGuard(optionsGuard);
            }
        }

        private void OnDisable()
        {
            if (chargingGuard != null) { HitStopService.RemoveGuard(chargingGuard); chargingGuard = null; }
            if (optionsGuard != null) { HitStopService.RemoveGuard(optionsGuard); optionsGuard = null; }
        }
    }
}
