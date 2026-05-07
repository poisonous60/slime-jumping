using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Trigger volume that pushes the slime "downstream" along a designer-set flow direction
    /// while the slime is inside, AND renders a smooth centerline-following ribbon with a
    /// half-arc Z cross-section. The ribbon reacts to slime motion via a spring sim that
    /// produces a wake on the slime's side.
    ///
    /// Implementation is split across partials:
    ///   - WaterCurrentZone.cs           — push physics, state filter, lifecycle (this file)
    ///   - WaterCurrentZone.Shape.cs     — centerline storage, subdivision, collider sync, migration
    ///   - WaterCurrentZone.Mesh.cs      — 5-vert-cross-section ribbon mesh build + per-frame update
    ///   - WaterCurrentZone.Simulation.cs — two-pass spring sim with L/R channel separation
    ///   - WaterCurrentZone.Interaction.cs — slime impulse projection and dispatch
    ///
    /// Push physics is unchanged from the legacy cube-mesh version: state-dependent direction
    /// resolution (Attached/Charging ride the slime's SurfaceTangent down-component;
    /// Airborne/Stunned use world-down), terminal-speed falloff, AddSustainedFlowImpulse.
    ///
    /// The ribbon mesh's UV V-axis aligns with the centerline (downstream = larger arc length),
    /// so the Water_FlowingRefraction shader's _FlowSpeed scrolls noise in the same direction
    /// the physics push goes.
    /// </summary>
    [RequireComponent(typeof(PolygonCollider2D))]
    [ExecuteAlways]
    public partial class WaterCurrentZone : MonoBehaviour
    {
        [Header("Push")]
        [Tooltip("Flow direction in this object's local space. Auto-normalized in OnValidate. " +
                 "Independent from the centerline geometry — physics push direction for " +
                 "Airborne/Stunned states uses this; the ribbon's downstream UV axis follows the " +
                 "centerline curve. Gizmo shows it as a blue arrow.")]
        [SerializeField] Vector2 flowDirection = new Vector2(0f, -1f);
        [Tooltip("Acceleration toward flow direction (units/s²). Treated like ConveyorBelt's accel.")]
        [Min(0f)] [SerializeField] float flowAccel = 8f;
        [Tooltip("Velocity cap along flow direction. As slime velocity along flow approaches " +
                 "this, accel falls off to 0. Set higher than the slime's max crawl speed to make " +
                 "the wall un-climbable; lower to let strong crawl push back.")]
        [Min(0f)] [SerializeField] float flowTerminalSpeed = 6f;

        [Header("State Filter")]
        [SerializeField] bool affectAttached = true;
        [SerializeField] bool affectCharging = true;
        [SerializeField] bool affectAirborne = true;
        [SerializeField] bool affectStunned  = true;

        [Header("Surface Override")]
        [Tooltip("SurfaceMaterial pushed onto the slime while inside the zone. Mirrors snow's " +
                 "surfaceMod swap: the slime stays Attached but uses these modifiers (low " +
                 "stickDuration / no friction → slips off the wall behind the water). Leave " +
                 "empty to disable the override entirely. Last-in-wins for overlapping zones; " +
                 "takes priority over snow blend.")]
        [SerializeField] SurfaceMaterial waterSurfaceMaterial;

        [Header("Visual")]
        [Tooltip("Material with Water_FlowingRefraction shader. _FlowSpeed/_NoiseScale are pushed " +
                 "per-instance via MaterialPropertyBlock so multiple zones can share one material " +
                 "asset and still have different visual flow rates.")]
        [SerializeField] Material flowMaterial;
        [Tooltip("Centroid color (deep tint). Vertex colors are baked center→edge with alpha=0 at " +
                 "the arc apex (no rim contribution) and alpha=1 at the silhouette edges (full rim).")]
        [SerializeField] Color centerColor = new Color(0.05f, 0.25f, 0.55f, 1f);
        [Tooltip("Silhouette edge color (rim tint). The shader currently reads only color.a; RGB is " +
                 "preserved for future shader variants that may want a center→edge gradient.")]
        [SerializeField] Color edgeColor   = new Color(0.45f, 0.85f, 1.00f, 1f);
        [Tooltip("Shader UV-space noise scroll speed (uv/sec). Independent from physics flow " +
                 "magnitudes. Pushed via MaterialPropertyBlock.")]
        [SerializeField] float visualFlowSpeed = 1.5f;
        [Tooltip("Shader noise tiling. Higher = denser ripples per local-unit. Pushed via MPB.")]
        [SerializeField] Vector2 noiseScale = new Vector2(1.5f, 4f);

        const string MeshChildName = "FlowMesh";
        const string RibbonMeshName = "WaterCurrentZone_RibbonMesh";
        static readonly int FlowSpeedID  = Shader.PropertyToID("_FlowSpeed");
        static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");

        SlimeController trackedSlime;
        // True when we've pushed waterSurfaceMaterial onto trackedSlime's override stack and
        // still owe it a Pop. Tracked separately from `trackedSlime != null` because the
        // trigger-callback ordering (Enter, then Disable, then Exit on next physics tick) can
        // desync the two.
        bool overridePushed;
        PolygonCollider2D polyCollider;
        Transform meshChildTr;
        MeshFilter meshFilter;
        MeshRenderer meshRenderer;
        MaterialPropertyBlock mpb;

        void Reset()
        {
            var col = GetComponent<PolygonCollider2D>();
            if (col != null) col.isTrigger = true;
        }

        void OnEnable()
        {
            EnsureMeshChild();
            TryMigrateFromPolygon();
            BuildShape();
            SyncColliderToRibbonOutline();
            BuildRibbonMesh();
            ApplyMaterialProperties();
        }

        void OnDisable()
        {
            // Pop any outstanding surface override so the slime isn't permanently locked into
            // water modifiers when the zone is disabled mid-overlap (scene unload, designer
            // toggling enable, etc).
            if (overridePushed && trackedSlime != null && waterSurfaceMaterial != null)
                trackedSlime.PopSurfaceMaterialOverride(waterSurfaceMaterial);
            overridePushed = false;
            trackedSlime = null;
        }

        void OnValidate()
        {
            if (flowDirection.sqrMagnitude < 0.0001f) flowDirection = Vector2.down;
            else flowDirection = flowDirection.normalized;
            // Editor-time rebuild deferred to delayCall — Unity warns when OnValidate touches
            // scene state synchronously (Mesh.colors etc. sometimes triggers
            // "SendMessage cannot be called during").
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                EnsureMeshChild();
                TryMigrateFromPolygon();
                BuildShape();
                SyncColliderToRibbonOutline();
                BuildRibbonMesh();
                ApplyMaterialProperties();
            };
#endif
        }

        [ContextMenu("Rebuild Ribbon")]
        public void Rebuild()
        {
            EnsureMeshChild();
            TryMigrateFromPolygon();
            BuildShape();
            SyncColliderToRibbonOutline();
            BuildRibbonMesh();
            ApplyMaterialProperties();
        }

        // --- Trigger tracking (runtime only) ---

        void OnTriggerEnter2D(Collider2D other)
        {
            var s = other.GetComponentInParent<SlimeController>();
            if (s == null) return;
            trackedSlime = s;
            if (Application.isPlaying)
            {
                if (waterSurfaceMaterial != null && !overridePushed)
                {
                    s.PushSurfaceMaterialOverride(waterSurfaceMaterial);
                    overridePushed = true;
                }
                ApplySlimeImpulse(trackedSlime, Time.fixedDeltaTime, isEntry: true);
            }
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var s = other.GetComponentInParent<SlimeController>();
            if (s == null || s != trackedSlime) return;
            if (overridePushed && waterSurfaceMaterial != null)
            {
                s.PopSurfaceMaterialOverride(waterSurfaceMaterial);
                overridePushed = false;
            }
            trackedSlime = null;
        }

        void FixedUpdate()
        {
            if (!Application.isPlaying) return;

            float dt = Time.fixedDeltaTime;

            // Push physics (state-filtered).
            if (trackedSlime != null)
            {
                bool active;
                switch (trackedSlime.CurrentState)
                {
                    case SlimeController.SlimeState.Attached: active = affectAttached; break;
                    case SlimeController.SlimeState.Charging: active = affectCharging; break;
                    case SlimeController.SlimeState.Airborne: active = affectAirborne; break;
                    case SlimeController.SlimeState.Stunned:  active = affectStunned;  break;
                    default: active = false; break;
                }

                if (active)
                {
                    Vector2 dir = ResolvePushDirection(trackedSlime);
                    if (dir.sqrMagnitude > 0.0001f)
                    {
                        float falloff = 1f;
                        if (flowTerminalSpeed > 0f)
                        {
                            float along = Vector2.Dot(trackedSlime.Velocity, dir);
                            if (along > 0f)
                                falloff = Mathf.Clamp01(1f - along / flowTerminalSpeed);
                        }
                        // AddSustainedFlowImpulse (not AddExternalVelocity) so the push
                        // integrates into slideVelocity/crawlVelocity and compounds across
                        // frames in Attached/Charging — same effective accel as in Airborne.
                        // See SlimeController.AddSustainedFlowImpulse for the asymmetry rationale.
                        trackedSlime.AddSustainedFlowImpulse(dir * (flowAccel * falloff * dt));
                    }
                }

                // Wake impulse fires regardless of push state filter — even a stunned slime
                // displaces water.
                ApplySlimeImpulse(trackedSlime, dt, isEntry: false);
            }

            // Spring sim runs every fixed step so wakes decay even while no slime is inside.
            SimulateWaves(dt);
        }

        void LateUpdate()
        {
            // Mesh visuals update on the rendering cadence (LateUpdate) rather than FixedUpdate
            // to stay aligned with the camera frame. Cheap — only positions are pushed.
            if (Application.isPlaying)
                UpdateMeshVerts();
        }

        /// <summary>
        /// State-dependent push direction. Attached/Charging ride the wall's surfaceTangent in
        /// the gravity-down sign (the SlimeController's externalDeltaV consumer projects against
        /// surfaceTangent anyway, so any normal component would be discarded — we feed it
        /// pre-projected to preserve full magnitude). Airborne/Stunned have no meaningful
        /// surface, so we push world-down.
        ///
        /// Returns Vector2.zero when the surface is essentially horizontal (no down-along-surface
        /// direction): the water has nowhere to flow, so we skip the push entirely.
        /// </summary>
        Vector2 ResolvePushDirection(SlimeController slime)
        {
            switch (slime.CurrentState)
            {
                case SlimeController.SlimeState.Attached:
                case SlimeController.SlimeState.Charging:
                {
                    Vector2 t = slime.SurfaceTangent;
                    if (t.sqrMagnitude < 0.0001f) return Vector2.zero;
                    float dotUp = Vector2.Dot(t, Vector2.up);
                    if (Mathf.Abs(dotUp) < 0.001f) return Vector2.zero;
                    float sign = -Mathf.Sign(dotUp);
                    return t * sign;
                }
                default:
                    return Vector2.down;
            }
        }

        // --- Mesh child setup ---

        void EnsureMeshChild()
        {
            if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();
            if (polyCollider != null && !polyCollider.isTrigger)
                polyCollider.isTrigger = true;

            if (meshChildTr == null)
            {
                var existing = transform.Find(MeshChildName);
                if (existing != null)
                {
                    meshChildTr = existing;
                }
                else
                {
                    var go = new GameObject(MeshChildName);
                    go.transform.SetParent(transform, false);
                    go.layer = gameObject.layer;
                    meshChildTr = go.transform;
                }
            }

            meshFilter = meshChildTr.GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = meshChildTr.gameObject.AddComponent<MeshFilter>();
            meshRenderer = meshChildTr.GetComponent<MeshRenderer>();
            if (meshRenderer == null) meshRenderer = meshChildTr.gameObject.AddComponent<MeshRenderer>();

            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (flowMaterial != null && meshRenderer.sharedMaterial != flowMaterial)
                meshRenderer.sharedMaterial = flowMaterial;
        }

        void ApplyMaterialProperties()
        {
            if (meshRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            meshRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(FlowSpeedID, visualFlowSpeed);
            // noiseScale guard: zero/near-zero collapses shader noise sampling to a single point
            // (uniform output → flat color). Older scenes saved (0,0) by accident; treat that as
            // "use material default" by falling back to (1.5, 4).
            if (noiseScale.sqrMagnitude > 0.0001f)
                mpb.SetVector(NoiseScaleID, new Vector4(noiseScale.x, noiseScale.y, 0f, 0f));
            else
                mpb.SetVector(NoiseScaleID, new Vector4(1.5f, 4f, 0f, 0f));
            meshRenderer.SetPropertyBlock(mpb);
        }

        // --- Gizmos ---

        void OnDrawGizmosSelected()
        {
            // Centerline preview in cyan.
            if (CenterlinePointCount >= 2)
            {
                Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
                int n = CenterlinePointCount;
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 a = transform.TransformPoint(GetCenterlinePointLocal(i));
                    Vector3 b = transform.TransformPoint(GetCenterlinePointLocal(i + 1));
                    Gizmos.DrawLine(a, b);
                }
            }

            // Flow direction arrow at GameObject origin.
            Vector3 originWS = transform.position;
            Vector2 dirWorld = transform.TransformDirection((Vector3)flowDirection.normalized);
            float arrowLen = 1.5f;
            Vector3 tip = originWS + (Vector3)(dirWorld * arrowLen);

            Gizmos.color = new Color(0.4f, 0.85f, 1f, 0.9f);
            Gizmos.DrawLine(originWS, tip);

            Vector3 perp = Vector3.Cross((Vector3)dirWorld, Vector3.forward).normalized * 0.18f;
            Vector3 backTip = tip - (Vector3)(dirWorld * 0.3f);
            Gizmos.DrawLine(tip, backTip + perp);
            Gizmos.DrawLine(tip, backTip - perp);
        }
    }
}
