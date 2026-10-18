# Low-frame-rate Scene View convergence

The live BREAK_URP castle camera had NRD enabled. Passive observation found a
1391×933 Scene View with diffuse/specular history dropping to 1/1; intervals
above one second also triggered the game-pause reset policy. This was not a
disabled denoiser or a frozen editor clock.

Non-playing Scene Views now retain valid history across slow repaints. Settled
diffuse/specular budgets have a 30-sample minimum. Input changes still restart
once and leave the settled profile; rewind/invalid clocks still reset. The
game/Player time policy is unchanged. A foreground-only editor driver requests
up to 64 settled samples, capped at 60 repaint requests per second, then stops
requesting extra renders. Existing animation/tools can still render more frames.

## Evidence

- `live-before.json`: original clocks, camera frames, history budgets and reset
  state. Repeated passive observations of the same frame are not extra samples.
- `live-preview-fixed.json`: same live camera reached 30/30 and 64 settled samples;
  no synthetic clock or requested-render loop was used by this passive probe.
- `live-reservoir-20-vs4.json`: live central64² crop, 32 actual render warmup frames
  plus64 observed frames per budget. Per-pixel RGB Welford variance excludes
  static texture/spatial contrast; coarse variance averages8×8 pixels first.
  The history4 combined variance is about1/10.7 of history20; coarse variance is
  about1/7.9. ROI mean differs by about2.4%; this is not an analytic energy gate.
- `slow-{rg,compat}.json`: controlled GPU fixture with injected1.2-second sample
  intervals. Legacy control resets192/192, repaired control resets1/192 with
  identical raw statistics. Filtered variance falls from about.951 to.000433.
  This is a signal/clock protocol test, not a measured FPS/GPU-performance result.
- `referenceFast` in the controlled report is a diagnostic30/6 fast-history
  candidate. It barely changes variance versus the first fix and was not adopted.

Only `RTDI.maxHistory` in the test project's `PC_Renderer.asset` was restored
from20 to4 after measurement. A local backup was kept. The A/B itself uses a
camera-context override and restores it without saving the renderer or scene.
The Inspector label now explicitly says ReSTIR history limit.

## Reproduce

In `.codex-research/RTInputValidation`, use Unity6000.3.15f1 and D3D12:

```text
-batchmode -force-d3d12 -projectPath <isolated project>
-executeMethod lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunSlowSceneView
-logFile <log>
```

Use `RunSlowSceneViewCompatibility` for the other path. Also run
`RunTimingCompatibility` and `RunComposite`. The timing fixture now includes a
zero-flux second light to exercise actual RELAX rather than the deterministic
single-delta bypass, and checks the settled/responsive budget as appropriate.
No motion/energy threshold was relaxed.

`Probes/` contains the opt-in live observers used during diagnosis. They are
outside the imported package code. For a future authorized live investigation,
temporarily copy the desired probe into the package Editor/RTDI folder and
provide its scoped `.codex-research/rtdi-live-request.json` or
`rtdi-live-noise-request.json` before reload. Requests name the exact project and
an output path. Completed output prevents rearming; the paired probe restores its
context override on completion/reload/quit. Do not move the camera during a pair.
Remove the temporary imported copy after diagnosis.

The preserved live observer sources include later guard/one-shot refinements;
they are reproduction helpers, not a claim of byte-identical original probe code.
The live reports are retained unchanged. Production and controlled-test sources
are separately fingerprinted in `evidence.json`.

```powershell
python Tests~/RTDI/SceneView/verify_scene_view.py
python Tests~/RTDI/Composite/verify_composite.py --source-commit da52a0f
python Tests~/RTDI/Reference/verify_convergence.py --source-commit f537c26
```
