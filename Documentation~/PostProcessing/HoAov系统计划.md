# HoAOV 系统计划

本文记录 `HoAOV` 的设计目标、边界和落地顺序。它不是 ShoostStack 的一部分，也不是 HTrace 的替代品，而是为 HoPost、未来材质后处理、调试视图和可能的追踪/风格化效果提供统一可复用的 AOV 数据层。

## 背景

当前 HoPost 已经包含 `EdgeLight / 边缘光`、`Outline / 轮廓`、`DropShadow / 投影` 等主体相关效果。这类效果和 ShoostStack 的最终画面滤镜不同，它们不能只读最终 camera color。它们需要明确的主体 mask、normal、depth、id、velocity、thickness、curvature 等信息。

现有 `DropShadow` 第一版尝试在 HoPost 内部临时生成 subject mask，再偏移 mask 合成阴影。这个方案能说明算法方向，但不适合作为长期架构：

- 每个效果各自生成输入会重复且难维护。
- 临时 subject mask 容易受 render queue、透明材质、RenderGraph/compatibility 路径和材质 pass 支持影响。
- HoPost 会同时承担“数据采集”和“画面合成”两个职责，后续会越来越难拆。

因此需要把主体数据采集拆成独立的 `AOV RendererFeature`。

## 总体分层

推荐的系统边界如下：

```text
HoAOV
- 输出数据 RT
- 不改 camera color
- 提供 mask / normal / depth / id / velocity / thickness / curvature 等通道

HoPost
- 消费 HoAOV
- 做边缘光、轮廓、投影、角色局部效果
- 不再自己临时生成 subject mask

ShoostStack
- 默认只消费 camera color
- 作为最终画面滤镜栈
- 不要求 AOV，不要求用户额外喂数据

HTrace
- 目前保持独立
- 继续使用自己的 RT 和 AO/GI 输出
- 第一阶段不改 HTrace 源码
```

核心原则：`HoAOV = 数据底座`，`HoPost = 主体效果消费者`，`ShoostStack = 最终画面滤镜`。

## 与 ShoostStack 的关系

ShoostStack 的设计目标是不需要额外喂数据，用户打开滤镜就能作用于最终画面。因此大部分 Shoost 滤镜不应该依赖 HoAOV：

- VHS
- CRT
- Grain
- Pixelize
- Color Grading
- Glow
- Weather
- Kuwahara
- Blur / Bokeh
- ToonMap

这些滤镜默认只读取 camera color、自己的参数和贴图。

未来可以为部分 Shoost 滤镜提供可选 AOV 模式，例如：

- Kuwahara 只作用角色区域。
- Glow 只扩散指定 AOV mask。
- ToonMap 分主体和背景处理。
- Weather 用 depth/AOV 做遮挡。
- 任意支持 AOV 的 Shoost 滤镜都可以选择 `Custom0..Custom11` 作为用户自定义局部遮罩。

但这些必须是高级选项，默认关闭。ShoostStack 的基础运行不应依赖 AOV。

## 与 HTrace 的关系

HTrace 当前已经能在项目中独立运行，并且自己维护 RT。它输出 AO 供 shader 采样，GI 也以较独立的后处理形式叠加。第一阶段不改 HTrace 源码，也不把 HTrace 并入 HoAOV。

推荐关系：

```text
HTrace -> 输出自己的 AO/GI
HoAOV  -> 输出自己的 Mask/Normal/Depth/Velocity...
HoPost -> 读取 HoAOV
Shader -> 可以同时读取 HTrace AO/GI 和 HoAOV
```

后续如果确实需要互通，可以增加一个桥接层，而不是直接耦合：

```text
HTraceAovBridge
- Off
- HTrace Reads HoAOV Depth
- HTrace Reads HoAOV Normal
- HoAOV Imports HTrace AO
```

这不是第一阶段目标。

## URP 内置 Buffer 的定位

URP 已经有一些类似 AOV 的内部中间纹理：

- `_CameraDepthTexture`
- `_CameraNormalsTexture`
- `_MotionVectorTexture`
- GBuffer
- Rendering Layers Texture
- Depth prepass / DepthNormals prepass
- MotionVectors pass

在 RenderGraph 路径下，它们也会出现在 `UniversalResourceData` 中，例如：

- `cameraDepthTexture`
- `cameraNormalsTexture`
- `motionVectorColor`
- `renderingLayersTexture`
- `gBuffer`

这些可以作为 HoAOV 的原料，但不能替代 HoAOV：

- 它们由 URP 自己按需要生成，不是稳定的自定义效果契约。
- 它们主要服务 opaque、SSAO、TAA、MotionBlur、Deferred 等内置流程。
- 透明 lilToon、头发、半透明材质、特殊 queue 未必可靠写入。
- 它们不包含 subject mask、object id、group id、材质厚度、曲率、自定义 flags 等 HoPost/HTrace 风格效果需要的信息。
- 它们不能表达“这个 renderer 属于角色主体、参与投影但不参与边缘光”这种艺术分组语义。

结论：URP 内置 buffer 是可复用的原料，不是最终 AOV 系统。

## 标记放在哪里

HoAOV 的参与标记应该放在对象或 Renderer 上，而不是只放在材质上。

原因是同一个材质资产可能被多个对象复用：角色 A 要参与投影，角色 B 不参与；头发要参与边缘光，眼睛不参与。只靠材质开关会很快失控。

推荐新增组件：

```text
HoAovSubject
- enabled
- groupId
- objectId
- maskWeight
- writeMask
- writeNormal
- writeDepth
- writeVelocity
- writeThickness
- writeCurvature
- affectsHoPost
- affectsTrace
- priority
```

对象组件负责“谁参与、属于哪一组、写哪些通道”。

材质负责“如何正确输出这些通道”。lilToon / lilPBR 应提供专用 pass：

```shaderlab
Tags { "LightMode" = "HoAOV" }
```

这个 pass 应复用材质自己的 alpha clip、cutout、dissolve、normal map、法线空间和材质参数语义，保证 AOV 边界与实际渲染一致。

## 建议的 AOV 通道

第一版可以直接占 12 个系统逻辑通道位，并额外保留 12 个用户自定义通道位。这些通道位不等于 24 张完整 RT。它们更像稳定的协议和 UI/资产占位：RendererFeature 根据当前启用的通道决定实际分配哪些纹理，以及哪些通道可以打包到同一张纹理里。

系统 slot 和 custom slot 必须分开管理。系统 slot 是 HoAOV 自己的长期协议，未来可能增加新的内置数据，例如 bent normal、material AO、light vector、trace result 等；custom slot 是用户容量，未来可能从 12 扩到 16、24 或 32。两者的扩容节奏和兼容策略不同，不应该塞进同一个固定 enum 位段里。

建议第一版固定 12 个系统 slot：

```text
0  Mask            主体遮罩 / 参与权重
1  Id              groupId / objectId / instanceId
2  Flags           subject flags / effect participation bits
3  LinearDepth     主体线性深度
4  WorldNormal     世界空间法线
5  ViewNormal      视图空间法线
6  TangentNormal   切线空间法线 / normal map 调试
7  Velocity        屏幕或物体运动向量
8  Thickness       厚度 / SSS 近似输入
9  Curvature       曲率 / 边缘细节输入
10 Material        材质分类、区域权重或 lilPBR/lilToon 层语义
11 Utility         预留给系统级桥接，例如 HTrace AO、材质 AO 或未来特殊输入
```

另外固定保留 12 个用户 custom slot：

```text
Custom0
Custom1
Custom2
Custom3
Custom4
Custom5
Custom6
Custom7
Custom8
Custom9
Custom10
Custom11
```

这些 custom slot 的目标不是内部算法，而是给用户和 Shoost 高级模式使用。例如用户可以把“皮肤区域”“头发区域”“眼睛区域”“衣服发光区域”“只吃某个滤镜的遮罩”等标记写进 custom 通道，Shoost 滤镜再选择读取其中一个 custom slot 做局部 Kuwahara、局部 Glow、局部色彩分级或局部 Weather 遮挡。

对应代码层建议把系统通道和 custom 通道拆成两套结构，而不是用一组散乱 bool，也不要让 custom slot 挤占系统 enum：

```csharp
[Flags]
public enum HoAovChannelMask
{
    Mask          = 1 << 0,
    Id            = 1 << 1,
    Flags         = 1 << 2,
    LinearDepth   = 1 << 3,
    WorldNormal   = 1 << 4,
    ViewNormal    = 1 << 5,
    TangentNormal = 1 << 6,
    Velocity      = 1 << 7,
    Thickness     = 1 << 8,
    Curvature     = 1 << 9,
    Material      = 1 << 10,
    Utility       = 1 << 11,
}

public static class HoAovCustomChannels
{
    public const int DefaultCount = 4;
    public const int MaxSupportedCount = 4;
}
```

`HoAovSubject` 上也应该使用同一套 channel mask，例如：

```text
writeChannels
debugColor
groupId
objectId
materialClass
customChannelMask
customValues
```

这样第一版 UI 就可以先把系统 slot 和 custom slot 全部占出来，后续新增真实输出时不用频繁改资产结构。

推荐字段分层：

```text
HoAovSettings
- systemChannels
- customChannelCount
- customChannelNames[]
- customChannelColors[]

HoAovSubject
- systemWriteChannels
- customWriteMask
- customValues[]
```

`customChannelCount` 第一版默认为 4。UI 显示数量跟随 settings，shader 常量和纹理分配跟随实际启用数量，旧资产缺少新增 custom slot 时按 0 处理。

当前扩展包里没有直接包含 lilPBR 源码或已有层位定义，因此 HoAOV 不应该先假设具体字段名。设计上把第 10 位 `Material` 作为 lilPBR/lilToon 层语义的承接口：未来如果发现 lilPBR 已经有材质层、区域层、debug 层或类似 layer slot，就把它映射到 `Material`/`Flags`，不要在 HoAOV 里并行发明第二套互相重叠的层系统。长期目标是 HoAOV 成为更通用的替代层，而不是只给后处理临时使用的小补丁。

虽然协议上保留 12 个系统 slot 和 12 个 custom slot，第一阶段仍推荐先做足够支撑 HoPost 的最小实际输出：

```text
Mask
World Normal
Linear Depth
Group/Object ID
```

第一版可用纹理布局：

```text
_HoAovMaskIdTexture
R: mask
G: groupId
B: objectId low
A: flags

_HoAovNormalDepthTexture
RGB: world normal encoded
A: linear eye depth 或备用
```

如果需要更高精度或更清晰的消费接口，再拆分为：

```text
_HoAovMaskTexture       R8
_HoAovWorldNormalTexture RG16 或 RGBA8
_HoAovDepthTexture      R32F
_HoAovIdTexture         RGBA8 或 R16
```

后续扩展通道：

```text
_HoAovViewNormalTexture
_HoAovTangentNormalTexture
_HoAovVelocityTexture
_HoAovThicknessTexture
_HoAovCurvatureTexture
_HoAovUtilityTexture
_HoAovCustom0Texture ... _HoAovCustom11Texture
```

建议精度：

- Mask: `R8_UNorm`
- ID/Flags: `RGBA8_UNorm` 或 `R16_UInt`，取决于平台和采样需求
- World/View Normal: `RG16_UNorm` octa encoding，或第一版 `RGBA8_UNorm`
- Depth: `R32_SFloat` 或复用 `_CameraDepthTexture`
- Velocity: `RG16_SFloat`
- Thickness: `R16_SFloat`
- Curvature: `R16_SFloat`

## 通道来源

各通道建议来源：

- Mask：由 `HoAOV` pass 在材质 alpha/cutout/dissolve 后输出。
- World Normal：材质 pass 输出最终 per-pixel normal，包含 normal map。
- View Normal：可由消费者从 world normal 转换，或后续单独输出。
- Tangent Normal：由材质 pass 输出 normal map 解包后的 tangent-space normal。
- Depth：可先复用 `_CameraDepthTexture`，需要主体独立深度时再由 HoAOV 输出。
- Velocity：优先复用 URP `_MotionVectorTexture`；自定义需求再由 AOV pass 输出 object velocity。
- Thickness：第一版用材质属性、顶点色或固定值近似；高级版做 backface depth - frontface depth。
- Curvature：第一版用 normal derivative 屏幕空间近似；高级版用预烘 vertex color 或 mesh attribute。
- ID/Group：由 `HoAovSubject` 通过 MaterialPropertyBlock 或 renderer 数据传给 pass。

## 渲染顺序

HoAOV 应在 HoPost 之前，ShoostStack 之前。

建议顺序：

```text
OIT / Transparent Resolve
HoAOV
HoPost
ShoostStack
Final
```

如果某些通道需要不透明阶段深度，可根据需要拆成两个阶段：

```text
HoAOV Opaque
HoAOV Transparent/Subject
```

第一阶段建议先做一个简单阶段，服务 HoPost 主体效果。

## RendererFeature 结构

建议新增目录：

```text
Runtime/AOV/
- HoAovRendererFeature.cs
- HoAovSettings.cs
- HoAovSubject.cs
- HoAovShaderConstants.cs
- Shaders/HoAOV/HoAovFallback.shader
```

职责：

- 根据 settings 分配 AOV RT。
- 收集参与对象。
- 使用 `LightMode = HoAOV` 绘制材质专用 pass。
- fallback 情况下用 override material 输出粗略 mask/depth/geometry normal。
- 将 AOV RT 绑定为全局纹理。
- 提供调试视图。

第一阶段的 fallback shader 可以让系统先跑起来，但它不是最终质量：

- 优点：不需要立刻改 lilToon/lilPBR shader 生成。
- 缺点：拿不到完整 alpha/dissolve/normal map/材质厚度语义。

长期仍应在 lilToon/lilPBR 中加入真正的 `HoAOV` pass。

## HoPost 改造方向

HoPost 应从“自己生成临时输入”改成“消费 HoAOV”：

- DropShadow 读取 `_HoAovMaskTexture` 和 `_HoAovDepthTexture`。
- EdgeLight 读取 `_HoAovMaskTexture` 和 `_HoAovWorldNormalTexture` / `_HoAovViewNormalTexture`。
- Outline 读取 `_HoAovMaskTexture`、depth 和 normal。
- HoPost RendererFeature 不再维护 subject mask 的 layer/render queue/shader 设置。
- `SubjectMask.shader` 可以保留为过渡实现，最终废弃。

对应关系：

```text
EdgeLight  -> mask + normal
Outline    -> mask + normal + depth
DropShadow -> mask + depth
```

## lilToon / lilPBR 侧改造方向

lilToon / lilPBR 材质侧需要提供 AOV 输出能力，而不是直接实现后处理效果。

建议新增材质属性：

```text
_HoAovEnabled
_HoAovMaskWeight
_HoAovThickness
_HoAovCurvature
```

并加入 pass：

```shaderlab
Tags { "LightMode" = "HoAOV" }
```

### 2026-05-17 Custom 贴图输入踩坑记录

不要再把 HoAOV custom 通道默认扩成大量独立贴图输入。lilToon / lilPBR 侧实际测试过 4、5、7、8、12 张 custom 贴图入口：

- `Custom0..Custom3` 打包到 `custom0.rgba`，稳定，可作为默认上限。
- 单独加到 `Custom4` 时没有复现崩溃，但不能证明继续扩展安全。
- 加到 7 张、8 张、12 张独立贴图入口时，Unity 会在 shader/import/启动阶段直接崩溃，表现不像普通 shader 编译错误。
- 因此当前工程约束是：默认只暴露 4 个 custom 遮罩通道，颜色乘贴图 R，默认值为 0。
- 后续如果确实需要更多遮罩，不要直接堆 `_HoAovCustomNTexture`。优先考虑 atlas、packed texture、数组纹理、外部 mask buffer，或做成明确的实验开关，并先在干净工程里逐级验证。

这不是 UI 难不难画的问题，而是 Unity/URP/lilToon/lilPBR 组合下大量材质 texture binding 可能触发 native 侧不稳定。后续维护者不要把 12 通道独立贴图作为默认方案重新加回来。

这个 pass 应该：

- 复用 alpha clip/cutout/dissolve。
- 输出最终 normal map 后的 normal。
- 支持 transparent/cutout 角色材质。
- 接收 object/group id 或 flags。
- 不改 camera color。

材质侧只负责“如何输出数据”，具体边缘光颜色、投影强度、描边参数仍由 HoPost/AOV 消费者控制。

## 调试与可视化

HoAOV 应把调试视图当成第一版功能，而不是后面补。目标是在场景视图里直接看每个通道的内容，并且后续可以接到材质球 / 材质 debug 模式中。

建议新增 debug mode：

```text
Off
Mask
Id
Flags
LinearDepth
WorldNormal
ViewNormal
TangentNormal
Velocity
Thickness
Curvature
Material
Utility
Custom0
Custom1
Custom2
Custom3
Custom4
Custom5
Custom6
Custom7
Custom8
Custom9
Custom10
Custom11
```

场景视图预览建议放在 `HoAovRendererFeature` 里，由一个只在 debug mode 开启时执行的 fullscreen debug pass 输出。它读取 HoAOV 全局纹理，不改变真实 AOV 生成逻辑。第一版可以对 `CameraType.SceneView` 和 GameView 都支持开关，默认只给 SceneView 打开，避免调试画面误进正式相机。

可视化约定：

- Mask：黑白显示，权重越高越白。
- Id：hash 到稳定伪彩色，便于看分组。
- Flags：按 bit 映射颜色，检查参与关系。
- LinearDepth：可调 near/far 或自动归一化。
- World/View Normal：`normal * 0.5 + 0.5`。
- TangentNormal：直接看 normal map 结果。
- Velocity：RG 映射到红绿，静止为中灰。
- Thickness/Curvature：热力图，低值暗，高值亮。
- Material/Utility：按值或材质分类显示伪彩色。
- Custom0..Custom11：默认黑白显示，也可以按用户给定颜色或 slot 名称显示。

材质球 debug 可以作为第二层接入：lilToon/lilPBR 的材质 debug 模式里增加 `HoAOV` 分组，把材质自身将要写出的 AOV 值显示在 preview sphere 上。这个 debug 入口只验证“材质会输出什么”，SceneView debug 验证“RendererFeature 最终收到了什么”。两者都要有，因为材质正确不代表 render queue、对象标记、override pass 和透明阶段都正确。

实现上可以先做 RendererFeature settings 里的枚举和 debug shader，后续再加 SceneView overlay 或材质面板按钮。

## 分阶段落地

推荐顺序：

1. 新增 `Runtime/AOV` 框架和全局 RT 绑定。
2. 使用 fallback shader 输出 `Mask + Geometry Normal + Depth + ID`，验证 RenderFeature 顺序。
3. HoPost DropShadow 改读 HoAOV mask/depth，让投影从临时 subject mask 中解耦。
4. HoPost EdgeLight / Outline 改读 HoAOV normal/mask/depth。
5. 给 lilToon/lilPBR shader 模板加入真正的 `HoAOV` pass。
6. 增加 thickness、curvature、velocity 等高级通道。
7. 根据需要增加 HTrace bridge，但不作为第一阶段目标。

## 非目标

第一阶段不要做这些事：

- 不改 HTrace 源码。
- 不把 HTrace RT 合并进 HoAOV。
- 不让 ShoostStack 默认依赖 AOV。
- 不改 URP 全局 RenderGraph/Compatibility 设置。
- 不把每个 HoPost 效果都继续写一套自己的 mask pass。

## 结论

HoAOV 应该作为独立 RendererFeature 存在。它统一产出主体和材质相关的可复用数据，HoPost、未来风格化效果、调试工具和可选的 HTrace bridge 都可以读取它。

短期目标是解决 HoPost 的投影、边缘光和轮廓输入不稳定问题；长期目标是建立一个类似 HTrace 可复用数据层的通道系统，让材质、后处理和追踪效果共享同一套稳定 AOV。
