Shader "Hidden/lilToon/URP/GeometryBuffer/DebugView"
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
            Name "GeometryBuffer Debug View"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryBuffer/Shaders/HoGeometryBufferSampling.hlsl"

            float _HoGeometryBufferDebugMode;
            float4 _HoGeometryBufferDebugDepthParams; // x near, y far, z inv range

            TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture);

            half3 Heat(float value)
            {
                value = saturate(value);
                return saturate(half3(value * 2.0, 1.0 - abs(value - 0.5) * 2.0, (1.0 - value) * 2.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half4 normalDepth = SAMPLE_TEXTURE2D_X(_HoGeometryBufferNormalDepthTexture, sampler_PointClamp, uv);
                int mode = (int)round(_HoGeometryBufferDebugMode);

                if (mode == 1)
                {
                    half coverage = LilHoGeometryBufferCoverage(normalDepth);
                    return lerp(source, half4(1.0, 1.0, 1.0, 1.0), coverage);
                }

                if (mode == 2)
                {
                    half depth = saturate((LilHoGeometryBufferLinearDepthOrFar(normalDepth, _HoGeometryBufferDebugDepthParams.y) - _HoGeometryBufferDebugDepthParams.x) * _HoGeometryBufferDebugDepthParams.z);
                    return half4(depth, depth, depth, 1.0);
                }

                if (mode == 3)
                {
                    return half4(LilHoGeometryBufferEncodedNormalOrBlack(normalDepth), 1.0);
                }

                if (mode == 4)
                {
                    half validity = LilHoGeometryBufferNormalValid(normalDepth);
                    return half4(Heat(validity), 1.0);
                }

                return source;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
