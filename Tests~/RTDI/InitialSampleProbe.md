Copy `HoInitialSampleProbe.cs` and `HoInitialSampleProbe.compute` into
`Assets/Editor/` of the isolated `.codex-research/RTInputValidation` project.
Launch a separate Unity 6000.3.15f1 batch process with `-force-d3d12` and
`-executeMethod HoInitialSampleProbe.Run`.

The probe uses two synthetic analytic lights, 4096 receivers and the pinned
SDK. It compares the SDK-returned sample position with the sample reconstructed
from the final reservoir's light index/UV. It does not trace scene geometry or
run a production renderer feature. All reservoirs must be valid before the
diagnostic can be interpreted. Exit zero means observation completed, not that
the SDK output contract passed.

Observed on this host: one candidate produced 0/4096 mismatches; eight produced
988/4096 mismatches. The precise count may differ with compiler/configuration.
HoRTDI initial visibility uses the reconstructed selected sample and preserves
the SDK's invisible-sample discard/M policy. This does not modify vendor code
or claim to identify a general compiler bug.

Scene regression:
`lilToon.URP.Extensions.Editor.RTDI.HoRTDIValidation.RunLocalVisibilityAudit`
isolates near-only, far-only, mixed one/eight candidates against independent
segment/AABB visibility. `RunLocalPairShadow` separately checks raw and filtered
quality in all four reuse modes; its temporal-only filtered highlight gate
currently fails, while raw visibility/energy passes. Win64 Player deployment
also checks the visible far lamp with eight candidates and no reuse.
