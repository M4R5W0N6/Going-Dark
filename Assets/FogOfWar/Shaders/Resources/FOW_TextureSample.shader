Shader "Hidden/FullScreen/FOW/TextureSample"
{
    Properties
    {
        _MainTex("Main Texture", 2DArray) = "grey" {}
        _fowTexture("Texture", 2D) = "white" {}
    }
    HLSLINCLUDE

    #pragma multi_compile NO_BLEED NO_BLEED_SOFT HARD SOFT
    #pragma multi_compile OUTER_SOFTEN INNER_SOFTEN
    #pragma multi_compile PLANE_XZ PLANE_XY PLANE_ZY
    #pragma multi_compile FADE_LINEAR FADE_SMOOTH FADE_EXP

    #pragma vertex Vert

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    #include "FogOfWarLogic.hlsl"

    sampler2D _fowTexture;
    float2 _fowTiling;
    float4 _unKnownColor;

    TEXTURE2D_X(_MainTex);
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        //UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);
        //float3 viewDirection = GetWorldSpaceNormalizeViewDir(posInput.positionWS);
        float3 AWS = GetAbsolutePositionWS(posInput.positionWS);

        float coneCheckOut;
        float2 pos; 
        float height;
#if PLANE_XZ
        pos = AWS.xz;
        height = AWS.y;
#elif PLANE_XY
        pos = AWS.xy;
        height = AWS.z;
#elif PLANE_ZY
        pos = AWS.zy;      
        height = AWS.x;
#endif

#if NO_BLEED
        NoBleedCheck_float(pos, height, coneCheckOut);
#elif NO_BLEED_SOFT
        NoBleedSoft_float(pos, height, coneCheckOut);
#elif HARD
        FowHard_float(pos, height, coneCheckOut);
#elif SOFT
        FowSoft_float(pos, height, coneCheckOut);
#endif
        CustomCurve_float(coneCheckOut, coneCheckOut);

        //float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1);
        float4 color = LOAD_TEXTURE2D_X(_MainTex, varyings.positionCS.xy);

        NormalData normalData;
        DecodeFromNormalBuffer(posInput.positionSS.xy, normalData);
        //cheap triplanar:
        float3 powResult = pow(abs(normalData.normalWS), 64);
        float dotResult = dot(powResult, float3(1, 1, 1));
        float3 lerpVals = round(powResult / dotResult);
        float2 uvSample = lerp(lerp(AWS.xz, AWS.yz, lerpVals.x), AWS.xy, lerpVals.z);

        float4 fog = tex2D(_fowTexture, uvSample * _fowTiling);
        fog = lerp(color, fog, _unKnownColor.w);
        return float4(lerp(fog.rgb * _unKnownColor, color.rgb, coneCheckOut), color.a);
    }

    ENDHLSL

    SubShader
    {
        Tags{ "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "FOW Pass"

            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
