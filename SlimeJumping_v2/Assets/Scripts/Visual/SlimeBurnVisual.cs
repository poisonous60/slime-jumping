using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Burn marks on the slime body, pushed to the body MPB as a Vector4[] (xyz = OS hit, w =
    /// radius * fade). Discs are anchored in REST-POSE OBJECT SPACE, not world: the shader
    /// captures positionOS before SlimeDeform_float runs, so they stay welded through squash,
    /// rotation and stretch. Hits within mergeDistance refresh an existing burn; MaxBurns slots,
    /// oldest evicted. Also drives SlimeVisual.SetExternalCompression off the same hazard hook.
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

        [Header("Hit Flash")]
        [Tooltip("Master toggle. When false, _HitFlashStrength is forced to 0 every frame — burn rendering is byte-identical to pre-flash behavior.")]
        [SerializeField] private bool enableHitFlash = true;
        [ColorUsage(true, true)]
        [SerializeField] private Color hitFlashColor = new Color(0.85f, 0.96f, 1f, 1f);
        [Tooltip("Peak intensity at hit moment.")]
        [Range(0f, 1f)] [SerializeField] private float hitFlashPeak = 1f;
        [Tooltip("Decay duration (sec) from peak to 0.")]
        [SerializeField] private float hitFlashDuration = 0.08f;

        // --- runtime ---
        private struct Burn
        {
            public Vector3 osPos;
            public float radius;
            public float bornTime;
        }

        private readonly List<Burn> burns = new List<Burn>(MaxBurns);
        // Fixed MaxBurns length: Unity locks the array size on the first SetVectorArray call,
        // and a later resize silently fails.
        private readonly Vector4[] burnPacked = new Vector4[MaxBurns];

        private MeshRenderer bodyMR;
        private Transform meshTransform;
        private MaterialPropertyBlock bodyMpb;

        private static readonly int BurnsID = Shader.PropertyToID("_Burns");
        private static readonly int BurnCountID = Shader.PropertyToID("_BurnCount");
        private static readonly int BurnStrengthID = Shader.PropertyToID("_BurnStrength");
        private static readonly int HitFlashStrengthID = Shader.PropertyToID("_HitFlashStrength");
        private static readonly int HitFlashColorID = Shader.PropertyToID("_HitFlashColor");

        private float lastHitTime = -999f;
        private float flashEndTime = -999f;
        private float pendingBurnScale = 1f;

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

        // meshTransform must be the MeshFilter's own transform (SlimeMesh, several levels under
        // body3d) — only its InverseTransformPoint lands in the same OS as IN.positionOS.
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
            // Else the shader keeps rendering marks while this component is disabled.
            ClearAndPushBurns();
        }

        /// Radius scale for the next OnHazardPushed burn only, then resets to 1. Call right
        /// before the hazard push — the event is synchronous, so the same hit consumes it.
        public void SetNextBurnScale(float scale)
        {
            pendingBurnScale = Mathf.Max(0f, scale);
        }

        private void HandleHazardPushed(Vector2 deltaV, Vector2 hitPoint)
        {
            float scale = pendingBurnScale;
            pendingBurnScale = 1f;
            lastHitTime = Time.time;
            if (enableHitFlash) flashEndTime = Time.time + hitFlashDuration;

            if (scale <= 0f) return;
            if (meshTransform == null) ResolveBodyMeshRenderer();
            if (meshTransform == null) return;

            // InverseTransformPoint accounts for body3d's per-frame surface-align rotation, so
            // the stored OS coord pins to the patch that was actually facing the projectile.
            Vector3 hitWS = new Vector3(hitPoint.x, hitPoint.y, meshTransform.position.z);
            Vector3 hitOS = meshTransform.InverseTransformPoint(hitWS);

            // Same-spot hits widen a capped radius instead of eating extra slots.
            float scaledRadius = burnRadius * scale;
            float merge2 = mergeDistance * mergeDistance;
            for (int i = 0; i < burns.Count; i++)
            {
                Burn b = burns[i];
                if ((b.osPos - hitOS).sqrMagnitude < merge2)
                {
                    b.bornTime = Time.time;
                    b.radius = Mathf.Min(scaledRadius * 1.4f, b.radius + scaledRadius * 0.25f);
                    burns[i] = b;
                    return;
                }
            }

            if (burns.Count >= MaxBurns)
            {
                burns.RemoveAt(0);
            }
            burns.Add(new Burn
            {
                osPos = hitOS,
                radius = scaledRadius,
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

        // [DefaultExecutionOrder(100)] puts this after SlimeVisual's LateUpdate; the
        // GetPropertyBlock-then-Set round trip is what preserves its deform writes to the same MPB.
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
            // Zero the unused tail so no stale slot can ever be sampled.
            for (int i = writeIdx; i < MaxBurns; i++)
            {
                burnPacked[i] = Vector4.zero;
            }

            bodyMR.GetPropertyBlock(bodyMpb);
            bodyMpb.SetVectorArray(BurnsID, burnPacked);
            bodyMpb.SetInt(BurnCountID, writeIdx);
            bodyMpb.SetFloat(BurnStrengthID, burnStrength);
            // Pushed even when disabled, else a stale GPU value from a prior session bleeds through.
            float flashStrength = 0f;
            if (enableHitFlash)
            {
                float flashRemain = flashEndTime - Time.time;
                if (flashRemain > 0f && hitFlashDuration > 0f)
                {
                    flashStrength = hitFlashPeak * Mathf.Clamp01(flashRemain / hitFlashDuration);
                }
            }
            bodyMpb.SetFloat(HitFlashStrengthID, flashStrength);
            bodyMpb.SetColor(HitFlashColorID, hitFlashColor);
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
            bodyMpb.SetFloat(HitFlashStrengthID, 0f);
            bodyMpb.SetColor(HitFlashColorID, hitFlashColor);
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
