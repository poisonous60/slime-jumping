using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class DragZone : MonoBehaviour
    {
        [Header("Enter/Exit")]
        [Tooltip("Velocity multiplier on zone entry.")]
        [Range(0.01f, 1f)] public float entryMultiplier = 0.3f;

        [Tooltip("Velocity multiplier on zone exit.")]
        [Min(0.01f)] public float exitMultiplier = 3.33f;

        [Header("Launch")]
        [Tooltip("Launch velocity multiplier when charging inside this zone. Exit multiplier applies on leaving.")]
        [Range(0.01f, 2f)] public float launchMultiplier = 0.3f;

        [Header("Internal Drag")]
        [Tooltip("Per-second velocity retention while inside (0.3 = 30% speed after 1s). 1 = no drag.")]
        [Range(0.01f, 1f)] public float dragFactor = 0.3f;

        [Tooltip("Maximum fall speed inside this zone (units/s). 0 = no cap.")]
        [Min(0f)] public float maxFallSpeed = 5f;

        [Header("Charging")]
        [Tooltip("If true, slime can enter Charging state while Airborne inside this zone.")]
        public bool allowCharging = false;

        SlimeController trackedSlime;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            trackedSlime = slime;
            slime.OnDragZoneEnter(this);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;
            trackedSlime = null;
            slime.OnDragZoneExit(this);
        }

        public void ForceExit()
        {
            trackedSlime = null;
        }

        void FixedUpdate()
        {
            if (trackedSlime == null) return;
            float dt = Time.fixedDeltaTime;
            if (dragFactor < 1f)
                trackedSlime.ApplyDragFactor(Mathf.Pow(dragFactor, dt));
            if (maxFallSpeed > 0f)
                trackedSlime.ClampFallSpeed(maxFallSpeed);
        }
    }
}
