using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlimeJumping.VFX
{
    /// 바위 표면에 TMP 텍스트를 음각으로 새긴다. label 의 RectTransform 이 투영 프레임 —
    /// 씬에서 그 사각형을 놓은 자리·크기 그대로 바위 픽셀에 파인다. label 메시는 마스크
    /// RT 를 굽는 소스로만 쓰고 렌더러는 항상 꺼 둔다. label.enabled 토글(EndingTimeDisplay
    /// 의 기록 리빌)이 그대로 각인 표시 스위치가 된다. rock 머티리얼은
    /// "Quibli/Stylized Lit (Rock Engraving)" 셰이더여야 반응한다. 컴포넌트 제거 = 롤백
    /// (OnDisable 이 라벨 렌더러와 프로퍼티 블록을 복구).
    [ExecuteAlways]
    public sealed class RockEngraving : MonoBehaviour
    {
        [SerializeField] private TextMeshPro label;
        [SerializeField] private MeshRenderer rock;
        [SerializeField] private RockEngravingConfig configOverride;

        static readonly int MaskId = Shader.PropertyToID("_EngraveMask");
        static readonly int WorldToRectId = Shader.PropertyToID("_EngraveWorldToRect");
        static readonly int RightId = Shader.PropertyToID("_EngraveRightWS");
        static readonly int UpId = Shader.PropertyToID("_EngraveUpWS");
        static readonly int OutwardId = Shader.PropertyToID("_EngraveOutwardWS");
        static readonly int TexelId = Shader.PropertyToID("_EngraveMaskTexel");
        static readonly int ParamsId = Shader.PropertyToID("_EngraveParams");
        static readonly int ShadowId = Shader.PropertyToID("_EngraveShadow");
        static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

        private RenderTexture mask;
        private Material maskMaterial;
        private MaterialPropertyBlock block;
        private CommandBuffer commands;
        private string bakedText;
        private float bakedSoftness = -1f;
        private int bakedWidth = -1;

        void OnEnable()
        {
#if UNITY_EDITOR
            // 도메인 리로드 뒤 에디터가 놀고 있으면 LateUpdate 가 안 돌아 각인이 빈 채 멈춘다.
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void OnDisable()
        {
            if (rock != null) rock.SetPropertyBlock(null);
            if (label != null)
            {
                var labelRenderer = label.GetComponent<MeshRenderer>();
                if (labelRenderer != null) labelRenderer.enabled = true;
            }
            Release(ref mask);
            if (maskMaterial != null)
            {
                DestroySafe(maskMaterial);
                maskMaterial = null;
            }
            if (commands != null)
            {
                commands.Release();
                commands = null;
            }
            bakedText = null;
            bakedSoftness = -1f;
            bakedWidth = -1;
        }

        void LateUpdate()
        {
            if (label == null || rock == null) return;
            var cfg = RockEngravingConfig.Resolve(configOverride);

            var labelRenderer = label.GetComponent<MeshRenderer>();
            if (labelRenderer != null && labelRenderer.enabled) labelRenderer.enabled = false;

            bool visible = label.enabled;
            string text = visible ? label.text : "";
            if (mask == null || text != bakedText
                || !Mathf.Approximately(cfg.edgeSoftness, bakedSoftness) || cfg.maskWidth != bakedWidth)
                Bake(cfg, visible, text);

            Apply(cfg);
        }

        void Bake(RockEngravingConfig cfg, bool visible, string text)
        {
            var rect = label.rectTransform.rect;
            int width = Mathf.Max(64, cfg.maskWidth);
            int height = Mathf.Max(8, Mathf.RoundToInt(width * rect.height / Mathf.Max(0.01f, rect.width)));
            if (mask == null || mask.width != width || mask.height != height)
            {
                Release(ref mask);
                mask = new RenderTexture(width, height, 0, RenderTextureFormat.R8)
                {
                    name = "EngraveMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                mask.Create();
            }
            if (maskMaterial == null)
            {
                Shader shader = cfg.maskShader;
                if (shader == null) shader = Shader.Find("Hidden/SlimeJumping/EngraveTextMask");
                if (shader == null) return;
                maskMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (commands == null) commands = new CommandBuffer { name = "RockEngraving" };

            commands.Clear();
            commands.SetRenderTarget(mask);
            commands.ClearRenderTarget(false, true, Color.clear);
            if (visible && !string.IsNullOrEmpty(text))
            {
                label.ForceMeshUpdate(true, true);
                maskMaterial.SetTexture(MainTexId, label.font.atlasTexture);
                maskMaterial.SetFloat(EdgeSoftnessId, cfg.edgeSoftness);
                // RT 굽기는 위아래가 뒤집혀 저장되므로 직교행렬에서 미리 한 번 뒤집는다.
                var proj = Matrix4x4.Ortho(rect.xMin, rect.xMax, rect.yMax, rect.yMin, -10f, 10f);
                commands.SetViewProjectionMatrices(Matrix4x4.identity, GL.GetGPUProjectionMatrix(proj, true));
                commands.DrawMesh(label.mesh, Matrix4x4.identity, maskMaterial, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(commands);

            bakedText = text;
            bakedSoftness = cfg.edgeSoftness;
            bakedWidth = cfg.maskWidth;
        }

        void Apply(RockEngravingConfig cfg)
        {
            var rectT = label.rectTransform;
            var rect = rectT.rect;
            if (block == null) block = new MaterialPropertyBlock();

            var toRect = Matrix4x4.Scale(new Vector3(
                    1f / Mathf.Max(0.01f, rect.width), 1f / Mathf.Max(0.01f, rect.height), 1f))
                * Matrix4x4.Translate(new Vector3(-rect.center.x, -rect.center.y, 0f))
                * rectT.worldToLocalMatrix;
            block.SetMatrix(WorldToRectId, toRect);
            block.SetVector(RightId, rectT.right);
            block.SetVector(UpId, rectT.up);
            block.SetVector(OutwardId, -rectT.forward);
            block.SetVector(TexelId, new Vector4(1f / mask.width, 1f / mask.height, 0f, 0f));
            block.SetVector(ParamsId, new Vector4(cfg.carveDepth, cfg.floorDarken, cfg.projectionDepth, 0f));
            block.SetVector(ShadowId, new Vector4(
                cfg.innerShadow, cfg.innerShadowLength / Mathf.Max(0.01f, rect.height), 0f, 0f));
            block.SetTexture(MaskId, mask);
            rock.SetPropertyBlock(block);
        }

        static void Release(ref RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            DestroySafe(rt);
            rt = null;
        }

        static void DestroySafe(Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
