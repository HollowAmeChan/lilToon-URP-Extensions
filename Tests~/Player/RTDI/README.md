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
D3D11 inactivity. It disposes owned DI/capture/target resources before exit.
Compatibility capture samples the published shader globals; their names are not
temporary RT identifiers for `CommandBuffer.Blit`.

This is 64² Mono validation, not IL2CPP, shader-debug retention, GB generic
fallback/MSAA resolve, dynamic geometry, TAA or production-performance acceptance.
The current real lilToon receiver needs neither GB generic fallback nor MSAA
resolve; their absent `Shader.Find` warnings are recorded separately.
