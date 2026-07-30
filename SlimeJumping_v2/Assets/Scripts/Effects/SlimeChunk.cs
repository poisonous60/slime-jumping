using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Effects
{
    /// <summary>
    /// Severed slime piece, a real pickup: Flying → Resting → Attracting → absorbed
    /// (SlimeSplitSequence.NotifyChunkAbsorbed). Continuous fall > severedMaxAirTime = pit →
    /// NotifyChunkLost converts the volume to ambient regen, never a soft-lock.
    /// The collider excludes the slime's layer (spawner-set) so it never blocks the player;
    /// shader pushes go through a per-renderer MPB, never the shared material.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class SlimeChunk : MonoBehaviour
    {
        private enum ChunkState { Flying, Resting, Attracting }

        private Rigidbody2D rb;
        private CircleCollider2D circle;
        private Transform meshTf;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock mpb;
        private SlimeSplitSettings s;
        private SlimeSplitSequence sequence;
        private SlimeController slime;

        private Vector3 baseMeshScale;     // the round droplet's rest scale (uniform)
        private Vector3 baseMeshLocalPos;
        private Vector2 gravity;           // slime's gravity vector (m/s²)
        private float lostVolume;          // full-body volume units this piece carries

        private ChunkState state = ChunkState.Flying;
        private float jiggleEnv;
        private float jigglePhase;
        private float airTime;             // total time since spawn
        private float airborneTime;        // CONTINUOUS airborne time (reset by any contact)
        private bool armed;                // absorption armed once the piece has left the body
        private float breathPhase;
        private bool resolved;             // notified absorbed/lost — guards double-counting
        private float roundUpGate;         // 0 = sliver, 1 = round droplet — gates shader stretch
        private SlimeFaceShard faceShard;  // the severed half of the face, riding along

        // Cut-face meniscus (mesh OS): the cut face looks back at the body = local -Y
        // (Init aligns local +Y with the fling direction).
        private float faceW;
        private float faceWVel;
        private bool faceDone;
        private float faceOff;             // plane offset along (0,-1,0), OS
        private float faceCapH;            // membrane cap height, OS
        private float faceRho2;            // rim radius², OS

        private static readonly int VelocityDirWSID = Shader.PropertyToID("_VelocityDirWS");
        private static readonly int VelocityStretchAmountID = Shader.PropertyToID("_VelocityStretchAmount");
        private static readonly int SurfaceNormalWSID = Shader.PropertyToID("_SurfaceNormalWS");
        private static readonly int DipDirWSID = Shader.PropertyToID("_DipDirWS");
        private static readonly int SliceCountID = Shader.PropertyToID("_SliceCount");
        private static readonly int SlicePlaneOSID = Shader.PropertyToID("_SlicePlaneOS");
        private static readonly int SliceSealID = Shader.PropertyToID("_SliceSeal");
        private static readonly int SliceHealID = Shader.PropertyToID("_SliceHeal");
        private static readonly int SliceMeniscusID = Shader.PropertyToID("_SliceMeniscus");
        private static readonly int TearAmpID = Shader.PropertyToID("_TearAmp");
        private static readonly int TearFreqID = Shader.PropertyToID("_TearFreq");
        private static readonly int LipBandID = Shader.PropertyToID("_LipBand");
        private static readonly int SliceLipID = Shader.PropertyToID("_SliceLip");
        private static readonly int SliceCapColorID = Shader.PropertyToID("_SliceCapColor");
        private static readonly int SliceCapRimColorID = Shader.PropertyToID("_SliceCapRimColor");
        private static readonly int SliceCapDepthID = Shader.PropertyToID("_SliceCapDepth");

        // SLICE_MAX-sized scratch (must match SlimeSlice.hlsl); only slot 0 is used here.
        private static readonly Vector4[] planeScratch = new Vector4[3];
        private static readonly float[] sealScratch = new float[3];
        private static readonly float[] healScratch = new float[3];
        private static readonly Vector4[] meniscusScratch = new Vector4[3];
        private static readonly float[] lipScratch = new float[3];

        private void Awake()
        {
            if (rb == null) rb = GetComponent<Rigidbody2D>();
        }

        /// <param name="severDir">Cut-plane normal (world); the droplet spawns flattened along it.</param>
        public void Init(SlimeSplitSettings settings, SlimeSplitSequence seq, SlimeController slimeRef,
                         Vector2 inheritVel, Vector2 severDir, Vector2 gravityVec, float volume,
                         SlimeFaceShard shard = null)
        {
            faceShard = shard;
            s = settings;
            sequence = seq;
            slime = slimeRef;
            gravity = gravityVec;
            lostVolume = volume;
            rb = GetComponent<Rigidbody2D>();
            circle = GetComponent<CircleCollider2D>();
            rb.gravityScale = 0f;                 // gravity applied manually = same as the slime
            rb.linearDamping = s.linearDamping;
            rb.angularDamping = s.angularDamping;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // don't tunnel through the floor

            meshRenderer = GetComponentInChildren<MeshRenderer>();
            meshTf = meshRenderer != null ? meshRenderer.transform : transform;
            baseMeshScale = meshTf.localScale;
            baseMeshLocalPos = meshTf.localPosition;
            mpb = new MaterialPropertyBlock();

            // Local Y aligned with the cut normal: round-up flattens along Y and the cut face
            // sits on local -Y. The blob is symmetric, so the rotation is invisible.
            Vector2 dir = severDir.sqrMagnitude > 1e-4f ? severDir.normalized : Vector2.up;
            meshTf.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(dir.x, dir.y, 0f));

            float imp = Random.Range(s.severImpulseMin, s.severImpulseMax);
            rb.linearVelocity = inheritVel * s.inheritVelocityFactor
                              + dir * imp
                              + Vector2.up * s.upwardImpulse;

            if (Mathf.Approximately(s.spinMinDeg, 0f) && Mathf.Approximately(s.spinMaxDeg, 0f))
                rb.freezeRotation = true;
            else
                rb.angularVelocity = Random.Range(s.spinMinDeg, s.spinMaxDeg);

            jiggleEnv = 1f;
            jigglePhase = Random.value * Mathf.PI * 2f;
            breathPhase = Random.value * Mathf.PI * 2f;

            InitCutFace();
        }

        // Cut-face membrane geometry in mesh OS along n = (0,-1,0); only the spring (faceW) animates.
        private void InitCutFace()
        {
            var mf = GetComponentInChildren<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || meshRenderer == null) { faceDone = true; return; }
            Bounds mb = mf.sharedMesh.bounds;
            float support = -mb.center.y + mb.extents.y;          // AABB support along (0,-1,0)
            faceOff = Mathf.Clamp01(s.pieceCutFaceFrac) * support;
            faceCapH = support - faceOff;
            faceRho2 = Mathf.Max(support * support - faceOff * faceOff, 0.0025f * support * support);
            faceW = 0f;
            faceWVel = 0f;
            faceDone = faceCapH <= 1e-4f;

            // Static wound-look uniforms once; per-frame pushes only touch the dynamic arrays.
            meshRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(TearAmpID, s.tearAmp);
            mpb.SetFloat(TearFreqID, s.tearFreq);
            mpb.SetFloat(LipBandID, s.lipBand);
            lipScratch[0] = -s.filletMag;                        // inward, same as the body wound
            mpb.SetFloatArray(SliceLipID, lipScratch);
            mpb.SetColor(SliceCapColorID, s.sliceCapColor);
            mpb.SetColor(SliceCapRimColorID, s.sliceCapRimColor);
            mpb.SetFloat(SliceCapDepthID, s.sliceCapDepth);
            meshRenderer.SetPropertyBlock(mpb);
        }

        private void FixedUpdate()
        {
            if (s == null) return;
            float dt = Time.fixedDeltaTime;
            airTime += dt;

            // A stunned slime can't feed: no new pull starts and an in-flight one lets go, so the
            // piece drops and waits instead of being swallowed while the player is helpless.
            bool canAbsorb = slime != null && !slime.IsStunned;
            if (state == ChunkState.Attracting && !canAbsorb) state = ChunkState.Flying;

            if (state == ChunkState.Attracting)
            {
                Vector2 toSlime = slime.Position - rb.position;
                float dist = toSlime.magnitude;
                float reach = slime.SlimeRadius + ChunkWorldRadius() * 0.25f;
                if (dist <= reach)
                {
                    Resolve(absorbed: true, contactDir: -toSlime);
                    return;
                }
                Vector2 dir = dist > 1e-4f ? toSlime / dist : Vector2.up;
                rb.linearVelocity = Vector2.MoveTowards(
                    rb.linearVelocity, dir * s.absorbPullMaxSpeed, s.absorbPullAccel * dt);
                return;   // no gravity, no pit timer while being reeled in
            }

            rb.linearVelocity += gravity * dt;

            // Resting → falling again (the platform under it crumbled/moved away): ground
            // contact cancels the gravity step each frame, so sustained fall speed = airborne.
            if (state == ChunkState.Resting
                && Vector2.Dot(rb.linearVelocity, gravity.normalized) > 1.5f)
            {
                state = ChunkState.Flying;
            }

            if (state == ChunkState.Flying)
            {
                airborneTime += dt;
                if (airborneTime >= s.severedMaxAirTime)
                {
                    Resolve(absorbed: false, contactDir: Vector2.zero);   // pit — gone forever
                    return;
                }
            }

            // Absorption arms only after the piece has genuinely left the body (it spawns at
            // the slime's center — without this it would be re-swallowed the same frame).
            if (slime != null)
            {
                float dist = (slime.Position - rb.position).magnitude;
                if (!armed && airTime >= s.absorbArmDelay
                    && dist > slime.SlimeRadius + ChunkWorldRadius())
                {
                    armed = true;
                }
                if (armed && canAbsorb && dist <= s.absorbPullRadius)
                    state = ChunkState.Attracting;
            }
        }

        private void Update()
        {
            if (s == null) return;
            float dt = Time.deltaTime;

            jiggleEnv *= Mathf.Exp(-s.jiggleDecay * dt);
            jigglePhase += dt * s.jiggleFrequency * 2f * Mathf.PI;
            float j = Mathf.Sin(jigglePhase) * s.jiggleAmplitude * jiggleEnv;

            // Sliver → round ball. NOT volume-preserving: true preservation expands the perp axes
            // by 1/√flatY (×2.9 at flatten 0.12) and reads as a streak, so clamped to piecePerpExpandMax.
            float rt = Mathf.Clamp01((airTime - s.pieceSharpBeat) / Mathf.Max(0.001f, s.pieceRoundUpDuration));
            float rc = (s.pieceRoundUpCurve != null && s.pieceRoundUpCurve.length > 1)
                ? s.pieceRoundUpCurve.Evaluate(rt) : rt;
            float flatY = Mathf.Lerp(s.pieceFlatten, 1f, rc);
            float perp = Mathf.Min(1f / Mathf.Sqrt(Mathf.Max(0.01f, flatY)), s.piecePerpExpandMax);
            roundUpGate = rc;

            float bx = 0f;
            if (state == ChunkState.Resting)
            {
                breathPhase += dt * s.restBreathFreq * 2f * Mathf.PI;
                bx = Mathf.Sin(breathPhase) * s.restBreathAmp;
            }

            Vector3 sc;
            sc.x = baseMeshScale.x * perp * (1f + j + bx);
            sc.y = baseMeshScale.y * flatY * (1f - j - bx * 0.7f);
            sc.z = baseMeshScale.z * perp;
            meshTf.localScale = sc;

            UpdateCutFace(dt, rc);
            // Dissolves as the droplet rounds up, so the same eye is never on screen twice.
            if (faceShard != null) faceShard.SetFade(rc);
            PushVelocityStretch();
        }

        // The cut face heals shut with the same meniscus spring as the body wound.
        private void UpdateCutFace(float dt, float roundUpProgress)
        {
            if (faceDone || meshRenderer == null) return;

            float sdt = Mathf.Min(dt, 0.05f);
            float target = Mathf.Clamp01(roundUpProgress);
            faceWVel += (s.meniscusSpringK * (target - faceW)
                         - s.meniscusSpringDamping * faceWVel) * sdt;
            faceW = Mathf.Min(1f, faceW + faceWVel * sdt);   // never past the droplet surface

            meshRenderer.GetPropertyBlock(mpb);
            if (faceW >= 0.997f)
            {
                faceDone = true;
                mpb.SetFloat(SliceCountID, 0f);
            }
            else
            {
                planeScratch[0] = new Vector4(0f, -1f, 0f, faceOff);
                meniscusScratch[0] = new Vector4(faceW * faceCapH * s.meniscusSealMargin, faceRho2, 0f, 0f);
                sealScratch[0] = Mathf.Clamp01(faceW);
                healScratch[0] = 0f;
                mpb.SetFloat(SliceCountID, 1f);
                mpb.SetVectorArray(SlicePlaneOSID, planeScratch);
                mpb.SetFloatArray(SliceSealID, sealScratch);
                mpb.SetFloatArray(SliceHealID, healScratch);
                mpb.SetVectorArray(SliceMeniscusID, meniscusScratch);
            }
            meshRenderer.SetPropertyBlock(mpb);
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (s == null) return;
            jiggleEnv = Mathf.Min(1.5f, jiggleEnv + s.landingJiggleBoost);
            airborneTime = 0f;
            if (state == ChunkState.Flying) state = ChunkState.Resting;
        }

        private void OnCollisionStay2D(Collision2D c)
        {
            airborneTime = 0f;
        }

        // Single exit point: hands the carried volume back to the sequence exactly once.
        private void Resolve(bool absorbed, Vector2 contactDir)
        {
            if (resolved) return;
            resolved = true;
            if (sequence != null)
            {
                if (absorbed) sequence.NotifyChunkAbsorbed(contactDir, lostVolume);
                else sequence.NotifyChunkLost(lostVolume);
            }
            Destroy(gameObject);
        }

        private float ChunkWorldRadius()
        {
            if (circle == null) return 0.1f;
            return circle.radius * Mathf.Abs(transform.lossyScale.x);
        }

        private void PushVelocityStretch()
        {
            if (meshRenderer == null) return;
            Vector2 v = rb.linearVelocity;
            float speed = v.magnitude;
            float speedNorm = Mathf.Clamp01(speed / Mathf.Max(0.01f, s.wobbleSpeedRef));
            Vector2 dir = speed > 0.01f ? v / speed : Vector2.zero;

            // Gated by the round-up: shader stretch on top of the spawn-frame sliver reads as a
            // long smear, so it grows in as the droplet forms.
            float stretch = speedNorm * s.velocityStretchScale * roundUpGate;
            if (state == ChunkState.Attracting)
                stretch += speedNorm * s.absorbStretchBoost;

            meshRenderer.GetPropertyBlock(mpb);
            mpb.SetVector(VelocityDirWSID, new Vector4(dir.x, dir.y, 0f, speedNorm));
            mpb.SetFloat(VelocityStretchAmountID, stretch);
            mpb.SetVector(SurfaceNormalWSID, Vector3.up);
            mpb.SetVector(DipDirWSID, Vector3.up);
            meshRenderer.SetPropertyBlock(mpb);
        }
    }
}
