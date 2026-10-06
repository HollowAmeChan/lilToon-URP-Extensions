Shader "Hidden/lilToon/URP/Tests/OutlineHandoffSubject"
{
    Properties
    {
        _HoSemanticWeight("Semantic Weight",Float) = 1
        _TestBand("Pixel Band",Vector) = (0,128,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/ObjectBuffer/Shaders/HoObjectBufferIdPass.hlsl"
        float _HoSemanticWeight;
        float4 _TestBand;
        float4 _HoSemanticLaneIds0, _HoSemanticLaneIds1, _HoSemanticLaneTagMasks0, _HoSemanticLaneTagMasks1;
        struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
        V Vert(A input)
        {
            V o; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(input.positionOS.xyz); return o;
        }
        void Band(V input) { clip(input.positionCS.y-_TestBand.x); clip(_TestBand.y-input.positionCS.y); }
        float Distance(V input) { return input.positionCS.x - (76 + .6 * (input.positionCS.y - 64)+_TestBand.z); }
        half4 ColorFrag(V input) : SV_Target
        {
            Band(input); float d = Distance(input);
            return d < 0 ? half4(0,1,0,1) : d < 3 ? half4(1,0,0,1) : half4(0,0,1,1);
        }
        half4 DepthFrag(V input) : SV_Target { Band(input); return half4(.5,.5,1,Distance(input) < 0 ? 3 : 40); }
        struct ShellOutput { half4 normalDepth : SV_Target0; half4 packet : SV_Target1; };
        ShellOutput OutlineFrag(V input)
        {
            UNITY_SETUP_INSTANCE_ID(input); Band(input);
            float d = Distance(input); if (d < 0 || d >= 3) discard;
            uint owner = unity_RendererUserValue & 0xFFFFu;
            ShellOutput output; output.normalDepth = half4(.5,.5,1,3);
            output.packet = half4((owner >> 8u)/255.0,(owner & 255u)/255.0,_HoSemanticWeight,1); return output;
        }
        float ObjectFrag(V input) : SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input); Band(input); clip(-Distance(input)-.0001);
            return (unity_RendererUserValue & 0xFFFFu)/65535.0;
        }
        struct SurfaceOutput { half4 owner : SV_Target0; half4 lane0 : SV_Target1; half4 lane1 : SV_Target2; half4 lane2 : SV_Target3; half4 lane3 : SV_Target4; };
        float2 Lane(uint lane, uint tags)
        {
            float4 ids = lane < 4u ? _HoSemanticLaneIds0 : _HoSemanticLaneIds1;
            float4 bits = lane < 4u ? _HoSemanticLaneTagMasks0 : _HoSemanticLaneTagMasks1;
            return (tags & (uint)bits[lane & 3u]) != 0u ? float2(ids[lane & 3u]/255.0,_HoSemanticWeight) : 0;
        }
        SurfaceOutput SurfaceFrag(V input)
        {
            UNITY_SETUP_INSTANCE_ID(input); Band(input); clip(-Distance(input)-.0001);
            uint owner = unity_RendererUserValue & 0xFFFFu; uint tags = HoObjectBufferLoadPart(owner).tags;
            SurfaceOutput output; output.owner = half4((owner>>8u)/255.0,(owner&255u)/255.0,0,0);
            output.lane0 = float4(Lane(0,tags),Lane(1,tags)); output.lane1 = float4(Lane(2,tags),Lane(3,tags));
            output.lane2 = float4(Lane(4,tags),Lane(5,tags)); output.lane3 = float4(Lane(6,tags),Lane(7,tags)); return output;
        }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ColorFrag
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="HoGeometryBuffer" }
            ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass
        {
            Name "HO_OUTLINE_NORMAL_DEPTH"
            Tags { "LightMode"="HoGeometryBufferOutlineNormalDepth" }
            ZTest Always ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutlineFrag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="HoObjectBuffer" }
            ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment ObjectFrag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="HoSurfaceSemantic" }
            ZTest Always ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment SurfaceFrag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            ENDHLSL
        }
    }
}
