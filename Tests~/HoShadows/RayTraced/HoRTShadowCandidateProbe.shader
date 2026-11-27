Shader "Hidden/HoShadows/RayTracedCandidateReadback"
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

            TEXTURE2D_ARRAY(_HoShadowsRayTracedVisibility);
            int _HoShadowsRayTracedActive;
            int _HoShadowsRayTracedMainSlot;
            float4 _HoShadowsRayTracedSlots[64];
            float4 _HoProbeLocalLightIndices;

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return GetFullScreenTriangleVertexPosition(id);
            }

            int FindLocalSlot(int shaderIndex)
            {
                if (shaderIndex < 0 || shaderIndex >= 256)
                    return 0;
                return (int)_HoShadowsRayTracedSlots[shaderIndex >> 2][shaderIndex & 3];
            }

            float4 LoadCandidate(uint2 pixel, int encodedSlot)
            {
                UNITY_BRANCH if (_HoShadowsRayTracedActive == 0 || encodedSlot <= 0)
                    return float4(1, 0, 0, 0);
                return LOAD_TEXTURE2D_ARRAY(_HoShadowsRayTracedVisibility, pixel, encodedSlot - 1);
            }

            float4 Frag(float4 positionCS : SV_POSITION) : SV_Target
            {
                uint2 pixel = (uint2)positionCS.xy;
                int pointSlot = FindLocalSlot((int)_HoProbeLocalLightIndices.x);
                int spotSlot = FindLocalSlot((int)_HoProbeLocalLightIndices.y);
                if (pixel.x == 0)
                    return float4(_HoShadowsRayTracedActive, _HoShadowsRayTracedMainSlot, pointSlot, spotSlot);
                if (pixel.x == 1)
                    return LoadCandidate(uint2(32, 32), pointSlot);
                if (pixel.x == 2)
                    return LoadCandidate(uint2(32, 32), spotSlot);
                return LoadCandidate(pixel, _HoShadowsRayTracedMainSlot);
            }
            ENDHLSL
        }
    }
}
