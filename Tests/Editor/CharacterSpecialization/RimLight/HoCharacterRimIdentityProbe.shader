Shader "Hidden/HoRim/IdentityProbe"
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
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/CharacterSpecialization/RimLight/HoCharacterRimSampling.hlsl"
            float _HoRimProbeIdentity;
            float _HoRimProbeBoundary;
            float4 _HoRimProbeGroup, _HoRimProbePart, _HoRimProbeSelection, _HoRimProbeOther;
            float4 Frag(Varyings input) : SV_Target
            {
                if (_HoRimProbeBoundary > .5)
                {
                    HoRimLightData source = (HoRimLightData)0;
                    source.worldToShadow = float4x4(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);
                    source.shadow = float4(1,0,0,0);
                    float a = HoRimShadowVisibility(source, float3(-2,0,0), 0);
                    source.shadow.y = .25;
                    float b = HoRimShadowVisibility(source, float3(-2,0,0), 0);
                    source.shadow.y = 1;
                    float c = HoRimShadowVisibility(source, float3(-2,0,0), 0);
                    return float4(a,b,c,1);
                }
                uint identity = (uint)round(_HoRimProbeIdentity);
                return float4(HoRimReceivesIdentity(identity, _HoRimProbeGroup),
                    HoRimReceivesIdentity(identity, _HoRimProbePart),
                    HoRimReceivesIdentity(identity, _HoRimProbeSelection),
                    HoRimReceivesIdentity(identity, _HoRimProbeOther));
            }
            ENDHLSL
        }
    }
}
