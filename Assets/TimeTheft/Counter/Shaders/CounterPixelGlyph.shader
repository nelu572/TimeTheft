Shader "TimeTheft/Counter Pixel Glyph"
{
    Properties
    {
        _MainTex("Binary Glyph Atlas",2D)="white"{}
        _Color("Text Color",Color)=(1,1,1,1)
        [HideInInspector] _TextureWidth("Width",Float)=128
        [HideInInspector] _TextureHeight("Height",Float)=128
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Pass
        {
            Name "PixelGlyph"
            Tags {"LightMode"="SRPDefaultUnlit"}
            Cull Off ZWrite Off ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;float _TextureWidth;float _TextureHeight;
            CBUFFER_END
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            V Vert(A a){V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.color=a.color*_Color;return o;}
            half4 Frag(V i):SV_Target
            {
                // Exact binary coverage: no SDF smoothing, outline, glow or per-glyph screen snapping.
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;clip(alpha-.5h);
                return i.color;
            }
            ENDHLSL
        }
    }
}
