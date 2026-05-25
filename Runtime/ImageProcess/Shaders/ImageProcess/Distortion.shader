Shader "Hidden/lilToon/URP/ImageProcess/Distortion"
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
            Name "ImageProcess Distortion"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDistortion

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _Intensity;
            float4 _LayerParams0;
            float4 _LayerParams1;
            float _LayerTextureEnabled;
            TEXTURE2D_X(_LayerTexture);

            half4 FragDistortion(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
                float intensity = saturate(_Intensity) * saturate(_LayerParams0.w);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float2 uv = input.texcoord;
                float2 animatedUV = uv * max(_LayerParams1.xy, 0.01) + _Time.y * _LayerParams1.zw;

                float2 offset;
                if (_LayerTextureEnabled > 0.5)
                {
                    half4 map = SAMPLE_TEXTURE2D_X(_LayerTexture, sampler_LinearRepeat, animatedUV);
                    float luma = dot(map.rgb, float3(0.299, 0.587, 0.114));
                    offset = (map.rg * 2.0 - 1.0) * luma * float2(_LayerParams0.y, _LayerParams0.z);
                }
                else
                {
                    float noiseX = frac(sin(dot(animatedUV, float2(12.9898, 78.233))) * 43758.5453);
                    float noiseY = frac(sin(dot(animatedUV + 19.19, float2(39.3468, 11.135))) * 24634.6345);
                    offset = (float2(noiseX, noiseY) * 2.0 - 1.0) * float2(_LayerParams0.y, _LayerParams0.z);
                }

                float2 edgeDistance = 0.5 - abs(uv - 0.5);
                float edgeFade = min(saturate(edgeDistance.x * _LayerParams0.x), saturate(edgeDistance.y * _LayerParams0.x));
                offset *= _LayerParams0.w * edgeFade * 0.1;
                half4 distorted = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + offset);
                return lerp(source, distorted, intensity);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
