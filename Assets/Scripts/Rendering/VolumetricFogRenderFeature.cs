using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

public class VolumetricFogRenderFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Material fogMaterial;
        public Material upscaleMaterial;
        [Range(1, 4)] public int downscaleFactor = 4;
        public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
        [Tooltip("Max linear eye-depth difference for nearest-depth upsample.")]
        public float depthThreshold = 0.5f;
    }

    public Settings settings = new Settings();

    VolumetricFogPass _pass;

    public override void Create()
    {
        _pass = new VolumetricFogPass(settings)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.fogMaterial == null || settings.upscaleMaterial == null)
            return;

        if (renderingData.cameraData.cameraType == CameraType.Preview
            || renderingData.cameraData.cameraType == CameraType.Reflection
            || UniversalRenderer.IsOffscreenDepthTexture(ref renderingData.cameraData))
            return;

        _pass.renderPassEvent = settings.renderPassEvent;
        _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
        _pass.Setup(settings);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);
    }

    sealed class VolumetricFogPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public int passIndex;
            public TextureHandle source;
            public TextureHandle fogTexture;
            public float depthThreshold;
        }

        Settings _settings;

        static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        static readonly int FogTextureId = Shader.PropertyToID("_FogTexture");

        const int UpscalePassIndex = 0;
        const int CompositePassIndex = 1;

        public VolumetricFogPass(Settings settings)
        {
            _settings = settings;
            profilingSampler = new ProfilingSampler("VolumetricFog");
        }

        public void Setup(Settings settings) => _settings = settings;

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();

            if (resources.isActiveTargetBackBuffer)
                return;

            if (_settings.fogMaterial == null || _settings.upscaleMaterial == null)
                return;

            TextureHandle sceneColor = resources.activeColorTexture;
            if (!sceneColor.IsValid())
                return;

            var sceneCopyDesc = renderGraph.GetTextureDesc(sceneColor);
            sceneCopyDesc.name = "_VolumetricFogSceneCopy";
            sceneCopyDesc.clearBuffer = false;
            TextureHandle sceneCopy = renderGraph.CreateTexture(sceneCopyDesc);
            renderGraph.AddBlitPass(sceneColor, sceneCopy, Vector2.one, Vector2.zero, passName: "Volumetric Fog Copy Scene");

            var lowResDesc = renderGraph.GetTextureDesc(sceneColor);
            lowResDesc.name = "_VolumetricFogLowRes";
            lowResDesc.clearBuffer = true;
            lowResDesc.width = Mathf.Max(1, lowResDesc.width / _settings.downscaleFactor);
            lowResDesc.height = Mathf.Max(1, lowResDesc.height / _settings.downscaleFactor);
            TextureHandle lowResFog = renderGraph.CreateTexture(lowResDesc);

            var fullResDesc = renderGraph.GetTextureDesc(sceneColor);
            fullResDesc.name = "_VolumetricFogFullRes";
            fullResDesc.clearBuffer = false;
            TextureHandle fullResFog = renderGraph.CreateTexture(fullResDesc);

            var compositeDesc = renderGraph.GetTextureDesc(sceneColor);
            compositeDesc.name = "_VolumetricFogComposite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);

            AddFullscreenPass(renderGraph, resources, TextureHandle.nullHandle, lowResFog, _settings.fogMaterial, 0, 0f, "Volumetric Fog Low Res");
            AddFullscreenPass(renderGraph, resources, lowResFog, fullResFog, _settings.upscaleMaterial, UpscalePassIndex, _settings.depthThreshold, "Volumetric Fog Upscale");
            AddCompositePass(renderGraph, sceneCopy, fullResFog, composite, _settings.upscaleMaterial, CompositePassIndex, "Volumetric Fog Composite");

            resources.cameraColor = composite;
        }

        void AddFullscreenPass(
            RenderGraph renderGraph,
            UniversalResourceData resources,
            TextureHandle source,
            TextureHandle destination,
            Material material,
            int passIndex,
            float depthThreshold,
            string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                passData.material = material;
                passData.passIndex = passIndex;
                passData.source = source;
                passData.depthThreshold = depthThreshold;

                if (source.IsValid())
                    builder.UseTexture(source, AccessFlags.Read);

                Debug.Assert(resources.cameraDepthTexture.IsValid());
                builder.UseTexture(resources.cameraDepthTexture);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    ExecuteMainPass(ctx.cmd, data.source, data.material, data.passIndex, data.depthThreshold);
                });
            }
        }

        void AddCompositePass(
            RenderGraph renderGraph,
            TextureHandle sceneTexture,
            TextureHandle fogTexture,
            TextureHandle destination,
            Material material,
            int passIndex,
            string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                passData.material = material;
                passData.passIndex = passIndex;
                passData.source = sceneTexture;
                passData.fogTexture = fogTexture;

                builder.UseTexture(sceneTexture, AccessFlags.Read);
                builder.UseTexture(fogTexture, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    ExecuteCompositePass(ctx.cmd, data.source, data.fogTexture, data.material, data.passIndex);
                });
            }
        }

        static void ExecuteMainPass(
            RasterCommandBuffer cmd,
            TextureHandle source,
            Material material,
            int passIndex,
            float depthThreshold)
        {
            s_PropertyBlock.Clear();

            if (source.IsValid())
                s_PropertyBlock.SetTexture(BlitTextureId, source);

            s_PropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));

            if (depthThreshold > 0f)
                s_PropertyBlock.SetFloat(DepthThresholdId, depthThreshold);

            cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
        }

        static void ExecuteCompositePass(
            RasterCommandBuffer cmd,
            TextureHandle sceneTexture,
            TextureHandle fogTexture,
            Material material,
            int passIndex)
        {
            s_PropertyBlock.Clear();

            if (sceneTexture.IsValid())
                s_PropertyBlock.SetTexture(BlitTextureId, sceneTexture);

            if (fogTexture.IsValid())
                s_PropertyBlock.SetTexture(FogTextureId, fogTexture);

            s_PropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));

            cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
        }
    }
}
