# Ray / material truth table (2026-10-30)

Scope: static source audit of the PMX/Ray importer, lilToon URP shaders, HoRT/RTDI/RTGI, NRD RELAX, HoURP 17.3 APV/reflection-probe plumbing, OIT, and local-volume lights. No BREAK_URP assets or runtime code were changed. `confirmed` means directly established by source; `partial` means a bounded path is proven but not the full MME/material domain; `unproven` means no implementation path was found.

## Truth table

| Domain | PMX / raster input | RT visibility / RTGI hit | Practical consequence | Status |
|---|---|---|---|---|
| PMX `shininess` | `HoPmxRayMaterial.ShininessToSmoothness(S) = 1 - (2/(S+2))^0.125`, clamped to [0,1]. Importer writes `_Smoothness`; e.g. S=0→0, 30→0.2928932, 100→0.3882785. | `HoRTGISecondaryMaterial.hlsl` samples `_Smoothness * _SmoothnessTex.r`, then writes `perceptualRoughness = saturate(1-smoothness)`. | PMX shininess is a smoothness input and is inverted exactly once at the RTGI hit boundary to perceptual roughness. | confirmed |
| Ray smoothness map | Map type 0 uses source value; type 1 converts `1-sqrt(value)`; type 2 converts `1-value`; scalar mode can multiply or exponentiate. Importer writes converted pixels to `_SmoothnessTex` and sets scalar `_Smoothness` to 1 when a texture is present. `SMOOTHNESS_MAP_FROM=9` evaluates the PMX-derived smoothness through the Ray mapping. | RTGI samples `_SmoothnessTex.r` at LOD 0 (secondary rays have no derivatives). | Texture channel/UV loop and map semantics survive only for supported 2D inputs; unsupported sources are warnings/manual. | confirmed / bounded |
| Metallic | Ray constant uses `EvaluateMetallic`; a supported texture is converted to `_MetallicGlossMap` and `_Metallic=1`, otherwise `_Metallic` is the clamped scalar. lilToon surface buffer samples metallic map R. | RTGI hit samples `_Metallic * _MetallicGlossMap.r`, clamps it, and uses it for diffuse `(1-metallic)` and specular F0 interpolation. | Metallic affects RTGI bounce energy; no PMX metalness is inferred from shininess. | confirmed |
| Opaque geometry | PMX opaque uses `lilToon`, queue 2000, `RenderType=Opaque`. HoRT scene accepts queue < AlphaTest, rejects Transparent/TransparentCutout and alpha-test keywords. | Adapter supports only shader `lilToon` opaque with queue < AlphaTest (and no main2/main3/parallax); unsupported opaque is still inserted as an occluder with no radiance payload. | Unsupported opaque objects block rays but contribute black/zero secondary radiance. | confirmed |
| Cutout | PMX Ray `KnownCutout` selects Hidden/lilToonCutout, queue 2450, `RenderType=TransparentCutout`, cutoff from `ALPHA_THRESHOLD`. `HoRTCutoutSupport` additionally requires static alpha domain, UV0, no property block, no dither/animation/parallax/main2/main3. | Any-hit evaluates MainTex alpha × `_Color.a`, then AlphaMask modes/scale/value; `alpha < _Cutoff` calls `IgnoreHit()`. Closest-hit then shades the surviving hit with the same smoothness/metallic path. | Holes are transparent to secondary rays; surviving cutout geometry is a real GI emitter/occluder. | confirmed (bounded cutout subset) |
| Transparent | PMX importer selects Hidden/lilToonTransparent, queue 3000, `RenderType=Transparent`, ZWrite=0. HoRT `Opaque` rejects it and `HoRTCutoutSupport` only recognizes Hidden/lilToonCutout. | No RTGI secondary hit adapter or any-hit path for continuous alpha. A transparent mesh is absent from the HoRT surface AS unless separately registered by another feature; there is no alpha blend evaluation in RTGI. | Transparent surfaces do not receive RTGI material radiance and are not modeled as transmissive/partial blockers. | confirmed unsupported |
| OIT | Weighted OIT is a raster RenderGraph feature: it draws a renderer list tagged `lilToonOIT` in a configurable transparent queue range, writes accumulation/revealage, then composites. `clip()` only applies the OIT accumulation threshold. | No ray tracing shader pass, AS registration, or any-hit hook is present in OIT code. | OIT changes raster composition only; it cannot make transparent fragments visible to HoRT/RTGI. | confirmed |
| RTGI sky / environment | HoRTGI settings default `useEnvironment=true`, but `includeDirectEnvironment=false`. The raygen sky query is enabled only when both flags are true and atmosphere is available. `excludedRayMaterials` is an exact material filter passed into the HoRT scene request; excluded submeshes are omitted from AS. | A finite ray miss is an opaque black sample unless direct environment mode performs the extra distant visibility query; when it does, atmosphere radiance is sampled. | Exclude a sky shell material explicitly; this prevents shell hits without changing raster sky. Do not claim generic “sky exclusion” from atmosphere alone. | confirmed (explicit opt-in) |
| RTGI + RELAX | `HoRTGIContext.PrepareDenoiser` creates the NRD context and reports `RELAX diffuse`; renderer dispatches NRD kernels only when denoise is enabled. | RELAX filters the RTGI signal/history; it does not alter AS material classification or add alpha/transparent support. | Denoising is a post-trace signal stage, not a material truth source. | confirmed |
| Reflection probes | lilToon URP shaders compile `_REFLECTION_PROBE_BLENDING`, `_REFLECTION_PROBE_BOX_PROJECTION`, `_REFLECTION_PROBE_ATLAS`; `LIL_GET_ENVIRONMENT_REFLECTION` delegates to URP `GlossyEnvironmentReflection` (or explicit custom cube override). HoURP ForwardLights sets the three keywords from `UniversalLightData`. | HoRTGI secondary hit adapter does not call `GlossyEnvironmentReflection`, reflection-probe APIs, or probe atlas code. | Raster primary shading can use URP reflection probes; RTGI secondary material lighting does not. | confirmed (raster only) |
| APV | lilToon shader variants define `LIL_OPTIMIZE_USE_PROBEVOLUMES`; `lil_common_functions.hlsl` calls `SampleAPV` for L1/L2 SH and applies `_APVWeight`. HoURP ForwardLights enables ProbeVolume keywords only when the asset uses `LightProbeSystem.ProbeVolumes`, updates APV shader variables, and sets `_EnableProbeVolumes`. | RTGI hit adapter directly builds albedo/normal/metallic/roughness/emission and its secondary shading samples analytic lights/environment through RTXDI; no `SampleAPV` call exists in the RTGI adapter. | APV is a raster primary-lighting input; it is not proven as an RTGI secondary-bounce source. | confirmed (raster only) |
| Local volume | `HoVolumeLightLocalPass` builds per-light Point/Spot depth atlases. HD captures independent shadow depth from ShadowCasters; SD uses camera depth/range. Status reports `HD：独立光源遮挡` or SD range/depth. | This is a raster volume-light pass before transparents, not an RT visibility/RTGI material path and not APV/probe data. | Local volume can illuminate participating media and honor cutout ShadowCaster in HD; it does not grant transparent/OIT RTGI support. | confirmed |

## Evidence notes and limitations

* `HoRTScene` is an explicit renderer registry. It does not automatically scan every Unity object; geometry layer filtering, exact material exclusion, mesh topology, UV0 and property-block constraints all affect the AS.
* `HoRTGISecondaryMaterials.IsSupported` is deliberately narrower than raster lilToon. Main 2nd/3rd textures, parallax, unsupported shaders, animated alpha domains and continuous transparency are rejected. Rejected opaque geometry remains an occluder by design.
* The RTGI hit adapter samples texture LOD 0 because secondary rays have no derivatives. This is a deliberate approximation, so anisotropic/mip behavior can differ from the forward pass.
* `HoPmxRayReader` only recognizes Ray source templates whose common material contains the expected helper symbols. Unknown MME fields are retained by the effect audit but are not executed or reconstructed.
* APV/reflection-probe claims are limited to source-level shader/keyword plumbing. No GPU capture was performed in this audit, and no claim is made that a particular scene has a baked APV asset or populated reflection-probe atlas.
* RELAX provenance is pinned in `Runtime/RTDI/ThirdParty/NRD/provenance.json` (NRD 4.17.3 source hashes); this audit does not re-derive NVIDIA algorithm behavior.

## Primary source paths

* `D:/Unity_Fork/HoUnityTools/Editor/PmxImport/Ray/HoPmxRayDocument.cs`
* `D:/Unity_Fork/HoUnityTools/Editor/PmxImport/HoPmxMaterials.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RT/HoRTScene.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RT/HoRTCutoutSupport.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTGI/HoRTGISecondaryMaterials.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTGI/Shaders/HoRTGISecondaryMaterial.hlsl`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTGI/Shaders/HoRTGI.raytrace`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTGI/HoRTGISettings.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/OIT/WeightedOITRendererFeature.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/OIT/WeightedOIT.hlsl`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTGI/HoRTGIContext.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/VolumeLight/HoVolumeLightLocalPass.cs`
* `D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl`
* `D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_functions.hlsl`
* `D:/Unity_Fork/lilToon/Assets/lilToon/Shader/lts_o.shader`
* `D:/Unity_Fork/HoUrp17.3.0/Runtime/ForwardLights.cs`
* `D:/Unity_Fork/HoUrp17.3.0/Runtime/Data/UniversalRenderPipelineAsset.cs`
* `D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/ThirdParty/NRD/provenance.json`
