using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Platforms;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Debug-only logger for the "slime stuck under snake" bug. Reads SlimeController's
    /// public API + SurfaceProbe diagnostic accessors only — never writes back.
    /// Activates while the slime is Airborne AND in contact with the assigned SnakeSpine's
    /// collider (or any collider if snake is not assigned). Logs every fixed frame so the
    /// trace shows how custom velocity evolves vs rb.linearVelocity and actual position.
    /// </summary>
    public class SnakeUndersideLogger : MonoBehaviour
    {
        [SerializeField] SlimeController slime;
        [SerializeField] SnakeSpine snake;
        [Tooltip("Only log while slime is in Airborne state (the stuck-falling case lives here).")]
        [SerializeField] bool airborneOnly = true;
        [Tooltip("Only log while slime's primary surface is the assigned snake's collider.")]
        [SerializeField] bool filterBySnakeContact = true;
        [Tooltip("Log every N fixed frames. 1 = every frame; 5 = 10Hz at default 50Hz physics.")]
        [Min(1)] [SerializeField] int logEveryN = 1;

        Rigidbody2D slimeRb;
        Collider2D snakeCol;
        Vector2 prevPos;
        Vector2 prevCtrlVel;
        int counter;
        bool prevInitialized;

        // References only — no API calls here. SlimeController.Position reads its internal
        // Rigidbody2D which is assigned in SlimeController.Awake; running our Awake first
        // would NullRef, so any call into slime/slimeRb is deferred to FixedUpdate.
        void Awake()
        {
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
            if (snake == null) snake = FindAnyObjectByType<SnakeSpine>();
            if (slime != null) slimeRb = slime.GetComponent<Rigidbody2D>();
            if (snake != null) snakeCol = snake.GetComponent<PolygonCollider2D>();
        }

        void FixedUpdate()
        {
            if (slime == null || slimeRb == null) return;

            if (!prevInitialized)
            {
                prevPos = slime.Position;
                prevCtrlVel = slime.Velocity;
                prevInitialized = true;
                return;
            }

            bool stateOk = !airborneOnly || slime.CurrentState == SlimeController.SlimeState.Airborne;
            bool contactOk = !filterBySnakeContact || (snakeCol != null && slime.SurfaceCollider == snakeCol);
            bool shouldConsider = stateOk && contactOk;

            Vector2 pos = slime.Position;
            Vector2 ctrlVel = slime.Velocity;
            Vector2 rbVel = slimeRb.linearVelocity;
            Vector2 posDelta = pos - prevPos;
            Vector2 ctrlVelDelta = ctrlVel - prevCtrlVel;

            if (shouldConsider && (counter++ % logEveryN == 0))
            {
                var probe = slime.Probe;
                var surfaceCol = slime.SurfaceCollider;
                string surfaceColName = surfaceCol != null ? surfaceCol.name : "<none>";
                Vector2 n = slime.SurfaceNormal;
                float surfaceUp = Vector2.Dot(n, Vector2.up);

                int totalContacts = probe != null ? probe.ContactCount : 0;
                int activeContacts = probe != null ? probe.ActiveContactCount : 0;
                int selectedIdx = probe != null ? probe.SelectedIndex : -1;

                var sb = new System.Text.StringBuilder();
                sb.Append($"[SnakeUnderside] state={slime.CurrentState}\n");
                sb.Append($"  pos={pos.ToString("F3")}  Δpos={posDelta.ToString("F5")}\n");
                sb.Append($"  ctrl.vel={ctrlVel.ToString("F3")}  Δ={ctrlVelDelta.ToString("F4")}\n");
                sb.Append($"  rb.linearVelocity={rbVel.ToString("F3")}\n");
                sb.Append($"  IsTouching={slime.IsTouchingSurface} primary.normal={n.ToString("F3")} dot(up)={surfaceUp:F3}\n");
                sb.Append($"  primary.collider={surfaceColName} selectedIdx={selectedIdx}\n");
                sb.Append($"  probe.contacts total={totalContacts} active={activeContacts}");

                if (probe != null)
                {
                    for (int i = 0; i < totalContacts; i++)
                    {
                        var c = probe.GetContactCollider(i);
                        var cn = probe.GetContactNormal(i);
                        float cd = probe.GetContactDistance(i);
                        bool ca = probe.IsContactActive(i);
                        float cnUp = Vector2.Dot(cn, Vector2.up);
                        string tag = ca ? "ACTIVE" : "idle  ";
                        string nameStr = c != null ? c.name : "null";
                        string kind = cnUp > 0.5f ? "FLOOR" : (cnUp < -0.5f ? "CEIL " : "WALL ");
                        sb.Append($"\n    [{i}] {tag} {kind} {nameStr} n={cn.ToString("F3")} d={cd:F3}");
                    }
                }

                Debug.Log(sb.ToString(), this);
            }

            prevPos = pos;
            prevCtrlVel = ctrlVel;
        }
    }
}
