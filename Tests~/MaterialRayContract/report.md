# lilToon / HoURP material-ray contract audit

Generated: `2026-10-29T18:40:41.024141+00:00`

This is a source-only audit. It does not modify BREAK_URP or run Unity.

| Check | Area | Status |
|---|---|---|
| `oit.resolve` | transparent/OIT | **PASS** |
| `oit.feature` | transparent/OIT | **PASS** |
| `liltoon.alpha` | transparent/Cutout | **PASS** |
| `liltoon.oit-entry` | transparent/OIT | **PASS** |
| `rtgi.sky-exclusion` | RTGI sky exclusion | **PASS** |
| `rtgi.material-domain` | Ray material domain | **PASS** |
| `ray.surface-material` | Ray material domain | **PASS** |
| `plr.material-gate` | PLR/reflection | **PASS** |
| `apv.variant` | APV/probe | **PASS** |
| `gpu.transparent-cutout` | GPU qualification | **UNVERIFIED** |
| `gpu.rtgi-sky` | GPU qualification | **UNVERIFIED** |
| `gpu.plr-probe` | GPU qualification | **UNVERIFIED** |

Static checks: **9 pass**, **0 fail**. Runtime checks awaiting GPU/Unity: **3**.

## Evidence

### `oit.resolve`

Weighted OIT clips alpha and writes weighted accumulation plus revealage.
- `Runtime\OIT\WeightedOIT.hlsl` (59a17c23dfb1aa3b…): clip\s*\(\s*color\.a\s*-\s*_lilOITAlphaClipThreshold @ [22], output\.accumulation\s*= @ [28], output\.revealage\s*= @ [29]
- Caveat: Static shader contract only; blend order and visual correctness need GPU capture.

### `oit.feature`

Ho-WeightedOIT owns reset, opaque copy, accumulation and composite passes.
- `Runtime\OIT\WeightedOITRendererFeature.cs` (09d9115df82f11d1…): WeightedOITResetPass @ [19, 33, 159], WeightedOITOpaqueCopyPass @ [21, 35, 198, 489], WeightedOITAccumulationPass @ [22, 36, 398, 417], WeightedOITCompositePass @ [23, 37, 596], renderer\.EnqueuePass\(clearPass\) @ [81]

### `liltoon.alpha`

lilToon forward alpha uses cutoff and Ho surface alpha rejects invisible ordinary transparent fragments.
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_common_frag_alpha.hlsl` (2b6bb0415a938d35…): clip\s*\(\s*fd\.col\.a\s*-\s*_Cutoff @ [93]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_ho_surface_alpha.hlsl` (06cb253e388b4923…): LIL_RENDER\s*==\s*2 @ [10], clip\s*\(\s*alpha\s*-\s*_Cutoff @ [11], clip\s*\(\s*alpha\s*>\s*0\.0 @ [12]

### `liltoon.oit-entry`

lilToon OIT entry point is present in generated forward include paths.
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_oit.hlsl` (e23f3bd15ed73443…): LIL_OIT_RETURN @ [36], lilOITOutputColor @ [21, 36]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_pass_forward_normal.hlsl` (f8f78a94cca1b76a…): LIL_OIT_RETURN @ [618]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_pass_forward_hair.hlsl` (4a668fe8be5e3e1d…): LIL_OIT_RETURN @ [620]

### `rtgi.sky-exclusion`

RTGI exposes explicit excluded material inputs and forwards them to the ray scene synchronizer.
- `Runtime\RTGI\HoRTGISettings.cs` (267fad96a9bfb24e…): excludedRayMaterials @ [29], includeDirectEnvironment @ [25], useEnvironment @ [23]
- `Runtime\RTGI\HoRTGIRendererFeature.cs` (088c0f26d5860520…): new HoRTViewRequest\([^\n]*settings\.excludedRayMaterials @ [72], useEnvironment @ [107, 174, 179, 188, 243, 269, 276, 372], includeDirectEnvironment @ [174, 179, 188, 276]
- `Runtime\RT\HoRTScene.cs` (cdba6fd6bb143c0f…): excludedMaterials @ [109, 117, 118], excluded\.Contains @ [132, 160, 175], excluded by material filter @ [176]

### `rtgi.material-domain`

GI adapters preserve source properties while rejecting unsupported animated/parallax material domains.
- `Runtime\RTGI\HoRTGISecondaryMaterials.cs` (7c0f01519bd10290…): CopyPropertiesFromMaterial @ [50], HoRTCutoutSupport\.Supports @ [24, 56, 62], remains opaque to RT @ [57]
- `Runtime\RTGI\Shaders\HoRTGISecondaryMaterial.hlsl` (b2783aa471ecf527…): perceptualRoughness\s*=\s*saturate\(1-smoothness\) @ [130], metallic\s*=\s*saturate @ [131], reflectance\s*=\s*saturate @ [131]

### `ray.surface-material`

Primary ray surface reads SB material channels as perceptual roughness/metallic and validates finite data.
- `Runtime\RT\Shaders\HoRTPrimarySurface.hlsl` (682cb34f549394ed…): value\.perceptualRoughness\s*=\s*material\.r @ [71], value\.metallic\s*=\s*material\.g @ [71], value\.reflectance\s*=\s*SAMPLE_TEXTURE2D_X_LOD @ [72], HoRTPrimaryFinite3 @ [40, 76, 77, 78]
- `Runtime\RT\HoRTPrimarySurfaceInputs.cs` (f91b4f901c0c4032…): materialTexture @ [36, 45, 57, 94, 108, 117], reflectionTexture @ [36, 45, 57, 95, 109, 117], attributes @ [45, 54, 60, 62, 116, 121]

### `plr.material-gate`

PLR composite derives smoothness from perceptual roughness and gates by metallic/F0/strength.
- `Runtime\PlanarReflection\HoPlanarReflectionComposite.shader` (0b63c1b08be8cf67…): perceptualRoughness @ [74, 86, 87, 98, 162, 171], smoothness\s*=\s*1\.0h\s*-\s*perceptualRoughness @ [87], metallic @ [74, 75, 88, 99, 164, 165], reflectance @ [74, 76, 89, 100, 164, 165]
- `Runtime\PlanarReflection\HoPlanarReflectionRendererFeature.cs` (9d78860bda1fbbeb…): minSmoothness @ [68, 342, 410], _UsePlanarReflection @ [376], reflectionMaterial @ [367, 534, 663, 664, 672, 676, 712, 727]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_common_frag.hlsl` (9fc634df0db84dc3…): _UsePlanarReflection @ [1978], _PlanarReflectionStrength @ [1978, 2031], smoothnessFade @ [1991, 1992, 2031]

### `apv.variant`

Generated lilToon Standard and Cutout shaders include URP probe-volume variants and runtime APV sampling exists.
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\lts.shader` (96b518bd2c93f6e2…): ProbeVolumeVariants\.hlsl @ [777], _REFLECTION_PROBE_BLENDING @ [767]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\lts_cutout.shader` (e5989f238fec2057…): ProbeVolumeVariants\.hlsl @ [777], _REFLECTION_PROBE_BLENDING @ [767]
- `D:\Unity_Fork\lilToon\Assets\lilToon\Shader\Includes\lil_common_functions.hlsl` (9bc82ffcd400b11f…): SampleAPV @ [1062, 1114], PROBE_VOLUMES_L1 @ [1049, 1067], lilGetLightColorDoubleAPV @ [1128]
- `Runtime\Debug\Shaders\HoDebugTile.shader` (af0925923faf699e…): SampleAPV @ [121, 142], EvaluateAdaptiveProbeVolume @ [138], APV_SAMPLE_STATUS_INVALID @ [44, 69, 125, 130]

## Re-run

```powershell
python Tests~/MaterialRayContract/validate_material_ray_contract.py
```

Unity follow-up is intentionally separate from this static command. Use an isolated project and invoke the existing RTGI/Cutout/PLR validation entry points; do not run against BREAK_URP.
