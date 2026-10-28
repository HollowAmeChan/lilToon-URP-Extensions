Shader "Hidden/lilToon/URP/RTGI/Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "../../RT/Shaders/HoRTPrimarySurface.hlsl"
            TEXTURE2D(_HoRTGICompositeDiffuse);
            TEXTURE2D_FLOAT(_CameraDepthTexture);
            float _HoRTGICompositeIntensity;
            float4 Frag(Varyings input):SV_Target
            {
                float4 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_PointClamp,input.texcoord);
                HoRTPrimarySurface surface;
                if(!HoRTTryLoadPrimarySurfaceAtUV(input.texcoord,surface))return original;
                float depth=SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture,sampler_PointClamp,input.texcoord,0).r;
                float eyeDepth=HoSurfaceDeviceToEyeDepth(depth,_HoRTPrimaryZBufferParameters,_HoRTPrimaryClipParameters);
                if(abs(eyeDepth-surface.eyeDepth)>max(.0001,abs(surface.eyeDepth)*.00001))return original;
                float4 diffuse=SAMPLE_TEXTURE2D(_HoRTGICompositeDiffuse,sampler_PointClamp,input.texcoord);
                if(diffuse.a<.5||!any(diffuse.rgb))return original;
                float fog=InitializeInputDataFog(float4(surface.positionWS,1),0);
                float3 extra=MixFogColor(diffuse.rgb*_HoRTGICompositeIntensity,float3(0,0,0),fog);
                // The input already owns its albedo, emission and fog colour.
                // GI is linear HDR with receiver BRDF/albedo already applied.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return float4(LinearToSRGB(SRGBToLinear(original.rgb)+extra),original.a);
                #else
                    return float4(original.rgb+extra,original.a);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
