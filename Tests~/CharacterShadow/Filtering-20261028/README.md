# CharacterShadow filtering verification — 2026-10-28

Status: preserved measurements for the source checkpoint `541d06f2`, not a new GPU run during documentation cleanup. Unity 6000.3.15f1, independent validation project, Direct3D11 and Direct3D12 RenderGraph. No desktop UI was operated for these accepted filtering runs or this cleanup.

## Evidence

- `sources.json` contains the exact four tracked sampler/probe/validation SHA-256 values, all rechecked before commit.
- `d3d11-metrics.txt` and `d3d12-metrics.txt` are copied from the final accepted expanded filtering runs.
- The preceding complete suite logs contain PASS for filtering, PCSS, material reception, distance/cascade scans and unrelated scene shadows on both APIs. The subsequent expanded filtering runs on both APIs also contain PASS.
- Runtime/Editor split compilation was repeated before the checkpoint with `ERRORS: 0`; existing warnings in unrelated GI/SSGI/PLR validation fields remain.

Original full logs remain in the ignored workspace: `.codex-research/character-shadow-quality-verified-d3d11.log`, `-d3d12.log`, `character-shadow-quality-coverage-d3d11.log`, `-d3d12.log`, and `character-shadow-quality-accepted-compile.log`. The full logs include existing isolated-project import/cleanup warnings, including a four-allocation persistent cleanup warning; a process exit code alone was not used as a shader or image-quality pass. These log paths and character image fixtures are not shipped as a portable project.

## Checked results

| Check | Result |
| --- | --- |
| Dense paired reads vs independent individual-cell tent integration | Maximum visibility difference ≤0.000104 |
| Softness over 0–4 texel radii, eight edge angles | Checked monotonic edge widths |
| Blocker-depth step, 1/128-texel motion | Maximum normalized gap change 0.00004785; previous point control 0.00153126 |
| First thin blocker, low-support region | Maximum radius change 0.02772593 texels; 4096 pixels with nonzero coverage below a quarter tap |
| Gather / non-Gather blocker statistics | Difference 0.0 in the fixture |
| Existing slope, foreign occluder and tile checks | Passed, including PCSS and the 4096 user-parameter fixture |
| End-to-end PCSS | PCF width 12px, PCSS 22px, zero-angle 12px, 4096 tile 7px; checked core brightness / speckles |

The edge-width helper takes the median of complete bilinear normal-direction 10–90% profiles. The earlier clipped-corner 28px result remains a rejected measurement; the original width and widening thresholds were not loosened.

## Reproduction entry points

The package exposes `lilToon.URP.Extensions.Editor.CharacterShadow.HoCharacterShadowValidation.ValidateFiltering`. Import the package and its matching lilToon / URP dependencies in an independent Unity project, then use batch mode with `-force-d3d11` or `-force-d3d12`. The probe constructs synthetic GPU fixtures; it does not need the user's character assets.

The complete suite additionally exercises `ValidatePcss`, `ValidateRendering`, `ValidateDistanceRendering` and `ValidateSceneShadows`. `ValidateReceiverSelectionRendering` checks whole-group, selected parts and an empty explicit selection. Do not create fixtures in the user's open project.

## Limits

The approximately 32× reduction above concerns a local blocker-search statistic, not image quality or speed. No GPU timing benchmark was performed. Actual character copies still show residual streaks and some face structure; hard toon thresholds, large sparse kernels, local receiver-plane approximation, caster bias and filter borders remain separate limits. The material diagnosis used clones; no source character material was changed.

This verifies the current raster sampler. It does not implement or validate the proposed Ho-RT host, RTShadow or NRD SIGMA pipeline.
