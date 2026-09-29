using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class BlurToTextureFeature : ScriptableRendererFeature
{
    public Material blurMaterial;
    [Range(0f, 10f)] public float blurStrength = 2f;

    static readonly int blurStrengthId = Shader.PropertyToID("_BlurStrength");

    class PassData
    {
        public Material material;
        public TextureHandle source;
    }

    class BlurPass : ScriptableRenderPass
    {
        public Material material;
        static readonly int blitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int blitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int blurredTexId = Shader.PropertyToID("_BlurredSceneTexture");
        static readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (material == null)
                return;

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();

            if (!resourceData.cameraColor.IsValid())
                return;

            var desc = cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            TextureHandle blurredTexture = renderGraph.CreateTexture(new TextureDesc(desc)
            {
                name = "_BlurredSceneTexture"
            });

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlurToTexture", out var passData))
            {
                passData.material = material;
                passData.source = resourceData.cameraColor;

                builder.UseTexture(resourceData.cameraColor, AccessFlags.Read);
                builder.SetRenderAttachment(blurredTexture, 0, AccessFlags.Write);
                builder.SetGlobalTextureAfterPass(blurredTexture, blurredTexId);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    propertyBlock.Clear();
                    propertyBlock.SetTexture(blitTextureId, data.source);
                    propertyBlock.SetVector(blitScaleBiasId, new Vector4(1, 1, 0, 0));
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1, propertyBlock);
                });
            }

        
        }
    }

    BlurPass pass;

    public override void Create()
    {
        pass = new BlurPass();
        pass.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (pass == null || blurMaterial == null)
            return;

        blurMaterial.SetFloat(blurStrengthId, blurStrength);
        pass.material = blurMaterial;
        renderer.EnqueuePass(pass);
    }
}