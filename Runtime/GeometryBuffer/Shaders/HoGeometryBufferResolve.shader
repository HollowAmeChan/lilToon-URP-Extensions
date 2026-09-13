Shader "Hidden/lilToon/URP/GeometryBuffer/Resolve"
{
    // The one place where camera MSAA is turned into single sample facts.
    // Reads every covered sample of the camera's depth/normal target, picks the
    // nearest one as this pixel's geometry, and publishes two numbers into the
    // coverage texture: the fraction of the pixel that carries geometry at all
    // (R) and the fraction owned by the surface it selected (G). Screen space and
    // temporal features consume the single sample result and use G to compensate
    // the pixels that are only partly covered; without this resolve, MSAA leaves
    // them with a one pixel hard edge (a bright line hugging every silhouette).
    // See Documentation~/架构边界/MSAA.md.
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
        #define HO_GEOMETRY_BUFFER_TEXTURE_MS(type, name) Texture2DMSArray<type, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
        #define HO_GEOMETRY_BUFFER_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_ARRAY_MSAA(name, coord, SLICE_ARRAY_INDEX, sampleIndex)
    #else
        #define HO_GEOMETRY_BUFFER_TEXTURE_MS(type, name) Texture2DMS<type, HO_GEOMETRY_BUFFER_MSAA_SAMPLES> name
        #define HO_GEOMETRY_BUFFER_LOAD_MS(name, coord, sampleIndex) LOAD_TEXTURE2D_MSAA(name, coord, sampleIndex)
    #endif

    HO_GEOMETRY_BUFFER_TEXTURE_MS(float4, _HoGeometryBufferResolveNormalDepthTextureMS);
    HO_GEOMETRY_BUFFER_TEXTURE_MS(float, _HoGeometryBufferResolveDepthTextureMS);

    struct ResolvedGeometry
    {
        float4 normalDepth;
        float coverage;
        float selectedCoverage;
        int selectedSample;
    };

    ResolvedGeometry ResolveNormalDepth(uint2 coord)
    {
        ResolvedGeometry output;
        output.normalDepth = 0.0;
        output.coverage = 0.0;
        output.selectedCoverage = 0.0;
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

        // Share of the pixel owned by the surface the resolve selected. Samples
        // are grouped by orientation plus a tight relative depth band: two
        // different surfaces at a silhouette are separated by far more than
        // that, while fp16 depth quantisation and the sub-pixel slope of a
        // grazing surface stay well inside it. Getting this wrong in the
        // permissive direction only under-reports the split, which is the safe
        // failure for a consumer that uses it to weight the background in.
        float selectedDepth = output.normalDepth.a;
        float3 selectedNormalRaw = (float3)output.normalDepth.rgb * 2.0 - 1.0;
        float selectedNormalLength = length(selectedNormalRaw);
        float depthTolerance = max(1.0e-5, abs(selectedDepth) * 0.002);
        if (output.coverage > 0.0 && selectedNormalLength > 1.0e-4)
        {
            float3 selectedNormal = selectedNormalRaw / selectedNormalLength;
            float selectedCount = 0.0;
            UNITY_UNROLL
            for (int sampleIndex = 0; sampleIndex < HO_GEOMETRY_BUFFER_MSAA_SAMPLES; ++sampleIndex)
            {
                float4 normalDepth = HO_GEOMETRY_BUFFER_LOAD_MS(_HoGeometryBufferResolveNormalDepthTextureMS, coord, sampleIndex);
                float covered = step(0.0001, normalDepth.a);
                float depthMatch = step(abs(normalDepth.a - selectedDepth), depthTolerance);
                float3 sampleNormalRaw = (float3)normalDepth.rgb * 2.0 - 1.0;
                float sampleNormalLength = length(sampleNormalRaw);
                float normalMatch = sampleNormalLength > 1.0e-4
                    ? step(0.9, dot(sampleNormalRaw / sampleNormalLength, selectedNormal))
                    : 0.0;
                selectedCount += covered * depthMatch * normalMatch;
            }

            output.selectedCoverage = selectedCount * rcp((float)HO_GEOMETRY_BUFFER_MSAA_SAMPLES);
        }
        else
        {
            // Uncovered, or no usable orientation to group by: report the whole
            // covered area as one surface instead of inventing a split.
            output.selectedCoverage = output.coverage;
        }

        return output;
    }
    ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "GeometryBuffer Resolve"
            ZTest Always
            Cull Off
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

                uint2 coord = uint2(input.positionCS.xy);
                ResolvedGeometry resolved = ResolveNormalDepth(coord);

                GeometryResolveOutput output;
                output.normalDepth = (half4)resolved.normalDepth;
                // R = total coverage (public contract), G = share owned by the
                // surface this resolve selected.
                output.coverage = half4((half)resolved.coverage, (half)resolved.selectedCoverage, 0.0h, 1.0h);
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
            ZTest Always
            Cull Off
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

                ResolvedGeometry resolved = ResolveNormalDepth(uint2(input.positionCS.xy));

                OutlineResolveOutput output;
                output.normalDepth = (half4)resolved.normalDepth;
                output.coverage = half4((half)resolved.coverage, (half)resolved.selectedCoverage, 0.0h, 1.0h);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
