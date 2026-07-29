using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Beam line for <see cref="GraniteBlastEmitter"/>: the tip grows from muzzle at travelSpeed, halts on the first hit, and lives out <c>duration</c>; travelSpeed 0 = instant full length.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class GraniteBlastBeam : MonoBehaviour
    {
        // Without a floor, a zero-distance hit (muzzle inside a hit-mask collider) collapses
        // both LR endpoints to one point and only the cap circle is visible.
        private const float MinVisibleReach = 0.5f;

        private GraniteBlastEmitter owner;
        private Transform muzzle;
        private Vector2 lockedDir;
        private LineRenderer lr;
        private float baseWidth;

        private float duration;
        private float fadeTime;
        private float travelSpeed;
        private float trackAngularSpeed;
        private float trackLag;
        private float age;
        private bool initialized;

        // Projectile-style growth state
        private float currentReach;       // tip distance from muzzle right now
        private float prevReach;          // tip distance last frame (for collision sweep)
        private bool tipHalted;           // true once the tip stops advancing
        private bool hitResolved;         // slime hazard applied once
        private bool groundImpactPlayed;
        private ParticleSystem activeImpact;

        void Awake()
        {
            // Configure here so the LR can draw on the first frame regardless of Init's timing.
            lr = GetComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.textureMode = LineTextureMode.Stretch;
            lr.sortingOrder = 100;
            lr.positionCount = 2;
            lr.enabled = false;
        }

        public void Init(GraniteBlastEmitter emitter, Transform muzzle, Vector2 fireDir,
                         float duration, float fadeTime, float coreWidth, float travelSpeed,
                         float trackAngularSpeed = 0f, float trackLag = 0f)
        {
            this.owner = emitter;
            this.muzzle = muzzle;
            Vector2 d = fireDir;
            // Snapshot the start dir from the lagged trail so both track modes begin aimed
            // trackLag seconds behind the slime.
            if (trackAngularSpeed > 0f && emitter != null && emitter.Target != null && muzzle != null)
            {
                Vector2 laggedPos = emitter.GetLaggedTargetPosition(Mathf.Max(0f, trackLag));
                Vector2 toLagged = laggedPos - (Vector2)muzzle.position;
                if (toLagged.sqrMagnitude > 0.0001f) d = toLagged.normalized;
            }
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            else d.Normalize();
            this.lockedDir = d;
            this.duration = Mathf.Max(0.05f, duration);
            this.fadeTime = Mathf.Max(0.001f, fadeTime);
            this.baseWidth = Mathf.Max(0.001f, coreWidth);
            this.travelSpeed = Mathf.Max(0f, travelSpeed);
            this.trackAngularSpeed = Mathf.Max(0f, trackAngularSpeed);
            this.trackLag = Mathf.Max(0f, trackLag);

            // travelSpeed <= 0 → instant full length; the first tick still resolves the halt
            // point with one sweep over [0, maxRange] because prevReach stays 0.
            currentReach = this.travelSpeed > 0f ? 0f : (owner != null ? owner.MaxRange : 0f);
            prevReach = 0f;

            ConfigureMaterial();
            lr.widthMultiplier = baseWidth;
            DrawCurrent();
            lr.enabled = true;
            initialized = true;
        }

        private void ConfigureMaterial()
        {
            Material mat = owner != null ? owner.GetCoreMaterial() : null;
            if (mat == null)
            {
                Shader sh = Shader.Find("Sprites/Default");
                if (sh == null) sh = Shader.Find("UI/Default");
                mat = new Material(sh);
                mat.mainTexture = Texture2D.whiteTexture;
            }
            lr.material = mat;

            Color c = owner != null ? owner.CoreColor : new Color(0.85f, 0.96f, 1f, 1f);
            lr.startColor = c;
            lr.endColor = c;
        }

        private void DrawCurrent()
        {
            if (lr == null || muzzle == null || owner == null) return;
            Vector3 origin = muzzle.position;
            float visibleReach = Mathf.Max(MinVisibleReach, currentReach);
            float zOff = owner.LineZOffset;
            Vector3 start = new Vector3(origin.x, origin.y, origin.z + zOff);
            Vector3 end = new Vector3(origin.x + lockedDir.x * visibleReach,
                                      origin.y + lockedDir.y * visibleReach,
                                      origin.z + zOff);
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);
        }

        void Update()
        {
            if (SlimeJumping.Effects.WorldTimeFreezeService.IsFrozen) return;
            if (!initialized) return;
            age += Time.deltaTime;

            if (owner == null || muzzle == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 origin = muzzle.position;
            float maxRange = owner.MaxRange;

            // Rotation target by mode: InitialLagThenLive → live slime pos (Init already
            // lagged the start dir); ContinuousLag → the lagged trail pos every frame.
            if (trackAngularSpeed > 0f && owner.Target != null)
            {
                Vector2 targetPos = owner.BeamTrackingMode == GraniteBlastEmitter.BeamTrackMode.ContinuousLag
                    ? owner.GetLaggedTargetPosition(trackLag)
                    : new Vector2(owner.Target.position.x, owner.Target.position.y);
                Vector2 toSlime = targetPos - (Vector2)origin;
                if (toSlime.sqrMagnitude > 0.0001f)
                {
                    Vector2 desired = toSlime.normalized;
                    float maxStep = trackAngularSpeed * Time.deltaTime * Mathf.Deg2Rad;
                    Vector3 rotated = Vector3.RotateTowards(
                        new Vector3(lockedDir.x, lockedDir.y, 0f),
                        new Vector3(desired.x, desired.y, 0f),
                        maxStep, 0f);
                    lockedDir = new Vector2(rotated.x, rotated.y).normalized;
                }
            }

            Vector2 dir = lockedDir;

            // Tracking forces sustained behavior — without it, the beam would halt
            // on the first hit and the rotated direction would be invisible.
            bool useSustained = owner.SustainedHit || trackAngularSpeed > 0f;

            if (useSustained)
            {
                UpdateSustainedSweep(origin, dir, maxRange);
            }
            else if (!tipHalted)
            {
                if (travelSpeed > 0f)
                {
                    currentReach = Mathf.Min(currentReach + travelSpeed * Time.deltaTime, maxRange);
                }
                else
                {
                    currentReach = maxRange;
                }

                // Projectile semantics: only what sits in this frame's [prevTip, currTip]
                // window is hit — a slime that already moved away is missed.
                if (currentReach > prevReach && owner.BulletHitMask != 0)
                {
                    Vector2 castStart = new Vector2(origin.x + dir.x * prevReach,
                                                     origin.y + dir.y * prevReach);
                    float castDist = currentReach - prevReach;
                    float radius = Mathf.Max(0.01f, owner.HitRadius);
                    var hit = owner.CircleCastExcludingSelf(castStart, radius, dir, castDist, owner.BulletHitMask);
                    if (hit.collider != null)
                    {
                        var slime = hit.collider.GetComponentInParent<SlimeController>();
                        bool isSlime = slime != null;
                        if (isSlime && !hitResolved)
                        {
                            hitResolved = true;
                            Vector2 away = (Vector2)slime.transform.position - (Vector2)origin;
                            Vector2 pushDir = away.sqrMagnitude > 0.0001f ? away.normalized : dir;
                            if (!owner.TryApplyStunOnHit(slime, pushDir, owner.KnockbackImpulse, hit.point))
                            {
                                // Wipe prior slide momentum so the kick replaces velocity;
                                // the additive impulse then rides on top.
                                if (owner.BeamOverwriteSlide)
                                {
                                    float pinned = owner.BeamOverwritePinnedSpeed * owner.BeamGroundedSlideScale;
                                    slime.ApplyHazardPushOverwriteSlide(pushDir * pinned, hit.point);
                                }
                                slime.ApplyHazardPush(ScaleGroundedSlide(slime, pushDir * owner.KnockbackImpulse), hit.point);
                            }
                            // A beam halting on the slime body still bites snow; the
                            // pass-through carve below only fires when it reaches ground.
                            owner.CarveSnowNearSlime(slime, hit.point);
                            owner.TryFireHitStopFeedback();
                            owner.SpawnGroundExplosion(hit.point, hit.normal);
                        }

                        SpawnImpactAt(hit.point, hit.normal);

                        if (isSlime)
                        {
                            // Continue past the slime so a slime block doesn't suppress the
                            // ground carve / explosion behind it.
                            float passOffset = 0.05f;
                            Vector2 passStart = (Vector2)origin + dir * (prevReach + hit.distance + passOffset);
                            float passDist = currentReach - (prevReach + hit.distance + passOffset);
                            if (passDist > 0.01f)
                            {
                                var groundHit = owner.CircleCastIgnoringSlime(passStart, radius, dir, passDist, owner.BulletHitMask);
                                if (groundHit.collider != null)
                                {
                                    tipHalted = true;
                                    currentReach = prevReach + hit.distance + passOffset + groundHit.distance;
                                    owner.SpawnGroundExplosion(groundHit.point, groundHit.normal);
                                    owner.CarveSnowAtImpact(groundHit.collider, groundHit.point);
                                    if (!groundImpactPlayed)
                                    {
                                        groundImpactPlayed = true;
                                        owner.PlayGroundImpactSound(groundHit.point);
                                    }
                                }
                                // else: no ground in the window — currentReach is already at
                                // its far edge, so the tip just extends.
                            }
                            else
                            {
                                // Slime sits at the tip of this frame's window — halt now,
                                // next frame's advance sweeps past it.
                                tipHalted = true;
                                currentReach = prevReach + hit.distance;
                            }
                        }
                        else
                        {
                            tipHalted = true;
                            currentReach = prevReach + hit.distance;
                            owner.SpawnGroundExplosion(hit.point, hit.normal);
                            owner.CarveSnowAtImpact(hit.collider, hit.point);
                            if (!groundImpactPlayed)
                            {
                                groundImpactPlayed = true;
                                owner.PlayGroundImpactSound(hit.point);
                            }
                        }
                    }
                }

                if (!tipHalted && currentReach >= maxRange) tipHalted = true;

                prevReach = currentReach;
            }

            DrawCurrent();

            // Pulse + fade — anchored on absolute beam lifetime (`duration`) so an
            // early-halted beam still respects the configured visible time.
            float pulse = 1f + 0.18f * (Mathf.PerlinNoise(age * 22f, 0f) - 0.5f);
            lr.widthMultiplier = baseWidth * pulse;

            float fadeStart = duration - fadeTime;
            if (age >= fadeStart)
            {
                float k = Mathf.Clamp01(1f - (age - fadeStart) / fadeTime);
                Color c = owner.CoreColor;
                c.a *= k;
                lr.startColor = c;
                lr.endColor = c;
                lr.widthMultiplier = baseWidth * pulse * Mathf.Lerp(0.4f, 1f, k);
            }

            if (age >= duration) Destroy(gameObject);
        }

        // Sustained: re-sweep muzzle→maxRange every frame and rewrite reach from scratch,
        // so a slime that steps out lets the beam through. tipHalted unused in this branch.
        private void UpdateSustainedSweep(Vector3 origin, Vector2 dir, float maxRange)
        {
            float dt = Time.deltaTime;
            // Still grow at travelSpeed on the first frames so the beam visibly leaves the
            // muzzle; afterwards it saturates at maxRange and the live hit crops it.
            if (travelSpeed > 0f && currentReach < maxRange)
                currentReach = Mathf.Min(currentReach + travelSpeed * dt, maxRange);
            else
                currentReach = maxRange;

            float reachThisFrame = currentReach;
            if (owner.BulletHitMask != 0)
            {
                float radius = Mathf.Max(0.01f, owner.HitRadius);
                var hit = owner.CircleCastExcludingSelf((Vector2)origin, radius, dir, currentReach, owner.BulletHitMask);
                if (hit.collider != null)
                {
                    reachThisFrame = hit.distance;
                    var slime = hit.collider.GetComponentInParent<SlimeController>();
                    bool isSlime = slime != null;
                    if (isSlime)
                    {
                        Vector2 away = (Vector2)slime.transform.position - (Vector2)origin;
                        Vector2 pushDir = away.sqrMagnitude > 0.0001f ? away.normalized : dir;
                        bool firstSlimeContact = !hitResolved;
                        if (firstSlimeContact && owner.TryApplyStunOnHit(slime, pushDir, owner.KnockbackImpulse, hit.point))
                        {
                            // Stun took the impulse; ApplyHazardEffect's Stunned guard
                            // absorbs same-frame re-entries.
                            hitResolved = true;
                        }
                        else if (owner.BeamOverwriteSlide)
                        {
                            // Every frame is a fresh "first contact" so a slime hopping out keeps feeling
                            // the kick. Grounded can't compound — externalDeltaV is consumed-then-zeroed;
                            // airborne compounds by design.
                            float pinned = owner.BeamOverwritePinnedSpeed * owner.BeamGroundedSlideScale;
                            slime.ApplyHazardPushOverwriteSlide(pushDir * pinned, hit.point);
                            slime.ApplyHazardPush(ScaleGroundedSlide(slime, pushDir * owner.KnockbackImpulse), hit.point);
                            hitResolved = true;
                        }
                        else
                        {
                            // Additive hybrid: one-shot impulse ("퍽") + per-frame accel ("지지직").
                            if (!hitResolved)
                            {
                                hitResolved = true;
                                slime.ApplyHazardPush(ScaleGroundedSlide(slime, pushDir * owner.KnockbackImpulse), hit.point);
                            }
                            slime.ApplyHazardPush(ScaleGroundedSlide(slime, pushDir * owner.KnockbackAcceleration * dt), hit.point);
                        }
                        // Covers sideways/upward beams where the pass-through sweep below
                        // never reaches snow. Once per beam.
                        if (firstSlimeContact)
                        {
                            owner.CarveSnowNearSlime(slime, hit.point);
                            owner.TryFireHitStopFeedback();
                        }
                        // Explosion VFX every frame; sound + carve stay once per beam.
                        owner.SpawnGroundExplosion(hit.point, hit.normal);
                    }
                    else
                    {
                        // Explosion ungated so a pinned beam keeps puffing; sound + carve once.
                        owner.SpawnGroundExplosion(hit.point, hit.normal);
                        if (!groundImpactPlayed)
                        {
                            groundImpactPlayed = true;
                            owner.PlayGroundImpactSound(hit.point);
                            owner.CarveSnowAtImpact(hit.collider, hit.point);
                            SpawnImpactAt(hit.point, hit.normal);
                        }
                    }

                    // Primary hit was the slime: sweep on to the ground behind it so the
                    // SnowField carve and ground impact still fire.
                    if (isSlime)
                    {
                        float passOffset = 0.05f;
                        Vector2 passStart = (Vector2)origin + dir * (hit.distance + passOffset);
                        float passDist = currentReach - (hit.distance + passOffset);
                        if (passDist > 0.01f)
                        {
                            var groundHit = owner.CircleCastIgnoringSlime(passStart, radius, dir, passDist, owner.BulletHitMask);
                            if (groundHit.collider != null)
                            {
                                reachThisFrame = hit.distance + passOffset + groundHit.distance;
                                // Explosion every frame; sound + carve once per beam.
                                owner.SpawnGroundExplosion(groundHit.point, groundHit.normal);
                                if (!groundImpactPlayed)
                                {
                                    groundImpactPlayed = true;
                                    owner.PlayGroundImpactSound(groundHit.point);
                                    owner.CarveSnowAtImpact(groundHit.collider, groundHit.point);
                                    SpawnImpactAt(groundHit.point, groundHit.normal);
                                }
                            }
                            else
                            {
                                // No ground behind the slime — beam reaches full length.
                                reachThisFrame = currentReach;
                            }
                        }
                    }
                }
            }
            currentReach = reachThisFrame;
            prevReach = currentReach;
        }

        // SlimeController projects externalDeltaV onto surfaceTangent while grounded, so
        // pre-subtracting (1-scale) of the tangent lands the gain at exactly tangent*scale.
        // Airborne/Stunned skip that projection, so the full deltaV passes through.
        private Vector2 ScaleGroundedSlide(SlimeController slime, Vector2 deltaV)
        {
            float scale = owner.BeamGroundedSlideScale;
            if (Mathf.Approximately(scale, 1f)) return deltaV;
            var st = slime.CurrentState;
            if (st != SlimeController.SlimeState.Attached
                && st != SlimeController.SlimeState.Charging) return deltaV;
            Vector2 t = slime.SurfaceTangent;
            if (t.sqrMagnitude < 0.0001f) return deltaV;
            float along = Vector2.Dot(deltaV, t);
            return deltaV - t * (along * (1f - scale));
        }

        private ParticleSystem SpawnImpactAt(Vector3 point, Vector3 normal)
        {
            if (owner == null || owner.ImpactPrefab == null) return null;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            Vector3 spawnPos = new Vector3(point.x, point.y, point.z + owner.LineZOffset);
            var inst = Instantiate(owner.ImpactPrefab, spawnPos, rot);
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.2f;
            Destroy(inst.gameObject, life);
            return inst;
        }
    }
}
