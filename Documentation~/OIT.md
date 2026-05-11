# Weighted OIT Plan

The first OIT implementation should follow the roadmap in the lilToon fork:

1. Add lilToon transparent shader pass output for accumulation and revealage.
2. Allocate per-camera accumulation and revealage render targets in URP.
3. Composite the resolved OIT result into the camera color target after transparents.
4. Add material controls for Off / Weighted OIT and weight response.

Initial integration target:

- Unity 2022.3
- URP 14.x
- lilToon 2.3.x fork layout
