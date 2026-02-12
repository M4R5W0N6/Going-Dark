Shader "Hidden/TPSBR/HDRP/SightMask"
{
	HLSLINCLUDE

	#pragma vertex Vert
	#pragma target 4.5
	#pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch
	#pragma multi_compile USE_FPTL_LIGHTLIST USE_CLUSTERED_LIGHTLIST
	#pragma multi_compile_fragment PUNCTUAL_SHADOW_LOW PUNCTUAL_SHADOW_MEDIUM PUNCTUAL_SHADOW_HIGH
	#pragma multi_compile_fragment DIRECTIONAL_SHADOW_LOW DIRECTIONAL_SHADOW_MEDIUM DIRECTIONAL_SHADOW_HIGH
	#pragma multi_compile_fragment AREA_SHADOW_MEDIUM AREA_SHADOW_HIGH

	#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/PunctualLightCommon.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/HDShadow.hlsl"
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

	CBUFFER_START(UnityPerMaterial)
	int _TargetLightLayerMask;
	CBUFFER_END

	float ComputeDirectionalVisibility(PositionInputs posInput, float3 normalWS, LightLoopContext lightLoopContext, uint targetLayerMask)
	{
		if (_DirectionalShadowIndex < 0)
			return 0.0;

		DirectionalLightData light = _DirectionalLightDatas[_DirectionalShadowIndex];
		if (!IsMatchingLightLayer(light.lightLayers, targetLayerMask))
			return 0.0;
		if (light.lightDimmer <= 0.0 || light.shadowIndex < 0 || light.shadowDimmer <= 0.0)
			return 0.0;

		float3 L = -light.forward;
		float shadow = GetDirectionalShadowAttenuation(
			lightLoopContext.shadowContext,
			posInput.positionSS.xy,
			posInput.positionWS,
			normalWS,
			light.shadowIndex,
			L);

		shadow = lerp(1.0, shadow, light.shadowDimmer);
		return light.lightDimmer * shadow;
	}

	float ComputePunctualVisibility(PositionInputs posInput, float3 normalWS, LightLoopContext lightLoopContext, uint targetLayerMask)
	{
		float visibility = 0.0;
		uint lightStart = 0;
		uint lightCount = 0;

		GetCountAndStart(posInput, LIGHTCATEGORY_PUNCTUAL, lightStart, lightCount);

		for (uint i = 0; i < lightCount; ++i)
		{
			uint lightIdx = FetchIndex(lightStart, i);
			LightData light = FetchLight(lightIdx);

			if (!IsMatchingLightLayer(light.lightLayers, targetLayerMask))
				continue;
			if (light.lightDimmer <= 0.0 || light.shadowIndex < 0 || light.shadowDimmer <= 0.0)
				continue;

			float3 L;
			float4 distances;
			GetPunctualLightVectors(posInput.positionWS, light, L, distances);

			float punctualAttenuation = PunctualLightAttenuation(
				distances,
				light.rangeAttenuationScale,
				light.rangeAttenuationBias,
				light.angleScale,
				light.angleOffset);

			if (distances.x >= light.range || punctualAttenuation <= 0.0)
				continue;

			float shadow = GetPunctualShadowAttenuation(
				lightLoopContext.shadowContext,
				posInput.positionSS,
				posInput.positionWS,
				normalWS,
				light.shadowIndex,
				L,
				distances.x,
				light.lightType == GPULIGHTTYPE_POINT,
				light.lightType != GPULIGHTTYPE_PROJECTOR_BOX);

			shadow = lerp(1.0, shadow, light.shadowDimmer);
			visibility += punctualAttenuation * light.lightDimmer * shadow;
		}

		return visibility;
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		float depth = LoadCameraDepth(varyings.positionCS.xy);
		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		uint2 pixelCoord = uint2(varyings.positionCS.xy);
		uint2 tileCoord = pixelCoord / GetTileSize();
		PositionInputs posInput = GetPositionInput(
			varyings.positionCS.xy,
			_ScreenSize.zw,
			depth,
			UNITY_MATRIX_I_VP,
			UNITY_MATRIX_V,
			tileCoord);

		ApplyCameraRelativeXR(posInput.positionWS);

		NormalData normalData;
		DecodeFromNormalBuffer(posInput.positionSS.xy, normalData);
		float3 normalWS = normalData.normalWS;

		LightLoopContext lightLoopContext;
		lightLoopContext.shadowContext = InitShadowContext();

		uint targetLayerMask = (uint)_TargetLightLayerMask;
		float visibility = 0.0;
		visibility += ComputeDirectionalVisibility(posInput, normalWS, lightLoopContext, targetLayerMask);
		visibility += ComputePunctualVisibility(posInput, normalWS, lightLoopContext, targetLayerMask);

		float shadowMask = saturate(visibility);

		return float4(shadowMask, shadowMask, shadowMask, 1.0);
	}

	ENDHLSL

	SubShader
	{
		Tags { "RenderPipeline" = "HDRenderPipeline" }
		Pass
		{
			Name "Shadow Layer Mask"

			ZWrite Off
			ZTest Always
			Blend Off
			Cull Off

			HLSLPROGRAM
				#pragma fragment FullScreenPass
			ENDHLSL
		}
	}

	Fallback Off
}
