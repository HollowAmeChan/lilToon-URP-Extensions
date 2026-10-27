# 角色 Cast 的精度上限与无历史渲染路线

研究日期：2026-10-28。本文是文献研究、本地源码审计与原型建议，不代表新后端已经实现或在实际角色上通过画质验收。

架构补充：后续讨论将本轮范围提升为 Ho-RT 公共底座与独立 Ho-RTShadow 设计。本文的独立可见性路线由公共 RT 查询支撑，硬投影参考由 RTShadow 最小模式验证；资源所有权、多灯输出与 NPR 接收边界以 [公共底座设计草案](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/架构/Ho-RT-公共底座与RTShadow-设计草案.md) 为准。

## 1. 建议先明确的选择

对本项目“只在角色上接收、追求清楚、可接受较高成本、导出时不想每帧等待历史稳定”的需求，建议把最高质量目标定为：

**指定主光的独立几何可见性查询 + 当前输出帧内的空间超采样。需要真实半影时，再在同一帧采样有限大小的光源。**

这条路线可以使用硬件光追，但不需要把 RTDI 的 ReSTIR、跨帧历史、RELAX 或完整路径追踪一起搬过来。无历史表示结果不依赖前面渲染过哪些帧；不表示高采样免费，也不保证所有细节在很低的样本数下都稳定。

保留 shadow map 作为无 RT 设备和低成本预览的后端。先做相同条件下的对照原型，再决定是否让 RT 成为角色高质量模式。继续修正确实存在的 PCF/PCSS 错误仍有价值，但不宜再把扩大滤波核当成最高精度的主要投入。

这个建议是结合论文与当前代码的工程判断；论文并不能替代本项目实际头发、材质、动画和导出流程的验收。

## 2. 现在混在一起的三个问题

### 2.1 Shadow map 对遮挡几何的采样

普通 shadow map 保存的是光空间规则网格上的深度。一个 texel 投到屏幕上可能覆盖许多像素；接收面接近掠射角时，放大尤其严重。4K 是纹理边长，不能直接代表头发特写中每个输出像素都有足够的遮挡信息。PCF 对深度比较后的明暗做滤波，无法恢复生成 shadow map 时漏掉的薄几何或轮廓位置。[NVIDIA：Shadow Map Antialiasing](https://developer.nvidia.com/gpugems/gpugems/part-ii-lighting-and-shadows/chapter-11-shadow-map-antialiasing)

前一轮独立验证确认本项目角色阴影使用了真实 4096×4096 D32 单 mip 纹理，没有发现该链路中另有低分辨率屏幕遮罩。RenderGraph 执行后全局纹理查询得到默认黑图，不是实际渲染资源被缩小的证据。这个结论限于已检查的角色阴影链路。

### 2.2 半影的模型与采样

PCF 的核宽主要控制滤波。PCSS 根据 blocker 深度估计半影大小，再改变 PCF 的核宽；原方法使用平均 blocker 与平行平面近似。它能得到接触硬化的外观，但并没有逐个检查真实面积光方向上的全部遮挡关系。因此更多 tap 可以减少采样伪影，不能让单张深度图变成完整几何可见性。[NVIDIA：PCSS Integration](https://developer.download.nvidia.com/whitepapers/2008/PCSS_Integration.pdf)

必须区分“为消除像素锯齿所需的覆盖滤波”和“光源具有面积而产生的物理半影”。把光源变大可以让边缘更柔和，也会真实地损失细小投影的对比度；这不符合所有二次元画面的美术目标。

### 2.3 最终屏幕像素的积分

即使能精确回答某个表面点是否被遮挡，屏幕像素也不是一个无面积的点。阴影边、细发片和材质分段都可能穿过像素。单个中心样本仍会产生屏幕锯齿。高质量离线栅格化的一条成熟路线，是在更高的空间采样率下完成着色，再对最终颜色重建，而不是只模糊一张 shadow mask。[NVIDIA GPU Gems 2：High-Quality Antialiased Rasterization](https://developer.nvidia.com/gpugems/gpugems2/part-iii-high-quality-rendering/chapter-21-high-quality-antialiased-rasterization)

本地 lilToon 在接收 cast 后，仍会经过各层 toon border/blur。相关顺序见 [lil_common_frag.hlsl](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:963) 与 [toon 分段](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:1010)。因此“可见度图看起来平滑”不等于最终材质也被正确抗锯齿。

对于像素内不同位置的样本，通常有：

```text
toon(平均可见度) ≠ 平均(toon(每个空间样本的可见度和材质输入))
```

空间抗锯齿的参考应当是：每个子像素得到自己的表面、遮挡和完整材质结果，然后平均最终颜色。在同一个表面点重复发出相同硬阴影射线，或者只提高 mask 尺寸再缩回去，都不能替代它。

这里不要混淆光源积分：有限光源的多个方向可以先汇总可见度，再交给现有 toon 美术规则；是否改成逐方向执行整套 toon，是另一项会改变外观的设计选择。不能借修抗锯齿悄悄更改。

## 3. 光追阴影不必依赖跨帧累计

理想零角度方向光只有一个入射方向。给定准确的表面点及参与遮挡的几何，一条遮挡射线就能判定该方向是否被遮挡，没有光源方向抽样的随机方差。这是一个点上的硬阴影判断，不是整幅图已经没有锯齿，也不涵盖半透明传输。[PBRT：Distant Lights](https://www.pbr-book.org/4ed/Light_Sources/Distant_Lights)

光源有有限角度时，需要积分多个方向。可把这些样本全部放在当前帧，而不复用前一帧的结果。Unity HDRP 本身也将“每像素每帧的射线数”和可选的时空降噪分开提供；它们不是绑定的一个开关。[Unity HDRP 17.3：Ray-Traced Shadows](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Ray-Traced-Shadows.html)

对快速逐帧导出，建议采用固定且可复现的当前帧预算：冻结本输出时刻的动画和光照，完成该帧的空间与光源样本，得到结果后再推进时间。可以分多次 dispatch 或多次渲染相加，但不读取前一个输出帧的阴影历史。这样没有阴影 warmup、历史拖尾或镜头切换后的等待，代价是每帧明确承担所选采样成本。

这只解决 cast 的历史依赖。其他 GI、反射、TAA、物理模拟或缓存若依赖历史，必须分别处理，不能据此承诺整个工程导出无需准备。

## 4. 当前 RTDI 究竟可以拆出什么

### 4.1 已经存在无随机单灯路径

[HoRTDIRendererFeature.cs](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/HoRTDIRendererFeature.cs:306) 在只有一盏解析灯、没有有效环境采样时启用 `singleDelta`：跳过 reservoir Temporal/Spatial，并将 raw 输出发布到原输出，跳过后续 RELAX 路径。现有 [SingleDelta 验证记录](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/RTDI/技术/Ho-RTDI-SingleDelta.md) 也明确区分了此条件与多灯/环境模式。

[HoRTDIApplicationBridge.hlsl](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/Shaders/HoRTDIApplicationBridge.hlsl:151) 中方向光目前取固定方向，没有太阳圆盘的角度抽样。所以不能根据 RTDI 当前硬边很清晰，就推断它已经解决了有限面积光源的单帧软阴影。

这说明“RTDI 清楚必然是时域攒出来的”不成立；但仅凭截图，也不能断言当前画面 RTDI 的全部优点都来自同一个原因。

### 4.2 可复用的是求交与材质支持

- [HoRTScene](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RayTracing/HoRTScene.cs) 已有手动 AS、实例/材质身份和当前姿态几何登记。
- [HoRTDIFinalVisibility.raytrace](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/Shaders/HoRTDIFinalVisibility.raytrace:14) 已有真实场景遮挡射线。
- [HoRTDIVisibilityMaterial.shader](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/Shaders/HoRTDIVisibilityMaterial.shader:34) 已有基于命中三角形 UV 的 alpha any-hit。
- [HoRTSkinnedMesh](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RayTracing/HoRTSkinnedMesh.cs:66) 已有骨骼/BlendShape 姿态快照及未变化时的复用。

但是现有通用 [HoRTSceneQuery.hlsl](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RayTracing/Shaders/HoRTSceneQuery.hlsl:23) 会剔除 non-opaque 实例。只调用它来做新 cast，会漏掉需要 alpha 判断的发片；必须接入 native any-hit 或正确的候选 alpha 查询。alpha 合约宜抽到共同层，避免复制两套不同的裁切语义。

### 4.3 不可直接拿来用的输出

`_HoRTDIFinalVisibility` 跟随 reservoir 选中的灯样本；部分模式把主样本与两个辅助样本的 bit 打包后写入。它不是一个恒定代表“指定主光可见度”的标量遮罩。RTDI 的 diffuse/specular 输出又已经含有灯能量、BRDF、材质及抽样权重，不能用相除等方式可靠地还原独立 cast。

RTDI 当前 pass 在不透明物体之后运行，见 [调度](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/HoRTDIRendererFeature.cs:147)，不能直接供更早的 lilToon forward 阶段接收。独立后端必须有自己的输入、顺序和发布合约。

## 5. 候选技术对照

下表的优先级是针对本仓库已有 RT 基础和角色导出需求作出的判断，不是算法的通用排名。

| 路线 | 遮挡几何精度与半影 | 可否不依赖历史 | 本项目判断 |
| --- | --- | --- | --- |
| 更紧的角色/头部 shadow map + 正确 PCF/PCSS | 改善 texel 密度；仍受规则深度采样和半影近似限制 | 可以 | 应保留的栅格化基线 |
| 上述方案 + 最终颜色空间超采样 | 同时改善屏幕边；不能单独恢复 map 已丢失的几何 | 可以 | 最低架构风险的改进与对照 |
| 同帧多张子 texel 抖动 map | 可以增加光空间采样；应重新生成深度并正确汇总 | 可以 | 可作无 RT 实验；成本与收益需实测 |
| EVSM / Moment Shadow Maps | 可预滤波、宽核方便；深度分布重建仍有近似 | 可以 | 偏宽软影，不作为最清楚硬边首选 |
| Virtual Shadow Maps / SMRT | 更多局部纹素；仍有投影采样与单深度近似 | 不要求靠历史收敛，但实现可使用缓存 | 不建议为少数角色搬整套世界级分页系统 |
| Alias-Free / Sample-Based Visibility | 改变采样结构，直接对实际屏幕样本求遮挡 | 可以 | 非 RT 的高精度方向，但需专门数据结构与求交流程 |
| 几何 shadow volumes | 硬阴影轮廓准确，像素 AA 仍要解决 | 可以 | 发片 alpha、拓扑和动态几何使其不适合作为首选 |
| Deep Shadow Maps | 深度方向的分数可见度，适合密集毛发/半透明层 | 可以，运动模糊另议 | 未来真实毛发或透射需求再评估 |
| 独立 RT visibility + 最终颜色空间超采样 | 按真实几何求可见度，有限光源可同帧积分 | 可以 | 建议的质量上限原型 |
| RT + 时空复用/降噪 | 用历史与邻域降低当前帧采样成本 | 有历史时依赖过去帧 | 可选预览优化；不应成为导出默认前提 |

Moment Shadow Mapping 的作者资料说明了可预滤波及较低漏光的优势；它仍不等价于保存完整遮挡几何。[作者讲解](https://momentsingraphics.de/GDCEurope2016.html) Epic 也明确记录 Virtual Shadow Maps 的投影锯齿及 SMRT 近似限制。[Epic 官方文档](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)

不能说“非 RT 注定不精确”：Aila/Laine 的 Alias-Free Shadow Maps 将实际可见表面样本变换到光平面后，在这些不规则位置判断遮挡；后续 Sample Based Visibility 又扩展了面积光源采样。它们绕开普通规则 shadow map 的采样错位，但不是换一种 PCF 权重就能接入。[Alias-Free Shadow Maps](https://users.aalto.fi/~ailat1/publications/aila2004egsr_paper.pdf)、[Sample Based Visibility](https://publications.graphics.tudelft.nl/papers/616)

Deep Shadow Maps 存储随深度变化的分数可见度，重点是密集细几何、部分透射与体积，不是普通 toon 发片硬投影的通用升级按钮。[Lokovic / Veach 原论文](https://graphics.stanford.edu/papers/deepshadows/)

## 6. 独立角色 RT Cast 的接入边界

### 6.1 输出与美术响应分开

新后端只回答指定光的 `V ∈ [0,1]`：硬阴影在一个表面样本上为 0/1，有限光源为加权可见比例。它应进入现有角色 cast 接收位置，保留材质对各层阴影的接收选择，再做最终空间抗锯齿。直接在 beauty 图上乘一张黑色遮罩，会改变现有 toon 分层、高光及其他响应，不宜作为正式接入。

先支持单个主方向光；不要把多灯选择、间接光、环境照明或 ReSTIR 带进第一版。预览与导出都应能独立选择阴影后端及当前帧采样预算。

### 6.2 屏幕预通道与 inline 两种实现

现有 GeometryBuffer / SurfaceBuffer 默认在 `BeforeRenderingOpaques` 生成，[HoRTPrimarySurfaceInputs](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RayTracing/HoRTPrimarySurfaceInputs.cs:40) 可以读取主表面及 owner。可研究明确的 `GB → SB → Cast → Forward` 顺序，使用 native raygeneration + any-hit 生成指定主光的 mask。相同 RenderPassEvent 不保证依赖成立，必须显式验证资源 readiness 与消费者顺序。

该 mask 必须匹配当前相机、实际分辨率、viewport、jitter、深度/表面身份；边界不能把前后两个表面的 mask 随意混合。做多次空间样本时，每个样本也要有匹配的主表面。仅从中心像素深度向周围猜几个位置，不能正确覆盖发片和几何轮廓。

另一条路线是在实际 forward fragment 中 inline ray query，表面与着色位置更直接对应；但需相应 shader 编译目标、设备能力与 alpha 材质访问，不能把现有会跳过 cutout 的 query 原样塞进去。Unity 提供 inline ray tracing 能力查询，实际支持仍需设备/API 检查。[Unity 6.3：supportsInlineRayTracing](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/SystemInfo-supportsInlineRayTracing.html)

优先原型可选前一种，以复用已工作的 native alpha 路径；最终方案由边界正确性与实测成本决定。透明接收层、MSAA、XR 等不能在尚未验证时声称自动兼容。

### 6.3 接收集合与遮挡集合不是同一件事

只给角色算接收样本，可以减少查询量；遮挡集合仍要包含美术要求的世界物体、其他角色及离屏遮挡物。如果刻意只做角色自投影，也应作为显式模式，而不是意外漏掉世界。

当前 `HoRTScene` 并未按 `Renderer.shadowCastingMode` 筛选。新 cast 要明确处理 Cast Off、Shadows Only、Two Sided、light/rendering layers 与排除材质。不要用相机可见集合代替光照遮挡集合。

### 6.4 光追也要修几何、alpha 与数值问题

现有 alpha adapter 支持基础纹理、mask 与 cutoff，不意味着任意 dissolve、GPU 顶点位移、透明传输和自定义发丝都已支持。显式 LOD 0 的射线 alpha 也需要和最终像素覆盖/纹理过滤一起验证。

现有蒙皮快照是 CPU `BakeMesh`，动画变化仍有快照及 AS 更新成本；同一个输出时刻的多个空间样本应共享同一份正确姿态。RTDI/RTGI 当前各持有场景实例，不应未经重构就宣称新模块可免费共享所有 AS 与蒙皮更新。[现有场景支持记录](D:/Unity_Fork/lilToon-URP-Extensions/Documentation~/RTDI/技术/Ho-RTDI-SceneSupport.md)

射线自相交应依据几何法线及误差范围处理；单纯把 bias 拉大可能吞掉发片间很近的真实遮挡。NVIDIA 的稳健偏移方法给出了误差来源及几何法线偏移思路；从栅格深度重建表面时还要核对额外的输入误差，不能直接声称套公式便有相同保证。[NVIDIA：Self-Intersection Artifacts](https://developer.nvidia.com/blog/solving-self-intersection-artifacts-in-directx-raytracing/)

还要单独检查 shadow terminator：平滑着色法线朝向光，但真实低模三角形已经背光。这类面状边界在准确光追中也会出现，解决方向可能是几何细分、合理的着色/可见性协调，而不是增加射线数。不能把此前所有三角块都归为这个问题；只有对照诊断才能确认。[Epic：Shadow Terminator Problem](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)

## 7. 建议的对照原型与验收

### 第一步：先建立硬阴影的几何参考

固定同一个输出时刻、相机、光源方向、角色姿态及材质，关闭额外 PCF/PCSS 软化。用零角度主光的独立 RT visibility 作为参考，分别保存原始可见度与最终 toon 颜色。参考仍有浮点、几何与像素采样边界，不能只凭“光追”二字免检。

同时比较当前 map、紧头部 map 和 RT。空间样本先试 1 / 4 / 16 个每最终像素；这些数字是实验档位，不是已验证的推荐默认值。观察增加空间样本究竟消除了屏幕台阶，还是仍留下 map 固定的细节损失或几何终止线。

若 RT 中心可见度正确、最终 toon 仍有硬台阶，优先查空间积分；若原始 RT visibility 就出现错误块，先查几何、alpha、起点偏移和表面关联，不能靠降噪掩盖。

### 第二步：增加有限光源，不用 PCSS 代替真实参考

固定空间采样后，测试零角度及小角度光源；方向样本可先试 16 / 64 / 256 个，逐档与更高当前帧预算比较。需要细硬影时应保留零角度或小角度选项，而不是强制大半影。

简单乘积实现中，4 个空间样本 × 16 个方向样本就是每最终像素 64 次可见性查询；不含主表面生成、alpha、AS 更新和材质着色成本。也可以研究联合采样分配，但不能无验证地承诺更少样本同等质量。固定样本位置保证可复现，不保证没有结构性纹理，仍需检查采样模式。

### 第三步：验证导出，不只检查静止截图

| 对照场景 | 必须检查的结果 |
| --- | --- |
| 细发片、亚像素间隙、斜向投影 | 轮廓位置、覆盖率及细阴影是否被滤没 |
| 掠射面、近平行发片、曲面低模 | acne、漏光、三角面边与真实终止线分开记录 |
| alpha 洞、双面、Cast Off / Shadows Only | 与材质/Renderer 语义一致 |
| 骨骼、BlendShape、镜像和非均匀缩放 | 当前姿态及法线/偏移正确 |
| 相机微移、灯微转、遮挡物移动 | 局部边缘稳定性，不能只报全图平均误差 |
| 冷启动单帧、随机帧序、镜头切换 | 相同场景状态的 cast 与顺序渲染一致，无历史准备要求 |
| 高预算单帧参考 | 薄影覆盖、边缘差异、半影误差分别比较 |

导出时冻结姿态及 shader 时间，再完成当前帧的各空间样本；按最终材质颜色在场景线性域重建，然后进入约定的显示变换。不要在各子样本之间推进动画或隐式累积 TAA。需要运动模糊时另行定义快门时间积分。

先记录无 denoiser 的正确性与耗时，再决定是否加入保持边界的空间重建或时域预览。至少分别测蒙皮/AS CPU 工作、AS GPU 工作、可见性、额外着色和完整导出帧；现有数据不足以给出本方案每帧毫秒数或“比等待累计快多少”的承诺。

## 8. 投入顺序

1. 建立无历史、单主光、零角度的独立 RT cast 参考；复用现有几何与 alpha 能力，先不重构整套 RTDI。
2. 做当前帧最终颜色空间超采样，同时对照当前 shadow map，确定用户画面中的剩余锯齿分别来自哪里。
3. 在该参考上增加有限光源的同帧采样，验证需要的半影质量及成本。
4. 根据实际导出结果决定生产后端：高质量 RT、栅格化兼容模式，以及可选的低成本预览。跨帧复用放在这些正确性工作之后。

如果硬件/API 条件不适合 RT，就以紧投影 + 高质量 shadow map + 最终颜色超采样推进；必须明确它仍可能存在光空间采样上限。当前不建议先为少数角色实现完整虚拟阴影系统、Deep Shadow Maps 或一套专门的不规则光空间光栅器。
