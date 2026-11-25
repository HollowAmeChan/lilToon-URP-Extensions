Shader "Hidden/lilToon/URP/CharacterCapsuleAO"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_CameraDepthTexture);
        TEXTURE2D(_HoCapsuleVisibility);
        float4 _HoCapsuleScreenSize;
        float4x4 _HoCapsuleViewProjection;
        float4 _HoCapsuleA[64], _HoCapsuleB[64];
        float4 _HoCapsuleParams, _HoCapsuleCross;
        float _HoCapsuleRotation;
        int _HoCapsuleCount;
        struct Varyings
        {
            float4 positionCS : SV_Position;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
        };
        Varyings ReceiverVertex(float3 positionOS : POSITION, float3 normalOS : NORMAL)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(positionOS);
            o.normalWS = TransformObjectToWorldNormal(normalOS);
            o.positionCS = mul(_HoCapsuleViewProjection, float4(o.positionWS, 1));
            return o;
        }
        Varyings FullscreenVertex(uint vertex : SV_VertexID)
        {
            Varyings o = (Varyings)0;
            o.positionCS = GetFullScreenTriangleVertexPosition(vertex); return o;
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
            float hA = dot(a - p, n), hB = dot(b - p, n);
            // A foot-bone pivot on the floor may place the capsule partly below
            // it. Reject only a capsule entirely behind the receiver plane.
            if (max(hA, hB) + r < 0) return 0;
            float gap = max(min(hA, hB) - r, 0);
            if (gap >= _HoCapsuleParams.y) return 0;
            float3 axis = b - a;
            axis -= n * dot(axis, n);
            if (dot(axis, axis) < 1e-8)
            {
                axis = float3(0, 0, 1) - n * n.z;
                if (dot(axis, axis) < 1e-8) axis = float3(1, 0, 0) - n * n.x;
            }
            axis = normalize(axis);
            float3 perpendicular = cross(n, axis);
            float3 offset = p - (a + b) * .5;
            float2 local = float2(dot(offset, axis), dot(offset, perpendicular));
            float2 direction = Rotate(float2(1, 0), _HoCapsuleRotation);
            float amount = max(CrossBand(local, Rotate(direction, _HoCapsuleCross.z * .5)),
                               CrossBand(local, Rotate(direction, -_HoCapsuleCross.z * .5)));
            return amount * (1 - smoothstep(0, _HoCapsuleParams.y, gap));
        }
        float4 ReceiverFragment(Varyings i) : SV_Target
        {
            float raw = LOAD_TEXTURE2D(_CameraDepthTexture, int2(i.positionCS.xy)).r;
            float tolerance = 2e-6 + .5 * (abs(ddx(i.positionCS.z)) + abs(ddy(i.positionCS.z)));
            clip(tolerance - abs(raw - i.positionCS.z));
            float3 n = normalize(i.normalWS);
            float3 p = i.positionWS;
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
            return 1 - saturate(occlusion * _HoCapsuleParams.x);
        }
        float4 CompositeFragment(Varyings i) : SV_Target
        {
            float visibility = LOAD_TEXTURE2D(_HoCapsuleVisibility, int2(i.positionCS.xy)).r;
            return float4(visibility.xxx, 1);
        }
        ENDHLSL
        Pass
        {
            Name "Receiver Visibility"
            ZTest Always ZWrite Off Cull Off ColorMask R
            Blend One One BlendOp Min
            HLSLPROGRAM
            #pragma vertex ReceiverVertex
            #pragma fragment ReceiverFragment
            ENDHLSL
        }
        Pass
        {
            Name "Color Overlay"
            ZTest Always ZWrite Off Cull Off ColorMask RGB
            Blend DstColor Zero
            HLSLPROGRAM
            #pragma vertex FullscreenVertex
            #pragma fragment CompositeFragment
            ENDHLSL
        }
    }
    Fallback Off
}
