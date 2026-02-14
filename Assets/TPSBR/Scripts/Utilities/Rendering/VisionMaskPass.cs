namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public sealed class VisionMaskPass : CustomPass
	{
		[SerializeField]
		private uint _lightLayerMask = uint.MaxValue;

		private const string SHADER_NAME = "Hidden/TPSBR/HDRP/VisionMask";
		private static readonly int GLOBAL_TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_VisionMaskGlobalTargetLightLayerMask");
		private static readonly int TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_TargetLightLayerMask");
		private static readonly int USE_LINEAR_DEPTH_TEX = Shader.PropertyToID("_UseLinearDepthTex");

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

			_propertyBlock.Clear();
			_propertyBlock.SetInt(TARGET_LIGHT_LAYER_MASK, unchecked((int)_lightLayerMask));
			_propertyBlock.SetFloat(USE_LINEAR_DEPTH_TEX, 0.0f);
			Shader.SetGlobalInt(GLOBAL_TARGET_LIGHT_LAYER_MASK, unchecked((int)_lightLayerMask));

			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.VisionMask, ClearFlag.Color);
			CoreUtils.DrawFullScreen(ctx.cmd, _material, _propertyBlock, shaderPassId: 0);
		}

		protected override void Cleanup()
		{
			CoreUtils.Destroy(_material);
			_material = null;
			_propertyBlock = null;
		}
	}
}
