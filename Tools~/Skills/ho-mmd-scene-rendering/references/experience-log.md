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

## 2026-10-30 — H2CU inventory expansion

- The reorganized source tree contains 32 H2CU source packages. After the original 19 scenes and recovered complete Yueximei, eight additional independent scene packages were identified: 孔雀月令, 孔雀月令青瓷, 丝苇楼梯, 月极蝶夜礼, 虫无阶梯, 虫染阶梯, 乐园·葡萄柚 and 乐园·西瓜霜.
- Evidence: `Temp~/MmdStages/H2CuInventory-20261030/agent-inventory.json`, `new-source-copy.json`, and `new-scenes-three-review.jpg`. Confidence: confirmed; 369 source files are covered by the expanded manifest.
- Author preview evidence suggests the blue/red insect stairs are intentionally stylized, while the ladder scene has deliberately high-contrast sparse materials. Preserve those styles during refinement instead of treating them as importer failures.
