# Planar Reflection

`LILPlanarReflectionSurface` renders a mirrored camera for flat reflective surfaces and feeds the result to renderers through a material property block.

## Setup

1. Add `LILPlanarReflectionSurface` to the flat reflective mesh.
2. Use a material that samples `_LILPBRPlanarReflectionTexture`, such as the modified lilPBR shader.
3. Tune the material's `Planar Reflection` strength, tint, smoothness threshold, edge fade, and distance fade.
4. Set the component's `Reflection Mask` to the layers that should appear in the reflection.

The reflection plane uses the GameObject transform:

- position: `transform.position`
- normal: `transform.up`

## Notes

- The surface renderer is hidden while its reflection is being rendered to avoid recursive self-reflection.
- The reflection texture and projection matrix are written with a `MaterialPropertyBlock`, so each surface can have its own reflection.
- By default the component also enables lilPBR's `_UsePlanarReflection` property on that renderer through the same property block.
- The reflected camera uses the source camera projection, including FOV/focal length, and only applies `Clip Plane Offset` to the oblique clipping plane.
- lilPBR samples the planar reflection with the current camera screen UV, which is the stable mapping for mirrored cameras with matching projection.
- In lilPBR, `Fade Start` and `Fade End` provide camera-distance fade. Leave both at `0` to disable distance fade.
- This path is intended for flat mirrors, polished floors, water sheets, and similar planar surfaces.
- It is more expensive than screen-space reflection because it renders the scene again.
