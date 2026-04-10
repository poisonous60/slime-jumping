using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class WeightDescendPlatform : SignalSource
    {
        [SerializeField] private float descendSpeed = 1.5f;
        [SerializeField] private float ascendSpeed = 2.5f;
        [SerializeField] private float maxDrop = 2f;
        [SerializeField] private bool returnsUp = true;
        [SerializeField] private PlatformRiderSensor sensor;

        private Rigidbody2D rb;
        private Vector2 topPos;
        private bool firedAtBottom;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            topPos = rb.position;
        }

        void FixedUpdate()
        {
            float drop = topPos.y - rb.position.y;
            bool hasRider = sensor != null && sensor.RiderCount > 0;

            Vector2 target = rb.position;
            if (hasRider && drop < maxDrop)
                target.y -= descendSpeed * Time.fixedDeltaTime;
            else if (!hasRider && returnsUp && drop > 0f)
                target.y += ascendSpeed * Time.fixedDeltaTime;

            target.y = Mathf.Clamp(target.y, topPos.y - maxDrop, topPos.y);
            rb.MovePosition(target);

            // Fire signal at bottom
            if (!firedAtBottom && topPos.y - target.y >= maxDrop - 0.01f)
            {
                Activate();
                firedAtBottom = true;
            }

            // Re-arm when returned to top
            if (firedAtBottom && drop <= 0.01f)
            {
                firedAtBottom = false;
                Deactivate();
            }
        }
    }
}
