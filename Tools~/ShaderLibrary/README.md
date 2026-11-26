# Pipeline shader-library checks

Run from the pipeline repository root:

~~~sh
python Tools~/ShaderLibrary/check_library.py
python Tools~/ShaderLibrary/check_library.py --hoshader-root ../HoShader
python -m unittest discover -s Tools~/ShaderLibrary/tests -p "test_*.py" -v
~~~

The checker is read-only and has no repair or migration mode. Five pipeline-owned receiver headers live at ShaderLibrary/Shadow/Receiver/, retaining the producer's original floors, tier pragmas and PCF policy; they must not be replaced with material-family dynamic adapters. It requires the twelve pipeline-owned RTGI headers at ShaderLibrary/RTGI/, with no old top-level Runtime/RTGI/Shaders HLSL or orphan metadata. The five top-level compute, raytrace and shader entry assets are explicitly required to keep their Runtime locations.

It scans active Runtime, Editor and Tests/Editor source includes for retired RTGI and Receiver header paths. It resolves the dependency closure rooted at the RTGI Runtime/Editor sources and ShaderLibrary, including same-package Packages/... includes. Missing local includes and package escapes fail. External Unity packages and known engine cginc files are explicit dependency boundaries. Vendored ThirdParty files must exist at the incoming edge, but their conditional SDK/private search-path closure remains the vendor package's responsibility. Comments and Tests~/ historical snapshots are not active source.

All library source and folder assets require valid 32-character GUID metadata. Orphan library metadata fails. Library identities must be unique against other library and active Runtime/Editor/Tests assets. Library HLSL uses canonical LF, avoiding Windows/Linux byte differences.

With --hoshader-root, the pipeline HoBufferEncoding header and the three common Receiver protocol headers (HoShadowCastShaderContract, HoRTShadowSampling, HoRendererIdentity) are compared byte-for-byte with HoShader's corresponding ShaderLibrary~/ authorities. Different Cast sampling and Character PCF implementations are not forced equal. This reads the sibling repository only; installed runtime packages remain independent.

These checks validate source layout, dependency references and asset identities. Unity shader compilation and GPU image/numerical regression remain separate required evidence for runtime changes.

The checked-in contract.json pins canonical LF SHA256 values for all four shared headers. Default checks validate these local bytes even in an isolated pipeline checkout without HoShader. An intentional common-code change must update both authorities/mirrors and the declared contract hashes. The optional sibling check validates its authority hashes against the same contract as well as local byte equality, so updating a local file and its contract alone cannot conceal an unchanged sibling authority.
