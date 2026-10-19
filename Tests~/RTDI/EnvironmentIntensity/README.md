# RTDI sky illumination intensity

`HoRTDISettings.environmentIntensity` defaults to1. In the Renderer Feature it
appears as **天空照明强度** beneath the existing **天空环境光** checkbox.
Use0 for no sky illumination, .25 for one quarter,1 for the original result,
or2 for twice the illumination. The checkbox remains the master switch.
Negative or non-finite script values are treated as0.

The gain applies to both diffuse and specular environmental radiance before
initial selection, reuse, MIS, visibility confidence and final shading. It is
not an extra gain on already mixed analytic+sky output. Each pass copies the
shared Atmosphere parameters and changes only its local gain. Visible background,
Atmosphere settings/resources and analytic light intensity are unaffected.
No user scene/renderer asset was modified by this delivery.

At zero, the sky PDF and its RIS segment are released and sky dispatches skip;
the common one-entry dummy binding remains. Gain changes invalidate DI and NRD
history once. Reenable allocates the needed inputs again. A uniform positive
gain does not require rebuilding the cached importance distribution: the PDF
used for sampling and MIS remains self-consistent, while targets/shading use
the new radiance. The setting is captured with the other immutable pass settings.

This control changes lighting, not ray geometry. An opaque sky-dome mesh can
still occlude rays; use the existing excluded-material geometry filter for that
separate case. It also does not implement a complete NPR lighting integration.

## Actual validation

Unity6000.3.15f1 / D3D12, isolated `.codex-research/RTInputValidation` project:

- `RunEnvironmentIntensity`:40 actual GPU checks.
- `RunEnvironmentIntensityCompatibility`:44 checks, including unchanged shared
  compatibility parameters.
- `RunSettingsSnapshot` / `RunSettingsSnapshotCompatibility`:35 checks each.
- `RunComposite`:18 normal-camera checks.
- `RunEnvironmentIntensityLegacy`:a preexisting serialized renderer with no
  `environmentIntensity` field loads with gain1; the asset is not saved.

All six processes exited0. Each full entry is prefixed with
`lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.`. Use Unity `-batchmode
-force-d3d12 -projectPath <isolated project> -executeMethod <entry> -logFile <log>`.
The legacy entry requires the existing `Assets/RTDIPlayerProbe/LegacyPartialRenderer.asset`
fixture produced by the Player preparation flow; it must still lack the new field.

The constant-sky oracle is independent Lambert integration, expected RGB
`gain * (1,.5,.25)`. Over128 rendered observations after8 warmups, RG and
compat diffuse R means are1.0002147 / .2500537 /2.0004294 /0 for gains1/.25/2/0.
Specular R means are.0359285 / .00898213 / .0718570 /0. The tests additionally
exercise analytic-only output at zero, negative/NaN/infinite input, master
disable, zero PDF/RIS release, warm actual NRD history transitions, and unchanged
camera backdrop and shared producer values. No quality gate was relaxed.

This is not a fresh Player build, a full user-scene brightness diagnosis, or a
resolution of the older mixed-environment specular/caster-step/Power failures.
The latest static-convergence evidence remains bound to its own captured commit.

```powershell
python Tests~/RTDI/EnvironmentIntensity/verify.py --source-commit 1ea7d4f
python Tests~/RTDI/SceneView/FastHistory/verify.py --source-commit 7360367
```

These commands check preserved evidence, not a Unity rerun.
