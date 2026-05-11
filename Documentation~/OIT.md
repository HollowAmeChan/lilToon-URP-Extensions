# lilToon URP Weighted OIT Implementation Notes

This document records the completed Weighted OIT integration between the
`lilToon` fork and the companion `lilToon-URP-Extensions` package.

It is intentionally written as an implementation case study, not just a feature
overview. The goal is to preserve the architecture, the actual file-level
workflow, and the traps that were found while debugging RenderDoc captures.

## Final Goal

The feature adds optional Weighted Order-Independent Transparency for lilToon
transparent shaders under URP.

When a material enables `_lilOITEnabled`:

- lilToon writes transparent color into OIT accumulation and revealage buffers.
- The URP renderer feature composites the resolved transparent result back into
  camera color.
- Skybox-only backgrounds work.
- Overlapping transparent lilToon objects no longer depend on object sorting in
  the same way as regular alpha blending.

The implementation target is:

- Unity 2022.3
- URP 14.x
- lilToon fork shader-template layout
- `lilToon-URP-Extensions` as an external renderer-feature package

## Architecture

The implementation is split across two repositories on purpose.

### lilToon fork side

The lilToon fork owns material properties, shader template assembly, and shader
fragment output.

It adds:

- A material property named `_lilOITEnabled`.
- URP use-pass templates that include a `LILTOON_OIT` pass.
- A shared `lil_oit.hlsl` include that converts lilToon forward color into MRT
  OIT output.
- Forward shader guards that skip the normal forward transparent pass while the
  OIT accumulation pass is active.

The shader pass tag used for OIT is:

```shaderlab
Tags {"LightMode" = "lilToonOIT"}
```

This is the contract consumed by the extension package.

### URP extension side

The extension package owns URP render target allocation and render pass timing.

It adds:

- `WeightedOITRendererFeature`
- `WeightedOITClearPass`
- `WeightedOITOpaqueCopyPass`
- `WeightedOITAccumulationPass`
- `WeightedOITCompositePass`
- `WeightedOITComposite.shader`
- OIT shader constants and settings

The renderer feature draws only passes tagged with `LightMode = lilToonOIT`.
It does not know about lilToon material internals except the global shader
properties used by the OIT include.

## Data Flow

The intended frame flow is:

1. Reset `_lilOITActive` to `0` when each camera starts rendering.
2. Render opaque objects.
3. Render skybox.
4. Copy camera color after skybox into `_lilOITOpaqueTexture`.
5. Clear OIT accumulation to transparent black.
6. Clear OIT revealage to white.
7. Draw lilToon objects with `LightMode = lilToonOIT`.
8. Set `_lilOITActive = 1` only while drawing the OIT accumulation pass.
9. Composite accumulation and revealage over the camera color target.
10. Set `_lilOITActive = 0` again after composite.

The background copy is also published as `_CameraOpaqueTexture` during the OIT
draw. This lets existing lilToon background/refraction macros continue to sample
a useful skybox-inclusive background without rewriting all shader-side sampling
sites.

## Weighted OIT Buffers

The shader writes two MRT outputs:

- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`

The lilToon OIT include currently uses:

```hlsl
output.accumulation = float4(color.rgb * alpha * weight, alpha * weight);
output.revealage = float4(alpha, 0.0, 0.0, 0.0);
```

The accumulation buffer stores weighted premultiplied color and total weight.
The revealage buffer is blended separately by the pass blend state.

Composite resolves:

```hlsl
transparentColor = accumulation.rgb / max(accumulation.a, epsilon);
transparentAlpha = saturate(1.0 - revealage);
cameraColor.rgb = lerp(cameraColor.rgb, transparentColor, transparentAlpha);
```

## Render Pass Timing

The important URP timing choices are:

- Opaque background copy: after skybox, before OIT accumulation.
- OIT accumulation: `BeforeRenderingTransparents`.
- OIT composite: `AfterRenderingTransparents`.

URP normally draws skybox after opaque objects and before transparent objects.
That ordering is correct and should not be fought. The OIT system needs a
background texture captured after skybox, not a second manual skybox draw.

## Background And Skybox

An early bug made OIT objects too dark when their background was only skybox.

The cause was that the shader-side background sampling path ultimately relied on
`_CameraOpaqueTexture`, but URP's opaque texture path does not necessarily give
the OIT shader a skybox-inclusive texture in this custom timing.

The working solution is:

1. After skybox, copy the camera color target into `_lilOITOpaqueTexture`.
2. Bind that texture as both:
   - `_lilOITOpaqueTexture`
   - `_CameraOpaqueTexture`
3. Also update `_CameraOpaqueTexture_TexelSize`.

This preserves lilToon's existing `LIL_GET_BG_TEX` / `LIL_GET_GRAB_TEX` style
paths while giving OIT shaders the right background.

## Render Queue Notes

lilToon transparent shaders intentionally use:

```shaderlab
"RenderType" = "TransparentCutout"
"Queue" = "AlphaTest+10"
```

This is not an accident. lilToon also defaults transparent `_ZWrite` to `1`.
That combination makes many toon/avatar transparent surfaces behave more like a
depth-stable alpha-test-adjacent object than a normal Unity transparent object.

This is useful for regular lilToon rendering, especially for character parts
such as hair, eyelashes, and layered clothing where stable self-occlusion is
often more important than physically correct alpha sorting.

However, this queue choice complicates OIT:

- `AlphaTest+10` is still in the opaque/alpha-test range.
- It renders before skybox in URP.
- Regular forward rendering can appear before the OIT pass.

The final implementation does not globally change lilToon's queue. Changing all
transparent shaders to `Transparent` would alter existing material behavior too
much. Instead, OIT is added as an optional pass and renderer-feature path.

If a future version chooses to force OIT materials into queue 3000, do it as a
material-level opt-in and verify that the OIT accumulation and composite passes
are definitely producing output first. During debugging, forcing queue 3000 too
early made OIT materials disappear because the normal forward pass was skipped
but OIT accumulation was invalid.

## `_lilOITActive`

`_lilOITActive` is a global shader state used as a handshake between the
extension package and lilToon shader code.

In the regular forward shader path:

```hlsl
clip(0.5 - _lilOITEnabled * _lilOITActive);
```

This prevents OIT-enabled materials from also drawing in the regular forward
path while the OIT accumulation pass is active.

Important rule:

`_lilOITActive` must be reset to `0` for every camera.

It is global, so if one camera leaves it as `1`, editor preview or scene camera
paths can lose OIT-enabled materials while non-OIT materials continue to draw.
The extension registers `RenderPipelineManager.beginCameraRendering` to reset
the value before each camera begins.

## Render Target Sizing And MSAA

The largest practical bug in this implementation was not shader math. It was
illegal render target binding.

RenderDoc showed this D3D warning:

```text
Invalid output merger - Depth target is different size or MS count to render target(s).
```

Two captures exposed two variants:

- OIT accumulation was `238x790`, but camera depth had a different size.
- OIT accumulation was `1056x790` non-MSAA, but camera depth was `1056x790 MSAA8x`.

When this happens, the OIT accumulation pass can silently fail to write useful
results. If the regular forward path is skipped, OIT objects disappear.

The final rules are:

- Full scale OIT keeps the camera descriptor MSAA sample count.
- Half/Quarter OIT forces `msaaSamples = 1`.
- The accumulation pass binds camera depth only when color and depth match:
  - width
  - height
  - volume depth
  - anti-aliasing sample count
- If they do not match, the pass binds only the OIT MRTs.

This avoids invalid output merger state and makes the feature work in Game view
and Scene view.

## Files Changed: lilToon Fork

### Base shader resources

Transparent shader descriptors were switched to OIT-aware URP use-pass blocks.
Affected families include:

- `lts*_trans*.lilinternal`
- `lts*_onetrans*.lilinternal`
- `lts*_twotrans*.lilinternal`
- `lts*_overlay*.lilinternal`
- lite and tessellation variants

These files select blocks such as:

- `DefaultUsePassOIT`
- `DefaultUsePassOutlineOIT`
- `DefaultUsePassTwoSideOIT`
- `DefaultUsePassOutlineTwoSideOIT`
- `DefaultUsePassOverlayOIT`

### URP shader templates

OIT passes were added to the hidden pass shaders:

- `Assets/lilToon/CustomShaderResources/URP/DefaultTwoSide.lilblock`
- `Assets/lilToon/CustomShaderResources/URP/DefaultLiteTwoSide.lilblock`
- `Assets/lilToon/CustomShaderResources/URP/DefaultTessellationTwoSide.lilblock`

The pass uses:

```shaderlab
Name "LILTOON_OIT"
Tags {"LightMode" = "lilToonOIT"}
ZWrite Off
BlendOp 0 Add
BlendOp 1 Add
Blend 0 One One
Blend 1 Zero OneMinusSrcColor
```

### URP use-pass templates

New URP use-pass templates route transparent shaders to the OIT pass:

- `DefaultUsePassOIT.lilblock`
- `DefaultUsePassOutlineOIT.lilblock`
- `DefaultUsePassTwoSideOIT.lilblock`
- `DefaultUsePassOutlineTwoSideOIT.lilblock`
- `DefaultUsePassOverlayOIT.lilblock`

These keep the regular lilToon passes and add:

```shaderlab
UsePass "*LIL_PASS_SHADER_NAME*/LILTOON_OIT"
```

### Material property and Inspector

The transparent property block adds:

```shaderlab
[lilToggle] _lilOITEnabled ("Weighted OIT", Int) = 0
```

Inspector changes:

- `lilMaterialProperties.cs` binds `_lilOITEnabled`.
- `lilPropertyGroupDrawerBaseSetting.cs` displays the toggle only for URP
  transparent materials.

### Shader includes

Changed/added includes:

- `Shader/Includes/lil_oit.hlsl`
- `Shader/Includes/lil_common_input.hlsl`
- `Shader/Includes/lil_common_input_base.hlsl`
- `Shader/Includes/lil_common_input_opt.hlsl`
- `Shader/Includes/lil_pass_forward_normal.hlsl`
- `Shader/Includes/lil_pass_forward_lite.hlsl`

`lil_pass_forward_*` switches the fragment return type to an MRT output for
`LIL_OIT_PASS` and calls `LIL_OIT_RETURN(...)` instead of normal output.

## Files Changed: URP Extension Package

### Runtime/OIT/WeightedOITRendererFeature.cs

Owns render pass orchestration:

- reset global OIT state per camera
- allocate OIT RTs
- copy skybox-inclusive background
- clear accumulation/revealage
- draw `lilToonOIT` shader-tag pass
- composite back to camera color
- release RTHandles

### Runtime/OIT/WeightedOITShaderConstants.cs

Centralizes shader names and property IDs:

- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`
- `_lilOITOpaqueTexture`
- `_lilOITCompositeSourceTexture`
- `_lilOITActive`
- `_CameraOpaqueTexture`
- `_CameraOpaqueTexture_TexelSize`

### Runtime/OIT/WeightedOITComposite.shader

Fullscreen composite shader. It samples:

- `_BlitTexture` for current camera color
- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`

Then resolves weighted color over camera color.

### Runtime/OIT/WeightedOIT.hlsl

Package-side helper include retained for extension consumers. The lilToon fork
currently has its own `lil_oit.hlsl` include because it is integrated into the
lilToon shader generation pipeline.

## Debugging Timeline And Traps

### 1. Skybox-only background was dark

Symptom:

- OIT object looked correct when opaque objects were behind it.
- OIT object looked too dark when only skybox was behind it.

Cause:

- OIT shader background sampling did not have a skybox-inclusive texture.

Fix:

- Copy camera color after skybox.
- Publish the copy as `_CameraOpaqueTexture` for the OIT draw.

### 2. Forcing queue 3000 made objects disappear

Symptom:

- Moving OIT materials into the transparent queue made them disappear.

Cause:

- The regular forward pass was skipped by `_lilOITActive`.
- The OIT accumulation pass was failing because render target binding was
  invalid.

Lesson:

- Queue issues were real, but they were not the first bug to fix.
- Always confirm accumulation and composite are actually producing output before
  changing material queue behavior.

### 3. RenderDoc exposed invalid output merger state

Symptom:

- OIT effect unstable or invisible in Scene view / camera preview.

RenderDoc warning:

```text
Invalid output merger - Depth target is different size or MS count to render target(s).
```

Fix:

- Match MSAA in full-scale OIT RTs.
- Bind camera depth only when compatible.

### 4. Preview/camera state could lose OIT objects

Symptom:

- OIT objects missing in preview paths while non-OIT objects were visible.

Cause:

- `_lilOITActive` is global state.

Fix:

- Reset `_lilOITActive` at `RenderPipelineManager.beginCameraRendering`.
- Also keep a preview reset pass as a defensive fallback.

## How To Verify

In Frame Debugger or RenderDoc, look for:

```text
lilToon Weighted OIT Opaque Copy
lilToon Weighted OIT Clear
lilToon Weighted OIT Accumulation
lilToon Weighted OIT Composite
```

Check that:

- OIT accumulation draw calls exist.
- No `Invalid output merger` warnings appear.
- `_lilOITAccumulationTexture` size and MSAA match camera depth when depth is
  bound.
- Skybox is visible through OIT-only objects.
- OIT and non-OIT transparent objects can coexist.

If OIT objects disappear:

1. Check whether the composite shader was found.
2. Check whether `lilToon Weighted OIT Accumulation` has draw calls.
3. Check RenderDoc warnings for RT/depth size or MSAA mismatch.
4. Check whether `_lilOITActive` is stuck at `1` outside the accumulation pass.
5. Check material render queue overrides; manually forced queue 3000 can hide
   other bugs.

## Extension And lilToon Contract

The package/fork boundary is deliberately narrow.

lilToon promises:

- OIT-enabled transparent shaders expose `_lilOITEnabled`.
- OIT-capable shaders include a pass tagged `LightMode = lilToonOIT`.
- That pass writes MRT accumulation/revealage output.
- Regular forward passes respect `_lilOITActive`.

The extension promises:

- Allocate and bind the MRTs.
- Set OIT global constants.
- Draw only the `lilToonOIT` pass in the accumulation stage.
- Provide a skybox-inclusive background texture.
- Composite the final result back to camera color.
- Reset global OIT state for every camera.

Keeping this contract small makes future lilToon shader changes easier and keeps
URP renderer-feature code independent from lilToon editor internals.

## Practical Guidance For Future Changes

- Do not globally change lilToon's transparent queue without checking existing
  avatar/toon material behavior.
- Do not draw skybox manually as a substitute for a background texture.
- Do not bind camera depth to scaled OIT RTs unless dimensions and MSAA match.
- Do not leave `_lilOITActive` set after the accumulation pass.
- Use RenderDoc early. The fastest path to the final fix was the D3D output
  merger warning, not shader-side visual guessing.
- Treat generated `Assets/lilToon/Shader/*.shader` files as verification
  output. Long-term edits belong in `.lilinternal`, `.lilblock`, editor binding,
  or HLSL include files.
