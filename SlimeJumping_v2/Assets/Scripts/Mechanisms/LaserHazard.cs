using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Visual;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Patrolling laser beam. Beam runs along local +X (rotate root Z for a vertical laser),
    /// patrols along local +Y between start and start + patrolDistance at constant speed.
    /// Contact adds a fixed-angle velocity via ApplyHazardPush — no stun; SlimeBurnVisual
    /// picks up the burn mark from the same event. Placement knobs live here; shared tuning
    /// (hit, beam width, visual) comes from <see cref="LaserHazardConfig"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public class LaserHazard : MonoBehaviour
    {
        [Header("Config")]
        [Tooltip("Shared tuning asset. Empty = auto-load Resources/LaserHazardConfig.")]
        [SerializeField] private LaserHazardConfig config;

        [Header("Placement")]
        [Tooltip("Beam span along local X (world units). Collider and visual both follow this.")]
        [SerializeField] private float beamLength = 5f;
        [Tooltip("One-way travel along local +Y from the placed position. 0 = static laser.")]
        [SerializeField] private float patrolDistance = 4f;
        [Tooltip("Constant patrol speed (units/s).")]
        [SerializeField] private float moveSpeed = 2f;
        [Tooltip("Pause at each end before reversing (seconds).")]
        [SerializeField] private float endDelay = 0.5f;
        [Tooltip("Initial position along the patrol, in units from the placed position (0 = placed spot, patrolDistance = far end).")]
        [SerializeField] private float startOffset = 0f;
        [Tooltip("Start moving back toward the placed position instead of away from it.")]
        [SerializeField] private bool startReversed = false;

        private Rigidbody2D rb;
        private BoxCollider2D col;
        private LineRenderer coreLine;
        private LineRenderer glowLine;

        private Vector2 startPos;
        private float patrolOffset;
        private float patrolDir = 1f;
        private float waitTimer;
        private float nextHitTime;
        private SlimeController sweepSlime;
        private Vector2 prevSlimePos;
        private bool hasPrevSlimePos;
        private SlimeController cachedBurnSlime;
        private SlimeBurnVisual cachedBurnVisual;

        private static Texture2D beamGradientTex;
        private static Material beamMaterial;

        private LaserHazardConfig Cfg
        {
            get
            {
                if (config == null) config = Resources.Load<LaserHazardConfig>("LaserHazardConfig");
                return config;
            }
        }

        void Reset()
        {
            var body = GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            var box = GetComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(beamLength, Cfg != null ? Cfg.beamWidth : 0.25f);
        }

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<BoxCollider2D>();
            if (Cfg == null)
            {
                Debug.LogWarning("[LaserHazard] No LaserHazardConfig assigned and none in Resources/LaserHazardConfig.", this);
                enabled = false;
                return;
            }
            rb.bodyType = RigidbodyType2D.Kinematic;
            ConfigureCollider();
            startPos = rb.position;
            patrolOffset = Mathf.Clamp(startOffset, 0f, patrolDistance);
            patrolDir = startReversed ? -1f : 1f;
            if (patrolOffset > 0f) rb.position = startPos + (Vector2)transform.up * patrolOffset;
            BuildBeamVisual();
            SpawnEndCaps();
        }

        void OnValidate()
        {
            beamLength = Mathf.Max(0.01f, beamLength);
            startOffset = Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, patrolDistance));
            var box = GetComponent<BoxCollider2D>();
            if (box != null)
            {
                box.isTrigger = true;
                box.size = new Vector2(beamLength, Cfg != null ? Cfg.beamWidth : 0.25f);
            }
            if (coreLine != null) ApplyBeamShape();
        }

        // Solid box = beam at rest position, faint box = far end of the patrol.
        void OnDrawGizmosSelected()
        {
            float width = Cfg != null ? Cfg.beamWidth : 0.25f;
            Vector3 origin = Application.isPlaying && rb != null
                ? new Vector3(startPos.x, startPos.y, transform.position.z)
                : transform.position;
            Gizmos.matrix = Matrix4x4.TRS(origin, transform.rotation, Vector3.one);
            Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(beamLength, width, 0f));
            if (patrolDistance > 0.0001f)
            {
                Gizmos.color = new Color(1f, 0.2f, 0.1f, 0.35f);
                Gizmos.DrawWireCube(new Vector3(0f, patrolDistance, 0f), new Vector3(beamLength, width, 0f));
                Gizmos.DrawLine(new Vector3(-beamLength * 0.5f, 0f, 0f), new Vector3(-beamLength * 0.5f, patrolDistance, 0f));
                Gizmos.DrawLine(new Vector3(beamLength * 0.5f, 0f, 0f), new Vector3(beamLength * 0.5f, patrolDistance, 0f));
                if (startOffset > 0.0001f)
                {
                    Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
                    Gizmos.DrawWireCube(new Vector3(0f, Mathf.Min(startOffset, patrolDistance), 0f),
                        new Vector3(beamLength, width, 0f));
                }
            }

            // Animated preview: solid box ping-pongs along the patrol in edit mode
            // (LaserHazardScenePreview keeps the SceneView repainting); play mode shows the
            // real offset. Caps ride it, so knob changes read live without entering play.
            float previewOff;
            if (Application.isPlaying)
            {
                previewOff = patrolOffset;
            }
            else
            {
#if UNITY_EDITOR
                previewOff = PreviewOffsetAt((float)UnityEditor.EditorApplication.timeSinceStartup);
#else
                previewOff = Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, patrolDistance));
#endif
            }
            Gizmos.color = new Color(1f, 0.25f, 0.1f, 0.45f);
            Gizmos.DrawCube(new Vector3(0f, previewOff, 0f), new Vector3(beamLength, width, 0f));

            // End caps only exist at runtime (Awake spawn) — draw their meshes here so
            // placement is visible in edit mode.
            var cfg = Cfg;
            if (cfg != null)
            {
                Gizmos.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
                DrawCapGizmo(cfg.endCapA, -1f, previewOff);
                DrawCapGizmo(cfg.endCapB, 1f, previewOff);
            }
        }

        // Deterministic patrol phase for the edit-mode preview: up → endDelay → down → endDelay,
        // phase-shifted so startOffset/startReversed show their real starting point.
        private float PreviewOffsetAt(float t)
        {
            float dist = patrolDistance;
            if (dist <= 0.0001f || moveSpeed <= 0f)
                return Mathf.Clamp(startOffset, 0f, Mathf.Max(0f, dist));
            float travel = dist / moveSpeed;
            float cycle = 2f * (travel + endDelay);
            float s0 = Mathf.Clamp(startOffset, 0f, dist);
            float phase0 = startReversed
                ? travel + endDelay + (dist - s0) / moveSpeed
                : s0 / moveSpeed;
            float p = Mathf.Repeat(t + phase0, cycle);
            if (p < travel) return p * moveSpeed;
            p -= travel;
            if (p < endDelay) return dist;
            p -= endDelay;
            if (p < travel) return dist - p * moveSpeed;
            return 0f;
        }

        private void DrawCapGizmo(LaserHazardConfig.EndCapSide side, float sign, float yOffset)
        {
            if (side == null || side.prefab == null) return;
            Matrix4x4 baseM = Gizmos.matrix;
            Transform root = side.prefab.transform;
            Matrix4x4 capM = baseM * Matrix4x4.TRS(
                CapLocalPosition(side, sign, yOffset),
                SideRotation(sign) * Quaternion.Euler(side.rotation),
                side.scale);
            foreach (var mf in side.prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Gizmos.matrix = capM * (root.worldToLocalMatrix * mf.transform.localToWorldMatrix);
                Gizmos.DrawMesh(mf.sharedMesh);
            }
            Gizmos.matrix = baseM;
        }

        private void ConfigureCollider()
        {
            col.isTrigger = true;
            col.size = new Vector2(beamLength, Cfg.beamWidth);
            col.offset = Vector2.zero;
        }

        void FixedUpdate()
        {
            if (WorldTimeFreezeService.IsFrozen) return;

            Vector2 beamPrev = rb.position;
            Vector2 beamNext = beamPrev;
            if (patrolDistance > 0.0001f && moveSpeed > 0f)
            {
                if (waitTimer > 0f)
                {
                    waitTimer -= Time.fixedDeltaTime;
                }
                else
                {
                    patrolOffset += patrolDir * moveSpeed * Time.fixedDeltaTime;
                    if (patrolOffset >= patrolDistance)
                    {
                        patrolOffset = patrolDistance;
                        patrolDir = -1f;
                        waitTimer = endDelay;
                    }
                    else if (patrolOffset <= 0f)
                    {
                        patrolOffset = 0f;
                        patrolDir = 1f;
                        waitTimer = endDelay;
                    }
                    beamNext = startPos + (Vector2)transform.up * patrolOffset;
                    rb.MovePosition(beamNext);
                }
            }
            SweepCheck(beamNext - beamPrev, beamNext);
        }

        // Trigger events alone tunnel when the slime crosses the thin beam faster than one
        // physics step; a relative segment sweep in the beam frame catches those crossings.
        private void SweepCheck(Vector2 beamDelta, Vector2 beamCenter)
        {
            if (sweepSlime == null)
            {
                sweepSlime = FindFirstObjectByType<SlimeController>();
                hasPrevSlimePos = false;
                if (sweepSlime == null) return;
            }

            Vector2 curr = sweepSlime.Position;
            if (!hasPrevSlimePos)
            {
                prevSlimePos = curr;
                hasPrevSlimePos = true;
                return;
            }
            Vector2 prevRel = prevSlimePos + beamDelta;
            prevSlimePos = curr;

            if (Time.time < nextHitTime) return;

            Vector2 dir = transform.right;
            Vector2 half = dir * (beamLength * 0.5f);
            Vector2 beamA = beamCenter - half;
            Vector2 beamB = beamCenter + half;

            float slimeRadius = sweepSlime.BodyCollider != null ? sweepSlime.BodyCollider.radius : 0.2f;
            float threshold = Cfg.beamWidth * 0.5f + slimeRadius;
            if (SegSegDistance(prevRel, curr, beamA, beamB) > threshold) return;

            HitSlime(sweepSlime, ClosestPointOnSegment(beamA, beamB, curr));
        }

        void Update()
        {
            if (WorldTimeFreezeService.IsFrozen) return;
            if (coreLine == null) return;
            float pulse = 1f + Cfg.pulseAmount * (Mathf.PerlinNoise(Time.time * Cfg.pulseSpeed, 0f) - 0.5f);
            coreLine.widthMultiplier = Cfg.coreWidth * pulse;
            glowLine.widthMultiplier = Cfg.glowWidth * pulse;
        }

        void OnTriggerEnter2D(Collider2D other) => TryHit(other);
        void OnTriggerStay2D(Collider2D other) => TryHit(other);

        private void TryHit(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            HitSlime(slime, col.ClosestPoint(slime.Position));
        }

        private void HitSlime(SlimeController slime, Vector2 hitPoint)
        {
            if (Cfg == null) return;
            if (Time.time < nextHitTime) return;
            if (cachedBurnSlime != slime)
            {
                cachedBurnSlime = slime;
                cachedBurnVisual = slime.GetComponentInChildren<SlimeBurnVisual>();
            }
            if (cachedBurnVisual != null) cachedBurnVisual.SetNextBurnScale(Cfg.burnMarkScale);

            // Burn anchors in the heading frame (head = 12 o'clock): rotate the world hit
            // direction by the velocity heading and re-project onto the body rim. The mesh is
            // world-axis locked (SlimeVisual cancels body rotation), so without this the mark
            // ignores which way the slime is flying. Near-zero velocity keeps the raw contact.
            Vector2 vel = slime.Velocity;
            Vector2 toHit = hitPoint - slime.Position;
            if (vel.sqrMagnitude > 0.01f && toHit.sqrMagnitude > 0.0001f)
            {
                float headingOffset = Mathf.Atan2(vel.y, vel.x) * Mathf.Rad2Deg - 90f;
                Vector2 burnDir = Quaternion.Euler(0f, 0f, headingOffset) * toHit.normalized;
                float bodyR = slime.BodyCollider != null ? slime.BodyCollider.radius : 0.2f;
                hitPoint = slime.Position + burnDir * bodyR;
            }

            Vector2 dir = Quaternion.Euler(0f, 0f, Cfg.knockbackAngle) * Vector2.right;
            // Attached consumes only the tangent component of the push channel, so a stuck
            // slime would slide instead of flying — lift off first, then push while Airborne.
            // Charging stays on the tangent channel (a charge must end in a launch).
            if (slime.CurrentState == SlimeController.SlimeState.Attached)
                slime.ForceAirborneWithVelocity(slime.Velocity);
            slime.ApplyHazardPush(dir * Cfg.knockbackSpeed, hitPoint);
            if (Cfg.hitClip != null)
                SoundService.PlayAt(Cfg.hitClip, slime.Position, SoundCategory.SFX_Object, Cfg.hitVolume);
            nextHitTime = Time.time + Cfg.hitCooldown;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return a + ab * t;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static float SegSegDistance(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            float o1 = Cross(p2 - p1, q1 - p1);
            float o2 = Cross(p2 - p1, q2 - p1);
            float o3 = Cross(q2 - q1, p1 - q1);
            float o4 = Cross(q2 - q1, p2 - q1);
            if (o1 * o2 < 0f && o3 * o4 < 0f) return 0f;

            float d = Vector2.Distance(p1, ClosestPointOnSegment(q1, q2, p1));
            d = Mathf.Min(d, Vector2.Distance(p2, ClosestPointOnSegment(q1, q2, p2)));
            d = Mathf.Min(d, Vector2.Distance(q1, ClosestPointOnSegment(p1, p2, q1)));
            d = Mathf.Min(d, Vector2.Distance(q2, ClosestPointOnSegment(p1, p2, q2)));
            return d;
        }

        // Visual-only emitter models riding the beam tips. Prefab +X faces into the beam;
        // the +X-side cap gets a Y-mirror so both point inward.
        private void SpawnEndCaps()
        {
            SpawnCap("EndCapA", Cfg.endCapA, -1f);
            SpawnCap("EndCapB", Cfg.endCapB, 1f);
        }

        // sign: -1 = local -X end (identity), +1 = local +X end (Y-mirrored). positionOffset is
        // rotated by the side mirror so +X always means "into the beam" on both sides.
        private Quaternion SideRotation(float sign)
            => sign < 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

        private Vector3 CapLocalPosition(LaserHazardConfig.EndCapSide side, float sign, float yOffset)
        {
            return new Vector3(sign * beamLength * 0.5f, yOffset, Cfg.visualZOffset)
                   + SideRotation(sign) * side.positionOffset;
        }

        private void SpawnCap(string name, LaserHazardConfig.EndCapSide side, float sign)
        {
            if (side == null || side.prefab == null) return;
            var cap = Instantiate(side.prefab, transform);
            cap.name = name;
            cap.transform.localPosition = CapLocalPosition(side, sign, 0f);
            cap.transform.localRotation = SideRotation(sign) * Quaternion.Euler(side.rotation);
            cap.transform.localScale = side.scale;
        }

        // White-core + red-glow look from two stacked LineRenderers sharing a soft
        // vertical-falloff texture (per the reference: center-white, edges-dark gradient).
        private void BuildBeamVisual()
        {
            glowLine = MakeLine("LaserGlow", Cfg.glowWidth, Cfg.glowColor, 100);
            coreLine = MakeLine("LaserCore", Cfg.coreWidth, Cfg.coreColor, 101);
            ApplyBeamShape();
        }

        private void ApplyBeamShape()
        {
            float z = Cfg != null ? Cfg.visualZOffset : 0f;
            float trim = Cfg != null ? Cfg.beamVisualTrim : 0f;
            float half = Mathf.Max(0.005f, beamLength * 0.5f - trim);
            var p0 = new Vector3(-half, 0f, z);
            var p1 = new Vector3(half, 0f, z);
            coreLine.SetPosition(0, p0);
            coreLine.SetPosition(1, p1);
            glowLine.SetPosition(0, p0);
            glowLine.SetPosition(1, p1);
        }

        private LineRenderer MakeLine(string name, float width, Color color, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.sharedMaterial = GetBeamMaterial();
            lr.startColor = color;
            lr.endColor = color;
            lr.widthMultiplier = width;
            lr.sortingOrder = sortingOrder;
            return lr;
        }

        private static Material GetBeamMaterial()
        {
            if (beamMaterial != null) return beamMaterial;
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            beamMaterial = new Material(sh);
            beamMaterial.hideFlags = HideFlags.HideAndDontSave;
            beamMaterial.mainTexture = GetBeamGradient();
            return beamMaterial;
        }

        private static Texture2D GetBeamGradient()
        {
            if (beamGradientTex != null) return beamGradientTex;
            const int H = 32;
            var tex = new Texture2D(1, H, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < H; y++)
            {
                float edge = Mathf.Abs((y + 0.5f) / H * 2f - 1f);
                float a = (1f - edge) * (1f - edge);
                tex.SetPixel(0, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            beamGradientTex = tex;
            return tex;
        }
    }
}
