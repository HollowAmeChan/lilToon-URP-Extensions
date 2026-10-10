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
Unity ComputeShaders execute the RELAX dispatch sequence; Unity owns GPU resources.

DenseDI uses explicitly derived shader bodies in ../../Shaders/NRD:
HoRTDINrdAtrousSmem.hlsl averages per-pixel temporal variance instead of
including between-pixel illumination/material contrast in noise variance.
HoRTDINrdHistoryClamping.hlsl preserves the second moment when HistoryFix is
disabled: fast specular alpha then contains hit distance, not a moment.
Atrous / AtrousSmem also condition specular spatial weights on borrowed primary
F0. Temporal reduces specular history budgets when the existing DI primary
history's F0 footprint does not match. HoRTDINrdMaterialGuide.hlsl implements
this project-specific material condition; it does not allocate another guide
texture or alter the raw BRDF. Diffuse filtering is not conditioned by this guide.
The remaining code, resource layouts, CPU library and original files in this
directory stay pinned. These are project adaptations, not an upstream NRD
upgrade or a claim that NVIDIA's general-purpose filtering is unmodified.
Original notices and license apply to the derived files as well.
Tests~/RTDI/generate_nrd_variance_adapter.py --check reconstructs the changes
against the original SHA records and verifies exact normalized contents.

The reference fork's GetResourceTypeString lookup retains an older enum order.
Integration uses the numeric ResourceType order in the pinned NRDDescs.h instead.
Version, encoding, struct sizes, pipeline identifiers and shader resources are
checked. Player deployment and broader camera/material support remain separate
validation gates; passing the Editor tests is not a claim of those results.
