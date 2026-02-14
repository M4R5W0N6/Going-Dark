Shader "Hidden/TPSBR/HDRP/PostOutline"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeWeights("Mode Weights", Vector) = (0,0,0,0)
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
	TEXTURE2D_X(_VisionMaskTex);
	TEXTURE2D_X(_HiddenColorTex);
	TEXTURE2D_X(_HiddenMaskTex);
	float _Strength;
	float4 _ModeWeights;
	float4 _OutlineColor;
	float _OutlineThickness;

	float ComputeVisionMask(float mask, float visionControl)
	{
		visionControl = clamp(visionControl, -1.0, 1.0);

		float modeMask = 1.0;
		if (visionControl > 0.0)
		{
			modeMask = lerp(1.0, mask, visionControl);
		}
		else if (visionControl < 0.0)
		{
			modeMask = lerp(1.0, 1.0 - mask, -visionControl);
		}

		return saturate(modeMask);
	}

	float ComputeBlendMask(float mask, float hiddenSignal, float2 controls)
	{
		float visionMask = ComputeVisionMask(mask, controls.x);
		float hiddenControl = saturate(controls.y);
		return saturate(lerp(visionMask, hiddenControl, saturate(hiddenSignal)));
	}

	float SampleMask(int2 p)
	{
		return saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, p).r);
	}

	float SampleHidden(int2 p)
	{
		return saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, p).a);
	}

	float SampleHiddenInVision(int2 p)
	{
		return saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, p).r);
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		float2 positionSS = varyings.positionCS.xy;
		int2 pixelCoord = (int2)positionSS;
		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float centerRawMask = SampleMask(pixelCoord);
		float centerHidden = SampleHidden(pixelCoord);
		float centerHiddenInVision = SampleHiddenInVision(pixelCoord);
		float centerVisionMask = ComputeVisionMask(centerRawMask, _ModeWeights.x);
		float hiddenControl = saturate(_ModeWeights.y);

		float depth = LoadCameraDepth(positionSS);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		int t = max(1, (int)round(_OutlineThickness));
		float mL = ComputeVisionMask(SampleMask(pixelCoord + int2(-t, 0)), _ModeWeights.x);
		float mR = ComputeVisionMask(SampleMask(pixelCoord + int2( t, 0)), _ModeWeights.x);
		float mU = ComputeVisionMask(SampleMask(pixelCoord + int2(0, -t)), _ModeWeights.x);
		float mD = ComputeVisionMask(SampleMask(pixelCoord + int2(0,  t)), _ModeWeights.x);
		float hL = SampleHidden(pixelCoord + int2(-t, 0));
		float hR = SampleHidden(pixelCoord + int2( t, 0));
		float hU = SampleHidden(pixelCoord + int2(0, -t));
		float hD = SampleHidden(pixelCoord + int2(0,  t));

		float edgeVision = max(max(abs(centerVisionMask - mL), abs(centerVisionMask - mR)), max(abs(centerVisionMask - mU), abs(centerVisionMask - mD)));
		float edgeHidden = max(max(abs(centerHidden - hL), abs(centerHidden - hR)), max(abs(centerHidden - hU), abs(centerHidden - hD)));
		float hiddenGate = centerHidden * centerHiddenInVision;
		float edge = lerp(edgeVision * centerVisionMask, edgeHidden * hiddenControl * centerHiddenInVision, hiddenGate);
		float tBlend = saturate(edge * _Strength * _OutlineColor.a);
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

