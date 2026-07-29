using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Trigger volume that pushes the slime downstream and renders a centerline-following ribbon.
    /// Partials: .cs (push physics, lifecycle) / .Shape (centerline, collider sync) /
    /// .Mesh (ribbon build) / .Simulation (spring wake) / .Interaction (slime impulse).
    /// The mesh UV V-axis follows the centerline so the shader's _FlowSpeed scrolls the same
    /// direction the physics push goes.
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
        // Separate from `trackedSlime != null`: trigger callback order (Enter → Disable → Exit a
        // tick later) desyncs the two, and the override would never get popped.
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
            // Disabling mid-overlap would otherwise leave the slime permanently in water modifiers.
            if (overridePushed && trackedSlime != null && waterSurfaceMaterial != null)
                trackedSlime.PopSurfaceMaterialOverride(waterSurfaceMaterial);
            overridePushed = false;
            trackedSlime = null;
        }

        void OnValidate()
        {
            if (flowDirection.sqrMagnitude < 0.0001f) flowDirection = Vector2.down;
            else flowDirection = flowDirection.normalized;
            // Rebuild deferred to delayCall: touching scene state synchronously in OnValidate
            // trips Unity's "SendMessage cannot be called during..." warning.
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
                        // Not AddExternalVelocity: only the sustained path compounds into
                        // slide/crawl velocity, so Attached/Charging accel matches Airborne.
                        trackedSlime.AddSustainedFlowImpulse(dir * (flowAccel * falloff * dt));
                    }
                }

                // Wake fires regardless of the push state filter — a stunned slime still displaces water.
                ApplySlimeImpulse(trackedSlime, dt, isEntry: false);
            }

            // Spring sim runs every fixed step so wakes decay even while no slime is inside.
            SimulateWaves(dt);
        }

        void LateUpdate()
        {
            // Rendering cadence, not FixedUpdate, so the ribbon stays aligned with the camera frame.
            if (Application.isPlaying)
                UpdateMeshVerts();
        }

        /// <summary>
        /// Push direction: Attached/Charging ride surfaceTangent downhill (fed pre-projected —
        /// the controller discards any normal component), else world-down. Zero on a flat
        /// surface, which skips the push.
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
            // Zero noiseScale collapses shader noise sampling to one point (flat color); some
            // saved scenes hold (0,0), so fall back to the material default.
            if (noiseScale.sqrMagnitude > 0.0001f)
                mpb.SetVector(NoiseScaleID, new Vector4(noiseScale.x, noiseScale.y, 0f, 0f));
            else
                mpb.SetVector(NoiseScaleID, new Vector4(1.5f, 4f, 0f, 0f));
            meshRenderer.SetPropertyBlock(mpb);
        }

        // --- Gizmos ---

        void OnDrawGizmosSelected()
        {
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
