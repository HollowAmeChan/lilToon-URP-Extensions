# Pinned NRD source and CPU library

NRD 4.17.3 is sourced from the user's supplied UnityPathTracing reference:

- RenderingPlugin/External/NRD/Shaders and Include in the adjacent UnityPathTracing fork.
- NRDConfig.hlsli and ml.hlsli from its UnityProject/Assets/Shaders deployment.
- NRD.dll from UnityPathTracing-6000.3.15/Assets/Plugins/x86_64, renamed HoRTDINRD.dll to avoid generic plugin-name collisions.

Original copyright notices and NRD LICENSE.txt are retained. NRD and MathLib
portions are not relicensed by the surrounding package's MIT label.
The supplied MathLib file retains its NVIDIA copyright notice and source provenance.

provenance.json records hashes of all 36 original shader/header files and the
original CPU library. Source bodies are unchanged (UTF-8 BOM / line endings may
be normalized). Unity entry-point, resource/constant binding and inline-sampler
wrappers are separate in ../../Shaders/NRD. The generated C# manifest is derived
from the pinned instance shader identifiers and resource headers, not from an
independently rewritten RELAX algorithm.

Only the core CPU scheduler is used. No Denoiser.dll, NRI.dll, D3D12HeapHook,
DLSS/NIS plugin or Unity native command-list/descriptor-heap hook is imported.
Unity ComputeShaders execute the original RELAX passes; Unity owns GPU resources.

The reference fork's GetResourceTypeString lookup retains an older enum order.
Integration uses the numeric ResourceType order in the pinned NRDDescs.h instead.
Version, encoding, struct sizes, pipeline identifiers and shader resources are
checked. Player deployment and broader camera/material support remain separate
validation gates; passing the Editor tests is not a claim of those results.
