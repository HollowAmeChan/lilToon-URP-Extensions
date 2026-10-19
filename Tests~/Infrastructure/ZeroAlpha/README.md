# Zero-opacity infrastructure output audit

The reported sky material uses `Hidden/lilToonTransparent`, queue2460, with
`_Color.a=0` and cutoff.001. Its Forward fragment clips this opacity, but the
GB writer formerly normalized it to a full surface. The shared SB/OB material
path saturated transparent alpha without clipping it. Such an invisible front
surface could overwrite private depth/material inputs and invalidate downstream
association. The fix belongs to infrastructure writers, not individual effects.

## Output paths

| Writer/output | Handling |
| --- | --- |
| GB geometry normal and private depth | Ordinary transparent alpha resolves first, then shared cutoff/zero clip before output |
| SB colour, material, normal, reflection, classification, emission writer and owner | Shared material builder discards before any MRT/depth write |
| OB native identity, MSAA coverage and selection | Same shared builder runs before identity/selection output |
| SB semantic owner/lanes and correlated fragment statistics | Same builder runs before lane/owner/statistic writes; source audited |
| GB outline normal/depth/owner | Existing standard alpha subpass plus explicit ordinary-transparent zero rejection, including cutoff0 |
| AC geometry/identity/surface references | Retain the actual rear inputs; no added per-effect or post-composition opacity heuristic |

`lil_ho_surface_alpha.hlsl` checks ordinary `LIL_RENDER==2`, excluding refraction:
match Forward's cutoff, then discard exact zero even when cutoff is0. It runs
after the main/layer/alpha-mask/dissolve calculation. A discard leaves rear
depth and attachments intact; writing black/zero validity would not be enough.
No draw-call skipping, rendering-order change or new transparency compositor is
introduced.

Opaque colour alpha is deliberately ignored by its rendering model and must
still produce a visible surface. Refraction may remain visible at colour alpha0.
Neither is globally removed just because `_Color.a` is zero. Partially transparent
pixels retain the existing nearest-surface approximation. Arbitrary custom
shaders/late depth-fade/dither variants are not newly certified by this change.
GB/OB override-material fallback remains limited to opaque queues below2450;
it cannot reconstruct arbitrary source-shader opacity or a transparent material
manually forced into an opaque fallback queue.

## Actual GPU qualification

Unity6000.3.15f1, D3D12, perspective camera and64-square isolated scene with
GB+OB+SB+AC. OB actually negotiates4 samples on both RG and compatibility paths.
The real native transparent writer is exercised; 1x opaque-only fallback is not
accepted as a transparent positive control.

Six RGBA32 captures read GB/AC depth and producer state, SB colour, ranked OB/AC
identity, numeric/semantic owner bytes, a semantic lane and outline depths.
The scene contains a rear opaque red plane at eye depth3 and a front green
ordinary transparent plane at depth2, with distinct registered identities.
Both render paths pass28 checks:

- Front disabled provides a measured rear baseline.
- Front colour alpha0, alpha0 with cutoff0, and texture alpha0 each preserve
  every captured rear value (per-channel tolerance.002).
- Front alpha1 produces depth2, green SB colour and the distinct front
  identity/owners, proving the transparent writer was not simply skipped.
- Opaque colour-alpha0 still produces the visible front surface.

Semantic ownership is positively exercised. The fixture's semantic lane values
and outline images are empty; those payloads are equality guards, not positive
proof of every lane/outline variant. Correlated semantics are disabled in this
fixture; the correlated writer's shared clip entry was audited in source. No
claim of complete fractional-opacity/MSAA semantic reconstruction is made.

`outputs` indices in the JSON reports:

0. GB depth, AC depth, SB active, AC inputs-published.
1. SB colour.
2. Ranked identity exposed to OB/AC consumers.
3. Numeric owner RG and semantic owner BA.
4. Semantic lane0 (zero-valued in this fixture).
5. GB and AC outline depth (empty in this fixture).

Fresh surface-association RG/compatibility and RTDI blend RG/compatibility
regressions also exit0. No source scene/material is saved, and thresholds were
not relaxed. The test reads AC's inputs-published flag, not selection-pool active.

Run `lilToon.URP.Extensions.Editor.SurfaceBuffer.HoInvisibleSurfaceValidation.Run`
and `RunCompatibility` in `.codex-research/RTInputValidation` with Unity
`-batchmode -force-d3d12 -projectPath <isolated project> -executeMethod <entry>
-logFile <log>`. Both this package and the adjacent patched lilToon package are
required. The shader fix is committed in lilToon at `b124f7e`.

```powershell
python Tests~/Infrastructure/ZeroAlpha/verify.py
```

This checks preserved artifacts/source fingerprints, not a fresh Unity rerun.
The adjacent lilToon working tree may contain unrelated generated/editor input
changes; this delivery does not stage or rewrite those files.
