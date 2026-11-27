Shader "Hidden/HoShadows/RayTracedResolve"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_HoRayTracedShadowRaw);
            TEXTURE2D(_HoRayTracedShadowVisibility);
            TEXTURE2D_FLOAT(_HoRayTracedShadowSourceDepth);
            TEXTURE2D_FLOAT(_HoRayTracedShadowCameraDepth);
            float _HoRayTracedShadowLightStrength;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            float EyeDepth(float rawDepth)
            {
                return unity_OrthoParams.w > 0.5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            float4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                uint2 pixel = (uint2)positionCS.xy;
                float4 ray = LOAD_TEXTURE2D(_HoRayTracedShadowRaw, pixel);
                uint owner = (uint)ray.w;
                if (ray.z != 1.0 || owner == 0u || owner > 65535u)
                    return float4(1, 0, 0, 0);

                float cameraDepth = LOAD_TEXTURE2D(_HoRayTracedShadowCameraDepth, pixel).r;
                float sourceDepth = LOAD_TEXTURE2D(_HoRayTracedShadowSourceDepth, pixel).r;
                if (cameraDepth == UNITY_RAW_FAR_CLIP_VALUE || sourceDepth == UNITY_RAW_FAR_CLIP_VALUE)
                    return float4(1, 0, 0, 0);

                float eyeDepth = EyeDepth(cameraDepth);
                if (abs(eyeDepth - EyeDepth(sourceDepth)) > max(0.0001, abs(eyeDepth) * 0.00001))
                    return float4(1, 0, 0, 0);

                float visibility = saturate(LOAD_TEXTURE2D(_HoRayTracedShadowVisibility, pixel).r);
                return float4(lerp(1.0, visibility, saturate(_HoRayTracedShadowLightStrength)), 1, owner >> 8u, owner & 255u);
            }
            ENDHLSL
        }
    }
}
