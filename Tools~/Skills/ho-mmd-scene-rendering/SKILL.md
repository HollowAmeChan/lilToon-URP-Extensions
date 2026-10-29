---
name: ho-mmd-scene-rendering
description: Build and refine static PMX scene renders in Unity with Ho lilToon and HoURP, including local volume lights, RTGI, PLR, three-view review, and CLI evidence.
---

# Ho Mmd Scene Rendering

Use this skill when a static PMX/PMD stage must become a clean, editable HoURP scene or when an existing scene needs a lighting/render-quality pass. The normal target is `D:\Unity_Project\BREAK_URP`; use CLI and Unity batch mode, preserve the source package, and keep each stage self-contained.

The workflow is iterative. Read only the references needed for the current phase, then add durable discoveries to [experience-log.md](references/experience-log.md) with a date, evidence path, and confidence. Do not turn a one-scene accident into a global rule without a second observation.

## Required contract

- Use the project’s Ho PMX importer and Ho lilToon materials. Extract editable materials into the stage folder; preserve PMX-relative external texture layout under that stage’s `Source/` tree.
- Treat a PMX stage as a static asset. Do not depend on an upstream PMX authoring loop. Record unsupported physics/MME information in the importer report and implement visible effects in Unity scene assets.
- Use RTGI + RELAX when ray GI is enabled. SSGI is prohibited at runtime; the fixed shared Renderer may retain an inactive SSGI feature placeholder for inspector/debug discoverability, but no SSGI Volume or active SSGI pass is allowed.
- Prefer world-space lights, real shadow casting, local Point/Spot HD volume lights and material emission over screen-center PostLighting or CenterColorCorrection. Screen effects may grade the image, but they must not carry the subject illumination.
- Give every stage its own scene, Volume/profile, materials, rendering assets and evidence. The H2CU family uses the fixed project `PC_RPAsset/PC_Renderer`; do not create per-stage RendererData copies. Do not share stage model/material/Volume assets.
- Keep the documented feature order: `GeometryData → Ho-RT → CharacterShadow → ShadowCast → Atmosphere → PLR source → GeometryBuffer → ObjectBuffer → SurfaceBuffer → RTShadow → AttributeComposite → GTAO`. Opaque ForwardLit consumes the per-renderer source directly; PLR material debug and special fullscreen composite additionally need OB identities plus GB/SB/AC. Enable Scene View preview on each surface.
- Review at least the main camera plus left and right observation cameras. A stage is not ready from one flattering still.
- Finish with a Unity-native audit and actual GPU captures. Evidence must include image dimensions, RTGI state, local-volume state, no-SSGI state, material/caster coverage, PLR, source hashes and unchanged unrelated stages.
- PLR `Ready=true` is an allocation/submission check. Verify non-black roughness mips and a same-camera reflection on/off pixel difference before claiming reflection works.

## Choose the phase

- For source selection, PMX import, relative textures or recovered multi-PMX packages, read [pmx-and-assets.md](references/pmx-and-assets.md).
- For atmosphere, shadows, local volume lights, world-space subject lighting, AO and PLR, read [lighting-and-rendering.md](references/lighting-and-rendering.md).
- For Unity CLI capture, three-view checks and delivery gates, read [validation-and-cli.md](references/validation-and-cli.md).
- Before changing a workflow, search [experience-log.md](references/experience-log.md) for the stage or subsystem and append the new evidence after the change.

## Working loop

1. Inventory the package and source hashes. Identify whether it is a complete stage, a prop-only package, or multiple PMX components. Keep source read-only.
2. Build or update the scene with independent assets. Extract materials and inspect texture/alpha/normal/metallic/smoothness roles before lighting.
3. Establish the world-space lighting composition: key, fill, rim, practicals, distant detail lights and local HD volume sources. Keep atmosphere strong only where it is part of the stage style; reduce competing global haze elsewhere.
4. Apply Ho lilToon AO, ShadowCaster, sky, PLR, RTGI/RELAX, depth of field, grading, bars and the bottom centered Hollow logo.
5. Capture three views across frames with StandardRequest and GPU readback. Inspect a contact sheet and individual views. Correct overexposure, unreadable distance detail, flat shadows and view-dependent center lighting.
6. Run the native audit, source/hash audit and unrelated-scene hash check. Update the scene index and evidence report, then commit only the reusable tool/skill changes.

The current durable implementation lives in `Tools~/MmdStages/Unity/Editor/HoMmdStageDetail.cs`, `HoMmdStageRemaining.cs`, `HoMmdStageAdvanced.cs`, `HoMmdStageAudit.cs`, and `HoMmdYueximeiComplete.cs`; use those as examples, not as a reason to overwrite artist edits.
