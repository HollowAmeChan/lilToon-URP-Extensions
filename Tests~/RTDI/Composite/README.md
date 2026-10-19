# Normal-camera RTDI composition

This folder preserves the original replacement-mode qualification at `da52a0f`.
The current Inspector calls the switch **合成到画面** and also offers additive
blending; current formulas, captures and qualification are in [Blend](Blend/README.md).
The original reports below are not retroactively treated as additive tests.

The production Feature now has an optional **合成到画面（参考 PBR）** setting.
It defaults to false. With weight 1, valid opaque receivers get published
diffuse + specular + SB emission in the normal camera target. Old receiver
lighting is replaced, not added a second time. Debug remains Off.

The producer and composite run, in that order, at
`BeforeRenderingTransparents`. Transparent rendering and postprocessing then use
the new camera colour. Disabled composition keeps the original producer timing.
The material response is the existing Lambert/GGX reference; this is not a new
NPR ramp/SSS/MatCap or RTGI integration.

## Editor reproduction

Use Unity 6000.3.15f1, Windows/D3D12 and the isolated
`.codex-research/RTInputValidation` project. Run one process per project at a
time with `-batchmode -force-d3d12 -projectPath <project> -executeMethod <entry>
-logFile <log>`; do not use `-nographics`. Prefix entries with
`lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.`:

| Entry | Purpose |
| --- | --- |
| `ConfigureCompositeGamma` | Set the isolated project to Gamma, then exit |
| `ConfigureCompositeLinear` | Set the isolated project to Linear, then exit |
| `RunComposite` | RenderGraph camera output and scene captures |
| `RunCompositeCompatibility` | Compatibility camera output and scene captures |

Configure a colour space in its own process, then run each render path.
Each test creates a temporary scene and renderer without saving a replacement
user scene or pipeline. The default target is 64², single-sample HDR. The 18
checks include independent directional Lambert energy, colour and metal
responses, explicit replacement/crossfade, zero-illumination shadows, HDR
emission, background, GB/SB domain rejection, foreground Unlit depth protection,
transparent composition order, camera fog, filtered publication and disable /
reenable lifecycle. They compare the actual final camera target, not only DI
debug textures.

Reports appear in the isolated project as `rtdi-composite-{rg,compat}.json`.
Preserve each colour/path combination separately, as in `results/`.
`rtdi-composite-images/` contains real camera captures at 384×256. The tracked
`images/before.png` and `images/after.png` use the same scene and the same display
mapping (Reinhard followed by sRGB); debug is Off. This mapping is only for
displaying the HDR captures, not an extra production composite operation.

## Actual Mono Player

Prefix these build entries with
`lilToon.URP.Extensions.Editor.RTDI.HoRTDIPlayerValidation.`:

1. Run `Prepare`, then allow the copied runtime templates to import.
2. Run `BuildComposite` or `BuildCompositeCompatibility`. They create separate
   Win64 Mono release output directories `PlayerComposite` and
   `PlayerCompositeCompatibility`, with composition enabled in the serialized
   renderer and the composite shader explicitly retained.
3. Launch that EXE in a small **visible window** with `-force-d3d12
   -screen-width 128 -screen-height 128 -screen-fullscreen 0 --composite
   --result <json> -logFile <log>`. Add `--compat` for the compatibility build.
   Do not use a hidden window or `-batchmode` for the automatic-camera test.

The probe checks disabled behaviour, actual camera DI, crossfade, world shadow,
HDR emission, filtered output and Volume lifecycle. It also uses an enabled
Mono camera for 96 application frames, with no `SubmitRenderRequest` inside
that observation loop. A fresh DI capture and advancing application frame are
required for each observation. The startup camera used for synchronous request
checks is kept separate from the automatic camera.

Runtime-only enabling of emission cannot recover a material feature already
stripped during a release build. The composite build fixture therefore saves a
material with emission enabled; the probe disables it for non-emission checks.
The initial failing emission fixture report is retained separately and is not a
passing product result. The hidden host advanced application updates without
entering the automatic rendering context; its failed observation is also retained
in `player-hidden-host-failure.json`. The visible-window qualification genuinely
observed 96 automatic captures per path, without manual requests in that loop.

For the unsupported path, launch the same build with `-batchmode -force-d3d11
--unsupported --result <json>` (and `--compat` for the compatibility build).
Require actual exit 0 and a fresh passing report; missing output is a failure.

## Supporting fix and remaining limits

The numeric SB compatibility pass now calls `ConfigureTarget` for its owned
MRTs. Manual `SetRenderTarget` alone left URP's attachment cache describing the
camera target; enabling an intermediate camera colour could then let subsequent
opaque draws overwrite SB. The composite also reads compatibility camera depth
through URP's actual shader global, rather than treating its name as a temporary
render-target identifier. Both fixes are exercised by the camera tests.

This validates a controlled Mono, single-sample, full-viewport 2D integration.
It does not certify arbitrary NPR materials, coplanar identity association,
object motion/TAA/XR/IL2CPP or production GPU performance. The prior strict
caster-step and mixed-environment specular failures are still failures; a
working composite does not remove them.

The broad `HoRTDIValidation.Run` / `RunCompatibility` suites additionally fail
the existing `Power proposal did not follow the new bright light` assertion
(the post-identity-swap variance must be below .01). A bounded comparison using
an archived `f537c26` package in the same isolated project fails that exact gate
on both paths too. Uniform mean/variance (1.003284/55.813219) and initial Power
mean/variance (.999520/0 at printed precision) match the current run. Thresholds
and production sampling were not changed to make this pass. Original exits and
log hashes are retained in the regression reports and manifest; these broad
suites remain failed. The focused real-NRD compatibility suite passes.

`verify_composite.py` checks the retained reports and hashes in `evidence.json`.
It verifies evidence integrity and declared acceptance, and does not rerun Unity.
Historical convergence evidence stays bound to its original source:

```powershell
python Tests~/RTDI/Composite/verify_composite.py --source-commit da52a0f
python Tests~/RTDI/Reference/verify_convergence.py --source-commit f537c26
python Tests~/RTDI/generate_nrd_variance_adapter.py --check
```

The composite capture predates the Scene View history fix. Its original source
hashes remain bound to `da52a0f`; the new argument checks that historical source.
Without it, the verifier continues to require exact current-file hashes.
