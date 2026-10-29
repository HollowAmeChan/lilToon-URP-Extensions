# Dynamic experience log

This is a maintained evidence log, not a list of universal rules. Append only observations that changed a decision or were confirmed by a rendered/audit artifact. Each entry should include date, stage/subsystem, evidence path and confidence.

## 2026-10-29 — H2Cu batch authoring

- Scope: 19 independent H2Cu scenes in `BREAK_URP/Assets/mmd场景测试/H2Cu阿相`.
- Evidence: `Temp~/MmdStages/DetailLighting-20261029/final-detail-delivery.json`, `场景索引.md`, Unity `scene-audit.json`.
- Result: 57 three-camera D3D12 captures; RTGI + RELAX active; SSGI feature/volume counts zero; AO, ShadowCaster and PLR checks passed. Confidence: confirmed.

## 2026-10-29 — source boundaries

- Keep PMX source packages read-only and retain relative texture folders. A copied source manifest with SHA256 is the authoritative proof.
- Evidence: `Temp~/MmdStages/YueximeiComplete-20261030/all-source-copy.json` and `source-copy.json`. Confidence: confirmed for 283 files.

## 2026-10-30 — complete Yueximei recovery

- The package with suffix `32903cd061fc7937dd3f529c539d094c` contains the complete courtyard: 主體, 樹, 花枝 and 燈. The older package used in the first pass only contained the fan prop.
- Assemble four PMX assets under `Source/完整场景`, extract materials under `Materials/完整场景`, preserve the fan as an optional disabled prefab, and remove the temporary studio plane.
- Evidence: `Temp~/MmdStages/YueximeiComplete-20261030/final-delivery.json`, `Library/HoMmdStages/月昔梅-complete-audit.json`, `月昔梅/Renders/三机位总览.jpg`. Confidence: confirmed; four reports have no importer warnings and all three views use RTGI.

## 2026-10-30 — lighting refinement

- Screen-center PostLighting and CenterColorCorrection helped a single still but failed as a view-independent lighting solution. World-space key/fill/rim, distant detail lamps, local HD volume sources and shadow-only dapple geometry hold up across three views.
- 乐园·墟实 benefits from its original ruin haze; lowering all atmosphere uniformly loses the intended window shafts. 百鸟溯归 needs broad rear-wall lights to recover distant shelves without lifting the whole image.
- Evidence: `Temp~/MmdStages/DetailLighting-20261029/final-detail-delivery.json`, `final-review-*.jpg`. Confidence: confirmed on the 19-scene set.

## How to append

Use the date and a short subsystem heading. State the failed or surprising behavior, the change made, the exact evidence path, and whether it is confirmed or a working hypothesis. Prefer a focused note over a general slogan.

## 2026-10-30 — MME/FX/Ray audit

- The H2CU source tree contains 32 package directories, 31 author preview images, 88 PMX files and no direct `.fx`, `.pmd` or `.x` files; readable ZIP entries also contain no `.fx`. `.7z` archives were intentionally not unpacked. This means visible MME behavior must be reconstructed from previews/material conventions and marked as inferred.
- Highest priority follow-up is a PMX effect manifest and material/ray truth table: preserve alpha mode, toon/sphere maps, shininess, emission hints, source hashes and explicit `unknown` MME fields. Then verify PMX shininess→lilToon smoothness, cutout/transparent/OIT/RT behavior and sky ray exclusion.
- The existing Ho paths cover Glow, Particle/Weather, atmosphere dust, local HD volume lights, PLR, RTGI+RELAX and APV-related URP plumbing. APV/reflection-probe activation and PMX-specific MME reconstruction are not proven by the source packages or this audit.
- Evidence: `Temp~/MmdStages/EffectAudit-20261030/H2CU-HoURP-EffectGapReport.md`, `effect-inventory.json`, `author-preview-index.json`. Confidence: source inventory confirmed; effect semantics remain confidence-scored inference.

## 2026-10-30 — eight additional H2CU stages

- The reorganized source tree added eight independent stage packages beyond the first 19 scenes: 孔雀月令, 孔雀月令青瓷, 丝苇楼梯, 月极蝶夜礼, 虫无阶梯, 虫染阶梯, 乐园·葡萄柚 and 乐园·西瓜霜. The resulting project has 27 Unity scenes.
- Some packages used different PMX filenames than their archive description: `场景.pmx` for both insect stairs, `主体/合并版/植物.pmx` for 丝苇楼梯, and `场景主体/吊饰/地板追加.pmx` for the two stage05-2 recolors. Confirm the actual copied filenames before building.
- New packages may not carry an HDR sky. Copy a selected SkySeries HDR into each stage-owned Rendering folder before invoking `HdrSky`; otherwise Unity import can leave the `TextureImporter` null.
- Evidence: `Temp~/MmdStages/H2CuInventory-20261030/new-source-copy.json`, `new-scenes-three-review.jpg`, and the 27-scene `Library/HoMmdStages/scene-audit.json`. Confidence: confirmed.

## 2026-10-30 — P0 PMX and Ray material contract

- The repeatable source-only manifest in `Tools~/MmdStages/EffectAudit-20261030` records PMX SHA256, relative textures, toon/sphere maps, flags, alpha inference, Ray emission/template fields, warnings/dependencies and `smoothnessFromShininess`. Unity-side texture alpha classification remains separate and is never fabricated by the pure parser.
- The Ray truth table confirms PMX shininess is converted to lilToon smoothness, then RTGI uses perceptual roughness `1 - smoothness`; metallic and smoothness feed RTGI bounce energy and primary ray material buffers. Opaque and a bounded Cutout subset have RTGI hit/any-hit support. Continuous Transparent and Weighted OIT remain raster-only for RTGI.
- Reflection probes, PLR and APV are raster primary-lighting paths. RELAX is a denoiser, not a material or light transport feature. Sky shells require exact material exclusion; local Point/Spot volume lights use an independent raster depth atlas and do not add transparent/OIT RT support.
- Evidence: `Tools~/MmdStages/EffectAudit-20261030/ray-material-truth-table.md`, `ray-material-truth-table.json`, `Tests~/MaterialRayContract/report.md`. Static fixture result: 9 pass, 0 fail, 3 runtime GPU checks unverified. Confidence: source claims confirmed; the three runtime items require an isolated D3D12 Unity fixture.

## 2026-10-30 — H2CU inventory expansion

- The reorganized source tree contains 32 H2CU source packages. After the original 19 scenes and recovered complete Yueximei, eight additional independent scene packages were identified: 孔雀月令, 孔雀月令青瓷, 丝苇楼梯, 月极蝶夜礼, 虫无阶梯, 虫染阶梯, 乐园·葡萄柚 and 乐园·西瓜霜.
- Evidence: `Temp~/MmdStages/H2CuInventory-20261030/agent-inventory.json`, `new-source-copy.json`, and `new-scenes-three-review.jpg`. Confidence: confirmed; 369 source files are covered by the expanded manifest.
- Author preview evidence suggests the blue/red insect stairs are intentionally stylized, while the ladder scene has deliberately high-contrast sparse materials. Preserve those styles during refinement instead of treating them as importer failures.

## 2026-10-30 — AC prerequisite for PLR and Scene View preview

- A renderer can contain a PLR feature and still fail to produce valid coverage when Object Buffer/Attribute Composite are absent or when PLR is ordered before AC. The stable order is `GeometryData → GeometryBuffer → ObjectBuffer → SurfaceBuffer → AttributeComposite → PlanarReflection`.
- `HoMmdFeatureRepair.Run` now repairs all 27 H2CU renderer assets, enables PLR for game and Scene View, and serializes `reflectSceneView: 1` on each floor receiver. A serial audit after the repair reported `errors=[]`; a D3D12 capture of 孔雀月令 and 乐园·墟实 produced all six requested views, with the former showing the floor reflection and the latter retaining its authored haze and window shafts.
- Evidence: `D:/Unity_Project/BREAK_URP/Library/HoMmdStages/scene-audit.json`, `D:/Unity_Project/BREAK_URP/Library/HoMmdStages/plr-ac-render-check-d3d12.log`, `Assets/mmd场景测试/H2Cu阿相/孔雀月令/Renders/` and `Assets/mmd场景测试/H2Cu阿相/乐园·墟实/Renders/`. Confidence: confirmed on the 27-scene set.
