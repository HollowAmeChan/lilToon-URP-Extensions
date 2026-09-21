# Ho-GTAO / Ho-SSGI ReSTIR 共享层评估

> 日期：2026-09-09（2026 文档审核核对：结论仍然成立，已按当前代码更新事实性细节）
>
> 结论：当前不合并 GTAO 和 SSGI，也不新增一个“万能 ReSTIR RendererFeature”。先在 Ho-SSGI 内完成 HTrace 风格的 GI reservoir、temporal/spatial reuse 和 validation。只有当 GTAO、SSGI 或后续 DI 确实重复消耗同一套几何/Hi-Z资源时，才抽出共享资源 Feature。
>
> 本次核对到的当前事实（结论未变）：Ho-GTAO 仍**没有 reservoir**（history = AO+velocity / depth / normal 三对 ping-pong）；Ho-SSGI 的 reservoir 以 RGBAHalf 保存 color/aux（另有代表方向的 ray 纹理）；HTrace 侧 `HCommonSSGI.hlsl:8-9` 仍是 `ENABLE_SPATIAL_RESTIR 1` / `ENABLE_TEMPORAL_RESTIR 1`（§7 的判断不变）。

## 1. 先纠正当前语义

当前 Ho-GTAO **没有 ReSTIR reservoir**。它的实际链路是：

```text
HoGeometryBuffer normal/depth
    -> depth pyramid MIP0..3
    -> Bitmask ray march（32-bin visibility bitmask；Ho 只实现这一种 tracing）
    -> AO temporal history + motion/depth validation
    -> bilateral spatial filter
    -> AO output
```

对应实现是 `Runtime/GTAO/HoGTAORendererFeature.cs` 和 `Runtime/GTAO/Shaders/HoGTAO.shader`。它保存的是 AO 标量、history sample count、法线和深度，不保存 `Wsum/M/W`、代表样本方向或代表样本距离，也没有 weighted reservoir update/merge。

当前 Ho-SSGI 才有第一段真正的 reservoir 链：

```text
opaque camera source + GeometryBuffer
    -> 每条 screen-space GI ray 生成 candidate
    -> GI reservoir（Color/Wsum/M/target/hit/distance）
    -> temporal reservoir merge
    -> firefly W clamp
    -> 8 邻居 spatial reservoir reuse
    -> GI resolve
```

实现位于 `Runtime/SSGI/HoSSGIRendererFeature.cs` 和 `Runtime/SSGI/Shaders/HoSSGI.shader`。当前布局用 RGBAHalf 保存 reservoir color/aux（另有代表方向的 ray 纹理），仍是 HTrace 的第一段移植，不是完整的 HTrace 打包格式。

HTrace 的情况容易造成误解：它在 `HRenderSSGI.compute` 中同时生成 `TemporalReservoir` 和 `OcclusionReservoir`。后者是 SSGI tracing 的近距离遮挡辅助，不等于一个独立 Ho-GTAO producer。随后 `HRestirSSGI.compute` 会把 GI reservoir、AO/occlusion、temporal invalidity 和 denoiser guidance 一起处理。HTrace 这样做是为了让同一条 GI ray 的命中、遮挡和滤波互相提供验证信息。

## 2. 两者真正共用的语义

### 2.1 几何真值

这是现在最稳定、也最值得共用的部分：

```text
normal + linear depth + coverage/validity
```

两者都应从 HoGeometryBuffer 读取。它同时解决：

- 屏幕空间位置重建；
- 法线一致性；
- sky/invalid 像素排除；
- 描边外扩壳排除；
- temporal disocclusion；
- spatial plane/normal 权重。

这部分已经属于 `HoGeometryBufferRenderGraphResources`，不需要再复制一份。

### 2.2 Hi-Z / depth pyramid

GTAO 当前在 `HoGTAORendererFeature.CreateDepthPyramid` 中自己创建 MIP0..3；SSGI 当前的基础实现直接采样 GeometryBuffer depth，没有独立 Hi-Z producer。未来如果 SSGI 改成 HTrace 的完整 Hi-Z march，GTAO、SSGI 和 DI 都可以读取同一套深度金字塔。

这里能共享的是：

- pyramid 的尺寸、格式和 mip 生命周期；
- invalid/sky 的约定；
- reversed-Z/far-depth 处理；
- RenderGraph handle 和全局绑定；
- caster exclusion 后的 MIP0 输入。

这里不能共享的是每个算法的采样半径、LOD选择、命中判定和遮挡解释。GTAO 需要 horizon visibility，SSGI 需要 ray hit/camera radiance，DI 可能需要 visibility query。

### 2.3 相机帧上下文与 history reset

两者都需要以下帧级信息：

- camera identity；
- width/height/render scale/XR slices；
- frame index；
- motion vector 是否有效；
- camera cut、尺寸变化、渲染器切换后的 history invalidation；
- ping-pong 的生命周期和资源回收。

现在二者分别维护 `HoGTAOHistory` 和 `HoSSGIHistory`。这说明“生命周期管理”有共用潜力，但不说明两种 history 的纹理可以合并。AO history 是 AO 标量/辅助字段，GI history 是 radiance/reservoir，格式和清除条件不同。

### 2.4 重投影与邻域验证原语

可以共用的是数学原语或 shader include：

- motion 得到 previous UV；
- previous UV 越界拒绝；
- depth/normal/coverage agreement；
- world/view plane distance；
- normal dot；
- Gaussian/Poisson 邻居权重；
- edge-safe sample coordinate；
- history confidence/sample-count 的基本处理。

这些函数适合放到类似 `Runtime/ScreenSpace/HoScreenSpaceValidation.hlsl` 的公共 include。它们不应变成一个 Feature，因为函数本身不生产 RenderGraph 资源，也无法决定 GI 与 AO 的接受条件。

### 2.5 随机序列和邻居分布

两者都需要稳定的屏幕空间随机性，但分布不同：

- GTAO 使用 interleaved gradient noise 和 horizon slice rotation；
- SSGI 使用 cosine hemisphere、ray sequence 和 reservoir random；
- HTrace spatial ReSTIR 使用 8 个 Poisson/world-plane 邻居；
- 后续 DI 可能需要蓝噪声、低差异 light sample 和 RIS 排序。

可以共享 frame seed、蓝噪声纹理和一个可复用的 Poisson buffer；不能让一个固定分布同时替代 AO slice、GI ray 和 DI light candidates。

### 2.6 ReSTIR 数学

weighted reservoir sampling 的数学形式可以共用：

```text
Wsum += sampleWeight
M    += sampleM
replace selected sample with sampleWeight / Wsum probability
W     = Wsum / (M * target(selected))
```

但它适合做 `HoReSTIRCommon.hlsl` 的函数库，不适合由一个 RendererFeature 持有通用 reservoir 纹理。HLSL 里的 reservoir payload 仍要按算法分别定义：

| 算法 | 代表样本 | target/权重 | 需要的验证 |
|---|---|---|---|
| SSGI | 命中 radiance、方向、距离、hit | radiance 与 receiver response | hit depth、法线、lighting、selected-ray visibility |
| AO | 遮挡值、方向、距离 | visibility/occlusion 统计 | horizon/occlusion consistency |
| DI | light id/位置/方向、PDF、visibility | target contribution / proposal PDF | light visibility、reservoir PDF、shadow |

即使三者都有 `Wsum/M/W`，它们也不是同一个 reservoir 类型。

## 3. 不能抽成一个算法 Feature 的内容

以下内容必须留在各自 producer：

1. candidate 的来源。SSGI 来自 screen-space hit radiance，AO 来自遮挡/horizon，DI 来自灯光候选；
2. target function。SSGI 依赖 source radiance 和接收面响应，DI 依赖 light PDF，AO 不是 radiance estimator；
3. payload layout。SSGI 要保留 hit direction/distance，DI 要保留 light identity/PDF，AO 只需遮挡语义；
4. history 的有效性。SSGI 要验证 hit lighting，AO 主要验证几何/遮挡，DI 还要验证灯光和 shadow 变化；
5. 输出契约。SSGI 输出 `_HoGITexture`，GTAO 输出 AO visibility，DI 将来可能输出 direct-light contribution；
6. pass 时机。SSGI 必须等待 lit source，GTAO 可以更早执行，DI 是否提前取决于 light list 和 shadow source。

如果把这些内容塞进一个 `HoReSTIRRendererFeature`，结果会是一个按枚举分支的算法总管，RendererGraph 依赖、history reset 和资源格式都会被互相绑死。它既不能真正降低 GPU 成本，也会让以后接 Brixelizer 或硬件 RT 时无法复用清晰的接口。

## 4. 如果未来合并，名称应该怎么定

名称必须描述它真正拥有的资源，而不是描述所有算法。

### 不推荐

- `Ho-RTBuffer`：容易被理解成硬件 RT buffer，也没有说明它是否拥有 Hi-Z、history 或 reservoir；
- `HoReSTIRRendererFeature`：ReSTIR 是采样算法，不是所有 AO/RT 路径都必须使用的公共资源；
- `HoLightingRendererFeature`：范围过大，会把 source、shadow、GI、AO、DI 的所有权混在一起。

### 推荐候选

如果它只服务当前屏幕空间路径，并且已经实际创建共享 Hi-Z / ray-query 工作资源，推荐：

```text
HoScreenSpaceRayQueryRendererFeature
HoScreenSpaceRayQueryResources
```

它可以拥有 GeometryBuffer 之后的共享 Hi-Z、screen-space query 输入、motion/seed 上下文和 frame-local RenderGraph handles；SSGI/GTAO/DI 仍然拥有各自 reservoir。

如果抽取的第一步只有相机级生命周期、motion、history reset、邻居分布和 RenderGraph 契约，而不生产 Hi-Z，则用 `HoScreenSpaceContext` / `HoScreenSpaceContextResources` 更准确，也不必把它伪装成一个 RendererFeature。

如果未来确实要统一屏幕空间、硬件 RT、Brixelizer 等多种实现，则更高层可以叫：

```text
HoRayTracingContext
HoRayTracingResources
```

但那应该是算法无关的上下文接口，不能假设所有实现都能共享同一张 depth pyramid 或同一套 reservoir。当前项目还没有达到需要建立这个高层接口的阶段。

## 5. 是否现在抽取

结论是：**现在不抽 GTAO，也不新建合并 Feature。**

原因：

- Ho-GTAO 当前没有 ReSTIR，抽取“GTAO ReSTIR”没有实际对象；
- Ho-SSGI 的 ReSTIR 仍在补 HTrace 的 temporal taps、history cap、selected-ray validation 和完整 denoiser；
- SSGI 当前没有与 GTAO 共享的独立 Hi-Z handle，暂时抽不出能立刻减少重复工作的资源；
- 新 Feature 会引入 Renderer Feature 列表顺序、RenderGraph producer/consumer 声明、XR descriptor 和 history reset 的新耦合；
- 目前只有一个 ReSTIR producer，抽象收益小于调试成本。

建议的抽取触发条件是：

1. SSGI 改成完整 Hi-Z traversal 后，与 GTAO 的 depth pyramid 重复生成已经通过 RenderDoc/Profiler 证明是成本热点；
2. GTAO 需要使用同一套 Hi-Z、motion、Poisson/blue-noise context；
3. DI 或其他未来 screen-space ray feature 开始复用这些资源；
4. 需要统一跨 camera、XR、dynamic resolution 的资源生命周期。

满足条件后，先抽 `HoScreenSpaceContextResources`，再把 GTAO 的 `CreateDepthPyramid` 迁移成共享 Hi-Z producer；不要先抽 GI/AO/DI reservoir。

## 6. 当前 Ho-SSGI 的执行边界

现阶段只推进 Ho-SSGI：

```text
HoGeometryBuffer
    -> opaque lit source
    -> Ho-SSGI candidate reservoir
    -> temporal reservoir + strict reprojection validation
    -> firefly W clamp
    -> spatial reservoir reuse
    -> selected-ray validation
    -> GI denoiser
    -> _HoGITexture
```

Ho-GTAO 保持独立。它可以继续提供 AO output 作为将来 SSGI denoiser 的 guidance，但这属于“读取 AO 语义输出”，不代表共享 AO reservoir，也不应让 SSGI 反向接管 GTAO 的 history。

当前 Ho-SSGI 已经完成第一批验证增强：reservoir 保存 Color、Wsum/M/target、Direction、Distance、OriginNormal、HitFound；history 有 HTrace 风格的 M 上限；temporal 会做 selected-ray 的几何和 source lighting validation，spatial 使用 world-plane Poisson 邻居并做 selected-ray re-march validation，之后还有一层 HDR bilateral denoise。Volume 已经能分别控制时域/空间 reservoir 重用、时域/空间验证和 Firefly。

后续优先级：

1. 完善当前四 tap history 的 render-scale/history-depth disocclusion 和 history source clamp；
2. 优化 spatial 的 Poisson/world-plane 邻居分布与重采样权重；当前已经有独立的 selected-ray re-march validation 阶段；
3. 保留 firefly 与 temporal/spatial denoise 的独立 debug，并用 Volume 的 temporal/spatial reuse 开关做 A/B；
4. 朱木古堂中确认红墙反弹、灯光变化、摄像机上下移动和描边排除；
5. 以上稳定后，再评估是否把 Hi-Z producer 抽给 GTAO 共用。

最终目标不是让 GTAO 和 SSGI 使用同一套 reservoir，而是让它们在需要时共享几何、可见性和相机上下文，同时保持 GI/AO/DI 的估计器语义独立。

## 7. HTrace 是否真的启用了 ReSTIR

结论：**正常 HTrace SSGI 路径确实一直使用 ReSTIR，而且没有公开的总开关。**

### 7.1 代码证据

HTrace 的 `Resources/HTraceSSGI/Includes/HCommonSSGI.hlsl` 直接定义：

```hlsl
#define ENABLE_SPATIAL_RESTIR 1
#define ENABLE_TEMPORAL_RESTIR 1
```

Volume/Profile 的“ReSTIR Validation”区域只有这些参数：

```text
HalfStepValidation
SpatialOcclusionValidation
TemporalLightingValidation
TemporalOcclusionValidation
```

它们只切换 validation 关键字，不会关闭 reservoir temporal/spatial resampling。`FireflySuppression` 也只是单独的权重抑制阶段。

在 `Scripts/Passes/Shared/SSGI.cs` 中，以下调度每帧都会发生：

```text
HRenderSSGI.TraceSSGI
HReSTIR.TemporalResampling
HReSTIR.SpatialResampling
HReSTIR.SpatialValidation
HDenoiser.TemporalAccumulation
HDenoiser.SpatialFilter1/2
```

Firefly kernel 只有在 `DenoisingSettings.FireflySuppression` 为 true 时才 dispatch，但这不影响 ReSTIR temporal/spatial 本身。

### 7.2 关闭验证不等于关闭 ReSTIR

把三个 validation 选项都关掉，只会减少：

- temporal selected-ray occlusion validation；
- temporal lighting change validation；
- spatial occlusion guidance/validation。

reservoir 仍然会合并当前候选、历史候选和空间邻居。这个配置可以用来观察“验证对稳定性和反应速度的影响”，不能作为“无 ReSTIR”基线。

### 7.3 临时画面对比方法

最快的实验是在外部 HTrace 包的 `HCommonSSGI.hlsl` 中临时改为：

```hlsl
#define ENABLE_SPATIAL_RESTIR 0
#define ENABLE_TEMPORAL_RESTIR 0
```

同时关闭 Volume 中的三个 validation 和 Firefly。这样会让 temporal 不再合并历史 reservoir，spatial 不再把邻居 reservoir 加入候选；后面的 spatial validation 和普通 denoiser 仍会运行。因此它适合比较画面中 ReSTIR 对连续性、噪声和拖影的贡献，但**不能比较 GPU 成本**，因为 `HReSTIR` kernel 仍然会被 dispatch。

这也不是一个完全干净的“纯 raw trace”模式：HRenderSSGI 产生的是 reservoir candidate，最终 radiance 仍由后续 spatial validation/denoiser 路径 resolve。HTrace 的 raw trace 输出并没有直接作为最终 camera radiance 写出，`HRenderSSGI.compute` 中的直接 radiance output 仍是注释状态。

### 7.4 真正的无 ReSTIR baseline 需要什么

如果要做严格对照，需要在 HTrace 分支里增加一个正式设置，而不只是改宏：

1. Trace 阶段额外写出 `sumRadiance / rayCount` 或等价的 raw GI texture；
2. 绕过 `HReSTIR.TemporalResampling`、Firefly、SpatialResampling/Validation；
3. 让 denoiser 直接消费 raw GI 或只保留独立的普通 temporal/spatial denoise；
4. 保持 camera color、GeometryBuffer/depth 和 composite 完全不变。

这才是“同一 tracing 输入下，ReSTIR 开/关”的可解释 A/B 测试。当前 HTrace 包没有提供该模式，不能从 Volume Inspector 直接完成。

对 Ho-SSGI 的意义是：在判断是否继续移植 ReSTIR 时，应比较三组结果，而不是只比较开关前后：

```text
A. raw candidate resolve
B. temporal reservoir reuse
C. temporal + spatial reservoir + validation + denoiser
```

当前 Ho-SSGI 已经有 A 的 reservoir resolve和 B/C 的第一段实现，但还没有 HTrace 那种严格的 A/B 调试模式。下一步应先给 Ho-SSGI 加可控的 `TemporalReuse`、`SpatialReuse` 和 `Validation` debug 开关，再决定是否继续补齐 HTrace 的完整 ReSTIR，而不是把所有阶段绑成一个不可拆的总开关。
