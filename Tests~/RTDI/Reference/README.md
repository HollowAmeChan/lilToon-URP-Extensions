# RTDI reference convergence benchmark

The captured source predates the normal-camera composite implementation. Verify
its preserved evidence against `f537c26` with
`python Tests~/RTDI/Reference/verify_convergence.py --source-commit f537c26`.
Without that argument the verifier continues to require exact current source
hashes. Historical verification is not a benchmark of the current runtime.
Current composition tests and actual camera captures are in [Composite](../Composite/README.md).

Use isolated Unity 6000.3.15f1 projects. Do not run these entry points in the
user's working scene. The reference probe checks for `/.codex-research/` in its
project path, creates an empty temporary scene and an in-memory renderer, and
does not save a replacement user scene or pipeline asset.

## Reference project

The measured copy is `.codex-research/UnityPathTracing-6000.3.15`; native sources
are in `.codex-research/UnityPathTracing/RenderingPlugin`. Keep its existing
plugin binaries before rebuilding anything. Add these files to its
`Assets/Editor/` directory:

- `HoReferenceConvergenceProbe.cs` from this directory.
- `HoRTDIConvergenceStatistics.cs` from the package's `Editor/RTDI/` directory.

This recipe uses the supplied local fork and its existing local shader-tooling
repairs/binaries, whose hashes are recorded in `evidence.json`. It is not a claim
that a clean upstream checkout imports unchanged. Preserve unrelated local edits.

The reference has two integration prerequisites discovered by actual rendering:

1. `PrepareLight.dll` depends on `dxcompiler.dll`, which is in the native-rendering
   package's plugin directory. Add both that directory and `Assets/Plugins/x86_64`
   to the launched Unity process's `PATH`. Do not change the system `PATH`.
2. The pinned NRI commit `1ffdb954d0ab4f950594aa10f83111b1cc441cd9` calls
   `ID3D12Device15::TryCreate*` without checking the runtime device interface.
   `NriDescriptorCompatibility.patch` adds those checks and uses the older
   descriptor methods when necessary. Apply it in `RenderingPlugin/External/NRI`
   after `git apply --check`, then build the existing native `Denoiser` target in
   Release. Copy the resulting **matching NRI.dll and Denoiser.dll only to the
   isolated reference project**. Keep Agility SDK compilation enabled; the SDK's
   older-header build has independent compilation failures. This does not patch
   Unity.exe, system D3D12 libraries, or the Ho-RTDI package's NRD core DLL.

Run one Unity process at a time for a given project, with
`-batchmode -force-d3d12 -projectPath <project> -executeMethod <entry> -logFile <log>`.
Do not use `-nographics`. Entry points:

| Entry | Measures |
| --- | --- |
| `HoReferenceConvergenceProbe.Inventory` | Actual graphics API, receiver/compute availability and local RELAX defaults |
| `HoReferenceConvergenceProbe.RunMatchedRaw` | Unity RTXDI raw combined radiance, RR disabled |
| `HoReferenceConvergenceProbe.RunMatchedRR` | The same scene's raw and actual DLSS RR output |
| `HoReferenceConvergenceProbe.RunMatchedNative` | Native RTXDI input and actual RELAX composition |

The final canonical Unity entries execute the original native emissive preparation
pass too; `emptyEmitterGuard=false` is recorded. An earlier diagnostic entry can
skip that pass after asserting zero emissive triangles, but it is not used for
the final baseline. No DI sampling shader is replaced. Native targets require
`NativeRayTracingTarget`, which the probe attaches to the receiver.

The Native RELAX entry enables `WITH_NRD=1`, normal encoding 2 and roughness
encoding 1, supplies the explicit local NRD include directory, and reimports the
shade/composition shaders. The supplied project originally omitted `WITH_NRD`:
its UI could enable NRD while its final composition still selected raw lighting.
These settings are deliberately saved only in the isolated reference project.

Reports are `ho-reference-convergence108-matched-point-{raw,rr,native-relax}.json`
in that project's root. For Native RELAX, the raw diagnostic reconstructs the
white dielectric signal from the actual diffuse/specular input with the known
constant F0 of .04; it does not use the inactive `DirectLightingRaw` debug texture.
The reference shader's gamma-quantized F0 differs slightly from exact .04. Inspect
raw means as well as normalized variance before comparing reconstructed outputs.

## Ho-RTDI project

Use the package's `.codex-research/RTInputValidation` project and the same Unity
arguments. Prefix the following entries with
`lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.`:

| Entry | Measures |
| --- | --- |
| `RunMatchedConvergence` | Explicit historical 3/1 diffuse history at 60 Hz |
| `RunMatchedConvergenceReferenceHistory` | 30/6 history on the same production shaders/input |
| `RunMatchedConvergenceReferenceRELAX` | Original fixed-version RELAX kernels/defaults on that same input |
| `RunMatchedConvergenceAdaptive` | Actual production stability policy, with no settings override |
| `RunMatchedConvergenceAdaptiveCompatibility` | Production policy through the compatibility path |
| `RunConvergenceShortHistoryMotion` | Historical responsive profile, fixed 60 Hz, with stability policy disabled |
| `RunConvergenceAdaptiveMotion` | Production policy under the identical slow/rapid/step caster sequence |
| `RunConvergenceAdaptiveSpecularMotion` | Independent specular response gates |
| `RunConvergenceAdaptiveDynamic` | Actual Triple analytic light-domain changes |

The canonical fixture is 128x128, FOV 40, camera `(0,0,-3)`, a white dielectric
quad, roughness .5, two red/blue delta point lights at `(±1.5,0,-1)`, intensity 4,
range 100. Both sides use 8 local candidates, temporal history 4, 4 spatial
samples, radius 16 and ray-traced correction. Environment, indirect lighting,
jitter, PT accumulation and extra final samples are disabled. RNG sequences,
quantization and the existing Ho specular-importance multiplier are **not** claimed
to be bit-identical. The separate same-input RELAX run isolates denoising.
Uniform selection, temporal/spatial mode and disabled initial visibility are
selected explicitly instead of relying on inherited renderer-asset defaults.
The fixture has no occluders; these reports do not compare visibility-cache ray
cost or GPU execution time.

Each component is a fixed ROI pixel/RGB channel. Welford temporal variance excludes
spatial texture variation and is normalized by that window's squared per-component
means. Report windows are 1-8, 9-32, 33-128, 129-256, 257-512; first-window startup
of the independently initialized pipelines is not used as an equal-history gate.
Do not equate a finite-history variance floor with an infinitely accumulated
reference image. Diagnostic capture success is not a quality pass.

The production probe additionally checks stability timing at 30/60/120 Hz, clock
rewinds, invalid times and repeated same-time requests. It asserts one initial
history reset and actual promotion to the long budget in the static fixture.

## Player

`HoRTDIPlayerValidation.Prepare` copies the probe templates, then `Build` and
`BuildCompatibility` build separate Win64 Mono release executables. In addition
to the existing default suite, run each executable with `--convergence --result
<json>` (and `--compat` for the compatibility build). This test advances real
application frames, verifies actual private runtime budgets, measures raw/filtered
time variance and then recolours a light to check the one-time responsive restart
and old-colour removal. It does not rely on Editor time overrides.

Require actual exit 0, the correct graphics API/mode, fresh JSON `pass=true`, and
no shader/command-buffer errors. A report with a known failed energy/transition
gate remains failed even when an A/B variant improves it. The final audit links
the captured results and explicitly lists those remaining limits.
