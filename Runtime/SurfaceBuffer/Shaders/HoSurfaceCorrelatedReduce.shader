Shader "Hidden/lilToon/URP/SurfaceBuffer/CorrelatedReduce"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma target 5.0
            #pragma only_renderers d3d11
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local_fragment _ HO_CORRELATED_2 HO_CORRELATED_4
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "../../ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"
            #if defined(HO_CORRELATED_4)
                #define HO_CORRELATED_N 4
            #elif defined(HO_CORRELATED_2)
                #define HO_CORRELATED_N 2
            #else
                #define HO_CORRELATED_N 1
            #endif
            #if HO_CORRELATED_N > 1
                Texture2DMS<float, HO_CORRELATED_N> _HoCorrelatedIdentitySamples;
                Texture2DMS<float4, HO_CORRELATED_N> _HoCorrelatedSurfaceSamples;
            #else
                TEXTURE2D(_HoCorrelatedIdentitySamples);
                TEXTURE2D(_HoCorrelatedSurfaceSamples);
            #endif
            TEXTURE2D(_HoCorrelatedId0);
            TEXTURE2D(_HoCorrelatedId1);
            struct Output { float4 written:SV_Target0; float4 weighted:SV_Target1; float4 status:SV_Target2; };
            Output Frag(Varyings input)
            {
                uint2 pixel=(uint2)input.positionCS.xy;
                float4 id0=LOAD_TEXTURE2D(_HoCorrelatedId0,pixel);
                float4 id1=LOAD_TEXTURE2D(_HoCorrelatedId1,pixel);
                uint ids[4]={HoObjectBufferDecodeIdExact(id0.rg),HoObjectBufferDecodeIdExact(id0.ba),HoObjectBufferDecodeIdExact(id1.rg),HoObjectBufferDecodeIdExact(id1.ba)};
                Output o; o.written=0; o.weighted=0; o.status=0;
                [unroll] for(uint j=0;j<HO_CORRELATED_N;j++)
                {
                    #if HO_CORRELATED_N > 1
                        uint owner=(uint)round(saturate(_HoCorrelatedIdentitySamples.Load(pixel,j))*65535.0);
                        float4 packet=_HoCorrelatedSurfaceSamples.Load(pixel,j);
                        bool pattern=all(abs(_HoCorrelatedIdentitySamples.GetSamplePosition(j)-_HoCorrelatedSurfaceSamples.GetSamplePosition(j))<1e-6);
                    #else
                        uint owner=HoObjectBufferDecodeIdExact(LOAD_TEXTURE2D(_HoCorrelatedIdentitySamples,pixel).rg);
                        float4 packet=LOAD_TEXTURE2D(_HoCorrelatedSurfaceSamples,pixel);
                        bool pattern=true;
                    #endif
                    uint surfaceOwner=HoObjectBufferDecodeIdExact(packet.rg);
                    bool written=packet.a>0.5 && surfaceOwner!=0u;
                    if(!pattern) { o.status.b+=1.0; continue; }
                    if(owner==0u) { if(written) o.status.a+=1.0; continue; }
                    if(!written) { o.status.g+=1.0; continue; }
                    if(surfaceOwner!=owner) { o.status.r+=1.0; continue; }
                    bool found=false;
                    [unroll] for(uint rank=0;rank<4;rank++)
                    {
                        if(ids[rank]!=0u && ids[rank]==owner)
                        { o.written[rank]+=1.0; o.weighted[rank]+=saturate(packet.b); found=true; }
                    }
                    if(!found) o.status.r+=1.0;
                }
                o.written/=HO_CORRELATED_N; o.weighted/=HO_CORRELATED_N; o.status/=HO_CORRELATED_N;
                return o;
            }
            ENDHLSL
        }
    }
}
