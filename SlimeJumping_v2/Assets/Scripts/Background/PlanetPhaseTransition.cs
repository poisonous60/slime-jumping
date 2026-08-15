using System.Collections;
using UnityEngine;

namespace SlimeJumping.Background
{
    /// Blends the background planet from its authored look (the renderers' sharedMaterial)
    /// to a target material of the same shader — every float/color property is lerped into a
    /// MaterialPropertyBlock, so neither material asset is touched. Textures are not lerped:
    /// the target's textures are bound outright, and the source material is expected to keep
    /// them invisible at t=0 (e.g. _SurfaceStrength 0). The editor preview slider scrubs the
    /// whole transition without entering Play mode.
    public class PlanetPhaseTransition : MonoBehaviour
    {
        [SerializeField] private Renderer bodyRenderer;
        [SerializeField] private Renderer atmosphereRenderer;
        [SerializeField] private Material bodyTarget;
        [SerializeField] private Material atmosphereTarget;

        [Header("Timing")]
        [Tooltip("Seconds from the current look to the target look.")]
        [SerializeField] private float duration = 4f;
        [Tooltip("Blend progress over normalized time.")]
        [SerializeField] private AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Editor Preview")]
        [Range(0f, 1f)]
        [Tooltip("Scrub the transition in edit mode. Leave at 0 — runtime playback starts from here.")]
        [SerializeField] private float preview;

        public float Duration => duration;

        private MaterialPropertyBlock bodyBlock;
        private MaterialPropertyBlock atmosphereBlock;

        public void Play()
        {
            StartCoroutine(PlayRoutine());
        }

        private IEnumerator PlayRoutine()
        {
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                ApplyBlend(ease.Evaluate(Mathf.Clamp01(t / duration)));
                yield return null;
            }
            ApplyBlend(1f);
        }

        private void ApplyBlend(float t)
        {
            if (bodyBlock == null) bodyBlock = new MaterialPropertyBlock();
            if (atmosphereBlock == null) atmosphereBlock = new MaterialPropertyBlock();
            ApplyBlend(bodyRenderer, bodyTarget, bodyBlock, t);
            ApplyBlend(atmosphereRenderer, atmosphereTarget, atmosphereBlock, t);
        }

        private static void ApplyBlend(Renderer renderer, Material target, MaterialPropertyBlock block, float t)
        {
            if (renderer == null || target == null) return;
            Material source = renderer.sharedMaterial;
            if (source == null || source.shader != target.shader) return;

            Shader shader = target.shader;
            int count = shader.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                int id = shader.GetPropertyNameId(i);
                switch (shader.GetPropertyType(i))
                {
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
                        block.SetColor(id, Color.Lerp(source.GetColor(id), target.GetColor(id), t));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
                        block.SetFloat(id, Mathf.Lerp(source.GetFloat(id), target.GetFloat(id), t));
                        break;
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
                        Texture tex = target.GetTexture(id);
                        if (tex != null) block.SetTexture(id, tex);
                        break;
                }
            }
            renderer.SetPropertyBlock(block);
        }

        [ContextMenu("Clear Preview")]
        private void ClearPreview()
        {
            preview = 0f;
            ClearBlock(bodyRenderer);
            ClearBlock(atmosphereRenderer);
        }

        private static void ClearBlock(Renderer renderer)
        {
            if (renderer != null) renderer.SetPropertyBlock(null);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (!Application.isPlaying) ApplyBlend(ease.Evaluate(preview));
        }
#endif
    }
}
