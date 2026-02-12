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
		}

		protected override void AggregateCullingParameters(ref ScriptableCullingParameters cullingParameters, HDCamera hdCamera)
		{
			cullingParameters.cullingMask |= (uint)_hiddenLayerMask.value;
		}

		protected override void Execute(CustomPassContext ctx)
		{
			if (_hiddenLayerMask.value == 0)
				return;

			VisionPassBuffers.Ensure();

			// Step 1: merge hidden depth into the camera depth so hidden objects are depth-tested against world geometry.
			CustomPassUtils.RenderDepthFromCamera(
				ctx,
				ctx.hdCamera.camera,
				VisionPassBuffers.HiddenColor,
				ctx.cameraDepthBuffer,
				ClearFlag.None,
				_hiddenLayerMask,
				RenderQueueType.All);

			// Step 2: render hidden color offscreen while using camera depth for correct occlusion.
			CoreUtils.SetRenderTarget(ctx.cmd, VisionPassBuffers.HiddenColor, ctx.cameraDepthBuffer, ClearFlag.Color);
			var colorState = new RenderStateBlock(RenderStateMask.Depth)
			{
				depthState = new DepthState(false, CompareFunction.LessEqual)
			};

			CustomPassUtils.DrawRenderers(ctx, _hiddenLayerMask, RenderQueueType.All, overrideRenderState: colorState);
		}
	}
}
