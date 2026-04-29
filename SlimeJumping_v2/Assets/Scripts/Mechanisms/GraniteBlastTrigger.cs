using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Trigger volume that fires its bound GraniteBlastEmitter once when the slime
    /// enters. Designer wires the emitter (set its autoFire=false so it stays
    /// dormant), drops a trigger Collider2D in the scene, and the laser fires when
    /// the slime walks into the volume. Optional retrigger cooldown supports
    /// "fires every N seconds while the slime is inside" gameplay; default is
    /// strict one-shot.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class GraniteBlastTrigger : MonoBehaviour
    {
        [Tooltip("Emitter to fire. Set its autoFire=false so it only fires from this trigger.")]
        [SerializeField] private GraniteBlastEmitter emitter;
        [Tooltip("If false (default), the trigger is one-shot — fires the very first time the slime enters and never again.")]
        [SerializeField] private bool allowRetrigger = false;
        [Tooltip("Only used when allowRetrigger=true. Seconds the trigger must wait after firing before it can fire again, regardless of emitter state.")]
        [SerializeField] private float retriggerCooldown = 0f;

        private bool hasFired;
        private float retriggerTimer;

        void Reset()
        {
            // Ensure the attached collider is a trigger by default.
            var col = GetComponent<Collider2D>();
            if (col != null) col.isTrigger = true;
        }

        void Update()
        {
            if (retriggerTimer > 0f) retriggerTimer -= Time.deltaTime;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (emitter == null) return;
            if (other.GetComponentInParent<SlimeController>() == null) return;
            if (!allowRetrigger && hasFired) return;
            if (retriggerTimer > 0f) return;

            if (!emitter.FireOnce()) return;
            hasFired = true;
            if (allowRetrigger && retriggerCooldown > 0f) retriggerTimer = retriggerCooldown;
        }
    }
}
