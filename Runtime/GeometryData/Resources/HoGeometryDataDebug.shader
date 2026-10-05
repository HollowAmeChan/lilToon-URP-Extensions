Shader "Hidden/Ho-GeometryData/Debug"
{
    Properties
    {
        _HoGDDebugMode ("Mode", Float) = 1
        _HoGDDebugMaxValue ("Range", Float) = 0.2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "DisableBatching"="True" }
        Pass
        {
            Cull Off ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Shaders/HoGeometryOutline.hlsl"
            #include "../Shaders/HoGeometryTension.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _HoGDDebugMode, _HoGDDebugMaxValue;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 outline : TEXCOORD0;
                float4 tension : TEXCOORD1;
                nointerpolation uint slot : TEXCOORD2;
                float outlineValid : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS);
                #if !defined(UNITY_INSTANCING_ENABLED) || defined(UNITY_USE_RENDERINGLAYER_ARRAY)
                    o.slot = unity_RendererUserValue >> 16u;
                #endif
                float4 encoded;
                if(HoGDOutlineVertexColor(i.vertexID, encoded))
                {
                    float3 n = normalize(i.normalOS);
                    float3 t = normalize(i.tangentOS.xyz);
                    float3 b = cross(n, t) * i.tangentOS.w;
                    float3 direction = mul(encoded.rgb * 2 - 1, float3x3(t, b, n));
                    float3 world = TransformObjectToWorldNormal(direction);
                    o.outline = float4(world * 0.5 + 0.5, encoded.a); o.outlineValid = 1;
                }
                o.tension = HoGDTensionVertex(i.vertexID);
                return o;
            }
            float3 Heat(float value)
            {
                // Black is neutral, blue/cyan/yellow/red increase with magnitude.
                float x = saturate(value / max(_HoGDDebugMaxValue, 0.0001));
                float3 color = saturate(float3(1.5-abs(4*x-3), 1.5-abs(4*x-2), 1.5-abs(4*x-1)));
                return color * saturate(x * 16);
            }
            float4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                uint mode = (uint)_HoGDDebugMode;
                if(mode == 1)
                {
                    if(i.slot == 0)return float4(0.12,0.12,0.12,1);
                    return float4(0.2 + 0.8 * frac(i.slot * float3(0.618034,0.381966,0.754878)), 1);
                }
                if(mode == 7)return i.tension.w > 0.999 ? float4(0.05,1,0.15,1) : float4(1,0,1,1);
                if(i.slot == 0)discard;
                if(mode <= 3)
                {
                    if(i.outlineValid < 0.999)return float4(1,0,1,1);
                    return mode == 2 ? float4(i.outline.rgb,1) : float4(i.outline.aaa,1);
                }
                if(i.tension.w < 0.999)return float4(1,0,1,1);
                return float4(Heat(mode == 4 ? i.tension.x : mode == 5 ? i.tension.y : i.tension.z),1);
            }
            ENDHLSL
        }
    }
}
