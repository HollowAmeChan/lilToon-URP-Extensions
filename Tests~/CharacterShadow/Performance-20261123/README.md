# CharacterShadow performance investigation — 2026-11-23

Code baseline: `acde9c8b` (`Track active Ho features in render execution order`).
Investigation only: production CS code, linked lilToon code, scene, materials and Volume assets were not edited.

## Findings

1. **The cost includes a material-side filter, not only atlas rendering.** `HoCharacterShadowPass` records `Ho-CharacterShadow` around the atlas pass. The actual local-shadow lookup runs in lilToon's Forward attenuation via `HoCSResolveMainCast`. Consequently the HUD's CharShadow G is producer time; it does not attribute the extra Forward work to CS.
2. **The saved active-project configuration is Ultra, not Low.** In `BREAK_URP/Assets/00动画/角色/羽啾/Config/Global Volume Profile.asset`, resolution=4096 and PCSS=true override their fallbacks. The quality field stores Low but has `m_OverrideState: 0`. `Assets/Settings/PC_Renderer.asset` supplies quality=3 (Ultra), blocker search=0.11 m, angle=0.1°, maximum penumbra=0.076 m. The Volume overrides fixed softness to 0.002 m. These are saved asset observations, not a snapshot of unsaved Inspector state or every contributing local Volume.
3. **Ultra executes up to 32 blocker quads plus 64 filter quads per receiving fragment.** The shader has receiver identity/part/bounds rejection, but accepted fragments run blocker search followed by filtering even when no blocker was found. PCSS Off removes blocker search only; fixed-radius/base-AA filtering remains. A world-space radius converts to more atlas texels as resolution increases. The 2–2.5 texel transition can select the wider sparse filter, so resolution affects receiver work as well as atlas storage.
4. **The common linked lilToon Forward targets 3.5, disabling CS's Gather branch.** `lts.shader` and `lts_o.shader` declare target 3.5; the current CS header requires target >=4.5 and platform Gather support. The fallback issues four scalar depth reads per quad: 384 raw depth reads in the regular 32+64 wide-kernel path. The 2–2.5 texel transition additionally evaluates the dense kernel; 384 is not a universal upper bound. `Chu2u_M_Cloth2.mat` references `lts.shader`; `Chu2u_M_Body.mat` references `lts_o.shader`. This is verified source/asset routing, not disassembly of a captured live material variant. Some hidden pass shaders have a 4.5 SubShader, so the statement must not be applied to all variants. The controlled 3.5/4.5 comparison below shows only a modest difference; a four-read path is not proof of a fourfold frame-time penalty.
5. **The producer performs additional scene work.** Each visible receiving domain causes a separate light-space cull, shadow caster cull, and native shadow renderer list. Casters include environment geometry in the upstream light-space column, not only the character. A broad Renderer/Terrain bounds scan also runs per camera. Original material ShadowCaster passes are retained; no accidental full Forward pass was found in the atlas path.
6. **Camera multiplicity and atlas transitions remain risks.** Scene and Game cameras both render CS, and Game overlay cameras are not filtered. The feature has one persistent atlas handle; differing camera resolutions/slice counts may reallocate it. Single 2K/4K slices really allocate 2K/4K D32 (16/64 MiB), not the configured maximum atlas size. A second 4K slice increases the square atlas to 8K D32 (256 MiB), including unused tiles in its clear. Neither extra slices nor reallocations have been confirmed in the user's live frame.

## GPU experiments

Unity 6000.3.15f1, D3D12, NVIDIA RTX 4060 8 GB. Separate project `Temp~/DebugTileCompile7`; no user scene was loaded or saved. Real production `HoCharacterShadowRendererFeature`, one shadow-only cube, one two-triangle receiving quad, one directional light, 2560×1440 target. Receiving quad spans about 56% of the target. Each case warms for 18 editor iterations and takes 32 valid GPU Recorder observations; GPU data has its normal delivery delay. The fixture's sample settings match the saved search/angle/softness values above. The report saves the producer's cull status to verify one slice, one renderer list, and no hidden-light leak into the camera (`camLights=1,add=0`).

2K atlas medians, milliseconds (`receiver-target-comparison.json`):

| Receiver shader | Configuration | Atlas GPU | DrawOpaqueObjects GPU |
| --- | --- | ---: | ---: |
| target 3.5 | CS off, receiver bypass | 0 (not scheduled) | 0.016 |
| target 3.5 | Atlas generated, receiver bypass | 0.031 | 0.617 |
| target 3.5 | PCSS off, fixed softness 0 | 0.031 | 0.743 |
| target 3.5 | PCSS Low | 0.030 | 2.501 |
| target 3.5 | PCSS Ultra | 0.031 | 3.534 |
| target 4.5 | PCSS Ultra | 0.031 | 3.384 |

This establishes a significant receiver-side cost while producer time is essentially unchanged. It does **not** establish the complete cost or exact FPS loss of the user's skinned character. The fixture has no lilToon material stack, hair overdraw, complex shadow casters, or other Ho features. CPU render-call medians include culling, pipeline invocation, and possible driver stalls; they are not CPU work exclusive to the feature.

`sampling.json` separately measures the production sampling functions over a synthetic RFloat atlas, with dedicated point/baseline shader variants. At 2K atlas and 1280×720: point lookup ~0.016 ms, PCSS Low ~0.35 ms, PCSS Ultra ~1.34 ms. This is a full-screen kernel microbenchmark, not a normal character render or a D32 atlas-generation benchmark.

### Excluded evidence / uncertainty

- Concurrent interactive applications were using the GPU. During investigation `nvidia-smi` reported ~7300–7500 MiB occupied out of 8188 MiB and GPU utilization of 84–96%. No unrelated process was stopped or reconfigured.
- 4K experiments showed large, inconsistent producer/receiver spikes, including slow minimal producer cases. They are unsuitable for attributing a normal 4K scaling factor to CS; GPU scheduling and memory pressure remain confounders. They are not used in the table or treated as proof that a simple 4K map intrinsically takes 10–16 ms.
- No current live-scene RenderDoc capture or before/after whole-frame GPU timeline was acquired. Cached old captures were not used to diagnose the current frame.
- VSync is 0 in the saved PC QualitySettings. Runtime overrides, Game View composition and other bottlenecks have not been excluded, so the exact halving of FPS is not yet a quantified feature-exclusive cost.

## Recommended optimization order

1. Correct the desktop receiving path's capability/variant selection, and measure real lilToon variants. Preserve an explicit lower-capability fallback; do not globally remove compatibility just to enable Gather.
2. Remove repeated sample-pattern arithmetic (sqrt/sin/cos per tap), and design adaptive sampling with image validation. Do not blindly return lit after blocker search: fixed softness and receiver-plane correction still need correct coverage.
3. Provide a cheap deliberate hard-shadow/base-AA mode and a realistic default budget. Preserve requested world-space softness instead of silently shrinking it to make a quality tier faster.
4. Instrument producer cull/build/atlas and the receiving Forward phase separately. Report inclusive/dependent costs honestly; summing a producer marker cannot measure the cost injected into material shading.
5. Address per-camera resource reuse, optional update policies, and caster culling after measuring actual camera/slice/draw counts. Preserve existing off-screen upstream casters, receiver selections, and ordinary CSM isolation tests.

## Reproduction

The sibling C# and shader sources are diagnostic fixtures, intended for an isolated Unity project with this package and URP installed. Place them in `Assets/Editor`. `HoCSCameraPerformanceProbe.Run` performs the 2K target-3.5/4.5 test; `HoCSPerformanceProbe.Run` performs the kernel sweep. Launch with `-batchmode -force-d3d12 -executeMethod <entry> -hoCsPerf`, **without** `-nographics` or `-quit`. Any auto-exit compile helper must ignore `-hoCsPerf`. Current paths in these fixtures intentionally refer to this workspace's `Temp~` evidence directory. The isolated project also has an unrelated pre-existing `IHoPostProcessingProvider` compatibility stub; it is not a CS performance change.
