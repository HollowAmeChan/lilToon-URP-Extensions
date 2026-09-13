Shader "Hidden/lilToon-HoCharacterSpecialization/URP/SemanticMaskBlur"
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
            Name "HoCharacter SemanticMask Blur"

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // objectCustom0..7 is RGBA(bits) by contract: eight independent 0/1 fields, one bit per
            // channel. A box filter works per channel, so both textures can be blurred whole - no
            // channel bleeds into another, and each channel comes out as that semantic's coverage.
            // Never point this at maskId (byte ids), surfaceData (material class), custom0 or
            // eyeData: averaging those is meaningless.
            TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture);
            TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture);
            float4 _HoMetadataBufferMaskIdTexture_TexelSize;
            float4 _HoCharacterSemanticMaskBlurParams; // x radius px, y max taps per axis

            struct SemanticMaskBlurOutput
            {
                half4 low : SV_Target0;
                half4 high : SV_Target1;
            };

            SemanticMaskBlurOutput Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                // Reading a single-sample 0/1 field only stops being a hard edge once the filter
                // spreads it over at least one texel, so the width floors at 1px.
                float side = 2.0 * max(_HoCharacterSemanticMaskBlurParams.x, 1.0) + 1.0;
                int taps = (int)clamp(round(side), 3.0, max(_HoCharacterSemanticMaskBlurParams.y, 3.0));
                // Spacing stays near or under 2 texels: wider apart and the bilinear taps stop
                // reaching every column, which turns the ramp back into flat plateaus.
                float spacing = side / (float)taps;
                float2 tapStep = _HoMetadataBufferMaskIdTexture_TexelSize.xy * spacing;
                float2 firstTap = _HoMetadataBufferMaskIdTexture_TexelSize.xy * (spacing - side) * 0.5;
                float4 lowSum = 0.0;
                float4 highSum = 0.0;
                [loop]
                for (int y = 0; y < taps; y++)
                {
                    [loop]
                    for (int x = 0; x < taps; x++)
                    {
                        float2 tapUv = uv + firstTap + tapStep * float2((float)x, (float)y);
                        lowSum += SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom0_3Texture, sampler_LinearClamp, tapUv);
                        highSum += SAMPLE_TEXTURE2D_X(_HoMetadataBufferObjectCustom4_7Texture, sampler_LinearClamp, tapUv);
                    }
                }

                float inverseTaps = 1.0 / max((float)(taps * taps), 1.0);
                SemanticMaskBlurOutput output;
                output.low = (half4)saturate(lowSum * inverseTaps);
                output.high = (half4)saturate(highSum * inverseTaps);
                return output;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
