using System;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Effects
{
    /// Installs the two standard hitstop vetoes: slime charging (hitstop would warp an in-progress aim)
    /// and options panel open (panel policy: never touch Time.timeScale). Unassigned field = that guard skipped.
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
