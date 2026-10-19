# Material selection crash investigation

## 2026-10-19 findings

The reported busy dialog shows `ProjectBrowser.Paint`. This is the editor event waiting for work to finish, not a profile of the function causing the wait.

The 12:25 crash report (`Crash_2026-10-19_042518563/Editor.log`) repeats the earlier D3D12 failure:

- line 1360: GPU fence timeout, followed by `m_CmdState == kEmpty` assertions;
- line 1812: `Your order of command lists with Render Passes is corrupted!`;
- lines 1828–1843: command-list close failure (`80070057`) and unrecoverable GPU device error. The report says neither local nor non-local memory was exhausted.

This report has no D3D12 debug-layer validation message identifying the first invalid command. It does not establish that lilToon ShaderGUI or a particular RendererFeature caused the native crash.

The restarted editor separately reports `HoSubsurfaceScatteringSourcePass.RecordRenderGraph` registering a null SurfaceBuffer texture (previous line 455). The Source pass checked camera color, AC identity and GB, but not its five SB inputs. This has been fixed by validating all five before creating the pass, publishing the skip reason and leaving SSS outputs unavailable when inputs are missing. This is a confirmed recording error; it is **not proof that the native crash is fixed**.

## Next reproduction with validation messages

Save and close BREAK_URP, then run in PowerShell:

```powershell
& 'D:/Unity_Fork/lilToon-URP-Extensions/Tools~/MaterialDiagnostics/Start-MaterialCrashDiagnostics.ps1'
```

The launcher uses the existing Unity 6000.3.15f1 installation with `-force-d3d12 -force-d3d12-debug`. It refuses to launch a second copy of the same open project and never stops the existing editor. Logs go to `BREAK_URP/Logs/MaterialCrash-<timestamp>/Editor-D3D12-debug.log`, preserving earlier logs. It does not change project graphics settings or clear caches. `-PrintOnly` prints the proposed launch without starting Unity.

Reproduce the material click. Compare the first debug-layer error before the fence timeouts, and distinguish Project browser thumbnails, Inspector header previews and the interactive preview panel. Collapsing the bottom preview alone does not isolate every material preview: Unity also renders thumbnails and header icons.

Unity reference: [MaterialEditor.cs, Unity 6000.3](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.3/Editor/Mono/Inspector/MaterialEditor.cs).

## Validation performed

- Runtime and Editor Roslyn compilation: zero errors.
- `Tests~/SubsurfaceScattering/HoSubsurfaceScatteringInputValidation.cs`, run in the isolated CharacterShadowValidation project: seven cases passed. Missing SB and each of its five individually missing inputs skip; all inputs present record Source. This records the shipping pass with real RenderGraph handles and cancels before executing the graph; it is not a GPU rendering or material-click crash regression test.
- Final validation used D3D11 because RenderGraph's default texture initialization requires a graphics device. The initial `-nographics` run exercised the same branches but logged default render-texture allocation failures; it is not the final clean graphics-device check.
- Launcher PowerShell parsing, dry run and refusal to open an already running project verified.

Local records: `.codex-research/sss-input-validation-20261019-d3d11.log` and `.codex-research/compile_check.log`. Unity also reported persistent allocations during validation-process shutdown; the test does not establish leak-free engine shutdown.
