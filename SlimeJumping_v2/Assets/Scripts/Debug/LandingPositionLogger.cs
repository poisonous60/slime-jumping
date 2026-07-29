using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>Landing-pop diagnostic: logs rb.position.x vs transform.position.x around Airborne→Attached transitions.</summary>
    public class LandingPositionLogger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private int captureWindow = 10;

        private Rigidbody2D rb;
        private SlimeController.SlimeState prevState;

        private struct LogEntry
        {
            public int frame;
            public string phase; // "Fixed" or "Late"
            public string state;
            public float rbPosX;
            public float tfPosX;
            public float velX;
        }

        private LogEntry[] buffer;
        private int bufferHead;
        private int bufferCount;

        private int transitionFrame = -1;
        private int postTransitionCount;
        private bool flushed;

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
            rb = slimeController.GetComponent<Rigidbody2D>();

            int bufferSize = captureWindow * 4; // Fixed+Late, some margin
            buffer = new LogEntry[bufferSize];
        }

        private void FixedUpdate()
        {
            if (rb == null) return;

            var state = slimeController.CurrentState;

            if (prevState == SlimeController.SlimeState.Airborne && state == SlimeController.SlimeState.Attached)
            {
                transitionFrame = Time.frameCount;
                postTransitionCount = 0;
                flushed = false;
            }
            prevState = state;

            Push(new LogEntry
            {
                frame = Time.frameCount,
                phase = "Fixed",
                state = state.ToString(),
                rbPosX = rb.position.x,
                tfPosX = transform.position.x,
                velX = rb.linearVelocity.x,
            });
        }

        private void LateUpdate()
        {
            if (rb == null) return;

            var state = slimeController.CurrentState;

            Push(new LogEntry
            {
                frame = Time.frameCount,
                phase = "Late",
                state = state.ToString(),
                rbPosX = rb.position.x,
                tfPosX = transform.position.x,
                velX = rb.linearVelocity.x,
            });

            if (transitionFrame >= 0 && !flushed)
            {
                postTransitionCount++;
                if (postTransitionCount >= captureWindow)
                {
                    FlushBuffer();
                    flushed = true;
                    transitionFrame = -1;
                }
            }
        }

        private void Push(LogEntry entry)
        {
            buffer[bufferHead] = entry;
            bufferHead = (bufferHead + 1) % buffer.Length;
            if (bufferCount < buffer.Length) bufferCount++;
        }

        private void FlushBuffer()
        {
            Debug.Log("===== LANDING POSITION LOG =====");

            int start = (bufferHead - bufferCount + buffer.Length) % buffer.Length;
            for (int i = 0; i < bufferCount; i++)
            {
                var e = buffer[(start + i) % buffer.Length];
                Debug.Log($"[F{e.frame}][{e.state}][{e.phase}] rb.x={e.rbPosX:F4}  tf.x={e.tfPosX:F4}  vel.x={e.velX:F4}");
            }

            Debug.Log("===== END LANDING LOG =====");
            bufferCount = 0;
            bufferHead = 0;
        }
    }
}
