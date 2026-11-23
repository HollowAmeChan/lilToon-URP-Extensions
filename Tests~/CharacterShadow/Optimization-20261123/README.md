# CharacterShadow optimization — 2026-11-23

Baseline: `acde9c8be10747a7a51e1dde289eee166368893d`. This directory records the following working-tree optimization, separately from the preceding [performance investigation](../Performance-20261123/README.md). No user scene, material, Volume asset, or linked lilToon source was edited.

## Changes and preserved behavior

- Reuse CPU frame/slice/array/list storage separately for each camera. Rebuild receiver selection, masks, validity, and dynamic upstream caster bounds every render. Skip the scene-wide caster scan when there is no eligible receiver. Discard destroyed/inactive camera entries on subsequent frame builds; release all cached frames on pass disposal.
- Precompute the original antipodal sample offsets once per sample budget, then bind a fixed-size table. Keep the arithmetic fallback for diagnostic callers. Process adjacent antipodal taps together in the same accumulation order.
- Share interior depth-quad coordinate/reference math on target 3.5 as well as Gather-capable variants. Keep per-texel clamping at tile boundaries. Skip the plane-trust calculation only when all corrected/raw depth comparisons already match.
- Use an algebraically equivalent tent integral and skip rows/columns with zero weight. Remove unused reflection-probe culling from the local depth-only cull. Declare the atlas depth attachment as completely written because the pass clears the whole atlas.

Sample budgets, world-space softness, PCSS behavior, receiver-part selection, upstream environment casters, and ordinary CSM isolation are retained. Target 3.5 still uses four scalar depth reads per quad; this change does not raise linked shader targets or add a per-camera GPU atlas.

## Why PCSS Off can still be expensive

PCSS Off disables blocker search, while the final PCF/base-AA filter remains. Ultra can still use 64 filter quads per accepted receiving fragment. The final radius is the larger of artistic softness in atlas texels and the projected screen-pixel footprint. Crossing 2–2.5 texels transitions from the dense tent to the sparse filter; the transition evaluates both kernels.

In this specific 1280×720 fixture, the UV span is 0.9: the footprint is about 1.28 texels for 2K and 2.56 for 4K. Thus even zero artistic softness selects the full sparse filter at 4K. This is a source/fixture explanation, not proof of the live scene's exact bottleneck. The optimization reduces work within the existing filter; it does not remove that budget transition.

The HUD's `Ho-CharacterShadow` GPU marker covers atlas production. Material-side receiver filtering remains part of Forward and is not included in that producer number.

## Correctness validation

Unity 6000.3.15f1, NVIDIA RTX 4060, isolated project `Temp~/DebugTileCompile7`, graphics-enabled batch editor.

| Check | D3D11 | D3D12 |
| --- | --- | --- |
| Runtime + Editor compilation | Pass | Pass |
| Projection/contract; per-camera frame reuse and resets | Pass | Pass |
| Real lilToon receiver; receiver parts and empty selection | Pass | Pass |
| Dynamic off-screen upstream casters; perspective/distant receivers | Pass | Pass |
| 1/4-cascade distance sweep and ordinary scene-shadow isolation | Pass | Pass |
| PCSS/fixed PCF; real Volume PCSS-off override and fallback restoration | Pass | Pass |
| Existing filtering suite, including slopes, occluders, tile isolation, motion and 4K settings | Pass | Pass |
| Original/current image comparison, 324 cases | Pass | Pass |
| Additional original/current boundary/budget comparison, 1280 cases | Not run | Pass |

The 324-case comparisons had maximum absolute visibility error `2.71201134e-05` on both APIs. The 1280 additional comparisons had maximum error `4.41074371e-05`, below the predefined `0.0004` threshold. They cover 4096/6144 atlases; Gather and forced scalar fallback; cached offsets and arithmetic fallback; filter/blocker budgets (1,1), (2,2), (3,3), (7,7), (16,8), (31,31), (63,32), (64,32); tile boundaries, steep planes and thin occluders; direct filtering and the full PCSS-off/on entry point. PCSS state is balanced over the two tested radii, rather than every case being a complete Cartesian product.

CPU storage identity, repeated rebuilding, active→empty→active reset, independent camera state, destroyed-camera cleanup, and animated caster depth refresh all passed. Unity Mono returned **zero even for a known 64 KiB allocation probe**, so the managed-byte reduction assertion was explicitly skipped. No measured byte-saving claim is made; allocation-counter support is tested before asserting a reduction.

`validation-summary-*.txt`, `image-equivalence-d3d11.txt`, and `edge-image-equivalence-d3d12.txt` contain the retained results. The batch editor also reported four Persistent allocations at shutdown; the edge run reported a temporary allocator warning. These are recorded rather than treating suite success as a leak-free certification. Full raw logs remain in the workspace's `Temp~/CharacterShadowOptimization-20261123` directory.

## GPU A/B results and limits

The benchmark compiles the untouched baseline and current implementation as variants of the same target-3.5 probe. Each configuration uses ABBA order, 12 warmup iterations and 32 valid GPU Recorder observations per variant, eight blits per scope, divided by eight. All cases have PCSS disabled and a 64-filter budget. The following numbers are means of the two per-variant medians, in milliseconds per 1280×720 draw:

| 2K flat-receiver case | Run 1 baseline → current | Run 2 baseline → current |
| --- | --- | --- |
| Softness 0 | 0.1882 → 0.1764 (6.2%) | 0.2002 → 0.1915 (4.3%) |
| Softness 0.002 m | 1.8465 → 1.4285 (22.6%) | 1.7048 → 1.3363 (21.6%) |
| Softness 0.005 m | 1.5668 → 1.1266 (28.1%) | 1.4651 → 1.0312 (29.6%) |

These are **filter microbenchmarks, not whole-frame speedups**. They use a synthetic RFloat atlas and repeated fullscreen blits, without skinning, the real lilToon material stack, hair overdraw, complex casters, or competing Ho features. Flat receivers favor the corrected/raw-equality fast path. Added sloped receivers use gradient `(0.2,0.1)` and depth `0.3 + dot(gradient, uv - 0.5)` to exercise comparison disagreement near the atlas's 0.3 depth layer.

The sloped and 4K results varied substantially between ABBA observations/runs, including both gains and apparent regressions. They do not establish a reliable speedup or a confirmed regression. Both complete raw runs are retained in `sampling-final-run1.json` and `sampling-final-run2.json`; no stable 4K percentage is claimed. At one observation outside the benchmark, `nvidia-smi` reported 7462/8188 MiB used. Other interactive applications were not stopped. Actual scene frame-time improvement remains unmeasured.

Remaining costs include scene-wide Renderer discovery/returned arrays, per-slice native culling, all eligible caster draws, multiple rendering cameras, and full square-atlas allocation/clearing. The frame cache is storage reuse, not zero allocation or cached shadows. A larger gain for large-footprint PCSS-off cases requires a separately validated filtering-budget/algorithm change or live-frame attribution.

## Reproduction and provenance

The sibling fixtures are intended for an isolated project with this package, URP, and lilToon available. Copy the `*.cs` and `*.shader` files to `Assets/Editor`. Copy `baseline.hlsl` to the isolated package as `Runtime/CharacterShadow/Shaders/HoCharacterShadowSamplingBaseline.hlsl`; its original SHA256 is `6fbd342049144eb772a0870dcdaef924caaa800042835a0d97da779be2bf23b3`. The baseline is not a production include.

Use Unity with `-batchmode -force-d3d12 -job-worker-count 2 -projectPath <isolated-project> -executeMethod <method> -hoCsPerf -logFile <log>`, without `-quit` or `-nographics`. Entry points are `HoCSOptimizationValidation.Run`, `HoCSOptimizationEdgeValidation.Run`, and `HoCSOptimizedPerformanceProbe.Run`. Use `-force-d3d11` for the D3D11 suite. Fixtures intentionally use this workspace's output path. The isolated stock-URP project also contains a pre-existing `IHoPostProcessingProvider` compatibility stub; it is not a production CS change.

`manifest.json` records source and retained evidence hashes. The source baseline is Git `acde9c8b`; test results refer to the source hashes, not to an unrecorded final commit.
