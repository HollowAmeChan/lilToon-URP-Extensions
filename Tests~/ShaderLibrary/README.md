# RTGI shader-library GPU regression

`HoRtgiLibraryChecks.cs` checks the real RTGI entry points after shared headers move to `ShaderLibrary/RTGI`. Copy this single file into an isolated Unity 6000.3/URP17 host's `Assets/Editor`; `Tests~` itself is excluded from package import. The host needs this pipeline package, Cloth_URP, a D3D12 graphics device, and hardware supporting native and inline ray tracing. It must be under `D:/Temp/`. Do not run in BREAK_URP or use `-nographics`.

Set `HO_RTGI_LIBRARY_REPORT` to a fresh absolute JSON path outside the host's imported Assets, then execute `HoRtgiLibraryChecks.Run` in an available private host:

```powershell
$env:HO_RTGI_LIBRARY_REPORT = 'D:/Temp/ho-three-band-restore-20261126/rtgi-library-v1.json'
unity run 'D:/Temp/HoMaterialUiContracts-20261126/Project' --timeout 180 --log-file 'D:/Temp/ho-three-band-restore-20261126/rtgi-library-v1.log' -- -force-d3d12 -executeMethod HoRtgiLibraryChecks.Run
```

The fixture performs bounded import and real GPU checks:

- Invokes the existing internal ShadowCast contract validator through reflection and requires `Validate(false)` to return true, including its UPM lookup of the relocated receiver headers.
- Loads all eight production `HoRTGI.compute` kernels and their compiled thread-group sizes, and records compiler diagnostics.
- Dispatches `BuildEnvironmentPdf` at 1×1 against an explicitly bound constant atmosphere and reads back its finite, positive luminance `.294125`. This rejects a missing or zero GPU write.
- Creates an in-memory Cloth quad and uses the existing runtime material adapter and RT scene through reflection, without adding public product APIs. The existing secondary-validation ray shader performs a real hit and miss; assertions cover distance, shading/geometric normals, base colour, roughness, metallic and F0.
- Dispatches the separate visibility ray entry, checking an occluder status/distance and a preserved miss.
- Dispatches the production `HoRTGI.raytrace` `GenerateSecondary` entry at 1×1 with unavailable primary association. A sentinel 48-byte secondary packet must become the complete invalid-primary zero packet. This compiles and runs the native entry without requiring a render graph or a full GI frame.
- Checks the source material snapshot and hashes of the twelve shared headers, five RTGI entry assets, Cloth shader and their metas remain unchanged. All GPU resources and scene objects are transient and disposed; the fixture never saves assets or scenes, or changes product source or existing material assets.

The report records actual hardware/version, kernel names/sizes, CPU/HLSL strides, GPU PDF and ray payloads, diagnostics and source hashes. Unsupported hardware or any failed check produces a failed report and throws; absence of a GPU run is not a pass. Warnings are retained in the report; compiler, binding and execution errors fail it.

This is a shader-library relocation regression, not a GI energy, convergence, denoising, quality or performance qualification. Results/logs belong in the external run directory. Remove the copied host C# file/meta after the disposable host is no longer needed; no Editor menu or persistent migration tool is installed.

Validated on 2026-11-26 with Unity 6000.3.15f1 / D3D12: all eight kernels, PDF readback, real secondary hit/visibility/miss and production native invalid-primary dispatch passed, with 171 assertions.
