# Volume UI validation — 2026-11-23

Source baseline: `e96003e2`. Unity 6000.3.15f1, isolated project `Temp~/DebugTileCompile7`. No user scene, production Volume Profile, script GUID, serialized type or parameter value was migrated.

## Results

- Runtime and Editor compilation passed.
- All 18 component menu entries use the canonical `Ho PascalCase` name at the Add Override root. Actual `VolumeComponentEditor.GetDisplayTitle()` matches each menu entry; names are unique and do not end in `Volume`.
- All 18 custom Volume editors use the scoped presentation helper. Static checks found no Info/None HelpBox calls in their class bodies. Renderer-feature and scene-component editors sharing a source file are deliberately excluded.
- 79 declared parameter tooltips survived a custom label and were merged with a mode-specific tooltip.
- Fresh instances of all 18 components exactly match their pre-change serialized defaults.
- A Profile saved before the UI changes reloaded with all 18 component values and mixed Override flags unchanged.
- Nested layout checks passed: four normal/warning/star Glow row budgets, and three valid/invalid/disabled mask row budgets. Removing ordinary help rows does not reserve their former space; actionable warnings retain their own row budget.
- Seven graphics-enabled preview pages completed without a GUI or script exception: names, GTAO, CharacterShadow, DebugHUD, PlanarReflection, ImageProcess and SSGI. The retained PNGs were inspected for layout and ordinary help-box removal.

The preview host draws the real component `OnInspectorGUI()` methods using their initialized Unity editors, with a simple fixture title/toolbar. It bypasses the stock Profile inspector's render-pipeline visibility filtering because this standalone project's default pipeline is not URP. Thus these images demonstrate the component layouts; they are not screenshots of a live production Profile or an Add Override popup. The menu/header contract is tested separately by reflection. The images were captured at 720 px width; they do not certify every inspector width, light theme, localization, or effect-layer combination. Hovering the title during capture produced the ordinary tooltip visible in the PNGs.

Both the pre-change baseline batch and the final batch reported four Persistent allocations during Unity shutdown. Those logs are retained as a known test-host diagnostic, not treated as proof of leak-free behavior. Full logs remain at `Temp~/VolumeUI-*.log`.

## Reproduction

Use an isolated project; the fixtures intentionally create the disposable `Assets/HoVolumeUICompatibility.asset` and write to this workspace's `Temp~/VolumeUI-20261123` directory. Copy the fixture C# files to `Assets/Editor`.

1. With the baseline package, run `HoVolumeUIBaseline.Run` using a batch editor to save default JSON and the mixed-override Profile.
2. Update the package to the current source, then run `HoVolumeUICompatibility.Run`. It invokes the permanent `HoVolumeUIValidation.Validate` check and the nested row-budget checks before comparing the saved data.
3. Run `HoVolumeUIPreview.Run` in a graphics-enabled editor to render the preview pages. The fixture captures them with Unity's screen-pixel API, restores prior foldout expansion states, closes itself, and exits that isolated editor. If needed, its menu command is `HoLil/Validation/Open Volume UI Preview`.

Batch invocation: `Unity.exe -batchmode -nographics -job-worker-count 2 -projectPath <isolated-project> -executeMethod HoVolumeUICompatibility.Run -hoCsPerf -logFile <log>`. The last custom switch disables this test project's unrelated auto-exit compile helper. For preview, omit `-batchmode` and `-nographics`; keep `-hoCsPerf`.

`source-manifest.json` records hashes of the final working-tree source bytes matched against the isolated package. Git may normalize line endings when storing/checking out those files. `baseline-defaults-and-overrides.json` and `validation-summary.txt` retain the actual comparison input and result. The UI conventions and future-component checklist are documented in `Documentation~/Ho-Volume-UI.md`.
