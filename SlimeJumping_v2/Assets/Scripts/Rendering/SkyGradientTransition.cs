using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Crossfades the skybox across an ordered list of stage materials as the slime crosses world Y lines.
    /// Ramp blends in linear space; runtime works on clones, source materials are never modified.
    public class SkyGradientTransition : MonoBehaviour
    {
        [Serializable]
        private class Stage
        {
            [Tooltip("Skybox for this band")]
            public Material sky;

            [Tooltip("World Y where this sky takes over from the previous entry. Ignored on the first entry")]
            public float triggerY;
        }

        [Header("Refs")]
        [SerializeField] private SlimeController slime;

        [Tooltip("Bottom to top. Entries must be in ascending triggerY order")]
        [SerializeField] private List<Stage> stages = new List<Stage>();

        [Header("Trigger")]
        [Tooltip("Dead band around each line so hovering at a boundary does not flip back and forth")]
        [SerializeField] private float triggerHysteresis = 4f;

        [Header("Fade")]
        [Tooltip("Seconds for a full sky change. Reversing partway takes proportionally less")]
        [SerializeField] private float fadeDuration = 6f;

        [Tooltip("Maps elapsed progress to sky blend. Raise the early part to shift the change earlier")]
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Seconds between ambient probe refreshes while fading. Higher = cheaper")]
        [SerializeField] private float ambientRefreshInterval = 0.25f;

        [Tooltip("Half-width of the trigger gizmo lines")]
        [SerializeField] private float gizmoHalfWidth = 120f;

        static readonly int GradientId = Shader.PropertyToID("_Gradient");

        private Material runtimeSky;
        private Material originalSky;
        private Texture2D rampTex;
        private Color[][] stageLinear;
        private float[][] stageScalars;
        private Color[] work;
        private int[] scalarIds;
        private float blend;
        private int target;
        private float ambientTimer;

        void OnEnable()
        {
            if (!Build())
            {
                enabled = false;
                return;
            }
            // transform, not slime.Position: OnEnable can run before SlimeController.Awake creates its Rigidbody2D
            float y = slime != null ? slime.transform.position.y : 0f;
            target = 0;
            for (int i = 1; i < stages.Count; i++)
                if (y >= stages[i].triggerY) target = i;
            blend = target;
            ApplyBlend();
            DynamicGI.UpdateEnvironment();
        }

        void Update()
        {
            if (slime != null)
            {
                float half = triggerHysteresis * 0.5f;
                float y = slime.Position.y;
                while (target < stages.Count - 1 && y > stages[target + 1].triggerY + half) target++;
                while (target > 0 && y < stages[target].triggerY - half) target--;
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
            int i0 = Mathf.Min(Mathf.FloorToInt(blend), stages.Count - 2);
            int i1 = i0 + 1;
            float k = Mathf.Clamp01(fadeCurve.Evaluate(blend - i0));

            var from = stageLinear[i0];
            var to = stageLinear[i1];
            for (int i = 0; i < work.Length; i++)
                work[i] = Color.Lerp(from[i], to[i], k).gamma;
            rampTex.SetPixels(work);
            rampTex.Apply(false);

            for (int i = 0; i < scalarIds.Length; i++)
                runtimeSky.SetFloat(scalarIds[i], Mathf.Lerp(stageScalars[i0][i], stageScalars[i1][i], k));
        }

        bool Build()
        {
            if (stages.Count < 2)
            {
                Debug.LogWarning("SkyGradientTransition: needs at least 2 stages", this);
                return false;
            }

            var firstSky = stages[0].sky;
            if (firstSky == null)
            {
                Debug.LogWarning("SkyGradientTransition: sky material not assigned", this);
                return false;
            }
            var firstRamp = firstSky.GetTexture(GradientId) as Texture2D;
            if (firstRamp == null)
            {
                Debug.LogWarning("SkyGradientTransition: gradient ramp missing", this);
                return false;
            }

            var shader = firstSky.shader;
            var ids = new List<int>();
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var type = shader.GetPropertyType(i);
                if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range) continue;
                ids.Add(shader.GetPropertyNameId(i));
            }
            scalarIds = ids.ToArray();

            stageLinear = new Color[stages.Count][];
            stageScalars = new float[stages.Count][];
            for (int s = 0; s < stages.Count; s++)
            {
                var sky = stages[s].sky;
                if (sky == null || sky.shader != shader)
                {
                    Debug.LogWarning($"SkyGradientTransition: stage {s} sky missing or shader mismatch", this);
                    return false;
                }
                var ramp = sky.GetTexture(GradientId) as Texture2D;
                if (ramp == null || ramp.width != firstRamp.width)
                {
                    Debug.LogWarning($"SkyGradientTransition: stage {s} gradient ramp missing or width mismatch", this);
                    return false;
                }

                var px = ramp.GetPixels();
                var linear = new Color[px.Length];
                for (int i = 0; i < px.Length; i++)
                    linear[i] = px[i].linear;
                stageLinear[s] = linear;

                var scalars = new float[scalarIds.Length];
                for (int i = 0; i < scalarIds.Length; i++)
                    scalars[i] = sky.GetFloat(scalarIds[i]);
                stageScalars[s] = scalars;
            }
            work = new Color[stageLinear[0].Length];

            rampTex = Instantiate(firstRamp);
            originalSky = RenderSettings.skybox;
            runtimeSky = new Material(firstSky);
            runtimeSky.SetTexture(GradientId, rampTex);
            RenderSettings.skybox = runtimeSky;
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
            for (int i = 1; i < stages.Count; i++)
            {
                var center = new Vector3(transform.position.x, stages[i].triggerY, 0f);
                Gizmos.DrawLine(center + Vector3.left * gizmoHalfWidth, center + Vector3.right * gizmoHalfWidth);
            }
        }
    }
}
