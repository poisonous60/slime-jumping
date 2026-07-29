using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SlimeJumping.Rendering
{
    /// <summary>Copies camera color into _SpaceWarpSourceTex, then draws LightMode="SpaceWarpDraw" meshes over it.</summary>
    /// Warp shaders use that tag instead of UniversalForward, so with this feature absent from the
    /// URP renderer list URP never draws them at all and the warp is invisible.
    public class SpaceWarpSourceFeature : ScriptableRendererFeature
    {
        [SerializeField]
        private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;

        [SerializeField]
        private LayerMask layerMask = -1;

        private CopyPass m_CopyPass;
        private DrawPass m_DrawPass;

        public override void Create()
        {
            m_CopyPass = new CopyPass { renderPassEvent = injectionPoint };
            m_DrawPass = new DrawPass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var ct = renderingData.cameraData.cameraType;
            if (ct != CameraType.Game && ct != CameraType.SceneView) return;

            m_CopyPass.renderPassEvent = injectionPoint;
            m_DrawPass.renderPassEvent = injectionPoint;
            m_DrawPass.SetLayerMask(layerMask);

            // Enqueue copy first so SetGlobalTexture is in place before draw runs.
            renderer.EnqueuePass(m_CopyPass);
            renderer.EnqueuePass(m_DrawPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_CopyPass?.Dispose();
        }

        private class CopyPass : ScriptableRenderPass, System.IDisposable
        {
            const string PassName = "SpaceWarpSourceCopy";
            static readonly int TexId = Shader.PropertyToID("_SpaceWarpSourceTex");

            private RTHandle m_TempRT;

            private class PassData
            {
                public TextureHandle src;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var src = resourceData.activeColorTexture;
                if (!src.IsValid()) return;

                var desc = renderGraph.GetTextureDesc(src);
                desc.name = "_SpaceWarpSourceTex";
                desc.clearBuffer = false;
                desc.depthBufferBits = DepthBits.None;
                var dst = renderGraph.CreateTexture(desc);

                using var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData);
                passData.src = src;
                builder.UseTexture(src, AccessFlags.Read);
                builder.SetRenderAttachment(dst, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(dst, TexId);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, d.src, new Vector4(1, 1, 0, 0), 0, false);
                });
            }

            [System.Obsolete("Compatibility-mode path for URP with RenderGraph disabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var desc = renderingData.cameraData.cameraTargetDescriptor;
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;
                RenderingUtils.ReAllocateHandleIfNeeded(ref m_TempRT, desc, name: "_SpaceWarpSourceTex");

                var cmd = CommandBufferPool.Get(PassName);
                using (new ProfilingScope(cmd, new ProfilingSampler(PassName)))
                {
                    var src = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    Blitter.BlitCameraTexture(cmd, src, m_TempRT);
                    cmd.SetGlobalTexture(TexId, m_TempRT);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                m_TempRT?.Release();
                m_TempRT = null;
            }
        }

        private class DrawPass : ScriptableRenderPass
        {
            const string PassName = "SpaceWarpDraw";
            static readonly ShaderTagId s_TagId = new ShaderTagId("SpaceWarpDraw");

            private FilteringSettings m_Filter =
                new FilteringSettings(RenderQueueRange.transparent, -1);

            public void SetLayerMask(LayerMask mask) { m_Filter.layerMask = mask; }

            private class PassData
            {
                public RendererListHandle rl;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData    = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData     = frameData.Get<UniversalLightData>();
                var resourceData  = frameData.Get<UniversalResourceData>();

                using var builder = renderGraph.AddRasterRenderPass<PassData>(PassName, out var passData);

                var drawSettings = RenderingUtils.CreateDrawingSettings(
                    s_TagId, renderingData, cameraData, lightData, SortingCriteria.CommonTransparent);

                var rlParams = new RendererListParams(renderingData.cullResults, drawSettings, m_Filter);
                passData.rl = renderGraph.CreateRendererList(rlParams);

                builder.UseRendererList(passData.rl);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.rl);
                });
            }

            [System.Obsolete("Compatibility-mode path for URP with RenderGraph disabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var drawSettings = CreateDrawingSettings(
                    s_TagId, ref renderingData, SortingCriteria.CommonTransparent);

                var cmd = CommandBufferPool.Get(PassName);
                using (new ProfilingScope(cmd, new ProfilingSampler(PassName)))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // BlitCameraTexture left the active target on the scratch RT; without rebinding,
                    // the warp mesh draws there instead of camera color and is invisible.
                    var renderer = renderingData.cameraData.renderer;
                    cmd.SetRenderTarget(renderer.cameraColorTargetHandle, renderer.cameraDepthTargetHandle);

                    var rlParams = new RendererListParams(renderingData.cullResults, drawSettings, m_Filter);
                    var rl = context.CreateRendererList(ref rlParams);
                    cmd.DrawRendererList(rl);
                }
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
