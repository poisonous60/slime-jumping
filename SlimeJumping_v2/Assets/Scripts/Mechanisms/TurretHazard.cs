using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>CCTV that aims at where the slime was `trackingDelay` seconds ago, telegraphs with a laser sight, then fires a knockback raycast.</summary>
    [RequireComponent(typeof(CCTVTracker))]
    public class TurretHazard : MonoBehaviour, IHazardSource
    {
        public enum State { Idle, Detecting, Telegraphing, Cooldown }

        [Header("Target")]
        [Tooltip("Target to detect. If null, auto-finds SlimeController on Start.")]
        [SerializeField] private Transform target;
        [SerializeField] private float detectionRange = 15f;
        [Tooltip("Layers that block line of sight (walls). Leave 0 to skip LOS checks.")]
        [SerializeField] private LayerMask lineOfSightBlockers = 0;

        [Header("Delayed Aim")]
        [Tooltip("Seconds the aim lags behind the slime's live position. The turret will 'remember' where the slime was this many seconds ago and aim there.")]
        [SerializeField] private float trackingDelay = 1.0f;

        [Header("Muzzle")]
        [Tooltip("Muzzle point — ray origin, muzzle flash origin. If null, uses this transform.")]
        [SerializeField] private Transform muzzle;
        [Tooltip("Local axis of muzzle that points along the barrel.")]
        [SerializeField] private CCTVTracker.LensAxis muzzleAxis = CCTVTracker.LensAxis.PositiveX;

        [Header("Timing")]
        [Tooltip("Seconds between first detection and first telegraph.")]
        [SerializeField] private float detectionDelay = 1.2f;
        [Tooltip("Seconds the laser sight shows before the bullet fires, warning the player which past-position the shot is committed to.")]
        [SerializeField] private float telegraphDuration = 0.6f;
        [Tooltip("Seconds between shots.")]
        [SerializeField] private float cooldown = 0.9f;
        [Tooltip("Seconds of continued activity after player leaves range (hysteresis).")]
        [SerializeField] private float loseSightGrace = 0.5f;

        [Header("Shot")]
        [SerializeField] private float maxShotDistance = 30f;
        [Tooltip("Layers the bullet can hit. Defaults to Default (0) + SlimeNode (6) + Environment (7). Avoid Everything (~0) — Interactable/InteractionSensor/Water triggers may block the raycast at the muzzle.")]
        [SerializeField] private LayerMask bulletHitMask = (1 << 0) | (1 << 6) | (1 << 7);
        [Tooltip("Warn in the console if the raycast's first valid hit is closer than this — catches muzzles clipping inside a wall or unwanted layer in the mask.")]
        [SerializeField] private float suspiciousHitDistance = 0.2f;

        [Header("Knockback")]
        [Tooltip("Knockback magnitude applied to slime on hit (units/sec).")]
        [SerializeField] private float knockbackForce = 14f;
        [SerializeField] private float stunDuration = 0.4f;
        [Tooltip("How the knockback direction is chosen on hit.\n" +
                 "BulletDirection — slime is pushed along the bullet's travel direction (depends on turret position).\n" +
                 "Fixed — slime is pushed in fixedKnockbackDirection regardless of turret position (like SpikeHazard.useLocalUp or JumpPad).")]
        [SerializeField] private KnockbackMode knockbackMode = KnockbackMode.BulletDirection;
        [Tooltip("World-space knockback direction used when knockbackMode = Fixed. Normalized at apply time. Default (0,1) = straight up.")]
        [SerializeField] private Vector2 fixedKnockbackDirection = Vector2.up;

        public enum KnockbackMode { BulletDirection, Fixed }

        [Header("Audio")]
        [SerializeField] private SlimeJumping.Audio.SoundDef detectSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef telegraphSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef fireSound;

        [Header("VFX")]
        [Tooltip("Particle system played at the muzzle when a shot fires.")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [Tooltip("Particle prefab spawned at the bullet impact point (e.g. ground dust). Auto-destroyed after its lifetime.")]
        [SerializeField] private ParticleSystem impactPrefab;

        [Header("Bullet Tracer")]
        [Tooltip("LineRenderer drawn from muzzle to the impact point for a brief flash after firing. If null, one is auto-created at Start.")]
        [SerializeField] private LineRenderer bulletTracer;
        [Tooltip("How long the tracer stays visible (fading out) after a shot. Short values (0.03-0.08) read as a gun flash; longer values read as a laser beam.")]
        [SerializeField] private float tracerDuration = 0.06f;
        [Tooltip("World-space Z offset applied to line positions so they render in front of 2.5D geometry (toward camera = negative Z).")]
        [SerializeField] private float lineZOffset = -0.5f;

        [Header("Telegraph Visual")]
        [Tooltip("LineRenderer drawn from muzzle to the first blocker during the Telegraphing phase. If null, one is auto-created at Start.")]
        [SerializeField] private LineRenderer laserSight;
        [Tooltip("GameObject enabled while a target is detected (before, during, and between shots). Disabled when idle.")]
        [SerializeField] private GameObject detectionIndicator;

        [Header("Aim Reticle")]
        [Tooltip("Transform placed at the current raycast hit point while a target is in range. Confirms where the turret will fire. If null, a bright sphere is auto-created.")]
        [SerializeField] private Transform aimReticle;
        [SerializeField] private float aimReticleSize = 0.4f;
        [Tooltip("Draw the raycast in the Scene view each frame (editor only).")]
        [SerializeField] private bool drawAimDebugLine = true;

        // --- runtime ---
        private CCTVTracker cctv;
        private State state = State.Idle;
        private float stateTimer;
        private float graceTimer;
        private bool indicatorOn;
        private float tracerTimer;
        private Color tracerStartColorBase;
        private Color tracerEndColorBase;
        private bool tracerColorsCaptured;
        private Transform delayedAimTarget;
        private struct PosStamp { public Vector2 pos; public float t; }
        private readonly List<PosStamp> history = new List<PosStamp>(256);

        public State CurrentState => state;
        public bool IsActive => isActiveAndEnabled;

        public HazardEffect GetHazardEffect() => new HazardEffect
        {
            knockbackForce = knockbackForce,
            knockbackDirection = Vector2.zero, // filled per-shot by bullet direction
            stunDuration = stunDuration,
            damageType = DamageType.Generic
        };

        void Awake()
        {
            cctv = GetComponent<CCTVTracker>();
        }

        void Start()
        {
            if (target == null)
            {
                var slime = FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
                if (slime != null) target = slime.transform;
            }
            ApplyIndicator(false);

            if (laserSight == null)
                laserSight = CreateLineRenderer("LaserSight_Auto", 0.06f,
                    new Color(1f, 0.1f, 0.1f, 0.6f), new Color(1f, 0.1f, 0.1f, 0.15f));
            laserSight.enabled = false;

            if (bulletTracer == null)
                bulletTracer = CreateLineRenderer("BulletTracer_Auto", 0.12f,
                    new Color(1f, 0.95f, 0.5f, 1f), new Color(1f, 0.55f, 0.1f, 1f));
            tracerStartColorBase = bulletTracer.startColor;
            tracerEndColorBase = bulletTracer.endColor;
            tracerColorsCaptured = true;
            bulletTracer.enabled = false;

            if (aimReticle == null) aimReticle = CreateAimReticle();
            aimReticle.gameObject.SetActive(false);

            // CCTV tracks this stand-in instead of the live slime.
            var dt = new GameObject("DelayedAimTarget_Auto");
            dt.transform.SetParent(transform, false);
            if (target != null) dt.transform.position = target.position;
            delayedAimTarget = dt.transform;
            if (cctv != null) cctv.Target = delayedAimTarget;
        }

        private Transform CreateAimReticle()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "AimReticle_Auto";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * aimReticleSize;

            var mr = go.GetComponent<MeshRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var mat = new Material(sh);
            Color c = new Color(1f, 0.3f, 0.1f, 1f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            mat.color = c;
            mr.sharedMaterial = mat;
            return go.transform;
        }

        private LineRenderer CreateLineRenderer(string name, float width, Color start, Color end)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.widthMultiplier = width;
            lr.numCapVertices = 2;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 100;

            // Sprites/Default respects LineRenderer's vertex colors (startColor/endColor).
            // URP/Unlit does NOT multiply by vertex color → everything rendered white.
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            var mat = new Material(sh);
            mat.mainTexture = Texture2D.whiteTexture; // required: Sprites/Default samples _MainTex
            lr.material = mat;

            lr.startColor = start;
            lr.endColor = end;
            lr.enabled = false;
            return lr;
        }

        void OnDisable()
        {
            if (laserSight != null) laserSight.enabled = false;
            if (bulletTracer != null) bulletTracer.enabled = false;
            if (aimReticle != null) aimReticle.gameObject.SetActive(false);
            tracerTimer = 0f;
            ApplyIndicator(false);
            state = State.Idle;
        }

        private void UpdateDelayedAim()
        {
            if (target == null || delayedAimTarget == null) return;

            float now = Time.time;
            history.Add(new PosStamp { pos = target.position, t = now });

            float threshold = now - Mathf.Max(0f, trackingDelay);

            // Find the newest stamp at or before `threshold`. Linear scan — history is kept small by trimming below.
            int idx = -1;
            for (int i = history.Count - 1; i >= 0; i--)
            {
                if (history[i].t <= threshold) { idx = i; break; }
            }

            Vector2 delayedPos;
            if (idx < 0)
            {
                // Not enough history yet — use the oldest we have (aim starts pinned to early position).
                delayedPos = history[0].pos;
            }
            else
            {
                delayedPos = history[idx].pos;
                if (idx > 0) history.RemoveRange(0, idx);
            }

            delayedAimTarget.position = new Vector3(delayedPos.x, delayedPos.y, target.position.z);
        }

        private void UpdateAimReticle(bool seen)
        {
            if (aimReticle == null) return;
            if (!seen)
            {
                if (aimReticle.gameObject.activeSelf) aimReticle.gameObject.SetActive(false);
                return;
            }
            Vector3 origin = AimOrigin();
            Vector3 dir = AimDirection();
            Vector2 dir2D = new Vector2(dir.x, dir.y);
            if (dir2D.sqrMagnitude < 0.0001f) dir2D = Vector2.right;
            dir2D.Normalize();

            var hit = RaycastExcludingSelf((Vector2)origin, dir2D, maxShotDistance, bulletHitMask);

#if UNITY_EDITOR
            if (drawAimDebugLine)
            {
                Vector3 endDebug = hit.collider != null
                    ? (Vector3)hit.point
                    : origin + new Vector3(dir2D.x, dir2D.y, 0f) * maxShotDistance;
                Debug.DrawLine(origin, endDebug, hit.collider != null ? Color.red : Color.gray, 0f, false);
            }
            if (hit.collider != null && hit.distance < suspiciousHitDistance)
            {
                int layer = hit.collider.gameObject.layer;
                Debug.LogWarning(
                    $"[TurretHazard:{name}] Aim ray blocked at {hit.distance:F3} by '{hit.collider.name}' " +
                    $"(layer {layer}={LayerMask.LayerToName(layer)}). " +
                    $"Remove that layer from Bullet Hit Mask or move the muzzle outside the collider.",
                    hit.collider);
            }
#endif

            if (hit.collider == null)
            {
                if (aimReticle.gameObject.activeSelf) aimReticle.gameObject.SetActive(false);
                return;
            }

            if (!aimReticle.gameObject.activeSelf) aimReticle.gameObject.SetActive(true);
            aimReticle.position = new Vector3(hit.point.x, hit.point.y, origin.z + lineZOffset);
        }

        void Update()
        {
            float dt = Time.deltaTime;

            UpdateTracerFade(dt);
            UpdateDelayedAim();

            bool inRange = IsTargetVisible();
            graceTimer = inRange ? loseSightGrace : Mathf.Max(0f, graceTimer - dt);
            bool effectivelySeen = inRange || graceTimer > 0f;

            UpdateAimReticle(effectivelySeen);

            switch (state)
            {
                case State.Idle:
                    if (effectivelySeen) EnterDetecting();
                    break;

                case State.Detecting:
                    if (!effectivelySeen) { EnterIdle(); break; }
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterTelegraphing();
                    break;

                case State.Telegraphing:
                    UpdateLaserSight();
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        FireShot();
                        EnterCooldown();
                    }
                    break;

                case State.Cooldown:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        if (effectivelySeen) EnterTelegraphing();
                        else EnterIdle();
                    }
                    break;
            }
        }

        // --- Transitions ---

        private void EnterIdle()
        {
            state = State.Idle;
            ApplyIndicator(false);
            if (laserSight != null) laserSight.enabled = false;
        }

        private void EnterDetecting()
        {
            state = State.Detecting;
            stateTimer = detectionDelay;
            ApplyIndicator(true);
            if (laserSight != null) laserSight.enabled = false;
            if (detectSound != null)
                SoundService.Play(detectSound, AimOrigin());
        }

        private void EnterTelegraphing()
        {
            state = State.Telegraphing;
            stateTimer = telegraphDuration;
            ApplyIndicator(true);
            if (laserSight != null) laserSight.enabled = true;
            if (telegraphSound != null)
                SoundService.Play(telegraphSound, AimOrigin());
        }

        private void EnterCooldown()
        {
            state = State.Cooldown;
            stateTimer = cooldown;
            if (laserSight != null) laserSight.enabled = false;
        }

        private void ApplyIndicator(bool on)
        {
            if (indicatorOn == on) return;
            indicatorOn = on;
            if (detectionIndicator != null) detectionIndicator.SetActive(on);
        }

        // --- Detection ---

        private bool IsTargetVisible()
        {
            if (target == null) return false;
            Vector3 origin = AimOrigin();
            Vector3 delta = target.position - origin;
            float distSqr = delta.sqrMagnitude;
            if (distSqr > detectionRange * detectionRange) return false;
            if (lineOfSightBlockers == 0) return true;

            float dist = Mathf.Sqrt(distSqr);
            Vector2 dir2D = new Vector2(delta.x, delta.y);
            if (dir2D.sqrMagnitude < 0.0001f) return true;
            dir2D.Normalize();

            var hit = RaycastExcludingSelf((Vector2)origin, dir2D, dist, lineOfSightBlockers);
            if (hit.collider == null) return true;
            var slime = hit.collider.GetComponentInParent<SlimeController>();
            return slime != null && slime.transform == target;
        }

        /// <summary>Raycast skipping colliders under this turret, else the barrel/mount blocks every shot at distance ~0.</summary>
        private RaycastHit2D RaycastExcludingSelf(Vector2 origin, Vector2 dir, float maxDist, LayerMask mask)
        {
            var hits = Physics2D.RaycastAll(origin, dir, maxDist, mask);
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                return h;
            }
            return default;
        }

        // --- Aim / Fire ---

        private Vector3 AimOrigin() => muzzle != null ? muzzle.position : transform.position;

        private Vector3 AimDirection()
        {
            Transform t = muzzle != null ? muzzle : transform;
            return t.rotation * AxisToVec(muzzleAxis);
        }

        private void UpdateLaserSight()
        {
            if (laserSight == null) return;
            Vector3 origin = AimOrigin();
            Vector3 dir = AimDirection();
            Vector2 dir2D = new Vector2(dir.x, dir.y);
            if (dir2D.sqrMagnitude < 0.0001f) dir2D = Vector2.right;
            dir2D.Normalize();

            float reach = maxShotDistance;
            if (bulletHitMask != 0)
            {
                var hit = RaycastExcludingSelf((Vector2)origin, dir2D, maxShotDistance, bulletHitMask);
                if (hit.collider != null) reach = hit.distance;
            }
            Vector3 start = new Vector3(origin.x, origin.y, origin.z + lineZOffset);
            Vector3 end = new Vector3(origin.x + dir2D.x * reach, origin.y + dir2D.y * reach, origin.z + lineZOffset);
            laserSight.useWorldSpace = true;
            laserSight.positionCount = 2;
            laserSight.SetPosition(0, start);
            laserSight.SetPosition(1, end);
        }

        private void FireShot()
        {
            Vector3 origin = AimOrigin();
            Vector3 dir = AimDirection();
            Vector2 dir2D = new Vector2(dir.x, dir.y);
            if (dir2D.sqrMagnitude < 0.0001f) dir2D = Vector2.right;
            dir2D.Normalize();

            if (muzzleFlash != null) muzzleFlash.Play(true);
            if (fireSound != null)
                SoundService.Play(fireSound, origin);

            var hit = RaycastExcludingSelf((Vector2)origin, dir2D, maxShotDistance, bulletHitMask);
            Vector3 tracerEnd;
            if (hit.collider == null)
            {
                tracerEnd = origin + new Vector3(dir2D.x, dir2D.y, 0f) * maxShotDistance;
                ShowTracer(origin, tracerEnd);
                return;
            }

            tracerEnd = hit.point;
            ShowTracer(origin, tracerEnd);

            var slime = hit.collider.GetComponentInParent<SlimeController>();
            if (slime != null)
            {
                var effect = GetHazardEffect();
                Vector2 kbDir = knockbackMode == KnockbackMode.Fixed
                    ? (fixedKnockbackDirection.sqrMagnitude > 0.0001f
                        ? fixedKnockbackDirection.normalized
                        : Vector2.up)
                    : dir2D;
                effect.knockbackDirection = kbDir;
                slime.ApplyHazardEffect(effect);
#if UNITY_EDITOR
                Debug.Log($"[TurretHazard:{name}] HIT slime at {(Vector2)hit.point} " +
                          $"force={effect.knockbackForce} mode={knockbackMode} dir={kbDir} stun={effect.stunDuration}", this);
#endif
                return;
            }

            SpawnImpact(hit.point, hit.normal);
        }

        private void ShowTracer(Vector3 start, Vector3 end)
        {
            if (bulletTracer == null || tracerDuration <= 0f) return;
            Vector3 s = new Vector3(start.x, start.y, start.z + lineZOffset);
            Vector3 e = new Vector3(end.x, end.y, start.z + lineZOffset);
            bulletTracer.useWorldSpace = true;
            bulletTracer.positionCount = 2;
            bulletTracer.SetPosition(0, s);
            bulletTracer.SetPosition(1, e);
            bulletTracer.startColor = tracerStartColorBase;
            bulletTracer.endColor = tracerEndColorBase;
            bulletTracer.enabled = true;
            tracerTimer = tracerDuration;
        }

        private void UpdateTracerFade(float dt)
        {
            if (bulletTracer == null || tracerTimer <= 0f) return;
            tracerTimer -= dt;
            if (tracerTimer <= 0f)
            {
                bulletTracer.enabled = false;
                tracerTimer = 0f;
                return;
            }
            if (!tracerColorsCaptured) return;
            float k = Mathf.Clamp01(tracerTimer / Mathf.Max(tracerDuration, 0.0001f));
            Color s = tracerStartColorBase; s.a *= k;
            Color e = tracerEndColorBase; e.a *= k;
            bulletTracer.startColor = s;
            bulletTracer.endColor = e;
        }

        private void SpawnImpact(Vector3 point, Vector3 normal)
        {
            if (impactPrefab == null) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            // Nudge toward camera so the burst isn't occluded by the 2.5D ground mesh.
            Vector3 spawnPos = new Vector3(point.x, point.y, point.z + lineZOffset);
            var inst = Instantiate(impactPrefab, spawnPos, rot);
            // Template may be a disabled prefab; the clone inherits that state, so re-activate.
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.2f;
            Destroy(inst.gameObject, life);
        }

        private static Vector3 AxisToVec(CCTVTracker.LensAxis a)
        {
            switch (a)
            {
                case CCTVTracker.LensAxis.PositiveZ: return Vector3.forward;
                case CCTVTracker.LensAxis.NegativeZ: return Vector3.back;
                case CCTVTracker.LensAxis.PositiveX: return Vector3.right;
                case CCTVTracker.LensAxis.NegativeX: return Vector3.left;
                case CCTVTracker.LensAxis.PositiveY: return Vector3.up;
                case CCTVTracker.LensAxis.NegativeY: return Vector3.down;
            }
            return Vector3.forward;
        }

        void OnDrawGizmosSelected()
        {
            Vector3 origin = AimOrigin();
            Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.35f);
            Gizmos.DrawWireSphere(origin, detectionRange);

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Vector3 d = AimDirection().normalized;
            Gizmos.DrawLine(origin, origin + d * Mathf.Min(maxShotDistance, detectionRange));

            if (knockbackMode == KnockbackMode.Fixed && fixedKnockbackDirection.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.9f);
                Vector3 kbDir = (Vector3)fixedKnockbackDirection.normalized;
                Vector3 tail = origin;
                Vector3 tip = tail + kbDir * 1.5f;
                Gizmos.DrawLine(tail, tip);
                Vector3 perp = new Vector3(-kbDir.y, kbDir.x, 0f) * 0.2f;
                Gizmos.DrawLine(tip, tip - kbDir * 0.4f + perp);
                Gizmos.DrawLine(tip, tip - kbDir * 0.4f - perp);
            }
        }
    }
}
