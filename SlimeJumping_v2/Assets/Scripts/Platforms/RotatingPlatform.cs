using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class RotatingPlatform : PlatformMotor
    {
        [SerializeField] float degreesPerSecond = 45f;
        [SerializeField] bool oscillate = false;
        [SerializeField] float oscillateRange = 90f;

        float baseRotation;
        float elapsed;

        protected override void Awake()
        {
            base.Awake();
            baseRotation = rb.rotation;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            elapsed += dt;
            float rot;

            if (oscillate)
            {
                rot = baseRotation + Mathf.Sin(elapsed * degreesPerSecond
                      * Mathf.Deg2Rad) * oscillateRange;
            }
            else
            {
                rot = baseRotation + elapsed * degreesPerSecond;
            }

            return (rb.position, rot);
        }
    }
}
