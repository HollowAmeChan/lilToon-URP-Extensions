# lilToon URP Extensions

URP renderer extensions for lilToon experiments and production-facing quality work.

The first milestone is Weighted Blended OIT for transparent lilToon materials:

- a URP `ScriptableRendererFeature` entry point,
- accumulation and revealage render targets,
- final camera color composite,
- material/shader integration points for lilToon transparent passes.

## Installation

Add this folder as a local Unity Package Manager package.

```json
{
  "dependencies": {
    "jp.lilxyzw.liltoon.urp.extensions": "file:../lilToon-URP-Extensions"
  }
}
```

This package expects Unity 6000.0, URP 17.3.0, and the lilToon fork in the project. lilToon is treated as a peer requirement for now so local `Assets/lilToon` installs do not break UPM dependency resolution.

## Current Status

Weighted OIT is implemented as a production-facing first milestone:

- `WeightedOITRendererFeature` allocates accumulation and revealage render targets.
- The opaque camera color is copied after skybox and exposed as `_lilOITOpaqueTexture` and `_CameraOpaqueTexture`.
- The accumulation pass draws only shader passes tagged `LightMode = "lilToonOIT"`.
- The composite pass blends accumulation and revealage back into the camera color target.
- `_lilOITActive` is reset per camera and only enabled while the accumulation pass is drawing.
- RenderGraph and non-RenderGraph paths are both present.

The matching lilToon fork supplies `_lilOITEnabled`, `LILTOON_OIT` passes, and `lil_oit.hlsl`.

See `Documentation~/OIT.md` for implementation notes, debugging steps, and known edge cases around skybox backgrounds, render scale, MSAA, and Scene view.

## Planar Reflection

`LILPlanarReflectionSurface` is a shared planar reflection runtime for flat mirrors, polished floors, and water-like sheets. Add it to the reflective mesh, then use a shader/material that samples `_LILPBRPlanarReflectionTexture`.

The modified lilPBR shader already has a `Planar Reflection` foldout that consumes this texture through per-renderer material property blocks.

See `Documentation~/PlanarReflection.md` for setup notes.
