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

This package expects Unity 2022.3, URP 14.x, and the lilToon fork in the project. lilToon is treated as a peer requirement for now so local `Assets/lilToon` installs do not break UPM dependency resolution.

## Current Status

The package shell and OIT renderer feature entry point are in place. Rendering passes are intentionally stubbed until the accumulation, revealage, and composite shaders are added.
