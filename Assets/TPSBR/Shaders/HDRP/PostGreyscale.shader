Shader "Hidden/TPSBR/HDRP/PostGreyscale"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeWeights("Mode Weights", Vector) = (0,0,0,0)
		_TintColor("Unknown Color", Color) = (0.6037736, 0.6037736, 0.6037736, 1)
		_SaturationStrength("Saturation", Range(0,1)) = 0
	}

	HLSLINCLUDE

	#pragma vertex Vert
	#pragma fragment FullScreenPass
	#pragma target 4.5

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

	TEXTURE2D_X(_SceneColorTex);
	TEXTURE2D_X(_VisionMaskTex);
	TEXTURE2D_X(_HiddenColorTex);
	TEXTURE2D_X(_HiddenMaskTex);
	float _Strength;
	float4 _ModeWeights;
	float4 _TintColor;
	float _SaturationStrength;

	float ComputeBlendMask(float mask, float hiddenSignal, float2 controls)
	{
		float visionControl = clamp(controls.x, -1.0, 1.0);
		float hiddenControl = saturate(controls.y);

		float modeMask = 1.0;
		if (visionControl > 0.0)
		{
			modeMask = lerp(1.0, mask, visionControl);
		}
		else if (visionControl < 0.0)
		{
			modeMask = lerp(1.0, 1.0 - mask, -visionControl);
		}

		return saturate(lerp(modeMask, hiddenControl, saturate(hiddenSignal)));
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = (uint2)varyings.positionCS.xy;

		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float finalMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenCoverage = saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord).a);
		float hiddenInVision = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float hiddenSignal = hiddenCoverage * hiddenInVision;
		float depth = LoadCameraDepth(varyings.positionCS.xy);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		float blendMask = ComputeBlendMask(finalMask, hiddenSignal, _ModeWeights.xy);

		float3 fogged = sceneColor.rgb * _TintColor.rgb;
		float luminance = dot(fogged, float3(0.2126729, 0.7151522, 0.0721750));
		float3 saturated = luminance.xxx + _SaturationStrength.xxx * (sceneColor.rgb - luminance.xxx);

		float t = saturate(blendMask * _Strength);
		float3 composed = lerp(sceneColor.rgb, saturated, t);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Greyscale"

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

