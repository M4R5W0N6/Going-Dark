Shader "Hidden/TPSBR/HDRP/PostTexture"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeMask("Mode Mask", Float) = 3
		_OverlayTex("Overlay Texture", 2D) = "white" {}
		_TintColor("Unknown Color", Color) = (0.6037736, 0.6037736, 0.6037736, 1)
		_TextureTiling("Texture Tiling", Vector) = (1,1,0,0)
		_TextureScrollSpeed("Texture Scroll", Vector) = (0,0,0,0)
		_TriplanarSharpness("Triplanar Sharpness", Range(1,32)) = 8
	}

	HLSLINCLUDE

	#pragma vertex Vert
	#pragma fragment FullScreenPass
	#pragma target 4.5

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_MaskTex);
	TEXTURE2D(_OverlayTex);
	SAMPLER(sampler_OverlayTex);
	float _Strength;
	float _ModeMask;
	float4 _TintColor;
	float2 _TextureTiling;
	float2 _TextureScrollSpeed;
	float _TriplanarSharpness;

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		float2 positionSS = varyings.positionCS.xy;
		uint2 pixelCoord = (uint2)positionSS;
		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float finalMask = saturate(LOAD_TEXTURE2D_X(_MaskTex, pixelCoord).r);

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
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;
		PositionInputs posInput = GetPositionInput(positionSS, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

		float3 aws = GetAbsolutePositionWS(posInput.positionWS);

		NormalData normalData;
		DecodeFromNormalBuffer(positionSS, normalData);
		float3 n = abs(normalData.normalWS);
		float3 weights = pow(n, float3(_TriplanarSharpness, _TriplanarSharpness, _TriplanarSharpness));
		float wSum = max(weights.x + weights.y + weights.z, 1e-5);
		weights /= wSum;

		float2 scroll = _Time.yy * _TextureScrollSpeed;
		float2 uvX = (aws.yz * _TextureTiling) + scroll;
		float2 uvY = (aws.xz * _TextureTiling) + scroll;
		float2 uvZ = (aws.xy * _TextureTiling) + scroll;

		float4 sx = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, uvX);
		float4 sy = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, uvY);
		float4 sz = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, uvZ);
		float4 texColor = sx * weights.x + sy * weights.y + sz * weights.z;

		float3 foggedTex = lerp(sceneColor.rgb, texColor.rgb, _TintColor.a);
		float3 effectColor = foggedTex * _TintColor.rgb;

		float t = saturate(blendMask * _Strength);
		float3 composed = lerp(sceneColor.rgb, effectColor, t);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Texture"

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

