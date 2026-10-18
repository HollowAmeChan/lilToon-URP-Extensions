# Scene View fast-history repair

The previous repair retained a 30-sample main history but left the responsive
history time-based. At low sample rates its budget became zero. In RELAX Temporal
this means responsive alpha is one: the responsive colour is the current noisy
sample. HistoryClamping builds its 5x5 colour box from that signal and repeatedly
clips the longer history. The user's carpet showed moving coarse noise and a
large loss of brightness even after main-history resets had been fixed.

Settled, non-playing Scene Views now retain at least 30 main and 6 fast samples
for both lobes. Input changes leave this profile and retain the existing restart
and responsiveness policy. Game/Player budgets, raw sampling, reservoir settings,
NRD shader bodies, clamping and anti-lag settings are unchanged. No output is
frozen. The Inspector exposes both main and fast budgets.

## Live result

`live-pair.json` is the final versioned-probe measurement in the user's live
BREAK_URP castle Scene View, 1391x933. Both phases use the same central64x64 pixels,
96 actual rendered warmup frames, then96 observations. `legacyFastZero` reproduces
the missing fast-history floor with a context-only override; `production` uses
the repaired production settings. Both hold main30; the observed diffuse fast
budget is0/6. All96 observation frames are stable, with zero resets in both
phases. The complete raw diffuse and raw specular statistics match exactly.
This is matching aggregate statistics, not a bitwise hash of every raw frame.

| Signal | Legacy coarse variance | Repaired coarse variance | Reduction |
| --- | ---: | ---: | ---: |
| Filtered diffuse | 0.07471197 | 0.001373807 | 98.16% |
| Filtered specular | 0.1001894 | 0.005365218 | 94.64% |
| Final SceneView RT | 0.05762265 | 0.001120341 | 98.06% |

Variance is measured over time at fixed pixels, normalized by their mean-square
energy; coarse measurement averages8x8 pixels before collecting statistics.
Diffuse filtered/raw mean changes from0.1705 to0.9707; specular from0.1584 to0.9593.
The old suppression of bright samples was substantial. The repaired window means
are close to raw, but raw is a finite noisy observation, not analytic ground
truth. The changed mean also affects normalized variance: the reports do not
preserve its full mean-square denominator, so absolute variance changes cannot
be recovered exactly. Do not read the normalized percentages as equal absolute
noise reductions. Final camera colour additionally includes colour conversion
and other pipeline effects. No wall-clock convergence-time claim is made.

Internal Temporal/Clamping signals in the report are demodulated and converted
back from the per-frame GPU-selected NRD scale before collecting statistics.
Their mean/spatial domain differs from material-restored filtered outputs.
Filtered diffuse pixel variance drops from0.1114591 to0.002077175. Remaining
variance is nonzero; this audit does not certify the whole scene or motion quality.

An earlier exploration found the same diffuse improvement from a fast6 override;
disabling spatial reuse or switching Uniform to Power did not reproduce it.
Disabling anti-lag in addition made little difference, so anti-lag was retained.
A second exploratory request was consumed before the updated probe reloaded;
that report is excluded. Only the final versioned-path pair is acceptance evidence.

## Controlled regressions and reproduction

`slow-rg.json` and `slow-compat.json` rerun the actual GPU fixture with injected
1.2-second sample intervals: old pause-reset control resets192 times; production
resets once, reaches30/6 for both lobes, and restarts once on a light change.
The injected interval is not an actual FPS or GPU timing measurement. The
two-light fixture already had little fast-history sensitivity; the live scene
is the evidence for the new failure. `regression-exits.json` records exit0 for
both paths and `RunTimingCompatibility` (ordinary camera30/60/120Hz and clock
duplicate/rewind/gap/invalid checks). No existing thresholds were relaxed.

Run those three `HoRTDIValidation` entry points in the isolated
`.codex-research/RTInputValidation` project with Unity6000.3.15f1, D3D12 and
`-batchmode`. The adjacent parent README describes invocation.

For an authorized live reproduction, temporarily copy
`../Probes/HoRTDILiveFlowAudit.cs` and its meta into `Editor/RTDI`. Before reload,
write `.codex-research/rtdi-live-flow-v2-request.json` with the exact `project`,
a fresh absolute `output`, and `modes:["legacyFastZero","production"]`.
Keep the view fixed. The bounded observer restores the transient settings and
callbacks on completion/reload/quit/timeout and never saves assets. Remove the
imported helper afterward; the delivered copy is outside imported code.

```powershell
python Tests~/RTDI/SceneView/FastHistory/verify.py --source-commit 7360367
```

This checks stored reports and captured source fingerprints; it does not rerun
Unity. The historical source selector is needed after subsequent production
changes; no selector still checks the current source strictly.
