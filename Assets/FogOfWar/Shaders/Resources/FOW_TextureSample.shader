Shader "Hidden/FullScreen/FOW/TextureSample"
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
    float2 _fowTiling;
    float2 _fowScrollSpeed;
    float4 _unKnownColor;

    sampler2D _fowTexture;
    bool _skipTriplanar;
    float3 _fowAxis;

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

        NormalData normalData;
        DecodeFromNormalBuffer(posInput.positionSS.xy, normalData);
        //cheap triplanar:
        float3 powResult = pow(abs(normalData.normalWS), 8);
        float dotResult = dot(powResult, float3(1, 1, 1));
        //float3 lerpVals = round(powResult / dotResult);
        float3 lerpVals = (powResult / dotResult);
        if (_skipTriplanar)
            lerpVals = _fowAxis;
        //float2 uvSample = lerp(lerp(AWS.xz, AWS.yz, lerpVals.x), AWS.xy, lerpVals.z) + (_Time * _fowScrollSpeed);;
        float2 uvSample1 = AWS.yz + (_Time.yy * _fowScrollSpeed);
        float2 uvSample2 = AWS.xz + (_Time.yy * _fowScrollSpeed);
        float2 uvSample3 = AWS.xy + (_Time.yy * _fowScrollSpeed);

        float4 fogColor = tex2D(_fowTexture, uvSample1 * _fowTiling) * lerpVals.x;
        fogColor += tex2D(_fowTexture, uvSample2 * _fowTiling) * lerpVals.y;
        fogColor += tex2D(_fowTexture, uvSample3 * _fowTiling) * lerpVals.z;

        //float4 fogColor = tex2D(_fowTexture, uvSample * _fowTiling);
        fogColor = lerp(color, fogColor, _unKnownColor.w);
        OutOfBoundsCheck(pos, color);
        OutOfBoundsCheck(pos, fogColor);
        return float4(lerp(fogColor.rgb * _unKnownColor.rgb, color.rgb, coneCheckOut), color.a);
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
