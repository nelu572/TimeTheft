Shader "Hidden/TimeTheft/LoFi"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _LoFi;
        float _SceneIntensity;
        float Bayer(float2 pixel)
        {
            uint2 p = (uint2)pixel & 3;
            const float values[16] = {0,8,2,10,12,4,14,6,3,11,1,9,15,7,13,5};
            return (values[p.y * 4 + p.x] + .5) / 16.0 - .5;
        }
        half4 Grade(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float3 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord).rgb;
            // Work in display space so deep shadows retain more than one quantization step.
            color = LinearToSRGB(max(color, 0));
            // Lift existing low-value detail before it collapses to the same quantized level.
            // Black stays black; midtones/highlights and the palette are left intact.
            float luminance = dot(color, float3(.2126, .7152, .0722));
            float shadowWeight = (1.0 - smoothstep(.10, .30, luminance)) * saturate(luminance * 40.0);
            color *= 1.0 + _LoFi.w * shadowWeight / max(luminance, .0001);
            color *= _SceneIntensity;
            float grain = frac(sin(dot(floor(input.positionCS.xy) + floor(_Time.y * 12), float2(12.9898,78.233))) * 43758.5453) - .5;
            color += grain * _LoFi.z;
            color = floor(saturate(color) * (_LoFi.x - 1) + .5 + Bayer(input.positionCS.xy) * _LoFi.y) / (_LoFi.x - 1);
            return half4(SRGBToLinear(saturate(color)), 1);
        }
        half4 Upscale(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
        }
        ENDHLSL
        Pass
        {
            Name "Quantize"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Grade
            ENDHLSL
        }
        Pass
        {
            Name "Point upscale"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Upscale
            ENDHLSL
        }
    }
}

