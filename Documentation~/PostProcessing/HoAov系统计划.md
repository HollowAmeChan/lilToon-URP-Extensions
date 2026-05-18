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
- 任意支持 AOV 的 Shoost 滤镜都可以选择 `MaterialCustom0..3` 或 `ObjectCustom0..7` 作为用户自定义局部遮罩。

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

2026-05-18 决定：角色多光源 cast 不放进 `HoCharacterSpecializationRendererFeature`。少量必须 shadowcast 的项目级光源由 `HoShadowCastController` 统一管理并输出 shadow atlas / light data；HoAOV/角色特化只提供角色语义、receiver mask 和可选桥接入口。

2026-05-18 补充：当前 `lilToon-URP-Extensions` 内的 HoShadowCast 负责少量指定光源的 shadow atlas 与全局光源数据；lilToon / lilPBR 通过独立 receiver 采样该 atlas，不把结果写入 URP 内置 shadow receiver，也不实现全屏最终投影合成。

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

HoAOV 的参与标记不应该只放在材质上。材质适合表达“这个表面按 UV 写出什么”，但不适合表达“这一组 Renderer 属于角色脸部、前发、眼睛、配件，参与哪个后期合成层”。

### 2026-05-17 RSUV / Object AOV 决议

Object AOV 是 Renderer 级协议，来源为 `HoAovGroup` 写入 Renderer Shader User Value。Material AOV 是材质/slot/UV 级协议，来源为现有 HoCustomAOV，固定为 `MaterialCustom0..MaterialCustom3` 四个通道。两者编号独立，不共享 `Custom0..N` 命名空间。

Unity 6.3+ / URP 主路径使用 RSUV。`HoAovGroup` 将对象级语义打包成一个 32-bit `uint`，材质侧 `LightMode = "HoAOV"` pass 读取 `unity_RendererUserValue`，再按材质自身 alpha/cutout/dither/dissolve 规则写入 HoAOV RT。RSUV 只回答“这个 Renderer 是什么”，不回答“哪些像素存在”；像素级存在性永远由材质 HoAOV pass 决定。

第一版 RSUV 打包约定：

```text
bits 0..7    ObjectCustom0..7 二值开关
bits 8..15   CharacterId / GroupId
bits 16..23  PartId / ObjectId
bits 24..31  flags
```

读取语义：

```text
ObjectCustom0..7  按 bit 读取，可以多选，例如 Character + FrontHair 同时为 1
CharacterId       按 8-bit 数字读取，范围 0..255，通常 0 表示未设置
PartId            按 8-bit 数字读取，范围 0..255，通常 0 表示未设置
flags             可按 bit 读取，也可作为 0..255 的小整数标记，第一版优先按 bit 读取
```

未命中 `HoAovGroup` 的 Renderer 不应该自动获得身份。默认 RSUV/MPB/material/fallback 值统一为 0：

```text
ObjectCustom0..7 = 0
CharacterId / GroupId = 0
PartId / ObjectId = 0
Flags = 0
```

不要在 shader 侧用物体位置、实例 ID 或组件 ID 自动生成 `ObjectId`。这类 objectSeed 会让没有分组的对象在 debug 和 HoPost 匹配里表现得像“仅写 ID”对象，破坏 0 表示未设置的约定。`HoAovSubject` 作为兼容覆盖组件也必须默认 `objectId = 0`、`flags = 0`，不能用默认 1 或实例 ID 填空。

`CharacterId` 不是 8 个角色开关，`PartId` 也不是 8 个部件开关。脸、前发、眼睛、配件这类可以同时成立的语义放在 `ObjectCustom0..7`；角色编号和部件编号放在 `CharacterId` / `PartId` 里，用于同角色比较、眼透和角色合成。

`ObjectCustom0..7` 在 RSUV 中是 8 个 bit mask，不是 8 个 float 贴图通道。需要 UV 细节、软遮罩或 slot 级差异时，继续使用 `MaterialCustom0..3`。如果一个 Renderer 内部混合了需要不同 Object AOV 的多个 submesh，应优先拆 Renderer；不能拆时再用现有材质级 HoCustomAOV 承接局部差异。

RSUV 值不作为 authoring 数据保存。`HoAovGroup` 组件字段才是序列化源，必须在 `OnEnable`、`OnValidate`、prefab instantiate 和 scene load 后重新写入目标 Renderer。Unity 6.3 以下或 RSUV API 不可用时，才启用 MaterialPropertyBlock 兼容路径，并在 UI 中明确提示 SRP Batcher 风险。

第一版对象级标记应该挂在空物体上，而不是塞进 Volume 的 renderer 列表，也不是强制每个 Renderer 都挂组件。Volume 适合混合后处理参数，不适合混合离散对象集合；renderer 列表在 Volume 权重里很难定义“半影响”或“只影响空间区域内的某些对象”。对象 AOV 属于场景/角色语义，应该跟 prefab 和层级走。

推荐新增组件：

```text
HoAovGroup
- enabled
- characterId / groupId
- partId / objectId
- flags
- explicitRenderers[] / 仅写 ID
- ObjectCustom0 Character      objects[]
- ObjectCustom1 Face           objects[]
- ObjectCustom2 FrontHair      objects[]
- ObjectCustom3 Eye            objects[]
- ObjectCustom4 EyeRevealArea  objects[]
- ObjectCustom5 Accessory      objects[]
- ObjectCustom6 Reserved       objects[]
- ObjectCustom7 Reserved       objects[]
- includeChildrenForListedObjects / 展开预制件
- priority
```

空物体组件负责“这组 Renderer 属于哪个 AOV 语义层”。典型用法是角色根节点挂一个总组，头发、脸、眼睛、衣服、配件子空物体再挂局部组覆盖。编辑器 UI 显示 8 个 ObjectCustom 列表，列表里可以拖 `GameObject` 或 `Renderer`；命中列表就表示该 Renderer 对应 bit 写 1。同一 Renderer 出现在多个 ObjectCustom 列表时按 bit OR 合并，例如前发可以同时写 `Character` 和 `FrontHair`。

列表实现建议使用 `UnityEngine.Object` 引用并在 Editor 中校验，只接受 `GameObject` 和 `Renderer`。拖入 `GameObject` 时，若 `includeChildrenForListedObjects` 为 true，则收集该物体及其子层级下所有 Renderer；否则只收集该物体自身的 Renderer。拖入 `Renderer` 时只影响该 Renderer。不要接受 `Mesh`、`MeshFilter` 或 mesh asset，因为 RSUV 写在 Renderer 上，不写在网格资源上。

Prefab 使用规则必须明确：在 prefab 自身内部挂 `HoAovGroup` 并拖它的子物体/Renderer 是推荐路径，实例化后组件会重新把 RSUV 写到实例 Renderer。把一个 prefab asset 拖进场景中另一个 `HoAovGroup` 的列表，不代表会自动标记场景里的某个实例；这种引用应该在 Editor 中警告或拒绝。场景对象只应引用同场景对象，Prefab Mode 中只应引用同一 prefab stage 内的对象。

`HoAovGroup` 是普通用户入口，Inspector 应保持低噪音：不要堆大段说明文字，参数名和 tooltip 已经承担解释作用。建议结构：

```text
ID / Flags 区
- CharacterId
- PartId
- Flags
- 仅写 ID 列表

ObjectCustom 列表区
- 8 行彩色列表，整行着色，不另放开头色块
- 行内提供小按钮添加空槽和清空本通道
- 展开后子行也保留淡色背景

组件控制区
- 展开预制件
- 优先级
- 刷新全场景 RSUV
```

`仅写 ID` 列表用于只写 CharacterId / PartId / Flags，ObjectCustom mask 保持 0。它和 ObjectCustom 列表一样接受 `GameObject` / `Renderer`，也遵守组件级 `展开预制件`。`展开预制件` 和 `优先级` 是整个组件的控制项，不属于某一个 ObjectCustom 通道，因此放在底部单独区域。按钮应叫“刷新全场景 RSUV”，语义是清理当前已加载场景中的所有 Renderer 后再重建，而不是只刷新当前组件。

`HoAovSubject` 应标记为高级/兼容覆盖组件，只用于系统 AOV、厚度、曲率、旧式 ID 或 `MaterialCustom0..3` 的 MPB 覆盖，不作为 Object AOV 分组入口。它禁用或销毁时必须清掉写过的 MPB 字段，否则旧 ObjectId/Flags 会在 Renderer 上残留。

合并规则建议：

```text
includeChildren 为 true 时收集子层级 Renderer
explicitRenderers[] 可以补充不在子层级下的 Renderer
同一个 Renderer 命中多个 HoAovGroup 时，priority 高者覆盖
priority 相同时，离 Renderer 最近的层级覆盖
未命中 HoAovGroup 的 Renderer 使用材质默认 AOV 和系统默认值
```

全场景刷新规则：

```text
1. 扫当前已加载场景中的所有 Renderer，包括 inactive。
2. 调 SetShaderUserValue(0) 清 RSUV。
3. 清 HoAOV 相关 MaterialPropertyBlock 字段。
4. 重新应用所有启用的 HoAovSubject。
5. 重建所有启用的 HoAovGroup。
```

这个流程用于修复旧版本、禁用/删除组件、列表变更或 prefab/stage 切换后留下的 RSUV/MPB 脏值。普通属性修改仍可走当前组件的增量重建，但显式按钮必须能“洗全场景”。

材质负责“如何正确输出这些通道”。lilToon / lilPBR 应提供专用 pass：

```shaderlab
Tags { "LightMode" = "HoAOV" }
```

这个 pass 应复用材质自己的 alpha clip、cutout、dissolve、normal map、法线空间和材质参数语义，保证 AOV 边界与实际渲染一致。

## 建议的 AOV 通道

第一版可以直接占 12 个系统逻辑通道位，并额外保留 4 个 Material AOV 和 8 个 Object AOV。这些通道位不等于 24 张完整 RT。它们更像稳定的协议和 UI/资产占位：RendererFeature 根据当前启用的通道决定实际分配哪些纹理，以及哪些通道可以打包到同一张纹理里。

系统 slot、Material AOV 和 Object AOV 必须分开管理。系统 slot 是 HoAOV 自己的长期协议，未来可能增加新的内置数据，例如 bent normal、material AO、light vector、trace result 等；Material AOV 是材质贴图容量；Object AOV 是物体/部件分组容量。三者的扩容节奏和兼容策略不同，不应该塞进同一个固定 enum 位段里。

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

用户可控 AOV 不再作为一组并列的 `Custom0..Custom11` 来理解，而是拆成 Material AOV 和 Object AOV 两类。铁律是：Object AOV 是 Renderer 级协议，Material AOV 是材质/slot/UV 级协议。

```text
MaterialCustom0
MaterialCustom1
MaterialCustom2
MaterialCustom3

ObjectCustom0
ObjectCustom1
ObjectCustom2
ObjectCustom3
ObjectCustom4
ObjectCustom5
ObjectCustom6
ObjectCustom7
```

Material AOV 的目标是贴图/UV 细节遮罩，例如用户画出的皮肤区域、衣服花纹、发光区域、局部滤镜权重。它只允许 4 个通道，并且只在 lilToon/lilPBR 材质 UI 中暴露这 4 个灰度颜色 + 灰度贴图入口。现有 HoCustomAOV 就负责这层，不再扩展到 `MaterialCustom4..11`。

Object AOV 的目标是物体/部件分组和实时合成遮罩，例如角色主体、脸、前发、眼睛、配件、只吃某个后处理的对象组。它默认预留 8 个二值开关，不允许贴图输入，不回到 lilToon/lilPBR 材质 UI，由 `HoAovGroup` 空物体批量指定，并通过 RSUV 写到 Renderer。

眼透是 Object AOV 的刚需用例之一。建议预留默认语义：

```text
ObjectCustom0 = Character / 主体
ObjectCustom1 = Face
ObjectCustom2 = FrontHair
ObjectCustom3 = Eye
ObjectCustom4 = EyeRevealArea / 允许眼透区域
ObjectCustom5 = Accessory / 配件
ObjectCustom6 = Reserved
ObjectCustom7 = Reserved
```

这不是把材质 custom 扩到 12 个，而是把用户可控 matte 分成“画出来的遮罩”和“标出来的对象/部件”。底层可以打包进 AOV RT，但文档、UI 和命名必须保持这两类清晰分离，避免后续又把 `ObjectCustom4..7` 误解成应该给 lilToon 增加的贴图入口。

对应代码层建议把系统通道、材质 AOV 和物体 AOV 拆成三套结构，而不是用一组散乱 bool，也不要让用户 slot 挤占系统 enum：

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

public static class HoAovMaterialChannels
{
    public const int DefaultCount = 4;
    public const int MaxSupportedCount = 4;
}

public static class HoAovObjectChannels
{
    public const int DefaultCount = 8;
    public const int MaxSupportedCount = 8;
}
```

`HoAovGroup` 上应该使用对象 AOV 字段，例如：

```text
enabled
includeChildren
explicitRenderers[]
priority
groupId
objectId
flags
materialClass
utility
objectCustomValues[8]
```

这样第一版 UI 就可以先把 Material AOV 和 Object AOV 的语义占出来，后续新增真实输出时不用频繁改资产结构。

推荐字段分层：

```text
HoAovSettings
- systemChannels
- materialChannelCount = 4
- objectChannelCount = 8
- materialChannelNames[]
- objectChannelNames[]
- objectChannelColors[]

HoAovGroup
- groupId
- objectId
- flags
- materialClass
- utility
- objectCustomValues[8]
```

`materialChannelCount` 第一版固定为 4。`objectChannelCount` 第一版固定为 8。旧资产缺少新增字段时按 0 处理。Object AOV 的 8 个通道是为了角色分组、眼透和局部合成预留，不代表材质侧可以继续新增贴图。

当前扩展包里没有直接包含 lilPBR 源码或已有层位定义，因此 HoAOV 不应该先假设具体字段名。设计上把第 10 位 `Material` 作为 lilPBR/lilToon 层语义的承接口：未来如果发现 lilPBR 已经有材质层、区域层、debug 层或类似 layer slot，就把它映射到 `Material`/`Flags`，不要在 HoAOV 里并行发明第二套互相重叠的层系统。长期目标是 HoAOV 成为更通用的替代层，而不是只给后处理临时使用的小补丁。

虽然协议上保留系统 slot、4 个 Material AOV 和 8 个 Object AOV，第一阶段仍推荐先做足够支撑 HoPost 的最小实际输出：

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
_HoAovMaterialCustom0_3Texture
_HoAovObjectCustom0_3Texture
_HoAovObjectCustom4_7Texture
```

ObjectCustom 应使用独立 RT 名称，不要复用材质 custom 的旧预留纹理名。实现命名建议与现有全局纹理前缀保持一致：`_lilHoAovObjectCustom0_3Texture` 和 `_lilHoAovObjectCustom4_7Texture`。`_lilHoAovCustom4_7Texture` / `_lilHoAovCustom8_11Texture` 已从运行时输出链路移除，不作为 Object AOV 输出，也不作为默认材质 custom 扩展。

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
- ID/Group/Object AOV：Unity 6.3+ 主路径由 `HoAovGroup` 通过 RSUV 传给 pass；RSUV 不可用时才退回 MaterialPropertyBlock 或后续对象 buffer 兼容路径。

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
- HoAovGroup.cs
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

### 2026-05-17 fallback / cutout 回归记录

实际踩坑确认：fallback 不能作为“所有没有 HoAOV pass 的材质都画一遍”的通用兜底。它使用 override material，无法读取源材质 cutout alpha、dither、dissolve 和透明排序语义。若 fallback renderer list 包含 `AlphaTest` / cutout 队列，它会先把镂空面整片写进 AOV；随后材质自己的 `HoAOV` pass 即使正确 `clip`，也只会跳过洞里的像素，不能把 fallback 已写入的黑色 custom 值擦回 clear color。

这次修复后的设计规则：

- fallback renderer list 限制在 opaque 队列，当前上限应小于 `RenderQueue.AlphaTest`。
- `AlphaTest`、cutout、dither、dissolve、transparent 必须依赖材质专用 `LightMode = "HoAOV"` pass。
- 为修深度脏块而拆出的独立 clear pass 可以保留；output pass 使用 `ReadWrite` 承接 clear 后结果，但不要因此把 fallback 重新扩到 cutout 队列。
- Custom AOV debug 中如果 discard 区域出现黑色，优先检查 fallback 是否抢先写入，而不是只盯 lilToon pass 的 `clip` 代码。

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

- `MaterialCustom0..MaterialCustom3` 打包到 `custom0.rgba`，稳定，可作为材质贴图输入的默认上限。
- 单独加到材质 `Custom4` 时没有复现崩溃，但不能证明继续扩展安全。
- 加到 7 张、8 张、12 张独立贴图入口时，Unity 会在 shader/import/启动阶段直接崩溃，表现不像普通 shader 编译错误。
- 曾尝试把 `_lilHoAovCustom4_7Texture` 和 `_lilHoAovCustom8_11Texture` 也接成真实 custom 输入链路，但 SRP/Unity 仍然反复崩溃；这两张旧预留纹理不再进入运行时输出链路，不应默认绑定到 lilToon/lilPBR 的新增贴图输入。
- 因此当前工程约束是：默认只暴露 4 个 Material AOV 贴图遮罩通道，颜色乘贴图 R，默认值为 0。
- 需要更多用户可控遮罩时，优先走 Object AOV：`ObjectCustom0..ObjectCustom7` 由 `HoAovGroup` 空物体批量指定，不新增材质贴图输入。
- 后续如果确实需要更多遮罩，不要直接堆 `_HoAovCustomNTexture`。优先考虑 atlas、packed texture、数组纹理、外部 mask buffer，或做成明确的实验开关，并先在干净工程里逐级验证。

这不是 UI 难不难画的问题，而是 Unity/URP/lilToon/lilPBR 组合下大量材质 texture binding 可能触发 native 侧不稳定。后续维护者不要把 12 通道独立贴图作为默认方案重新加回来，也不要把 Object AOV 误接成材质贴图入口。

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
MaterialCustom0
MaterialCustom1
MaterialCustom2
MaterialCustom3
ObjectCustom0
ObjectCustom1
ObjectCustom2
ObjectCustom3
ObjectCustom4
ObjectCustom5
ObjectCustom6
ObjectCustom7
RSUV 总览
RSUV 角色组 ID
RSUV 部件 ID
RSUV 标记
RSUV 仅写 ID
```

场景视图预览建议放在 `HoAovRendererFeature` 里，由一个只在 debug mode 开启时执行的 fullscreen debug pass 输出。它读取 HoAOV 全局纹理，不改变真实 AOV 生成逻辑。第一版可以对 `CameraType.SceneView` 和 GameView 都支持开关，默认只给 SceneView 打开，避免调试画面误进正式相机。

Object AOV 接入后，HoAOV debug view 必须能直接预览 `_lilHoAovObjectCustom0_3Texture` 和 `_lilHoAovObjectCustom4_7Texture` 的 8 个通道，确认 `HoAovGroup -> RSUV -> HoAOV pass -> ObjectCustom RT` 链路是否正确。HoPost 的图层级 `debugAovMask` 也必须支持 `ObjectCustom0..7`，用于验证某个后期图层在 source/mode/threshold/match/invert 解析后实际消费到的遮罩。

HoAOV debug view 读取的是 HoAOV MRT 的最终结果，不直接读组件字段、材质 inspector 或 `unity_RendererUserValue` 原始值。RSUV 相关 debug 应基于 `_lilHoAovMaskIdTexture.gba` 和 ObjectCustom RT：

```text
MaskId.r    coverage / mask
MaskId.g    raw normalized CharacterId / GroupId
MaskId.b    raw normalized PartId / ObjectId
MaskId.a    raw normalized Flags
```

ID 颜色可以使用 raw normalized 值 hash 成稳定伪彩色，只作为可视化。后处理消费者必须读取 AOV RT 的 raw normalized 值，不能读取 debug 画面的颜色。0 值不应覆盖显示；否则未设置 ID/Flags 的像素会被画成黑色块，看起来像“有数据”。`RSUV 仅写 ID` 应只标出有 CharacterId 或 PartId 且没有 ObjectCustom bit 的像素，不应标出所有未分组物体。

可视化约定：

### HoPost 消费端 AOV 遮罩调试

HoPost 图层需要有独立于 HoAOV 原始通道预览的“消费端匹配结果”调试。HoAOV debug view 用来确认原始 AOV 纹理是否写对；HoPost 的 AOV 遮罩调试用来确认某个图层选择的 `AOV 源 + 使用方式 + 阈值/容差 + 匹配值/颜色 + 反转` 最终得到什么 mask。

当前约定：

- 每个 HoPost 图层提供通用 `AOV 遮罩` 设置。
- `输出匹配结果` 打开后，该图层不再输出原效果，而是直接输出当前 AOV 匹配结果。
- 输出颜色为灰度：白色表示该像素被当前图层选中，黑色表示未选中，灰色表示直接灰度或柔和阈值的中间结果。
- shader 侧不要在每个效果里重复实现调试输出；统一调用 `Runtime/HoPostProcessing/Shaders/HoPost/HoPostAovMask.hlsl` 中的公共方法。
- DropShadow / 投影属于强依赖主体 mask 的效果，调试时应使用同一套 AOV 解析结果作为“主体来源”预览；EdgeLight、Outline 等普通图层则只在 `AOV 遮罩` 开启时把该 mask 作为图层强度限制。


- Mask：黑白显示，权重越高越白。
- Id：非 0 时 hash 到稳定伪彩色，便于看分组；0 不覆盖显示。
- Flags：非 0 时用热力图或 bit 色检查参与关系；0 不覆盖显示。
- LinearDepth：可调 near/far 或自动归一化。
- World/View Normal：`normal * 0.5 + 0.5`。
- TangentNormal：直接看 normal map 结果。
- Velocity：RG 映射到红绿，静止为中灰。
- Thickness/Curvature：热力图，低值暗，高值亮。
- Material/Utility：按值或材质分类显示伪彩色。
- MaterialCustom0..3：默认黑白显示，用来检查材质贴图遮罩。
- ObjectCustom0..7：默认黑白显示，用来检查 HoAovGroup 物体/部件分组。
- RSUV 总览：显示处理后的 MaskId.gba，只看已写入 MRT 的结果。
- RSUV 仅写 ID：显示“有 ID 但无 ObjectCustom”的像素，用来检查 `仅写 ID` 列表。

材质球 debug 可以作为第二层接入：lilToon/lilPBR 的材质 debug 模式里增加 `HoAOV` 分组，把材质自身将要写出的 AOV 值显示在 preview sphere 上。这个 debug 入口只验证“材质会输出什么”，SceneView debug 验证“RendererFeature 最终收到了什么”。两者都要有，因为材质正确不代表 render queue、对象标记、override pass 和透明阶段都正确。

实现上可以先做 RendererFeature settings 里的枚举和 debug shader，后续再加 SceneView overlay 或材质面板按钮。

## 分阶段落地

推荐顺序：

1. 新增 `Runtime/AOV` 框架和全局 RT 绑定。
2. 使用 fallback shader 输出 `Mask + Geometry Normal + Depth + ID`，验证 RenderFeature 顺序。
3. HoPost DropShadow 改读 HoAOV mask/depth，让投影从临时 subject mask 中解耦。
4. HoPost EdgeLight / Outline 改读 HoAOV normal/mask/depth。
5. 新增 `HoAovGroup` 空物体组件，用于批量指定 Object AOV、groupId、flags 和角色部件分组。
6. 将 `HoAovGroup` Inspector 做成面向用户的 RSUV 分组面板；将 `HoAovSubject` Inspector 标记为高级/兼容 MPB 覆盖面板。
7. 给 lilToon/lilPBR shader 模板加入真正的 `HoAOV` pass。
8. 增加 thickness、curvature、velocity 等高级通道。
9. 根据需要增加 HTrace bridge，但不作为第一阶段目标。

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

## 2026-05-17 AOV 遮罩规则组设计

HoPost 和可选的 ShoostStack AOV 遮罩升级为规则组，而不是继续把单个 `source + mode + value` 当成长期接口。Object AOV 接入后，`CharacterId / PartId / Flags / ObjectCustom0..7 / MaterialCustom0..3` 会共同参与角色、部件和局部效果选择；单条规则无法表达“角色 ID 在 1..3，且是主体，排除前发”这类实际需求。

新的消费端模型为：

```text
AOV 遮罩
- 启用
- 输出匹配结果
- 最终反转
- 规则列表，最多 4 条

AOV 遮罩规则
- enabled
- name
- source
- operator
- value
- minValue
- maxValue
- tolerance
- matchColor
- combine
- invert
```

规则 `operator` 至少支持直接灰度、阈值、大于、大于等于、小于、小于等于、等于、不等于、范围、颜色匹配、Flags 任意 bit 和 Flags 全部 bit。规则 `combine` 至少支持 Replace、Or、And、Subtract、Add 和 Multiply。

shader 侧评估约定：

```text
coverage = _lilHoAovMaskIdTexture.r
mask = 0
for each enabled rule:
    ruleMask = EvaluateRule(source, operator, params)
    if rule.invert:
        ruleMask = coverage - ruleMask
    mask = Combine(mask, ruleMask, rule.combine)
mask *= coverage
if finalInvert:
    mask = coverage - mask
```

为了支持 ID 的小于、大于和范围匹配，HoAOV 数据 RT 必须保存可比较的原始归一化 ID，而不是只保存 debug 用伪彩或 hash 值：

```text
_lilHoAovMaskIdTexture.r = coverage / mask
_lilHoAovMaskIdTexture.g = CharacterId / GroupId / 255
_lilHoAovMaskIdTexture.b = PartId / ObjectId / 255
_lilHoAovMaskIdTexture.a = Flags / 255
```

debug view 可以继续把非 0 ID hash 成伪彩颜色，但后处理消费者只能读取 raw AOV RT。数据纹理负责可比较数据，debug shader 负责可视化颜色；不要把伪彩编码写回 AOV RT。




2026-05-17 更新：AOV 规则组现在为硬 0/1 判断，规则参数不再包含过渡宽度字段。
