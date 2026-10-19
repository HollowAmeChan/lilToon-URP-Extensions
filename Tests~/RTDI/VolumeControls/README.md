# RTDI appearance controls in Volume

> Historical qualification for `5c404cd`. Current behavior requires explicit
> Volume enable and also moves sampling quality to Volume; see
> [OptInQuality](OptInQuality/README.md). The reports below remain unchanged.

Frequent appearance controls now live in Ho-RTDI Volume. Feature retains the
production master/Scene View switches and collapsed sampling/resource/filter
settings. Appearance values no longer appear in both editors.

| Volume section | Controls |
| --- | --- |
| 运行 | Enable, NRD RELAX |
| 画面合成 | Composite, blend mode, additive gain0..8, replacement weight0..1 |
| 天空照明 | Sky enable, sky intensity0..8 |
| 调试 | Existing debug mode and Scene/Game View controls |

The three numerical appearance controls use standard `ClampedFloatParameter`
sliders. Additive defaults.25, replacement weight1, sky intensity1. New
parameters default to override unchecked. This preserves old profiles and
renderer appearance until the user explicitly chooses an override. Existing
serialized Feature fields and API remain as compatibility fallbacks, hidden
from the Feature editor. No user scene/profile/renderer is rewritten.

`HoRTDIViewSettings` resolves the current camera's Volume once without mutating
the shared Feature. Composite/NRD and the immutable DI pass snapshot consume
these resolved values. Local Volume weight uses normal URP parameter blending;
set a global Volume baseline when mixing local appearance overrides. Clearing
the overrides restores legacy values. The Feature master remains authoritative.

Sky changes invalidate sampling/NRD history; display-only additive gain or mode
does not. Volume-only NRD can start Scene View accumulation even when the legacy
Feature denoise field is false. The bounded repaint driver checks the actual
context/validity rather than the obsolete Feature appearance field.

## Actual qualification

`RunVolumeControls` and `RunVolumeControlsCompatibility` each pass28 checks on
Unity6000.3.15f1 / D3D12 in `.codex-research/RTInputValidation`. The tests render
real normal-camera targets through actual global and collider-based local Volumes.

- Load an old serialized Volume with no new fields and verify unchecked
  overrides retain the Feature's old composite/NRD/sky settings.
- Override actual camera blend/gain, replacement, enable, NRD and sky intensity;
  compare final output and sky+analytic mean with independent expectations.
- A global additive gain.25 plus local gain1 at weight.5 produces.625 for cameraA.
  CameraB uses a trigger outside that Volume and retains.25. Rendering A/B/A
  preserves each camera's values and distinct NRD allocation state. Shared
  Feature JSON remains byte-for-byte unchanged by those Volume edits.
- Exercise real RELAX, sky history restart, display-gain history retention,
  resource release, slider clamping, a Scene View using Volume-only denoising,
  clearing overrides and the Feature master switch.

Fresh regressions also exit0: both `RunCompositeBlend` paths, both
`RunSlowSceneView` paths, `RunEnvironmentIntensity`, and
`RunSettingsSnapshotCompatibility`. Reports and all8 process exits are retained.
The legacy feature-API fixtures now explicitly override only their original
enable/debug parameters; new Volume-appearance tests opt into the new overrides.
Player preparation uses the same explicit fixture policy. No new Player build
or pixel screenshot of the Inspector is claimed; this validates actual runtime
Volume effects and standard slider types/bounds, not UI automation.

Use entry prefix `lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.` with
Unity `-batchmode -force-d3d12 -projectPath <isolated project>
-executeMethod <entry> -logFile <log>`, one process per project. The test needs
the old `Assets/RTDIPlayerProbe/Volume.asset` fixture from Player preparation.

```powershell
python Tests~/RTDI/VolumeControls/verify.py --source-commit 5c404cd
python Tests~/RTDI/Composite/Blend/verify.py --source-commit 94c9128
python Tests~/RTDI/SceneView/FastHistory/verify.py --source-commit 7360367
```

These check preserved source/report evidence, not a fresh Unity rerun. Known
RTDI material/motion/energy/performance gaps remain as recorded in the main guide.
