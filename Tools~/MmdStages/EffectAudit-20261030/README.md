# Ho PMX effect manifest audit

This is a repeatable, pure C# source audit for PMX packages. It does not launch Unity, import assets, execute MME FX, or modify source files. Each model entry contains the PMX SHA-256, parser counts/warnings, original relative texture references, toon/sphere slots, PMX shininess plus the Ray `ShininessToSmoothness` conversion hint, material flags, a conservative alpha section, and per-material name/emission information from the optional Ray `Material_MME.emd` mapping. Unknown Ray FX macro keys are retained under `materials[].mme.unknownFields`.

Run from the extension repository:

```powershell
dotnet run --project Tools~/MmdStages/EffectAudit-20261030/EffectAudit.csproj -- --root 'E:\path\to\package' --output 'Temp~/EffectAudit-20261030/manifest.json'
dotnet run --project Tools~/MmdStages/EffectAudit-20261030/EffectAudit.csproj -- --file 'E:\path\to\model.pmx'
```

The first command recursively audits `*.pmx`, sorted case-insensitively. Paths in the manifest are relative to each PMX package for MME dependencies and relative to `--root` for model paths. A non-zero exit code means at least one PMX failed to parse. Re-running with unchanged inputs produces the same source data and hashes; the tool intentionally omits a wall-clock timestamp.

Limitations: PMX material alpha is reported from the diffuse alpha and raw flags; image alpha is not decoded, so `inferred` can remain `UnknownWithoutTextureDecode`. MME is source evidence only: unrecognized FX templates are never executed or guessed, and fields outside the Ray macro contract are listed as unknown strings. Unity importer overrides and generated `HoPmxImportReport` alpha classification require a Unity-side audit and are not represented by this standalone tool.

The 2026-10-30 H2CU snapshot is committed under [`evidence/`](evidence/): 88 PMX files, 1,078 materials, 331 texture slots and zero parser failures. The snapshot is evidence for the source tree at that date; rerun the command when the source package changes.
