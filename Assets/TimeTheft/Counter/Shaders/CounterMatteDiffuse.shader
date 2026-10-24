Shader "TimeTheft/Counter Matte Diffuse"
{
    Properties
    {
        [MainTexture] _BaseMap("Pixel Albedo",2D)="white"{}
        [MainColor] _BaseColor("Paint Color",Color)=(1,1,1,1)
        [HideInInspector] _Cutoff("Cutoff",Float)=0.5
        [HideInInspector] _Cull("Cull",Float)=2
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="SimpleLit"}
        Pass
        {
            Name "MatteDiffuse"
            Tags {"LightMode"="UniversalForwardOnly"}
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;half4 _BaseColor;float _Cutoff;float _Cull;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO};
            V Vert(A a)
            {
                V o=(V)0;UNITY_SETUP_INSTANCE_ID(a);UNITY_TRANSFER_INSTANCE_ID(a,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(a.positionOS.xyz);
                o.positionCS=p.positionCS;o.positionWS=p.positionWS;o.normalWS=TransformObjectToWorldNormal(a.normalOS);
                o.uv=TRANSFORM_TEX(a.uv,_BaseMap);o.fog=ComputeFogFactor(p.positionCS.z);return o;
            }
            half3 Diffuse(Light l,half3 n)
            {
                return l.color*(l.distanceAttenuation*l.shadowAttenuation*saturate(dot(n,l.direction)));
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=normalize(i.normalWS);
                // No emission, brightness floor, reflections or specular. Ambient is the scene SH.
                half3 irradiance=max(0,SampleSH(n));
                irradiance+=Diffuse(GetMainLight(TransformWorldToShadowCoord(i.positionWS)),n);
                uint count=GetAdditionalLightsCount();
                for(uint index=0u;index<count;index++)irradiance+=Diffuse(GetAdditionalLight(index,i.positionWS,half4(1,1,1,1)),n);
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb*_BaseColor.rgb;
                return half4(MixFog(albedo*irradiance,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags {"LightMode"="ShadowCaster"}
            ZWrite On ZTest LEqual ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/UnlitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Unlit/DepthOnly"
    }
}
