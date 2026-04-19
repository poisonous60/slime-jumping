using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SlimeJumping.Rendering
{
    /// Draws the slime's "OccludedSilhouette" shader pass after all opaques have
    /// written to the depth buffer. The pass itself uses ZTest Greater + alpha blend
    /// to tint the slime's silhouette where it is hidden behind terrain.
    ///
    /// The pass must tag LightMode = "SlimeOccludedSilhouette" (unique, so URP's
    /// default DrawObjectsPass ignores it). Current materials:
    ///   Assets/Art/Shader/SlimeBody_Quibli.shader (active)
    ///   Assets/Art/Shader/SlimeBody3D.shader      (fallback)
    public class SlimeOccludedSilhouetteFeature : ScriptableRendererFeature
    {
        [SerializeField] private LayerMask layerMask = -1;
        [SerializeField] private RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingOpaques;

        private SilhouettePass m_Pass;

        public override void Create()
        {
            m_Pass = new SilhouettePass
            {
                renderPassEvent = injectionPoint
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            m_Pass.renderPassEvent = injectionPoint;
            m_Pass.Setup(layerMask);
            renderer.EnqueuePass(m_Pass);
        }

        private class SilhouettePass : ScriptableRenderPass
        {
            private static readonly ShaderTagId s_ShaderTagId = new ShaderTagId("SlimeOccludedSilhouette");

            private FilteringSettings m_FilteringSettings =
                new FilteringSettings(RenderQueueRange.opaque, -1);

            public void Setup(LayerMask layerMask)
            {
                m_FilteringSettings.layerMask = layerMask;
            }

            private class PassData
            {
                public RendererListHandle rendererList;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var renderingData = frameData.Get<UniversalRenderingData>();
                var lightData = frameData.Get<UniversalLightData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                using var builder = renderGraph.AddRasterRenderPass<PassData>(
                    "SlimeOccludedSilhouette", out var passData);

                var sortFlags = cameraData.defaultOpaqueSortFlags;
                var drawSettings = RenderingUtils.CreateDrawingSettings(
                    s_ShaderTagId, renderingData, cameraData, lightData, sortFlags);

                var listParams = new RendererListParams(
                    renderingData.cullResults, drawSettings, m_FilteringSettings);
                passData.rendererList = renderGraph.CreateRendererList(listParams);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(d.rendererList);
                });
            }

            [System.Obsolete("Compatibility-mode path for URP with RenderGraph disabled.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
                var drawSettings = CreateDrawingSettings(s_ShaderTagId, ref renderingData, sortFlags);

                var cmd = CommandBufferPool.Get("SlimeOccludedSilhouette");
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                var rlParams = new RendererListParams(
                    renderingData.cullResults, drawSettings, m_FilteringSettings);
                var rl = context.CreateRendererList(ref rlParams);
                cmd.DrawRendererList(rl);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }
    }
}
