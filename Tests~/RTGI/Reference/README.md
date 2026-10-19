# RTGI reference and Ho diagnostic baseline

Step 001, 2026-10-19. This is a **diagnostic baseline**, not a complete GI quality,
unbiased-energy, performance, automatic-frame motion or Player acceptance suite.
No production GI algorithm is changed by this step.

## What actually ran

Unity 6000.3.15f1, D3D12, RTX 4060, Linear, 128x128. Camera `(0,0,-3)`, FOV40,
near/far `.1/50`; white quad at the origin, size2x2; offscreen red quad at
`(1.25,0,-1)`, Y rotation90, size2x3, color `(.8,.1,.05)`; white point light at
`(.25,0,-1)`, intensity4, range100. No sky, emission, camera jitter or extra
secondary DI reservoir reuse. Initial secondary local candidate count1.

Reference runs first make 8 direct-only render requests, then enable GI for 256.
Ho publishes dedicated GI and makes 256 requests. Captures use synchronous
readback and do not imply a GPU timing result. Statistics are per-pixel,
per-channel temporal variance over the central32x32 ROI, averaged across those
components; spatial variation is not counted as temporal noise.

| Actual entry | Exit/report | Last window (GI129-256) raw temporal variance | Reconstructed combined temporal variance |
| --- | --- | ---: | ---: |
| Reference Unity no reuse | 0 / true | .01071720498 | n/a |
| Reference Unity ReSTIR GI raw | 0 / true | .00059019024 | n/a |
| Reference Unity ReSTIR GI + RR | 0 / true | .00059019024 | .00000752505 |
| Reference Native GI + RELAX | 0 / true | .00163685801 | .00000643314 |
| Ho no reuse | 0 / true | .00993114833 | not implemented |
| Ho RenderGraph reuse | 0 / true | .00001676143 | not implemented |
| Ho compatibility reuse | 0 / true | .00001676143 | not implemented |

These establish live GI and reconstruction paths and show that Ho's existing
ReSTIR path already reduces temporal fluctuations in this fixture. Do not rank
implementations by these numbers: their BRDFs, age policies, PDF distributions,
quantization, input/output meanings and denoiser histories differ. Low temporal
variance alone does not prove low image error or unbiased energy.

## Important review findings

- Unity's existing final GI shader adds diffuse and specular into the combined
  DirectLighting texture. Its measured raw GI is that texture minus a proven
  deterministic direct-only baseline. Small channels remain subject to FP16
  cancellation/rounding. RR measurements are actual reconstructed **DI+GI**.
- Setting reference `_IOR=1` does **not** make its secondary F0 zero. Its actual
  conversion supplies dielectric F0 about.04. Actual secondary packets contain
  `specularRoughness=0xFF3B3B3B`; Ho's explicitly zero-reflectance fixture contains
  `0xFF000000`. Reference primary specular GI also remains in its output. Hence
  this step is not an exact diffuse-only energy match.
- Unity's local `Pack_R11G11B10_UFLOAT` is an integer normalized packing, not an
  assumption that can be decoded using a standard GPU R11G11B10 float format.
  Actual Unity secondary diffuse packet `0x0100C4D4` and Ho UNORM8 `0xFF01039A`
  decode to similar main colors under their respective local helpers. Native's
  packet uses its own source/encoding; do not compare those integers directly.
- Native DiffuseLighting is a denoiser input, and its direct-only frames did not
  pass the deterministic-subtraction guard. The first attempt stopped with exit1
  after8 requests. Preserve [the rejected report](results/native-relax-rejected-baseline-subtraction.json).
  The accepted Native probe enables the already-existing
  `debug.outputDebugIndirectLighting` constant after host setup, captures the
  actual IndirectLightingRaw texture, and reads reconstructed HdrColor. It does
  not replace GI sampling shaders. The debug signal is demodulated diffuse plus
  specular; it is not Ho's isolated diffuse.
- The Ho secondary packet has a different48-byte field order from the reference.
  An intermediate metadata reader used the wrong order; review corrected it and
  added the actual hit-status guard. All accepted Ho reports were rerun with the
  corrected reader and matching source hash. Rendered GI was not changed.
- Canonical logs contain no shader/C# compilation errors or shader warnings.
  Reference shutdown still reports referenced-resource/persistent-allocation
  diagnostics; licensing bootstrap messages were followed by actual successful
  rendering. This step does not declare the reference lifecycle clean.

## Reproduce and inspect evidence

Use the existing repaired reference copy at
`.codex-research/UnityPathTracing-6000.3.15`. Prerequisites and NRI/native repairs
are documented in [the DI reference recipe](../../RTDI/Reference/README.md).
Do not change system PATH or patch system D3D12/Unity binaries.

From the repository root, run `Tests~/RTGI/Reference/run_reference.ps1`. It refuses
a non-isolated project or an already-running instance, copies the new probe into
Assets/Editor, runs one process per entry in order, checks real exit codes and
fresh reports, and records probe/report hashes in [run.json](results/run.json).
The launcher adds only child-process DLL search paths and restores its own PATH.

Ho entries are public methods on
`lilToon.URP.Extensions.Editor.RTGI.HoRTGIMatchedReferenceValidation`:
`RunNoReuse`, `Run`, `RunCompatibility`. Run only in the dedicated
`.codex-research/RTGIValidation` project with `-batchmode -force-d3d12`.
That project uses the local HoURP, lilToon and Extensions package paths, Linear
color space and `URP_COMPATIBILITY_MODE`; unrelated GLTF package dependencies
were omitted. It does not occupy the parallel DI RTInputValidation project.
[ho-run.json](results/ho-run.json) records accepted source/report hashes, actual
process exits and freshness. Accepted reports live in `results/`.

Full EXR/PNG frame captures remain in each isolated project's corresponding
`ho-reference-gi-v01-*` or `ho-gi-v01-matched-*` output directory. The selected
PNG snapshots in `images/` illustrate signal differences; linear PNGs are dark
and are not tonemapped presentation renders. In the Native run,
`combined-raw-*` is retained only for tool compatibility and contains its
indirect debug texture; see that report's protocol field.

Next: use the actual material/output findings to finish a controlled diffuse
energy comparison and choose the missing production work. Do not treat seven
diagnostic passes as the final RTGI product delivery.
