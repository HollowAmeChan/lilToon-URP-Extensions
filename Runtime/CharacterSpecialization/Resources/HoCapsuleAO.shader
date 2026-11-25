Shader "Hidden/lilToon/URP/CharacterCapsuleAO"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_HoCapsuleGeometryDepth);
        TEXTURE2D(_HoCapsuleGeometryNormal);
        TEXTURE2D(_HoCapsuleReceiverMask);
        float4 _HoCapsuleScreenSize;
        float4x4 _HoCapsuleViewProjection, _HoCapsuleInverseViewProjection;
        float4 _HoCapsuleA[64], _HoCapsuleB[64];
        float4 _HoCapsuleParams, _HoCapsuleCross;
        float _HoCapsuleRotation;
        int _HoCapsuleCount;

        struct Varyings { float4 positionCS : SV_Position; };
        Varyings MaskVertex(float3 positionOS : POSITION)
        {
            Varyings o;
            o.positionCS = mul(_HoCapsuleViewProjection, float4(TransformObjectToWorld(positionOS), 1));
            return o;
        }
        float4 MaskFragment(Varyings i) : SV_Target
        {
            float raw = LOAD_TEXTURE2D(_HoCapsuleGeometryDepth, int2(i.positionCS.xy)).r;
            // Both values are raw device depth. The small gradient allowance accommodates
            // sample positions in the resolved GB without accepting distant foreground.
            float tolerance = 2e-6 + .5 * (abs(ddx(i.positionCS.z)) + abs(ddy(i.positionCS.z)));
            clip(tolerance - abs(raw - i.positionCS.z));
            return 1;
        }
        Varyings FullscreenVertex(uint vertex : SV_VertexID)
        {
            Varyings o; o.positionCS = GetFullScreenTriangleVertexPosition(vertex); return o;
        }

        float CapsuleOcclusion(float3 p, float3 n, float3 a, float3 b, float r)
        {
            float3 axis = b - a;
            float t = saturate(dot(p - a, axis) / max(dot(axis, axis), 1e-10));
            float3 delta = lerp(a, b, t) - p;
            float d2 = max(dot(delta, delta), 1e-10);
            float d = sqrt(d2);
            float radiusAngle = min(r / d, 1);
            float hemisphere = smoothstep(-radiusAngle, max(radiusAngle, 1e-5), dot(n, delta) / d);
            float distanceFade = 1 - smoothstep(0, _HoCapsuleParams.y, max(d - r, 0));
            // Closest-sphere projected-solid-angle approximation, not the ZMD Log-SH kernel.
            return saturate(r * r / d2) * hemisphere * distanceFade;
        }
        float2 Rotate(float2 p, float angle)
        {
            float s, c; sincos(angle, s, c); return float2(c * p.x - s * p.y, s * p.x + c * p.y);
        }
        float CrossBand(float2 p, float2 axis)
        {
            float halfWidth = _HoCapsuleCross.y * .5;
            float along = abs(dot(p, axis));
            float across = abs(dot(p, float2(-axis.y, axis.x)));
            // A flat streak fades continuously to both tips. Do not use a
            // capsule signed distance: that produces uniform, round-ended bars.
            float tipFade = 1 - smoothstep(0, max(_HoCapsuleCross.x, 1e-5), along);
            float sideFade = 1 - smoothstep(max(halfWidth - _HoCapsuleCross.w, 0),
                halfWidth + _HoCapsuleCross.w, across);
            return tipFade * sideFade;
        }
        float CrossOcclusion(float3 p, float3 n, float3 a, float3 b, float r)
        {
            float3 origin = (a + b) * .5;
            origin.y = min(a.y, b.y) - r;
            float gap = origin.y - p.y;
            if (gap < -.02 || gap > _HoCapsuleParams.y) return 0;
            float2 axis = (b - a).xz;
            axis = dot(axis, axis) > 1e-8 ? normalize(axis) : float2(0, 1);
            axis = Rotate(axis, _HoCapsuleRotation);
            float2 local = p.xz - origin.xz;
            float amount = max(CrossBand(local, Rotate(axis, _HoCapsuleCross.z * .5)),
                               CrossBand(local, Rotate(axis, -_HoCapsuleCross.z * .5)));
            return amount * (1 - smoothstep(0, _HoCapsuleParams.y, max(gap, 0))) * smoothstep(0, .25, n.y);
        }
        float4 AppendFragment(Varyings i) : SV_Target
        {
            int2 pixel = int2(i.positionCS.xy);
            clip(LOAD_TEXTURE2D(_HoCapsuleReceiverMask, pixel).r - .5);
            float4 geometry = LOAD_TEXTURE2D(_HoCapsuleGeometryNormal, pixel);
            clip(geometry.a - .0001);
            float3 n = geometry.rgb * 2 - 1;
            float n2 = dot(n, n); clip(n2 - 1e-8); n *= rsqrt(max(n2, 1e-8));
            float raw = LOAD_TEXTURE2D(_HoCapsuleGeometryDepth, pixel).r;
            #if !UNITY_REVERSED_Z
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
            #endif
            float3 p = ComputeWorldSpacePosition(i.positionCS.xy * _HoCapsuleScreenSize.zw, raw, _HoCapsuleInverseViewProjection);
            float occlusion = 0;
            [loop] for (int capsule = 0; capsule < _HoCapsuleCount; capsule++)
            {
                float3 a = _HoCapsuleA[capsule].xyz, b = _HoCapsuleB[capsule].xyz;
                float r = _HoCapsuleA[capsule].w;
                float extent = r + max(_HoCapsuleParams.y, _HoCapsuleCross.x + _HoCapsuleCross.w);
                if (any(abs(p - (a + b) * .5) > abs(b - a) * .5 + extent)) continue;
                float value = _HoCapsuleParams.z < .5 ? CapsuleOcclusion(p, n, a, b, r) : CrossOcclusion(p, n, a, b, r);
                occlusion = max(occlusion, value);
            }
            float visibility = 1 - saturate(occlusion * _HoCapsuleParams.x);
            return float4(visibility.xxx, 1);
        }
        ENDHLSL
        Pass
        {
            Name "Receiver Coverage"
            ZTest Always ZWrite Off Cull Off ColorMask R
            Blend One One BlendOp Max
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            ENDHLSL
        }
        Pass
        {
            Name "Append Min"
            ZTest Always ZWrite Off Cull Off ColorMask R
            Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma vertex FullscreenVertex
            #pragma fragment AppendFragment
            ENDHLSL
        }
        Pass
        {
            Name "Append Multiply"
            ZTest Always ZWrite Off Cull Off ColorMask R
            Blend DstColor Zero
            HLSLPROGRAM
            #pragma vertex FullscreenVertex
            #pragma fragment AppendFragment
            ENDHLSL
        }
    }
    Fallback Off
}
