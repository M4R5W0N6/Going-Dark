Shader "Hidden/TPSBR/HDRP/PostOutline"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeMask("Mode Mask", Float) = 3
		_OutlineColor("Outline Color", Color) = (0,0,0,1)
		_OutlineThickness("Outline Thickness", Range(0.5,8)) = 1
	}

	HLSLINCLUDE

	#pragma vertex Vert
	#pragma fragment FullScreenPass
	#pragma target 4.5

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_MaskTex);
	float _Strength;
	float _ModeMask;
	float4 _OutlineColor;
	float _OutlineThickness;

	float SampleMask(int2 p)
	{
		return saturate(LOAD_TEXTURE2D_X(_MaskTex, p).r);
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		float2 positionSS = varyings.positionCS.xy;
		int2 pixelCoord = (int2)positionSS;
		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float centerMask = SampleMask(pixelCoord);

		float blendMask = 0.0;
		if (_ModeMask > 2.5)
		{
			blendMask = 1.0 - centerMask;
		}
		else if (_ModeMask > 1.5)
		{
			blendMask = centerMask;
		}
		else if (_ModeMask > 0.5)
		{
			blendMask = 1.0;
		}

		float depth = LoadCameraDepth(positionSS);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		int t = max(1, (int)round(_OutlineThickness));
		float mL = SampleMask(pixelCoord + int2(-t, 0));
		float mR = SampleMask(pixelCoord + int2( t, 0));
		float mU = SampleMask(pixelCoord + int2(0, -t));
		float mD = SampleMask(pixelCoord + int2(0,  t));

		float edge = max(max(abs(centerMask - mL), abs(centerMask - mR)), max(abs(centerMask - mU), abs(centerMask - mD)));
		float tBlend = saturate(edge * blendMask * _Strength * _OutlineColor.a);
		float3 composed = lerp(sceneColor.rgb, _OutlineColor.rgb, tBlend);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Outline"

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

