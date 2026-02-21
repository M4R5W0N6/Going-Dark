Shader "Hidden/TPSBR/HDRP/PostColor"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _LayerMaskTex("Layer Mask", 2D) = "black" {}
		[HideInInspector] _LayerMaskEnabled("Layer Mask Enabled", Range(0,1)) = 0
		[HideInInspector] _Strength("Strength", Range(0,1)) = 1
		[HideInInspector] _ModeWeights("Mode Weights", Vector) = (0,0,0,0)
		_TintColor("Tint Color", Color) = (0.7, 0.9, 1.0, 1.0)
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
	TEXTURE2D_X(_LayerMaskTex);
	float _LayerMaskEnabled;
	float _Strength;
	float4 _ModeWeights;
	float4 _TintColor;

	float ComputeSideMask(float mask, float2 visionControls)
	{
		float insideControl = saturate(visionControls.x);
		float outsideControl = saturate(visionControls.y);
		return saturate((mask * insideControl) + ((1.0 - mask) * outsideControl));
	}

	float ComputeBlendMask(float visionMask, float hiddenCoverage, float hiddenInVision, float layerCoverage, float3 controls)
	{
		float sideMask = ComputeSideMask(visionMask, controls.xy);
		float hiddenControl = saturate(controls.z);
		float hiddenVisibility = saturate(hiddenCoverage * hiddenInVision * visionMask);
		float hiddenSignal = hiddenVisibility * hiddenControl;
		float layerSignal = saturate(layerCoverage * sideMask);
		float sceneSignal = saturate(max(sideMask, layerSignal) * (1.0 - hiddenVisibility));
		return saturate(sceneSignal + hiddenSignal);
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = (uint2)varyings.positionCS.xy;

		float4 sceneColor = LOAD_TEXTURE2D_X(_SceneColorTex, pixelCoord);
		float finalMask = saturate(LOAD_TEXTURE2D_X(_VisionMaskTex, pixelCoord).r);
		float hiddenCoverage = saturate(LOAD_TEXTURE2D_X(_HiddenColorTex, pixelCoord).a);
		float hiddenInVision = saturate(LOAD_TEXTURE2D_X(_HiddenMaskTex, pixelCoord).r);
		float layerDepth = LOAD_TEXTURE2D_X(_LayerMaskTex, pixelCoord).r;
		float layerCoverage = step(1e-5, layerDepth) * saturate(_LayerMaskEnabled);
		float depth = LoadCameraDepth(varyings.positionCS.xy);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		float blendMask = ComputeBlendMask(finalMask, hiddenCoverage, hiddenInVision, layerCoverage, _ModeWeights.xyz);

		float t = saturate(blendMask * _Strength * _TintColor.a);
		float3 fogColor = lerp(_TintColor.rgb, sceneColor.rgb * _TintColor.rgb, _TintColor.a);
		float3 composed = lerp(sceneColor.rgb, fogColor, t);
		return float4(composed, sceneColor.a);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Post Color"

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

