Shader "Hidden/lilToon/URP/HoGTAO/DebugView"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend One Zero

        Pass
        {
            Name "Ho-GTAO Debug View"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float _HoGTAODebugIntensity;
            float _HoGTAODebugInvert;
            float _HoGTAODebugViewMode;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 value = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                if (_HoGTAODebugViewMode < 1.5 || _HoGTAODebugViewMode > 4.5)
                    value.rgb = value.rrr;
                value.rgb = lerp(value.rgb, 1.0h - value.rgb, _HoGTAODebugInvert);
                value.rgb = pow(saturate(value.rgb), _HoGTAODebugIntensity);
                return value;
            }
            ENDHLSL
        }
    }
}
