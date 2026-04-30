using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Burn-mark visual for the slime body. Each granite-blast hit deposits a
    /// soft black disc on the slime's surface at the impact location. Discs
    /// stay welded to the body through translation, surface-align rotation,
    /// asymmetric squash, charge dip, and velocity stretch, because they're
    /// anchored in the body mesh's REST-POSE OBJECT SPACE rather than world
    /// space — the body shader captures positionOS as a varying BEFORE
    /// SlimeDeform_float runs, so the rasterizer interpolates rest-pose OS
    /// across the triangle no matter how the vertex stage displaces vertices.
    ///
    /// Each burn fades independently after a per-hit delay. Hits within
    /// <c>mergeDistance</c> of an existing burn refresh that burn instead of
    /// consuming a new slot. Slot count is capped at <c>MaxBurns</c>; oldest
    /// is evicted when full.
    ///
    /// This component pushes a <c>Vector4[]</c> array (xyz = OS hit position,
    /// w = radius * fade) directly onto the body MeshRenderer's
    /// MaterialPropertyBlock via <c>GetPropertyBlock</c> /
    /// <c>SetPropertyBlock</c>. <c>SlimeVisual</c> writes its deform values
    /// to the same MPB earlier in LateUpdate; <c>[DefaultExecutionOrder(100)]</c>
    /// ensures this component runs after, and <c>GetPropertyBlock</c>
    /// preserves the deform writes.
    ///
    /// Also drives <see cref="SlimeVisual.SetExternalCompression"/> for the
    /// "stuck against a wall while being beamed" squeeze visual — unrelated
    /// to burns, just sharing the same OnHazardPushed hook.
    ///
    /// Lives on the slime root.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SlimeBurnVisual : MonoBehaviour
    {
        public const int MaxBurns = 24;

        [Header("Wiring")]
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private SlimeVisual slimeVisual;
        [Tooltip("Body3D root Transform — its MeshFilter is what we sample InverseTransformPoint against to map world hits into rest-pose OS. If null, auto-resolves from slimeVisual.Body3d.")]
        [SerializeField] private Transform body3d;

        [Header("Burn")]
        [Tooltip("Soft-disc radius in mesh OS units. Tune to mesh scale — for a unit-radius icosphere ~0.25 makes a ~28° angular spot.")]
        [SerializeField] private float burnRadius = 0.25f;
        [Tooltip("Master darkness multiplier. 1 = full black at disc center.")]
        [Range(0f, 1f)] [SerializeField] private float burnStrength = 1f;
        [Tooltip("If a new hit lands within this OS distance of an existing burn, the existing entry is refreshed (bumped radius + bornTime) instead of allocating a new slot.")]
        [SerializeField] private float mergeDistance = 0.18f;
        [Tooltip("Each burn holds full intensity for this many seconds before its fade timer starts.")]
        [SerializeField] private float recoveryStartDelay = 2.5f;
        [Tooltip("Once the delay passes, each burn linearly fades its radius to 0 over this many seconds.")]
        [SerializeField] private float recoveryDuration = 2.5f;

        [Header("Stuck Compression")]
        [Tooltip("When the slime is being hit and barely moving, SetExternalCompression is pumped to this peak. (X, Y) in scale-delta units (e.g. (0.18, -0.18) = 18% wider, 18% shorter).")]
        [SerializeField] private Vector2 compressionPeak = new Vector2(0.18f, -0.18f);
        [SerializeField] private float stuckPositionEpsilon = 0.06f;
        [SerializeField] private float stuckWindow = 0.18f;
        [SerializeField] private float pushReceiveWindow = 0.12f;
        [SerializeField] private float compressionRiseTime = 0.18f;
        [SerializeField] private float compressionDecayTime = 0.22f;

        // --- runtime ---
        private struct Burn
        {
            public Vector3 osPos;
            public float radius;
            public float bornTime;
        }

        private readonly List<Burn> burns = new List<Burn>(MaxBurns);
        // Allocated once at MaxBurns size — Unity locks the array length on the
        // first SetVectorArray call, so resizing later silently fails.
        private readonly Vector4[] burnPacked = new Vector4[MaxBurns];

        private MeshRenderer bodyMR;
        private Transform meshTransform;
        private MaterialPropertyBlock bodyMpb;

        private static readonly int BurnsID = Shader.PropertyToID("_Burns");
        private static readonly int BurnCountID = Shader.PropertyToID("_BurnCount");
        private static readonly int BurnStrengthID = Shader.PropertyToID("_BurnStrength");

        private float lastHitTime = -999f;

        // Sliding-window position samples for stuck detection.
        private const int RingCapacity = 32;
        private readonly Vector2[] posRing = new Vector2[RingCapacity];
        private readonly float[] timeRing = new float[RingCapacity];
        private int ringHead;
        private int ringCount;

        private Vector2 currentCompression;
        private Vector2 compressionVel;

        void Awake()
        {
            if (slimeController == null) slimeController = GetComponentInParent<SlimeController>();
            if (slimeVisual == null) slimeVisual = GetComponentInParent<SlimeVisual>();
            if (body3d == null && slimeVisual != null && slimeVisual.Body3d != null)
            {
                body3d = slimeVisual.Body3d;
            }
            ResolveBodyMeshRenderer();
            bodyMpb = new MaterialPropertyBlock();
        }

        // Walks body3d's subtree to find the MeshFilter+MeshRenderer pair. The
        // path in scene is Slime/SlimeVisual3d/MeshBaseline/MeshDepthFlatten/SlimeMesh
        // — body3d is SlimeVisual3d, MeshFilter+MeshRenderer live on SlimeMesh.
        // meshTransform is SlimeMesh's transform; its InverseTransformPoint maps
        // world coords into the same OS the vertex shader's IN.positionOS lives in.
        private void ResolveBodyMeshRenderer()
        {
            if (body3d == null) return;
            var mf = body3d.GetComponentInChildren<MeshFilter>();
            if (mf == null) return;
            meshTransform = mf.transform;
            bodyMR = mf.GetComponent<MeshRenderer>();
        }

        void OnEnable()
        {
            if (slimeController != null) slimeController.OnHazardPushed += HandleHazardPushed;
        }

        void OnDisable()
        {
            if (slimeController != null) slimeController.OnHazardPushed -= HandleHazardPushed;
            if (slimeVisual != null) slimeVisual.SetExternalCompression(Vector2.zero);
            currentCompression = Vector2.zero;
            compressionVel = Vector2.zero;
            // Push an empty burn array so the body shader doesn't keep rendering
            // marks while this component is disabled.
            ClearAndPushBurns();
        }

        private void HandleHazardPushed(Vector2 deltaV, Vector2 hitPoint)
        {
            lastHitTime = Time.time;

            if (meshTransform == null) ResolveBodyMeshRenderer();
            if (meshTransform == null) return;

            // World hit → rest-pose OS via the SlimeMesh transform. body3d
            // rotates per frame for surface-normal alignment, and InverseTransformPoint
            // accounts for the current orientation, so the captured OS coord is
            // pinned to whichever surface patch was facing the projectile.
            Vector3 hitWS = new Vector3(hitPoint.x, hitPoint.y, meshTransform.position.z);
            Vector3 hitOS = meshTransform.InverseTransformPoint(hitWS);

            // Merge if a near-duplicate exists — refresh time, bump radius up to
            // a cap so repeated same-spot hits darken/widen instead of stealing
            // additional slots.
            float merge2 = mergeDistance * mergeDistance;
            for (int i = 0; i < burns.Count; i++)
            {
                Burn b = burns[i];
                if ((b.osPos - hitOS).sqrMagnitude < merge2)
                {
                    b.bornTime = Time.time;
                    b.radius = Mathf.Min(burnRadius * 1.4f, b.radius + burnRadius * 0.25f);
                    burns[i] = b;
                    return;
                }
            }

            // No merge — append. Evict oldest if full.
            if (burns.Count >= MaxBurns)
            {
                burns.RemoveAt(0);
            }
            burns.Add(new Burn
            {
                osPos = hitOS,
                radius = burnRadius,
                bornTime = Time.time,
            });
        }

        void FixedUpdate()
        {
            if (slimeController == null) return;
            float now = Time.time;
            posRing[ringHead] = slimeController.Position;
            timeRing[ringHead] = now;
            ringHead = (ringHead + 1) % RingCapacity;
            if (ringCount < RingCapacity) ringCount++;
        }

        // Runs after SlimeVisual.LateUpdate (default execution order 0). SlimeVisual
        // writes deform values into the body MeshRenderer's MPB; this LateUpdate
        // GetPropertyBlock-then-SetPropertyBlock layers burn data on top while
        // preserving SlimeVisual's writes.
        void LateUpdate()
        {
            float dt = Time.deltaTime;
            PushBurnState();
            UpdateCompression(dt);
        }

        private void PushBurnState()
        {
            if (bodyMR == null)
            {
                ResolveBodyMeshRenderer();
                if (bodyMR == null) return;
            }

            float now = Time.time;
            int writeIdx = 0;
            for (int i = 0; i < burns.Count; i++)
            {
                Burn b = burns[i];
                float age = now - b.bornTime;
                float fade;
                if (age <= recoveryStartDelay)
                {
                    fade = 1f;
                }
                else if (recoveryDuration > 0f)
                {
                    fade = 1f - Mathf.Clamp01((age - recoveryStartDelay) / recoveryDuration);
                }
                else
                {
                    fade = 0f;
                }

                if (fade <= 0f)
                {
                    burns.RemoveAt(i);
                    i--;
                    continue;
                }

                float effectiveRadius = b.radius * fade;
                burnPacked[writeIdx] = new Vector4(b.osPos.x, b.osPos.y, b.osPos.z, effectiveRadius);
                writeIdx++;
            }
            // Zero-fill the unused tail so stale data from previous frames doesn't
            // get sampled if a slot is later occupied with a smaller _BurnCount.
            for (int i = writeIdx; i < MaxBurns; i++)
            {
                burnPacked[i] = Vector4.zero;
            }

            bodyMR.GetPropertyBlock(bodyMpb);
            bodyMpb.SetVectorArray(BurnsID, burnPacked);
            bodyMpb.SetInt(BurnCountID, writeIdx);
            bodyMpb.SetFloat(BurnStrengthID, burnStrength);
            bodyMR.SetPropertyBlock(bodyMpb);
        }

        private void ClearAndPushBurns()
        {
            burns.Clear();
            if (bodyMR == null) return;
            for (int i = 0; i < MaxBurns; i++) burnPacked[i] = Vector4.zero;
            bodyMR.GetPropertyBlock(bodyMpb);
            bodyMpb.SetVectorArray(BurnsID, burnPacked);
            bodyMpb.SetInt(BurnCountID, 0);
            bodyMpb.SetFloat(BurnStrengthID, burnStrength);
            bodyMR.SetPropertyBlock(bodyMpb);
        }

        private void UpdateCompression(float dt)
        {
            if (slimeVisual == null) return;
            bool beingPressed = (Time.time - lastHitTime) < pushReceiveWindow;
            bool stuck = beingPressed && IsStuckOverWindow();
            Vector2 target = stuck ? compressionPeak : Vector2.zero;
            float smoothTime = stuck ? compressionRiseTime : compressionDecayTime;
            currentCompression = Vector2.SmoothDamp(currentCompression, target, ref compressionVel, smoothTime);
            slimeVisual.SetExternalCompression(currentCompression);
        }

        private bool IsStuckOverWindow()
        {
            if (ringCount < 2) return false;
            int newestIdx = (ringHead - 1 + RingCapacity) % RingCapacity;
            Vector2 newestPos = posRing[newestIdx];
            float newestTime = timeRing[newestIdx];

            float eps2 = stuckPositionEpsilon * stuckPositionEpsilon;
            int seen = 0;
            for (int i = 0; i < ringCount; i++)
            {
                int probe = (newestIdx - i + RingCapacity) % RingCapacity;
                if (newestTime - timeRing[probe] > stuckWindow) break;
                if ((posRing[probe] - newestPos).sqrMagnitude > eps2) return false;
                seen++;
            }
            return seen >= 2;
        }
    }
}
