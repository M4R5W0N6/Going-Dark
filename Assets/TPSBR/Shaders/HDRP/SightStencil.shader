Shader "Hidden/TPSBR/HDRP/SightStencil"
{
	HLSLINCLUDE

	#pragma target 4.5
	#pragma only_renderers d3d11 d3d12 playstation xboxone xboxseries vulkan metal switch
	#pragma vertex Vert
	#pragma fragment FragClear

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	CBUFFER_START(UnityPerMaterial)
	float _VisibilityThreshold;
	CBUFFER_END

	float4 FragClear(Varyings input) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
		return 0.0;
	}

	float4 FragWrite(Varyings input) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
		float2 uv = input.positionCS.xy * _ScreenSize.zw;
		float visibility = CustomPassSampleCustomColor(uv).r;
		clip(visibility - _VisibilityThreshold);
		return 0.0;
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }

		Pass
		{
			Name "ClearStencil"
			Cull Off
			ZWrite Off
			ZTest Always
			ColorMask 0

			HLSLPROGRAM
			#pragma fragment FragClear
			ENDHLSL
		}

		Pass
		{
			Name "WriteStencil"
			Cull Off
			ZWrite Off
			ZTest Always
			ColorMask 0

			HLSLPROGRAM
			#pragma fragment FragWrite
			ENDHLSL
		}
	}

	Fallback Off
}
