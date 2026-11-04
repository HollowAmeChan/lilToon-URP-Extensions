# Ho-RT 功能路线图：RTAO、反射/折射与性能

> 状态：**长期路线图 / 尚未实现**，建立于 2026-11-04。本文定义 RTAO 及后续硬件 RT 反射、折射的产品边界、阶段与验收；写入路线图不代表代码、资产、GPU 预算或画质已交付。
>
> 架构权威仍是 [Ho-RT](Ho-RT.md)；当前 AO 消费合同与 GTAO 现状见 [Ho-GTAO](Ho-GTAO.md)；DI / GI 的实际支持域见 [Ho-RTDI](Ho-RTDI.md) 与 [Ho-RTGI](Ho-RTGI.md)；反射材质与来源合同见 [ReflectionPipelineDesign](ReflectionPipelineDesign.md)。

## 1. 目标与边界

用户希望将 RT 系列发展为一套协作的实时光照系统：先处理 GTAO 的高成本，继而完成反射与折射，并持续降低整套 RT 功能的 GPU 时间和资源占用。本路线图按这一方向组织，同时将“看起来能复用”与“已证明复用”分开。

### 1.1 目标结构：前置表面语义 + RT producer + 独立材质 consumer

当前应优先评估的不是“把所有材质效果做成一个完整 Deferred G-buffer”，而是把已有 Surface/Geometry Buffer 明确当成**前置表面语义输入层**：

```text
Ho-RT CPU scene snapshot / AS lifetime
    -> GB / OB / SB finalize current opaque surface inputs
    -> unified Ho-RTLighting pre-forward producer stage
         shared primary rays / visibility queries / light samples
         separate typed products: AO / DI / GI / future reflection
    -> material consumers remain independent
         existing lilToon Forward retains its current compatibility contracts
         new HoShader RT material family consumes typed RT products in its own Forward/NPR shader
    -> optional late consumer/composite stage
         consumes only products deliberately defined as camera-color contributions
    -> transparents / ScreenProcess / ImageProcess as ordered by contract
```

这是一种 **buffer-fed hybrid forward** / 前置 RT producer 架构，不等于把每种效果拆成逐层屏幕图，也不要求 RTDI/RTGI 先等 Forward camera color。A1/A2 后，RTDI 与 RTGI producer 固定在 `BeforeRenderingOpaques`，实际读取 Ho-RT view、SB primary、灯光/环境及各自历史；读取 `activeColorTexture` 的是 `BeforeRenderingTransparents` ScreenProcess RT-light consumer。因此 producer 的前置时序与 camera-color 消费已经明确分离。

职责边界：

- **GB / OB / SB** 生产本相机 opaque surface facts 与已有的主色、法线、smoothness/metallic map、GSAA 等表面参数；它们不是 lighting integrator，也不必装下每个材质效果开关。已有通道只覆盖当前 writer 的合同，不自动等于任意自定义材质都能匹配其 Forward 结果。
- **Ho-RT** 在 opaque Forward 前准备 renderer/camera 域、共享几何/姿态与查询 view，并为 RTLighting 建立显式 pass / RenderGraph 依赖。
- **Ho-RTLighting** 是统一调度与共享射线层：可以共用 primary surface、AS query、blue noise、motion、reset 和 transient ray product；但 AO、DI、GI、reflection 仍分别拥有 estimator、reservoir、NRD history、输出和有效性合同。
- **RT products** 尽量在 Forward 前消费 GB/SB 与 world scene，发布带有效性、编码、时域和相机域的 typed product。材质当帧需要的 AO visibility 必须在 Forward 前发布。
- **现有 lilToon** 保持通用 NPR/Forward 职责；不再为 RTDI/RTGI/RT reflection 增加新的 Ho 专属灯光结构、RT ramp 或 composite 消费逻辑。当前 `_HoAOTexture` 路径保留为既有兼容合同，不代表未来 RT 产品都进入 lilToon。
- **HoShader RT-aware 材质族** 是拟新增的独立材质/Shader 包与 consumer，专门消费 Ho-RT typed products，并在自身完整 Forward/NPR evaluation 中决定 direct / indirect / AO / reflection 怎样进入该材质响应。它是新的一类，不假设当前 HoShader 的 Skin、Fur 或其它包已经具备 RT 能力；也不拆成 ramp、影色等 fullscreen effects。
- **late consumer/composite** 是可选 consumer，仅把明确约定为 radiance contribution 的 RT product 组合进 camera color。它不拥有 ray generation 或 GI/DI estimator；若输出需驱动 NPR ramp，应由 RT-aware material consumer 在材质着色内读取合适输入，而非晚期合成已着色 RGB。

ScreenProcess 现在拥有一个独立的 **RT-light consumer pass**，不把同帧 RT product 塞进静态 `ScreenProcessLayer.texture`。该 pass 在 `BeforeRenderingTransparents` 消费 RenderGraph `ContextItem` / compatibility registry 发布的 typed handles，仍处于线性 HDR 场景域；普通 ScreenProcess layer stack 继续固定在 `AfterRenderingPostProcessing`。因此 RTDI/RTGI 的 producer 与 camera-color consumer 已经解耦，但透明、fog、HDR blend 和深度 gate 的语义仍由这个专用 consumer 明确承担。

HoShader 当前已有独立 URP UPM 材质包和各自的 shader/Editor 结构，但没有 RT-aware 材质类别。本路线提出新增材质类别，不表示既有 Skin/Fur 实现已经接入 RT。是否单独拆成新的 Git 仓库或作为 HoShader 下的新 UPM 包，在该新类别正式启动时决定。

只有在未来明确要求**不再执行 Forward**、改由统一 Deferred resolve 完成新材质族时，才需要另开完整延迟着色项目；RTAO/DI/GI 前置化和 RT product consumer 解耦本身不以 full deferred 为前提。

目标推进顺序固定为：

1. **先抽离合成入口**：移除 HoRTDI/HoRTGI 自己持有的 camera-color composite entry，把它们的输出交给 ScreenProcess 的专用 RT consumer contract；这一步不改变 producer 算法。
2. **再前移完整 RT producer**：把 RTDI/RTGI 的生产链从“Forward 后才运行”改成 GB/SB/Ho-RT 后、opaque Forward 前；ScreenProcess 只消费已经发布的 RT product。
3. **再建立 HoShader RT 材质族**：需要 RT-aware NPR 的材质在独立包中消费 typed product；不把 RT 逻辑回灌到 lilToon。
4. **最后做 RTAO**：在前置 RT 调度和产品/consumer 边界稳定后，把 RTAO 接到同一套 producer 基础设施。

当前两个旧 RT composite pass 源文件仍保留作回退参考，但已不再由 Feature enqueue；第 1 步已经落成“consumer contract + 迁移”。待隔离夹具通过 A1/A2 的 RenderGraph/compat 对照后，再删除 `HoRTDICompositePass` / `HoRTGICompositePass` 及旧 shader 资产。

RT 产品按 consumer ownership 与消费时机分流，不能把全部结果统称“晚叠加”：

| 产品类别 | 例子 | 正确消费边界 |
| --- | --- | --- |
| Forward 前可见度/遮挡 | AO visibility、Ho-RTShadow per-light Cast | Forward 前生产。现有 lilToon 只保留当前 AO/HoCast 兼容接口；新 HoShader RT 材质按自有 Ramp/receive contract 消费 |
| 预先计算的照明贡献 | RTDI radiance、RTGI diffuse radiance | 可以 Forward 前生产。新材质 consumer 只能按产品真实语义接入；已包含 BRDF/albedo 的最终 radiance 不能直接冒充入射照明或 toon ramp 参数 |
| 独立艺术叠加 | 当前 RTDI additive、RTGI additive | Forward 后合成，仅用于明确的兼容/艺术模式；不等于驱动 NPR ramp，也可能与原 Forward 照明重复 |

HoShader 新材质若要让 RT lighting 参与 NPR response，应在其自有完整 Forward evaluation 内消费适配后的 RT product；不把这项依赖扩散到 lilToon 主干。

主要目标：

1. 提供世界空间 RTAO，保留现有 lilToon AO 材质语义与输出通道。
2. 让 RTDI、RTGI、RTAO 共享 Ho-RT 的场景准备、几何快照、可兼容的求交 view 和生命周期；每个效果继续拥有正确的估计器、信号、历史及输出。
3. 仅在样本方向、PDF、表面法线、距离域、遮罩与时序严格兼容时共享射线结果；不以“同一张图”或“同一个 AS”推断可以复用。
4. 在新增 RTAO、反射、折射的同时建立稳定的 GPU 与显存账本；性能优化贯穿每一阶段，而不是等所有功能结束才启动。

不在本文承诺：RTAO 必然比 GTAO 快、RTAO 自动替代 DI/GI 遮挡、一个 AS view 适用于所有材质 profile、RT 反射自动替代 PLR/SSR/Probe、或任意透明材质都能被正确折射。

## 2. 现状与证据状态

| 项目 | 仓库当前事实 | 路线图处理 |
| --- | --- | --- |
| GTAO 输出 | `Ho-GTAO` 发布 `_HoAOTexture`，语义为 0..1 visibility，1 表示无遮挡；材质端不依赖算法名 | RTAO 沿用该消费者合同，不复制或改写 lilToon AO 逻辑 |
| GTAO 计算分辨率 | `HoGTAOResolution` 有 Full/Half/Quarter 选项，但目前 RenderGraph 按 camera target 全分辨率分配和计算 | 先建立配对基线；实际低分辨率 GTAO 是必须的对照，而非仅看 Volume 选项 |
| lilToon AO 用途 | 同一 `fd.aoVis` 同时推动 toon ramp 偏移，并在主光照着色后对 `fd.col.rgb` 作 AOColor/AODarkStrength 整体压暗；ShadowBorderMask 与 AO Mask 也参与组合 | 保持两个输出和现有材质参数；算法输出不得预乘艺术强度 |
| RT 场景 | Ho-RT 按 Renderer/Camera 管理 domain、共享几何/蒙皮快照和 fence；view 可因 hit-material / 求交规则不同而分开 | 复用 host 与 pose cache，不预设每个消费者共用同一 AS |
| RTGI 采样 | 当前每个有效主表面生成一条 cosine-hemisphere 次级方向，绕 `shadingNormalWS` 建基；A2 producer 位于 `BeforeRenderingOpaques`，ScreenProcess consumer 仍在透明前 | 有潜在同方向样本复用，但需解决 opaque 前 AO 时序、法线域与距离域匹配 |
| RTDI / RTGI producer 输入与时序 | RenderGraph producer 直接读 Ho-RT view、`HoRTPrimarySurfaceInputs`/SB 与灯光/环境；正常相机颜色只由后置 composite 消费 | 前移 producer 是可评估的调度重构，不是 GBuffer 缺少完整 Forward color 的材质依赖 |
| RTDI 遮挡 | RTDI 对最终选中的解析灯/环境样本做方向相关的 visibility；它不是通用半球可见度 | 不把标量 AO 乘到已包含 visibility 的 direct-light 输出 |
| 性能报告 | 邻近审计报告 GTAO 超过 200 ms，并称 RTDI/RTGI 约低一个数量级；当前消息未附同一相机、分辨率、GPU marker 与原始报告 | 暂记为用户提供的观察/性能目标。导入原始 capture 后再冻结基线，不能据不同测试尺寸和场景宣布算法胜负 |
| RTGI 历史计时 | 仓库曾记录 RTX 4060 / D3D12、256²约 1.19 ms、512²约 3.9 ms 的带插桩 GI+NRD 区间 | 该夹具和目标画面不同，不可直接与邻近审计的 GTAO 数值相减或比率化 |
| 参考工程 | 历史实现记录使用 UnityPathTracing 6000.3.15 与固定 NRD/RTXDI 版本；本次检查发现文档记录的 `.codex-research/UnityPathTracing-6000.3.15` 副本当前不在此 checkout | A0 先定位/恢复实际参考副本并核对版本、二进制与输入合同；不复制整套 Native renderer 或混用新版 ABI |

GTAO 超过 200 ms 是严重的性能信号，值得优先处理；但当前仓库没有邻近审计的原始 timestamp / capture，因此不会把它伪装成已复核的产品基准。类似地，RTDI 的现行文档仍标注有效目标场景 GPU 预算未完成。**A0 的首件产物是复核数据与可重放配置，不是调整算法参数。**

## 3. RTAO 产品定义

### 3.1 光照语义

首版 RTAO 定义为有限半径内的 cosine-weighted hemispherical accessibility：

```text
V(p) = E_w [ visible(p, w, aoRadius) ]
```

每条样本的 `visible` 为：在 AO 半径内没有有效遮挡命中时取 1，否则取 0。使用 cosine hemisphere 采样时，样本均值估计可见比例；输出保持 visibility 极性。RTAO 不着色命中材质、不估计辐射、不作为逐灯 Cast，也不添加多次反弹。

默认候选采用几何法线定义有效半球和几何 coverage gate；是否增加基于着色法线的艺术选项，须单独验证掠射角、normal map 和 GI 样本兼容性。描边视觉壳、无效 coverage、未提交/过期相机资源均不得进入物理 RTAO。

### 3.2 公共输出与消费者

| 接口 | 约定 |
| --- | --- |
| `_HoAOTexture` / `ao` 通道 | 全分辨率、单采样 visibility；1=无遮挡。由 Ho-RTLighting 发布，供现有 lilToon 兼容路径、新 HoShader 材质和已登记 AOV 使用 |
| Raw / filtered | RTAO 必须保留可独立查看的 raw 与最终 filtered 输出；debug 名称不得覆盖生产纹理 |
| 强度、对比、颜色、mask | 继续由 lilToon AO Map、AO Mask、`_AOContrast`、`_AOColor`、`_AODarkStrength`、`_AOStrength` 决定 |
| 缺失/关闭/不支持 | 输出白色无 AO 或由显式 AO source 选择使用 GTAO fallback；绝不能复用上一相机纹理 |
| producer 选择 | 每相机只允许一个 AO producer 发布 `_HoAOTexture`。目标路线是由 Ho-RTLighting 统一生产 AO product；GTAO 只作为显式 fallback/对照，不靠 Renderer Feature 顺序争夺全局写入 |

首版不要求材质 shader 新增 RTAO keyword 或改用 AO 色调。算法交换应只改变 `_HoAOTexture` 的生产端，不改变 lilToon 的 Ramp 与全局压暗路径。

### 3.3 候选渲染链

```text
Ho-RTLighting 当前相机 primary-ray stage
    -> 低分辨率 cosine-hemisphere / visibility rays
    -> shared transient ray result: direction, pdf, hit status, distance, surface validity
    -> AO visibility product (and, when GI is enabled, GI secondary-surface product)
    -> separate temporal / denoise / history contracts
    -> raw scalar AO + hit-distance/validity
    -> temporal reconstruction / independently qualified denoiser
    -> depth/normal/coverage-guided upsample
    -> full-resolution _HoAOTexture
    -> existing lilToon AO-map/mask/ramp/darken composition
```

MVP 候选预算为**每有效低分辨率像素每帧 1 条射线**，优先测试半线性分辨率（半宽、半高，即全分辨率四分之一像素数），并将四分之一线性分辨率作为低档对照。比较全分辨率单样本只作为标尺，不默认进入产品。采样数、半径、bias、历史预算、denoiser 均由配对测试决定，本文不冻结未经测量的默认值。

实现必须遵守：

- AO product stage 在 opaque lilToon 前完成，因为 `_HoAOTexture` 在 Forward 着色期间采样；它属于 Ho-RTLighting 的调度合同，不再要求一个独立 RTAO RendererFeature。Feature 列表及 RenderGraph 依赖遵照 [RendererFeature 顺序契约](Ho-RenderFeatureOrdering.md)。
- 屏幕 primary surface 的几何、法线、深度和 coverage 来源显式声明。输出降采样不等于把 GB/SB 改成低分辨率。
- 对 opaque blocker 使用尽早结束的 boolean occlusion query；不要为 RTAO 每条射线求完整 hit material / BRDF。
- Cutout 需要与 RTDI/Ho-RT 已支持的 alpha adapter 一致地做 any-hit。未知 alpha/不支持几何不能被误判为天空无遮挡；status、miss、unsupported 与 invalid ray 分开。
- AO radius 是世界距离，ray bias 与 surface-normal 域必须有一致的定义；不同色彩空间不得改变 visibility。
- RenderGraph 与 compatibility 两条路径使用同一信号合同；首个生产支持域限定 D3D12 DXR、Win64 Mono、Base camera，Scene View、XR、动态分辨率逐项验收后再声明支持。

## 4. RTAO 与 RTDI / RTGI 联合设计

### 4.1 禁止把 AO 当作通用乘数

RTDI 的最终直射光 visibility 是针对 RTXDI 最终选中的光样本进行的。RTAO 的 cosine hemisphere accessibility 对应不同方向分布；把 `V_RTAO` 再乘到已通过光线遮挡估计的 RTDI diffuse/specular 上，会重复遮挡，也无法保留 direct light 的方向性。因此：

- RTDI raw/filtered direct radiance 默认不乘 RTAO。
- RTAO 仍可按现有 lilToon 材质规则影响 forward 材质 Ramp 与 AODark 输出。
- 只有未来有明确 estimator/PDF 证明的 sample reuse 或环境项定义，才允许 DI 消费 visibility；必须有无偏/受控偏差测试及独立开关。

RTGI 的一次间接漫反射路径也已查询主表面到次级表面的可见性，并查询最终连接 visibility。再对整张 GI 结果盲乘 RTAO 会重复压暗。默认同样不做后乘。

### 4.2 AO 作为 GI primary-ray 副产物

现有 GI `GenerateSecondary` 使用 cosine hemisphere，理论上同一条主表面方向与首命中距离可以贡献两个量：

1. RTGI：命中次级表面后继续估计其直射/环境辐射。
2. RTAO：若首有效 blocker 距离小于 `aoRadius`，计作遮挡；超过半径或半径内 miss，则计作无遮挡。

因此最合理的实现不是让“AO 读取 GI 最终颜色”，而是让 Ho-RTLighting 先生成一个共享的 primary-ray transient product，再由两个独立 product builder 消费：

```text
PrimaryRayProduct
  -> AOAccessibilityProduct
  -> GISecondarySurface / GI radiance product
```

这样 AO 可以成为 GI primary-ray 阶段的副产物，但 AO 仍保留自己的 scalar visibility history、边界 rejection 和输出；GI 仍保留自己的 secondary reservoir、radiance history 和 RELAX 输入。共享的是 ray/query 结果，不是两种效果的数值历史。

这不是当前即插即用的共享：

- 当前 RTGI 整段生产通常在 opaque 后；RTAO 的材质输入必须在 opaque 前。只有将 GI 的 `GenerateSecondary` 拆成早期生产与后续 Shade/Temporal/Spatial/Final，或建立等价的帧内 sample contract，才可能同帧提供 AO。
- RTGI 当前方向围绕着色法线生成，且对落在几何法线背侧的方向不发射射线；首版 RTAO 候选使用几何法线。法线域、PDF、bias 和对背侧样本的处理不一致时，不能共享结果。
- RTAO 与 GI 的输出分辨率、有效 surface、alpha adapter、世界 revision、sample frame/seed 及 AO cutoff 必须一致或有经过证明的映射。
- 如果为了早期 AO 将 GI 的 raygen 前移，必须单独量化 pass split、buffer 生命周期和 Graph ordering 的成本；不得把 RTAO 的单项节省写成系统总耗时下降。

因此共享 primary-ray product 是当前路线的默认研究方向；若拆分/保存 transient payload 的总帧成本高于重复发射少量 AO rays，记录拒绝理由并保留 AO-only fallback。**不把 AO 塞进 GI 最终颜色，也不把 GI history 当作 AO history。**

### 4.3 前置 RT producer 与后置 camera-color consumer

RTDI / RTGI 的 producer 与 composite 应视作两个时间责任，不因历史代码把它们挂在同一个 Feature 而绑在一起。当前代码已经把这个边界落成：两个 producer 不再 enqueue 自己的 CompositePass；ScreenProcess 的 RT-light pass 统一消费产品。

```text
CPU pre-cull: Ho-RT declares consumer views and prepares frozen scene
GB / OB / SB: current primary surface facts
Before opaque: Ho-RTLighting shared ray/query stage + separate DI/GI/AO product resolve
Opaque Forward: lilToon complete NPR shading; RTAO is already available to the material
After opaque: consume RTDI/RTGI products into camera color at an explicit composition point
```

- RTDI / RTGI 当前通过 `HoRTPrimarySurfaceInputs.Read` 读取 SB/GB，并在 unsafe producer pass 里生产自己的纹理/缓冲；它们不是从 Forward color 提取 base color、roughness 或 metallic。
- 因此可以原型化将**统一 RTLighting producer**移到 `{GB, SB, required light/environment inputs} -> Ho-RTLighting -> opaque Forward` 的偏序中，同时保留 DI/GI/AO 各自独立输出。
- ScreenProcess RT-light consumer 读取当前 `activeColorTexture`，位于 opaque camera color 已产生、常规透明尚未绘制的 `BeforeRenderingTransparents`；其普通视觉 layer 仍在 `AfterRenderingPostProcessing`。因此它不会把线性 HDR RT contribution 错放到 tone-map 后，也不会覆盖后续透明。
- 一项系统需要前置 surface data，不代表所有 shader effect 都要变成可单独合成的屏幕层。完整 NPR Forward 仍是材质着色权威；RT producer 发布 lighting/visibility product，compositor 只负责晚期路由与混合。
- 每次前移都要核对 RenderGraph `ContextItem` 创建顺序、`UseTexture`、同事件入队序、Ho-RT 首次 AS build、Atmosphere ready、RT host fence 及资源跨 pass 存活。GPU pass event 不能替代 CPU pre-cull 请求 epoch。

这是当前比“直接全面转 Deferred”成本更低的架构候选：既能利用已有前置 buffers，也能把 DI/GI/AO 收进一个 RTLighting feature 的调度边界，同时保留不同 product 的独立语义；其是否节省等待、同步、重复 shading 或总帧 GPU 仍须测量。

### 4.4 lilToon AO、GI 合成与最终画面

现有材质把 `fd.aoVis` 同时用于 toon ramp 和全局 AOColor darkening。RTGI 当前 normal-camera composite 在不透明物绘制后加到 camera color，因此它不经过前面材质 shader 的这两路 AO 输出。后续联合作用需明确选择：

- **默认物理路径**：RTGI 已自行追踪几何，不再叠乘 RTAO；维持现有 raster material AO 行为。
- **显式艺术模式**：若目标是 AO 再压暗新增 GI，单独定义“indirect AO”意图与强度，不把它伪装成 RTDI/RTGI 物理 visibility。先检查是否与 GI path visibility / sky sampling 重复。
- **HoShader 新材质一致性**：若新 RT-aware 材质要让 AO products 影响自己的 Ramp/AODark，相关响应在新材质包内定义；不要求把 `_AOMask`、AO Map、`_AOColor` 等 RT-specific 控制加入 lilToon。现有 lilToon 的 AO 行为不因新包而改变。

当前 `aointent` 仍是未实现的通道占位，SB 没有通用 AO intent 产品。新 HoShader RT consumer 若需要逐材质 AO intent，应在独立材质/产品接口里登记与验证；不因这个需求把全套艺术控制打入公共 GBuffer。

上述产品路由是 RTLighting products 与新材质 consumer 整合 gate。首版 AO 只生产 typed visibility 并沿用现有兼容输出，不修改 RTGI product contract 或 lilToon shading。

## 5. 降噪与重建路线

1. 保存原始 scalar visibility、首命中距离/状态、输入几何 guide，并建立高样本参考；先确认 raw 估计器的统计和偏差。
2. 比较三种重建：现有 AO temporal 思路移植到 RT primary sample；NRD `RELAX_DIFFUSE` 对 unit-white diffuse accessibility 信号的适配；必要时保留独立的轻量 visibility temporal + edge-aware spatial。
3. NRD 只共享公共 executor、ABI、shader provenance 与资源调度，不共享 RTDI/RTGI 的 Instance、history、reset、信号 scale 或 profile。AO visibility 映射成 `RELAX_DIFFUSE` 输入的假设必须以同一参考和 guide 对照验证。
4. SIGMA 是灯方向的 shadow/penumbra signal，不作为半球 RTAO 的默认 denoiser。
5. 记录 raw mean、filtered mean、逐像素 temporal variance、遮挡边界误差、首次历史/运动重置、首次分配与稳态 GPU 时间。只看方差下降不能证明 AO 没被系统性漂白或压黑。
6. 用 GB coverage、深度和法线引导低到全分辨率重建；禁止跨轮廓扩散、细发片漏光、checkerboard 残留和描边参与物理遮挡。

## 6. 阶段与退出条件

所有阶段为规划态；通过标准不能用“能编译”“有纹理”或“画面变黑”代替。

| 阶段 | 状态 | 工作范围 | 退出条件 |
| --- | --- | --- | --- |
| **A0 基线与参考恢复** | 部分完成 | 导入邻近审计原始 GPU markers；恢复隔离参考；标出 Ho-RT / GB / OB / SB / AC / opaque Forward / RT producer / composite / transparent 的真实执行时序 | 同一目标场景有逐 pass GPU、CPU prepare、首次 AS build、steady-state 和资源账本；source 与配置 hash 完整 |
| **A1 合成入口抽离** | **代码已落地，待运行时 A/B** | 新增 `ScreenProcessRTLightingResources` 与 compatibility registry；由 ScreenProcess 专用 pass 统一消费 RTDI/GI typed products，保留 DI add/replace、GI add、深度 gate、线性 HDR、Gamma 和 fog 语义；普通 ScreenProcess stack 不承担 RT handle | Runtime/Editor 扩展程序集已编译；RenderGraph/compat 实际画面对照、关闭/无效/不支持 no-op、无重复合成仍待隔离夹具复验；旧 Composite 源文件暂保留为回退参考，未再被 Feature enqueue |
| **A2 Ho-RTLighting 全 producer 前移** | **代码已落地，待运行时 A/B** | RTDI/RTGI producer 固定安排到 `BeforeRenderingOpaques`，从 GB/SB/Ho-RT 读取主表面并发布 product；ScreenProcess consumer 在 `BeforeRenderingTransparents` 读取；DI/GI 的灯光、天空、history、NRD 和输出所有权仍分开 | Runtime/Editor 扩展程序集已编译；需用 RenderGraph/compat 夹具确认 GB/SB ContextItem、Ho-RT lifetime、opaque 前有效性以及 OIT/SSS/transparent 边界；尚未宣称 GPU 性能收益 |
| **A2.5 HoShader RT 材质族合同** | 未开始 | 在 HoShader 体系新开 RT-aware 材质类别；定义它读取的 typed RT products、材质内 Forward/NPR evaluation、缺失/关闭 fallback、与 SB/GB/Ho-RT 的依赖；不修改 lilToon 主干 | 独立包/仓库边界明确；至少一个 opaque RT-aware 材质在 Forward 中消费 RTAO/RT lighting product；与现有 lilToon、HoShader Skin/Fur 互不产生隐式依赖 |
| **A3 AO product / GI primary-ray 副产物** | 规划中 | 在统一 primary-ray stage 上从 shared transient ray product 派生 AO accessibility；保留 AO 自己的 visibility history/denoise/output；GI 关闭时保留 AO-only fallback | 共享 ray/query 与独立 AO 估计、GI 估计配对通过；AO 与 GI 不共享错误 history；受控 geometry 与离屏 blocker 匹配高 spp reference；保持现有 lilToon AO 两路消费 |
| **A4 RTAO reconstruction / fallback** | 未开始 | 独立验证历史、NRD 可行性、边缘引导 upscale；RTAO/GTAO/Off 按相机选 producer | raw/filtered 均值、边界、运动与冷启动通过；白色 fallback 正确、无全局纹理串相机 |
| **A5 RTGI primary sample reuse** | 条件启动 | RTAO 与 RTGI 前置阶段存在后，验证一次 cosine raygen 是否可共享 AO accessibility 与 GI secondary hit | 采样法线/PDF、Cutout、距离、history 与分辨率同域；若拆 pass/存 payload 的总帧成本更高则拒绝 |
| **R1 硬件 RT 反射** | 未开始 | 以 [ReflectionPipelineDesign](ReflectionPipelineDesign.md) 为材质和来源权威；Ho-RT hit/radiance view、opaque glossy、confidence/hit distance、PLR/SSR/Probe fallback、NRD specular 独立 profile | 镜面/粗糙金属/介质、屏幕内外命中及 miss fallback 过关；已知单次 source，不与 PLR/SSR/RTGI 重复累计 |
| **R2 折射** | 未开始 | 单层受限 transmission、IOR/TIR、厚度/吸收、cutout 与 OIT/透明时序；不将现有透明 blocker 自动解释为可透射材质 | 玻璃/薄片/水面受控夹具的路径、coverage、背景合成和多层拒绝条件明确；透明物不被重复绘制或错误遮挡 |
| **O1 系统优化** | 持续 | 共享姿态、AS build/view、pixel count、ray count、payload、full/half/quarter 分辨率、NRD pool、RenderGraph transient、CPU 等待分别记账 | 每项优化有等画质 A/B、steady/first-frame GPU 分段、峰值资源账本及整帧收益；无证据的“共用”不合入 |

## 7. 性能验收与优化策略

### 7.1 配对矩阵

所有性能比较固定 Unity/GPU/驱动/graphics API、场景、动画帧、camera、Volume、色彩空间、render scale、TAA、后处理与 capture mode。至少包括：

| 对照 | 目的 |
| --- | --- |
| AO Off / 当前 GTAO / RTAO | 测生产成本和 AO 对最终材质画面的影响 |
| GTAO Full / 真正 Half / 真正 Quarter | 防止把当前“仅配置”误读为已有低分辨率收益 |
| RTAO 1 spp at 1/2 / 1/4 linear scale | 测射线像素数和 upsample/denoise 成本 |
| RTDI Off / On、RTGI Off / On、两者并用 | 测各自增量与组合后的整帧成本 |
| RTAO 独立 / RTGI+RTAO candidate sharing | 只在 sample sharing 原型达成后比较 |
| 首帧 AS build / 稳态 / pose change | 分开记录 TLAS、姿态 bake、历史 reset 与日常成本 |

邻近审计当前的 `>200 ms GTAO` 与 RTDI/RTGI 低一个数量级作为**待导入原始报告的目标锚点**。先验证其是 GPU queue 时间还是 Editor/CPU 等待、总 Feature scope 还是单一 pass；确认同一分辨率和场景后，才设正式毫秒预算。

### 7.2 初始性能目标

- **候选目标**：同场景、同分辨率、同最终材质 AO 误差门槛下，稳态 RTAO GPU time 争取低于当前 GTAO 的 10%；此为待 A0 数据确认的目标，不是已达指标。
- 分别报告 median/p95、完整 camera GPU frame、RTAO trace、temporal/NRD、upsample、AS build、pose update、CPU PrepareWait 和 per-camera memory。
- 同时计量 RTAO 独立运行与 RTGI/RTDI 同时运行的**整帧增量**。共享射线的收益若只减少 trace marker、却增加 pass split/sync/资源，按完整 frame 判定。
- 低分辨率计算必须附最终 full-resolution material AO 质量；raw 预览、静态简单墙面或单纯降低采样不能作为等质量性能比较。
- 如果 RTAO 没有达到目标，按 ROI 判断：优化 GTAO 实际 half/quarter 路径、采用 hybrid fallback，或接受 RTAO 主要换来离屏正确性而不宣称性能替代。

### 7.3 每阶段即做的优化

1. 先剪去算法工作量：有效 surface、AO 半径、sample count、线性渲染尺寸和可见像素。
2. 再剪资源与数据流：输出精度、低分辨率 history、首命中元数据最小化、RenderGraph transient aliasing、按需创建 NRD。
3. 最后做 producer 之间的 sample reuse / dispatch merge；只有在算法合同相同且能保留独立 raw 参考时才考虑。
4. 优化 Ho-RT host 的 per-camera world scan、pose cache、compatible view 数、AS 更新/build 与 CPU fence wait。重复 AS / view 是显存与 build 成本，不能仅凭共用 GeometryCache 宣称已共用 TLAS。
5. 保持 GTAO 作为非 DXR fallback，维护相同材质输入和基本对照；不能让 D3D11/无 RT 设备读到前一相机 RTAO。

## 8. 反射、折射与 RTAO 之后的组合

### R1：反射

先完成 opaque specular RT reflection，不把 refraction 混进首版。消费已落地的 SB `Reflection` material 数据及 GB physical geometry/coverage，使用 `_UseReflection` 总开关。Hit radiance 的直接光、天空、GI、自发光来源要明确单次累计；screen miss 采用已有 PLR/SSR/Probe/Sky 的优先级合同。NRD specular 使用独立 signal、guides、history 与 per-camera instance。

若 hit shader 需要 RTDI/RTGI 的最终相机纹理，屏幕空间输出对离屏 ray hit 不可直接代表命中表面 radiance；需显式设计 hit shading 或 cache，不得把 cameraColor 当作全世界 radiance。

### R2：折射

折射依赖实际 transmission 材质语义，不只是反射方向的反号：至少要定义 IOR、几何/材质厚度、吸收、内外界面/TIR、层数、Cutout、透明排序以及 OIT 交互。按 `ReflectionPipelineDesign.md` 已登记的 PLR/水面/玻璃边界推进；第一版只声明已验的有限子集，其余保持现有透明/Probe fallback。

## 9. 参考工程、版本与操作边界

- 历史 reference runs 记录 Unity `6000.3.15f1`、D3D12、RTX 4060，以及本地 `UnityPathTracing-6000.3.15` 的 RTXDI / GI / NRD 实际链路；仓库内 fixed vendor、provenance 和 NRD ABI 仍是当前实现依据。
- 本次检查时，reference copy 不在此 checkout 的文档默认 `.codex-research/UnityPathTracing-6000.3.15` 路径；此前的结果和源引用仍保留在 `Tests~/RTDI/Reference`、`Tests~/RTGI/Reference`。A0 要找回或重新准备隔离副本，确认其源提交、插件 hash、NRI repair 和 NRI/NRD 部署；不要在用户工作场景或共享参考工程里直接运行探针。
- 一次只运行一个 Unity 进程访问同一个项目。批处理使用独立副本，真实用户 Editor/场景不自动切换、不保存、不重载。
- 新增证据必须绑定生产源码 hash、测试 harness hash、Unity/GPU/driver/API、Volume/camera 配置、进程退出码和原始 report；历史证据不改绑到新源码。

## 10. 当前阶段评价与下一步

**本轮完成：** 源码/文档基线已核对；现有 GTAO 对材质的 ramp 与全局压暗两种输出已确认；RTDI/RTGI producer 只读 buffer/world inputs，而 camera color 由独立 consumer 消费；A1 的 ScreenProcess RT-light typed contract、RenderGraph handle 发布、compatibility registry 和等价合成 shader 已落地；A2 已将两个 producer 的默认事件前移到 `BeforeRenderingOpaques`。Unity 6000.3.15 已生成 Runtime/Editor 扩展程序集。

**尚未完成：** 邻近审计的原始 GPU capture 未在本轮取得；reference project copy 当前不在记录的 checkout 路径；A1/A2 的 RenderGraph/compat 实际画面与资源生命周期仍待隔离夹具；HoShader RT 材质族、AO/GI shared primary-ray estimator、denoiser profile 和正式性能目标均待后续阶段。当前 Unity 全项目测试编译仍被仓库已有的 `ScreenProcessRegressionTests.cs` compatibility-mode setter 错误阻断，不能视为本次 Runtime 代码失败。

**下一步 A0/A1/A2 验证：** 从邻近 GTAO/RTDI/RTGI 审计取回实际配置与 raw GPU marker 报告；在隔离 ScreenProcess/RTDI/RTGI 夹具中验证同帧 product、关闭/重启/多相机清理、透明前合成和 RenderGraph/compat 对等；记录 GB/SB/Forward/RT producer/consumer 的真实顺序及数据依赖。A1/A2 验证通过前不删除旧 Composite 源文件，也不开始 AO product Runtime。

**阶段原则：** 每阶段只改变一个关键合同；记录预计收益、实际增量成本、质量结果、资源变化与停止条件；无收益候选及时搁置。已有 Ho-RTShadow、DI、GI 的历史“下一步”继续按各自现行文档解释，不由本路线图自动复活旧实验。
