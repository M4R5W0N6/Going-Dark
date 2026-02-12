namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.Experimental.Rendering;
	using UnityEngine.Rendering;
	using UnityEngine.Rendering.HighDefinition;

	internal static class VisionPassBuffers
	{
		public static RTHandle VisionMask { get; private set; }
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
			RTHandles.Release(HiddenColor);
			RTHandles.Release(SceneColorCopy);

			VisionMask = null;
			HiddenColor = null;
			SceneColorCopy = null;
		}
	}
}
