# MSAA / Coverage — verified citations

Every URL below was fetched successfully with `web_fetch` (HTTP 200, real content). Items marked
**UNVERIFIED** are explicitly flagged and were *not* confirmed.

Fetched: 2026 (Unity docs builds dated 2026-09-13; Microsoft Learn "last updated" dates given per page).

---

## 1. Alpha-to-coverage / alpha-to-mask

### 1.1 Unity — AlphaToMask command in ShaderLab reference
- URL: https://docs.unity3d.com/Manual/SL-AlphaToMask.html
  (canonical: https://docs.unity3d.com/6000.6/Documentation/Manual/SL-AlphaToMask.html)
- Org: Unity Technologies · Unity 6.6 (6000.6) · page canonicalises to 6000.6
- Claims supported:
  - "Enables or disables alpha-to-coverage mode on the GPU."
  - Signature `AlphaToMask <state>` with `On` / `Off`; *"This command makes a change to the render
    state. Use it in a `Pass` block to set the render state for that Pass, or use it in a `SubShader`
    block to set the render state for all Passes in that SubShader."*
  - Compatibility table lists **AlphaToMask = Yes** for URP, HDRP, Custom SRP and Built-in.

### 1.2 Unity — Reduce aliasing with AlphaToMask mode
- URL: https://docs.unity3d.com/6000.0/Documentation/Manual/writing-shader-alpha-to-mask.html
- Org: Unity Technologies · Unity 6.0 (6000.0)
- Claims supported (the MSAA-requirement claim, verbatim):
  - "Alpha-to-coverage mode can reduce the excessive aliasing that occurs when you use multisample
    anti-aliasing (MSAA) with shaders that use alpha testing, such as vegetation shaders. To do this,
    it modifies the multisample coverage mask proportionally to the alpha value in the output of the
    fragment shader result."
  - "This command is intended for use with MSAA. If you enable alpha-to-coverage mode when you are
    not using MSAA, the results can be unpredictable; different graphics APIs and GPUs handle this
    differently."

---

## 2. MSAA sample counts, sample positions, coverage, per-sample reads

### 2.1 Microsoft — Configuring Blending Functionality (Alpha-To-Coverage)
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-blend-state
- Org: Microsoft (Win32 / Direct3D 11 programming guide) · last updated 2020-08-19
- Claims supported:
  - "Blending operations are performed on every pixel shader output (RGBA value) before the output
    value is written to a render target. If multisampling is enabled, blending is done on each
    multisample; otherwise, blending is performed on each pixel."
  - "Alpha-to-coverage is a multisampling technique that is most useful for situations such as dense
    foliage where there are several overlapping polygons that use alpha transparency to define edges
    within the surface."
  - "You can use the **AlphaToCoverageEnable** member of D3D11_BLEND_DESC1 or D3D11_BLEND_DESC to
    toggle whether the runtime converts the .a component (alpha) of output register SV_Target0 from
    the pixel shader to an n-step coverage mask (given an n-sample RenderTarget). The runtime performs
    an **AND** operation of this mask with the typical sample coverage for the pixel in the primitive
    (in addition to the sample mask) to determine which samples to update in all the active
    RenderTargets."
  - "If the pixel shader outputs SV_Coverage, the runtime disables alpha-to-coverage."
  - "In multisampling, the runtime shares only one coverage for all RenderTargets."
  - "Graphics hardware doesn't precisely specify exactly how it converts pixel shader SV_Target0.a
    (alpha) to a coverage mask, except that alpha of 0 (or less) must map to no coverage and alpha of
    1 (or greater) must map to full coverage ... As alpha goes from 0 to 1, the resulting coverage
    should generally increase monotonically. However, hardware might perform area dithering..."
  - "Alpha-to-coverage is also traditionally used for screen-door transparency or defining detailed
    silhouettes for otherwise opaque sprites."

### 2.2 Microsoft — Rasterization Rules (MSAA section)
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules
- Org: Microsoft · last updated 2026-09-08
- Claims supported:
  - "Multisample antialiasing (MSAA) reduces geometry aliasing using pixel coverage and
    depth-stencil tests at multiple sub-sample locations. ... Multisample antialiasing does not reduce
    surface aliasing. **Sample locations and reconstruction functions are dependent on the hardware
    implementation.**"
  - "For a triangle, a coverage test is performed for each sample location (not for a pixel center).
    If more than one sample location is covered, a pixel shader runs once with attributes interpolated
    at the pixel center. The result is stored (replicated) for each covered sample location in the
    pixel that passes the depth/stencil test."
  - "The number of sample locations is dependent on the multisample mode."
  - "Multisampling formats can be used in render targets which can be read back into shaders using
    load, since no resolve is required for individual samples accessed by the shader. Depth formats
    are not supported for multisample resource..."
  - "some formats can be resolved (ResolveSubresource; which downsamples a multisampled format to a
    sample size of 1)."
  - Centroid sampling: "A sample mask (specified by the rasterizer state) is applied prior to centroid
    computation."

### 2.3 Microsoft — Getting Started with the Rasterizer Stage (Multisampling)
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-getting-started
- Org: Microsoft · last updated 2022-02-28
- Claims supported:
  - "Multisampling samples some or all of the components of an image at a higher resolution (followed
    by downsampling to the original resolution)... Even though multisampling requires sub-pixel
    samples, modern GPU's implement multisampling so that a pixel shader runs once per pixel."
  - "To use multisampling, set the enable field in the rasterization description, create a multisampled
    render target, and either read the render target with a shader to resolve the samples into a
    single pixel color or call ID3D11DeviceContext::ResolveSubresource to resolve the samples using
    the video card."
  - "When multisampling is enabled, depth is interpolated per sample and the depth/stencil test is done
    per sample; the pixel shader output color is duplicated for all passing samples."
  - "Multisampling is independent of whether or not a sample mask is used, alpha-to-coverage is
    enabled, or stencil operations (which are always performed per-sample)."

### 2.4 Microsoft — DXGI_SAMPLE_DESC structure (dxgicommon.h)
- URL: https://learn.microsoft.com/en-us/windows/win32/api/dxgicommon/ns-dxgicommon-dxgi_sample_desc
- Org: Microsoft · last updated 2024-02-22
- Claims supported:
  - "Describes multi-sampling parameters for a resource."
  - `Count`: "The number of multisamples per pixel." (`Quality`: "The image quality level. The higher
    the quality, the lower the performance.")
  - "The default sampler mode, with no anti-aliasing, has a count of 1 and a quality level of 0."
  - "If multi-sample antialiasing is being used, all bound render targets and depth buffers must have
    the same sample counts and quality levels."

### 2.5 Microsoft — GetRenderTargetSampleCount (DirectX HLSL intrinsic)
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/dx-graphics-hlsl-getrendertargetsamplecount
- Org: Microsoft · last updated 2019-10-24
- Claims supported:
  - "Gets the number of samples for a render target." Signature `UINT GetRenderTargetSampleCount()`.
  - "Use this function and **GetRenderTargetSamplePosition** to find out the number and position of
    the sampling locations for a render target."
  - Supported in Shader Model 4 and higher (not SM1/2/3).

### 2.6 Microsoft — Texture2DMS
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/sm5-object-texture2dms
- Org: Microsoft · last updated 2019-10-24
- Claims supported:
  - "Texture2DMS type (as it exists in Shader Model 4) plus resource variables."
  - Members: `GetDimensions`, `GetSamplePosition` ("Returns the sample position for the sample index
    provided"), `Load methods` ("Retrieves a value from the resource at the location and sample index
    provided"), `sample.Operator[][]`, `Operator[]`.
  - "This object is supported in ... Shader model 4 and higher".

### 2.7 Microsoft — Texture2DMS::Load methods
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/texture2dms-load
- Org: Microsoft · last updated 2021-03-09
- Claims supported:
  - "Retrieves a value from the resource at the location and sample index provided."
  - Overloads: `Load(int,int)`, `Load(int,int,int)`, `Load(int,int,int,uint)` (returns status).

### 2.8 Microsoft — EvaluateAttributeAtSample function
- URL: https://learn.microsoft.com/en-us/windows/win32/direct3dhlsl/evaluateattributeatsample
- Org: Microsoft · last updated 2019-10-24
- Claims supported:
  - "Evaluates at the indexed sample location."
    `numeric EvaluateAttributeAtSample(in attrib numeric value, in uint sampleindex);`
  - "Interpolation mode can be **linear** or **linear_no_perspective** on the variable. Use of
    **centroid** or **sample** is ignored. Attributes with constant interpolation are also allowed, in
    which case the sample index is ignored."
  - "Supported in Shader Model 5 and higher"; pixel shader only.

---

## 3. Sample-resolution vs pixel-resolution data

### 3.1 Microsoft — ID3D11DeviceContext::ResolveSubresource
- URL: https://learn.microsoft.com/en-us/windows/win32/api/d3d11/nf-d3d11-id3d11devicecontext-resolvesubresource
- Org: Microsoft · last updated 2021-10-13
- Claims supported:
  - "Copy a multisampled resource into a non-multisampled resource."
  - `pDstResource`: "Destination resource. Must be a created with the D3D11_USAGE_DEFAULT flag and be
    **single-sampled**."
  - `pSrcResource`: "Source resource. **Must be multisampled**."
  - → A resolve yields a single-sample resource: per-sample identity is not retained by the API.

### 3.2 Vulkan-Samples (GPUOpen-LibrariesAndSDKs) — MSAA (Multisample anti-aliasing)
- URL: https://gpuopen-librariesandsdks.github.io/Vulkan-Samples/samples/performance/msaa/
- Org: Vulkan-Samples, "maintained by GPUOpen-LibrariesAndSDKs" (based on Khronos Vulkan-Samples) · n.d.
- Claims supported (explicit averaging):
  - "With multisample anti-aliasing, more than one location is tested within a pixel. ... This
    effectively increases the resolution of each pixel, by storing a color value for each sample. The
    fragment shader is still evaluated only once (using the centre coordinate) and the color result is
    stored as the value of those samples that lie within the primitive ... In other words, the fragment
    shader value will be blended to all samples with coverage. **The final value for the pixel is
    calculated as the average of all samples. This is known as the resolving step.**"
  - "the hardware can resolve (average the samples of) the multisampled attachment as the image is
    written back to main memory."
  - "Note that MSAA has no effect for pixels within the primitive, where all samples store the same
    color value."
  - "In contrast to color, Vulkan does not offer an alternative way to resolve depth attachments
    (`vkCmdResolveImage` does not support depth)."
  - Depth resolve modes enumerated: `VK_RESOLVE_MODE_SAMPLE_ZERO_BIT`,
    `VK_RESOLVE_MODE_AVERAGE_BIT`, `VK_RESOLVE_MODE_MIN_BIT`, `VK_RESOLVE_MODE_MAX_BIT`.

### 3.3 Microsoft — Rasterization Rules, per-sample read-back caveat
- URL: (same as 2.2) https://learn.microsoft.com/en-us/windows/win32/direct3d11/d3d10-graphics-programming-guide-rasterizer-stage-rules
- Claim supported: per-sample data is only readable *without* resolving — "Multisampling formats can
  be used in render targets which can be read back into shaders using load, since no resolve is
  required for individual samples accessed by the shader." Depth formats "are not supported for
  multisample resource".

---

## 4. Unity specifics

### 4.1 SystemInfo.GetRenderTextureSupportedMSAASampleCount
- URL: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SystemInfo.GetRenderTextureSupportedMSAASampleCount.html
- Org: Unity Technologies · Unity 6.0 (6000.0)
- Claims supported:
  - `public static int GetRenderTextureSupportedMSAASampleCount(RenderTextureDescriptor desc);`
  - "Checks if the target platform supports the MSAA samples count in the RenderTextureDescriptor argument."
  - Returns: "If the target platform supports the given MSAA samples count of RenderTextureDescriptor,
    returns the given MSAA samples count. Otherwise returns a lower fallback MSAA samples count value
    that the target platform supports."

### 4.2 RenderTextureDescriptor.bindMS
- URL: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTextureDescriptor-bindMS.html
- Org: Unity Technologies · Unity 6.0
- Claim supported: "If true and msaaSamples is greater than 1, the render texture will not be resolved
  by default. Use this if the render texture needs to be bound as a multisampled texture in a shader."

### 4.3 RenderTexture.bindTextureMS
- URL: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/RenderTexture-bindTextureMS.html
- Org: Unity Technologies · Unity 6.0
- Claim supported: "If true and antiAliasing is greater than 1, the render texture will not be resolved
  by default. Use this if the render texture needs to be bound as a multisampled texture in a shader."

### 4.4 Renderer.renderingLayerMask
- URL: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer-renderingLayerMask.html
- Org: Unity Technologies · Unity 6.0
- Claims supported: `public uint renderingLayerMask;` — "Determines which rendering layer this
  renderer lives on, if you use a scriptable render pipeline." (Links out to the URP and HDRP
  Rendering Layers pages.)

### 4.5 RenderingLayerMask struct
- URL: https://docs.unity3d.com/6000.1/Documentation/ScriptReference/RenderingLayerMask.html
- Org: Unity Technologies · Unity 6.1 (6000.1) · "struct in UnityEngine", implemented in UnityEngine.CoreModule
- Claims supported:
  - "The Render Pipeline allows you to use Rendering Layers, which are LayerMasks to make Lights or
    effects only affect specific Renderers."
  - "Rendering Layers are a Bitmask and it represents the 32 Layers and define them as true or false.
    Each bitmask describes whether the RenderingLayer is used."
  - "Rendering Layers are also supported on decal projectors, and can be sampled from the ShaderGraph
    to implement custom effects."
  - Static helpers: `GetMask`, `NameToRenderingLayer`, `RenderingLayerToName`,
    `GetDefinedRenderingLayerCount`, `defaultRenderingLayerMask`; implicit `uint` ↔ `RenderingLayerMask`.

### 4.6 URP — Rendering Layers in URP (landing page)
- URL: https://docs.unity3d.com/6000.0/Documentation/Manual/urp/features/rendering-layers.html
- Org: Unity Technologies · Unity 6.0 Manual
- Note: the package URL `.../com.unity.render-pipelines.universal@17.0/manual/features/rendering-layers.html`
  answers with a cross-origin redirect to `http://docs.unity3d.com` and could **not** be fetched
  directly; the URP 17 Rendering Layers content now lives in the main Manual at the URL above
  (title "Rendering Layers in URP").
- Claims supported: page exists and covers "configuring certain Lights or Decals to affect only
  specific GameObjects in the Universal Render Pipeline (URP)"; sub-pages: Introduction, Enable
  Rendering Layers for Lights, Enable Rendering Layers for Decals.

### 4.7 URP — Introduction to Rendering Layers in URP
- URL: https://docs.unity3d.com/6000.0/Documentation/Manual/urp/features/rendering-layers-introduction.html
- Org: Unity Technologies · Unity 6.0 Manual
- Claims supported:
  - "The Rendering Layers feature lets you configure certain Lights to affect only specific GameObjects."
  - "Rendering Layers are labels you assign to Renderer components... Rendering Layer Masks are
    filters you set on components and render passes that affect or query those Renderers, for example,
    Lights, Decal Projectors, shadows, or custom render passes. A component or render pass affects a
    GameObject when its Renderer's Rendering Layers share at least one layer with that component or
    pass's Rendering Layer Mask."
  - "Performance impact increases more significantly when the number of Rendering Layers reaches 9, 17,
    25, etc. This is because when the Rendering Layers exceed a multiple of 8, URP adds an extra
    texture channel the GPU must access."

### 4.8 HDRP — Use light rendering layers
- URL: https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Rendering-Layers.html
- Org: Unity Technologies · HDRP 17.0.4
- Claims supported (both requested points, verbatim):
  - "The High Definition Render Pipeline (HDRP) allows you to use Rendering Layers, which are LayerMasks
    to make Lights or effects only affect specific Renderers."
  - "A Renderer can support up to 32 rendering layers, but **all HDRP effects using Rendering Layers
    only support the first 16 layers.**"
  - Per-pixel buffer: "To access the Rendering Layer Mask buffer from the ShaderGraph, go to the
    Lighting section of your Project's HDRP Asset and enable the **Rendering Layer Mask Buffer**
    checkbox. You can then use the **HD Sample Buffer** node and set **RenderingLayerMask** as the
    source buffer to sample the layer mask buffer per pixel."
  - "Rendering Layers are also supported on decal projectors, and can be sampled from the ShaderGraph
    to implement custom effects."

### 4.9 Unity Manual — Introduction to RSUV
- URL: https://docs.unity3d.com/6000.6/Documentation/Manual/renderer-shader-user-value-intro.html
- Org: Unity Technologies · Unity 6.6 Manual
- Claims supported:
  - "Use the Renderer Shader User Value (RSUV) when managing many objects ... RSUV lets you specify
    per-renderer data to apply shader customizations while sharing a single material across multiple
    renderers."
  - "RSUV is a custom 32-bit integer value that you can assign to each renderer to provide specific
    data to the shader."
  - **Supported renderer types (exact list):** Mesh Renderer (`MeshRenderer.SetShaderUserValue(uint)`),
    Skinned Mesh Renderer (`SkinnedMeshRenderer.SetShaderUserValue`), Sprite Renderer
    (`SpriteRendererDataAccessExtensions.SetShaderUserValue`), Sprite Shape Renderer
    (`SpriteShapeRenderer.SetShaderUserValue`), Tilemap Renderer (`TilemapRenderer.SetShaderUserValue`).
  - **Batching claim:** "You can access the value within shader HLSL code through the
    `unity_RendererUserValue` property. **This functionality doesn't interfere with batching.**"
  - Scope limit: "**Note**: RSUV is only available in Scriptable Render Pipelines (SRPs). It is not
    supported in the Built-In Render Pipeline."
  - Performance: "The RSUV feature introduces no additional CPU overhead. Using RSUV to customize
    values per renderer is always slightly faster than duplicating a material and significantly faster
    than using Material Property Block (MPB)." / "The performance gain is maximum when using GPU
    Resident Drawer (GRD), as RSUV doesn't require any material duplication, which avoids breaking GRD
    instancing draw calls."

### 4.10 Unity Manual — Set and use the RSUV
- URL: https://docs.unity3d.com/6000.6/Documentation/Manual/renderer-shader-user-value-set-and-use.html
- Org: Unity Technologies · Unity 6.6 Manual
- Claims supported:
  - Setting: `meshRenderer.SetShaderUserValue(cc);` with a 24-bit packed colour example.
  - Reading in HLSL: `uint c = unity_RendererUserValue;` then unpacking with shifts/masks in the
    fragment shader.
  - Shader Graph path via a reflected custom HLSL node using `unity_RendererUserValue`; also usable
    with Entities Graphics via `[MaterialProperty("unity_RendererUserValuesPropertyEntry")]`.

### 4.11 MeshRenderer.SetShaderUserValue (Scripting API)
- URL: https://docs.unity3d.com/6000.4/Documentation/ScriptReference/MeshRenderer.SetShaderUserValue.html
- Org: Unity Technologies · Unity 6.4 (6000.4)
- Claims supported:
  - `public void SetShaderUserValue(uint v);` — "The integer to assign to the renderer as a custom
    value to be used in shaders." / "Assign a custom value to this renderer."
  - "You can then access the value in your shaders as the `unity_RendererUserValue` variable. You can
    use this method to change how a shader draws each renderer, without using different materials or
    additional CPU time."
  - "**Note**: The value of the `unity_RendererUserValue` shader variable will always be 0 in the
    following situations: The shader is written for the Built-In Render Pipeline. When baking
    lightmaps or light probes."
  - "**Note**: The value is not serialized, so it is not saved to the asset and resets when the object
    is reloaded."
  - URP sample shader `Example/URPUnlitUserValue` demonstrating `unity_RendererUserValue` unpacking.

### 4.12 `Renderer.SetShaderUserValue` — **UNVERIFIED / likely does not exist under that name**
- Tried and got HTTP 404 (page-not-found) on all of:
  - https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Renderer.SetShaderUserValue.html
  - https://docs.unity3d.com/6000.2/Documentation/ScriptReference/Renderer.SetShaderUserValue.html
  - https://docs.unity3d.com/6000.4/Documentation/ScriptReference/Renderer.SetShaderUserValue.html
- The official Manual links the API per concrete renderer type (see 4.9/4.11), i.e. the documented
  entry points are `MeshRenderer` / `SkinnedMeshRenderer` / `SpriteRendererDataAccessExtensions` /
  `SpriteShapeRenderer` / `TilemapRenderer` — **not** a method on the base `Renderer` class.
