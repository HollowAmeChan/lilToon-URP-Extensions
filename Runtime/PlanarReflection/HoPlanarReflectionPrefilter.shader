Shader "Hidden/lilToon/URP/PLR/Prefilter"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            Name "PLR Tent Prefilter"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _HoPLRPrefilterRadius;

            float3 SampleSource(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 texel = abs(_BlitTexture_TexelSize.xy) * max(_HoPLRPrefilterRadius, 1.0);

                // Normalized 13-tap tent. Repeated application builds a stable,
                // energy-preserving roughness pyramid without clamping HDR radiance.
                float3 color = SampleSource(uv) * 0.20;
                color += SampleSource(uv + texel * float2( 1.0,  0.0)) * 0.10;
                color += SampleSource(uv + texel * float2(-1.0,  0.0)) * 0.10;
                color += SampleSource(uv + texel * float2( 0.0,  1.0)) * 0.10;
                color += SampleSource(uv + texel * float2( 0.0, -1.0)) * 0.10;
                color += SampleSource(uv + texel * float2( 1.0,  1.0)) * 0.0625;
                color += SampleSource(uv + texel * float2(-1.0,  1.0)) * 0.0625;
                color += SampleSource(uv + texel * float2( 1.0, -1.0)) * 0.0625;
                color += SampleSource(uv + texel * float2(-1.0, -1.0)) * 0.0625;
                color += SampleSource(uv + texel * float2( 2.0,  0.0)) * 0.0375;
                color += SampleSource(uv + texel * float2(-2.0,  0.0)) * 0.0375;
                color += SampleSource(uv + texel * float2( 0.0,  2.0)) * 0.0375;
                color += SampleSource(uv + texel * float2( 0.0, -2.0)) * 0.0375;
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
