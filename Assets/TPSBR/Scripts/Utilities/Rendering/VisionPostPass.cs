namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	[System.Serializable]
	public class VisionPostPass : CustomPass
	{
		private enum RenderMode
		{
			Both = 0,
			Vision = 1,
			Hidden = 2,
			None = 3
		}

		[SerializeField]
		private Shader _shader;
		[SerializeField]
		private RenderMode _renderMode = RenderMode.Hidden;
		[SerializeField, Range(-1.0f, 1.0f)]
		private float _strength = 1.0f;
		[SerializeField]
		private Color _tintColor = new Color(0.7f, 0.9f, 1.0f, 1.0f);
		[SerializeField, Range(0.0f, 1.0f)]
		private float _saturationStrength = 0.0f;
		[SerializeField]
		private Texture2D _overlayTexture;
		[SerializeField]
		private Vector2 _textureTiling = Vector2.one;
		[SerializeField]
		private Vector2 _textureScrollSpeed = Vector2.zero;
		[SerializeField, Range(1.0f, 32.0f)]
		private float _triplanarSharpness = 8.0f;
		[SerializeField]
		private Color _outlineColor = Color.black;
		[SerializeField, Range(0.5f, 8.0f)]
		private float _outlineThickness = 1.0f;
		[SerializeField, Min(1.0f)]
		private float _depthDistance = 100.0f;
		[SerializeField, Range(0.0f, 1.0f)]
		private float _depthAttenuationPower = 1.0f;

		private const string DEFAULT_SHADER_NAME = "Hidden/TPSBR/HDRP/PostGreyscale";
		private static readonly int SCENE_COLOR_TEX = Shader.PropertyToID("_SceneColorTex");
		private static readonly int VISION_MASK_TEX = Shader.PropertyToID("_VisionMaskTex");
		private static readonly int MASK_TEX = Shader.PropertyToID("_MaskTex");
		private static readonly int HIDDEN_COLOR_TEX = Shader.PropertyToID("_HiddenColorTex");
		private static readonly int HIDDEN_MASK_TEX = Shader.PropertyToID("_HiddenMaskTex");
		private static readonly int HIDDEN_DEPTH_TEX = Shader.PropertyToID("_HiddenDepthTex");
		private static readonly int STRENGTH = Shader.PropertyToID("_Strength");
		private static readonly int MODE_MASK = Shader.PropertyToID("_ModeMask");
		private static readonly int TINT_COLOR = Shader.PropertyToID("_TintColor");
		private static readonly int SATURATION_STRENGTH = Shader.PropertyToID("_SaturationStrength");
		private static readonly int OVERLAY_TEX = Shader.PropertyToID("_OverlayTex");
		private static readonly int TEXTURE_TILING = Shader.PropertyToID("_TextureTiling");
		private static readonly int TEXTURE_SCROLL_SPEED = Shader.PropertyToID("_TextureScrollSpeed");
		private static readonly int TRIPLANAR_SHARPNESS = Shader.PropertyToID("_TriplanarSharpness");
		private static readonly int OUTLINE_COLOR = Shader.PropertyToID("_OutlineColor");
		private static readonly int OUTLINE_THICKNESS = Shader.PropertyToID("_OutlineThickness");
		private static readonly int DEPTH_DISTANCE = Shader.PropertyToID("_DepthDistance");
		private static readonly int DEPTH_ATTENUATION_POWER = Shader.PropertyToID("_DepthAttenuationPower");

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;
		private Shader _activeShader;

		protected override bool executeInSceneView => true;

		protected override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd)
		{
			EnsureMaterial();
			_propertyBlock = new MaterialPropertyBlock();
			clearFlags = ClearFlag.None;
		}

		protected override void Execute(CustomPassContext ctx)
		{
			EnsureMaterial();

			if (_material == null || _propertyBlock == null)
				return;

			VisionPassBuffers.Ensure();

			CustomPassUtils.Copy(ctx, ctx.cameraColorBuffer, VisionPassBuffers.SceneColorCopy);

			_propertyBlock.Clear();
			_propertyBlock.SetTexture(SCENE_COLOR_TEX, VisionPassBuffers.SceneColorCopy);
			_propertyBlock.SetTexture(VISION_MASK_TEX, VisionPassBuffers.VisionMask);
			_propertyBlock.SetTexture(MASK_TEX, VisionPassBuffers.FinalMask);
			_propertyBlock.SetTexture(HIDDEN_COLOR_TEX, VisionPassBuffers.HiddenColor);
			_propertyBlock.SetTexture(HIDDEN_MASK_TEX, VisionPassBuffers.HiddenMask);
			_propertyBlock.SetTexture(HIDDEN_DEPTH_TEX, VisionPassBuffers.HiddenDepth);
			_propertyBlock.SetFloat(STRENGTH, _strength);
			_propertyBlock.SetFloat(MODE_MASK, GetModeMaskValue());
			_propertyBlock.SetColor(TINT_COLOR, _tintColor);
			_propertyBlock.SetFloat(SATURATION_STRENGTH, _saturationStrength);
			_propertyBlock.SetTexture(OVERLAY_TEX, _overlayTexture != null ? _overlayTexture : Texture2D.whiteTexture);
			_propertyBlock.SetVector(TEXTURE_TILING, _textureTiling);
			_propertyBlock.SetVector(TEXTURE_SCROLL_SPEED, _textureScrollSpeed);
			_propertyBlock.SetFloat(TRIPLANAR_SHARPNESS, _triplanarSharpness);
			_propertyBlock.SetColor(OUTLINE_COLOR, _outlineColor);
			_propertyBlock.SetFloat(OUTLINE_THICKNESS, _outlineThickness);
			_propertyBlock.SetFloat(DEPTH_DISTANCE, _depthDistance);
			_propertyBlock.SetFloat(DEPTH_ATTENUATION_POWER, _depthAttenuationPower);

			CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
			CoreUtils.DrawFullScreen(ctx.cmd, _material, _propertyBlock, shaderPassId: 0);
		}

		private void EnsureMaterial()
		{
			Shader selectedShader = _shader != null ? _shader : Shader.Find(DEFAULT_SHADER_NAME);
			if (selectedShader == null)
			{
				Debug.LogError($"[VisionPostPass] Shader not found: {DEFAULT_SHADER_NAME}. Assign one manually in the pass.");
				return;
			}

			if (_material != null && _activeShader == selectedShader)
				return;

			CoreUtils.Destroy(_material);
			_material = CoreUtils.CreateEngineMaterial(selectedShader);
			_activeShader = selectedShader;
		}

		private float GetModeMaskValue()
		{
			switch (_renderMode)
			{
				case RenderMode.Both: return 1.0f;
				case RenderMode.Vision: return 2.0f;
				case RenderMode.Hidden: return 3.0f;
				default: return 0.0f;
			}
		}

		protected override void Cleanup()
		{
			CoreUtils.Destroy(_material);
			_material = null;
			_propertyBlock = null;
			_activeShader = null;
		}
	}

	[System.Serializable]
	public sealed class FullscreenPostPass : VisionPostPass
	{
	}
}
