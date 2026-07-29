using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Mortar projectile from <see cref="GraniteBlastEmitter"/>. Travel → Dwell at the orbit
    /// anchor → Strike at the slime (homingStrength 0 = heading locked at strike start,
    /// 1 = full chase). Kinematic and gravity-free, moved by rb.position; hits resolve via a
    /// swept <c>Collider2D.Cast</c> so a fast projectile cannot tunnel through geometry.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public class GraniteBlastMortar : MonoBehaviour
    {
        private enum Phase { Travel, Dwell, Strike }

        private GraniteBlastEmitter owner;
        private GraniteBlastMortarConfig cfg;

        private Rigidbody2D rb;
        private CircleCollider2D bodyCol;
        private Transform visual;
        private SpriteRenderer headRenderer;
        private TrailRenderer trail;

        private Phase phase;
        private float phaseAge;

        // Travel phase scalars.
        private Vector2 spawnPos;
        private Vector2 orbitAnchor;
        private Vector2 travelDir;     // unit vector spawn → orbitAnchor

        // Strike phase scalars.
        private Vector2 strikeDir;     // current heading; updated each frame by homing
        private float strikeAge;       // strike phase elapsed sec

        private static readonly RaycastHit2D[] sweepBuf = new RaycastHit2D[8];

        public void Init(GraniteBlastEmitter emitter, GraniteBlastMortarConfig config, Vector2 ejectDir)
        {
            owner = emitter;
            cfg = config;

            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.useFullKinematicContacts = true;

            bodyCol = GetComponent<CircleCollider2D>();
            bodyCol.isTrigger = true;
            bodyCol.radius = Mathf.Max(0.05f, cfg.bodyRadius);
            // Disabled through Travel + Dwell; EnterStrike re-enables.
            bodyCol.enabled = false;

            // Visual on a child transform, collider on root — no rotation propagation.
            BuildVisual();

            spawnPos = rb.position;
            travelDir = ejectDir.sqrMagnitude > 0.0001f ? ejectDir.normalized : Vector2.up;
            orbitAnchor = spawnPos + travelDir * Mathf.Max(0.01f, cfg.orbitDistance);
            phase = Phase.Travel;
            phaseAge = 0f;
        }

        private void BuildVisual()
        {
            var visGo = new GameObject("Visual");
            visual = visGo.transform;
            visual.SetParent(transform, false);
            visual.localPosition = Vector3.zero;

            headRenderer = visGo.AddComponent<SpriteRenderer>();
            headRenderer.sprite = cfg.headSprite != null ? cfg.headSprite : MakeDiscSprite();
            headRenderer.color = owner != null ? owner.CoreColor : Color.white;
            headRenderer.sortingOrder = 100;
            // Generated disc has a 0.5 world-unit sprite radius, so ×2 matches bodyCol.radius.
            float visScale = bodyCol.radius * 2f;
            visual.localScale = new Vector3(visScale, visScale, 1f);

            if (cfg.useTrail)
            {
                trail = visGo.AddComponent<TrailRenderer>();
                trail.time = Mathf.Max(0.01f, cfg.trailTime);
                trail.startWidth = bodyCol.radius * cfg.trailStartWidth;
                trail.endWidth = bodyCol.radius * cfg.trailEndWidth;
                Material mat = owner != null ? owner.GetCoreMaterial() : null;
                if (mat != null) trail.material = mat;
                Color c = owner != null ? owner.CoreColor : Color.white;
                trail.startColor = c;
                Color end = c; end.a = 0f;
                trail.endColor = end;
                trail.minVertexDistance = 0.02f;
                trail.numCapVertices = 2;
                trail.sortingOrder = 99;
            }
        }

        private static Sprite cachedDisc;
        private static Sprite MakeDiscSprite()
        {
            if (cachedDisc != null) return cachedDisc;
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = (size - 2) * 0.5f;
            Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), center);
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(r - 1.5f, r, d));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            cachedDisc = Sprite.Create(tex, new Rect(0, 0, size, size),
                new Vector2(0.5f, 0.5f), size); // pixelsPerUnit=size → sprite radius = 0.5 world units
            return cachedDisc;
        }

        void FixedUpdate()
        {
            if (SlimeJumping.Effects.WorldTimeFreezeService.IsFrozen) return;
            if (owner == null || cfg == null)
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.fixedDeltaTime;
            phaseAge += dt;

            switch (phase)
            {
                case Phase.Travel:    StepTravel(dt); break;
                case Phase.Dwell:     StepDwell(dt); break;
                case Phase.Strike:    StepStrike(dt); break;
            }
        }

        // Travel + Dwell are deliberately collision-IMMUNE: otherwise the first FixedUpdate's
        // sweep hits muzzle-adjacent geometry and detonates before the projectile is visible.
        private void StepTravel(float dt)
        {
            float total = Mathf.Max(0.001f, cfg.travelToOrbitDuration);
            float dist = Mathf.Max(0.01f, cfg.orbitDistance);
            float baselineSpeed = dist / total;
            float t = Mathf.Clamp01(phaseAge / total);
            float speed = baselineSpeed * Mathf.Max(0f, EvalCurve(cfg.travelToOrbitSpeedCurve, t));
            Vector2 next = rb.position + travelDir * speed * dt;

            // Snap to anchor when we arrive (covers float drift).
            if (Vector2.Distance(next, orbitAnchor) <= 0.01f || phaseAge >= total)
            {
                rb.position = orbitAnchor;
                EnterDwell();
                return;
            }
            rb.position = next;
        }

        private void EnterDwell()
        {
            phase = Phase.Dwell;
            phaseAge = 0f;
        }

        private void StepDwell(float dt)
        {
            if (phaseAge >= Mathf.Max(0f, cfg.dwellAtOrbitDuration))
            {
                EnterStrike();
            }
        }

        private void EnterStrike()
        {
            phase = Phase.Strike;
            phaseAge = 0f;
            strikeAge = 0f;
            if (bodyCol != null) bodyCol.enabled = true;
            // Lock initial direction at the slime's current position.
            Vector2 toSlime = ResolveSlimePos() - rb.position;
            strikeDir = toSlime.sqrMagnitude > 0.0001f ? toSlime.normalized : -travelDir;
        }

        private void StepStrike(float dt)
        {
            strikeAge += dt;

            float homing = Mathf.Clamp01(cfg.homingStrength);
            if (homing > 0f && owner.Target != null)
            {
                Vector2 toSlime = ResolveSlimePos() - rb.position;
                if (toSlime.sqrMagnitude > 0.0001f)
                {
                    Vector2 desired = toSlime.normalized;
                    // pow-based factor keeps the turn rate FPS-stable.
                    float k = 1f - Mathf.Pow(1f - homing, dt * 60f);
                    Vector2 blended = Vector2.Lerp(strikeDir, desired, k);
                    if (blended.sqrMagnitude > 0.0001f) strikeDir = blended.normalized;
                }
            }

            float total = Mathf.Max(0.001f, cfg.strikeMaxDuration);
            float t = Mathf.Clamp01(strikeAge / total);
            float speed = Mathf.Max(0f, cfg.strikeSpeed) * Mathf.Max(0f, EvalCurve(cfg.strikeSpeedCurve, t));
            Vector2 next = rb.position + strikeDir * speed * dt;

            if (TryResolveHit(next)) return;

            if (strikeAge >= total)
            {
                Fizzle();
                return;
            }

            rb.position = next;
        }

        // Returns true if the projectile was consumed by a hit.
        private bool TryResolveHit(Vector2 next)
        {
            Vector2 from = rb.position;
            Vector2 delta = next - from;
            float dist = delta.magnitude;
            if (dist <= 0.0001f) return false;
            Vector2 dir = delta / dist;

            if (owner.BulletHitMask == 0) return false;

            // useTriggers = true because the slime body collider is itself a trigger; foreign
            // triggers (water zones, sensors) are filtered out below.
            var filter = new ContactFilter2D { useTriggers = true };
            filter.SetLayerMask(owner.BulletHitMask);
            filter.useLayerMask = true;

            int n = bodyCol.Cast(dir, filter, sweepBuf, dist);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                var h = sweepBuf[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(owner.transform)) continue;
                if (h.collider.isTrigger
                    && h.collider.GetComponentInParent<SlimeController>() == null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }

            if (best.collider == null) return false;

            // Move to the contact point so the impact VFX spawns at the hit, not mid-air.
            rb.position = from + dir * Mathf.Max(0f, best.distance);

            var slime = best.collider.GetComponentInParent<SlimeController>();
            if (slime != null) HandleSlimeHit(slime, best, dir);
            else HandleGroundHit(best);
            return true;
        }

        private void HandleSlimeHit(SlimeController slime, RaycastHit2D hit, Vector2 dir)
        {
            Vector2 slimePos = slime.transform.position;
            Vector2 away = slimePos - (Vector2)owner.AimOrigin();
            Vector2 pushDir = away.sqrMagnitude > 0.0001f ? away.normalized : dir;
            if (!owner.TryApplyStunOnHit(slime, pushDir, owner.KnockbackImpulse, hit.point))
            {
                slime.ApplyHazardPush(pushDir * owner.KnockbackImpulse, hit.point);
            }
            owner.CarveSnowNearSlime(slime, hit.point);
            owner.TryFireHitStopFeedback();
            owner.SpawnGroundExplosion(hit.point, hit.normal);
            SpawnImpactSpark(hit.point, hit.normal);
            DetachTrailAndDestroy();
        }

        private void HandleGroundHit(RaycastHit2D hit)
        {
            owner.SpawnGroundExplosion(hit.point, hit.normal);
            owner.CarveSnowAtImpact(hit.collider, hit.point);
            owner.PlayGroundImpactSound(hit.point);
            SpawnImpactSpark(hit.point, hit.normal);
            DetachTrailAndDestroy();
        }

        private void Fizzle()
        {
            // Lifetime-expired projectile: skip explosion / sound, just clean up.
            DetachTrailAndDestroy();
        }

        private void SpawnImpactSpark(Vector3 pos, Vector3 normal)
        {
            if (owner.ImpactPrefab == null) return;
            Vector3 n = normal.sqrMagnitude > 0.0001f ? normal : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward, n);
            var inst = Instantiate(owner.ImpactPrefab, new Vector3(pos.x, pos.y, pos.z + owner.LineZOffset), rot);
            if (!inst.gameObject.activeSelf) inst.gameObject.SetActive(true);
            inst.Play(true);
            var main = inst.main;
            float life = main.duration + main.startLifetime.constantMax + 0.3f;
            Destroy(inst.gameObject, life);
        }

        private void DetachTrailAndDestroy()
        {
            // Detach so the trail fades out instead of vanishing with the body.
            if (trail != null)
            {
                trail.transform.SetParent(null, true);
                trail.autodestruct = true;
                trail.emitting = false;
            }
            Destroy(gameObject);
        }

        private Vector2 ResolveSlimePos()
        {
            if (owner != null && owner.Target != null)
                return new Vector2(owner.Target.position.x, owner.Target.position.y);
            return rb.position;
        }

        // Unassigned curve evaluates to 1 so the projectile still moves at baseline speed.
        private static float EvalCurve(AnimationCurve c, float t)
        {
            if (c == null || c.length == 0) return 1f;
            return c.Evaluate(t);
        }
    }
}
