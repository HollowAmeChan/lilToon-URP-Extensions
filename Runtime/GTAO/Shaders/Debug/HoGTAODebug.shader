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
            float _HoGTAOAllDebugViews;
            TEXTURE2D_X(_HoGTAODebugView0);
            TEXTURE2D_X(_HoGTAODebugView1);
            TEXTURE2D_X(_HoGTAODebugView2);
            TEXTURE2D_X(_HoGTAODebugView3);
            TEXTURE2D_X(_HoGTAODebugView4);

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 value = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, input.texcoord);
                if (_HoGTAOAllDebugViews > 0.5)
                {
                    int mode = (int)_HoGTAODebugViewMode;
                    if (mode == 1) value = SAMPLE_TEXTURE2D_X(_HoGTAODebugView0, sampler_PointClamp, input.texcoord);
                    if (mode == 2) value = SAMPLE_TEXTURE2D_X(_HoGTAODebugView1, sampler_PointClamp, input.texcoord);
                    if (mode == 3) value = SAMPLE_TEXTURE2D_X(_HoGTAODebugView2, sampler_PointClamp, input.texcoord);
                    if (mode == 4) value = SAMPLE_TEXTURE2D_X(_HoGTAODebugView3, sampler_PointClamp, input.texcoord);
                    if (mode == 5) value = SAMPLE_TEXTURE2D_X(_HoGTAODebugView4, sampler_PointClamp, input.texcoord);
                    if (mode != 1) return value;
                }
                // AO/Off are scalar views. Depth, Normal, Motion and
                // Temporal intentionally carry RGB diagnostic semantics.
                if (_HoGTAODebugViewMode < 1.5)
                    value.rgb = value.rrr;
                value.rgb = lerp(value.rgb, 1.0h - value.rgb, _HoGTAODebugInvert);
                value.rgb = pow(saturate(value.rgb), _HoGTAODebugIntensity);
                return value;
            }
            ENDHLSL
        }
    }
}
