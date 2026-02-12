	namespace TPSBR
	{
	using UnityEngine;
	using UnityEngine.Experimental.Rendering;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public sealed class SightMaskPass : CustomPass
	{
		[SerializeField]
		private uint _lightLayerMask = uint.MaxValue;
		[SerializeField]
		private bool _debugDrawToCamera = true;

		private const string SHADER_NAME = "Hidden/TPSBR/HDRP/SightMask";
		private static readonly int TARGET_LIGHT_LAYER_MASK = Shader.PropertyToID("_TargetLightLayerMask");

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;

		protected override bool executeInSceneView => true;

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			Shader shader = Shader.Find(SHADER_NAME);
			if (shader == null)
			{
				Debug.LogError($"[SightMaskPass] Shader not found: {SHADER_NAME}. It may be stripped from the player build.");
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

			_propertyBlock.Clear();
			_propertyBlock.SetInt(TARGET_LIGHT_LAYER_MASK, unchecked((int)_lightLayerMask));

			if (_debugDrawToCamera == false)
				return;

			CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
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
