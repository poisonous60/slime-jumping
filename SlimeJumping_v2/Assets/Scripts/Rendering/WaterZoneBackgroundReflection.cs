using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.Rendering
{
    /// Mirrors a WaterZone_v2's animated fan mesh onto the BackgroundBlur layer,
    /// so the vertex-level wave simulation shows up both as the crisp foreground
    /// water surface AND inside the blurred background. The ghost child shares
    /// the source MeshFilter's sharedMesh, so wave motion auto-syncs every
    /// frame with zero copy cost — WaterZone_v2 mutates the one Mesh instance
    /// and both MeshRenderers read from it.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [AddComponentMenu("SlimeJumping/Rendering/Water Zone Background Reflection")]
    public class WaterZoneBackgroundReflection : MonoBehaviour
    {
        [Tooltip("Optional override for the ghost's material. If null, reuses " +
                 "the source MeshRenderer's material (tint below still applies).")]
        [SerializeField] Material backgroundMaterial;

        [Tooltip("Multiplicative tint applied via MaterialPropertyBlock _Color.")]
        [SerializeField] Color ghostTint = new Color(1f, 1f, 1f, 1f);

        [Tooltip("Local-space offset of the ghost relative to this water zone. " +
                 "Usually Z-only; the BackgroundCamera is orthographic so XY " +
                 "must match for the reflection to line up behind the scene.")]
        [SerializeField] Vector3 localOffset = new Vector3(0f, 0f, 0.5f);

        [SerializeField, HideInInspector] MeshFilter sourceFilter;
        [SerializeField, HideInInspector] MeshRenderer sourceRenderer;
        [SerializeField, HideInInspector] GameObject ghost;
        [SerializeField, HideInInspector] MeshFilter ghostFilter;
        [SerializeField, HideInInspector] MeshRenderer ghostRenderer;

        MaterialPropertyBlock mpb;
        static readonly int ID_Color = Shader.PropertyToID("_Color");
        const string GhostName = "BG_Reflection";

        void OnEnable()
        {
            CacheSourceRefs();
            EnsureGhost();
            SyncGhost();
        }

        void OnDisable()
        {
            if (ghostRenderer != null) ghostRenderer.enabled = false;
        }

        void LateUpdate()
        {
            // Runs in edit mode too (ExecuteAlways). The wave sim writes mesh
            // vertices continuously, so we re-apply material/tint each frame
            // to stay in step if the source material or tint is swapped.
            SyncGhost();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // AddComponent / new GameObject are unsafe inside OnValidate — defer.
            EditorApplication.delayCall += DelayedValidate;
        }

        void DelayedValidate()
        {
            if (this == null) return;
            CacheSourceRefs();
            EnsureGhost();
            SyncGhost();
        }
#endif

        void CacheSourceRefs()
        {
            if (sourceFilter == null)   sourceFilter   = GetComponent<MeshFilter>();
            if (sourceRenderer == null) sourceRenderer = GetComponent<MeshRenderer>();
        }

        void EnsureGhost()
        {
            int bgLayer = LayerMask.NameToLayer("BackgroundBlur");
            if (bgLayer < 0) return;

            if (ghost == null)
            {
                var t = transform.Find(GhostName);
                if (t != null) ghost = t.gameObject;
            }

            if (ghost == null)
            {
                ghost = new GameObject(GhostName);
                ghost.transform.SetParent(transform, false);
            }

            if (ghostFilter == null)
            {
                ghostFilter = ghost.GetComponent<MeshFilter>();
                if (ghostFilter == null) ghostFilter = ghost.AddComponent<MeshFilter>();
            }
            if (ghostRenderer == null)
            {
                ghostRenderer = ghost.GetComponent<MeshRenderer>();
                if (ghostRenderer == null) ghostRenderer = ghost.AddComponent<MeshRenderer>();
            }

            ghost.layer = bgLayer;
            ghost.transform.localPosition = localOffset;
            ghost.transform.localRotation = Quaternion.identity;
            ghost.transform.localScale = Vector3.one;

            ghostRenderer.enabled = true;
            ghostRenderer.shadowCastingMode     = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghostRenderer.receiveShadows        = false;
            ghostRenderer.lightProbeUsage       = UnityEngine.Rendering.LightProbeUsage.Off;
            ghostRenderer.reflectionProbeUsage  = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        void SyncGhost()
        {
            if (ghost == null || ghostFilter == null || ghostRenderer == null) return;
            CacheSourceRefs();
            if (sourceFilter == null || sourceRenderer == null) return;

            if (ghostFilter.sharedMesh != sourceFilter.sharedMesh)
                ghostFilter.sharedMesh = sourceFilter.sharedMesh;

            Material mat = backgroundMaterial;
            if (mat == null) mat = sourceRenderer.sharedMaterial;
            if (mat != null && ghostRenderer.sharedMaterial != mat)
                ghostRenderer.sharedMaterial = mat;

            if (mat == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            ghostRenderer.GetPropertyBlock(mpb);
            mpb.SetColor(ID_Color, ghostTint);
            ghostRenderer.SetPropertyBlock(mpb);

            if (ghost.transform.localPosition != localOffset)
                ghost.transform.localPosition = localOffset;
        }
    }
}
