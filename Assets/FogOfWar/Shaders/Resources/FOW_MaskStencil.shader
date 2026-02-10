Shader "Hidden/FOW/URP/FoW_MaskStencil"
{
    Properties
    {
        _FowRT ("FoW Texture", 2D) = "white" {}
        _worldBounds ("FoW Bounds", Vector) = (1,0,1,0)
        _fowPlane ("FoW Plane", Int) = 1
        _VisibilityThreshold ("Visibility Threshold", Range(0,1)) = 0.5
        _UseWorldSampling ("Use World Sampling", Float) = 0
        _UseDitheredSoftEdge ("Use Dithered Soft Edge", Float) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ClearStencil"
            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragClear

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexPosition = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = vertexPosition.positionCS;
                return output;
            }

            half4 fragClear(Varyings input) : SV_Target
            {
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "WriteVisibleStencil"
            ZWrite Off
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragMask

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include_with_pragmas "../FogOfWarLogic.hlsl"

            float _VisibilityThreshold;
            float _UseWorldSampling;
            float _UseDitheredSoftEdge;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs vertexPosition = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = vertexPosition.positionCS;
                output.positionWS = vertexPosition.positionWS;
                return output;
            }

            float2 ProjectToFowPlane(float3 worldPos)
            {
                if (_fowPlane == 1)
                {
                    return worldPos.xz;
                }

                if (_fowPlane == 2)
                {
                    return worldPos.xy;
                }

                if (_fowPlane == 3)
                {
                    return worldPos.zy;
                }

                return worldPos.xy;
            }

            half4 fragMask(Varyings input) : SV_Target
            {
                float2 position = ProjectToFowPlane(input.positionWS);

                float halfX = _worldBounds.x * 0.5;
                float halfY = _worldBounds.z * 0.5;

                float2 boundsMin = float2(_worldBounds.y - halfX, _worldBounds.w - halfY);
                float2 boundsMax = float2(_worldBounds.y + halfX, _worldBounds.w + halfY);

                clip(position.x - boundsMin.x);
                clip(boundsMax.x - position.x);
                clip(position.y - boundsMin.y);
                clip(boundsMax.y - position.y);

                float2 uv;
                uv.x = ((position.x - _worldBounds.y) + halfX) / max(_worldBounds.x, 0.0001);
                uv.y = ((position.y - _worldBounds.w) + halfY) / max(_worldBounds.z, 0.0001);

                float visibility = 0;
                if (_UseWorldSampling > 0.5)
                {
                    FOW_Sample_WS_float(input.positionWS, visibility);
                }
                else
                {
                    visibility = 1.0 - tex2D(_FowRT, uv).r;
                }

                if (_UseDitheredSoftEdge > 0.5)
                {
                    float2 pixel = floor(input.positionHCS.xy);
                    static const float bayer4x4[16] =
                    {
                        1.0 / 17.0, 9.0 / 17.0, 3.0 / 17.0, 11.0 / 17.0,
                        13.0 / 17.0, 5.0 / 17.0, 15.0 / 17.0, 7.0 / 17.0,
                        4.0 / 17.0, 12.0 / 17.0, 2.0 / 17.0, 10.0 / 17.0,
                        16.0 / 17.0, 8.0 / 17.0, 14.0 / 17.0, 6.0 / 17.0
                    };
                    int index = ((int)pixel.x & 3) * 4 + ((int)pixel.y & 3);
                    float ditherThreshold = bayer4x4[index];
                    clip(visibility - ditherThreshold);
                }
                else
                {
                    clip(visibility - _VisibilityThreshold);
                }

                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
