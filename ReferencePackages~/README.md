# Post-processing reference packages

This folder is for local research material while rebuilding Shoost-style post-processing as URP renderer features inside `lilToon-URP-Extensions`.

The folder name ends with `~` so Unity Package Manager ignores it. It is safe to place package source snapshots, Asset Store exports, or decompiled notes here for local comparison without adding them to the runtime package.

The `.gitignore` in this folder ignores real reference package contents by default. Keep only the small README/index files in git unless a source package is explicitly safe and intended to be vendored.

Recommended local layout:

```text
ReferencePackages~/
  UnityPostProcessingV2/
  RetroLookPro/
  XPostProcessing/
  KinoPostprocessing/
  ShoostUnpack/
```

Use `PackageSourceMap.md` as the checklist for which upstream package to inspect for each Shoost effect.

Use `ShoostSourceReadingGuide.md` for the practical workflow: how to combine the AssetRipper project, Cpp2IL ISIL, RenderDoc shader dumps, and upstream package source when rebuilding an effect.
