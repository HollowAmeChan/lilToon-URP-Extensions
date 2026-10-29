# H2CU P0 effect and Ray evidence — 2026-10-30

This snapshot records the source-side PMX effect manifest, the material/Ray truth table, and the independent contract fixture. The PMX parser is pure C# and read-only: it does not launch Unity, execute MME FX, rewrite source packages, or rely on an upstream PMX authoring loop.

## Source manifest

- Root scanned: `E:/BaiduSyncdisk/大师/_完整场景/H2Cu阿相`.
- 88 PMX files parsed, 1,078 PMX materials, 331 referenced texture slots, and 0 parser failures.
- Each file records SHA-256, relative texture paths, toon/sphere slots, PMX flags, shininess, `smoothnessFromShininess`, alpha inference, optional Ray EMD/MME fields, warnings and dependencies.
- Alpha values that require image decoding remain `UnknownWithoutTextureDecode`; this is an explicit uncertainty, not a fabricated opaque/transparent decision. No direct `.fx`, `.pmd` or `.x` files were found in the source inventory; unknown MME fields remain marked as unknown.

Machine-readable evidence: [H2CU-pmx-effect-manifest-20261030.json](H2CU-pmx-effect-manifest-20261030.json), [H2CU-effect-inventory-20261030.json](H2CU-effect-inventory-20261030.json), [H2CU-author-preview-index-20261030.json](H2CU-author-preview-index-20261030.json).

## Material and Ray contract

The committed truth table defines PMX shininess → lilToon smoothness → perceptual roughness (`1 - smoothness`), metallic/reflectance propagation, Opaque/Cutout/Transparent/Weighted-OIT boundaries, RTGI intersection and sky/GI exclusion rules, PLR gates, reflection probes and APV requirements. See [ray-material-truth-table.md](../ray-material-truth-table.md) and its JSON form.

The independent fixture reports **9 static checks passed, 0 failed**. Three GPU checks remain explicitly `UNVERIFIED` because they need an isolated D3D12 fixture: a transparent/Cutout/OIT render matrix, an excluded-sky toggle, and a probe/APV PLR capture. The H2CU stage pipeline has separate D3D12 evidence for RTGI and PLR behavior; it does not silently convert those three fixture caveats into passes.

Fixture evidence: [MaterialRayContract README](../../../../Tests~/MaterialRayContract/README.md), [report.json](../../../../Tests~/MaterialRayContract/report.json), [report.md](../../../../Tests~/MaterialRayContract/report.md).

## H2CU scene integration evidence

The later scene pass repaired the real H2CU stage assets without changing the project’s main scene: all 27 stage-owned renderers contain Object Buffer and Attribute Composite, PLR is ordered after AC, and every PLR surface has Scene View preview enabled. The native scene audit reports `errors=[]`; the delivery verifier reports 27 stages, 81 three-view captures, 369 source files verified and no errors. Two representative D3D12 captures (孔雀月令 and 乐园·墟实) have non-zero RTGI readback and sky exclusion; the former visibly carries the floor reflection.

Evidence is generated in the local project under `D:/Unity_Project/BREAK_URP/Library/HoMmdStages/scene-audit.json`, `Temp~/MmdStages/DetailLighting-20261029/final-detail-delivery.json`, and each stage `Renders/` folder. SSGI feature and volume counts are zero.

## Reproduction

```powershell
dotnet run --project Tools~/MmdStages/EffectAudit-20261030/EffectAudit.csproj -- `
  --root 'E:/BaiduSyncdisk/大师/_完整场景/H2Cu阿相' `
  --output Temp~/MmdStages/EffectAudit-20261030/pmx-effect-manifest.json
python Tests~/MaterialRayContract/validate_material_ray_contract.py
```

The parser intentionally does not claim that an author preview proves a specific MME pass. Visible glow, dust, particles, sky and post effects must be recreated as explicit HoURP assets and recorded with confidence-scored evidence.
