# HoAOV 与真 SSS 设计说明

这份文档记录从当前材质内 Fake SSS 走向 URP 屏幕空间真 SSS 的路线。结论先写清楚：SSS 不应该塞进 HoAOV 或 HoPost 里执行，应该是和 OIT、平面反射平级的独立 RendererFeature。HoAOV 负责在真实渲染队列之前写数据，HoSubsurfaceScatteringRendererFeature 负责读取这些数据并做扩散与合成。

## 当前状态

lilToon 和 lilPBR 现在都有可用的材质内 Fake SSS：

- lilToon：`_UseSSS`、`_SSSThicknessMap`、`_SSSStrength`、`_SSSColor`，以及 shadow、view、normal shaping。
- lilPBR：`_SUBSURFACE`、`_SubsurfaceMap`、`_SubsurfaceScattering`、`_SubsurfaceThickness`、`_SubsurfaceColor`。
- HoAOV：已经输出 `_lilHoAovMaskIdTexture`、`_lilHoAovNormalDepthTexture`、`_lilHoAovSurfaceDataTexture`。

真 SSS 缺的不是再加一次颜色，而是一张可以跨像素扩散的缓冲，并且扩散时要尊重 coverage、depth、normal 和材质的 SSS mask。

## 推荐帧顺序

推荐顺序如下：

```text
HoShadowCast ShadowMap
Opaque / Cutout 正常渲染
HoAOV Output                  // AfterRenderingOpaques
HoSSS Source Capture          // opaque color 与 HoAOV 已存在
HoSSS Separable Diffusion     // depth/normal aware blur
HoSSS Composite               // 写回 camera color
Transparent / OIT
HoCharacterSpecialization
URP Post Processing
HoPost Stack
Shoost Final Stack
```

如果启用真 SSS，HoAOV 第一版应放在 `AfterRenderingOpaques`，并早于 HoSSS Source。这样 HoAOV 仍然在透明/OIT 之前提供数据，但不会提前到真实不透明绘制之前破坏现有 AOV 的材质采样、法线和深度行为。若后续确实需要真实渲染前的数据，应新增专门的 EarlyAOV/prepass，而不是把现有 HoAOV 整体前移。

## HoAOV 输入语义

第一版 SSS 消费这些 HoAOV 数据：

```text
_lilHoAovMaskIdTexture.r        主体 coverage
_lilHoAovNormalDepthTexture.rgb world normal，0..1 编码
_lilHoAovNormalDepthTexture.a   linear eye depth
_lilHoAovSurfaceDataTexture.r   SSS thinness / scattering mask
_lilHoAovSurfaceDataTexture.g   curvature，后续可做 profile boost
_lilHoAovSurfaceDataTexture.b   material class，后续可做 profile selector
```

`surfaceData.r` 在第一版里定义为 SSS thinness/scattering mask。值越高，屏幕空间扩散越强。这与 lilToon 当前 thickness workflow 保持一致：白色表示更明显的 SSS。未来如果加入背面深度或物理 thickness prepass，也应该 remap 到同一个 consumer-facing mask。

## 材质契约

材质侧 HoAOV pass 负责写逐像素 SSS 参与度：

- lilToon：启用 `_UseSSS` 时，写入 `max(_HoAovThickness, _SSSThicknessMap.r * _SSSStrength)`。
- lilPBR：启用 `_SUBSURFACE` 时，写入 `max(_HoAovThickness, pow(_SubsurfaceMap[channel], _SubsurfacePower) * _SubsurfaceScattering * rim)`。
- 手动 `_HoAovThickness` 仍然是非 lil 材质的 fallback 和 override 路径。

cutout、dither、dissolve、透明覆盖和法线贴图仍由各自材质的 HoAOV pass 负责，因为它们必须匹配真实材质。

## 独立 RendererFeature

新增功能应命名为 `HoSubsurfaceScatteringRendererFeature`，放在 `Runtime/SubsurfaceScattering`。它和 OIT、PlanarReflection 平级，不属于 HoPost 的 filter，也不属于 HoAOV 的输出 pass。

第一版最小缓冲：

```text
_lilHoSSSSourceTexture      从 camera color 提取出的 SSS 源
_lilHoSSSDiffusedTexture    中间扩散结果
```

第一版最小 pass：

1. Source
   - 读取 opaque/cutout 之后的 camera color。
   - 读取 HoAOV coverage、normal-depth、surfaceData.r。
   - 只在参与 SSS 的像素写入 source。

2. Horizontal Diffusion
   - 横向 separable kernel。
   - 半径受全局 radius 和 thinness mask 控制。
   - 用 depth/normal/coverage 抑制跨轮廓扩散。

3. Vertical Diffusion + Composite
   - 纵向扩散。
   - 将扩散结果按 thinness 和全局 strength 写回 camera color。

第一版可以用 camera color 作为 source proxy。质量版再考虑更干净的 diffuse irradiance/source pass 或 MRT 路径。

## 为什么 HoAOV 参与

HoAOV 适合做 SSS 的数据层，因为它已经拥有每个对象和材质的 subject contract：

- 能匹配 lilToon/lilPBR 的 cutout 和 dither。
- 能拿到材质法线，而不仅是 mesh normal。
- 能携带逐像素 SSS mask/thinness。
- 后续可以用 object/material ID 选择 SSS profile。

URP 自带 depth/normal texture 不够，因为它没有材质 SSS 参与度、profile 数据，也不理解这些自定义材质的 alpha 语义。

## 已开始实现

当前第一版实现路径：

- `Runtime/SubsurfaceScattering/HoSubsurfaceScatteringRendererFeature.cs`
- `Runtime/SubsurfaceScattering/HoSubsurfaceScatteringSettings.cs`
- `Runtime/SubsurfaceScattering/HoSubsurfaceScatteringShaderConstants.cs`
- `Runtime/SubsurfaceScattering/HoSubsurfaceScattering.shader`

这版先做可跑的 screen-space diffusion 骨架：读取 HoAOV，捕获 camera color，做横向/纵向双边扩散，再合成回 camera color。它还不是最终物理版 SSS，但已经把 HoAOV 和真正 SSS RenderFeature 的边界划清楚了。

## 2026-05-21 HDRP 方向增强记录

本次增强目标先服务皮肤，而不是玉石、玻璃或厚介质。HoSSS 应参考 HDRP 的屏幕空间 SSS + transmission 分层思路：用已有 HoAOV / depth / normal / camera color 在角色皮肤上做更稳定、更容易调的“有方向的深度散射”，让脸颊、鼻翼、耳缘、手指边缘和皮肤阴影交界处出现更柔和的透光和血色感。

推荐把 HoSSS 拆成两层：

```text
HoSSS Diffusion                 // 现有 depth/normal aware screen-space blur
HoSSS Directional Transmission  // 皮肤用的屏幕空间方向性透射 gather
HoSSS Composite                 // 按 profile 混合回 camera color
```

`HoSSS Diffusion` 继续负责表面扩散和细节软化。它适合皮肤主观质感，也是第一版已经落地的稳定基础。

`HoSSS Directional Transmission` 作为第二层增强，不做真正体积 ray tracing，而是在屏幕空间内沿主光方向、法线投影方向或 view/normal 混合方向做短距离 gather。它的目标是把较亮、较薄、较高处的皮肤颜色带到当前像素的阴影或凹陷区域，形成“皮下血色被散进暗部”的感觉。对皮肤来说，这比长距离折射步进更可控，也更接近角色渲染里常见的 screen-space SSS / transmission hybrid。

最小输入仍然来自 HoAOV：

```text
_lilHoAovMaskIdTexture.r        coverage / subject gate
_lilHoAovNormalDepthTexture.rgb material normal
_lilHoAovNormalDepthTexture.a   linear eye depth
_lilHoAovSurfaceDataTexture.r   SSS thinness / skin scattering mask
_lilHoSSSSourceTexture          skin lighting source
_CameraDepthTexture             depth rejection / thickness proxy
```

第一版 Directional Transmission 可以这样定义：

- 只在 `surfaceData.r > 0` 且 coverage 有效的皮肤像素运行。
- 步进距离短，优先 4 到 12 taps，不追求长距离透明感。
- gather 方向优先使用主光屏幕投影方向；没有主光时使用 normal/view 派生方向。
- 每个 tap 使用 depth gate、normal gate、coverage gate 和 material/object gate，避免串到头发、眼睛、衣服或背景。
- 权重随深度差、法线差、thinness、曲率和皮肤 profile 衰减。
- 颜色吸收以皮肤血色为主，默认偏红橙，不能把整张脸推成玻璃或蜡。

合成语义：

```text
diffused = HoSSS Diffusion(source)
transmitted = DirectionalTransmissionGather(source, HoAOV, depth)
skinSSS = lerp(diffused, diffused + transmitted * skinTransmissionColor, transmissionStrength)
cameraColor = lerp(cameraColor, skinSSS, surfaceData.r * globalStrength)
```

后续如果需要更准的厚度，可以增加 backface thickness prepass，但它不应成为皮肤版 HoSSS 的前置条件。皮肤第一目标是角色观感：脸部暗部变柔、耳缘有透光、鼻翼和手指更有血色、阴影边界不硬，而不是模拟厚介质内部折射。

非目标：

- 不做玻璃 transmission、透明排序或 refraction。
- 不让透明头发、眼球、衣服进入皮肤 transmission gather。
- 不把 Directional Transmission 暴露成材质里的复杂物理参数；材质侧优先保留 `SSS strength / thickness / color / profile` 这种美术可控语义。
- 不替代 HTrace AO / SSGI。HoSSS 是皮肤材质效果，HTrace 仍负责屏幕空间 AO / GI。

## 后续问题

- Source pass 需要比 camera color 更干净的 diffuse/lighting separation。第一版可用 color proxy，质量版应做材质 source pass 或 MRT。
- 透明皮肤语义复杂。第一版只针对 opaque/cutout 皮肤，透明头发和玻璃不进 SSS diffusion。
- 物理 thickness 可以后续作为 HoAOV 或 HoSSS prepass 加入，再 remap 到 `surfaceData.r`。
- 如果项目同时需要 early HoAOV 和 late HoAOV，后续需要支持两套输出或明确 per-renderer asset 的 pass event 配置。
