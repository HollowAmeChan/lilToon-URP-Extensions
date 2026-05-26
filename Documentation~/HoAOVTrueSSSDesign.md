# HoAOV / HoSSS 设计现状

> 历史资料：本文记录旧 `HoAOV` / `HoSSS` 数据契约，不作为当前 `MetadataBuffer` / `GeometryBuffer` / `Ho-SubsurfaceScattering` 使用说明。当前边界、用户顺序和验收口径以 `RPComponentRework/RPComponentRework_验收文档.md` 为准。

本文只记录已经落地的 HoSSS 工作、当前数据契约，以及对照 Unity HDRP 17.3 SSS 源码后确认的差距。早期试验过程和按日期堆叠的流水账不再保留。

## 目标边界

HoSSS 的目标是给 lilToon / lilPBR 的不透明与镂空皮肤材质提供屏幕空间次表面散射。它是独立的 URP RendererFeature，不属于 HoPost，也不属于 HoAOV 的调试输出。

HoAOV 负责输出材质级数据；HoSSS 负责读取这些数据、做扩散与合成。当前目标优先服务皮肤观感，不追求玉石、玻璃、厚介质折射或体积路径追踪。

## 当前已落地

### 独立 RendererFeature

已新增并接入：

```text
Runtime/SubsurfaceScattering/HoSubsurfaceScatteringRendererFeature.cs
Runtime/SubsurfaceScattering/HoSubsurfaceScatteringSettings.cs
Runtime/SubsurfaceScattering/HoSubsurfaceScatteringShaderConstants.cs
Runtime/SubsurfaceScattering/HoSubsurfaceScattering.shader
```

当前 pass 链路：

```text
HoSSS Source
HoSSS Diffusion X
HoSSS Diffusion Y
HoSSS Transmission Gather
HoSSS Transmission Blur X
HoSSS Transmission Blur Y
HoSSS Composite
```

当前中间 RT：

```text
_lilHoSSSSourceTexture
_lilHoSSSDiffusedTexture
_lilHoSSSTransmissionTexture
_lilHoSSSTransmissionTempTexture
_lilHoSSSCompositeSourceTexture
```

`Transmission` 已经从 composite 现场计算拆成独立 RT，再做双向 bilateral blur。这样 debug 看到的是 blur 后结果，避免之前单方向 gather 的等高线直接进入合成。

### HoAOV 数据输入

HoSSS 当前依赖 HoAOV 输出：

```text
_lilHoAovMaskIdTexture.r        coverage / subject gate
_lilHoAovNormalDepthTexture.rgb world normal，0..1 编码
_lilHoAovNormalDepthTexture.a   linear eye depth
_lilHoAovSurfaceDataTexture.r   SSS thinness / scattering mask
_lilHoAovSurfaceDataTexture.g   curvature / transmission strength boost
_lilHoAovSurfaceDataTexture.b   SSS profile id / material class
_lilHoAovSurfaceDataTexture.a   transmission radius utility
_lilHoAovSssTexture.rgb         SSS source color
_lilHoAovSssTexture.a           source validity / SSS weight
```

HoAOV RenderGraph 与非 RenderGraph 路径都已经分配并全局绑定 SSS 专用 MRT。`_lilHoAovCustom0_3Texture` 继续作为后处理/材质自定义通道使用，不再挪给 SSS：

```text
SV_Target4 -> _lilHoAovCustom0_3Texture
SV_Target7 -> _lilHoAovSssTexture
```

当前 lilToon HoAOV pass 在启用 `_UseSSS` 时把 `_SSSColor` / albedo 混合结果写入 `_lilHoAovSssTexture`；lilPBR 在启用 Subsurface 时把 `_SubsurfaceColor` 与 albedo blend 后写入同一通道。HoSSS Source pass 会优先读取这个专用通道，未写入时回退 camera color。

### 材质接口

lilToon 已接入 HoSSS 相关材质数据：

```text
_HoSSSProfileId
_HoSSSThicknessScale
_HoSSSTransmissionStrength
_HoSSSTransmissionRadius
```

启用 `_UseSSS` 时：

```text
surfaceData.r = SSS thinness / scattering mask
surfaceData.b = byte encoded _HoSSSProfileId
```

lilPBR 已接入：

```text
_HoSSSProfileId
```

启用 `_SUBSURFACE` 时：

```text
surfaceData.r = Subsurface mask / thickness proxy
surfaceData.b = byte encoded _HoSSSProfileId
```

未启用 SSS / Subsurface 的材质继续使用原来的 material class 语义，避免非皮肤材质被误当作 SSS profile。

### Profile 驱动参数

RendererFeature 已有 8 个固定 profile 槽位。shader 侧通过常量数组读取：

```text
_lilHoSSSProfileIds
_lilHoSSSProfileDiffusionParams
_lilHoSSSProfileTransmissionParams
_lilHoSSSProfileShapeParams
```

每个 profile 当前包含：

```text
enabled
profileId
diffusionColor
diffusionRadius
sourcePreserve
transmissionColor
transmissionStrength
transmissionRadius
thicknessScale
```

已实现 profile-aware diffusion / transmission：

- diffusion radius 来自 profile，而不是只用全局 radius。
- diffusion color 来自 profile。
- transmission strength / radius / color 来自 profile。
- blur 和 transmission gather 使用 profile gate，避免不同 profile 之间串色。
- 半径进入 shader 前做平方响应打包，让小半径区间更容易微调。

### 调试模式

HoSSS 已提供调试输出：

```text
Off
Mask
Source
Diffusion
Transmission
TransmissionGate
CompositeWeight
ProfileId
Thickness
ProfileRadius
TransmissionDirection
TransmissionRim
```

这些模式用于定位材质是否写入 profile、mask 是否为 0、厚度是否有效、半径是否过大、transmission 方向是否异常。

### BIRP 支持移除

lilPBR / shader 侧已经按当前项目方向移除 BIRP 支持，不再维护 `unity_birp.hlsl` 路径。HoSSS 只面向 URP。

## 当前帧顺序

推荐顺序保持为：

```text
HoShadowCast ShadowMap
Opaque / Cutout
HoAOV Output                  // AfterRenderingOpaques
HoSSS Source Capture
HoSSS Diffusion X/Y
HoSSS Transmission Gather
HoSSS Transmission Blur X/Y
HoSSS Composite               // BeforeRenderingTransparents
Transparent / OIT
HoCharacterSpecialization
URP Post Processing
HoPost Stack
Shoost Final Stack
```

HoAOV 必须早于 HoSSS Source。HoSSS Composite 通常应早于透明与 OIT，避免皮肤散射结果被后续透明顺序污染。

## HDRP 17.3 对照

参考源码已拉取到：

```text
D:/Unity_Fork/UnityGraphics-6000.3-HDRP
```

对应项目版本：

```text
Unity 6000.3.15f1
HDRP package 17.3.0
```

关键源码：

```text
Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/HDRenderPipeline.SubsurfaceScattering.cs
Packages/com.unity.render-pipelines.high-definition/Runtime/Material/SubsurfaceScattering/SubsurfaceScattering.compute
Packages/com.unity.render-pipelines.high-definition/Runtime/Material/SubsurfaceScattering/SubsurfaceScattering.hlsl
Packages/com.unity.render-pipelines.high-definition/Runtime/Material/SubsurfaceScattering/CombineLighting.shader
Packages/com.unity.render-pipelines.high-definition/Runtime/Material/DiffusionProfile/DiffusionProfileSettings.cs
Packages/com.unity.render-pipelines.high-definition/Runtime/Material/DiffusionProfile/DiffusionProfile.hlsl
Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForward.hlsl
```

HDRP 的主要结构：

1. Diffusion Profile 不是简单颜色参数，而是资产/Volume 驱动的 profile 列表。
2. `HDRenderPipeline.SubsurfaceScattering.cs` 每帧把 profile 转成常量数组，例如 shape、max scatter distance、transmission tint、world scale、filter radius、thickness remap、dual lobe、border attenuation。
3. HDRP 使用 `SSSBuffer` 存储 albedo + SSS profile/mask 数据。
4. `SubsurfaceScattering.compute` 读取 diffuse lighting source、depth、SSSBuffer、profile index，执行屏幕空间 Burley diffusion。
5. compute 里有 profile index packing、sample budget、downsample、random rotation、LDS cache、bilateral depth weight、profile gate。
6. `CombineLighting.shader` 负责把过滤后的 diffuse lighting 重新合成回最终 lighting/color。
7. Transmission 和 SSS 同属 Diffusion Profile 体系，但 transmission 不是靠当前 HoSSS 这种任意屏幕方向投射来模拟。

HDRP diffusion 核心来自 Burley profile：

```text
EvalBurleyDiffusionProfile()
SampleBurleyDiffusionProfile()
ComputeBilateralWeight()
```

这和 HoSSS 当前的简化 gaussian-like separable blur 有本质差距。

## HoSSS 与 HDRP 的差距

### 已接近的部分

```text
独立 SSS RendererFeature
屏幕空间 source / diffusion / composite
材质 mask / normal / depth / profile id 输入
profile-driven 参数
debug view
中间 RT 拆分
```

这些方向与 HDRP 是一致的：SSS 不应该只是材质里的一段 fake lighting，也不应该塞进普通后处理 filter。

### 仍明显缺失的部分

```text
Burley / Disney diffusion kernel
真实 Diffusion Profile asset 或等价 profile 数据结构
lighting source 与 camera color 分离
SSSBuffer 等价 MRT 数据
profile index packing / stable profile lookup
sample budget 与 downsample quality path
random rotation + TAA 友好的采样
与主光、阴影、occlusion 更明确的 transmission 关系
backface thickness 或更稳定的 thickness proxy
```

当前最大问题不是“参数不够多”，而是 source 与 kernel 仍不够像 HDRP：

- Source 仍主要来自 camera color，包含高光、阴影、后续合成影响，不是干净的 diffuse lighting / irradiance。
- Diffusion kernel 还是简化 separable blur，不是按 profile 半径采样 Burley distribution。
- Directional transmission 的艺术化 gather 容易退化成 rim / tint，视觉收益不稳定。

## 下一步实现方向

### 1. 用 HoAOV SSS 专用 MRT 作为 source

已落地。HoAOV 新增专用 SSS MRT：

```text
_lilHoAovSssTexture.rgb = SSS source color
_lilHoAovSssTexture.a   = source validity / SSS weight
```

优先级：

```text
if sss.a > 0:
    source = lerp(cameraColor, sss.rgb, sss.a)
else:
    source = camera color fallback
```

这样可以减少从屏幕投射里“猜颜色”，也更接近 HDRP 的 SSSBuffer / lighting source 思路。Custom0 保持给后处理链路，不再承担 SSS 数据契约。

### 2. 替换 diffusion kernel

当前 `HoSSSProfileWeight()` 应替换为 HDRP 对照的 Burley profile 采样逻辑：

```text
SampleBurleyDiffusionProfile()
EvalBurleyDiffusionProfile()
ComputeBilateralWeight()
```

第一步不必照搬 compute shader，可以先在现有 fullscreen shader 里实现固定 tap 的 Burley-like disk sampling。目标是先消除当前半径敏感、等高线、单方向感强的问题。

### 3. 降低 transmission gather 权重

现有 transmission gather 可以保留为艺术增强，但不应作为主 SSS 质量来源。皮肤的主要柔和感应该来自 profile diffusion；transmission 只负责耳缘、鼻翼、指尖、薄处的暖色边缘补偿。

推荐默认：

```text
transmissionStrength 低于 diffusion strength
transmissionRadius 小于 diffusion radius
transmissionBlendMode 使用 SoftTint
transmission debug 只作为诊断，不作为主外观判断
```

### 4. 压缩 UI 参数

当前 HoSSS 参数过多，调参难。应把大部分参数收进 profile preset，RendererFeature 只保留：

```text
enabled
renderScale
masterStrength
quality
debugMode
profiles
renderInSceneView
```

当前 RendererFeature inspector 已按这个方向收敛：主入口分为运行、Diffusion Profiles、调试；未命中 profile 的全局 radius/color/sourcePreserve、depth/normal gate、pass event、shader override 和 transmission 补偿都放在“高级/兼容”。`quality` 直接控制 Burley disk gather 的采样预算，当前为 Low 8 / Medium 16 / High 24 taps。Transmission 的细节参数保留在高级区，默认 profile 给出皮肤可用值。

### 5. 后续 thickness

短期继续使用：

```text
surfaceData.r
depth gate
normal gate
view/rim factor
```

质量版再加入 backface thickness prepass：

```text
frontDepth = HoAOV / camera depth
backDepth  = SSS object backface depth
thickness  = max(0, backDepth - frontDepth)
```

这不是当前皮肤版的前置条件，但会明显改善耳朵、手指、鼻翼等薄处。

## 2026-05-22 落地调整

已在现有 fullscreen shader / pass 链路内先推进一版 HDRP 对齐：

- Diffusion 主结果从双向 separable blur 改为 16 tap Burley-like disk gather。横向 pass 保留为轻量预滤波，纵向 pass 输出最终 profile diffusion。
- RendererFeature 新增 quality/sample budget 主参数，并重做 inspector：普通调节聚焦运行状态、renderScale、quality、master strength 和 Diffusion Profiles。
- Burley 采样参考 HDRP 的 `SampleBurleyDiffusionProfile()` / `EvalBurleyDiffusionProfile()`，继续使用 HoAOV depth、normal、profile id 做 bilateral / profile gate。
- Transmission 保留为辅助暖边，不再作为主 SSS 观感来源；默认强度和半径已下调，合成阶段也降低了 transmission 注入权重。

仍未落地的 HDRP 差距：真实 DiffusionProfile asset / Volume 驱动、compute path、sample budget 质量档、随机旋转与 TAA 的完整配合，以及从 diffuse lighting / irradiance 分离出的更干净 source。

## 当前结论

HoSSS 已经从“普通后处理 blur”推进到“HoAOV 数据驱动的独立屏幕空间 SSS 框架”。SSS source 已经从 camera color fallback 前进到 HoAOV 专用 MRT；但它离 HDRP 17.3 的关键差距仍在 kernel 和 lighting source：HDRP 的质量来自 Diffusion Profile + SSSBuffer + Burley diffusion compute，而不是方向性投射。

后续应优先把 diffusion profile 数据结构和 quality / sampleBudget 接口补齐，并继续把参数入口收敛到 profile 风格。继续堆 transmission 投射参数的收益有限，且容易让效果看起来像普通 rim tint。
