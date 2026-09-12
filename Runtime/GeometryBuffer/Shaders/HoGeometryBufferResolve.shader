Shader "Hidden/lilToon/URP/GeometryBuffer/Resolve"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

        #if defined(_HO_GEOMETRY_BUFFER_MSAA_2)
            #define HO_GEOMETRY_BUFFER_MSAA_SAMPLES 2
        #elif defined(_HO_GEOMETRY_BUFFER_MSAA_4)
            #define HO_GEOMETRY_BUFFER_MSAA_SAMPLES 4
        #elif defined(_HO_GEOMETRY_BUFFER_MSAA_8)
            #define HO_GEOMETRY_BUFFER_MSAA_SAMPLES 8
        #else
            #define HO_GEOMETRY_BUFFER_MSAA_SAMPLES 2
        #endif

        #if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
            #define HO_GEOMETRY_BUFFER_TEXTURE_MS(name) Texture2DMSArray<float4, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
            #define HO_GEOMETRY_BUFFER_DEPTH_MS(name) Texture2DMSArray<float, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
            #define HO_GEOMETRY_BUFFER_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_ARRAY_MSAA(name, coord, SLICE_ARRAY_INDEX, sampleIndex)
        #else
            #define HO_GEOMETRY_BUFFER_TEXTURE_MS(name) Texture2DMS<float4, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
            #define HO_GEOMETRY_BUFFER_DEPTH_MS(name) Texture2DMS<float, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
            #define HO_GEOMETRY_BUFFER_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_MSAA(name, coord, sampleIndex)
        #endif

        HO_GEOMETRY_BUFFER_TEXTURE_MS(_HoGeometryBufferResolveNormalDepthTextureMS);
        HO_GEOMETRY_BUFFER_DEPTH_MS(_HoGeometryBufferResolveDepthTextureMS);

        struct ResolvedGeometry
        {
            float4 normalDepth;
            float coverage;
            int selectedSample;
        };

        ResolvedGeometry ResolveNormalDepth(int2 coord)
        {
            ResolvedGeometry output;
            output.normalDepth = 0.0;
            output.coverage = 0.0;
            output.selectedSample = 0;

            float nearestLinearDepth = 1.0e20;
            UNITY_UNROLL
            for (int sampleIndex = 0; sampleIndex < HO_GEOMETRY_BUFFER_MSAA_SAMPLES; ++sampleIndex)
            {
                float4 normalDepth = HO_GEOMETRY_BUFFER_LOAD_MS(_HoGeometryBufferResolveNormalDepthTextureMS, coord, sampleIndex);
                float covered = step(0.0001, normalDepth.a);
                output.coverage += covered;
                if (covered > 0.5 && normalDepth.a < nearestLinearDepth)
                {
                    nearestLinearDepth = normalDepth.a;
                    output.normalDepth = normalDepth;
                    output.selectedSample = sampleIndex;
                }
            }

            output.coverage *= rcp((float)HO_GEOMETRY_BUFFER_MSAA_SAMPLES);
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "GeometryBuffer Resolve"
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ _HO_GEOMETRY_BUFFER_MSAA_2 _HO_GEOMETRY_BUFFER_MSAA_4 _HO_GEOMETRY_BUFFER_MSAA_8
            #pragma require msaatex

            struct GeometryResolveOutput
            {
                half4 normalDepth : SV_Target0;
                half4 coverage : SV_Target1;
                float depth : SV_Depth;
            };

            GeometryResolveOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                int2 coord = int2(input.positionCS.xy);
                ResolvedGeometry resolved = ResolveNormalDepth(coord);

                GeometryResolveOutput output;
                output.normalDepth = (half4)resolved.normalDepth;
                output.coverage = half4((half)resolved.coverage, 0.0h, 0.0h, 1.0h);
                output.depth = resolved.coverage > 0.0
                    ? HO_GEOMETRY_BUFFER_LOAD_MS(_HoGeometryBufferResolveDepthTextureMS, coord, resolved.selectedSample)
                    : 1.0;
                return output;
            }
            ENDHLSL
        }

        Pass
        {
            Name "GeometryBuffer Outline Resolve"
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ _HO_GEOMETRY_BUFFER_MSAA_2 _HO_GEOMETRY_BUFFER_MSAA_4 _HO_GEOMETRY_BUFFER_MSAA_8
            #pragma require msaatex

            struct OutlineResolveOutput
            {
                half4 normalDepth : SV_Target0;
                half4 coverage : SV_Target1;
            };

            OutlineResolveOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                ResolvedGeometry resolved = ResolveNormalDepth(int2(input.positionCS.xy));

                OutlineResolveOutput output;
                output.normalDepth = (half4)resolved.normalDepth;
                output.coverage = half4((half)resolved.coverage, 0.0h, 0.0h, 1.0h);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
