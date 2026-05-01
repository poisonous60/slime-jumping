using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Visualises the snow surface-tag detection state. Drop on any GameObject
    /// in the scene, point <c>slimeController</c> at the slime, and the gizmo
    /// shows whether the slime's contact point registers as "on snow" — the
    /// same two-stage probe SlimeController.RefreshSnowBlend uses every FU.
    ///
    /// Probe = slope-normal primary gate (surfaceNormal.y ≥ cos(maxSnowSlopeDeg))
    /// + mesh-local nearest-vertex hit-test against heightRest > 0.
    ///
    /// Reads only public APIs on SlimeController / SnowField / SnowFieldRegistry.
    ///
    ///  • Each SnowField patch's vertices drawn in world space, coloured by
    ///    whether the vertex carries snow (heightRest > 0) or is in a bare /
    ///    tapered region (heightRest = 0).
    ///  • Probe glyph at the slime: outer = slime body radius, dot = contact
    ///    sample (rb.position - surfaceNormal * slimeRadius — the exact point
    ///    fed into the probe).
    ///  • Colour code:
    ///      RED    — controller has no surface (airborne / detached).
    ///      YELLOW — surface attached but the host collider isn't a SnowField,
    ///               OR the probe returned false (off-patch / steep).
    ///      GREEN  — probe returned true. Slime reads as on-snow this frame;
    ///               ActiveSurfaceMaterial == snow material.
    /// </summary>
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Plow Debug")]
    public sealed class SnowPlowDebug : MonoBehaviour
    {
        [Header("Refs")]
        [Tooltip("The slime's SlimeController. Position / SurfaceNormal / SlimeRadius / SurfaceCollider are pulled from here so the probe matches what RefreshSnowBlend tests.")]
        public SlimeController slimeController;

        [Tooltip("Optional fallback when slimeController is unset — auto-resolves via GetComponent on this transform.")]
        public Transform slimeTransform;

        [Header("Display")]
        public bool drawPatchVerts = true;
        public bool drawProbe = true;
        public bool drawStatusLabel = true;

        [Header("Status (read-only — populated each gizmo tick)")]
        [SerializeField] string status = "—";
        [SerializeField] string fieldHit = "—";
        [SerializeField] string activeSurfaceMaterial = "—";
        [SerializeField] bool registersAsSnow = false;
        [Tooltip("Surface normal y-component vs cos(maxSnowSlopeDeg). When normalY < cosMax the primary gate rejects regardless of xy footprint.")]
        [SerializeField] string slopeGate = "—";

        SlimeController ResolveController()
        {
            if (slimeController != null) return slimeController;
            if (slimeTransform != null) return slimeTransform.GetComponent<SlimeController>();
            return null;
        }

        void OnDrawGizmos()
        {
            if (drawPatchVerts) DrawAllPatchVerts();

            var ctrl = ResolveController();
            if (ctrl == null)
            {
                status = "no SlimeController";
                return;
            }

            // Same sample math RefreshSnowBlend uses.
            Vector2 slimePos = ctrl.Position;
            bool touching = ctrl.IsTouchingSurface;
            Vector2 normal = ctrl.SurfaceNormal;
            Vector2 sample = touching
                ? slimePos - normal * ctrl.SlimeRadius
                : slimePos;

            // Same field lookup RefreshSnowBlend uses (registry by SurfaceCollider).
            SnowField field = null;
            var col = ctrl.SurfaceCollider;
            if (col != null) SnowFieldRegistry.TryGet(col, out field);

            // Reproduce the two-stage probe locally so the debug never depends on
            // SlimeController's per-FU cache (which only holds for the FU window
            // RefreshSnowBlend writes it).
            bool slopePass = false;
            float cosMax = 0f;
            if (field != null)
            {
                cosMax = Mathf.Cos(field.MaxSnowSlopeDeg * Mathf.Deg2Rad);
                slopePass = touching && normal.y >= cosMax;
            }
            slopeGate = field == null
                ? "—"
                : (slopePass
                    ? $"PASS  normal.y={normal.y:F2} ≥ cosMax={cosMax:F2}"
                    : $"FAIL  normal.y={normal.y:F2} < cosMax={cosMax:F2}");

            registersAsSnow = false;
            SnowStrip hitStrip = null;
            int hitVertIdx = -1;
            if (field != null && slopePass)
                registersAsSnow = field.TryFindFootprintHit(sample, out hitStrip, out hitVertIdx);

            fieldHit = field != null ? field.name : "—";
            var mat = ctrl.ActiveSurfaceMaterial;
            activeSurfaceMaterial = mat != null ? mat.name : "—";

            Color glyph;
            if (!touching)
            {
                status = "OUT (airborne)";
                glyph = Color.red;
            }
            else if (col == null)
            {
                status = "OUT (no SurfaceCollider)";
                glyph = Color.red;
            }
            else if (field == null)
            {
                status = "EDGE (host=" + col.name + " — not a SnowField)";
                glyph = Color.yellow;
            }
            else if (!slopePass)
            {
                status = "EDGE (field=" + field.name + " — slope gate failed)";
                glyph = Color.yellow;
            }
            else if (!registersAsSnow)
            {
                status = "EDGE (field=" + field.name + " — nearest vert not on snow)";
                glyph = Color.yellow;
            }
            else
            {
                status = "IN (field=" + field.name + ", tag=" + activeSurfaceMaterial + ")";
                glyph = Color.green;
            }

            if (drawProbe)
            {
                Gizmos.color = glyph;
                Gizmos.DrawWireSphere(slimePos, ctrl.SlimeRadius);
                // Sample point — the exact xy fed into the probe.
                Gizmos.color = Color.magenta;
                Gizmos.DrawSphere(sample, 0.12f);
                Gizmos.color = new Color(1f, 0f, 1f, 0.6f);
                Gizmos.DrawLine(slimePos, sample);

                // If we got a hit, draw a line from the sample to the matched
                // patch vertex so the user can see which vert decided the result.
                if (registersAsSnow && hitStrip != null && hitVertIdx >= 0
                    && hitStrip.verts != null && hitVertIdx < hitStrip.verts.Length
                    && field != null && field.sourceMeshFilter != null)
                {
                    Vector3 vWorld = field.sourceMeshFilter.transform.TransformPoint(
                        hitStrip.verts[hitVertIdx]);
                    Gizmos.color = Color.green;
                    Gizmos.DrawLine(sample, vWorld);
                    Gizmos.DrawSphere(vWorld, 0.06f);
                }
            }

#if UNITY_EDITOR
            if (drawStatusLabel)
            {
                var style = new GUIStyle(UnityEditor.EditorStyles.whiteBoldLabel)
                {
                    normal = { textColor = glyph }
                };
                UnityEditor.Handles.Label((Vector3)slimePos + Vector3.up * (ctrl.SlimeRadius + 0.15f), status, style);
            }
#endif
        }

        // Draw each SnowField's strip vertices in world space, colour-coded by
        // heightRest. Snow-bearing verts (heightRest > 0) draw cyan; bare verts
        // (heightRest = 0, i.e. boundary taper or future region-masked verts)
        // draw dark grey. The probe matches "nearest vert with heightRest > 0",
        // so this is exactly the data it tests against.
        void DrawAllPatchVerts()
        {
#if UNITY_2023_1_OR_NEWER
            var fields = Object.FindObjectsByType<SnowField>(FindObjectsSortMode.None);
#else
            var fields = Object.FindObjectsOfType<SnowField>();
#endif
            Color snowVert = new Color(0.4f, 0.9f, 1f, 0.95f);
            Color bareVert = new Color(0.35f, 0.35f, 0.35f, 0.8f);

            for (int f = 0; f < fields.Length; f++)
            {
                var field = fields[f];
                if (field == null || field.sourceMeshFilter == null) continue;
                var meshT = field.sourceMeshFilter.transform;
                int childCount = field.transform.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    var child = field.transform.GetChild(i);
                    if (child == null) continue;
                    var strip = child.GetComponent<SnowStrip>();
                    if (strip == null || strip.verts == null || strip.heightRest == null) continue;

                    int n = Mathf.Min(strip.verts.Length, strip.heightRest.Length);
                    for (int v = 0; v < n; v++)
                    {
                        Vector3 w = meshT.TransformPoint(strip.verts[v]);
                        bool hasSnow = strip.heightRest[v] > 1e-5f;
                        Gizmos.color = hasSnow ? snowVert : bareVert;
                        Gizmos.DrawSphere(w, hasSnow ? 0.04f : 0.025f);
                    }
                }
            }
        }
    }
}
