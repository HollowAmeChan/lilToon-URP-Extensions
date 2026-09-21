Shader "Hidden/Ho-CharacterShadow/Debug"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _HoCSTileRects[16];
            int _HoCSCount, _HoCSDebugMode, _HoCSDebugCharacter;
            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                if (_HoCSDebugMode == 2)
                {
                    if (_HoCSDebugCharacter >= _HoCSCount) return half4(0.2, 0, 0.2, 1);
                    float4 tile = _HoCSTileRects[_HoCSDebugCharacter];
                    uv = tile.xy + uv * tile.zw;
                }
                float depth = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0).r;
                #if !UNITY_REVERSED_Z
                    depth = 1 - depth;
                #endif
                return half4(depth.xxx, 1);
            }
            ENDHLSL
        }
    }
}
