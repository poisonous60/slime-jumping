using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Burn-mark visual for the slime body. Drives a single-state cel-shaded black
    /// overlay rendered as a thin shell over the body mesh — burn appears on the
    /// SIDE of the slime that was hit, scorched into the visual surface, not as
    /// floating particles. All hits accumulate into one shared state:
    ///   • <c>burnDir</c> — exponentially-smoothed world direction (slime center →
    ///     latest hit point). The shader darkens fragments whose normal aligns
    ///     with this direction.
    ///   • <c>burnStrength</c> — total darkness 0..1, raised by every hit, capped
    ///     at <c>maxStrength</c>. After <c>recoveryStartDelay</c> seconds without
    ///     a new hit, decays to 0 over <c>recoveryRate</c> per second. Recovery is
    ///     a single global fade — the entire burn area lightens together, not per
    ///     decal.
    ///
    /// Deformation tracking: the shell shader is a verbatim clone of SlimeBody3D's
    /// vertex pipeline (same SlimeDeform_float call). SlimeVisual mirrors its full
    /// MaterialPropertyBlock onto the shell renderer (via RegisterMirroredRenderer)
    /// at the end of every LateUpdate, so the shell deforms in lock-step with the
    /// body — velocity stretch, charge dip, asymmetric squash, all matched. This
    /// component then layers _BurnDir + _Inflate on top via GetPropertyBlock /
    /// SetPropertyBlock in its own LateUpdate, scheduled AFTER SlimeVisual via
    /// DefaultExecutionOrder.
    ///
    /// Also drives the <see cref="SlimeVisual.SetExternalCompression"/> hook for
    /// the "stuck against a wall while being beamed" squeeze visual.
    /// Lives on the slime root.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class SlimeBurnVisual : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private SlimeVisual slimeVisual;
        [Tooltip("Body3D root Transform — the burn shell mesh is parented under the body's MeshFilter so it follows squash/stretch via inherited transform. If null, auto-resolves from slimeVisual.Body3d (preferred) or slimeVisual.Body.")]
        [SerializeField] private Transform body3d;
        [Tooltip("Shader for the burn shell mesh. Defaults to 'SlimeJumping/SlimeBurnShell' via Shader.Find on Awake.")]
        [SerializeField] private Shader burnShellShader;

        [Header("Burn Accumulation")]
        [Tooltip("Strength added per hit. Caps at maxStrength.")]
        [Range(0f, 1f)] [SerializeField] private float perHitStrength = 0.45f;
        [Range(0.1f, 1f)] [SerializeField] private float maxStrength = 1f;
        [Tooltip("Blend toward the latest hit direction per hit. 1 = snap fully to latest, 0 = ignore new hits. Higher values = burn rotates faster toward newest hit. ~0.55 keeps the previous burn visible while shifting toward the latest impact.")]
        [Range(0f, 1f)] [SerializeField] private float dirRecencyBlend = 0.55f;

        [Header("Recovery")]
        [Tooltip("Seconds without any new hit before recovery begins. While the slime keeps getting hit, strength stays high (or grows).")]
        [SerializeField] private float recoveryStartDelay = 1.5f;
        [Tooltip("Strength units per second decayed during recovery. 0.4 = full burn fades in 2.5 sec.")]
        [SerializeField] private float recoveryRate = 0.4f;

        [Header("Shell Render")]
        [Tooltip("World-units inflation along the body's vertex normal. The shell sits this much outside the body so it renders cleanly without z-fighting. Keep tiny (<= ~1% of slime radius).")]
        [SerializeField] private float shellInflate = 0.01f;

        [Header("Stuck Compression")]
        [Tooltip("When the slime is being hit and barely moving, SetExternalCompression is pumped to this peak. (X, Y) in scale-delta units (e.g. (0.18, -0.18) = 18% wider, 18% shorter).")]
        [SerializeField] private Vector2 compressionPeak = new Vector2(0.18f, -0.18f);
        [SerializeField] private float stuckPositionEpsilon = 0.06f;
        [SerializeField] private float stuckWindow = 0.18f;
        [SerializeField] private float pushReceiveWindow = 0.12f;
        [SerializeField] private float compressionRiseTime = 0.18f;
        [SerializeField] private float compressionDecayTime = 0.22f;

        // --- runtime ---
        // burnDir is stored in MESH OBJECT SPACE so it stays glued to the same
        // vertices on the body. World-space hit direction is converted to OS at
        // hit time via Quaternion.Inverse(meshTransform.rotation) — that way when
        // the slime walks on a curved surface (body3d rotates) or jumps with any
        // body rotation, the burn stays on the spot it was hit on the body.
        private Vector3 burnDir = Vector3.right;
        private float burnStrength;
        private float lastHitTime = -999f;

        private GameObject shellGO;
        private MeshRenderer shellMR;
        private MeshFilter shellMF;
        private Transform meshTransform;   // the body's actual MeshFilter.transform
        private Material shellMatInstance;
        private MaterialPropertyBlock mpb;

        private static readonly int BurnDirID = Shader.PropertyToID("_BurnDir");
        private static readonly int InflateID = Shader.PropertyToID("_Inflate");

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
            if (body3d == null && slimeVisual != null)
            {
                if (slimeVisual.Body3d != null) body3d = slimeVisual.Body3d;
                else if (slimeVisual.Body != null) body3d = slimeVisual.Body;
            }
            if (burnShellShader == null) burnShellShader = Shader.Find("SlimeJumping/SlimeBurnShell");
            BuildShell();
        }

        void OnEnable()
        {
            if (slimeController != null) slimeController.OnHazardPushed += HandleHazardPushed;
            if (slimeVisual != null && shellMR != null) slimeVisual.RegisterMirroredRenderer(shellMR);
        }

        void OnDisable()
        {
            if (slimeController != null) slimeController.OnHazardPushed -= HandleHazardPushed;
            if (slimeVisual != null)
            {
                if (shellMR != null) slimeVisual.UnregisterMirroredRenderer(shellMR);
                slimeVisual.SetExternalCompression(Vector2.zero);
            }
            currentCompression = Vector2.zero;
            compressionVel = Vector2.zero;
        }

        void OnDestroy()
        {
            if (shellMatInstance != null) Destroy(shellMatInstance);
        }

        // Builds the child shell that mirrors the body mesh's silhouette. The shell
        // shares body3d's mesh asset and is parented under the body MeshFilter, so
        // it inherits all transform-level squash/stretch automatically. (Shader-level
        // vertex displacement on the body — velocity stretch / dip — is NOT mirrored;
        // the shell sees the un-displaced rest mesh, which is fine for a directional
        // burn overlay where exact silhouette match isn't critical.)
        private void BuildShell()
        {
            if (body3d == null) return;
            var bodyMF = body3d.GetComponentInChildren<MeshFilter>();
            if (bodyMF == null || bodyMF.sharedMesh == null) return;
            if (burnShellShader == null) return;

            meshTransform = bodyMF.transform;

            shellGO = new GameObject("BurnShell");
            shellGO.transform.SetParent(meshTransform, false);
            shellGO.transform.localPosition = Vector3.zero;
            shellGO.transform.localRotation = Quaternion.identity;
            shellGO.transform.localScale = Vector3.one;

            shellMF = shellGO.AddComponent<MeshFilter>();
            shellMF.sharedMesh = bodyMF.sharedMesh;

            shellMR = shellGO.AddComponent<MeshRenderer>();
            shellMatInstance = new Material(burnShellShader);
            shellMatInstance.name = "SlimeBurnShell_Instance";
            shellMR.sharedMaterial = shellMatInstance;
            shellMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shellMR.receiveShadows = false;

            mpb = new MaterialPropertyBlock();
        }

        private void HandleHazardPushed(Vector2 deltaV, Vector2 hitPoint)
        {
            // Direction = slime center → hit point in world XY (slime is 2.5D — Z
            // mostly 0). Z component left at 0 so the shader's dot(normal, dir) on
            // a roughly-spherical body picks out the side facing hitPoint.
            Vector2 slimeCenter = slimeController != null ? slimeController.Position : (Vector2)transform.position;
            Vector2 d2 = hitPoint - slimeCenter;
            Vector3 worldHitDir;
            if (d2.sqrMagnitude < 0.0001f) worldHitDir = Vector3.right;
            else worldHitDir = new Vector3(d2.x, d2.y, 0f).normalized;

            // Convert world hit direction → mesh object-space direction so it's
            // anchored to specific vertices on the body. Use Quaternion.Inverse on
            // the mesh transform's world rotation (ignores translation/scale).
            // After this, the body can rotate / move freely and the burn stays on
            // the same triangles — the shader dots OS normals against this OS
            // burnDir, so the result is invariant under transform rotation.
            Vector3 osHitDir;
            if (meshTransform != null)
                osHitDir = Quaternion.Inverse(meshTransform.rotation) * worldHitDir;
            else
                osHitDir = worldHitDir;
            if (osHitDir.sqrMagnitude < 0.0001f) osHitDir = Vector3.right;
            else osHitDir.Normalize();

            // Smooth-blend toward the latest hit direction (in OS) so consecutive
            // hits at similar spots reinforce, while opposite-side hits gradually
            // rotate the burn around the body.
            burnDir = Vector3.Slerp(burnDir.sqrMagnitude > 0.0001f ? burnDir : osHitDir,
                                    osHitDir,
                                    dirRecencyBlend);
            if (burnDir.sqrMagnitude < 0.0001f) burnDir = Vector3.right;
            else burnDir.Normalize();

            burnStrength = Mathf.Min(burnStrength + perHitStrength, maxStrength);
            lastHitTime = Time.time;
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

        // LateUpdate (not Update) so the shell MPB push lands AFTER SlimeVisual's
        // mirror call — SlimeVisual's LateUpdate (default execution order 0) sets
        // all deform values on shellMR via its mirror list, then this component's
        // LateUpdate (DefaultExecutionOrder=100) layers _BurnDir / _Inflate on top
        // via GetPropertyBlock / SetPropertyBlock. The deform values are preserved
        // across the second SetPropertyBlock because GetPropertyBlock returns the
        // renderer's current MPB state.
        void LateUpdate()
        {
            float dt = Time.deltaTime;

            // Recovery — single shared decay across the whole burn after a delay.
            if (Time.time - lastHitTime > recoveryStartDelay && burnStrength > 0f)
            {
                burnStrength = Mathf.Max(0f, burnStrength - recoveryRate * dt);
            }

            PushShellState();
            UpdateCompression(dt);
        }

        private void PushShellState()
        {
            if (shellMR == null || mpb == null) return;
            shellMR.GetPropertyBlock(mpb);
            mpb.SetVector(BurnDirID, new Vector4(burnDir.x, burnDir.y, burnDir.z, burnStrength));
            mpb.SetFloat(InflateID, shellInflate);
            shellMR.SetPropertyBlock(mpb);
            // Disable the renderer entirely when there's no burn to render — saves
            // a draw call when slime is unhit / fully recovered.
            shellMR.enabled = burnStrength > 0.001f;
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
