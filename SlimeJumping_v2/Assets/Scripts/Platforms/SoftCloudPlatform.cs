using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;
using SlimeJumping.Interaction;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Jelly cloud, two channels: BODY = whole-cloud recoil (analytic spring → rb.MovePosition),
    /// SURFACE = local dent inside the rider's footprint (contour field → SetPath + mesh).
    /// A term added to every contour point breathes the whole silhouette instead of denting.
    /// Dent timing is an authored curve, not a spring — a spring can't dwell at the apex.
    /// Order -100 runs before the SlimeController probe; maxSurfaceSpeed caps per-tick surface motion.
    /// </summary>
    [RequireComponent(typeof(PolygonCollider2D))]
    public class SoftCloudPlatform : PlatformMotor, IPlatformEffect
    {
        [Header("Config")]
        [Tooltip("Leave empty to use the shared Assets/Resources/SoftCloudConfig.asset. Assign only for a " +
                 "cloud that must deviate from the shared tuning.")]
        [SerializeField] SoftCloudConfig configOverride;

        [Header("Scene refs")]
        [Tooltip("Child MeshFilter whose mesh dents to follow the field. Auto-found in children if empty.")]
        [SerializeField] MeshFilter visualMesh;

        SoftCloudConfig cfg;

        // --- Field state (contour = source of truth) -----------------------
        PolygonCollider2D poly;
        Vector2[] restLocal;      // rest contour points (root-local)
        Vector2[] inwardNormal;   // unit inward normal per point (root-local)
        float[]   disp;           // current displacement along inwardNormal (+ = inward)
        float[]   vel;            // SmoothDamp velocity per point
        float[]   target;         // authored target displacement this tick
        float[]   targetScratch;  // smoothing scratch
        Vector2[] pathScratch;    // reused SetPath buffer
        int n;
        bool asleep;

        // --- Body recoil state (whole-cloud translation) ---------------------
        // Spring is EVALUATED analytically, not integrated: a 50 Hz explicit integrator under-shoots
        // the first peak by 10-50%, which would make the config's recoil *distance* a lie.
        Vector2 restPosition;   // rb.position at Awake — the spring's anchor
        Vector2 springEq;       // where the body settles: sag offset while ridden, zero when empty
        Vector2 springX0, springV0;
        float   springT;

        readonly Breathing idle = new Breathing();

        // --- Riders --------------------------------------------------------
        class RiderState { public SlimeController slime; public float envTime; public float envPeak; }
        readonly List<RiderState> riders = new List<RiderState>();

        // --- Visual mesh follower ------------------------------------------
        const int K = 4; // contour influences per vertex
        Mesh meshInstance;
        Vector3[] restVerts;      // child-local rest positions
        Vector3[] vertScratch;    // reused mesh write buffer
        int[]   vertInfluence;    // [vertCount*K] contour indices (-1 = unused)
        float[] vertWeight;       // [vertCount*K] normalized weights
        Matrix4x4 rootToChildVec; // transforms a root-local vector into child-local

        protected override void Awake()
        {
            base.Awake(); // PlatformMotor: kinematic Rigidbody2D + interpolation
            poly = GetComponent<PolygonCollider2D>();
            cfg = SoftCloudConfig.Resolve(configOverride);
            idle.Configure(cfg.idleAmplitude, cfg.idleFrequency, cfg.idleAmplitude * 0.4f);
            idle.Reset();
            restPosition = rb.position;
            BuildContour();
            BuildVisual();
        }

        void BuildContour()
        {
            if (poly == null || poly.pathCount == 0) { n = 0; return; }

            var path = poly.GetPath(0);
            n = path.Length;
            restLocal     = new Vector2[n];
            inwardNormal  = new Vector2[n];
            disp          = new float[n];
            vel           = new float[n];
            target        = new float[n];
            targetScratch = new float[n];
            pathScratch   = new Vector2[n];

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < n; i++) { restLocal[i] = path[i]; centroid += path[i]; }
            centroid /= Mathf.Max(1, n);

            for (int i = 0; i < n; i++)
            {
                Vector2 prev = restLocal[(i - 1 + n) % n];
                Vector2 next = restLocal[(i + 1) % n];
                Vector2 tangent = next - prev;
                Vector2 nrm = new Vector2(-tangent.y, tangent.x);
                if (Vector2.Dot(nrm, centroid - restLocal[i]) < 0f) nrm = -nrm;
                inwardNormal[i] = nrm.sqrMagnitude > 1e-8f ? nrm.normalized : Vector2.up;
            }
        }

        void BuildVisual()
        {
            if (visualMesh == null) visualMesh = GetComponentInChildren<MeshFilter>();
            if (visualMesh == null || n == 0) return;

            meshInstance = visualMesh.mesh; // Unity instantiates a per-renderer copy (asset safe)
            restVerts   = meshInstance.vertices;
            vertScratch = new Vector3[restVerts.Length];
            System.Array.Copy(restVerts, vertScratch, restVerts.Length);

            rootToChildVec = visualMesh.transform.worldToLocalMatrix * transform.localToWorldMatrix;

            // Precompute each vertex's K nearest contour points (by XY, root-local) + weights.
            int vc = restVerts.Length;
            vertInfluence = new int[vc * K];
            vertWeight    = new float[vc * K];
            float r2 = cfg.visualInfluenceRadius * cfg.visualInfluenceRadius;

            var bestIdx = new int[K];
            var bestD2  = new float[K];
            for (int v = 0; v < vc; v++)
            {
                Vector3 rootLocal = transform.InverseTransformPoint(visualMesh.transform.TransformPoint(restVerts[v]));
                Vector2 xy = new Vector2(rootLocal.x, rootLocal.y);

                for (int k = 0; k < K; k++) { bestIdx[k] = -1; bestD2[k] = float.MaxValue; }
                for (int i = 0; i < n; i++)
                {
                    float d2 = (restLocal[i] - xy).sqrMagnitude;
                    if (d2 > r2) continue;
                    for (int k = 0; k < K; k++)
                    {
                        if (d2 < bestD2[k])
                        {
                            for (int m = K - 1; m > k; m--) { bestD2[m] = bestD2[m - 1]; bestIdx[m] = bestIdx[m - 1]; }
                            bestD2[k] = d2; bestIdx[k] = i;
                            break;
                        }
                    }
                }

                float wSum = 0f;
                for (int k = 0; k < K; k++)
                {
                    if (bestIdx[k] < 0) { vertWeight[v * K + k] = 0f; vertInfluence[v * K + k] = -1; continue; }
                    float w = 1f / (bestD2[k] + 1e-4f);
                    vertInfluence[v * K + k] = bestIdx[k];
                    vertWeight[v * K + k] = w;
                    wSum += w;
                }
                if (wSum > 0f)
                    for (int k = 0; k < K; k++) vertWeight[v * K + k] /= wSum;
            }
        }

        // --- PlatformMotor ---------------------------------------------------

        // BODY channel: rigid translation, carried to the rider by PlatformCarrier via DeltaPosition.
        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            springT += dt;
            EvaluateSpring(springX0, springV0, springT, cfg.bodyFrequency, cfg.bodyDamping,
                           out Vector2 swing, out Vector2 bodyVel);
            Vector2 bodyOffset = springEq + swing;

            if (bodyOffset.sqrMagnitude > cfg.bodyMaxOffset * cfg.bodyMaxOffset)
            {
                // Re-seed from the clamped pose, else the analytic solution drifts from where the body is.
                bodyOffset = bodyOffset.normalized * cfg.bodyMaxOffset;
                springX0 = bodyOffset - springEq; springV0 = Vector2.zero; springT = 0f;
            }
            else if (swing.sqrMagnitude < 1e-8f && bodyVel.sqrMagnitude < 1e-8f)
            {
                // Settled — stop springT growing unbounded (cos/sin of a huge argument loses precision).
                bodyOffset = springEq;
                springX0 = Vector2.zero; springV0 = Vector2.zero; springT = 0f;
            }

            return (restPosition + bodyOffset, rb.rotation);
        }

        public override float VelocityTransferScale => 0f; // wobble must never bake into a launch

        protected override void OnAfterMotion(float dt)
        {
            if (n == 0) return;
            PruneRiders();

            if (asleep && riders.Count == 0) return;

            ComputeTarget(dt);
            Follow(dt);

            float maxDelta = ApplyToCollider();
            ApplyToVisual();

            asleep = riders.Count == 0 && maxDelta < cfg.restEpsilon && FieldAtRest();
        }

        // Layer 1 (landing envelope) + layer 2 (resting dimple) + layer 3 (idle jiggle) → target[].
        void ComputeTarget(float dt)
        {
            for (int i = 0; i < n; i++) target[i] = 0f;

            // Idle breathing folds into the rider footprint below; adding it to every contour point
            // would puff the WHOLE silhouette instead of the local dimple.
            float breath = 0f;
            if (riders.Count > 0 && cfg.idleAmplitude > 0f)
            {
                idle.Configure(cfg.idleAmplitude, cfg.idleFrequency, cfg.idleAmplitude * 0.4f); // pick up live config tweaks
                idle.Tick(dt);
                breath = idle.Value;
            }

            for (int r = 0; r < riders.Count; r++)
            {
                RiderState rs = riders[r];
                SlimeController rider = rs.slime;
                if (rider == null) continue;

                rs.envTime += dt;
                float t01 = cfg.dentDuration > 0f ? Mathf.Clamp01(rs.envTime / cfg.dentDuration) : 1f;
                float env = cfg.dentEnvelope.Evaluate(t01) * rs.envPeak; // signed world depth (authored shape)
                float amp = env + cfg.restDentDepth + breath;

                Vector2 pushLocal = transform.InverseTransformDirection(-rider.SurfaceNormal);
                Vector2 centerWorld = rider.Position - rider.SurfaceNormal * rider.SlimeRadius;
                Vector2 centerLocal = transform.InverseTransformPoint(centerWorld);
                float radius = (rider.SlimeRadius / Mathf.Max(0.01f, rider.BodyScaleRatio)) * cfg.radiusMult;

                for (int i = 0; i < n; i++)
                {
                    float f = Falloff(Vector2.Distance(restLocal[i], centerLocal), radius);
                    if (f <= 0f) continue;
                    float align = Vector2.Dot(pushLocal, inwardNormal[i]);
                    if (align <= 0f) continue;
                    target[i] += amp * f * align;
                }
            }

            SmoothTarget(); // neighbor smoothing → coherent chunk
        }

        void SmoothTarget()
        {
            if (cfg.neighborCoupling <= 0f) return;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    float avg = 0.5f * (target[(i - 1 + n) % n] + target[(i + 1) % n]);
                    targetScratch[i] = Mathf.Lerp(target[i], avg, cfg.neighborCoupling);
                }
                System.Array.Copy(targetScratch, target, n);
            }
        }

        // Follower is critically damped, so the bounce shape comes from the curve, not the follower.
        // maxSurfaceSpeed caps per-tick motion so the probe can always track the surface.
        void Follow(float dt)
        {
            for (int i = 0; i < n; i++)
            {
                disp[i] = Mathf.SmoothDamp(disp[i], target[i], ref vel[i], cfg.followSmoothTime, cfg.maxSurfaceSpeed, dt);
                if (disp[i] > cfg.maxDeflection) disp[i] = cfg.maxDeflection;
            }
        }

        float ApplyToCollider()
        {
            float maxDelta = 0f;
            for (int i = 0; i < n; i++)
            {
                pathScratch[i] = restLocal[i] + inwardNormal[i] * disp[i];
                float d = Mathf.Abs(disp[i]);
                if (d > maxDelta) maxDelta = d;
            }
            // Only re-cook the collider when it meaningfully moved (SetPath invalidates Box2D contacts).
            if (maxDelta >= cfg.restEpsilon || !asleep)
                poly.SetPath(0, pathScratch);
            return maxDelta;
        }

        void ApplyToVisual()
        {
            if (meshInstance == null) return;
            int vc = restVerts.Length;
            for (int v = 0; v < vc; v++)
            {
                Vector2 offRoot = Vector2.zero;
                int baseI = v * K;
                for (int kk = 0; kk < K; kk++)
                {
                    int ci = vertInfluence[baseI + kk];
                    if (ci < 0) continue;
                    offRoot += vertWeight[baseI + kk] * inwardNormal[ci] * disp[ci];
                }
                Vector3 offChild = rootToChildVec.MultiplyVector(new Vector3(offRoot.x, offRoot.y, 0f));
                vertScratch[v] = restVerts[v] + offChild;
            }
            meshInstance.SetVertices(vertScratch);
            meshInstance.RecalculateBounds();
        }

        // --- IPlatformEffect (rider set) -------------------------------------

        public void OnSlimeAttach(SlimeController slime)
        {
            if (slime == null) return;
            RiderState rs = FindRider(slime);
            if (rs == null) { rs = new RiderState { slime = slime }; riders.Add(rs); }
            rs.envTime = 0f;
            rs.envPeak = cfg.dentBaseDepth + cfg.dentDepthPerSpeed * Mathf.Max(0f, slime.ImpactNormalSpeed);

            // Kick along the landing normal and sink the equilibrium to the sag pose, so the cloud
            // stays pressed down while ridden; rest is restored on the last detach.
            float peak = cfg.bodyRecoilDistance * (Mathf.Max(0f, slime.ImpactNormalSpeed) / cfg.bodyRecoilRefSpeed);
            ReseedSpring(-slime.SurfaceNormal * cfg.bodySagDistance,
                         -slime.SurfaceNormal * (peak * ImpulsePerPeakDistance(cfg.bodyFrequency, cfg.bodyDamping)));

            asleep = false;
        }

        public void OnSlimeStay(SlimeController slime, float dt) { /* drive runs in OnAfterMotion (-100) */ }

        public void OnSlimeDetach(SlimeController slime)
        {
            for (int i = riders.Count - 1; i >= 0; i--)
                if (riders[i].slime == slime) riders.RemoveAt(i);

            if (riders.Count == 0) ReseedSpring(Vector2.zero, Vector2.zero); // unloaded — rise back to rest
        }

        // Move the equilibrium without teleporting the body: the current pose becomes the new
        // segment's initial condition.
        void ReseedSpring(Vector2 newEq, Vector2 addedVelocity)
        {
            EvaluateSpring(springX0, springV0, springT, cfg.bodyFrequency, cfg.bodyDamping,
                           out Vector2 swing, out Vector2 v);
            springX0 = springEq + swing - newEq;
            springV0 = v + addedVelocity;
            springEq = newEq;
            springT = 0f;
        }

        // --- Helpers ---------------------------------------------------------

        /// <summary>Exact underdamped-spring solution at t; evaluated, not integrated, so the peak is frame-rate independent.</summary>
        public static void EvaluateSpring(Vector2 x0, Vector2 v0, float t, float frequency, float damping,
                                          out Vector2 x, out Vector2 v)
        {
            float w = 2f * Mathf.PI * frequency;
            float z = Mathf.Clamp(damping, 0.01f, 0.99f); // overdamped has no oscillation — treat as near-critical
            float a = z * w;
            float wd = w * Mathf.Sqrt(1f - z * z);

            float e = Mathf.Exp(-a * t), c = Mathf.Cos(wd * t), s = Mathf.Sin(wd * t);
            x = e * (c * x0 + (s / wd) * (v0 + a * x0));
            v = e * (c * v0 - (s / wd) * (a * v0 + (w * w) * x0));
        }

        /// <summary>Initial velocity peaking at exactly 1 world unit, so <c>bodyRecoilDistance</c> reads as real distance, not an impulse.</summary>
        public static float ImpulsePerPeakDistance(float frequency, float damping)
        {
            float z = Mathf.Clamp(damping, 0.01f, 0.99f);
            return 2f * Mathf.PI * frequency * Mathf.Exp(z * Mathf.Acos(z) / Mathf.Sqrt(1f - z * z));
        }

        RiderState FindRider(SlimeController slime)
        {
            for (int i = 0; i < riders.Count; i++)
                if (riders[i].slime == slime) return riders[i];
            return null;
        }

        void PruneRiders()
        {
            for (int i = riders.Count - 1; i >= 0; i--)
                if (riders[i].slime == null) riders.RemoveAt(i); // destroyed slime (Unity fake-null aware)
        }

        bool FieldAtRest()
        {
            for (int i = 0; i < n; i++)
                if (Mathf.Abs(disp[i]) > cfg.restEpsilon || Mathf.Abs(vel[i]) > cfg.restEpsilon) return false;
            return true;
        }

        // smoothstep falloff, 1 at center → 0 at radius.
        static float Falloff(float dist, float radius)
        {
            if (radius <= 0f) return 0f;
            float t = 1f - Mathf.Clamp01(dist / radius);
            return t * t * (3f - 2f * t);
        }
    }
}
