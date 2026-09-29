using System.Collections.Generic;
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
        [Tooltip("Blend previous frame fog into the current result.")]
        [Range(0f, 1f)] public float historyBlend = 0.9f;
        [Tooltip("Reject history when a closer surface occludes (eye depth, meters). 0 = off.")]
        public float occluderDepthThreshold = 0.3f;
        [Tooltip("Soft falloff range for occluder rejection (meters).")]
        public float occluderDepthFalloff = 1f;
        [Tooltip("Fade history when reprojection lands outside the screen (UV distance).")]
        public float historyEdgeFadeDistance = 0.12f;
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
        _pass.Setup(settings, renderingData.cameraData);
        _pass.requiresIntermediateTexture = true;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }

    sealed class VolumetricFogPass : ScriptableRenderPass
    {
        sealed class PassData
        {
            public Material material;
            public int passIndex;
            public TextureHandle source;
            public TextureHandle fogTexture;
            public TextureHandle historyTexture;
            public float depthThreshold;
            public float historyBlend;
            public float occluderDepthThreshold;
            public float occluderDepthFalloff;
            public float historyEdgeFadeDistance;
            public bool hasValidHistory;
            public Matrix4x4 prevViewProj;
        }

        sealed class TemporalState
        {
            public Matrix4x4 prevViewProj = Matrix4x4.identity;
            public bool hasValidHistory;
            public int lastWidth;
            public int lastHeight;
        }

        Settings _settings;
        RTHandle _fogHistoryHandle;

        static readonly Dictionary<ulong, TemporalState> s_TemporalStates = new();
        static readonly MaterialPropertyBlock s_PropertyBlock = new MaterialPropertyBlock();
        static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
        static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
        static readonly int DepthThresholdId = Shader.PropertyToID("_DepthThreshold");
        static readonly int FogTextureId = Shader.PropertyToID("_FogTexture");
        static readonly int HistoryTextureId = Shader.PropertyToID("_HistoryTexture");
        static readonly int PrevViewProjMatrixId = Shader.PropertyToID("_FogPrevViewProjMatrix");
        static readonly int HistoryBlendId = Shader.PropertyToID("_HistoryBlend");
        static readonly int OccluderDepthThresholdId = Shader.PropertyToID("_OccluderDepthThreshold");
        static readonly int OccluderDepthFalloffId = Shader.PropertyToID("_OccluderDepthFalloff");
        static readonly int HistoryEdgeFadeDistanceId = Shader.PropertyToID("_HistoryEdgeFadeDistance");
        static readonly int HasValidHistoryId = Shader.PropertyToID("_HasValidHistory");
        static readonly int BlueNoiseId = Shader.PropertyToID("_BlueNoise");
        static readonly int BlueNoiseTexelSizeId = Shader.PropertyToID("_BlueNoise_TexelSize");

        const int UpscalePassIndex = 0;
        const int TemporalPassIndex = 1;
        const int CompositePassIndex = 2;

        public VolumetricFogPass(Settings settings)
        {
            _settings = settings;
            profilingSampler = new ProfilingSampler("VolumetricFog");
        }

        public void Setup(Settings settings, in CameraData cameraData)
        {
            _settings = settings;

            var desc = cameraData.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(
                ref _fogHistoryHandle,
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Clamp,
                name: "_VolumetricFogHistory");
        }

        public void Dispose()
        {
            _fogHistoryHandle?.Release();
            _fogHistoryHandle = null;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            UniversalResourceData resources = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (resources.isActiveTargetBackBuffer)
                return;

            if (_settings.fogMaterial == null || _settings.upscaleMaterial == null)
                return;

            TextureHandle sceneColor = resources.activeColorTexture;
            if (!sceneColor.IsValid())
                return;

            ulong cameraId = EntityId.ToULong(cameraData.camera.GetEntityId());
            if (!s_TemporalStates.TryGetValue(cameraId, out TemporalState temporalState))
            {
                temporalState = new TemporalState();
                s_TemporalStates[cameraId] = temporalState;
            }

            var sceneDesc = renderGraph.GetTextureDesc(sceneColor);
            if (temporalState.lastWidth != sceneDesc.width || temporalState.lastHeight != sceneDesc.height)
                temporalState.hasValidHistory = false;

            temporalState.lastWidth = sceneDesc.width;
            temporalState.lastHeight = sceneDesc.height;

            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(), renderIntoTexture: true);
            Matrix4x4 view = cameraData.GetViewMatrix();
            Matrix4x4 currViewProj = gpuProj * view;
            Matrix4x4 prevViewProj = temporalState.prevViewProj;
            bool hasValidHistory = temporalState.hasValidHistory && _fogHistoryHandle != null;

            var sceneCopyDesc = sceneDesc;
            sceneCopyDesc.name = "_VolumetricFogSceneCopy";
            sceneCopyDesc.clearBuffer = false;
            TextureHandle sceneCopy = renderGraph.CreateTexture(sceneCopyDesc);
            renderGraph.AddBlitPass(sceneColor, sceneCopy, Vector2.one, Vector2.zero, passName: "Volumetric Fog Copy Scene");

            var lowResDesc = sceneDesc;
            lowResDesc.name = "_VolumetricFogLowRes";
            lowResDesc.clearBuffer = true;
            lowResDesc.width = Mathf.Max(1, lowResDesc.width / _settings.downscaleFactor);
            lowResDesc.height = Mathf.Max(1, lowResDesc.height / _settings.downscaleFactor);
            TextureHandle lowResFog = renderGraph.CreateTexture(lowResDesc);

            var fullResDesc = sceneDesc;
            fullResDesc.name = "_VolumetricFogFullRes";
            fullResDesc.clearBuffer = false;
            TextureHandle fullResFog = renderGraph.CreateTexture(fullResDesc);

            var temporalDesc = sceneDesc;
            temporalDesc.name = "_VolumetricFogTemporal";
            temporalDesc.clearBuffer = false;
            TextureHandle temporalFog = renderGraph.CreateTexture(temporalDesc);

            var compositeDesc = sceneDesc;
            compositeDesc.name = "_VolumetricFogComposite";
            compositeDesc.clearBuffer = false;
            TextureHandle composite = renderGraph.CreateTexture(compositeDesc);

            TextureHandle history = renderGraph.ImportTexture(_fogHistoryHandle);

            AddFullscreenPass(renderGraph, resources, TextureHandle.nullHandle, lowResFog, _settings.fogMaterial, 0, 0f, "Volumetric Fog Low Res");
            AddFullscreenPass(renderGraph, resources, lowResFog, fullResFog, _settings.upscaleMaterial, UpscalePassIndex, _settings.depthThreshold, "Volumetric Fog Upscale");
            AddTemporalPass(renderGraph, resources, fullResFog, history, temporalFog, prevViewProj, hasValidHistory, "Volumetric Fog Temporal");

            if (temporalFog.IsValid() && history.IsValid())
            {
                RenderGraphUtils.BlitMaterialParameters copyParams = new(temporalFog, history, Blitter.GetBlitMaterial(TextureDimension.Tex2D), 0);
                renderGraph.AddBlitPass(copyParams, "Volumetric Fog Copy History");
            }

            AddCompositePass(renderGraph, sceneCopy, temporalFog, composite, _settings.upscaleMaterial, CompositePassIndex, "Volumetric Fog Composite");

            temporalState.prevViewProj = currViewProj;
            temporalState.hasValidHistory = true;

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

        void AddTemporalPass(
            RenderGraph renderGraph,
            UniversalResourceData resources,
            TextureHandle currentFog,
            TextureHandle history,
            TextureHandle destination,
            Matrix4x4 prevViewProj,
            bool hasValidHistory,
            string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData, profilingSampler))
            {
                passData.material = _settings.upscaleMaterial;
                passData.passIndex = TemporalPassIndex;
                passData.source = currentFog;
                passData.historyTexture = history;
                passData.prevViewProj = prevViewProj;
                passData.historyBlend = _settings.historyBlend;
                passData.occluderDepthThreshold = _settings.occluderDepthThreshold;
                passData.occluderDepthFalloff = _settings.occluderDepthFalloff;
                passData.historyEdgeFadeDistance = _settings.historyEdgeFadeDistance;
                passData.hasValidHistory = hasValidHistory;

                builder.UseTexture(currentFog, AccessFlags.Read);

                if (history.IsValid())
                    builder.UseTexture(history, AccessFlags.Read);

                Debug.Assert(resources.cameraDepthTexture.IsValid());
                builder.UseTexture(resources.cameraDepthTexture);

                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
                {
                    ExecuteTemporalPass(
                        ctx.cmd,
                        data.source,
                        data.historyTexture,
                        data.material,
                        data.passIndex,
                        data.prevViewProj,
                        data.historyBlend,
                        data.occluderDepthThreshold,
                        data.occluderDepthFalloff,
                        data.historyEdgeFadeDistance,
                        data.hasValidHistory);
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

            BindBlueNoise(material, s_PropertyBlock);

            cmd.DrawProcedural(Matrix4x4.identity, material, passIndex, MeshTopology.Triangles, 3, 1, s_PropertyBlock);
        }

        static void BindBlueNoise(Material material, MaterialPropertyBlock block)
        {
            if (!material.HasProperty(BlueNoiseId))
                return;

            Texture blueNoise = material.GetTexture(BlueNoiseId);
            if (blueNoise == null)
                return;

            block.SetTexture(BlueNoiseId, blueNoise);
            block.SetVector(BlueNoiseTexelSizeId, new Vector4(
                1f / blueNoise.width,
                1f / blueNoise.height,
                blueNoise.width,
                blueNoise.height));
        }

        static void ExecuteTemporalPass(
            RasterCommandBuffer cmd,
            TextureHandle currentFog,
            TextureHandle history,
            Material material,
            int passIndex,
            Matrix4x4 prevViewProj,
            float historyBlend,
            float occluderDepthThreshold,
            float occluderDepthFalloff,
            float historyEdgeFadeDistance,
            bool hasValidHistory)
        {
            s_PropertyBlock.Clear();

            if (currentFog.IsValid())
                s_PropertyBlock.SetTexture(BlitTextureId, currentFog);

            if (history.IsValid())
                s_PropertyBlock.SetTexture(HistoryTextureId, history);

            s_PropertyBlock.SetMatrix(PrevViewProjMatrixId, prevViewProj);
            s_PropertyBlock.SetFloat(HistoryBlendId, historyBlend);
            s_PropertyBlock.SetFloat(OccluderDepthThresholdId, occluderDepthThreshold);
            s_PropertyBlock.SetFloat(OccluderDepthFalloffId, occluderDepthFalloff);
            s_PropertyBlock.SetFloat(HistoryEdgeFadeDistanceId, historyEdgeFadeDistance);
            s_PropertyBlock.SetFloat(HasValidHistoryId, hasValidHistory ? 1f : 0f);
            s_PropertyBlock.SetVector(BlitScaleBiasId, new Vector4(1, 1, 0, 0));

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
