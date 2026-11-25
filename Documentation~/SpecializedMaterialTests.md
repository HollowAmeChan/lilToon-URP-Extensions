# Specialized material integration tests

The pipeline's provider-only tests use pipeline-owned Shader probes. They require no Cloth, Hair, Skin or lilToon material package. Tests that draw actual Cloth/Hair materials carry NUnit `Category("MaterialPackageIntegration")`:

- `RTGIMaterialHistoryTests.DedicatedStaticMaterialTracksColorAndTextureTransformChanges` (Cloth/Hair).
- `ScreenProcessRegressionTests.SpecializedOutlineRetainsItsRendererOwnerDuringCameraMotion` (Cloth/Hair, compatibility/RenderGraph, MSAA1/4).
- `ScreenProcessRegressionTests.OverlappingNativeOutlinesInheritTheForwardVisibleCharacter` (Cloth Face and Hair actors).
- `ScreenProcessRegressionTests.MovingStencilCharacterCannotLeaveAnyAcMaskOutsideCurrentForwardSilhouette` (Cloth/Hair, compatibility/RenderGraph, MSAA1/4).

These tests require the dedicated packages and call their public material synchronizers through reflection. Missing dedicated Shaders or synchronization APIs fail the integration test; absence of lilToon does not skip it. Provider-only receiver-mode, capsule overlay and dormant legacy-animation-policy tests remain uncategorized and use the package's own probes. Pipeline-owned `Hidden/lilToon/URP/...` names do not require the old lilToon material package.

## Disposable host

Use a separate Unity6000.3.15f1 project, outside the production project. The verified local host was `D:/Temp/HoPipelineNoLilTests-20261126/Project`; no production scenes, materials or manifests were modified. Set `ProjectSettings/ProjectVersion.txt` to the matching Editor version. Before first import, create `Assets/csc.rsp` containing:

```text
-define:URP_COMPATIBILITY_MODE
```

This enables the compatibility cases as well as RenderGraph cases. A graphics device is required: use D3D11 and omit `-nographics`. The filtered run must contain no skipped tests when asserting coverage of both paths.

Use this `Packages/manifest.json`, adjusting local checkout paths:

```json
{
  "dependencies": {
    "com.unity.render-pipelines.universal": "file:D:/Unity_Fork/HoUrp17.3.0",
    "com.unity.render-pipelines.universal-config": "file:D:/Unity_Fork/HoUrpConfig17.0.3",
    "com.unity.test-framework": "1.6.0",
    "jp.lilxyzw.liltoon.urp.extensions": "file:D:/Unity_Fork/lilToon-URP-Extensions",
    "com.hoshader.cloth-urp": "file:D:/Unity_Fork/HoShader/Cloth_URP",
    "com.hoshader.hair-urp": "file:D:/Unity_Fork/HoShader/Hair_URP",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.particlesystem": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.terrain": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.unitywebrequest": "1.0.0",
    "com.unity.modules.wind": "1.0.0"
  },
  "testables": ["jp.lilxyzw.liltoon.urp.extensions"]
}
```

`testables` imports the pipeline's package test assemblies; Test Framework supplies NUnit and the EditMode runner. Use the matching Ho URP/config checkouts for real pipeline integration. Do not add `jp.lilxyzw.liltoon`. The verified host additionally had `com.hoshader.skin-urp-stylized` installed; these four categorized methods do not use Skin. For a provider-only host, omit Cloth/Hair and select the provider filter below.

## Filters and reproduction

Run the independent provider tests (19 cases in the verified configuration):

```powershell
unity test D:/Temp/HoPipelineNoLilTests-20261126/Project --mode EditMode --filter "HoRPShadowModeTests;HoCapsuleAOIntegrationTests;DormantEmissionAnimationDoesNotInvalidateStaticHistory" --output D:/Temp/HoPipelineNoLilTests-20261126/providers.xml -- -force-d3d11 -testCategory "!MaterialPackageIntegration" -logFile D:/Temp/HoPipelineNoLilTests-20261126/providers.log
```

Run only the actual material integration (20 parameterized cases):

```powershell
unity test D:/Temp/HoPipelineNoLilTests-20261126/Project --mode EditMode --filter "RTGIMaterialHistoryTests;ScreenProcessRegressionTests" --output D:/Temp/HoPipelineNoLilTests-20261126/material-integration.xml -- -force-d3d11 -testCategory MaterialPackageIntegration -logFile D:/Temp/HoPipelineNoLilTests-20261126/material-integration.log
```

Unity intersects test-name and category filters; `!MaterialPackageIntegration` excludes that category. See the [Unity Test Framework command-line reference](https://docs.unity.cn/Packages/com.unity.test-framework@1.3/manual/reference-command-line.html).

The combined changed-case filter used for the verified GPU run is:

```powershell
unity test D:/Temp/HoPipelineNoLilTests-20261126/Project --mode EditMode --filter "HoRPShadowModeTests;HoCapsuleAOIntegrationTests;RTGIMaterialHistoryTests;SpecializedOutlineRetainsItsRendererOwnerDuringCameraMotion;OverlappingNativeOutlinesInheritTheForwardVisibleCharacter;MovingStencilCharacterCannotLeaveAnyAcMaskOutsideCurrentForwardSilhouette" --output D:/Temp/HoPipelineNoLilTests-20261126/active-tests.xml -- -force-d3d11 -logFile D:/Temp/HoPipelineNoLilTests-20261126/active-tests.log
```

That D3D11 run passed39/39 with0fail/0skip, including real outline owner/semantic transfer, overlapping characters, stencil motion, material signatures/ST changes and provider-only behavior. The category attributes and this documentation were added afterward without changing test bodies or rerunning the GPU suite. This result covers the named cases; it is not a claim that every pipeline test or production scene was run. Check XML case counts/results and the resolved package lock before treating a later filtered run as evidence.
