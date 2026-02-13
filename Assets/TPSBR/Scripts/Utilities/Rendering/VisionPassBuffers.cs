namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Experimental.Rendering;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	internal static class VisionPassBuffers
	{
		public static RTHandle VisionMask { get; private set; }
		public static RTHandle HiddenMask { get; private set; }
		public static RTHandle FinalMask { get; private set; }
		public static RTHandle HiddenDepth { get; private set; }
		public static RTHandle HiddenColor { get; private set; }
		public static RTHandle SceneColorCopy { get; private set; }

		public static void Ensure()
		{
			if (VisionMask == null)
			{
				VisionMask = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.R8_UNorm,
					useDynamicScale: true,
					name: "VisionMaskBuffer");
			}

			if (HiddenColor == null)
			{
				HiddenColor = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.R16G16B16A16_SFloat,
					useDynamicScale: true,
					name: "VisionHiddenColorBuffer");
			}

			if (HiddenMask == null)
			{
				HiddenMask = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.R8_UNorm,
					useDynamicScale: true,
					name: "VisionHiddenMaskBuffer");
			}

			if (FinalMask == null)
			{
				FinalMask = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.R8_UNorm,
					useDynamicScale: true,
					name: "VisionFinalMaskBuffer");
			}

			if (HiddenDepth == null)
			{
				HiddenDepth = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.R32_SFloat,
					useDynamicScale: true,
					name: "VisionHiddenDepthBuffer");
			}

			if (SceneColorCopy == null)
			{
				SceneColorCopy = RTHandles.Alloc(
					Vector2.one,
					TextureXR.slices,
					dimension: TextureXR.dimension,
					colorFormat: GraphicsFormat.B10G11R11_UFloatPack32,
					useDynamicScale: true,
					name: "VisionSceneColorCopy");
			}
		}

		public static void Release()
		{
			RTHandles.Release(VisionMask);
			RTHandles.Release(HiddenMask);
			RTHandles.Release(FinalMask);
			RTHandles.Release(HiddenDepth);
			RTHandles.Release(HiddenColor);
			RTHandles.Release(SceneColorCopy);

			VisionMask = null;
			HiddenMask = null;
			FinalMask = null;
			HiddenDepth = null;
			HiddenColor = null;
			SceneColorCopy = null;
		}
	}
}
