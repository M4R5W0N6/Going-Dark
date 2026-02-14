namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public sealed class VisionMaskPass : CustomPass
	{
		private const string LOCAL_LAYER_NAME = "Local";

		[SerializeField]
		private uint _lightLayerMask = uint.MaxValue;
		[SerializeField]
		private LayerMask _localLayerMask;

		private const string SHADER_NAME = "Hidden/TPSBR/HDRP/VisionMask";
		private static readonly int GLOBAL_TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_VisionMaskGlobalTargetLightLayerMask");
		private static readonly int TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_TargetLightLayerMask");
		private static readonly int USE_LINEAR_DEPTH_TEX = Shader.PropertyToID("_UseLinearDepthTex");
		private static readonly int USE_LOCAL_DEPTH_TEX = Shader.PropertyToID("_UseLocalDepthTex");
		private static readonly int LOCAL_DEPTH_TEX = Shader.PropertyToID("_LocalDepthTex");

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;

		protected override bool executeInSceneView => true;

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			Shader shader = Shader.Find(SHADER_NAME);
			if (shader == null)
			{
				Debug.LogError($"[VisionMaskPass] Shader not found: {SHADER_NAME}. It may be stripped from the player build.");
				return;
			}

			_material = CoreUtils.CreateEngineMaterial(shader);
			_propertyBlock = new MaterialPropertyBlock();
			clearFlags = ClearFlag.None;
		}

		protected override void Execute(CustomPassContext ctx)
		{
			if (_material == null || _propertyBlock == null)
				return;

			VisionPassBuffers.Ensure();

			if (_localLayerMask.value != 0)
			{
				CustomPassUtils.RenderDepthFromCamera(
					ctx,
					ctx.hdCamera.camera,
					VisionPassBuffers.LocalDepth,
					ctx.cameraDepthBuffer,
					ClearFlag.Color,
					_localLayerMask,
					RenderQueueType.All);
			}

			_propertyBlock.Clear();
			_propertyBlock.SetInt(TARGET_LIGHT_LAYER_MASK, unchecked((int)_lightLayerMask));
			_propertyBlock.SetFloat(USE_LINEAR_DEPTH_TEX, 0.0f);
			_propertyBlock.SetFloat(USE_LOCAL_DEPTH_TEX, _localLayerMask.value != 0 ? 1.0f : 0.0f);
			_propertyBlock.SetTexture(LOCAL_DEPTH_TEX, VisionPassBuffers.LocalDepth);
			Shader.SetGlobalInt(GLOBAL_TARGET_LIGHT_LAYER_MASK, unchecked((int)_lightLayerMask));

			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.VisionMask, ClearFlag.Color);
			CoreUtils.DrawFullScreen(ctx.cmd, _material, _propertyBlock, shaderPassId: 0);
		}

		protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera hdCamera)
		{
			ResolveLocalLayerMaskIfUnset();
			cullingParameters.cullingMask |= (uint)_localLayerMask.value;
		}

		private void ResolveLocalLayerMaskIfUnset()
		{
			if (_localLayerMask.value != 0)
				return;

			int localLayer = LayerMask.NameToLayer(LOCAL_LAYER_NAME);
			if (localLayer >= 0)
			{
				_localLayerMask = 1 << localLayer;
			}
		}

		protected override void Cleanup()
		{
			CoreUtils.Destroy(_material);
			_material = null;
			_propertyBlock = null;
		}
	}
}
