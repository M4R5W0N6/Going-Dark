Shader "Hidden/FullScreen/FOW/SolidColor"
{
    Properties
    {
        _MainTex("Main Texture", 2DArray) = "grey" {}
        _fowTexture("Texture", 2D) = "white" {}
    }
    HLSLINCLUDE

    #pragma vertex Vert

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/NormalBuffer.hlsl"
    #include_with_pragmas "../FogOfWarLogic.hlsl"

    float _maxDistance;
    float4 _unKnownColor;

    TEXTURE2D_X(_MainTex);
    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        //float4 color = float4(CustomPassLoadCameraColor(varyings.positionCS.xy, 0), 1);
        float4 color = LOAD_TEXTURE2D_X(_MainTex, varyings.positionCS.xy);
        //UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float depth = LoadCameraDepth(varyings.positionCS.xy);
        PositionInputs posInput = GetPositionInput(varyings.positionCS.xy, _ScreenSize.zw, depth, UNITY_MATRIX_I_VP, UNITY_MATRIX_V);

        if (posInput.linearDepth > _maxDistance)
            return color;

        //float3 viewDirection = GetWorldSpaceNormalizeViewDir(posInput.positionWS);
        float3 AWS = GetAbsolutePositionWS(posInput.positionWS);

        float coneCheckOut = 0;
        float2 pos; 
        float height;

        GetFowSpacePosition(AWS, pos, height);

        FOW_Sample_float(pos, height, coneCheckOut);

        OutOfBoundsCheck(pos, color);
        float3 fogColor = lerp(_unKnownColor.rgb, color.rgb * _unKnownColor.rgb, _unKnownColor.a);
        return float4(lerp(fogColor, color.rgb, coneCheckOut), color.a);
        //return float4(lerp(color.rgb * _unKnownColor, color.rgb, coneCheckOut), color.a);
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
            Blend Off
            Cull Off

            HLSLPROGRAM
                #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
