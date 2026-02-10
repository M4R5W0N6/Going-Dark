using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using Unity.Collections;
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace FOW
{
    /// <summary>
    /// Renders layer-mask objects only where the current Fog Of War texture is revealed.
    /// Keeps materials opaque by using a stencil prepass instead of transparent FoW shaders.
    /// </summary>
    public sealed class FogOfWarStencilFeature : ScriptableRendererFeature
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        public LayerMask layerMask = 1 << 24;

        private FogOfWarVisibilityPass _pass;
        private Material _stencilMaterial;

        public override void Create()
        {
            _pass = new FogOfWarVisibilityPass();
            _pass.renderPassEvent = renderPassEvent;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (EnsureStencilMaterial() == false)
                return;

            _pass.renderPassEvent = renderPassEvent;
            _pass.Setup(_stencilMaterial, layerMask);
#if UNITY_6000_0_OR_NEWER
            _pass.SetupRenderGraph();
#endif
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_stencilMaterial);
            _stencilMaterial = null;
        }

        private bool EnsureStencilMaterial()
        {
            if (_stencilMaterial != null)
                return true;

            Shader shader = Shader.Find("Hidden/FOW/URP/FoW_MaskStencil");
            if (shader == null)
                return false;

            _stencilMaterial = CoreUtils.CreateEngineMaterial(shader);
            return _stencilMaterial != null;
        }

        private sealed class FogOfWarVisibilityPass : ScriptableRenderPass
        {
            private static readonly ProfilingSampler ProfilingSampler = new ProfilingSampler("FOW Visibility");

            private static readonly List<ShaderTagId> ShaderTagIds = new List<ShaderTagId>(5)
            {
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
                new ShaderTagId("UniversalGBuffer"),
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("LightweightForward"),
            };

            private static readonly int FowRtId = Shader.PropertyToID("_FowRT");
            private static readonly int WorldBoundsId = Shader.PropertyToID("_worldBounds");
            private static readonly int FowPlaneId = Shader.PropertyToID("_fowPlane");
            private static readonly int VisibilityThresholdId = Shader.PropertyToID("_VisibilityThreshold");
            private static readonly int UseWorldSamplingId = Shader.PropertyToID("_UseWorldSampling");
            private static readonly int UseDitheredSoftEdgeId = Shader.PropertyToID("_UseDitheredSoftEdge");

            private Material _stencilMaterial;
            private FilteringSettings _filtering;
            private bool _warnedMissingFowTexture;
            private const FogOfWarWorld.FogSampleMode FallbackSamplingMode = FogOfWarWorld.FogSampleMode.Texture;
            private const bool UseDitheredSoftEdge = true;

            private RenderStateBlock _clearStencilState;
            private RenderStateBlock _writeStencilState;
            private RenderStateBlock _testStencilState;

            public FogOfWarVisibilityPass()
            {
                _filtering = new FilteringSettings(RenderQueueRange.all, ~0);

                _clearStencilState = CreateStencilState(0, CompareFunction.Always, StencilOp.Replace);
                _writeStencilState = CreateStencilState(1, CompareFunction.Always, StencilOp.Replace);
                _testStencilState = CreateStencilState(1, CompareFunction.Equal, StencilOp.Keep);
            }

            public void Setup(Material stencilMaterial, LayerMask selectedLayerMask)
            {
                _stencilMaterial = stencilMaterial;
                _filtering = new FilteringSettings(RenderQueueRange.all, selectedLayerMask);
            }

#if UNITY_6000_0_OR_NEWER
            private sealed class RenderGraphPassData
            {
                public bool hasFowTexture;
                public RendererListHandle fallbackRendererList;
                public RendererListHandle clearStencilRendererList;
                public RendererListHandle writeStencilRendererList;
                public RendererListHandle drawVisibleRendererList;
            }

            private static readonly ShaderTagId[] RenderStateTagValues = { ShaderTagId.none };
            private static readonly RenderStateBlock[] RenderStateBlocks = new RenderStateBlock[1];
            private const string RenderGraphPassName = "FOW Visibility";

            public void SetupRenderGraph()
            {
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_stencilMaterial == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                if (cameraData.camera == null)
                    return;
                if (cameraData.renderType == CameraRenderType.Overlay)
                    return;

                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer)
                    return;

                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                bool hasFowData = SetupStencilMaterialForCurrentFoW();

                using (var builder = renderGraph.AddRasterRenderPass<RenderGraphPassData>(RenderGraphPassName, out RenderGraphPassData passData, ProfilingSampler))
                {
                    builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.ReadWrite);

                    if (hasFowData == false)
                    {
                        if (_warnedMissingFowTexture == false)
                        {
                            FogOfWarWorld.FogSampleMode mode = GetEffectiveSamplingMode(null);
                            Debug.LogWarning(mode == FogOfWarWorld.FogSampleMode.Texture
                                ? "[FogOfWarStencilFeature] FOW texture is unavailable. Set FogOfWarWorld sampling to Texture or Both for stencil masking."
                                : "[FogOfWarStencilFeature] FoW world-space data is unavailable for Pixel-Perfect stencil masking.");
                            _warnedMissingFowTexture = true;
                        }

                        passData.hasFowTexture = false;
                        passData.fallbackRendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, _filtering, null, 0, applyRenderState: false, _clearStencilState);
                        if (!passData.fallbackRendererList.IsValid())
                            return;

                        builder.UseRendererList(passData.fallbackRendererList);
                        builder.SetRenderFunc(static (RenderGraphPassData data, RasterGraphContext context) =>
                        {
                            context.cmd.DrawRendererList(data.fallbackRendererList);
                        });
                        return;
                    }

                    _warnedMissingFowTexture = false;

                    passData.hasFowTexture = true;
                    passData.clearStencilRendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, _filtering, _stencilMaterial, 0, applyRenderState: true, _clearStencilState);
                    passData.writeStencilRendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, _filtering, _stencilMaterial, 1, applyRenderState: true, _writeStencilState);
                    passData.drawVisibleRendererList = CreateRendererList(renderGraph, renderingData, cameraData, lightData, _filtering, null, 0, applyRenderState: true, _testStencilState);

                    if (!passData.clearStencilRendererList.IsValid() ||
                        !passData.writeStencilRendererList.IsValid() ||
                        !passData.drawVisibleRendererList.IsValid())
                        return;

                    builder.UseRendererList(passData.clearStencilRendererList);
                    builder.UseRendererList(passData.writeStencilRendererList);
                    builder.UseRendererList(passData.drawVisibleRendererList);
                    builder.SetRenderFunc(static (RenderGraphPassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawRendererList(data.clearStencilRendererList);
                        context.cmd.DrawRendererList(data.writeStencilRendererList);
                        context.cmd.DrawRendererList(data.drawVisibleRendererList);
                    });
                }
            }

            private RendererListHandle CreateRendererList(
                RenderGraph renderGraph,
                UniversalRenderingData renderingData,
                UniversalCameraData cameraData,
                UniversalLightData lightData,
                FilteringSettings filteringSettings,
                Material overrideMaterial,
                int overrideMaterialPassIndex,
                bool applyRenderState,
                RenderStateBlock renderStateBlock)
            {
                SortingCriteria sortingCriteria = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(ShaderTagIds, renderingData, cameraData, lightData, sortingCriteria);
                drawingSettings.overrideMaterial = overrideMaterial;
                drawingSettings.overrideMaterialPassIndex = overrideMaterialPassIndex;

                RendererListParams rendererListParams;
                if (applyRenderState == false)
                {
                    rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings);
                }
                else
                {
                    RenderStateTagValues[0] = ShaderTagId.none;
                    RenderStateBlocks[0] = renderStateBlock;
                    NativeArray<ShaderTagId> tagValues = new NativeArray<ShaderTagId>(RenderStateTagValues, Allocator.Temp);
                    NativeArray<RenderStateBlock> stateBlocks = new NativeArray<RenderStateBlock>(RenderStateBlocks, Allocator.Temp);

                    rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, filteringSettings)
                    {
                        tagValues = tagValues,
                        stateBlocks = stateBlocks,
                        isPassTagName = false
                    };
                }

                return renderGraph.CreateRendererList(rendererListParams);
            }
#endif

#if UNITY_6000_0_OR_NEWER
            [System.Obsolete]
#endif
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_stencilMaterial == null)
                    return;

                Camera camera = renderingData.cameraData.camera;
                if (camera == null)
                    return;

                if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
                    return;

                CommandBuffer cmd = CommandBufferPool.Get();
                using (new ProfilingScope(cmd, ProfilingSampler))
                {
                    context.ExecuteCommandBuffer(cmd);
                    cmd.Clear();

                    var sortingSettings = new SortingSettings(camera) { criteria = SortingCriteria.CommonOpaque };
                    var drawingSettings = CreateDrawingSettings(ShaderTagIds, ref renderingData, sortingSettings.criteria);

                    if (SetupStencilMaterialForCurrentFoW() == false)
                    {
                        if (_warnedMissingFowTexture == false)
                        {
                            FogOfWarWorld.FogSampleMode mode = GetEffectiveSamplingMode(null);
                            Debug.LogWarning(mode == FogOfWarWorld.FogSampleMode.Texture
                                ? "[FogOfWarStencilFeature] FOW texture is unavailable. Set FogOfWarWorld sampling to Texture or Both for stencil masking."
                                : "[FogOfWarStencilFeature] FoW world-space data is unavailable for Pixel-Perfect stencil masking.");
                            _warnedMissingFowTexture = true;
                        }

                        // Fallback: if FoW texture is unavailable, render selected layer normally.
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filtering);
                    }
                    else
                    {
                        _warnedMissingFowTexture = false;

                        // 1) Clear stencil footprint to 0.
                        drawingSettings.overrideMaterial = _stencilMaterial;
                        drawingSettings.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filtering, ref _clearStencilState);

                        // 2) Write stencil = 1 only where selected-layer pixels are inside revealed FoW.
                        drawingSettings.overrideMaterialPassIndex = 1;
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filtering, ref _writeStencilState);

                        // 3) Render selected-layer objects with original materials, but only on stencil==1.
                        drawingSettings.overrideMaterial = null;
                        drawingSettings.overrideMaterialPassIndex = 0;
                        context.DrawRenderers(renderingData.cullResults, ref drawingSettings, ref _filtering, ref _testStencilState);
                    }
                }

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            private bool SetupStencilMaterialForCurrentFoW()
            {
                FogOfWarWorld world = FogOfWarWorld.instance;
                if (world == null || world.enabled == false)
                    return false;

                FogOfWarWorld.FogSampleMode mode = GetEffectiveSamplingMode(world);
                bool useWorldSampling = mode != FogOfWarWorld.FogSampleMode.Texture;
                _stencilMaterial.SetFloat(UseWorldSamplingId, useWorldSampling ? 1f : 0f);
                _stencilMaterial.SetFloat(UseDitheredSoftEdgeId, UseDitheredSoftEdge ? 1f : 0f);

                world.InitializeFogProperties(_stencilMaterial);
                world.UpdateMaterialProperties(_stencilMaterial);

                _stencilMaterial.SetVector(WorldBoundsId, FogOfWarWorld.CachedFowShaderBounds);
                _stencilMaterial.SetInt(FowPlaneId, GetFowPlane(world));
                _stencilMaterial.SetFloat(VisibilityThresholdId, Mathf.Clamp01(world.HiderSeenThreshold));

                if (useWorldSampling)
                    return true;

                RenderTexture fowRt = world.GetFOWRT();
                if (fowRt == null)
                    return false;

                _stencilMaterial.SetTexture(FowRtId, fowRt);

                return true;
            }

            private FogOfWarWorld.FogSampleMode GetEffectiveSamplingMode(FogOfWarWorld world)
            {
                if (world != null)
                    return world.FOWSamplingMode;

                return FallbackSamplingMode;
            }

            private static int GetFowPlane(FogOfWarWorld world)
            {
                if (world.is2D)
                    return 0;

                switch (world.GamePlaneOrientation)
                {
                    case FogOfWarWorld.GamePlane.XZ:
                        return 1;
                    case FogOfWarWorld.GamePlane.XY:
                        return 2;
                    case FogOfWarWorld.GamePlane.ZY:
                        return 3;
                    default:
                        return 1;
                }
            }

            private static RenderStateBlock CreateStencilState(int reference, CompareFunction compareFunction, StencilOp passOperation)
            {
                var stencilState = new StencilState(
                    enabled: true,
                    readMask: 255,
                    writeMask: 255,
                    compareFunction: compareFunction,
                    passOperation: passOperation,
                    failOperation: StencilOp.Keep,
                    zFailOperation: StencilOp.Keep);

                return new RenderStateBlock(RenderStateMask.Stencil)
                {
                    stencilReference = reference,
                    stencilState = stencilState
                };
            }
        }
    }
}
