using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class WaypointPlatform : PlatformMotor
    {
        [SerializeField] Vector2[] localWaypoints;
        [SerializeField] float speed = 2f;
        [SerializeField] float waitTime = 0.5f;
        [SerializeField] bool loop = true;      // false = ping-pong
        [SerializeField] AnimationCurve easeCurve = AnimationCurve.Linear(0, 0, 1, 1);

        Vector2[] worldWaypoints;
        int currentIndex;
        int direction = 1;
        float journeyProgress;
        float waitTimer;

        protected override void Awake()
        {
            base.Awake();
            worldWaypoints = new Vector2[localWaypoints.Length];
            for (int i = 0; i < localWaypoints.Length; i++)
                worldWaypoints[i] = rb.position + localWaypoints[i];
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (worldWaypoints == null || worldWaypoints.Length < 2)
                return (rb.position, rb.rotation);

            if (waitTimer > 0)
            {
                waitTimer -= dt;
                return (rb.position, rb.rotation);
            }

            int nextIndex = currentIndex + direction;
            Vector2 from = worldWaypoints[currentIndex];
            Vector2 to = worldWaypoints[nextIndex];
            float dist = Vector2.Distance(from, to);

            if (dist > 0.001f)
                journeyProgress += (speed * dt) / dist;

            if (journeyProgress >= 1f)
            {
                journeyProgress = 0f;
                currentIndex = nextIndex;
                waitTimer = waitTime;

                int peek = currentIndex + direction;
                if (peek < 0 || peek >= worldWaypoints.Length)
                {
                    if (loop) currentIndex = 0;
                    else direction *= -1;
                }
            }

            float easedT = easeCurve.Evaluate(journeyProgress);
            Vector2 pos = Vector2.Lerp(from, to, easedT);
            return (pos, rb.rotation);
        }

        void OnDrawGizmos()
        {
            if (localWaypoints == null || localWaypoints.Length == 0) return;
            Gizmos.color = Color.cyan;

            for (int i = 0; i < localWaypoints.Length; i++)
            {
                Vector2 wp = Application.isPlaying
                    ? worldWaypoints[i]
                    : rb.position + localWaypoints[i];
                Gizmos.DrawSphere(wp, 0.15f);
                if (i > 0)
                {
                    Vector2 prev = Application.isPlaying
                        ? worldWaypoints[i - 1]
                        : rb.position + localWaypoints[i - 1];
                    Gizmos.DrawLine(prev, wp);
                }
            }
        }
    }
}
