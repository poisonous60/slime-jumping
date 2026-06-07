using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Rendering;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// The small severed piece of a sliced slime. Self-contained:
    ///   1. flies off + falls under the SAME gravity vector as the slime (applied
    ///      manually, gravityScale 0), wobbling like viscous goo;
    ///   2. on landing, MELTS — flattens + spreads with its bottom PINNED to the ground
    ///      surface found by a downward cast (not a uniform shrink, not the snowball-vanish
    ///      FX); the pin holds the puddle on the floor for the whole melt so it never melts
    ///      in mid-air or sinks through the ground, and stamps a WorldTrail stain where it
    ///      melted, so it leaves a mark;
    ///   3. destroys itself.
    /// Shader velocity-stretch is pushed via a per-renderer MaterialPropertyBlock
    /// (never mutates the shared material → no regression on the live slime).
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SlimeChunk : MonoBehaviour
    {
        private Rigidbody2D rb;
        private Transform meshTf;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock mpb;
        private SlimeSplitSettings s;

        private Vector3 baseMeshScale;
        private Vector3 baseMeshLocalPos;
        private float meshLocalExtentY;    // mesh half-height in its local space (for planted sink)
        private Vector2 gravity;           // slime's gravity vector (m/s²)

        private float jiggleEnv;
        private float jigglePhase;
        private float airTime;

        private bool melting;
        private float meltTimer;
        private float stampTimer;
        private Collider2D meltSurface;
        private Vector2 meltPoint;

        // Ground anchor resolved at melt-start: the puddle's bottom-center is pinned here
        // (world space) every melt frame so it melts ON the ground, never floating / sinking.
        private bool groundAnchored;
        private float groundY;     // world Y of the surface the puddle melts onto
        private float anchorX;     // world X of the landing spot (keeps the puddle from sliding)
        private static readonly RaycastHit2D[] groundHits = new RaycastHit2D[4];

        private static readonly int VelocityDirWSID = Shader.PropertyToID("_VelocityDirWS");
        private static readonly int VelocityStretchAmountID = Shader.PropertyToID("_VelocityStretchAmount");
        private static readonly int SurfaceNormalWSID = Shader.PropertyToID("_SurfaceNormalWS");
        private static readonly int DipDirWSID = Shader.PropertyToID("_DipDirWS");

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody2D>();
        }

        public void Init(SlimeSplitSettings settings, Vector2 inheritVel, Vector2 severDir, Vector2 gravityVec)
        {
            s = settings;
            gravity = gravityVec;
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;                 // gravity applied manually = same as the slime
            rb.linearDamping = s.linearDamping;
            rb.angularDamping = s.angularDamping;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // don't tunnel through the floor

            meshRenderer = GetComponentInChildren<MeshRenderer>();
            meshTf = meshRenderer != null ? meshRenderer.transform : transform;
            baseMeshScale = meshTf.localScale;
            baseMeshLocalPos = meshTf.localPosition;
            var mf = GetComponentInChildren<MeshFilter>();
            meshLocalExtentY = (mf != null && mf.sharedMesh != null)
                ? mf.sharedMesh.bounds.extents.y
                : 0.5f;
            mpb = new MaterialPropertyBlock();

            float imp = Random.Range(s.severImpulseMin, s.severImpulseMax);
            Vector2 dir = severDir.sqrMagnitude > 1e-4f ? severDir.normalized : Vector2.right;
            rb.linearVelocity = inheritVel * s.inheritVelocityFactor
                              + dir * imp
                              + Vector2.up * s.upwardImpulse;

            if (Mathf.Approximately(s.spinMinDeg, 0f) && Mathf.Approximately(s.spinMaxDeg, 0f))
                rb.freezeRotation = true;
            else
                rb.angularVelocity = Random.Range(s.spinMinDeg, s.spinMaxDeg);

            jiggleEnv = 1f;
            jigglePhase = Random.value * Mathf.PI * 2f;
        }

        private void FixedUpdate()
        {
            if (s == null || melting) return;
            rb.linearVelocity += gravity * Time.fixedDeltaTime;   // slime's own gravity
            airTime += Time.fixedDeltaTime;
            if (airTime >= s.severedMaxAirTime)
                StartMelt(null, rb.position, null);   // pit safety — melt where it is
        }

        private void Update()
        {
            if (s == null) return;
            float dt = Time.deltaTime;

            if (!melting)
            {
                jiggleEnv *= Mathf.Exp(-s.jiggleDecay * dt);
                jigglePhase += dt * s.jiggleFrequency * 2f * Mathf.PI;
                float j = Mathf.Sin(jigglePhase) * s.jiggleAmplitude * jiggleEnv;

                Vector3 sc = baseMeshScale;
                sc.x = baseMeshScale.x * (1f + j);
                sc.y = baseMeshScale.y * (1f - j);
                meshTf.localScale = sc;

                PushVelocityStretch();
            }
            else
            {
                meltTimer += dt;
                float t = Mathf.Clamp01(meltTimer / Mathf.Max(0.001f, s.meltDuration));

                // Flatten + spread: a melting puddle, NOT a uniform shrink.
                Vector3 sc = baseMeshScale;
                sc.y = baseMeshScale.y * (1f - t);
                sc.x = baseMeshScale.x * (1f + s.meltSpread * t);
                meshTf.localScale = sc;

                if (groundAnchored && meshRenderer != null)
                {
                    // Pin the VISIBLE puddle's bottom-center to the landing spot on the ground,
                    // measured from the live world AABB. The chunk mesh's pivot is the slime
                    // origin (DMC), far from the piece centroid — so scaling slides + lifts the
                    // visible piece. Re-anchoring from renderer.bounds each frame makes the
                    // bottom sit EXACTLY on the ground (no float, no sink) and stops the slide.
                    Bounds wb = meshRenderer.bounds;
                    Vector3 wp = meshTf.position;
                    wp.x += anchorX - wb.center.x;
                    wp.y += groundY - wb.min.y;
                    meshTf.position = wp;
                }
                else
                {
                    // No ground beneath it (fell into a pit): melt in place via the local sink.
                    float sink = meshLocalExtentY * baseMeshScale.y * t * s.meltSinkFactor;
                    meshTf.localPosition = baseMeshLocalPos + Vector3.down * sink;
                }

                // Widen the stain with the spreading puddle.
                stampTimer += dt;
                if (s.meltStampInterval > 0f && stampTimer >= s.meltStampInterval && s.meltStampRadius > 0f)
                {
                    stampTimer = 0f;
                    Stamp(meltPoint, s.meltStampRadius * (1f + s.meltSpread * t), s.meltStampStrength);
                }

                if (t >= 1f) { Destroy(gameObject); return; }
            }
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (s == null) return;
            jiggleEnv = Mathf.Min(1.5f, jiggleEnv + s.landingJiggleBoost);
            // Don't melt on the spawn-overlap contact — only after the piece has flown for
            // the grace period and genuinely lands.
            if (!melting && airTime >= s.meltContactGrace)
            {
                Vector2 point = c.contactCount > 0 ? c.GetContact(0).point : rb.position;
                StartMelt(c, point, c.collider);
            }
        }

        private void StartMelt(Collision2D contact, Vector2 fallbackPoint, Collider2D fallbackSurface)
        {
            melting = true;
            meltTimer = 0f;
            stampTimer = 0f;
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;

            // Reset any leftover wobble so the bottom-center we measure is the piece at rest.
            meshTf.localScale = baseMeshScale;
            meshTf.localPosition = baseMeshLocalPos;

            groundAnchored = TryResolveGround(contact, out groundY, out var surf);
            if (groundAnchored)
            {
                meltSurface = surf;
                anchorX = meshRenderer != null ? meshRenderer.bounds.center.x : rb.position.x;
                meltPoint = new Vector2(anchorX, groundY);   // stain lands ON the ground
            }
            else
            {
                meltSurface = fallbackSurface;
                meltPoint = fallbackPoint;                   // pit/air: stain where it floats
            }

            if (s.meltStampRadius > 0f)
                Stamp(meltPoint, s.meltStampRadius, s.meltStampStrength);   // initial stain
        }

        // Finds the surface the puddle should melt onto: cast along the slime's gravity from
        // the piece's center to the first non-trigger collider (so the spinning saw, a trigger,
        // is ignored). Falls back to a real landing contact, else reports "no ground" (a pit).
        private bool TryResolveGround(Collision2D contact, out float gY, out Collider2D surf)
        {
            gY = 0f;
            surf = null;

            Vector2 down = gravity.sqrMagnitude > 1e-4f ? gravity.normalized : Vector2.down;
            Bounds b = meshRenderer != null
                ? meshRenderer.bounds
                : new Bounds((Vector3)rb.position, Vector3.one * 0.1f);
            Vector2 origin = b.center;
            float reach = b.extents.magnitude + Mathf.Max(0f, s.meltGroundProbe);

            // Unset mask (0) → everything except the chunk's own (slime) layer, so the ray
            // never self-hits and never anchors to the slime.
            int mask = s.groundMask.value != 0
                ? s.groundMask.value
                : (Physics2D.AllLayers & ~(1 << gameObject.layer));

            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(mask);

            int n = Physics2D.Raycast(origin, down, filter, groundHits, reach);
            float best = float.PositiveInfinity;
            int bestI = -1;
            for (int i = 0; i < n; i++)
            {
                if (groundHits[i].collider == null) continue;
                if (groundHits[i].distance < best) { best = groundHits[i].distance; bestI = i; }
            }
            if (bestI >= 0)
            {
                gY = groundHits[bestI].point.y;
                surf = groundHits[bestI].collider;
                return true;
            }

            // Ray missed (e.g. started inside a collider it had penetrated) but we DID land —
            // anchor to the contact surface so a real landing never melts in mid-air.
            if (contact != null && contact.contactCount > 0)
            {
                gY = contact.GetContact(0).point.y;
                surf = contact.collider;
                return true;
            }
            return false;
        }

        // Routes a trail stamp to the surface's local trail RT, else the world RT —
        // same routing TrailStamper uses for the slime's landing marks.
        private void Stamp(Vector2 worldPos, float radius, float strength)
        {
            if (meltSurface != null && TrailColliderRegistry.TryGet(meltSurface, out var local) && local != null)
            {
                local.StampAt(worldPos, radius, strength);
                return;
            }
            var mgr = WorldTrailManager.Instance;
            if (mgr != null) mgr.Stamp(worldPos, radius, strength);
        }

        private void PushVelocityStretch()
        {
            if (meshRenderer == null) return;
            Vector2 v = rb.linearVelocity;
            float speed = v.magnitude;
            float speedNorm = Mathf.Clamp01(speed / Mathf.Max(0.01f, s.wobbleSpeedRef));
            Vector2 dir = speed > 0.01f ? v / speed : Vector2.zero;

            meshRenderer.GetPropertyBlock(mpb);
            mpb.SetVector(VelocityDirWSID, new Vector4(dir.x, dir.y, 0f, speedNorm));
            mpb.SetFloat(VelocityStretchAmountID, speedNorm * s.velocityStretchScale);
            mpb.SetVector(SurfaceNormalWSID, Vector3.up);
            mpb.SetVector(DipDirWSID, Vector3.up);
            meshRenderer.SetPropertyBlock(mpb);
        }
    }
}
