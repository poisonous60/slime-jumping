using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Beam line for <see cref="GraniteBlastEmitter"/>'s Beam mode and Burst-mode flashes.
    /// Projectile-style: the visible reach grows from muzzle along the locked direction
    /// at <c>travelSpeed</c> world units per second, doing a per-frame CircleCast sweep
    /// from the previous tip to the current tip. On the first slime/terrain encountered
    /// at that moment the beam halts at the hit point and stays there for the remainder
    /// of <c>duration</c>; if nothing intercepts, the tip reaches maxRange and the beam
    /// stays full-length until duration. travelSpeed = 0 falls back to instant
    /// full-length (hitscan-style).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class GraniteBlastBeam : MonoBehaviour
    {
        // Min reach for the visible line. Without this, a zero-distance hit
        // (muzzle inside a hit-mask collider) would collapse both LR endpoints to the
        // same point and leave only the cap circle visible.
        private const float MinVisibleReach = 0.5f;

        private GraniteBlastEmitter owner;
        private Transform muzzle;
        private Vector2 lockedDir;
        private LineRenderer lr;
        private float baseWidth;

        private float duration;
        private float fadeTime;
        private float travelSpeed;
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
            // Pre-create + pre-configure the LineRenderer so it can render on the very
            // first frame without depending on Init's call timing.
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
                         float duration, float fadeTime, float coreWidth, float travelSpeed)
        {
            this.owner = emitter;
            this.muzzle = muzzle;
            Vector2 d = fireDir;
            if (d.sqrMagnitude < 0.0001f) d = Vector2.right;
            else d.Normalize();
            this.lockedDir = d;
            this.duration = Mathf.Max(0.05f, duration);
            this.fadeTime = Mathf.Max(0.001f, fadeTime);
            this.baseWidth = Mathf.Max(0.001f, coreWidth);
            this.travelSpeed = Mathf.Max(0f, travelSpeed);

            // travelSpeed <= 0 → instant full-length (hitscan fallback). The first
            // Update tick will still resolve the halt point via a single sweep across
            // [0, maxRange] because prevReach=0 < currentReach=maxRange.
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
            if (!initialized) return;
            age += Time.deltaTime;

            if (owner == null || muzzle == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 origin = muzzle.position;
            Vector2 dir = lockedDir;
            float maxRange = owner.MaxRange;

            // Advance the tip toward maxRange at travelSpeed (or jump straight to
            // maxRange when speed=0). Once halted on a collision, the reach is frozen.
            if (!tipHalted)
            {
                if (travelSpeed > 0f)
                {
                    currentReach = Mathf.Min(currentReach + travelSpeed * Time.deltaTime, maxRange);
                }
                else
                {
                    currentReach = maxRange;
                }

                // Per-frame collision sweep from prevTip to currTip. On hit, halt at
                // the hit point — slime that's no longer there at this moment doesn't
                // get hit (projectile semantics).
                if (currentReach > prevReach && owner.BulletHitMask != 0)
                {
                    Vector2 castStart = new Vector2(origin.x + dir.x * prevReach,
                                                     origin.y + dir.y * prevReach);
                    float castDist = currentReach - prevReach;
                    float radius = Mathf.Max(0.01f, owner.HitRadius);
                    var hit = owner.CircleCastExcludingSelf(castStart, radius, dir, castDist, owner.BulletHitMask);
                    if (hit.collider != null)
                    {
                        tipHalted = true;
                        currentReach = prevReach + hit.distance;

                        var slime = hit.collider.GetComponentInParent<SlimeController>();
                        if (slime != null && !hitResolved)
                        {
                            hitResolved = true;
                            var effect = owner.GetHazardEffect();
                            Vector2 away = (Vector2)slime.transform.position - (Vector2)origin;
                            effect.knockbackDirection = away.sqrMagnitude > 0.0001f ? away.normalized : dir;
                            slime.ApplyHazardEffect(effect);
                        }

                        SpawnImpactAt(hit.point, hit.normal);
                        if (!groundImpactPlayed)
                        {
                            groundImpactPlayed = true;
                            owner.PlayGroundImpactSound(hit.point);
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
