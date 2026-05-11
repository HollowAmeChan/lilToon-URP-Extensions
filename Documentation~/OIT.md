# Weighted OIT Plan

The first OIT implementation should follow the roadmap in the lilToon fork:

1. Allocate per-camera accumulation and revealage render targets in URP.
2. Draw transparent renderers with a `LightMode` named `lilToonOIT`.
3. Composite the resolved OIT result into the camera color target after transparents.
4. Add lilToon transparent shader pass output for accumulation and revealage.
5. Add material controls for Off / Weighted OIT and weight response.

Shader passes that want to participate in OIT should use:

```shaderlab
Tags { "LightMode" = "lilToonOIT" }
```

The pass should write MRT output:

- `_lilOITAccumulationTexture`: premultiplied transparent color accumulation plus total weight.
- `_lilOITRevealageTexture`: first-version coverage accumulation. A later true revealage path can switch this to multiplicative remaining visibility.

Package helper include:

```hlsl
#include "Packages/jp.lilxyzw.liltoon.urp.extensions/Runtime/OIT/WeightedOIT.hlsl"
```

Initial integration target:

- Unity 2022.3
- URP 14.x
- lilToon 2.3.x fork layout
