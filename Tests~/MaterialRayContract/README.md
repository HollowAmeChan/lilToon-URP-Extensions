# lilToon / HoURP material-ray contract fixture

`validate_material_ray_contract.py` is an independent, read-only source audit for the P0 material and ray constraints. It does not load Unity, mutate assets, or touch `BREAK_URP`. The audit checks:

- ordinary transparent and Cutout alpha clipping, including zero-alpha infrastructure clipping;
- Weighted OIT alpha threshold, accumulation/revealage outputs, and pass registration;
- RTGI sky-shell exclusion wiring from `HoRTGISettings` through `HoRTScene`;
- lilToon metallic/smoothness/reflectance propagation into GI and primary ray surface channels;
- PLR smoothness/metallic/F0 gates and the lilToon planar-reflection consumer;
- generated Standard/Cutout APV variants, runtime APV sampling, and HoURP APV debug views.

Run from the extension checkout:

```powershell
python Tests~/MaterialRayContract/validate_material_ray_contract.py
```

Use an explicit adjacent lilToon checkout when needed:

```powershell
python Tests~/MaterialRayContract/validate_material_ray_contract.py `
  --repo D:/Unity_Fork/lilToon-URP-Extensions `
  --liltoon D:/Unity_Fork/lilToon `
  --out Tests~/MaterialRayContract
```

The command writes `report.json` and `report.md`, including normalized source SHA-256 fingerprints and line numbers for every match. The committed report is a reproducible snapshot from the local extension and lilToon checkouts; rerunning after source changes is expected to update it.

The report intentionally leaves three runtime checks as `UNVERIFIED`: a real transparent/Cutout/OIT render matrix, an RTGI excluded-sky toggle, and a PLR metallic/smoothness plus reflection-probe/APV capture. Those require an isolated Unity 6000.3/URP17 project and D3D12-capable GPU. Existing Editor validators under `Editor/RTGI` and the PLR/RTDI validation suites are the follow-up entry points; do not run them in `BREAK_URP`.

Static result in the snapshot: **9 pass, 0 fail, 3 unverified**.

Suggested isolated Unity follow-up commands (run one process at a time, with a
real D3D12-capable GPU) are:

```powershell
$unity = 'C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe'
$project = 'D:/path/to/.codex-research/MaterialRayValidation'
& $unity -batchmode -force-d3d12 -projectPath $project `
  -executeMethod lilToon.URP.Extensions.Editor.RTGI.HoRTGICutoutLightingValidation.RunCutout `
  -logFile "$project/cutout.log"
& $unity -batchmode -force-d3d12 -projectPath $project `
  -executeMethod lilToon.URP.Extensions.Editor.RTGI.HoRTGIMaterialExclusionValidation.RunMaterialExclusion `
  -logFile "$project/sky-exclusion.log"
& $unity -batchmode -force-d3d12 -projectPath $project `
  -executeMethod lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.Run `
  -logFile "$project/material-probe.log"
```

These are follow-up probes, not part of the static command and are only valid
when the isolated project contains the extension, the adjacent lilToon package,
the required native ray/NRD dependencies, and a configured renderer. Their
JSON outputs must be archived beside the logs; a missing GPU or Unity project
keeps the corresponding checks `UNVERIFIED` rather than being treated as a
pass.
