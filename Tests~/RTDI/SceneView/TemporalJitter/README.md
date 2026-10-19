# Static TAA jitter and NRD history

The user's live Scene View was rendering, NRD was active and its history was
valid, but the settled state was never entered. During the20-second passive
observation,84 actual renders retained the same view, world/structure revision
and DI signature while the projection changed almost every frame. Diffuse
history fell to main1 / fast0. This was projection jitter starving the stable
profile, not a disabled NRD or repeated full-history reset.

`live-before.json` preserves the passive observation. Only `atRender:true`
samples use the actual rendering Volume stack. Polling may lack a camera stack
and then reports legacy fallbacks; do not interpret those polls as parameter
changes or extra rendered samples.

`live-after.json` contains70 actual renders after the projection repair:64 are
settled with main30/fast6, the non-jittered lens key stays constant while actual
projection/sample jitter continues. The last preview counter is58, so this live
window does not claim completion of the64-sample cap. `startup-partial.json`
records an interrupted restart observation with only3 renders and frozen game
time0; it is not a successful convergence run. The later clock hardening below
is separately covered by actual GPU tests, not retroactively attributed to this
live capture. The preserved probe includes later startup-wait refinements.

## Repair

URP starts the supported Base/Mono projection from `camera.projectionMatrix`
and applies its TAA jitter as a clip-space translation. `HoRTDIProjectionSample`
separates a verified translation within half a pixel from that real lens matrix.
Non-translation/custom changes fall back conservatively to the actual matrix.

- Keep actual jittered projections for primary surface reconstruction and DI
  previous VP/inverse VP, so the sampled world surface is not changed.
- Use the unjittered projection for lighting/camera stability checks.
- Send NRD unjittered current/previous projection matrices plus current/previous
  sample jitter in pixels, bounded by the pinned SDK contract `[-.5,.5]`.
- Real FOV/lens changes still leave the stable profile and restart once.
- Non-playing Scene Views use monotonic Editor sample time. Startup/repaints
  with frozen game time must not keep the .5-second stability timer at zero;
  ordinary Game/Player clocks retain their existing policy.

This follows the pinned4.17.3 `NRDSettings.h` CommonSettings requirements
(`non jittered` matrices and separate `cameraJitter`), checked in the local
UnityPathTracing/RenderingPlugin/External/NRD source. No vendor shader bodies,
sampling settings, Volume values or user scene assets were changed. The new
context state is per camera. Object world-motion input remains outside this fix.

## Controlled actual GPU reproduction

`RunTemporalJitter` / `RunTemporalJitterCompatibility` render actual URP TAA
projections over192 distinct Editor engine frames per case. URP's existing
diagnostic Halton seed offset aligns the sequence; no projection is fabricated.
The Game test camera uses an Editor-only Scene View policy hook with injected
.25-second NRD sample intervals to reproduce the low-rate history policy.
This is not a measured4FPS benchmark or an actual Scene View window fixture.

| Measurement | Legacy | Repaired |
| --- | ---: | ---: |
| Jittered render frames | 192 | 192 |
| Settled frames | 0 | 190 |
| Main / fast at end | 1 / 0 | 30 / 6 |
| Initial full resets | 1 | 1 |
| Preview demand completed | 0 | 64 |
| Late normalized filtered temporal variance | .05903378 | .0008484488 |

Raw statistics are identical in both cases. Filtered means are.1448661/.1436062;
the normalized variance change is not caused by a large brightness shift.
Actual sample jitter is checked against an independent inverse/projected ray
oracle, worst pixel error about6.7e-8. A real FOV40-to45 change exits the stable
profile, restarts once and settles. Both render paths pass unchanged thresholds.

Also rerun: both Volume-control paths, both slow-Scene-View paths,
`RunTimingCompatibility`, and `RunMatchedConvergenceAdaptive`; all8 process exits
are retained. No new Player build or complete moving-object/TAA qualification
is claimed.

Each GPU path additionally renders actual requested Scene View samples within
one frozen engine frame, without a clock override. Editor sample time progresses
past.5 seconds and enters long history while `Time.frameCount` and game unscaled
time remain unchanged. Reports retain the count and measured editor interval.

Entry prefix: `lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.`. Run in the
isolated `.codex-research/RTInputValidation` project with Unity6000.3.15f1,
`-batchmode -force-d3d12 -executeMethod <entry> -logFile <log>`.

`Probes/HoRTDIVolumeLiveAudit.cs` is a bounded, opt-in passive live helper. It
does not repaint, change parameters, or save assets. Temporarily import it into
`Editor/RTDI` only for authorized live diagnosis and write the scoped request
`.codex-research/rtdi-volume-live-request.json` with exact project/output and
observation seconds before reload. Remove the imported helper after observation.

```powershell
python Tests~/RTDI/SceneView/TemporalJitter/verify.py
```

The verifier checks preserved evidence/source fingerprints; it does not rerun Unity.
