Shader "Hidden/TPSBR/HDRP/PostOutline"
{
	Properties
	{
		[HideInInspector] _SceneColorTex("Scene Color", 2D) = "white" {}
		[HideInInspector] _MaskTex("Mask", 2D) = "white" {}
		[HideInInspector] _LayerMaskTex("Layer Mask", 2D) = "black" {}
		[HideInInspector] _LayerMaskEnabled("Layer Mask Enabled", Range(0,1)) = 0
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
	TEXTURE2D_X(_LayerMaskTex);
	float _LayerMaskEnabled;
	float _Strength;
	float4 _ModeWeights;
	float4 _OutlineColor;
	float _OutlineThickness;

	float ComputeSideMask(float mask, float2 visionControls)
	{
		float insideControl = saturate(visionControls.x);
		float outsideControl = saturate(visionControls.y);
		return saturate((mask * insideControl) + ((1.0 - mask) * outsideControl));
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

	float SampleLayer(int2 p)
	{
		float layerDepth = LOAD_TEXTURE2D_X(_LayerMaskTex, p).r;
		return step(1e-5, layerDepth) * saturate(_LayerMaskEnabled);
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
		float centerLayerMask = SampleLayer(pixelCoord);
		float centerVisionMask = ComputeSideMask(centerRawMask, _ModeWeights.xy);
		float hiddenControl = saturate(_ModeWeights.z);
		float hiddenVisibility = saturate(centerHidden * centerHiddenInVision * centerRawMask);
		float sceneVisibility = 1.0 - hiddenVisibility;

		float depth = LoadCameraDepth(positionSS);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
			return sceneColor;

		int t = max(1, (int)round(_OutlineThickness));
		float mL = SampleMask(pixelCoord + int2(-t, 0));
		float mR = SampleMask(pixelCoord + int2( t, 0));
		float mU = SampleMask(pixelCoord + int2(0, -t));
		float mD = SampleMask(pixelCoord + int2(0,  t));
		float hL = SampleHidden(pixelCoord + int2(-t, 0));
		float hR = SampleHidden(pixelCoord + int2( t, 0));
		float hU = SampleHidden(pixelCoord + int2(0, -t));
		float hD = SampleHidden(pixelCoord + int2(0,  t));
		float lL = SampleLayer(pixelCoord + int2(-t, 0));
		float lR = SampleLayer(pixelCoord + int2( t, 0));
		float lU = SampleLayer(pixelCoord + int2(0, -t));
		float lD = SampleLayer(pixelCoord + int2(0,  t));

		float edgeVisionRaw = max(max(abs(centerRawMask - mL), abs(centerRawMask - mR)), max(abs(centerRawMask - mU), abs(centerRawMask - mD)));
		float edgeVision = edgeVisionRaw * centerVisionMask * sceneVisibility;
		float edgeHidden = max(max(abs(centerHidden - hL), abs(centerHidden - hR)), max(abs(centerHidden - hU), abs(centerHidden - hD)));
		float hiddenGate = hiddenVisibility * hiddenControl;
		float edgeLayerRaw = max(max(abs(centerLayerMask - lL), abs(centerLayerMask - lR)), max(abs(centerLayerMask - lU), abs(centerLayerMask - lD)));
		float layerGate = centerLayerMask * centerVisionMask * sceneVisibility;
		float edgeLayer = edgeLayerRaw * layerGate;
		float edge = max(edgeVision, max(edgeHidden * hiddenGate, edgeLayer));
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

