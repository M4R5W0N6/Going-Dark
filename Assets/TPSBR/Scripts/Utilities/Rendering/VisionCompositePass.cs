namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public sealed class VisionCompositePass : CustomPass
	{
		private enum RenderMode
		{
			Both = 0,
			Vision = 1,
			Hidden = 2
		}

		[SerializeField]
		private RenderMode _renderMode = RenderMode.Both;

		private const string SHADER_NAME = "Hidden/TPSBR/HDRP/VisionComposite";
		private static readonly int SCENE_COLOR_TEX = Shader.PropertyToID("_SceneColorTex");
		private static readonly int VISION_MASK_TEX = Shader.PropertyToID("_VisionMaskTex");
		private static readonly int HIDDEN_MASK_TEX = Shader.PropertyToID("_HiddenMaskTex");
		private static readonly int HIDDEN_COLOR_TEX = Shader.PropertyToID("_HiddenColorTex");

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;

		protected override bool executeInSceneView => true;

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			Shader shader = Shader.Find(SHADER_NAME);
			if (shader == null)
			{
				Debug.LogError($"[VisionCompositePass] Shader not found: {SHADER_NAME}. It may be stripped from the player build.");
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
			VisionPassBuffers.MarkCompositeExecuted(ctx.hdCamera.camera != null ? ctx.hdCamera.camera.GetInstanceID() : -1);

			_propertyBlock.Clear();
			_propertyBlock.SetTexture(SCENE_COLOR_TEX, VisionPassBuffers.SceneColorCopy);
			_propertyBlock.SetTexture(VISION_MASK_TEX, VisionPassBuffers.VisionMask);
			_propertyBlock.SetTexture(HIDDEN_MASK_TEX, VisionPassBuffers.HiddenMask);
			_propertyBlock.SetTexture(HIDDEN_COLOR_TEX, VisionPassBuffers.HiddenColor);

			// Build the exact final mask used by composition once, then share with later post passes.
			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.FinalMask, ClearFlag.Color);
			CoreUtils.DrawFullScreen(ctx.cmd, _material, _propertyBlock, shaderPassId: 1);

			if (_renderMode == RenderMode.Vision)
			{
				CustomPassUtils.Copy(ctx, VisionPassBuffers.FinalMask, ctx.cameraColorBuffer);
				return;
			}

			if (_renderMode == RenderMode.Hidden)
			{
				CustomPassUtils.Copy(ctx, VisionPassBuffers.HiddenColor, ctx.cameraColorBuffer);
				return;
			}

			CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, VisionPassBuffers.SceneColorCopy);
			_propertyBlock.SetTexture(SCENE_COLOR_TEX, VisionPassBuffers.SceneColorCopy);

			CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
			CoreUtils.DrawFullScreen(ctx.cmd, _material, _propertyBlock, shaderPassId: 0);
		}

		protected override void Cleanup()
		{
			CoreUtils.Destroy(_material);
			_material = null;
			_propertyBlock = null;
			VisionPassBuffers.Release();
		}
	}
}
