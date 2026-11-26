Shader "Hidden/lilToon/URP/RTGI/SecondaryMaterial"
{
    Properties
    {
        [HideInInspector] _HoRTGISecondarySupported("Supported", Float) = 0
        [HideInInspector] _HoRTGICutout("Cutout enabled", Float) = 0
        [HideInInspector] _HoRTMaterialFamily("Surface family", Float) = 0
        [HideInInspector] _HoRTAlphaDomain("Alpha domain", Float) = 0
        [HideInInspector] _HoRTPBRMode("PBR sources", Float) = 0
        [HideInInspector] _HoRTPBRFlags("PBR flags", Vector) = (0,0,0,0)
        [HideInInspector] _HoRTMetallicMapScale("Metal map multiplier", Float) = 1
        [HideInInspector] _HoRTSmoothnessMapScale("Smoothness map multiplier", Float) = 1
        [HideInInspector] _HoRTColorAdjustment("Color adjustment", Vector) = (0,1,1,1)
        [HideInInspector] _HoRTNormalEnabled("Normal input", Float) = 1
        [HideInInspector] _HoRTNormalPolicy("Normal frame policy", Float) = 0
        [HideInInspector] _HoRTEmissionMode("Emission mode", Float) = 0
        [HideInInspector] _HoRTTensionEnabled("Tension input", Float) = 0
        [HideInInspector] _HoRTInstanceUserValue("Owner and GeometryData slot", Vector) = (0,0,0,0)
        _AlphaMask("Alpha mask",2D)="white"{}
        _AlphaMaskMode("Mask mode",Float)=0
        _AlphaMaskScale("Mask scale",Float)=1
        _AlphaMaskValue("Mask value",Float)=0
        _Cutoff("Cutoff",Float)=.5
        _MainTex("Main", 2D) = "white" {}
        [HDR] _Color("Canonical linear tint", Color) = (1,1,1,1)
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
        _EmissionMap_UVMode("Legacy emission UV set",Float)=0
        _EmissionMap_ScrollRotate("Legacy emission UV motion",Vector)=(0,0,0,0)
        _EmissionBlendMask_ScrollRotate("Legacy emission mask motion",Vector)=(0,0,0,0)
        _EmissionBlend("Legacy emission weight",Float)=1
        _EmissionBlink("Legacy emission blink",Vector)=(0,0,0,0)
        _EmissionUseGrad("Legacy emission gradient",Float)=0
        _EmissionGradSpeed("Legacy emission gradient speed",Float)=1
        _EmissionMainStrength("Emission albedo influence",Float)=0
        _EmissionParallaxDepth("Legacy emission UV offset",Float)=0
        _HoTesionStrength("Tension strength",Float)=1
        _HoTesionMaps("Tension maps",Vector)=(0,0,0,0)
        _HoTesionStretchBaseColor("Stretch base",2D)="white"{}
        _HoTesionStretchColor("Stretch tint",Color)=(1,1,1,1)
        _HoTesionStretchRange("Stretch range",Vector)=(0.02,0.2,0,0)
        _HoTesionStretchStrength("Stretch weight",Float)=1
        _HoTesionStretchNormal("Stretch normal",2D)="bump"{}
        _HoTesionStretchNormalScale("Stretch normal scale",Float)=1
        _HoTesionCompressionBaseColor("Compression base",2D)="white"{}
        _HoTesionCompressionColor("Compression tint",Color)=(1,1,1,1)
        _HoTesionCompressionRange("Compression range",Vector)=(0.02,0.2,0,0)
        _HoTesionCompressionStrength("Compression weight",Float)=1
        _HoTesionCompressionNormal("Compression normal",2D)="bump"{}
        _HoTesionCompressionNormalScale("Compression normal scale",Float)=1
        _BumpMap("Normal", 2D) = "bump" {}
        _BumpScale("Normal scale", Float) = 1
        _FlipNormal("Flip backface normals", Float) = 0
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
            #pragma shader_feature_local _HO_RT_TENSION
            #include "../../../ShaderLibrary/RTGI/HoRTGISecondaryMaterial.hlsl"
            ENDHLSL
        }
    }
}
