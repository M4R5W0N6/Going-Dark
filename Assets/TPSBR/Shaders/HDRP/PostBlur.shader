Shader "Hidden/TPSBR/HDRP/PostBlur"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeWeights("Mode Weights", Vector) = (0,0,0,0)
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

	float3 SampleScene(int2 pixelCoord)
	{
		return LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord).rgb;
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		int2 pixelCoord = (int2)varyings.positionCS.xy;
		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float finalMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenCoverage = saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord).a);
		float hiddenInVision = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float hiddenSignal = hiddenCoverage * hiddenInVision;
		float depth = LoadCameraDepth(varyings.positionCS.xy);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		float blendMask = ComputeBlendMask(finalMask, hiddenSignal, _ModeWeights.xy);

		float strength = saturate(_Strength);
		float blurRadiusMin = 0.5;
		float blurRadiusMax = lerp(1.0, 6.0, strength);
		const int sampleCount = 12;
		const float samplePeriod = 6.28318530718 / sampleCount;

		float randomStart = frac(sin(dot(varyings.positionCS.xy, float2(12.9898, 78.233))) * 43758.5453) * 6.28318530718;

		float3 accum = sceneColor.rgb;
		float totalWeight = 1.0;

		[unroll]
		for (int s = 0; s < sampleCount; s++)
		{
			float2 dir;
			sincos((samplePeriod * s) + randomStart, dir.x, dir.y);

			float radialNoise = frac(sin((s + 1.0) * randomStart * 91.17) * 43758.5453);
			float distanceFromCenter = lerp(blurRadiusMin, blurRadiusMax, radialNoise);
			int2 tapOffset = (int2)round(dir * distanceFromCenter);

			float3 tap = SampleScene(pixelCoord + tapOffset);
			accum += tap;
			totalWeight += 1.0;
		}

		float3 blurred = accum / totalWeight;

		float t = saturate(blendMask * strength);
		float3 composed = lerp(sceneColor.rgb, blurred, t);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Blur"

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

