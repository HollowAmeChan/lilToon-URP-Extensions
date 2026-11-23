Shader "Hidden/Ho-CharacterSpecialization/CompositionOrderProbe"
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
            float4 _HoCharacterFaceHairDiffuseParams;
            float4 _HoCharacterHairShadowParams1;
            float4 _HoCharacterHairShadowColor;
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterSpecialization/Shaders/HoCharacterComposition.hlsl"
            float4 _TestSource, _TestEye, _TestDiffuse, _TestEnhanced, _TestSubject;
            float4 _TestAmounts;
            float4 _TestSubjectOptions;
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };
            V Vert(A input) { V output; output.positionCS = TransformObjectToHClip(input.positionOS.xyz); return output; }
            half4 Frag(V input) : SV_Target
            {
                HoCharacterCompositionInputs composition;
                composition.source = _TestSource.rgb;
                composition.eyeColor = _TestEye.rgb;
                composition.eyeAmount = _TestAmounts.x;
                composition.shadowAmount = _TestAmounts.y;
                composition.diffuseColor = _TestDiffuse.rgb;
                composition.diffuseAmount = _TestAmounts.z;
                composition.enhancedColor = _TestEnhanced.rgb;
                composition.enhancedAmount = _TestAmounts.w;
                composition.subjectColor = _TestSubject.rgb;
                composition.subjectAmount = _TestSubjectOptions.x;
                composition.subjectIsFog = _TestSubjectOptions.y;
                return half4(HoCharacterCompose(composition), _TestSource.a);
            }
            ENDHLSL
        }
    }
}
