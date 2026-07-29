using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Crossfades the skybox between two stage materials as the slime crosses a world Y line.
    /// Ramp blends in linear space; runtime works on clones, source materials are never modified.
    public class SkyGradientTransition : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private SlimeController slime;

        [Tooltip("Skybox below the line (stage 2)")]
        [SerializeField] private Material fromSky;

        [Tooltip("Skybox above the line (stage 3)")]
        [SerializeField] private Material toSky;

        [Header("Trigger")]
        [Tooltip("World Y the slime crosses. Red gizmo line when selected")]
        [SerializeField] private float triggerY = 260f;

        [Tooltip("Dead band around the line so hovering at the boundary does not flip back and forth")]
        [SerializeField] private float triggerHysteresis = 4f;

        [Header("Fade")]
        [Tooltip("Seconds for a full sky change. Reversing partway takes proportionally less")]
        [SerializeField] private float fadeDuration = 6f;

        [Tooltip("Maps elapsed progress to sky blend. Raise the early part to shift the change earlier")]
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Seconds between ambient probe refreshes while fading. Higher = cheaper")]
        [SerializeField] private float ambientRefreshInterval = 0.25f;

        [Tooltip("Half-width of the trigger gizmo line")]
        [SerializeField] private float gizmoHalfWidth = 120f;

        static readonly int GradientId = Shader.PropertyToID("_Gradient");

        private Material runtimeSky;
        private Material originalSky;
        private Texture2D rampTex;
        private Color[] fromLinear;
        private Color[] toLinear;
        private Color[] work;
        private int[] scalarIds;
        private float[] fromScalars;
        private float[] toScalars;
        private float blend;
        private float target;
        private float ambientTimer;

        void OnEnable()
        {
            if (!Build())
            {
                enabled = false;
                return;
            }
            target = blend = (slime != null && slime.Position.y >= triggerY) ? 1f : 0f;
            ApplyBlend();
            DynamicGI.UpdateEnvironment();
        }

        void Update()
        {
            if (slime != null)
            {
                float half = triggerHysteresis * 0.5f;
                float y = slime.Position.y;
                if (y > triggerY + half) target = 1f;
                else if (y < triggerY - half) target = 0f;
            }

            if (blend == target) return;

            float step = fadeDuration > 0f ? Time.deltaTime / fadeDuration : 1f;
            blend = Mathf.MoveTowards(blend, target, step);
            ApplyBlend();

            ambientTimer += Time.deltaTime;
            if (ambientTimer >= ambientRefreshInterval || blend == target)
            {
                DynamicGI.UpdateEnvironment();
                ambientTimer = 0f;
            }
        }

        void ApplyBlend()
        {
            float k = Mathf.Clamp01(fadeCurve.Evaluate(blend));

            for (int i = 0; i < work.Length; i++)
                work[i] = Color.Lerp(fromLinear[i], toLinear[i], k).gamma;
            rampTex.SetPixels(work);
            rampTex.Apply(false);

            for (int i = 0; i < scalarIds.Length; i++)
                runtimeSky.SetFloat(scalarIds[i], Mathf.Lerp(fromScalars[i], toScalars[i], k));
        }

        bool Build()
        {
            if (fromSky == null || toSky == null)
            {
                Debug.LogWarning("SkyGradientTransition: sky material not assigned", this);
                return false;
            }

            var fromRamp = fromSky.GetTexture(GradientId) as Texture2D;
            var toRamp = toSky.GetTexture(GradientId) as Texture2D;
            if (fromRamp == null || toRamp == null || fromRamp.width != toRamp.width)
            {
                Debug.LogWarning("SkyGradientTransition: gradient ramp missing or width mismatch", this);
                return false;
            }

            var a = fromRamp.GetPixels();
            var b = toRamp.GetPixels();
            fromLinear = new Color[a.Length];
            toLinear = new Color[a.Length];
            work = new Color[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                fromLinear[i] = a[i].linear;
                toLinear[i] = b[i].linear;
            }

            rampTex = Instantiate(fromRamp);
            originalSky = RenderSettings.skybox;
            runtimeSky = new Material(fromSky);
            runtimeSky.SetTexture(GradientId, rampTex);
            RenderSettings.skybox = runtimeSky;

            var shader = fromSky.shader;
            var ids = new List<int>();
            var fromValues = new List<float>();
            var toValues = new List<float>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var type = shader.GetPropertyType(i);
                if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range) continue;
                int id = shader.GetPropertyNameId(i);
                ids.Add(id);
                fromValues.Add(fromSky.GetFloat(id));
                toValues.Add(toSky.GetFloat(id));
            }
            scalarIds = ids.ToArray();
            fromScalars = fromValues.ToArray();
            toScalars = toValues.ToArray();
            return true;
        }

        void OnDestroy()
        {
            if (originalSky != null) RenderSettings.skybox = originalSky;
            if (runtimeSky != null) Destroy(runtimeSky);
            if (rampTex != null) Destroy(rampTex);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            var center = new Vector3(transform.position.x, triggerY, 0f);
            Gizmos.DrawLine(center + Vector3.left * gizmoHalfWidth, center + Vector3.right * gizmoHalfWidth);
        }
    }
}
