# Ho lighting and rendering contract

## Composition

Build illumination in world space. Use a key, fill, rim, practicals and distant detail lights aimed at the geometry that must read. For an architectural stage, add wide distant lights for rear walls and shelves; for a central prop, add low side fills and a controlled rim. Use 斑驳遮片/ShadowCaster geometry where the stage benefits from patterned shadows.

The old failure mode was a screen-center PostLighting layer making a single still look correct while side views became dark. Remove `ScreenProcessEffect.PostLighting` and `ImageProcessEffect.CenterColorCorrection` when real lights can carry the subject. Keep gradient maps, bloom, DoF, bars and logo as finishing effects.

Use the Ho local volume component on important Point/Spot lights:

- `HoVolumeLightSource.quality = HD`
- 96–256 steps for visible beams, 512–1024 HD shadow resolution for Spots, 256–512 for Point faces
- `shadowStrength=1`, moderate `sourceRadius`, and a soft edge
- keep the source’s real Light shadow/caster state enabled

The feature shares `HoAtmosphereRegion` medium data and captures independent LightDepth. It is not a substitute for surface light; add the raster light and the local volume source together. Check `Status` for `HD：独立光源遮挡`.

## Atmosphere and style

Reduce solar shafts, region density, height fog, SkyTyndall and Glow together when a scene becomes pale or fog-dominant. Do not use one global exposure lift to solve dark distant geometry. Preserve a stage’s intentional haze when it is part of the reference style: 乐园·墟实 uses a deliberate ruin-window haze, while 花吐电気 must stay bright and sunny.

Use per-scene HDR sky copies. Exclude the stage sky material from RTGI rays. Apply Ho lilToon realtime AO (`_UseRealtimeAO`, `_AOContrast`, `_AODarkStrength`) to all eligible model materials. Enable ShadowCaster and receive shadows for all model MeshRenderers, then tune shadow color/strength rather than flattening them with `AsUnlit`.

## Floor reflection

Identify real floors and water by material name and geometry, not just the lowest mesh bound. Add a `HoPlanarReflectionSurface` per named horizontal receiver when appropriate, set resolution to 1024, assign a stage-owned plane transform and enable the material’s planar reflection. Use the feature surface cap for multiple floors. Check the reflection plane height for water, thick floors and elevated platforms.

## Known scene choices

- 花吐电気: bright window daylight, outdoor lights on a separate rendering layer, low haze and real window/room light.
- 乐园·墟实: retain the atmospheric ruin haze, add warm window bounce and a cool separation light so windows and hanging ropes remain readable.
- 圣诞万花镜: low point lights at the floor/tree/gifts; keep exposure and bloom restrained.
- 百鸟溯归: illuminate far book walls and shelves with broad lights; retain warm tree shaping and a cool branch rim.
- 冷色古堂: control the bright center screen and use cool lantern bounce plus foreground carpet moonlight.
- 月昔梅 complete package: use the four recovered PMX parts, warm/cool courtyard lights, real local volumes and separate PLR for floor/water; keep the fan optional and disabled.

