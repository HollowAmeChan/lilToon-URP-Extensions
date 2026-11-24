Shader "Hidden/Ho-CharacterSpecialization/HairShadowProbe"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/GeometryData/Shaders/HoGeometryDataReferenceFrame.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterSpecialization/Shaders/HoCharacterHairShadow.hlsl"
            float4 _TestOrigin, _TestNormal, _TestLight, _TestParams;
            float4x4 _TestWorldToClip;
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V Vert(A input) { V output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); return output; }
            float4 Frag(V input) : SV_Target
            {
                HoGDObjectFrame frame;
                frame.originValid = _TestOrigin;
                frame.forward = _TestNormal;
                frame.right = 0;
                frame.up = 0;
                float influence;
                float2 offset = HoCharacterResolveHairShadowDirection(frame, _TestLight, _TestWorldToClip,
                    _TestParams.xy, _TestParams.z, _TestParams.w, influence);
                return float4(offset, influence, 1);
            }
            ENDHLSL
        }
    }
}
