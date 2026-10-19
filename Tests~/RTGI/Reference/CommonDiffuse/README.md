# Step002: common diffuse response and independent energy check

2026-10-19. The preserved step001 probe remains unchanged at commit6e90d21.
This adapter selects only the reference final receiver diffuse lobe, using its
existing BRDF, reservoir weight, visibility and output helper. Initial rays,
secondary shading, GI temporal/spatial and SDK sources are not replaced.
Final MIS and RR are off. Ho uses the corresponding diffuse receiver and actual
linear dielectric F0 near.04, including the Gamma-tagged Reflectance convention.

Copy the two `HoReferenceGIDiffuse*` files into the isolated reference Assets/Editor
and run `HoReferenceGIDiffuseProbe.RunNoReuse` / `Run`. The adapter wraps the
preserved step001 probe before its first request and replaces only its in-memory
final evaluator. Do not overwrite or modify the original reference shaders.
Outputs have distinct `ho-reference-gi-common-diffuse-*` directories.

Ho entries on `HoRTGIMatchedReferenceValidation` are `RunCommonDiffuseNoReuse`
and `RunCommonDiffuse`, using the dedicated RTGIValidation project. Actual
secondary F0 must pack to `0xFF0A0A0A`; writing.04 directly to the Gamma-tagged
material field produced about.0039 after quantization and was rejected as an
invalid fixture. That presence-only capture is preserved in `results/` and is
not a passing energy test.

All four corrected Unity diagnostic entries exited0 with fresh reports. Their
final shader/C# compile diagnostics were empty. The initial reference adapter
missed the DI reservoir include needed by ShadingHelpers, exited1, and its failed
report is retained; the include was restored before accepted runs.

## Energy check and review

`python Tests~/RTGI/Reference/CommonDiffuse/integrate_fixture.py` performs an
independent rectangle area integral for this fixture. It uses the actual packed
secondary main colors/F0, primary white Lambert, point intensity/range, geometry
and central32x32 receiver ROI. Roughness1 has closed-form GGX D and Smith terms.
It neither calls the GI implementation nor samples its reservoirs. Comparing
48x48 and96x96 wall quadrature changes RGB means by less than0.01%.

| Captured result,256 GI requests | R error | G error | B error | Original5% gate |
| --- | ---: | ---: | ---: | --- |
| Ho no reuse | -.146% | -.412% | +1.201% | pass |
| Ho ReSTIR | +.220% | +1.759% | +3.608% | pass |
| Reference no reuse, diffuse evaluator | -7.373% | -9.731% | -9.554% | fail |
| Reference ReSTIR, diffuse evaluator | -5.413% | -16.911% | -22.446% | fail |

The comparison script actually exits1 and records `overallPass=false`,
`hoQualityPass=true`, `referenceQualityPass=false`. Do not call the entire
comparison green because rendering exited0. This is a finite static planar
fixture, not proof of arbitrary scene quality or mathematical unbiasedness.
Reference output subtraction also has FP16 precision limits on weak channels.

Review decision: accept the controlled diagnostic and Ho energy evidence;
preserve the reference-side discrepancy and defer its unique-cause investigation.
Do not change Ho gain, weights or acceptance thresholds to match it. Existing
Ho ReSTIR does not need a replacement to move forward. Next production work is
secondary sky lighting and scene consumption/reconstruction.

## Evidence and versioning

`results/` preserves four accepted rendering reports, the rejected shader/parameter
attempts, independent quadrature and a manifest with real exits, hashes and known
failures. Full snapshots/logs remain in `.codex-research/rtgi-step002-*` and the
two isolated projects. Step001's original Ho diagnostic source hash belongs to
commit6e90d21; later common-response entries extend that source, so verify the
historical source against its historical commit, not today's file.
