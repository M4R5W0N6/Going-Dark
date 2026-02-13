Shader "Hidden/TPSBR/HDRP/PostDepth"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _HiddenColorTex("Hidden Color", 2D) = "black" {}
		[HideInInspector] _HiddenMaskTex("Hidden Mask", 2D) = "black" {}
		[HideInInspector] _HiddenDepthTex("Hidden Depth", 2D) = "black" {}
		[HideInInspector] _Strength("Strength", Range(-1,1)) = 1
		[HideInInspector] _ModeMask("Mode Mask", Float) = 3
		_TintColor("Tint Color", Color) = (0.7, 0.9, 1.0, 1.0)
		_DepthDistance("Depth Distance", Float) = 100
		_DepthAttenuationPower("Depth Attenuation Power", Range(0,1)) = 1
	}

	HLSLINCLUDE

	#pragma vertex Vert
	#pragma fragment FullScreenPass
	#pragma target 4.5

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_MaskTex);
	TEXTURE2D_X(_HiddenColorTex);
	TEXTURE2D_X(_HiddenMaskTex);
	TEXTURE2D_X(_HiddenDepthTex);
	float _Strength;
	float _ModeMask;
	float4 _TintColor;
	float _DepthDistance;
	float _DepthAttenuationPower;

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		float2 positionSS = varyings.positionCS.xy;
		uint2 pixelCoord = (uint2)positionSS;
		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float sceneMask = saturate(LOAD_TEXTURE2D_X(_MaskTex, pixelCoord).r);
		float hiddenAlpha = saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord).a);
		float hiddenMask = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float hiddenVisible = saturate(hiddenAlpha * hiddenMask);
		float finalMask = lerp(sceneMask, hiddenMask, hiddenVisible);

		float blendMask = 0.0;
		if (_ModeMask > 2.5)
		{
			blendMask = 1.0 - finalMask;
		}
		else if (_ModeMask > 1.5)
		{
			blendMask = finalMask;
		}
		else if (_ModeMask > 0.5)
		{
			blendMask = 1.0;
		}

		float depth = LoadCameraDepth(positionSS);
		float linearSceneDepth = 0.0;
		bool hasSceneDepth = depth != UNITY_RAW_FAR_CLIP_VALUE;
		if (hasSceneDepth)
		{
			PositionInputs scenePosInput = GetPositionInput(positionSS, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
			linearSceneDepth = scenePosInput.linearDepth;
		}

		float linearHiddenDepth = LOAD_TEXTURE2D_X(_HiddenDepthTex, pixelCoord).r;
		bool hasHiddenDepth = hiddenVisible > 0.0 && linearHiddenDepth > 0.0;

		if (!hasSceneDepth && !hasHiddenDepth)
			return sceneColor;

		float finalLinearDepth = hasSceneDepth ? linearSceneDepth : linearHiddenDepth;
		if (hasHiddenDepth && (!hasSceneDepth || linearHiddenDepth < linearSceneDepth))
		{
			finalLinearDepth = linearHiddenDepth;
		}

		float depth01 = saturate(finalLinearDepth / max(_DepthDistance, 0.001));
		depth01 = pow(depth01, max(_DepthAttenuationPower, 0.0));
		if (_Strength < 0.0)
		{
			depth01 = 1.0 - depth01;
		}
		float3 depthRgb = depth01.xxx * _TintColor.rgb;

		float t = saturate(blendMask * abs(_Strength) * _TintColor.a);
		float3 composed = lerp(sceneColor.rgb, depthRgb, t);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Depth"

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

