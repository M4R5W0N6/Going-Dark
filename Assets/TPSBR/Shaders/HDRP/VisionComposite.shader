Shader "Hidden/TPSBR/HDRP/VisionComposite"
{
	HLSLINCLUDE

	#pragma vertex Vert
	#pragma fragment FullScreenPass
	#pragma target 4.5
	#pragma only_renderers d3d11 d3d12 playstation xboxone xboxseries vulkan metal switch

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_VisionMaskTex);
	TEXTURE2D_X(_HiddenColorTex);

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = (uint2)varyings.positionCS.xy;

		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float4 hiddenColor = LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord);
		float visionMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenAlpha = saturate(hiddenColor.a);
		float alpha = saturate(visionMask * hiddenAlpha);

		float3 composed = lerp(sceneColor.rgb, hiddenColor.rgb, alpha);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Vision Composite"

			ZWrite Off
			ZTest Always
			Blend Off
			Cull Off

			HLSLPROGRAM
			ENDHLSL
		}
	}

	Fallback Off
}
