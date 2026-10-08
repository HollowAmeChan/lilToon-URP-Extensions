# Reference code and licenses

This directory is a DI shader dependency subset copied from the user's local
`UnityPathTracing-6000.3.15/Assets/Shaders` reference project. The adjacent fork
is https://github.com/Kuan-Mi/UnityPathTracing at local commit
`eb9ee9276390412d5791a4ec39212bf4039df8a0`.

`provenance.json` records each original file's SHA-256. Only include paths were
rewritten in the copied SDK/Donut files; resampling algorithms were not rewritten.
The subset includes DI initial sampling, reservoir/storage, temporal/spatial
resampling, random sampling and their transitive shader headers. ReGIR headers
are compile dependencies; runtime ReGIR and presampling are disabled. No PT,
DLSS, NRD, native rendering plugin or GI implementation is included in this subset.

- `RTXDI/` retains NVIDIA's copyright/SPDX notices and is governed by the
  bundled `RTXDI/LICENSE.txt` (copied from the reference's RTXDI-Library).
- `donut/` retains the MIT license text embedded in its shader headers.
- Application adaptations in `../Shaders/HoRTDIApplicationBridge.hlsl`,
  `../Shaders/HoRTDI.compute` and the neighbor-offset generation in
  `../HoRTDIContext.cs` derive from the reference RAB bridge, DI passes,
  ShadingHelpers/PolymorphicLight, and RtxdiUtils. NVIDIA portions retain their
  applicable NVIDIA license; they are not relicensed by the package's MIT label.

Host scheduling, URP/Ho bindings, feature UI and validation are integration code.
The four-pass flow follows the reference UnityRtxdi implementation; bridge
structures and enabled light types are application-specific as prescribed by RAB.
