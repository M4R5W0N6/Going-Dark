Shader "Hidden/TPSBR/HDRP/VisionMask"
{
	HLSLINCLUDE

	#pragma vertex Vert
	#pragma target 4.5
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
	#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/CookieSampling.hlsl"

	CBUFFER_START(UnityPerMaterial)
	int _TargetLightLayerMask;
	float _UseLinearDepthTex;
	float _UseLocalDepthTex;
	float _CookieLodBias;
	CBUFFER_END

	TEXTURE2D_X(_LinearDepthTex);
	TEXTURE2D_X(_LocalDepthTex);

	float DeviceDepthFromLinearEyeDepth(float linearEyeDepth)
	{
		float z = max(linearEyeDepth, 1e-5);
		return (rcp(z) - _ZBufferParams.w) / _ZBufferParams.z;
	}

	float4 EvaluateCookiePunctualMask(LightData light, float3 lightToSample)
	{
		float4 cookie = float4(1.0, 1.0, 1.0, 1.0);
		int lightType = light.lightType;

		float3x3 lightToWorld = float3x3(light.right, light.up, light.forward);
		float3 positionLS = mul(lightToSample, transpose(lightToWorld));

		if (lightType == GPULIGHTTYPE_POINT)
		{
			cookie.rgb = SamplePointCookie(mul(lightToWorld, lightToSample), light.cookieScaleOffset);
			return cookie;
		}

		float perspectiveZ = (lightType != GPULIGHTTYPE_PROJECTOR_BOX) ? positionLS.z : 1.0;
		float2 positionCS = positionLS.xy / perspectiveZ;

		float z = positionLS.z;
		float r = light.range;
		if (Max3(abs(positionCS.x), abs(positionCS.y), abs(z - 0.5 * r) - 0.5 * r + 1) > light.boxLightSafeExtent)
		{
			cookie.a = 0.0;
			return cookie;
		}

		if (lightType != GPULIGHTTYPE_PROJECTOR_PYRAMID && lightType != GPULIGHTTYPE_PROJECTOR_BOX)
		{
			float iesCut = light.iesCut;
			if (dot(positionCS, positionCS) > (iesCut * iesCut))
			{
				cookie.a = 0.0;
				return cookie;
			}
		}

		float2 positionNDC = positionCS * 0.5 + 0.5;
		float3 sharpCookie = SampleCookie2D(positionNDC, light.cookieScaleOffset, 0.0);
		if (_CookieLodBias > 0.0001)
		{
			float3 softCookie = SampleCookie2D(positionNDC, light.cookieScaleOffset, _CookieLodBias);
			cookie.rgb = max(sharpCookie, softCookie);
		}
		else
		{
			cookie.rgb = sharpCookie;
		}
		return cookie;
	}

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

			float distSq = distances.y;
			float distRcp = distances.z;
			float dist = max(distances.x, 1e-5);
			float cosFwd = distances.w / dist;

			float rangeAttenuation = min(distRcp, 1.0 / PUNCTUAL_LIGHT_THRESHOLD);
			rangeAttenuation *= DistanceWindowing(distSq, light.rangeAttenuationScale, light.rangeAttenuationBias);
			rangeAttenuation = Sq(rangeAttenuation);
			rangeAttenuation = pow(saturate(rangeAttenuation), 0.1);

			float angleAttenuation = Sq(AngleAttenuation(cosFwd, light.angleScale, light.angleOffset));
			float punctualAttenuation = rangeAttenuation * angleAttenuation;

			if (distances.x >= light.range || punctualAttenuation <= 0.0)
				continue;

			float cookieAttenuation = 1.0;
			if (light.cookieMode != COOKIEMODE_NONE)
			{
				float3 lightToSample = posInput.positionWS - light.positionRWS;
				float4 cookie = EvaluateCookiePunctualMask(light, lightToSample);
				float cookieLuminance = max(cookie.r, max(cookie.g, cookie.b));
				cookieAttenuation = saturate(cookieLuminance * cookie.a);
			}

			if (cookieAttenuation <= 0.0)
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
			visibility += punctualAttenuation * cookieAttenuation * light.lightDimmer * shadow;
		}

		return visibility;
	}

	float4 FullScreenPass(Varyings varyings) : SV_Target
	{
		UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);

		uint2 pixelCoord = uint2(varyings.positionCS.xy);

		float depth;
		float linearEyeDepth;
		if (_UseLinearDepthTex > 0.5)
		{
			linearEyeDepth = LOAD_TEXTURE2D_X(_LinearDepthTex, pixelCoord).r;
			if (linearEyeDepth <= 0.0)
				return float4(0.0, 0.0, 0.0, 1.0);

			depth = DeviceDepthFromLinearEyeDepth(linearEyeDepth);
		}
		else
		{
			depth = LoadCameraDepth(varyings.positionCS.xy);
			linearEyeDepth = LinearEyeDepth(depth, _ZBufferParams);
		}

		if (depth == UNITY_RAW_FAR_CLIP_VALUE)
		{
			return float4(0.0, 0.0, 0.0, 1.0);
		}

		if (_UseLocalDepthTex > 0.5)
		{
			float localDepth = LOAD_TEXTURE2D_X(_LocalDepthTex, pixelCoord).r;
			if (localDepth > 0.0)
			{
				float depthEpsilon = max(0.01, linearEyeDepth * 0.001);
				if (abs(localDepth - linearEyeDepth) <= depthEpsilon)
				{
					return float4(0.0, 0.0, 0.0, 1.0);
				}
			}
		}

		uint2 tileCoord = pixelCoord / GetTileSize();
		PositionInputs posInput = GetPositionInput(
			varyings.positionCS.xy,
			_ScreenSize.zw,
			depth,
			UNITY_MATRIX_I_VP,
			UNITY_MATRIX_V,
			tileCoord);

		ApplyCameraRelativeXR(posInput.positionWS);

		float3 normalWS;
		if (_UseLinearDepthTex > 0.5)
		{
			float3 dpdx = ddx(posInput.positionWS);
			float3 dpdy = ddy(posInput.positionWS);
			normalWS = normalize(cross(dpdy, dpdx));
			if (!all(isfinite(normalWS)))
			{
				normalWS = float3(0.0, 1.0, 0.0);
			}
		}
		else
		{
			NormalData normalData;
			DecodeFromNormalBuffer(posInput.positionSS.xy, normalData);
			normalWS = normalData.normalWS;
		}

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
