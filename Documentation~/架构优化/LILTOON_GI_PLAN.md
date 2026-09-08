# lilToon / Ho-SSGI 实施规划

> 状态：Draft v0.6
>
> 主线：先把 HTrace SSGI 改造成适合 lilToon 的 Ho-SSGI；Brixelizer GI 只作为后续 producer 替换，不提前展开完整实现。

## 0. 结论

当前最实际的路线是：

```text
Ho-SSGI（HTrace 改进）
    -> BeforeRenderingPostProcessing 合成
    -> 朱木古堂验收
    -> 保持同一 GI 输出契约
    -> 后续可替换为 Brixelizer GI
```

不为低质量档另做一套算法。先确定一条高质量路径，用分辨率、ray count、history length 和 denoise 参数控制成本；Low/Medium 只改变参数，不改变算法结构。

Brixelizer GI 只保留为后续方向。它是 compute/SDF/radiance-cache 路线，不是硬件 RTGI，但需要 DX12/Vulkan、HLSL CS 6.6 和较重的世界空间接入。[官方资料](https://gpuopen.com/fidelityfx-brixelizer/)

### 0.1 SSGI 插入时机的取舍

`BeforeRenderingOpaques` 和 `AfterRenderingOpaques` 不能同时得到同一种 source：

- **After Opaques** 能读取当前帧已经完成 direct lighting 的 opaque/camera color，这是 HTrace 在 URP 中使用的 source；因此最容易得到有灯光存在感的 SSGI，但结果只能在 forward 材质之后做 composite。
- **Before Opaques** 能让 lilToon 在当前帧 forward shading 中采样 GI，但当前帧 direct-light radiance 尚未生成。只读取 `SurfaceColor` 时，结果只是 base-color transfer/SSDO 风格的增强，不是完整 GI；要正确包含 Point Light、阴影、toon ramp、light cookie 和 HoShadowCast，必须另做 direct-source 评估或 source pass。

因此 Ho-SSGI 的算法基线放在 **After Opaques**：用 GeometryBuffer 做 hit validity 和 outline 排除，读取已经完成 direct lighting 的 opaque camera color，再验证 HTrace 的 temporal/ReSTIR/denoise 链。Ho-SSGI 作为纯后处理 producer，不向当帧 lilToon forward 材质提供 GI。

## 1. HTrace SSGI 的真实光照输入

### 1.1 当前 HTrace 怎么得到 hit radiance

HTrace 的 `HRenderSSGI.compute` 在屏幕空间 ray 命中后读取 `_Color`：

```hlsl
HitRadiance = UnpackColorHit(H_LOAD(_Color, HitData.xy).x, MovingHitPoint);
```

URP 路径里 `_Color` 绑定的是 camera color。也就是说，HTrace 的射线并没有在命中点重新计算灯光；它把命中像素已经得到的最终颜色当作 outgoing radiance，再做距离衰减、temporal accumulation 和 ReSTIR 重采样。

### 1.2 GBuffer 在 HTrace 中做什么

HTrace 自己生成或读取 GBuffer、depth pyramid、rendering layer 和 motion/history 资源。GBuffer0/1 在 `ColorComposeURP.shader` 中主要用于：

- GBuffer0：albedo；
- GBuffer1：metallic/specular 相关信息和 AO；
- 最终间接光合成：`GI * albedo * (1 - metallic) * AO`；
- ambient override：从 camera color 中减去估计的环境间接光，避免重复叠加。

它没有一张独立的“灯光属性 buffer”。直接光照已经被烘进 camera color，APV/天空只在 ray miss 时作为 fallback。

相关本地源码：

- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Resources/HTraceSSGI/Computes/HRenderSSGI.compute`
- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Resources/HTraceSSGI/Shaders/URP/ColorComposeURP.shader`
- `D:/Unity_Project/BREAK_URP/Assets/HTraceSSGI/Scripts/Passes/URP/GBufferPassURP.cs`

### 1.3 对 lilToon 的问题

camera color 不是干净的 GI source，因为它可能已经包含：

- 外扩描边；
- toon 最终合成；
- 透明/OIT；
- SSS、反射和后期处理；
- 不属于真实表面的装饰效果。

这正是 HTrace 描边发白的根本原因。问题不是单纯的 ray length、brightness clamp 或 sample count。

HTrace 的 `DirectLighting` debug 也不是一张独立的 direct-light RT。URP 中它显示的是间接光注入前的 camera color，所以会包含 opaque outline；透明物体尚未进入这个时机，因此会表现为没有透明内容或出现历史拖影。这解释了你现在看到的现象。

### 1.4 HTrace 已有设计盘点

HTrace 的 URP 链路实际上是：

```text
PrePass / motion vectors
    -> GBuffer 或 ForwardGBuffer0..3
    -> rendering layer mask
    -> depth pyramid MIP0..4
    -> temporal reprojection
    -> checkerboard（可选）
    -> ray tracing
    -> ReSTIR temporal / firefly / spatial
    -> temporal + spatial denoise
    -> interpolation（低 render scale 时）
    -> camera color composite
```

以下设计应先研究后再删减：

| HTrace 设计 | 当前机制 | Ho-SSGI 处理 |
|---|---|---|
| `ExcludeCastingMask` | 在深度金字塔 MIP0 把 caster 深度置为无效，再生成更高 MIP | 保留；改用 Ho 的几何/对象排除语义 |
| `ExcludeReceivingMask` | 最终合成时把 receiver 的 GI 替换为 APV/天空 fallback | 保留；改成 GI output 层的 receiver mask |
| `MaskExclude` kernel | HTrace shader 中存在，但当前 URP 链没有调用，主要是 HDRP 路径 | 不照搬 kernel，先修正 URP 路径 |
| `AmbientOverride` | 从 camera color 减去 APV/天空估计的环境光，避免双重 GI | 不直接照搬；Ho 输出独立 GI，避免 PBR albedo/metallic 假设 |
| `Multibounce` | 把 camera color/上一帧颜色写入 radiance history | 保留 history 思路，但 history 必须是干净 source/GI，不包含 outline/post |
| `FallbackType` | None、Sky、APV；ray miss 时补环境光 | 保留 APV/sky fallback |
| `NormalBias` / `ViewBias` / `SamplingNoise` | APV fallback 采样偏移与噪声 | 保留，作为 fallback 参数 |
| `BackfaceLighting` | 命中表面背面法线时限制或拒绝 hit radiance | 保留，按 lilToon 双面/背面语义重新验收 |
| `ThicknessMode` / `Thickness` | 线性或 uniform thickness，控制屏幕相交容差 | 保留；这是薄片、头发和描边附近稳定性的关键 |
| `RefineIntersection` | 命中后用更细的中间点确认相交 | 高质量路径默认开启 |
| `FullResolutionDepth` | 低分辨率 tracing 时仍使用全分辨率深度 | 高质量路径默认开启 |
| `Checkerboard` | 交错像素分类和间接 dispatch | 先关闭，确认高质量主链正确后再作为参数优化 |
| `BrightnessClamp` | 按最大值或邻域偏差限制亮点 | 保留，避免颜色反弹 firefly |
| ReSTIR validation | temporal lighting/occlusion validation、half-step validation | 保留已有设计，先验证 source 替换后的有效性 |
| spatial reservoir | 依据深度、法线、AO guidance 做空间重采样 | 保留，不另写一套低质量 blur |
| recurrent blur | 使用 spatial output 作为后续 temporal history | 先作为可选质量参数，不能污染 source history |

一个重要事实：`ExcludeCastingMask` 在 HTrace 的深度金字塔阶段是有效的，但 `ExcludeReceivingMask` 只能排除已经拥有正确 rendering layer 的接收像素。当前 lilToon outline 的基础 GBuffer/DepthNormals 不包含外扩壳，因此 HTrace 的 layer mask 不能单独解决 outline 白边。

### 1.5 Ho 的排除真值：GeometryBuffer coverage

Ho-GeometryBuffer 已经验证为：

- normal/depth pass 不执行 outline extrusion；
- outline 壳不写入 normal/depth；
- 可用 coverage、normal validity 和 depth 作为物理表面判断。

因此第一版不需要额外渲染一张“无描边完整 RT”，也不需要把主 shader 和 outline shader 拆成两个材质。SSGI 的 hit acceptance 统一使用：

```text
hitUV
  -> sample HoGeometryBuffer normal/depth
  -> coverage valid
  -> normal valid
  -> ray depth 与 geometry depth 一致
  -> 才允许 sample source radiance
```

描边仍然存在于 camera/opaque color，但它对应的 GeometryBuffer coverage 为空，会在 source 采样前被拒绝。它也不会出现在 Ho-SSGI 的 depth pyramid 中，因此不会作为 caster 遮挡或反弹。

当前 producer vertical slice 在 `AfterRenderingOpaques` 运行，source 是已经完成 direct lighting 的 opaque camera color。GeometryBuffer 仍然在更早的时机生成，用来拒绝描边壳和其他没有真实几何覆盖的像素。

透明/OIT 第一版不进入 GI caster/receiver 域，使用 APV fallback。这样先消除 HTrace 的透明拖影问题，再单独研究透明 GI，不把透明路径混进主 SSGI 验证。

### 1.6 现有双语义绘制就是干净 source 的基础

主 forward pass 把主体和 outline 一起画进 camera color；`HoGeometryBuffer` 则通过独立的 base geometry pass 再画一次主体：

```text
camera opaque color       = 后续 direct-light source，可能包含 outline
HoGeometryBuffer          = 无 outline 的 normal/depth/coverage
```

这不是为了 GI 新增一张 no-outline RT，而是复用已经存在的几何绘制成本。Ho-SSGI 使用它们的方式是：

- GeometryBuffer 决定 hit/receiver 是否是真实几何；
- opaque camera color 提供已经着色的 radiance；
- 两者在 hit UV 上做 coverage/depth 一致性校验后才进入 GI。

`HoMetadataBufferSurfaceColor` 不再是 Ho-SSGI 的输入。MetadataBuffer 继续服务角色语义、AOV 和其他后期 feature。

### 1.7 SSGI 真正需要的光照输入

SSGI 不消费 `Light` 对象列表，也不需要一张把所有灯光颜色简单相加的 RT。它需要的是命中表面的出射辐射 `L_o`，也就是 direct lighting、toon ramp 和可反弹 emissive 已经在表面上完成后的颜色。

```text
SSGI 输入
  = Geometry（depth / normal / coverage）
  + Screen visibility（Hi-Z depth ray march）
  + Hit radiance（source RGB）
  + Receiver response（diffuse / metallic / AO，可选）
  + Motion / history / fallback
```

各输入的职责和 ShadowCast 边界如下：

| 输入 | 用途 | 是否直接依赖 ShadowCast |
|---|---|---|
| Depth、normal、coverage | 重建位置和法线，判断 screen hit | 否 |
| Hi-Z depth pyramid | 加速屏幕空间 ray march、caster 排除 | 否 |
| Source radiance RGB | 命中点向外贡献的 direct/toon/emissive 辐射 | 阴影已间接烘入 source |
| Diffuse、metallic、AO | 接收面的间接光响应和合成 | 否 |
| Motion、history | temporal/reprojection 和灯光变化检测 | 否 |
| Light 列表 | 不由 SSGI 核心读取 | 只有 source pass 需要 |

SSGI 的屏幕空间遮挡来自 camera depth/GeometryBuffer，不是 ShadowCast atlas。ShadowCast 只有在生成独立的 lit source 时才参与：source pass 必须使用和主 forward 一致的 Unity shadow 或 Ho-ShadowCast 衰减，否则会把主画面中处于阴影的表面错误地当成受光 source。

HTrace 的 `DirectLighting` debug 只是间接光注入前的 camera color，并不是独立的 direct-light RT。HTrace 的 AO 也不是单独的灯光 pass：`HRenderSSGI.compute` 在 SSGI tracing 中顺便输出 near-hit AO，最终合成还会读取 URP 的 `_ScreenSpaceOcclusionTexture`。因此“自己渲了一遍 GTAO”不能推导出它有独立的灯光输入。

### 1.8 ReSTIR 的资源和所有权

ReSTIR 不是一张可以被所有 feature 直接共用的颜色图，而是一组带有算法语义的 reservoir/history 资源。HTrace 的 GI reservoir 至少保存：

```text
selected radiance / Color
Wsum、M、W
selected ray direction / distance
HitFound、OriginNormal
```

其中 `M` 是候选样本或历史长度，`Wsum` 是候选权重总和，`W` 是最终估计器归一化权重。最终 GI 不是简单平均，而是 `selectedColor * W`。ReSTIR temporal 会把当前 reservoir 和 motion 重投影后的历史 reservoir 合并；spatial 会把邻居 reservoir 按平面、法线、距离和空间权重合并；firefly suppression 主要修改选中 reservoir 的 `W`，保留样本本身。

HTrace 自己持有这些 RT，是因为它要跨 URP/HDRP、Forward/Deferred、不同材质和不同版本自行保证 GBuffer、motion、depth、history、render scale 和 shader binding。我们的中控可以消除这类兼容性重复，但不能把不同算法的 reservoir layout 强行合并。

Ho 的所有权边界应为：

```text
HoGeometryBuffer / 共享屏幕空间基础层
  = normal + depth + coverage + 可复用 Hi-Z + 几何有效性

HoScreenSpaceLightingContext / 共享资源中控
  = 每相机生命周期、尺寸变化、ping-pong、history reset、neighbor offsets、blue noise

Ho-SSGI
  = GI candidate、GI reservoir、GI temporal/spatial resampling、GI denoise

Ho-GTAO
  = AO candidate、AO history、AO temporal/spatial filter

Ho-DI（后续）
  = light candidate/RIS、DI reservoir、visibility validation、DI denoise
```

因此可以把 ReSTIR 的**资源管理基础设施**提前规划为 GeometryBuffer 之后的共享中控，也可以让 GeometryBuffer 发布 Hi-Z 和几何句柄供 AO/GI/DI 共同使用；但实际的 GI/DI reservoir 不应塞进 GeometryBuffer feature。GI reservoir 代表二次表面样本，DI reservoir 代表灯光样本，AO history 代表遮挡统计，三者的候选权重、验证条件、打包字段和 reset 条件都不同。

ReSTIR 的执行时机也不能全部提前到 GeometryBuffer：

```text
GeometryBuffer（Before Opaques）
  -> shared geometry / Hi-Z preparation
  -> opaque lighting 或 HoGI Lit Source
  -> GI/DI candidate generation
  -> temporal reservoir reuse
  -> spatial reservoir reuse
  -> algorithm-specific denoise
  -> composite
```

GeometryBuffer 可以提前准备输入和分配资源，但 GI candidate 必须等 source radiance 可用，DI candidate 必须等 light list 和材质/几何响应可用。这样既保留统一中控，也不会让 GeometryBuffer feature 承担 GI、DI、AO 的算法职责。

### 1.9 是否需要独立的共享屏幕空间资源层

需要保留这个架构方向，但不应现在就把所有临时 RT 搬进去。早先讨论的 `Ho-RTBuffer` 名称不作为最终命名，因为它容易被理解成硬件 RT buffer，也没有说明它是否拥有 Hi-Z、history 或 reservoir。

建议把职责分成三层：

```text
HoGeometryBuffer RendererFeature
  -> normal / depth / coverage，以及几何有效性

HoScreenSpaceContext（第二阶段抽取）
  -> 共享 Hi-Z、motion/blue-noise、尺寸与相机 reset、ping-pong 生命周期、资源格式

Ho-SSGI / Ho-GTAO / Ho-DI
  -> 各自 candidate、typed reservoir、validation、denoise 和输出
```

因此 `HoScreenSpaceContext` 可以成为 GeometryBuffer 之后的共享资源中控，但不能成为 GeometryBuffer 的算法附属物，也不能把 GI reservoir、AO history 和 DI light reservoir 合成一套“万能 RT”。这些数据的 payload、权重和历史失效条件不同。

当前先不立即新增空的 Feature，原因是 ReSTIR reservoir 目前只有 Ho-SSGI 一个消费者；过早拆分会增加 Renderer Feature 列表顺序、RenderGraph 依赖和资源回收路径，却没有共享收益。第一段 ReSTIR 先在 HoSSGI 内闭环。满足以下任一条件时再创建实际的共享资源 producer：

1. Ho-GTAO 和 Ho-SSGI 都需要同一套 Hi-Z，且重复生成已经成为可测量的 GPU 成本；
2. Ho-DI 开始使用同一套 motion、neighbor offset、blue-noise 或相机 history reset；
3. 需要跨多个 producer 统一暴露 RenderGraph 资源，而不是只共享一组 C# 工具函数。

首个迁移目标应是把 GTAO 当前的 `CreateDepthPyramid` 抽成共享 Hi-Z producer，再让 SSGI、GTAO 和后续 DI 通过 `HoScreenSpaceContextResources` 读取；reservoir 的具体纹理仍由对应 producer 创建和写入。如果它确实负责创建共享 Hi-Z 和 ray-query 工作资源，RendererFeature 才使用 `HoScreenSpaceRayQueryRendererFeature` 这个名称。

当前源码审计补充见 [Ho-GTAO / Ho-SSGI ReSTIR 共享层评估](LILTOON_RESTIR_SHARED_LAYER_REPORT.md)。需要特别区分：当前 Ho-GTAO 只有 temporal AO history 和 spatial bilateral filter，没有 ReSTIR reservoir；第一阶段只继续完善 Ho-SSGI 的 GI reservoir，不为 GTAO 增加抽象层。

## 2. Ho-SSGI v1 的核心设计

### 2.1 复用输入

Ho-SSGI 直接复用：

- `HoGeometryBuffer` 的 normal/depth/sky；
- URP camera motion vector，或深度/法线历史校验；
- APV/天空 fallback；
- Ho 自己的 depth pyramid 和 temporal history。

Ho-SSGI 不创建 HTrace 那套重复 GBuffer、重复 depth prepass 和独立 rendering-layer buffer。

MetadataBuffer 不属于 GI 输入。它继续作为角色语义、CharacterSpecialization、SSS、AOV 和 ScreenProcess 的来源。当前它已经可以在 `BeforeRenderingOpaques` 生成，但 GI 不读取它。

### 2.2 GI source

Ho-SSGI 读取 After Opaques 时的 opaque camera color，并用 GeometryBuffer coverage、法线和深度一致性排除描边与无效几何。这个 source 表达的是“命中点可以向外贡献的已经着色辐射”，不是单纯 albedo：

```text
GI source = opaque camera color（包含 toon direct lighting）
```

不需要把每个 Light 对象的参数单独塞进 SSGI。灯光属性已经由 opaque forward pass 完成，SSGI 只负责：

```text
source radiance
    * ray visibility
    * normal / cosine weighting
    * distance falloff
    * temporal / spatial filtering
```

### 2.3 后续的 lilToon Lit Source Pass

当 opaque camera color 中的 outline、透明、反射或后期污染已经成为主要误差来源时，再增加专用的 `HoGI Lit Source` 输出。它不是原始灯光缓存，而是 lilToon 在真实灯光模型下输出的干净表面辐射：

```text
RGB = direct diffuse / toon lighting + emissive
A   = valid opaque surface coverage
```

source pass 必须排除 outline、specular/reflection、透明/OIT 和 post effect；接收面的 diffuse、metallic、AO 仍然是独立语义。最理想的实现是让 lilToon opaque forward pass 通过 MRT 同时写入正常 camera color 和 `HoGI Lit Source`，避免完整重复一次几何和灯光计算。若 MRT 改动过大，再使用独立的 `LightMode = HoGI` lit pass，但它必须复用主 shader 的灯光、toon ramp、cookie、light layer 和 shadow attenuation 规则。

这个 source pass 可以在 After Opaques、Before Post Processing 之间生成，仍然适用于当前纯后处理 Ho-SSGI；只有要让 GI 在当帧 forward 材质内生效时，才需要把 source 和 GI producer 提前到 opaque shading 之前。

专用 screen source 只解决 SSGI 的可见表面辐射。它不能直接充当 Brixelizer/DDGI 的世界空间 radiance cache，后者仍需要 probe/SDF 采样和世界空间更新路径。

这样主光、附加光、阴影、toon ramp 和材质颜色都已经在 source 中表达，SSGI 不需要重新实现一套 light loop。

### 2.4 输出契约

生产端发布算法无关的语义：

- `_HoGITexture`：HDR 间接光颜色；
- `_HoGIConfidenceTexture`：命中、fallback、history 有效性；
- 可选 `_HoGIBentNormalTexture`：环境补光和 debug 使用。

关闭 Ho-SSGI 时，GI 颜色为黑色、confidence 为 0。lilToon 消费端只知道 GI 语义，不知道 HTrace 或其他实现。

## 3. 一条高质量 Ho-SSGI 链

```text
HoGeometryBuffer (250)
    -> opaque forward lighting
    -> GI source（opaque camera color；后续可替换为 HoGI Lit Source）
    -> Ho-SSGI raw trace
    -> world-space cosine hemisphere tracing
    -> GeometryBuffer depth intersection
    -> GI candidate reservoir
    -> temporal reservoir reuse + motion/depth validation
    -> firefly reservoir clamp
    -> spatial reservoir reuse + depth/normal/Gaussian guidance
    -> temporal/spatial GI reconstruction
    -> APV/sky fallback
    -> _HoGITexture + confidence
    -> composite BeforeRenderingPostProcessing
```

第一版不拆出低质量算法。质量参数只控制：

- render scale；
- ray count；
- trace step count；
- history length；
- spatial radius；
- source/indirect intensity。

Ho-SSGI v1 应尽量沿用 HTrace 已验证的 ReSTIR、temporal validation、firefly suppression、depth pyramid 和 recurrent history 结构。接入顺序是：先替换 source 并保持 HTrace 链路行为，再逐项检查每个阶段在 lilToon 上的语义，而不是重新发明一套简化算法。

## 4. 纯后处理合成

先验证 producer，再验证纯后处理合成：

1. debug 直出 GI source；
2. debug 直出未滤波 ray result；
3. debug 直出 temporal/confidence；
4. fullscreen composite 验证颜色反弹；
5. 最后处理 SSS、OIT、平面反射和角色特化的 pass 顺序。

### 4.1 当前合成语义

Ho-SSGI 输出一张独立的 HDR `_HoGITexture`，在 `BeforeRenderingPostProcessing` 读取当前 camera color 后叠加。它不改写 `fd.lightColor`、`fd.indLightColor`，也不增加 lilToon 材质采样。

当前合成只保留一个全局强度：

```text
cameraColor.rgb += hoGIColor * volumeStrength
```

`HoSSGIVolume` 是用户主要调节面；`sourceSaturation` 只影响命中点 radiance 的色彩，`intensity` 只影响最终 composite。

材质内 GI 接收和 NPR 阴影过渡暂不纳入当前实现。这样可以先在朱木古堂中判断 SSGI 的 source、相交、confidence、时域稳定性和整体画面收益，避免把 producer 与 lilToon shading 修改混在一起。

### 4.2 当前参数边界

当前 Ho-SSGI producer 的主要参数已放入 `HoSSGIVolume`：启用、ray count、step count、ray length、thickness、temporal blend、spatial radius、GI intensity、source saturation 和 feature-local debug mode。RendererFeature 上的同名字段只作为没有 Volume 时的兜底配置。材质控制、GI 颜色过渡和 confidence 驱动的 toon 阴影策略留到 producer 稳定后重新评估。

## 5. Producer-only vertical slice

在接入 lilToon 之前，先只实现 Ho-SSGI producer 和 debug 输出。每一步都必须能独立观察，避免把 source、ray、history 和 toon 合成混在一起调。

### 5.1 输入验证

先验证两种已有输入：

1. `HoGeometryBuffer` normal/depth/coverage；
2. URP opaque color。

MetadataBuffer 不参与 GI source，也不需要为 SSGI 增加材质 pass。

当前 producer vertical slice 的 source 组合是：

```text
opaque color        = 已着色 radiance
HoGeometryBuffer    = normal / depth / coverage validity
```

Source validity 编码为：

```text
sourceValid = geometryCoverage
            * normalValid
            * depthValid
```

描边像素的 geometryCoverage 应为 0，即使 opaque color 中仍然能看到描边颜色，也不能进入 source 或 history。

### 5.2 Depth pyramid

沿用 HTrace 的 MIP0..4 结构，但输入改为 Ho-GeometryBuffer depth：

- MIP0：GeometryBuffer depth；
- MIP1..4：用于 Hi-Z ray traversal；
- caster exclusion 在 MIP0 生效；
- sky/invalid depth 保持明确的 invalid 标记。

每个 MIP 都应有 debug 模式。首版由 Ho-SSGI RendererFeature 的 feature-local debug pass 直接显示，不要求接入 DebugTile。

### 5.3 Raw ray result

每个像素至少保留以下临时结果：

- hit flag；
- hit UV；
- hit distance；
- hit surface depth；
- hit surface normal validity；
- hit source color；
- invalid reason（无 coverage、深度不一致、背面、超出屏幕）。

高质量路径的初始设置：full-resolution depth、intersection refine、cosine hemisphere sampling、较高 ray/step 参数、关闭 checkerboard。这里不做另一套低质量算法。

当前 raw trace 已改为 world-space cosine hemisphere ray：从 GeometryBuffer 的线性眼深重建世界位置和世界法线，沿世界空间射线生成端点，再投影到 screen UV；沿投影轨迹用 GeometryBuffer 线性深度 crossing 判断相交，并使用命中面 cosine、距离衰减和 opaque source 过滤。旧的额外视空间轴翻转路径不再保留，因为它会让上下方向和摄像机移动产生错位。

当前 producer 已补上第一段 HTrace 风格的 reservoir 链：raw trace 为每条射线建立候选 reservoir，保存代表方向和 origin normal；Temporal 使用 motion vector、上一帧 GI/深度和 reservoir 做四 tap 重投影合并，并对选中射线做几何与 source lighting validation；Firefly 阶段按局部 luminance statistics 限制异常的 `W`；Spatial 阶段使用稳定的 world-plane Poisson 邻居、平面/法线/深度/Gaussian 权重复用 reservoir，再做 selected-ray re-march validation；最后通过 HDR bilateral denoise 输出 `selectedColor * W`。`Raw Trace` 只显示当前未滤波射线结果，`Raw GI` 显示重采样后的去噪结果，验收时要分开判断坐标/相交错误、reservoir 偏差、去噪效果和残余噪声。

### 5.4 Temporal result

沿用 HTrace 的 motion/depth/normal/history validation，但 history 只存 Ho-SSGI 的 source/GI 语义和 GI reservoir，不复制最终 camera color。当前实现使用上一帧 GI、上一帧法线/线性深度、当前 motion vector、source luminance change、代表射线几何/lighting validation 和 reservoir `Wsum/M` 上限做首版拒绝与合并；仍需补齐：

- history sample count；
- reprojected hit validity；
- depth/normal rejection；
- source luminance change；
- moving object rejection；
- confidence；
- receiver target 重评估；
- render-scale history 坐标转换和 source clamp；
- receiver target 重评估与更稳定的 Poisson/world-plane spatial reuse 权重。

需要分别 debug current raw、reprojected history 和 accumulated result，才能区分 ray 错误与 temporal 拖影。

### 5.5 Denoise result

先复用 HTrace 的高质量思路：

- firefly suppression；
- temporal accumulation；
- 深度/法线引导的 spatial filter；
- spatial occlusion guidance；
- 可选 recurrent blur。

第一版只保留一条主链，参数变化只影响 sample、history 和 filter 半径，不额外维护一套 Low 算法。

### 5.6 Producer debug views

Ho-SSGI 首版只需要 feature-local debug pass 和一个 `DebugMode` 枚举，至少包括：

```text
gi.clean-base-source
gi.source-valid
gi.geometry-coverage
gi.depth-pyramid-mip0..4
gi.ray-hit
gi.ray-distance
gi.raw-radiance
gi.raw-trace
gi.temporal-validity
gi.sample-count
gi.confidence
gi.fallback
gi.filtered-radiance
gi.reservoir-weight
gi.reservoir-M
gi.reservoir-hit
```

调试目标是能够回答：描边有没有进入 source、有没有进入 depth pyramid；ray 是否命中有效 geometry；颜色错误来自 source、ray、history 还是 composite；透明是否被错误写入 caster/receiver/history。

DebugTile/`HoDebugViewRegistry` 只作为后续统一调试入口，不作为 Ho-SSGI producer v1 的依赖。

## 6. Producer 验收顺序

朱木古堂只按以下顺序验收：

1. GeometryBuffer coverage/normal/depth；
2. opaque source 与 source-validity；
3. depth pyramid；
4. 单帧 raw ray result；
5. temporal reprojection；
6. spatial/temporal denoise；
7. APV/sky fallback；
8. `_HoGITexture` 独立 fullscreen composite；
9. 暂不进入 lilToon 内部接收，先完成纯后处理验收。

完成第 8 步之前，不调材质内的 toon transition 或 material mask；当前只调 Volume 的 producer 参数和 fullscreen composite。

## 7. 描边排除

以下规则必须在 Ho-SSGI 第一版就成立：

- opaque camera color 可以作为 radiance source，但 outline 必须被 GeometryBuffer coverage 拒绝；
- outline 不进入 GI source 的 hit sample；
- outline 不接收 GI；
- caster 排除应在 depth pyramid 生成前生效，等价于 HTrace 的 `ExcludeCastingMask`；
- receiver 排除应在 GI output/composite 阶段生效，等价于 HTrace 的 `ExcludeReceivingMask`；
- 深度/法线不一致时拒绝 screen hit；
- 无有效几何覆盖的像素不参与历史累积；
- camera color、source、GI、confidence 都要能单独 debug。

## 8. 朱木古堂验收顺序

1. HoGeometryBuffer + source debug；
2. 单帧 SSGI，不开 temporal；
3. temporal + depth/normal rejection；
4. spatial filter 和 firefly clamp；
5. 红墙/白地面颜色反弹；
6. 室内遮挡和近距离间接光；
7. 角色移动、头发和外扩描边；
8. SSS、OIT、平面反射和 CharacterSpecialization；
9. 关闭 Ho-SSGI 后确认无 GI、无上一帧残留；
10. 用 RenderDoc/Frame Debugger 对照 HTrace 的输入、输出和 GPU 成本。

完成标准是：HTrace 的独立 GBuffer/prepass 不再是必需输入；Ho-SSGI 能稳定输出适合 lilToon 画面的 GI；source 可以从 opaque camera color 平滑替换为 `HoGI Lit Source`；关闭时安全回退；后续替换 Brixelizer 时不改 `_HoGITexture` 契约。

## 9. Brixelizer 只保留为后续替换

Ho-SSGI 输出契约稳定后，再评估 Brixelizer 的世界空间 SDF、screen probe 和 radiance cache。它只替换 GI producer，不改变 `_HoGITexture` 和 confidence。

当前不展开 DDGI、Lumen 或硬件 RTGI 的实现规划。
