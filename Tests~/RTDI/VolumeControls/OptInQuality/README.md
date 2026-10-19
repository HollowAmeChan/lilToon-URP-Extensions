# Explicit RTDI Volume enable and sampling quality

RTDI now requires both the Feature production master and the current camera's
effective Volume enable override. `enable` defaults to false. A missing RTDI
component, unchecked enable, explicit off, inactive component, disabled Volume,
or zero Volume weight cannot activate production without another participating
Volume supplying an enable override. Disabled cameras publish black/inactive
bindings, skip DI dispatch and invalidate their previous history.

The following ten controls move from the Feature inspector to the Volume's
sampling section: local sampling distribution, initial candidates, temporal
resampling, ReSTIR history budget, spatial resampling, neighbor count, neighbor
radius, final visibility sampling, environment candidates and BRDF environment
candidates. Numeric controls use clamped integer/float sliders. Enums and booleans
use standard Volume interpolation. The Feature retains master/Scene View gates,
geometry/light layers, excluded ray materials, ray bias and shader resources.

Other unchecked Volume controls retain serialized Feature values, preserving
existing explicitly enabled scenes and API fixtures. Those legacy fields are
hidden in the Feature inspector. Resolution is per camera and immutable for the
recorded pass; Volume values never write into the shared Feature asset. This
change does not rewrite user materials, renderer assets or Volume profiles.

## Fresh qualification

Unity 6000.3.15f1 / D3D12, isolated RTInputValidation project. RenderGraph and
compatibility `RunVolumeControls` each pass 86 checks:

- All six disabled states above skip real dispatch, clear published output and
  preserve the normal camera target; reenabling starts fresh valid history.
- Global/local Volumes at local weight .5 blend candidate counts 8/24 to16,
  history 4/10 to7, neighbors 4/8 to6, radius 8/16 to12, sky candidates 4/8 to6
  and BRDF candidates 4/0 to2. The selected enums/toggles and the actual recorded
  GPU pass snapshot match the camera's stack.
- Actual A/B/A camera renders preserve independent quality, additive gains and
  denoiser allocation. Feature JSON remains unchanged. Power/triple-final paths
  allocate actual GPU resources; temporal resampling subsequently reuses history.
- Appearance, real RELAX dispatch, sky/reset behavior, gain history retention,
  slider limits and cleared-override fallback remain qualified.

Fresh regressions pass on both paths: `RunCompositeBlend`, `RunSettingsSnapshot`
and `RunTemporalJitter`. The TAA test still obtains main30 / fast6, 64 preview
samples, unchanged paired raw statistics and lower filtered temporal variance.
Its Scene View policy uses controlled .25-second intervals on real Editor
frames; it does not measure scene performance or user-visible convergence time.
The frozen-game-clock Scene View branch also still passes. All eight process
exits and JSON reports are retained here.

The base and Player fixture builders now explicitly enable their Volume. The
base fixture allows its first two camera requests to initialize URP/Volume
state before requiring active DI; a first manually updated stack can be replaced
when the new pipeline is created. No new Player build or Inspector screenshot
qualification is claimed. Existing RTDI energy/motion/performance limits remain.

```powershell
python Tests~/RTDI/VolumeControls/OptInQuality/verify.py
```

This checks preserved reports and source fingerprints, not a fresh Unity run.
Use `--source-commit <revision>` when verifying this evidence after later edits.
