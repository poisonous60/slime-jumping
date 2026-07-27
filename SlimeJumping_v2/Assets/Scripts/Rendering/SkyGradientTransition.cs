using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Crossfades the skybox gradient ramp from one stage material to another once the slime
    /// rises past a world Y line. Source materials are never modified.
    public class SkyGradientTransition : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private SlimeController slime;

        [Tooltip("Skybox below the line (stage 2)")]
        [SerializeField] private Material fromSky;

        [Tooltip("Skybox above the line (stage 3)")]
        [SerializeField] private Material toSky;

        [Header("Trigger")]
        [Tooltip("World Y the slime must rise past. Red gizmo line when selected")]
        [SerializeField] private float triggerY = 40f;

        [Header("Fade")]
        [Tooltip("Seconds to reach the new sky palette")]
        [SerializeField] private float fadeDuration = 6f;

        [Tooltip("Shapes the fade over time. Linear = steady drift")]
        [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Seconds between ambient probe refreshes while fading. Higher = cheaper")]
        [SerializeField] private float ambientRefreshInterval = 0.25f;

        [Tooltip("Half-width of the trigger gizmo line")]
        [SerializeField] private float gizmoHalfWidth = 60f;

        static readonly int GradientId = Shader.PropertyToID("_Gradient");

        private Material runtimeSky;
        private Texture2D blendRamp;
        private bool triggered;

        void Update()
        {
            if (triggered || slime == null || slime.Position.y < triggerY) return;
            triggered = true;
            StartCoroutine(Fade());
        }

        IEnumerator Fade()
        {
            var fromRamp = fromSky.GetTexture(GradientId) as Texture2D;
            var toRamp = toSky.GetTexture(GradientId) as Texture2D;
            if (fromRamp == null || toRamp == null || fromRamp.width != toRamp.width)
            {
                Debug.LogWarning("SkyGradientTransition: gradient ramp missing or width mismatch", this);
                yield break;
            }

            var a = fromRamp.GetPixels32();
            var b = toRamp.GetPixels32();
            var blended = new Color32[a.Length];

            blendRamp = new Texture2D(fromRamp.width, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = fromRamp.filterMode
            };
            runtimeSky = new Material(fromSky);
            runtimeSky.SetTexture(GradientId, blendRamp);
            RenderSettings.skybox = runtimeSky;

            float elapsed = 0f;
            float nextAmbient = 0f;
            while (elapsed < fadeDuration)
            {
                float k = fadeCurve.Evaluate(elapsed / fadeDuration);
                for (int i = 0; i < a.Length; i++) blended[i] = Color32.Lerp(a[i], b[i], k);
                blendRamp.SetPixels32(blended);
                blendRamp.Apply(false);

                if (elapsed >= nextAmbient)
                {
                    DynamicGI.UpdateEnvironment();
                    nextAmbient = elapsed + ambientRefreshInterval;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            RenderSettings.skybox = toSky;
            DynamicGI.UpdateEnvironment();
        }

        void OnDestroy()
        {
            if (runtimeSky != null) Destroy(runtimeSky);
            if (blendRamp != null) Destroy(blendRamp);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            var center = new Vector3(transform.position.x, triggerY, 0f);
            Gizmos.DrawLine(center + Vector3.left * gizmoHalfWidth, center + Vector3.right * gizmoHalfWidth);
        }
    }
}
