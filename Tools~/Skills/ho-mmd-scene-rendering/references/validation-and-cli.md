# Unity CLI and evidence

## Capture

Use Unity 6000.3.15f1 with D3D12 for ray scenes. Do not launch two Editors for BREAK_URP. Prefer an execute method that performs authoring, returns to `Assets/Hollow/WTD-summer/WTD.unity`, then schedules `HoMmdStageBuilder.BeginRender` through `EditorApplication.delayCall`. Do not pass `-quit` to an async capture entry; Unity can exit before the queued callback. Capture runs need a graphics device, so do not pass `-nographics` when rendering PNGs; use `-nographics` only for authoring or audit passes that never submit a frame.

## Renderer feature dependency and Scene View preview

For every stage-owned renderer, keep the producer/consumer order explicit:

`HoGeometryDataRendererFeature → HoGeometryBufferRendererFeature → HoObjectBufferRendererFeature → HoSurfaceBufferRendererFeature → HoAttributeCompositeRendererFeature → HoPlanarReflectionRendererFeature`.

Attribute Composite (AC) is a required producer for PLR coverage and material attributes. A PLR feature can exist and still render incorrectly when AC is missing or placed after it. The repair entry is `HoMmdFeatureRepair.Run`; it adds missing OB/AC/PLR features, restores this order, enables the PLR game and Scene View feature flags, and writes `reflectSceneView: 1` on every stage surface. Run it after any authoring pass that recreates a floor or renderer asset, then rerun `HoMmdStageAudit.RunBatch`.

Capture with StandardRequest across warmup frames (32 in this project), then read back the HDR render target asynchronously and encode PNG once. For each stage capture:

- main camera
- left observation camera
- right observation camera

Store `<stage>_主视角.png`, `<stage>_左侧视角.png`, `<stage>_右侧视角.png` and `三机位总览.jpg` under that stage’s Renders folder. Store runtime per-view state under `Library/HoMmdStages`.

## Audit gates

`HoMmdStageAudit.RunBatch` must report `errors=[]` for all authored scenes (27 in the current H2CU set). Check:

- stage-owned Pipeline, Renderer and Volume/profile dependencies
- all model renderers have ShadowCaster and receive shadows
- Ho lilToon/sky shaders only, with no missing shaders
- one atmosphere region, camera post processing and sky sphere
- DoF, cinematic bars, centered Hollow Logo and opposite border/logo colors
- AO art coverage equals eligible material count
- at least one 1024 PLR surface per scene
- active Object Buffer and Attribute Composite features, with PLR after AC in the renderer list
- PLR Scene View preview enabled both on the feature and on every surface
- RTGI active, Ho RT host before RTGI, sky excluded
- SSGI feature and volume asset counts are zero
- three review cameras and no screen-center illumination layers after the world-light pass

## Source and visual verification

Use `Tools~/MmdStages/verify-detail-lighting.py` after final captures. It checks the current capture count (81 images for the 27-scene H2CU set), 1280×720 dimensions, per-view RTGI output, `HD：独立光源遮挡` local volume status, three distinct camera positions, source hashes and unchanged unrelated scene hashes. Keep the resulting `Temp~/MmdStages/DetailLighting-20261029/final-detail-delivery.json` or the replacement-stage report with the scene index.

When replacing a stage package, archive the prior prop-only scene state under a task evidence folder before modifying it. Verify the replacement scene’s own PMX report, extracted material folder, relative texture paths, view captures, PLR count and RTGI state.

## Useful commands

```powershell
python Tools~/MmdStages/prepare-stage-sources.py --evidence Temp~/MmdStages/source-copy.json
python Tools~/MmdStages/verify-detail-lighting.py
```

```text
-batchmode -force-d3d12 -projectPath D:\Unity_Project\BREAK_URP
-executeMethod HoMmdStageDetail.Run
```

Use `-hoStages Name1,Name2` to limit a pass, `-hoApplyOnly 1` to apply without capture, and `-hoWidth 960 -hoOutputSuffix preview` for quick previews. Never treat an observation timeout as a failed capture; inspect the actual Unity PID, log and `capture-status.json` first.

