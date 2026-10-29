# PMX and asset handling

## Source layout

Keep the original package on `E:\BaiduSyncdisk\大师\_完整场景\H2Cu阿相` read-only. Copy only the package’s supported files into `Assets/mmd场景测试/H2Cu阿相/<stage>/Source`, retaining the PMX-relative texture directories. When a package has multiple models, put the complete package in a nested source directory such as `Source/完整场景`; this avoids collisions with an older prop package and makes the source relationship explicit.

Run `python Tools~/MmdStages/prepare-stage-sources.py --evidence <report.json>` and compare SHA256 for both source and destination. If a destination differs, stop instead of overwriting an artist edit.

## Import decisions

Use `HoPmxImporter` with the scene’s scale convention (`0.08` in this project), `generateOutline=false` for static stage work, `readRayMaterials=true`, and extract editable materials using `HoPmxMaterialExtraction.Extract` into a stage-owned Materials folder. PMX material texture paths are relative to the PMX file; never flatten them into a shared global texture directory.

The importer already maps common alpha modes and records source facts in `HoPmxImportReport`: PMX version, vertices/triangles, materials, morphs, rigid bodies, joints, soft bodies and warnings. Physics, MME effects and author preview lighting are not a reliable Unity scene. Recreate visible presentation with Ho lilToon, lights, shadow casting, local volume sources and post processing.

## Complete package example

The recovered Yueximei package contains `月昔梅 主體.pmx`, `月昔梅 樹.pmx`, `月昔梅 花枝.pmx` and `月昔梅 燈.pmx`, plus `tex/tex21.png`, `tex/tex22.png`, `tex/tex3.png` and `tex/YUE.jpg`. It is a full courtyard, unlike the older `團扇 月昔梅.pmx` prop package. Assemble the four models under `01 场景模型`, preserve the fan as an optional disabled prop prefab, remove the temporary studio plane, and keep each source package’s relative texture tree intact.

## What to inspect before lighting

- Material names that identify floors, water, windows, glass, lamps, foliage, metal and emissive ornaments.
- Mesh bounds and coordinate orientation. A floor’s reflection plane should use its top surface; a large wall or stray object can distort automatic camera bounds.
- Missing texture reports and duplicate texture names across separate packages.
- PMX components that are decorative duplicates. Do not stack every PMX in a distribution package without checking the author preview and material/geometry overlap.

