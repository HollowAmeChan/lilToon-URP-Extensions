# Ho-RTGI secondary environment delivery, step003

2026-10-19. Production now shades secondary hits from the existing Ho-Atmosphere
HDR environment using the pinned SDK environment PDF/RIS/initial MIS path.
GI owns its private small PDF/RIS resources and binds sky inputs locally to
its own shaders. It does not require RTDI to be active.

Direct sky from the primary surface is explicitly separate: new
`includeDirectEnvironment` defaults false, while `secondaryEnvironmentSamples`
defaults4. With RTDI, leave direct sky off in GI. For standalone direct sky,
enable it explicitly; a finite GI bounce-range miss is then checked for distant
occlusion up to100000 world units. Black outcomes still count in the estimator.

## Reviewed changes

- Secondary candidates use the same stored-reservoir reconstruction as the
  verified DI path before the final visibility test. SDK output-sample identity
  is not trusted after Unity DXC candidate selection. Vendor SDK is unchanged.
- Pass settings are numeric immutable values captured at Setup. Resources,
  signatures, RG recording and compatibility dispatch use the same values.
- Active secondary environment resources add128² R32F full mips (about.083MiB)
  and16384 uint2 RIS entries (.125MiB) per camera. Disabled sampling releases the
  PDF and keeps only one dummy RIS element for bindings.
- Each active frame pre-samples RIS; the PDF rebuilds on environment revision.
  Candidate/visibility work has real GPU cost. This is not a GPU budget test.

## Actual validation

Run `Tests~/RTGI/Environment/run_validation.ps1` from the repository root with
the dedicated `.codex-research/RTGIValidation` project prepared. It uses real
Unity6000.3.15f1/D3D12, runs sequentially, stores actual child PIDs and exits in
`results/run.json`, and restores original project settings in finally. Do not
run it against the user's scene or the parallel DI validation project.

All11 final entries actually exit0 with required pass markers and no shader
error/warning or C# compilation errors:

- Linear/Gamma × secondary environment RG/compatibility:4 runs.
- Linear/Gamma × existing complete GI chain RG/compatibility:4 runs.
- Linear/Gamma × real secondary material TraceRay validation:2 runs.
- Actual D3D11 unsupported/inactive path:1 run.

Each new environment run makes138 manual render requests, including three
bounded editor warmup requests;23 checks cover:

- No analytic lights still receive secondary sky, correct RGB ratios and actual
  private PDF/RIS production; disabling sky/candidates yields valid black.
- Combined analytic and sky signals agree with their separately measured sum
  within5%, exercising stored sample reconstruction and initial MIS.
- Direct sky is excluded by default, can be explicitly included, and disappears
  when turned off without changing the shared atmosphere.
- A world-only near blocker occludes secondary sky. A world-only blocker1500
  units away also blocks direct sky beyond the1000-unit GI bounce range.
- Doubling sky energy doubles the bounce within the stated Monte Carlo tolerance;
  sky revision rebuilds the PDF, while absence/disable rejects stale bindings and
  reservoir history.
- Actual pass Snapshot retains recorded numeric settings after source mutation;
  next GPU frame/Setup consumes new values. This is not permission to destroy
  borrowed scene/texture resources before graph execution.

Four final environment reports are numerically identical. ROI sky-only bounce
is approximately `(.357808,.179135,.088113)`; analytic-only is
`(.217788,.216423,.214741)`; mixed is `(.572891,.393576,.301248)`, about.47-.53%
below the separately sampled sum and within the5% gate. Original code produced
zero for the nonemissive secondary surface with no analytic lights.

## Preserved failure and limits

The first new Gamma test assumed a valid receiver on the very first editor
request after switching project color space and failed with receiver-invalid.
The existing GI suite, with its established bounded warmup, actually passed
Gamma. The new test now follows that same three-editor-update precondition and
strictly checks every measured receiver afterward. The initial failed report and
partial run are preserved in `results/`; no signal/validity threshold was relaxed.

These runs have synchronous readback and do not establish automatic-frame object
motion, denoising quality, Player deployment or target-scene performance. Output
remains dedicated raw GI; normal-camera composition and independent NRD remain
subsequent steps. No source material, user scene, RTDI runtime or vendor algorithm
was modified by this delivery.
