Shader "Hidden/lilToon/URP/RTGI/SecondaryMaterial"
{
    Properties
    {
        [HideInInspector] _HoRTGISecondarySupported("Supported", Float) = 0
        [HideInInspector] _HoRTGICutout("Cutout enabled", Float) = 0
        _AlphaMask("Alpha mask",2D)="white"{}
        _AlphaMaskMode("Mask mode",Float)=0
        _AlphaMaskScale("Mask scale",Float)=1
        _AlphaMaskValue("Mask value",Float)=0
        _Cutoff("Cutoff",Float)=.5
        _MainTex("Main", 2D) = "white" {}
        _Color("Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor("Emission color", Color) = (1,1,1,1)
        _Smoothness("Smoothness", Range(0,1)) = 1
        _PBRInputMode("PBR input mode", Int) = 0
        _Metallic("Metallic", Range(0,1)) = 0
        _Reflectance("Reflectance", Range(0,1)) = .04
        _ReflectanceTex("Reflectance (R)", 2D) = "white" {}
        _PBRMap("PBR (R Metal G Reflectance B AO A Smoothness)", 2D) = "white" {}
        _EmissionMap("Emission", 2D) = "white" {}
        _EmissionBlendMask("Emission mask", 2D) = "white" {}
        _EmissionGradTex("Emission gradient", 2D) = "white" {}
        _BumpMap("Normal", 2D) = "bump" {}
        _SmoothnessTex("Smoothness", 2D) = "white" {}
        _MetallicGlossMap("Metallic", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "HoRTGISecondarySurface"
            Tags { "LightMode" = "HoRTGISecondarySurface" }
            HLSLPROGRAM
            #pragma raytracing HoRTGISecondarySurface
            #include "HoRTGISecondaryMaterial.hlsl"
            ENDHLSL
        }
    }
}
