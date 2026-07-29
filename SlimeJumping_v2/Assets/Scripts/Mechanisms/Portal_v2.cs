using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Portal teleport with a consume/respawn lifecycle: on trigger the slime teleports and
    /// BOTH paired portals run Consuming → Gone → Respawning → Idle.</summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class Portal_v2 : MonoBehaviour
    {
        [Header("Pair")]
        [Tooltip("Linked partner portal. Assign on either side — the partner's pair is auto-mirrored to this in Awake.")]
        [SerializeField] private Portal_v2 pair;

        [Header("Teleport")]
        [Tooltip("How far in front of the exit portal (along its OutNormal) the slime appears.")]
        [SerializeField] private float exitOffset = 0.6f;
        [Tooltip("Hard cap on exit speed to prevent runaway loops.")]
        [SerializeField] private float maxExitSpeed = 30f;

        [Header("Visual")]
        [Tooltip("Child transform that scales during consume/respawn. Must NOT carry the trigger collider.")]
        [SerializeField] private Transform visual;
        [Tooltip("Renderer on the visual that receives portalColor. Auto-detected from `visual` if left blank.")]
        [SerializeField] private Renderer visualRenderer;
        [Tooltip("Per-portal tint. Applied live in the editor.")]
        [SerializeField] private Color portalColor = new Color(0.2f, 0.4f, 1f, 1f);

        [Header("Consume / Respawn")]
        [Tooltip("Seconds the consume (shrink) animation plays before the portal disappears.")]
        [SerializeField] private float consumeDuration = 0.3f;
        [Tooltip("Seconds the portal stays invisible before the respawn animation starts.")]
        [SerializeField] private float respawnDelay = 2.5f;
        [Tooltip("Seconds the respawn (grow) animation plays. Trigger stays disabled while this plays.")]
        [SerializeField] private float respawnDuration = 0.35f;
        [Tooltip("Visual scale over consume — X: 0→1 time, Y: scale multiplier (1=full, 0=gone).")]
        [SerializeField] private AnimationCurve consumeScaleCurve =
            new AnimationCurve(new Keyframe(0f, 1f, 0f, 0f), new Keyframe(1f, 0f, -2f, 0f));
        [Tooltip("Visual scale over respawn — X: 0→1 time, Y: scale multiplier (0=gone, 1=full).")]
        [SerializeField] private AnimationCurve respawnScaleCurve =
            new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(1f, 1f, 0f, 0f));

        [Header("SFX")]
        [SerializeField] private AudioClip consumeSfx;
        [SerializeField] private AudioClip respawnSfx;
        [Range(0f, 1f)]
        [SerializeField] private float sfxVolume = 1f;

        private enum State { Idle, Consuming, Gone, Respawning }

        private State state = State.Idle;
        private float stateTimer;

        private BoxCollider2D ownCollider;
        private Vector3 visualBaseLocalScale;
        private bool visualCached;

        private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColorPropId = Shader.PropertyToID("_Color");

        public Vector2 OutNormal => transform.right;
        public Vector2 Tangent => transform.up;

        void Reset()
        {
            var col = GetComponent<BoxCollider2D>();
            col.isTrigger = true;
            col.size = new Vector2(0.3f, 1.5f);
        }

        void OnValidate()
        {
            if (visualRenderer == null && visual != null)
                visualRenderer = visual.GetComponent<Renderer>();
            ApplyColorToRenderer();
        }

        void Awake()
        {
            ownCollider = GetComponent<BoxCollider2D>();
            if (visual != null)
            {
                visualBaseLocalScale = visual.localScale;
                visualCached = true;
            }
            // Auto-mirror partnership if only one side was wired in the inspector.
            if (pair != null && pair.pair == null) pair.pair = this;
            ApplyColorToRenderer();
        }

        private void ApplyColorToRenderer()
        {
            if (visualRenderer == null) return;
            if (visualRenderer is SpriteRenderer sr)
            {
                sr.color = portalColor;
                return;
            }
            var mpb = new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorPropId, portalColor);
            mpb.SetColor(LegacyColorPropId, portalColor);
            visualRenderer.SetPropertyBlock(mpb);
        }

        void Update()
        {
            if (!visualCached) return;
            switch (state)
            {
                case State.Consuming:
                {
                    float t = 1f - stateTimer / Mathf.Max(0.0001f, consumeDuration);
                    visual.localScale = visualBaseLocalScale * consumeScaleCurve.Evaluate(Mathf.Clamp01(t));
                    break;
                }
                case State.Respawning:
                {
                    float t = 1f - stateTimer / Mathf.Max(0.0001f, respawnDuration);
                    visual.localScale = visualBaseLocalScale * respawnScaleCurve.Evaluate(Mathf.Clamp01(t));
                    break;
                }
            }
        }

        void FixedUpdate()
        {
            if (state == State.Idle) return;
            stateTimer -= Time.fixedDeltaTime;
            if (stateTimer > 0f) return;
            switch (state)
            {
                case State.Consuming:  EnterGone(); break;
                case State.Gone:       EnterRespawning(); break;
                case State.Respawning: EnterIdle(); break;
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (pair == null) return;
            if (state != State.Idle || pair.state != State.Idle) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            // Disable both triggers BEFORE the move, else the exit fires OnTriggerEnter on arrival.
            EnterConsuming();
            pair.EnterConsuming();
            Teleport(slime);
        }

        private void Teleport(SlimeController slime)
        {
            Vector2 v = slime.Velocity;
            float vIn = Vector2.Dot(v, -OutNormal);
            float vTangent = Vector2.Dot(v, Tangent);
            Vector2 newV = pair.OutNormal * vIn + pair.Tangent * vTangent;

            if (maxExitSpeed > 0f && newV.magnitude > maxExitSpeed)
                newV = newV.normalized * maxExitSpeed;

            float tangentOff = Vector2.Dot(
                (Vector2)slime.transform.position - (Vector2)transform.position, Tangent);
            Vector2 exitPos = (Vector2)pair.transform.position
                              + pair.OutNormal * exitOffset
                              - pair.Tangent * tangentOff;
            slime.TeleportTo(exitPos, newV);
        }

        private void EnterConsuming()
        {
            state = State.Consuming;
            stateTimer = Mathf.Max(0.0001f, consumeDuration);
            if (ownCollider != null) ownCollider.enabled = false;
            PlaySfx(consumeSfx);
        }

        private void EnterGone()
        {
            state = State.Gone;
            stateTimer = Mathf.Max(0f, respawnDelay);
            if (visualCached) visual.gameObject.SetActive(false);
        }

        private void EnterRespawning()
        {
            state = State.Respawning;
            stateTimer = Mathf.Max(0.0001f, respawnDuration);
            if (visualCached)
            {
                visual.gameObject.SetActive(true);
                visual.localScale = visualBaseLocalScale * respawnScaleCurve.Evaluate(0f);
            }
            PlaySfx(respawnSfx);
        }

        private void EnterIdle()
        {
            state = State.Idle;
            stateTimer = 0f;
            if (ownCollider != null) ownCollider.enabled = true;
            if (visualCached)
            {
                visual.gameObject.SetActive(true);
                visual.localScale = visualBaseLocalScale;
            }
        }

        private void PlaySfx(AudioClip clip)
        {
            if (clip == null) return;
            SoundService.PlayAt(clip, transform.position, SoundCategory.SFX_Object, sfxVolume);
        }

        [ContextMenu("Create Paired Portal")]
        private void CreatePairedPortal()
        {
#if UNITY_EDITOR
            if (pair != null)
            {
                Debug.LogWarning($"[Portal_v2] '{name}' already has a pair: '{pair.name}'.", this);
                return;
            }

            // Clone the whole GameObject so the visual child hierarchy comes along.
            var clone = Instantiate(
                gameObject,
                transform.position + (Vector3)Tangent * 3f,
                transform.rotation * Quaternion.Euler(0f, 0f, 180f),
                transform.parent);
            clone.name = name + "_Pair";

            var clonePortal = clone.GetComponent<Portal_v2>();
            clonePortal.pair = this;
            pair = clonePortal;
            clonePortal.portalColor = ComplementaryColor(portalColor);
            clonePortal.ApplyColorToRenderer();

            UnityEditor.Undo.RegisterCreatedObjectUndo(clone, "Create Paired Portal");
            UnityEditor.Selection.activeGameObject = clone;
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
        }

        private static Color ComplementaryColor(Color c) =>
            new Color(1f - c.r, 1f - c.g, 1f - c.b, c.a);

        void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            Gizmos.color = portalColor;
            Gizmos.DrawLine(p - (Vector3)Tangent * 0.5f, p + (Vector3)Tangent * 0.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, p + (Vector3)(Vector2)OutNormal * 0.5f);
        }
    }
}
