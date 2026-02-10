Shader "Hidden/FOW/URP/FoW_MaskComposite"
{
    Properties
    {
        _FowVisibilityColorTex ("Visibility Color", 2D) = "black" {}
        _FowVisibilityDepthTex ("Visibility Depth", 2D) = "white" {}
        _VisibilityCutoff ("Visibility Cutoff", Range(0,1)) = 0.5
        _UseWorldSampling ("Use World Sampling", Float) = 1
        _CompositeMode ("Composite Mode", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "VisibilityComposite"
            ZWrite On
            ZTest Always
            Cull Back
            Blend One SrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include_with_pragmas "../FogOfWarLogic.hlsl"

            TEXTURE2D_X(_FowVisibilityColorTex);
            SAMPLER(sampler_FowVisibilityColorTex);

            TEXTURE2D_X(_FowVisibilityDepthTex);
            SAMPLER(sampler_FowVisibilityDepthTex);

            float4x4 _InvViewProj;
            float _VisibilityCutoff;
            float _UseWorldSampling;
            float _CompositeMode;

            float2 ProjectToFowPlane(float3 worldPos)
            {
                if (_fowPlane == 1)
                    return worldPos.xz;
                if (_fowPlane == 2)
                    return worldPos.xy;
                if (_fowPlane == 3)
                    return worldPos.zy;
                return worldPos.xy;
            }

            float SampleTextureVisibility(float3 worldPos)
            {
                float2 position = ProjectToFowPlane(worldPos);
                float halfX = _worldBounds.x * 0.5;
                float halfY = _worldBounds.z * 0.5;

                float2 boundsMin = float2(_worldBounds.y - halfX, _worldBounds.w - halfY);
                float2 boundsMax = float2(_worldBounds.y + halfX, _worldBounds.w + halfY);

                if (position.x < boundsMin.x || position.x > boundsMax.x || position.y < boundsMin.y || position.y > boundsMax.y)
                    return 0;

                float2 uv;
                uv.x = ((position.x - _worldBounds.y) + halfX) / max(_worldBounds.x, 0.0001);
                uv.y = ((position.y - _worldBounds.w) + halfY) / max(_worldBounds.z, 0.0001);
                return 1.0 - tex2D(_FowRT, uv).r;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord.xy;
                float4 baseColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float4 layerColor = SAMPLE_TEXTURE2D_X(_FowVisibilityColorTex, sampler_LinearClamp, uv);

                float sceneDepth = SampleSceneDepth(uv);
                float fowMaskR = 0.0;
                if (abs(sceneDepth - UNITY_RAW_FAR_CLIP_VALUE) > 0.000001)
                {
                    float3 sceneWorldPos = ComputeWorldSpacePosition(uv, sceneDepth, _InvViewProj);
                    float visibility = 0;
                    if (_UseWorldSampling > 0.5)
                    {
                        FOW_Sample_WS_float(sceneWorldPos, visibility);
                    }
                    else
                    {
                        visibility = SampleTextureVisibility(sceneWorldPos);
                    }

                    fowMaskR = step(_VisibilityCutoff, visibility);
                }

                float4 renderFoWOutput = float4(fowMaskR, fowMaskR, fowMaskR, 1.0);

                // Render FoW debug map from scene depth (not from captured layer depth).
                if (_CompositeMode < 1.5 && _CompositeMode >= 0.5)
                {
                    return renderFoWOutput;
                }

                // RenderLayer output is provided by the C# pass as an already-rendered image.
                float4 renderLayerOutput = layerColor;

                // Render layer directly with no FoW masking.
                if (_CompositeMode < 0.5)
                {
                    return renderLayerOutput;
                }

                // None (normal composite): mask the RenderLayer output with RenderFoW.r.
                return float4(lerp(baseColor.rgb, renderLayerOutput.rgb, saturate(renderFoWOutput.r)), 1.0);
            }
            ENDHLSL
        }
    }
}
