Shader "TimeTheft/Visual Counter Glyph"
{
    Properties
    {
        _MainTex("Binary Glyph Atlas",2D)="white"{}
        _Color("Text Color",Color)=(1,1,1,1)
        _DisplayGain("Display Brightness",Range(1,1.3))=1.08
        _GlowIntensity("Colored Bloom Emission",Range(0,4))=2
        [HideInInspector] _TextureWidth("Width",Float)=128
        [HideInInspector] _TextureHeight("Height",Float)=128
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off ZWrite Off ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
        half4 _Color; float _TextureWidth; float _TextureHeight;
        float _DisplayGain; float _GlowIntensity;
        CBUFFER_END
        struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
        struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
        V Vert(A a) { V o;o.positionCS=TransformObjectToHClip(a.positionOS.xyz);o.uv=a.uv;o.color=a.color*_Color;return o; }
        half4 Core(V i):SV_Target
        {
            clip(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a-.5h);
            return half4(saturate(i.color.rgb*_DisplayGain),i.color.a);
        }
        half4 Glow(V i):SV_Target
        {
            clip(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a-.5h);
            float peak=max(max(i.color.r,i.color.g),i.color.b);
            return half4(i.color.rgb/max(peak,.0001)*_GlowIntensity,i.color.a);
        }
        ENDHLSL
        // Crisp original binary glyphs are drawn at native resolution after post processing.
        Pass
        {
            Name "PixelGlyph" Tags {"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Core
            ENDHLSL
        }
        // Only the counter's three digit renderers expose this pass to the Bloom input.
        Pass
        {
            Name "CounterGlow" Tags {"LightMode"="CounterGlow"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Glow
            ENDHLSL
        }
    }
}
