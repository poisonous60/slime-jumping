using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// Inspector-side tuner for the 3-layer Waterfall_Background prefab.
    /// Writes to MaterialPropertyBlock (per-renderer overrides) so the shared
    /// .mat assets stay clean — the component captures each layer's material
    /// value as the baseline and applies master multipliers on top.
    ///
    /// Scene-view animation: requires the Scene View "Animated Materials"
    /// toggle to be ON, since the shader uses _Time.y for scroll.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Rendering/Waterfall Background")]
    public class WaterfallBackground : MonoBehaviour
    {
        [Header("Layers (auto-wired from children if left empty)")]
        [SerializeField] MeshRenderer backLayer;
        [SerializeField] MeshRenderer midLayer;
        [SerializeField] MeshRenderer frontLayer;

        [Header("Global")]
        [Tooltip("Multiplies every layer's scroll speed. 1 = material default.")]
        [Range(0f, 3f)] public float flowSpeed = 1f;

        [Tooltip("Multiplied into all three color channels (shadow/mid/highlight).")]
        [ColorUsage(showAlpha: false, hdr: false)]
        public Color tint = Color.white;

        [Header("Per-layer alpha")]
        [Range(0f, 1f)] public float backAlpha  = 1.00f;
        [Range(0f, 1f)] public float midAlpha   = 0.85f;
        [Range(0f, 1f)] public float frontAlpha = 0.55f;

        [Header("Per-layer foam (lower = more foam highlights)")]
        [Range(0f, 1f)] public float backFoam   = 0.70f;
        [Range(0f, 1f)] public float midFoam    = 0.55f;
        [Range(0f, 1f)] public float frontFoam  = 0.45f;

        static readonly int ID_ScrollA        = Shader.PropertyToID("_ScrollA");
        static readonly int ID_ScrollB        = Shader.PropertyToID("_ScrollB");
        static readonly int ID_ShadowColor    = Shader.PropertyToID("_ShadowColor");
        static readonly int ID_MidColor       = Shader.PropertyToID("_MidColor");
        static readonly int ID_HighlightColor = Shader.PropertyToID("_HighlightColor");
        static readonly int ID_Alpha          = Shader.PropertyToID("_Alpha");
        static readonly int ID_HiThreshold    = Shader.PropertyToID("_HighlightThreshold");

        MaterialPropertyBlock _mpb;

        void OnEnable()  { AutoWire(); Apply(); }
        void OnValidate() { AutoWire(); Apply(); }

        void AutoWire()
        {
            if (backLayer == null)
            {
                var t = transform.Find("Layer_Back");
                if (t != null) backLayer = t.GetComponent<MeshRenderer>();
            }
            if (midLayer == null)
            {
                var t = transform.Find("Layer_Mid");
                if (t != null) midLayer = t.GetComponent<MeshRenderer>();
            }
            if (frontLayer == null)
            {
                var t = transform.Find("Layer_Front");
                if (t != null) frontLayer = t.GetComponent<MeshRenderer>();
            }
        }

        void Apply()
        {
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            ApplyLayer(backLayer,  backAlpha,  backFoam);
            ApplyLayer(midLayer,   midAlpha,   midFoam);
            ApplyLayer(frontLayer, frontAlpha, frontFoam);
        }

        void ApplyLayer(MeshRenderer mr, float alpha, float foam)
        {
            if (mr == null) return;
            var mat = mr.sharedMaterial;
            if (mat == null) return;

            mr.GetPropertyBlock(_mpb);

            // Scroll speed: read material baseline, scale Y by flowSpeed.
            Vector4 sA = mat.GetVector(ID_ScrollA);
            Vector4 sB = mat.GetVector(ID_ScrollB);
            sA.y *= flowSpeed;
            sB.y *= flowSpeed;
            _mpb.SetVector(ID_ScrollA, sA);
            _mpb.SetVector(ID_ScrollB, sB);

            // Tint: multiply material baseline colors by the master tint.
            _mpb.SetColor(ID_ShadowColor,    mat.GetColor(ID_ShadowColor)    * tint);
            _mpb.SetColor(ID_MidColor,       mat.GetColor(ID_MidColor)       * tint);
            _mpb.SetColor(ID_HighlightColor, mat.GetColor(ID_HighlightColor) * tint);

            _mpb.SetFloat(ID_Alpha,       alpha);
            _mpb.SetFloat(ID_HiThreshold, foam);

            mr.SetPropertyBlock(_mpb);
        }

        [ContextMenu("Clear overrides (revert to material defaults)")]
        void ClearOverrides()
        {
            if (backLayer  != null) backLayer.SetPropertyBlock(null);
            if (midLayer   != null) midLayer.SetPropertyBlock(null);
            if (frontLayer != null) frontLayer.SetPropertyBlock(null);
        }
    }
}
