namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public class VisionHiddenPass : CustomPass
	{
		[SerializeField]
		private LayerMask _hiddenLayerMask;
		private const string VISION_MASK_SHADER_NAME = "Hidden/TPSBR/HDRP/VisionMask";
		private static readonly int TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_TargetLightLayerMask");
		private static readonly int USE_LINEAR_DEPTH_TEX = Shader.PropertyToID("_UseLinearDepthTex");
		private static readonly int LINEAR_DEPTH_TEX = Shader.PropertyToID("_LinearDepthTex");
		private static readonly int GLOBAL_TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_VisionMaskGlobalTargetLightLayerMask");

		private Material _visionMaskMaterial;
		private MaterialPropertyBlock _visionMaskProperties;

		protected override bool executeInSceneView => true;

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			if (_hiddenLayerMask.value == 0)
			{
				int hiddenLayer = LayerMask.NameToLayer("Hidden");
				if (hiddenLayer >= 0)
				{
					_hiddenLayerMask = 1 << hiddenLayer;
				}
			}

			Shader visionMaskShader = Shader.Find(VISION_MASK_SHADER_NAME);
			if (visionMaskShader == null)
			{
				Debug.LogError($"[VisionHiddenPass] Shader not found: {VISION_MASK_SHADER_NAME}.");
				return;
			}

			_visionMaskMaterial = CoreUtils.CreateEngineMaterial(visionMaskShader);
			_visionMaskProperties = new MaterialPropertyBlock();
		}

		protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera hdCamera)
		{
			cullingParameters.cullingMask |= (uint)_hiddenLayerMask.value;
		}

		protected override void Execute(CustomPassContext ctx)
		{
			if (_hiddenLayerMask.value == 0)
				return;
			if (_visionMaskMaterial == null || _visionMaskProperties == null)
				return;

			VisionPassBuffers.Ensure();

			// Step 1: write visible hidden eye depth (depth-tested against world) into an offscreen depth texture.
			CustomPassUtils.RenderDepthFromCamera(
				ctx,
				ctx.hdCamera.camera,
				VisionPassBuffers.HiddenDepth,
				ctx.cameraDepthBuffer,
				ClearFlag.Color,
				_hiddenLayerMask,
				RenderQueueType.All);

			// Step 2: evaluate the vision mask at hidden-pixel depth so hidden composition doesn't sample background mask.
			int lightLayerMask = Shader.GetGlobalInt(GLOBAL_TARGET_LIGHT_LAYER_MASK);
			if (lightLayerMask == 0)
			{
				lightLayerMask = -1;
			}

			_visionMaskProperties.Clear();
			_visionMaskProperties.SetInt(TARGET_LIGHT_LAYER_MASK, lightLayerMask);
			_visionMaskProperties.SetFloat(USE_LINEAR_DEPTH_TEX, 1.0f);
			_visionMaskProperties.SetTexture(LINEAR_DEPTH_TEX, VisionPassBuffers.HiddenDepth);
			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.HiddenMask, ClearFlag.Color);
			CoreUtils.DrawFullScreen(ctx.cmd, _visionMaskMaterial, _visionMaskProperties, shaderPassId: 0);

			// Step 3: render hidden color offscreen while using camera depth for correct occlusion.
			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.HiddenColor, ctx.cameraDepthBuffer, ClearFlag.Color);
			var colorState = new RenderStateBlock(RenderStateMask.Depth)
			{
				depthState = new DepthState(false, CompareFunction.LessEqual)
			};

			CustomPassUtils.DrawRenderers(ctx, _hiddenLayerMask, RenderQueueType.All, overrideRenderState: colorState);
		}

		protected override void Cleanup()
		{
			CoreUtils.Destroy(_visionMaskMaterial);
			_visionMaskMaterial = null;
			_visionMaskProperties = null;
		}
	}
}
