using UnityEngine;
using SlimeJumping.Mechanisms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Current drag zone (set by DragZone.OnTriggerEnter/Exit, single zone only)
        public DragZone CurrentDragZone { get; private set; }

        // --- Drag Zone API (called by DragZone.cs) ---

        public void OnDragZoneEnter(DragZone zone)
        {
            velocity *= zone.entryMultiplier;
            CurrentDragZone = zone;
        }

        public void OnDragZoneExit(DragZone zone)
        {
            if (CurrentDragZone != zone) return;
            float savedFall = Vector2.Dot(velocity, gravityUp); // negative when falling
            velocity *= zone.exitMultiplier;
            if (savedFall < 0f)
            {
                float hSpeed = Vector2.Dot(velocity, gravityRight);
                velocity = gravityUp * savedFall + gravityRight * hSpeed;
            }
            CurrentDragZone = null;
        }

        public void ApplyDragFactor(float factor) { velocity *= factor; }
        public void ClampFallSpeed(float max)
        {
            float fallSpeed = -Vector2.Dot(velocity, gravityUp); // positive when falling
            if (fallSpeed > max)
            {
                float hSpeed = Vector2.Dot(velocity, gravityRight);
                velocity = gravityUp * (-max) + gravityRight * hSpeed;
            }
        }
    }
}
