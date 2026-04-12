using UnityEngine;

namespace SlimeJumping.Platforms
{
    [DisallowMultipleComponent]
    public class WaypointPlatform : PlatformMotor
    {
        [SerializeField] Vector2[] localWaypoints;
        [SerializeField] float speed = 2f;
        [SerializeField] float waitTime = 0.5f;
        [SerializeField] bool loop = true;      // false = ping-pong
        [SerializeField] AnimationCurve easeCurve = AnimationCurve.Linear(0, 0, 1, 1);

        public System.Func<bool> CanAdvance;
        public event System.Action OnSegmentArrived;
        public Vector2 LastDirection { get; private set; }

        Vector2[] worldWaypoints;
        int currentIndex;
        int direction = 1;
        float journeyProgress;
        float waitTimer;

        protected override void Awake()
        {
            base.Awake();

            if (localWaypoints == null || localWaypoints.Length < 2)
            {
                Debug.LogError($"[WaypointPlatform] localWaypoints needs at least 2 entries on {name}. Disabling.", this);
                enabled = false;
                return;
            }

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

            // Gate only at segment start so a rider leaving mid-segment still completes the current run.
            if (journeyProgress == 0f && CanAdvance != null && !CanAdvance())
                return (rb.position, rb.rotation);

            int nextIndex = currentIndex + direction;
            Vector2 from = worldWaypoints[currentIndex];
            Vector2 to = worldWaypoints[nextIndex];
            float dist = Vector2.Distance(from, to);

            if (journeyProgress == 0f && dist > 0.0001f)
                LastDirection = (to - from) / dist;

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

                OnSegmentArrived?.Invoke();
                return (to, rb.rotation);
            }

            float easedT = easeCurve.Evaluate(journeyProgress);
            Vector2 pos = Vector2.Lerp(from, to, easedT);
            return (pos, rb.rotation);
        }

        void OnDrawGizmos()
        {
            if (localWaypoints == null || localWaypoints.Length == 0) return;
            Gizmos.color = Color.cyan;
            Vector2 origin = rb != null ? rb.position : (Vector2)transform.position;

            for (int i = 0; i < localWaypoints.Length; i++)
            {
                Vector2 wp = Application.isPlaying && worldWaypoints != null
                    ? worldWaypoints[i]
                    : origin + localWaypoints[i];
                Gizmos.DrawSphere(wp, 0.15f);
                if (i > 0)
                {
                    Vector2 prev = Application.isPlaying && worldWaypoints != null
                        ? worldWaypoints[i - 1]
                        : origin + localWaypoints[i - 1];
                    Gizmos.DrawLine(prev, wp);
                }
            }
        }
    }
}
