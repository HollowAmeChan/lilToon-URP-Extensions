Shader "Hidden/lilToon/URP/RTDI/Composite"
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
            #include "../../RayTracing/Shaders/HoRTPrimarySurface.hlsl"
            TEXTURE2D(_HoRTDICompositeDiffuse);
            TEXTURE2D(_HoRTDICompositeSpecular);
            TEXTURE2D_FLOAT(_CameraDepthTexture);
            float _HoRTDICompositeWeight;
            float4 Frag(Varyings input):SV_Target
            {
                float4 original=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_PointClamp,input.texcoord);
                HoRTPrimarySurface surface;
                if(!HoRTTryLoadPrimarySurfaceAtUV(input.texcoord,surface))return original;
                // Also compare against the actual camera's opaque depth: a
                // forward-only foreground or outline must not inherit a hidden SB.
                float rawDepth=SAMPLE_TEXTURE2D_LOD(_CameraDepthTexture,sampler_PointClamp,input.texcoord,0).r;
                float cameraDepth=HoSurfaceDeviceToEyeDepth(rawDepth,_HoRTPrimaryZBufferParameters,_HoRTPrimaryClipParameters);
                if(abs(cameraDepth-surface.eyeDepth)>max(.0001,abs(surface.eyeDepth)*.00001))return original;
                float4 diffuse=SAMPLE_TEXTURE2D(_HoRTDICompositeDiffuse,sampler_PointClamp,input.texcoord);
                float4 specular=SAMPLE_TEXTURE2D(_HoRTDICompositeSpecular,sampler_PointClamp,input.texcoord);
                if(diffuse.a<.5||specular.a<.5)return original;
                float3 result=diffuse.rgb+specular.rgb+surface.emission;
                // Sources are physical linear HDR even in a Gamma project.
                #if defined(UNITY_COLORSPACE_GAMMA)
                    result=LinearToSRGB(result);
                #endif
                result=MixFog(result,InitializeInputDataFog(float4(surface.positionWS,1),0));
                return float4(lerp(original.rgb,result,saturate(_HoRTDICompositeWeight)),original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
