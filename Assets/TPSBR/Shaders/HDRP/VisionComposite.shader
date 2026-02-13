Shader "Hidden/TPSBR/HDRP/VisionComposite"
{
	HLSLINCLUDE

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_VisionMaskTex);
	TEXTURE2D_X(_HiddenMaskTex);
	TEXTURE2D_X(_HiddenColorTex);

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = (uint2)varyings.positionCS.xy;

		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float4 hiddenColor = LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord);
		float sceneVisionMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenVisionMask = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float hiddenAlpha = saturate(hiddenColor.a);
		float hiddenVisibility = saturate(hiddenVisionMask * hiddenAlpha);
		float alpha = hiddenVisibility;

		float3 composed = lerp(sceneColor.rgb, hiddenColor.rgb, alpha);
		return float4(composed, sceneColor.a);
	}

	float4 FinalMaskPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = (uint2)varyings.positionCS.xy;
		float sceneVisionMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenVisionMask = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float hiddenAlpha = saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord).a);
		float hiddenVisibility = saturate(hiddenVisionMask * hiddenAlpha);
		float finalMask = lerp(sceneVisionMask, hiddenVisionMask, hiddenVisibility);
		return float4(finalMask, finalMask, finalMask, 1.0);
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
			#pragma vertex Vert
			#pragma fragment FullScreenPass
			#pragma target 4.5
			ENDHLSL
		}

		Pass
		{
			Name "Vision Final Mask"

			ZWrite Off
			ZTest Always
			Blend Off
			Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment FinalMaskPass
			#pragma target 4.5
			ENDHLSL
		}
	}

	Fallback Off
}
