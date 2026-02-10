using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace FOW
{
    /// <summary>
    /// Renders layer-mask objects to an offscreen target and composites them back to camera color
    /// using FoW visibility, allowing smooth visibility falloff without transparent materials.
    /// </summary>
    public sealed class FogOfWarCompositeFeature : ScriptableRendererFeature
    {
        public enum CompositeOutputMode
        {
            None = 2,
            RenderLayer = 0,
            RenderFoW = 1,
        }

        public LayerMask layerMask = 1 << 24;
        [FormerlySerializedAs("outputMode")]
        public CompositeOutputMode debugView = CompositeOutputMode.None;

        private FogOfWarCompositePass _pass;
        private Material _compositeMaterial;

        public override void Create()
        {
            _pass = new FogOfWarCompositePass();
            _pass.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (EnsureCompositeMaterial() == false)
                return;

            RenderPassEvent targetEvent =
                debugView == CompositeOutputMode.None
                    ? RenderPassEvent.AfterRenderingOpaques
                    : RenderPassEvent.AfterRenderingTransparents;
            _pass.renderPassEvent = targetEvent;
            _pass.Setup(_compositeMaterial, layerMask, debugView);
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth);
#if UNITY_6000_0_OR_NEWER
            _pass.SetupRenderGraph();
#endif
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_compositeMaterial);
            _compositeMaterial = null;
        }

        private bool EnsureCompositeMaterial()
        {
            if (_compositeMaterial != null)
                return true;

            Shader shader = Shader.Find("Hidden/FOW/URP/FoW_MaskComposite");
            if (shader == null)
                return false;

            _compositeMaterial = CoreUtils.CreateEngineMaterial(shader);
            return _compositeMaterial != null;
        }

        private sealed class FogOfWarCompositePass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("FOW Visibility (Composite)");

            private static readonly List<ShaderTagId> ShaderTagIds = new List<ShaderTagId>(5)
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("UniversalGBuffer"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("LightweightForward"),
            };

            private static readonly int VisibilityColorTexId = Shader.PropertyToID("_FowVisibilityColorTex");
            private static readonly int VisibilityDepthTexId = Shader.PropertyToID("_FowVisibilityDepthTex");
            private static readonly int TempColorTexId = Shader.PropertyToID("_FowVisibilityCompositeTemp");
            private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");
            private static readonly int BlitScaleBiasId = Shader.PropertyToID("_BlitScaleBias");
            private static readonly int InvViewProjId = Shader.PropertyToID("_InvViewProj");
            private static readonly int UseWorldSamplingId = Shader.PropertyToID("_UseWorldSampling");
            private static readonly int VisibilityCutoffId = Shader.PropertyToID("_VisibilityCutoff");
            private static readonly int CompositeModeId = Shader.PropertyToID("_CompositeMode");

            private Material _compositeMaterial;
            private FilteringSettings _requestedFiltering;
            private int _selectedLayerMask;
            private FogOfWarCompositeFeature.CompositeOutputMode _outputMode;

            private RenderTargetIdentifier _source;
            private RenderTargetIdentifier _visibilityColorTarget;
            private RenderTargetIdentifier _visibilityDepthTarget;
            private RenderTargetIdentifier _tempColorTarget;

            public FogOfWarCompositePass()
            {
                _selectedLayerMask = ~0;
                _requestedFiltering = new FilteringSettings(RenderQueueRange.opaque, _selectedLayerMask);
            }

            public void Setup(
                Material compositeMaterial,
                LayerMask selectedLayerMask,
                FogOfWarCompositeFeature.CompositeOutputMode outputMode)
            {
                _compositeMaterial = compositeMaterial;
                _selectedLayerMask = selectedLayerMask.value;
                _requestedFiltering = new FilteringSettings(RenderQueueRange.opaque, _selectedLayerMask);
                _outputMode = outputMode;
            }

            private static int GetRevealerLayerMask(FogOfWarWorld world)
            {
                if (world == null || FogOfWarWorld.ActiveRevealers == null || FogOfWarWorld.NumActiveRevealers <= 0)
                    return 0;

                int mask = 0;
                for (int i = 0; i < FogOfWarWorld.NumActiveRevealers; i++)
                {
                    var revealer = FogOfWarWorld.ActiveRevealers[i];
                    if (revealer == null)
                        continue;

                    mask |= 1 << revealer.gameObject.layer;
                }

                return mask;
            }

            private FilteringSettings ResolveFilteringSettings(int revealerLayerMaskBits)
            {
                RenderQueueRange queueRange = RenderQueueRange.opaque;

                if (_outputMode == FogOfWarCompositeFeature.CompositeOutputMode.RenderFoW && revealerLayerMaskBits != 0)
                    return new FilteringSettings(queueRange, revealerLayerMaskBits);

                return new FilteringSettings(queueRange, _selectedLayerMask);
            }

#if UNITY_6000_0_OR_NEWER
            private sealed class DrawVisibilityPassData
            {
                public RendererListHandle rendererList;
            }

            private sealed class DrawLayerPassData
            {
                public RendererListHandle rendererList;
            }

            private sealed class CopyPassData
            {
                public TextureHandle source;
            }

            private sealed class CompositePassData
            {
                public Material material;
                public TextureHandle source;
                public TextureHandle visibilityColor;
                public TextureHandle visibilityDepth;
            }

            public void SetupRenderGraph()
            {
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_compositeMaterial == null)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                if (cameraData.renderType == CameraRenderType.Overlay)
                    return;
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                FogOfWarWorld world = FogOfWarWorld.instance;
                bool requiresFowData = _outputMode != FogOfWarCompositeFeature.CompositeOutputMode.RenderLayer;
                bool useWorldSampling = false;
                int revealerLayerMaskBits = 0;
                FilteringSettings activeFiltering = _requestedFiltering;

                if (requiresFowData)
                {
                    if (world == null || world.enabled == false)
                        return;

                    useWorldSampling = world.FOWSamplingMode != FogOfWarWorld.FogSampleMode.Texture;
                    if (useWorldSampling == false && world.GetFOWRT() == null)
                        return;

                    revealerLayerMaskBits = GetRevealerLayerMask(world);
                    world.InitializeFogProperties(_compositeMaterial);
                    world.UpdateMaterialProperties(_compositeMaterial);
                    _compositeMaterial.SetFloat(UseWorldSamplingId, useWorldSampling ? 1f : 0f);
                    _compositeMaterial.SetFloat(VisibilityCutoffId, Mathf.Clamp01(world.HiderSeenThreshold));
                }
                else
                {
                    _compositeMaterial.SetFloat(UseWorldSamplingId, 0f);
                    _compositeMaterial.SetFloat(VisibilityCutoffId, 0f);
                }

                _compositeMaterial.SetFloat(CompositeModeId, (float)_outputMode);

                Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(cameraData.camera.projectionMatrix, true);
                Matrix4x4 viewProjection = gpuProjectionMatrix * cameraData.camera.worldToCameraMatrix;
                _compositeMaterial.SetMatrix(InvViewProjId, viewProjection.inverse);

                activeFiltering = ResolveFilteringSettings(revealerLayerMaskBits);

                TextureHandle source = resourceData.activeColorTexture;

                if (_outputMode == FogOfWarCompositeFeature.CompositeOutputMode.RenderLayer)
                {
                    using (var builder = renderGraph.AddRasterRenderPass<DrawLayerPassData>("FOW Visibility RenderLayer Debug", out DrawLayerPassData passData, ProfilingSampler))
                    {
                        builder.SetRenderAttachment(source, 0, AccessFlags.WriteAll);
                        builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.WriteAll);
                        builder.AllowGlobalStateModification(true);

                        passData.rendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, activeFiltering);
                        if (!passData.rendererList.IsValid())
                            return;

                        builder.UseRendererList(passData.rendererList);
                        builder.SetRenderFunc(static (DrawLayerPassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.ClearRenderTarget(true, true, Color.black);
                            rgContext.cmd.DrawRendererList(data.rendererList);
                        });
                    }

                    resourceData.cameraColor = source;
                    return;
                }

                if (_outputMode == FogOfWarCompositeFeature.CompositeOutputMode.None)
                {
                    var baseCopyDesc = renderGraph.GetTextureDesc(source);
                    baseCopyDesc.name = "_FowVisibilityColorTex";
                    baseCopyDesc.clearBuffer = false;
                    baseCopyDesc.msaaSamples = MSAASamples.None;
                    baseCopyDesc.bindTextureMS = false;
                    TextureHandle baseCopy = renderGraph.CreateTexture(baseCopyDesc);

                    using (var copyBuilder = renderGraph.AddRasterRenderPass<CopyPassData>("FOW Copy Base", out CopyPassData copyData, ProfilingSampler))
                    {
                        copyData.source = source;
                        copyBuilder.UseTexture(source, AccessFlags.Read);
                        copyBuilder.SetRenderAttachment(baseCopy, 0, AccessFlags.WriteAll);
                        copyBuilder.AllowGlobalStateModification(true);
                        copyBuilder.SetRenderFunc(static (CopyPassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                            Blitter.BlitTexture(rgContext.cmd, data.source, Vector2.one, 0, false);
                        });
                    }

                    var layerResultMsaaDesc = renderGraph.GetTextureDesc(source);
                    layerResultMsaaDesc.name = "_FowVisibilityLayerResultMSAA";
                    layerResultMsaaDesc.clearBuffer = false;
                    TextureHandle layerResultMsaa = renderGraph.CreateTexture(layerResultMsaaDesc);

                    var layerResultDesc = renderGraph.GetTextureDesc(source);
                    layerResultDesc.name = "_FowVisibilityLayerResult";
                    layerResultDesc.clearBuffer = false;
                    layerResultDesc.msaaSamples = MSAASamples.None;
                    layerResultDesc.bindTextureMS = false;
                    TextureHandle layerResult = renderGraph.CreateTexture(layerResultDesc);

                    // Initialize layer-result from base scene.
                    using (var copyLayerBuilder = renderGraph.AddRasterRenderPass<CopyPassData>("FOW Copy Base To LayerResult", out CopyPassData copyLayerData, ProfilingSampler))
                    {
                        copyLayerData.source = baseCopy;
                        copyLayerBuilder.UseTexture(baseCopy, AccessFlags.Read);
                        copyLayerBuilder.SetRenderAttachment(layerResultMsaa, 0, AccessFlags.WriteAll);
                        copyLayerBuilder.AllowGlobalStateModification(true);
                        copyLayerBuilder.SetRenderFunc(static (CopyPassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                            Blitter.BlitTexture(rgContext.cmd, data.source, Vector2.one, 0, false);
                        });
                    }

                    // Draw selected layer onto the offscreen layer-result target using camera depth.
                    using (var drawBuilder = renderGraph.AddRasterRenderPass<DrawLayerPassData>("FOW Draw LayerResult", out DrawLayerPassData drawData, ProfilingSampler))
                    {
                        drawBuilder.SetRenderAttachment(layerResultMsaa, 0, AccessFlags.ReadWrite);
                        drawBuilder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);
                        drawBuilder.AllowGlobalStateModification(true);

                        drawData.rendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, activeFiltering);
                        if (!drawData.rendererList.IsValid())
                            return;

                        drawBuilder.UseRendererList(drawData.rendererList);
                        drawBuilder.SetRenderFunc(static (DrawLayerPassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.DrawRendererList(data.rendererList);
                        });
                    }

                    // Resolve/copy MSAA layer-result into a sampleable non-MSAA texture for composite.
                    using (var resolveBuilder = renderGraph.AddRasterRenderPass<CopyPassData>("FOW Resolve LayerResult", out CopyPassData resolveData, ProfilingSampler))
                    {
                        resolveData.source = layerResultMsaa;
                        resolveBuilder.UseTexture(layerResultMsaa, AccessFlags.Read);
                        resolveBuilder.SetRenderAttachment(layerResult, 0, AccessFlags.WriteAll);
                        resolveBuilder.AllowGlobalStateModification(true);
                        resolveBuilder.SetRenderFunc(static (CopyPassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                            Blitter.BlitTexture(rgContext.cmd, data.source, Vector2.one, 0, false);
                        });
                    }

                    var bothDestinationDesc = renderGraph.GetTextureDesc(source);
                    bothDestinationDesc.name = "_FowVisibilityComposite";
                    bothDestinationDesc.clearBuffer = false;
                    TextureHandle bothDestination = renderGraph.CreateTexture(bothDestinationDesc);

                    using (var compositeBuilder = renderGraph.AddRasterRenderPass<CompositePassData>("FOW Composite", out CompositePassData bothData, ProfilingSampler))
                    {
                        bothData.material = _compositeMaterial;
                        bothData.source = baseCopy;
                        bothData.visibilityColor = layerResult;
                        bothData.visibilityDepth = resourceData.activeDepthTexture;

                        compositeBuilder.UseTexture(baseCopy, AccessFlags.Read);
                        compositeBuilder.UseTexture(layerResult, AccessFlags.Read);
                        compositeBuilder.UseTexture(resourceData.activeDepthTexture, AccessFlags.Read);
                        compositeBuilder.SetRenderAttachment(bothDestination, 0, AccessFlags.WriteAll);
                        compositeBuilder.AllowGlobalStateModification(true);
                        compositeBuilder.SetRenderFunc(static (CompositePassData data, RasterGraphContext rgContext) =>
                        {
                            rgContext.cmd.SetGlobalTexture(VisibilityColorTexId, data.visibilityColor);
                            rgContext.cmd.SetGlobalTexture(VisibilityDepthTexId, data.visibilityDepth);
                            rgContext.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                            Blitter.BlitTexture(rgContext.cmd, data.source, Vector2.one, data.material, 0);
                        });
                    }

                    resourceData.cameraColor = bothDestination;
                    return;
                }

                var visibilityColorDesc = renderGraph.GetTextureDesc(source);
                visibilityColorDesc.name = "_FowVisibilityColorTex";
                visibilityColorDesc.clearBuffer = true;
                visibilityColorDesc.clearColor = Color.clear;
                visibilityColorDesc.msaaSamples = MSAASamples.None;
                visibilityColorDesc.bindTextureMS = false;
                visibilityColorDesc.depthBufferBits = DepthBits.None;
                visibilityColorDesc.colorFormat = GraphicsFormat.R8G8B8A8_UNorm;
                TextureHandle visibilityColor = renderGraph.CreateTexture(visibilityColorDesc);

                var visibilityDepthDesc = renderGraph.GetTextureDesc(resourceData.activeDepthTexture);
                visibilityDepthDesc.name = "_FowVisibilityDepthTex";
                visibilityDepthDesc.clearBuffer = true;
                visibilityDepthDesc.clearColor = Color.black;
                visibilityDepthDesc.msaaSamples = MSAASamples.None;
                visibilityDepthDesc.bindTextureMS = false;
                TextureHandle visibilityDepth = renderGraph.CreateTexture(visibilityDepthDesc);

                using (var builder = renderGraph.AddRasterRenderPass<DrawVisibilityPassData>("FOW Visibility Draw", out DrawVisibilityPassData passData, ProfilingSampler))
                {
                    builder.SetRenderAttachment(visibilityColor, 0, AccessFlags.WriteAll);
                    builder.SetRenderAttachmentDepth(visibilityDepth, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);

                    passData.rendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, activeFiltering);
                    if (!passData.rendererList.IsValid())
                        return;

                    builder.UseRendererList(passData.rendererList);

                    builder.SetRenderFunc(static (DrawVisibilityPassData data, RasterGraphContext rgContext) =>
                    {
                        rgContext.cmd.DrawRendererList(data.rendererList);
                    });
                }

                var destinationDesc = renderGraph.GetTextureDesc(source);
                destinationDesc.name = "_FowVisibilityComposite";
                destinationDesc.clearBuffer = true;
                destinationDesc.clearColor = Color.black;
                TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

                using (var builder = renderGraph.AddRasterRenderPass<CompositePassData>("FOW Visibility Composite", out CompositePassData passData, ProfilingSampler))
                {
                    passData.material = _compositeMaterial;
                    passData.source = source;
                    passData.visibilityColor = visibilityColor;
                    passData.visibilityDepth = visibilityDepth;

                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(visibilityColor, AccessFlags.Read);
                    builder.UseTexture(visibilityDepth, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.WriteAll);
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc(static (CompositePassData data, RasterGraphContext rgContext) =>
                    {
                        rgContext.cmd.SetGlobalTexture(VisibilityColorTexId, data.visibilityColor);
                        rgContext.cmd.SetGlobalTexture(VisibilityDepthTexId, data.visibilityDepth);
                        rgContext.cmd.SetGlobalVector(BlitScaleBiasId, new Vector4(1f, 1f, 0f, 0f));
                        Blitter.BlitTexture(rgContext.cmd, data.source, Vector2.one, data.material, 0);
                    });
                }

                resourceData.cameraColor = destination;
            }

            private RendererListHandle CreateRendererList(
                RenderGraph renderGraph,
                UniversalRenderingData renderingData,
                UniversalCameraData cameraData,
                UniversalLightData lightData,
                FilteringSettings filtering)
            {
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(ShaderTagIds, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var param = new RendererListParams(renderingData.cullResults, drawingSettings, filtering);
                return renderGraph.CreateRendererList(param);
            }
#endif

#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var renderer = renderingData.cameraData.renderer;

#if UNITY_2022_2_OR_NEWER
                _source = renderer.cameraColorTargetHandle;
#else
                _source = renderer.cameraColorTarget;
#endif

                RenderTextureDescriptor colorDesc = renderingData.cameraData.cameraTargetDescriptor;
                colorDesc.depthBufferBits = 0;
                colorDesc.colorFormat = RenderTextureFormat.ARGB32;
                colorDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(VisibilityColorTexId, colorDesc, FilterMode.Bilinear);
                _visibilityColorTarget = new RenderTargetIdentifier(VisibilityColorTexId);

                RenderTextureDescriptor depthDesc = renderingData.cameraData.cameraTargetDescriptor;
                depthDesc.colorFormat = RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(VisibilityDepthTexId, depthDesc, FilterMode.Point);
                _visibilityDepthTarget = new RenderTargetIdentifier(VisibilityDepthTexId);

                RenderTextureDescriptor tempDesc = renderingData.cameraData.cameraTargetDescriptor;
                tempDesc.depthBufferBits = 0;
                tempDesc.msaaSamples = 1;
                cmd.GetTemporaryRT(TempColorTexId, tempDesc, FilterMode.Bilinear);
                _tempColorTarget = new RenderTargetIdentifier(TempColorTexId);
            }

#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_compositeMaterial == null)
                    return;
                if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                    return;

                Camera camera = renderingData.cameraData.camera;
                if (camera == null)
                    return;

                FogOfWarWorld world = FogOfWarWorld.instance;
                bool requiresFowData = _outputMode != FogOfWarCompositeFeature.CompositeOutputMode.RenderLayer;
                bool useWorldSampling = false;
                int revealerLayerMaskBits = 0;
                FilteringSettings activeFiltering = _requestedFiltering;

                if (requiresFowData)
                {
                    if (world == null || world.enabled == false)
                        return;

                    useWorldSampling = world.FOWSamplingMode != FogOfWarWorld.FogSampleMode.Texture;
                    if (useWorldSampling == false && world.GetFOWRT() == null)
                        return;

                    revealerLayerMaskBits = GetRevealerLayerMask(world);
                    world.InitializeFogProperties(_compositeMaterial);
                    world.UpdateMaterialProperties(_compositeMaterial);
                    _compositeMaterial.SetFloat(UseWorldSamplingId, useWorldSampling ? 1f : 0f);
                    _compositeMaterial.SetFloat(VisibilityCutoffId, Mathf.Clamp01(world.HiderSeenThreshold));
                }
                else
                {
                    _compositeMaterial.SetFloat(UseWorldSamplingId, 0f);
                    _compositeMaterial.SetFloat(VisibilityCutoffId, 0f);
                }

                activeFiltering = ResolveFilteringSettings(revealerLayerMaskBits);

                _compositeMaterial.SetFloat(CompositeModeId, (float)_outputMode);

                Matrix4x4 gpuProjectionMatrix = GL.GetGPUProjectionMatrix(camera.projectionMatrix, true);
                Matrix4x4 viewProjection = gpuProjectionMatrix * camera.worldToCameraMatrix;
                _compositeMaterial.SetMatrix(InvViewProjId, viewProjection.inverse);

#if UNITY_2022_2_OR_NEWER
                RenderTargetIdentifier depthTarget = renderingData.cameraData.renderer.cameraDepthTargetHandle;
#else
                RenderTargetIdentifier depthTarget = renderingData.cameraData.renderer.cameraDepthTarget;
#endif

                if (_outputMode == FogOfWarCompositeFeature.CompositeOutputMode.RenderLayer)
                {
                    CommandBuffer directCmd = CommandBufferPool.Get();
                    using (new ProfilingScope(directCmd, ProfilingSampler))
                    {
                        context.ExecuteCommandBuffer(directCmd);
                        directCmd.Clear();

                        directCmd.SetRenderTarget(
                            _source,
                            RenderBufferLoadAction.DontCare,
                            RenderBufferStoreAction.Store,
                            depthTarget,
                            RenderBufferLoadAction.DontCare,
                            RenderBufferStoreAction.Store);
                        directCmd.ClearRenderTarget(true, true, Color.black);
                        context.ExecuteCommandBuffer(directCmd);
                        directCmd.Clear();

                        var sort = new SortingSettings(camera) { criteria = renderingData.cameraData.defaultOpaqueSortFlags };
                        var draw = CreateDrawingSettings(ShaderTagIds, ref renderingData, sort.criteria);
                        context.DrawRenderers(renderingData.cullResults, ref draw, ref activeFiltering);
                    }

                    context.ExecuteCommandBuffer(directCmd);
                    CommandBufferPool.Release(directCmd);
                    return;
                }

                if (_outputMode == FogOfWarCompositeFeature.CompositeOutputMode.None)
                {
                    CommandBuffer bothCmd = CommandBufferPool.Get();
                    using (new ProfilingScope(bothCmd, ProfilingSampler))
                    {
                        // Snapshot base scene.
                        bothCmd.Blit(_source, _visibilityColorTarget);
                        context.ExecuteCommandBuffer(bothCmd);
                        bothCmd.Clear();

                        // Initialize layer-result from base scene.
                        bothCmd.Blit(_visibilityColorTarget, _tempColorTarget);
                        context.ExecuteCommandBuffer(bothCmd);
                        bothCmd.Clear();

                        // Draw selected layer onto offscreen layer-result using camera depth.
                        bothCmd.SetRenderTarget(
                            _tempColorTarget,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store,
                            depthTarget,
                            RenderBufferLoadAction.Load,
                            RenderBufferStoreAction.Store);
                        context.ExecuteCommandBuffer(bothCmd);
                        bothCmd.Clear();

                        var sort = new SortingSettings(camera) { criteria = renderingData.cameraData.defaultOpaqueSortFlags };
                        var draw = CreateDrawingSettings(ShaderTagIds, ref renderingData, sort.criteria);
                        context.DrawRenderers(renderingData.cullResults, ref draw, ref activeFiltering);

                        // Compose as: lerp(base, renderLayerResult, renderFoW.r)
                        bothCmd.SetGlobalTexture(BlitTextureId, _visibilityColorTarget);
                        bothCmd.SetGlobalTexture(VisibilityColorTexId, _tempColorTarget);
                        bothCmd.Blit(_visibilityColorTarget, _source, _compositeMaterial, 0);
                    }

                    context.ExecuteCommandBuffer(bothCmd);
                    CommandBufferPool.Release(bothCmd);
                    return;
                }

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, ProfilingSampler))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // 1) Render FoW layer to offscreen color/depth.
                    cmd.SetRenderTarget(_visibilityColorTarget, _visibilityDepthTarget);
                    cmd.ClearRenderTarget(true, true, Color.clear);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    var opaqueSort = new SortingSettings(camera) { criteria = renderingData.cameraData.defaultOpaqueSortFlags };
                    var opaqueDraw = CreateDrawingSettings(ShaderTagIds, ref renderingData, opaqueSort.criteria);
                    context.DrawRenderers(renderingData.cullResults, ref opaqueDraw, ref activeFiltering);

                    // 2) Debug output must be black where FoW is not visible.
                    cmd.SetRenderTarget(
                        _source,
                        RenderBufferLoadAction.DontCare,
                        RenderBufferStoreAction.Store,
                        depthTarget,
                        RenderBufferLoadAction.Load,
                        RenderBufferStoreAction.Store);
                    cmd.ClearRenderTarget(false, true, Color.black);
                    cmd.SetRenderTarget(_tempColorTarget);
                    cmd.ClearRenderTarget(false, true, Color.black);
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    // 3) Composite captured layer back to camera color with FoW visibility.
                    cmd.SetGlobalTexture(BlitTextureId, _source);
                    cmd.SetGlobalTexture(VisibilityColorTexId, _visibilityColorTarget);
                    cmd.SetGlobalTexture(VisibilityDepthTexId, _visibilityDepthTarget);
                    cmd.Blit(_source, _tempColorTarget, _compositeMaterial, 0);
                    cmd.Blit(_tempColorTarget, _source);
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(VisibilityColorTexId);
                cmd.ReleaseTemporaryRT(VisibilityDepthTexId);
                cmd.ReleaseTemporaryRT(TempColorTexId);
            }
        }
    }
}
