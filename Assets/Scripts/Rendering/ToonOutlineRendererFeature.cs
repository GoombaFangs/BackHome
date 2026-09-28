using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Screen-space ink outline. Samples the camera depth texture, which includes
/// the planet because terrain shaders write a depth-normals prepass.
/// </summary>
public class ToonOutlineRendererFeature : ScriptableRendererFeature
{
    [SerializeField] Material material;

    ToonOutlinePass _pass;

    public override void Create()
    {
        _pass = new ToonOutlinePass();
        _pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (material == null)
            return;

        CameraType cameraType = renderingData.cameraData.cameraType;
        if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
            return;

        _pass.Setup(material);
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass = null;
    }

    sealed class ToonOutlinePass : ScriptableRenderPass
    {
        static readonly int DepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        Material _material;

        public ToonOutlinePass()
        {
            profilingSampler = new ProfilingSampler("Toon Outline");
        }

        public void Setup(Material outlineMaterial)
        {
            _material = outlineMaterial;
        }

        class MaskPassData
        {
            public RendererListHandle planets;
        }

        class PassData
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle depth;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            if (_material == null || !resources.cameraColor.IsValid() || !resources.activeColorTexture.IsValid())
                return;
            if (!resources.cameraDepthTexture.IsValid())
                return;

            TextureDesc colorDesc = renderGraph.GetTextureDesc(resources.cameraColor);
            colorDesc.name = "_ToonOutlineColor";
            colorDesc.clearBuffer = false;
            TextureHandle source = renderGraph.CreateTexture(colorDesc);
            renderGraph.AddBlitPass(resources.cameraColor, source, Vector2.one, Vector2.zero, passName: "Toon Outline Copy");

            if (resources.activeDepthTexture.IsValid())
            {
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();
                DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(
                    new ShaderTagId("PlanetStencil"), renderingData, cameraData, lightData, SortingCriteria.None);
                FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.opaque);
                RendererListParams rendererListParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
                RendererListHandle planetMask = renderGraph.CreateRendererList(rendererListParams);

                using (IRasterRenderGraphBuilder maskBuilder = renderGraph.AddRasterRenderPass("Planet Outline Mask", out MaskPassData maskData))
                {
                    maskData.planets = planetMask;
                    maskBuilder.UseRendererList(planetMask);
                    maskBuilder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                    maskBuilder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                    maskBuilder.SetRenderFunc(static (MaskPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.planets);
                    });
                }
            }

            using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass("Toon Outline", out PassData passData, profilingSampler))
            {
                passData.material = _material;
                passData.source = source;
                passData.depth = resources.cameraDepthTexture;
                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.UseTexture(passData.depth, AccessFlags.Read);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                if (resources.activeDepthTexture.IsValid())
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.SetGlobalTexture(DepthTextureId, data.depth);
                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), data.material, 0);
                });
            }
        }
    }
}
