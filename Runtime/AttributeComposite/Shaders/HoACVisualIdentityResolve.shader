Shader "Hidden/lilToon/URP/AttributeComposite/VisualIdentityResolve"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"
            #include "HoACOutlineInheritance.hlsl"
            TEXTURE2D_X(_HoACOutlineBaseId0Texture);
            TEXTURE2D_X(_HoACOutlineBaseId1Texture);
            TEXTURE2D_X(_HoACOutlineBaseCoverageTexture);
            struct Output
            {
                half4 id0 : SV_Target0; half4 id1 : SV_Target1; half4 coverage : SV_Target2;
            };
            Output Frag(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 a = SAMPLE_TEXTURE2D_X(_HoACOutlineBaseId0Texture,sampler_PointClamp,uv);
                float4 b = SAMPLE_TEXTURE2D_X(_HoACOutlineBaseId1Texture,sampler_PointClamp,uv);
                float4 c = SAMPLE_TEXTURE2D_X(_HoACOutlineBaseCoverageTexture,sampler_PointClamp,uv);
                float4 shell = HoAC_ReadOutlineOwner(uv);
                uint owner = HoAC_OutlineOriginalOwner(shell);
                uint ids[5] = { HoObjectBufferDecodeIdExact(a.rg),HoObjectBufferDecodeIdExact(a.ba),
                    HoObjectBufferDecodeIdExact(b.rg),HoObjectBufferDecodeIdExact(b.ba),owner };
                float counts[5] = { c.r*(1-shell.a),c.g*(1-shell.a),c.b*(1-shell.a),c.a*(1-shell.a),owner != 0u ? shell.a : 0 };
                [unroll] for (int i = 0; i < 4; ++i)
                {
                    if (owner != 0u && ids[i] == owner) { counts[i] += counts[4]; counts[4] = 0; }
                    if (counts[i] <= 0) ids[i] = 0;
                }
                // A projected shell can introduce a fifth pixel-level owner when sampling domains
                // differ. Keep the four largest shares without renormalizing or borrowing a neighbor.
                [unroll] for (int i = 0; i < 4; ++i)
                [unroll] for (int j = i+1; j < 5; ++j)
                {
                    if (counts[j] > counts[i])
                    {
                        float count = counts[i]; counts[i] = counts[j]; counts[j] = count;
                        uint id = ids[i]; ids[i] = ids[j]; ids[j] = id;
                    }
                }
                Output output;
                output.id0 = HoObjectBufferPackIdRow(ids[0],ids[1]);
                output.id1 = HoObjectBufferPackIdRow(ids[2],ids[3]);
                output.coverage = half4(counts[0],counts[1],counts[2],counts[3]);
                return output;
            }
            ENDHLSL
        }
    }
}
