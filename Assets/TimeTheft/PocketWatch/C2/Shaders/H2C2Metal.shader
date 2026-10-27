Shader "TimeTheft/H2 C2 Pixel Metal"
{
    Properties
    {
        _OwnerId("UV owner", Float) = 2
        _PartTrim("Trim roughness profile", Float) = 0
        _RedInner("Red inner lid", Float) = 0
        _DebugView("Debug: 0 final, 1 PBR, 2 grid, 3 base, 4 glint, 5 tint", Float) = 0
        [NoScaleOffset] _CellIds48("Original UV48 IDs", 2D) = "black" {}
        [NoScaleOffset] _CellIds96("Original UV96 IDs", 2D) = "black" {}
        [NoScaleOffset] _Position48("Mesh cell48 positions", 2D) = "black" {}
        [NoScaleOffset] _Normal48("Mesh cell48 normals", 2D) = "black" {}
        [NoScaleOffset] _Position96("Mesh cell96 positions", 2DArray) = "" {}
        [NoScaleOffset] _Normal96("Mesh cell96 normals", 2DArray) = "" {}
        [NoScaleOffset] _DetailMap("Original 512 detail", 2D) = "gray" {}
        [NoScaleOffset] _InnerMask("Red inner mask", 2D) = "black" {}
        [NoScaleOffset] _PigmentMap("Red pigment (sRGB)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "H2C2Palette.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _OwnerId, _PartTrim, _RedInner, _DebugView;
        CBUFFER_END
        Texture2D<float4> _CellIds48, _CellIds96, _Position48, _Normal48;
        Texture2DArray<float4> _Position96, _Normal96;
        Texture2D<float4> _DetailMap, _InnerMask, _PigmentMap;

        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        Varyings Vert(Attributes v)
        {
            Varyings o = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS=TransformWorldToHClip(o.positionWS);
            o.normalWS=TransformObjectToWorldNormal(v.normalOS);
            o.uv=v.uv;
            return o;
        }
        float3 C2Lab(float3 rgb)
        {
            float3 lms = float3(dot(rgb,float3(.4122214708,.5363325363,.0514459929)), dot(rgb,float3(.2119034982,.6806995451,.1073969566)), dot(rgb,float3(.0883024619,.2817188376,.6299787005)));
            lms=pow(max(lms,0),1.0/3.0);
            return float3(dot(lms,float3(.2104542553,.7936177850,-.0040720468)),dot(lms,float3(1.9779984951,-2.4285922050,.4505937099)),dot(lms,float3(.0259040371,.7827717662,-.8086757660)));
        }
        uint C2Nearest(float3 rgb)
        {
            float3 lab=C2Lab(saturate(rgb)); float best=1e10; uint result=0;
            [unroll] for(uint k=0;k<13;k++) { float3 d=lab-C2GoldLab[k]; float distance=dot(d,d); if(distance<best){best=distance;result=k;} }
            return result;
        }
        uint CellID(float4 bytes, out bool valid)
        {
            uint4 b=(uint4)round(bytes*255.0);
            uint id=b.r+256*b.g;
            valid=b.a==255 && b.b==(uint)_OwnerId+1 && id!=65535;
            return id;
        }
        // P/N are actual imported mesh samples in this renderer's object space.
        // Every fragment of one owner/cell evaluates the SAME P/N/V/shadow/probe.
        float3 C2PBR(float3 p, float3 n, float inner, float detail, out float3 neutral)
        {
            n=normalize(n); float3 v=GetWorldSpaceNormalizeViewDir(p);
            float3 albedo=lerp(float3(.72305513,.496933,.1714411),float3(.61049557,.381326,.10461649),inner)*lerp(.95,1.05,detail);
            float roughness=lerp(lerp(.28,.43,inner),lerp(.32,.36,inner),_PartTrim)+lerp(-.025,.025,detail);
            BRDFData brdf; half alpha=1;
            InitializeBRDFData(albedo,1,0,1-roughness,alpha,brdf);
            float4 shadowCoord=TransformWorldToShadowCoord(p);
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                shadowCoord=ComputeScreenPos(TransformWorldToHClip(p));
            #endif
            Light main=GetMainLight(shadowCoord,p,half4(1,1,1,1));
            float3 direct=LightingPhysicallyBased(brdf,main,n,v);
            #if defined(_ADDITIONAL_LIGHTS)
                #if USE_FORWARD_PLUS
                [loop] for(uint i=0;i<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);i++)
                    direct+=LightingPhysicallyBased(brdf,GetAdditionalLight(i,p,half4(1,1,1,1)),n,v);
                #endif
                uint count=GetAdditionalLightsCount();
                InputData inputData=(InputData)0;inputData.positionWS=p;
                float4 lightScreen=ComputeScreenPos(TransformWorldToHClip(p));
                inputData.normalizedScreenSpaceUV=lightScreen.xy/max(lightScreen.w,1e-5);
                LIGHT_LOOP_BEGIN(count)
                    direct+=LightingPhysicallyBased(brdf,GetAdditionalLight(lightIndex,p,half4(1,1,1,1)),n,v);
                LIGHT_LOOP_END
            #endif
            float3 r=reflect(-v,n);
            float fresnel=Pow4(1-saturate(dot(n,v)));
            float4 clip=TransformWorldToHClip(p);
            float2 screen=ComputeScreenPos(clip).xy/max(clip.w,1e-5);
            float3 environment=GlossyEnvironmentReflection(r,p,brdf.perceptualRoughness,1,screen);
            float3 gi=EnvironmentBRDF(brdf,SampleSH(n),environment,fresnel);
            // Approximation: neutralize probe chroma at the same direction/mip.
            // This is not Blender's paired neutral-T3 path-traced render.
            float gray=dot(environment,float3(.2126,.7152,.0722));
            neutral=direct+EnvironmentBRDF(brdf,SampleSH(n),gray.xxx,fresnel);
            return direct+gi;
        }
        float3 Evaluate(float4 position, float4 normal, out float3 neutral)
        {
            return C2PBR(TransformObjectToWorld(position.xyz),TransformObjectToWorldNormal(normal.xyz),position.w,normal.w,neutral);
        }
        float4 Frag(Varyings i):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(i);
            int2 texel=clamp((int2)floor(i.uv*512),0,511);
            if(_DebugView==8)return float4(1,1,1,1);
            bool valid48,valid96;
            uint id48=CellID(_CellIds48.Load(int3(texel,0)),valid48);
            uint id96=CellID(_CellIds96.Load(int3(texel,0)),valid96);
            if(!valid48 || !valid96)
            {
                if(_DebugView>=2)return 0;
                float3 neutral;float3 fallback=C2PBR(i.positionWS,i.normalWS,0,_DetailMap.Load(int3(texel,0)).r,neutral);
                return float4(fallback,1);
            }
            int2 at48=int2(id48%256,id48/256),at96=int2(id96%256,id96/256);
            float3 unused;float3 broad=Evaluate(_Position48.Load(int3(at48,0)),_Normal48.Load(int3(at48,0)),unused);
            uint baseIndex=min(C2Nearest(broad),9);
            float3 colors[4];float3 mean=0,neutralMean=0;float square=0;uint bright=0;
            [unroll] for(uint s=0;s<4;s++)
            {
                float3 neutral;
                colors[s]=Evaluate(_Position96.Load(int4(at96,s,0)),_Normal96.Load(int4(at96,s,0)),neutral);
                float y=dot(colors[s],float3(.2126,.7152,.0722));
                mean+=colors[s]*.25;neutralMean+=neutral*.25;square+=y*y*.25;bright+=y>=.55;
            }
            float mu=dot(mean,float3(.2126,.7152,.0722));float sigma=sqrt(max(square-mu*mu,0));
            float3 tail=0;uint tailCount=0;
            [unroll] for(uint t=0;t<4;t++)if(dot(colors[t],float3(.2126,.7152,.0722))>=mu+.4*sigma){tail+=colors[t];tailCount++;}
            uint peak=C2Nearest(.8*mean+.2*tail/max(tailCount,1));
            uint glint=(peak>=10 && bright>=1 && tailCount>=1)?peak:0;
            float projection=dot(C2Lab(saturate(mean)).yz-C2Lab(saturate(neutralMean)).yz,float2(-.915063797654485,-.4033091199342657));
            uint tier=projection>=.007978613934181393?(uint)clamp(round(projection*.45/.012*2),0,2):0;
            float raised=max(baseIndex,glint);
            float d=_DetailMap.Load(int3(texel,0)).r;
            uint index=(uint)clamp(round(raised+round((d-.5)*4)*.4*min(raised/4,1)),0,12);
            if(index<3 || index>9)tier=0;
            float3 color=C2Palette[tier*13+index];
            if(_RedInner>.5 && (uint)_OwnerId==9 && _InnerMask.Load(int3(texel,0)).r>.5)
            {
                float y=dot(color,float3(.2126,.7152,.0722));
                float gain=y>=.30?1:y>=.12?.76:y>=.035?.48:y>=.008?.22:.055;
                color=_PigmentMap.Load(int3(texel,0)).rgb*gain;
            }
            if(_DebugView==1)color=broad;
            if(_DebugView==2)color=float3((id48%31)/30.0,(id96%37)/36.0,0);
            if(_DebugView==3)color=baseIndex/12.0;
            if(_DebugView==4)color=glint/12.0;
            if(_DebugView==5)color=tier/2.0;
            if(_DebugView==6)return float4(id48/65535.0,id96/65535.0,(_OwnerId+1)/16.0,1);
            return float4(color,1); // Already lit linear palette; never light twice.
        }
        ENDHLSL
        Pass
        {
            Name "C2Forward" Tags { "LightMode"="UniversalForwardOnly" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection, _LightPosition;
            Varyings ShadowVert(Attributes v)
            {
                Varyings o=Vert(v);
                float3 direction=_LightDirection;
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    direction=normalize(_LightPosition-o.positionWS);
                #endif
                o.positionCS=TransformWorldToHClip(ApplyShadowBias(o.positionWS,o.normalWS,direction));
                #if UNITY_REVERSED_Z
                    o.positionCS.z=min(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #else
                    o.positionCS.z=max(o.positionCS.z,UNITY_NEAR_CLIP_VALUE);
                #endif
                return o;
            }
            float4 DepthFrag(Varyings i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            float4 DepthFrag(Varyings i):SV_Target{return i.positionCS.z;}
            ENDHLSL
        }
    }
    Fallback Off
}
