# Ho-RTGI normal-camera composition, step004

2026-10-19. GI can now add its published diffuse contribution to normal camera
color before transparents. Feature defaults remain opt-in (`composite=false`,
`intensity=.25`); Ho-RTGI Volume supplies per-camera overrides. Unchecked values
fall back to the Feature without mutating it. Intensity affects appearance only,
not GI samples or reservoir history.

The shader consumes linear HDR GI with receiver BRDF/albedo already applied.
It does not multiply albedo or add primary emission again. Gamma source color
is decoded before addition and encoded afterward. Fog attenuates only the added
light; existing fog color/alpha remain with the source. Validity and actual opaque
camera depth prevent unrelated foreground/background receivers from inheriting
hidden GI. Composition requires a full viewport, Mono and single-sample input.

## Actual validation

Run `Tests~/RTGI/Composite/run_validation.ps1` against the dedicated
`.codex-research/RTGIValidation` project. The runner switches only that isolated
project's color setting, restores its original bytes in finally, and persists
actual PIDs, exit codes, report freshness and hashes in `results/run.json`.

All6 final processes actually exited0 with pass markers and zero shader/C# error
diagnostics. Four new groups are Linear/Gamma × RG/compatibility; each makes62
manual render requests and passes33 checks. The other two run the existing GI
chain and uncomposited secondary environment regression.

Checks include:

- Actual camera color equals captured source plus the same-frame GI contribution;
  gain1/.25/0, no duplicate albedo/emission, and history retained on gain changes.
- Volume enable/disable/fallback, no writes to shared defaults, and two actual
  cameras with separate Volume masks/gains (.25/.8) and independent GI contexts.
- Added-light fog transmission, full fog, background RGB/alpha, mismatched GB/SB
  domain, forward-only opaque depth and transparent rendering afterward.
- Partial viewport bypass; DI additive composition followed by GI consuming the
  current camera color, not an earlier source copy.
- A real offscreen red wall, secondary sky enabled and primary direct sky off:
  raw GI is colored and the normal camera measurably brightens by the expected
  same-frame contribution. `bounce-before/added` captures illustrate this path.

The first RG smoke run passed before new multicamera checks were added. A later
matrix attempt failed because the parallel DI work changed DI production to
require explicit Volume enable; the companion had no context. That fixture
failure/report is retained. The test now explicitly enables its DI Volume, and
the legacy joint-GI fixture does likewise. DI's production code/policy is not
modified by this step; a missing companion context now reports its real status.

## Scope and artifacts

`images/` contains actual normal-camera PNG captures, including the secondary
red-wall before/after pair. Raw noise is still visible: GI-specific NRD is a
subsequent delivery. Full EXRs remain in the isolated project's output folders.

These are synchronous diagnostic render requests with explicit Volume updates,
not real automatic-frame motion, backbuffer Player deployment or a GPU timing
benchmark. Transparent blending is tested as its existing raster overlay; this
does not add transparent GI receiving/tracing. Existing scene lighting remains
part of the source; the additive route does not automatically remove baked/SSGI
contributions or redefine the NPR base.
