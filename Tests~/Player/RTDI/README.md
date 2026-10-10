# Ho-RTDI release Player probe

This folder is ignored by Unity. `HoRTDIPlayerValidation.Prepare` copies its three
templates into `Assets/RTDIPlayerProbe` in an isolated `.codex-research` project.
The capture helper is never exposed in the package's Renderer Features menu.

Use Unity 6000.3.15f1 with Windows Standalone support, the local URP/lilToon/package
dependencies and `URP_COMPATIBILITY_MODE` in the isolated project's Standalone
defines. Do not use the user's scene or `-nographics`.

Run these Editor entry points in separate hidden batch-mode Unity processes,
using `-force-d3d12 -projectPath <isolated-project> -executeMethod <entry>`:

1. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIPlayerValidation.Prepare`
2. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIPlayerValidation.Build`
3. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIPlayerValidation.BuildCompatibility`

Wait for each process to exit successfully before continuing. The builder saves
ordinary renderer/pipeline/material/Volume/scene assets and produces a Win64 Mono
**release** Player with D3D12 and D3D11. It checks serialized DI/15 NRD compute
references, without adding all package shaders to Always Included. Pipeline settings
cannot be switched at runtime, so the two modes have separate builds.

Run the resulting EXEs with `-batchmode -screen-fullscreen 0 -logFile <log>` and:

| EXE folder | Arguments |
| --- | --- |
| `PlayerBuild` | `-force-d3d12 --result <json>` |
| `PlayerBuildCompatibility` | `-force-d3d12 --compat --result <json>` |
| `PlayerBuild` | `-force-d3d11 --unsupported --result <json>` |

Require exit=0, JSON `pass=true`, the actual requested graphics API/mode and no DI,
NRD, shader or command-buffer errors. Check the packed
`HoRTDIProbe_Data/Plugins/x86_64/HoRTDINRD.dll` SHA-256 against the package DLL.
Do not trust a stale JSON file after process failure. Limit each owned Player to
90 seconds and stop only that process if it fails to exit.

The probe uses a real lilToon receiver, dedicated GB/SB and a hidden-from-camera
world blocker. It checks single-light energy/validity, baked-only exclusion,
Mixed direct light, metal diffuse/independent specular, off-screen occlusion,
power sampling, raw/filtered publication, RELAX noise/energy, disable/recreate and
D3D11 inactivity. It also checks actual Raw/Filtered/Receiver debug presentation
and the receiver tile in the All grid, including retention of the grid-label shader.
The builder stores the HoRTDIVolume as a profile subasset, so the deployed profile
has a real component rather than a missing reference. The probe disposes owned
DI/capture/target resources before exit.
It additionally enables ReSTIR reuse and checks settled light recoloring and
switched-off raw/filtered results after four renders.
The probe also checks two-pixel red/blue albedo preservation and a four-pixel
full-metal strip against adjacent zero-F0 dielectric lighting. These boundary
gates disable ReSTIR reuse to give NRD a deterministic analytic input. The JSON
records maximum albedo error, metal specular relative error and adjacent specular
leak; they do not establish arbitrary colored-specular/material-ID support.
Additional gates cover a four-pixel blue foreground under a point light,
its analytically projected wider shadow, and single-directional-light red/blue
metal detail. The JSON records point-depth error, shadow leak and colored
specular error/leak. These deterministic gates do not establish stochastic
multi-light material-boundary fidelity.
The deployed probe additionally checks stochastic two-white-directional-light
energy across two-pixel metal colors and slow camera movement. Raw specular is
captured independently while NRD stays enabled; toggling NRD for each reference
sample would destroy the history under test. Camera tint compares normalized
RGB ratios, so Monte Carlo intensity variation and fractional material sampling
are not mislabeled as a tint error. The JSON records noisyMetalMean/reference,
noisyCameraMean and noisyCameraTint. The fourth capture texture belongs only to
this isolated test helper, not the production feature.
The multi-candidate initial-visibility gate hides the near point light behind
the serialized blocker while a far point light remains visible, using eight
initial candidates without temporal/spatial reuse. initialVisibilityShadowRatio
must stay within 5% of the independent inverse-square/range reference. This
guards against discarding the stored light using a different SDK-returned sample.
The mixed-lobe proposal gate uses a near point light for a narrow dielectric
highlight and a far, powerful point light for diffuse illumination. Eight initial
candidates run without ReSTIR reuse; NRD remains enabled for 192 warmup and 256
observation frames. The central 16x16 region is checked against independent
inverse-square/range and GGX references (quantized F0=10/255, roughness=76/255).
JSON mixedSpecularRatio and mixedDiffuseRatio must both stay within 5% of unity.
This deployment check does not replace the 128x128 Editor temporal-only audit.
The probe also keeps NRD alive while two normal-incidence directional lights
change intensity through 0.5/0.25, 16/8, 0.0005/0.00025 and back. An independent
GGX reference uses this material's SB FP16 / DI 8-bit roughness of 25/255.
JSON fields hdrRawRelative and hdrRelative report worst mean energy errors.
The 16/8 case has demodulated radiance above FP16 range while physical raw
remains inside the existing FP16 output range. Internal moment/scale recovery,
bright/dark F0 regions and resource reset gates are in the Editor HDR validator.
Compatibility capture samples the published shader globals; their names are not
temporary RT identifiers for `CommandBuffer.Blit`.

This is 64² Mono validation, not IL2CPP, GB generic
fallback/MSAA resolve, dynamic geometry, TAA or production-performance acceptance.
The current real lilToon receiver needs neither GB generic fallback nor MSAA
resolve; their absent `Shader.Find` warnings are recorded separately.

For legacy renderer reference persistence, run three more separate Editor
processes in the same isolated project:

1. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIResourceValidation.Stage`
2. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIResourceValidation.Repair`
3. `lilToon.URP.Extensions.Editor.RTDI.HoRTDIResourceValidation.Verify`

Stage saves one empty library and one partial library with an assigned override.
Repair uses actual feature Create / custom Inspector callbacks, checks dirty and
idempotence behavior, then SaveAssets without explicitly dirtying the renderer or
feature. Verify checks on-disk GUIDs before loading the assets in another process.
Assigned overrides and DI settings must survive. This tests the normal open/save
workflow; it does not run an automatic project-wide migration during a build.
