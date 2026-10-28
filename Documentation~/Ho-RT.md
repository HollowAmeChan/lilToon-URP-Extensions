# Ho-RT：公共底座与 RTShadow

> 状态：**现行实现 / 设计总入口**，2026-10-28。目录迁移、R2 公共资源宿主及共享姿态的有界验收已完成；DI/GI 有界迁移及 Player 资格已完成；RTShadow raw 已有有界验收，NPR 接收与 SIGMA 生产链仍未完成，精确进展见 §8.5。实现状态与后续接口建议分开阅读。

## 0. 先确认实现状态

| 对象 | 当前状态 | 入口 |
| --- | --- | --- |
| 栅格化角色 CS | 已有 shadow map / PCF / PCSS，本轮完成勾选与滤波修复，仍有画质限制 | [当前 CS](Ho-CharacterShadow.md) |
| RTDI / RTGI | 两路均消费 host view；DI / GI 的有界迁移与生产组合已验收，GI Mono Player 及 DI 有界 Player 两路径通过。独立输出 / 历史 / NRD 保持；完整 DI proposal 旧失败保留 | [DI](Ho-RTDI.md)、[GI](Ho-RTGI.md) |
| 公共 NRD | A01/A02 已完成代码解耦及有界资源复用；生产绑定为 RELAX | 本文 §11 的基线与证据 |
| Ho-RT host | R2 已有有界验收，生产 DI/GI 已接入并验证共享姿态与独立算法；DI/GI Player 已确认序列化和插件加载 | 本文 §8.5 |
| RTShadow / SIGMA | 独立 Feature 的单主方向灯 raw / OB 接收身份 / 有限光源同帧采样已验；NPR gate 被拒绝，SIGMA 尚未接入 | 本文 §4–8 |

当前底层 helper 的实现契约见本文 §10。旧阶段的冻结 / 停止条件只约束当时任务，不自动阻止本轮经用户提出的公共底座工作；历史质量、F39、复杂动态和性能缺口也不会因此变成已解决。

## 1. 本轮的架构定位

此前 RTDI / RTGI 的公共层抽离已经完成公共 NRD 执行器与部分 RT 契约的解耦，基线及证据压缩保留在本文 §11。本轮继续解决 RT 场景的资源所有权、准备时序及公共查询能力；之前阶段已经完成的收益与明确保留的问题维持原结论。

建议采用两个独立功能模块：

- **Ho-RT**：公共场景资源、几何与材质查询能力、当前渲染请求的准备与生命周期。
- **Ho-RTShadow**：按光源与接收域生成 Cast 可见度产品，供 NPR 材质和其他系统按约定消费。

现有 DI / GI 先迁移为公共底座消费者，验证资源归属与行为不变。随后 RTShadow 的零角度、无历史模式提供新增阴影链路的几何基准，再接 SIGMA 与 NPR。各消费者直接使用公共查询能力，保留各自算法、输出和历史。

## 2. 行业中两条路线同时存在

### 2.1 独立 ray-traced shadows

Unreal 官方把 Ray Traced Shadows 明确列为 standalone feature，并允许灯覆盖项目级开关；它与 Lumen 的 GI / 反射并存。这里的 feature 表示功能模块，并不指 URP 的 `ScriptableRendererFeature` 类。[Epic Hardware Ray Tracing](https://dev.epicgames.com/documentation/en-us/unreal-engine/hardware-ray-tracing-in-unreal-engine)

HDRP 17.3 支持独立 RT 阴影并将结果放在屏幕空间 buffer。公开 Graphics master 源码还能看到逐灯生成、slot/channel 写入和 lighting 根据灯的 screen-space shadow index 读取的过程。master 源码与 17.3 手册分别作为证据，不假定它们是同一 pinned revision。[HDRP 17.3 手册](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3/manual/Ray-Traced-Shadows.html)、[ScreenSpaceShadows 源码](https://raw.githubusercontent.com/Unity-Technologies/Graphics/master/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/Shadow/HDRenderPipeline.ScreenSpaceShadows.cs)、[LightEvaluation 源码](https://raw.githubusercontent.com/Unity-Technologies/Graphics/master/Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightEvaluation.hlsl)

AMD Hybrid Shadows 也单独组织阴影分类、射线与可选降噪。它说明可以局部替换 shadow 生成而保留混合渲染结构；不要求先重写整套 PBR 光照。[GPUOpen Hybrid Shadows](https://gpuopen.com/manuals/fidelityfx_sdk/samples/hybrid-shadows/)

### 2.2 联合直接光估计

用户对另一条行业路线的观察是成立的：MegaLights 使用固定数量的每像素光源样本，联合处理遮挡与直接光照，替换传统逐灯 shadowing / shading 的多个阶段。它通常不要求生成每个像素、每盏灯的完整 mask 集合。[Epic MegaLights](https://dev.epicgames.com/documentation/en-us/unreal-engine/megalights-in-unreal-engine)

RTXDI 集成同样是在最终选择的灯样本上执行 visibility ray，再执行材质 BRDF。其输出可直接是光照估计。[RTXDI Integration：Shade](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/Integration.md)

### 2.3 PBR 中仍有明确的遮挡含义

对固定表面，直接光可以概括为：

```text
直接光 = 对所有光源、所有入射方向积分 [可见性 × 入射光 × 材质响应 × 方向几何项]
```

阴影对应其中的可见性；完整路径的每段连接也受遮挡约束。联合估计改变了计算与存储方式，不能据此把“光照较弱”都叫成 cast：弱光、背光、材质吸收和几何遮挡是不同原因。[PBRT 的直接光采样实现](https://www.pbr-book.org/4ed/Light_Transport_I_Surface_Reflection/A_Simple_Path_Tracer)

## 3. 为什么 NPR 值得保留独立 Shadow 产品

这是针对本管线的设计判断：角色 toon 分层、主光选择、补光、轮廓光及多层接收规则，需要在最终着色前保留对遮挡来源的控制。逐灯可见性可以让材质选择“哪盏灯的遮挡影响哪一层”，而无需从最终 RGB 中猜测阴影。

需要保留三种语义的边界：

| 信号 | 表示什么 | 所属职责 |
| --- | --- | --- |
| 几何可见性 / 透射 | 表面到某个灯样本之间是否被挡 | Ho-RT 公共查询 |
| 某灯的 Cast 产品 | 该灯在约定采样、接收域上的阴影结果 | Ho-RTShadow |
| Toon 明暗、阴影色与分段 | 材质如何使用 Cast、法线和其他输入 | 材质 / NPR 光照消费者 |

RTShadow 输出不直接包含 albedo、toon 阴影色或最终 beauty。用户要的角色接收选择通过公共接收域声明传入；RTShadow 可以以后服务其他对象，不硬编码成只支持某个角色组件。

## 4. Ho-RT 应拥有的基础设施

```mermaid
flowchart TD
  R[Ho-RT 公共资源与帧准备] --> Q[场景快照与查询能力]
  Q --> S[Ho-RTShadow]
  Q --> D[Ho-RTDI]
  Q --> G[Ho-RTGI]
  Q --> F[未来反射等消费者]
  S --> P[逐灯 Cast 与可选派生产品]
  P --> N[NPR 材质与合成]
  D --> N
  G --> N
```

公共底座包括：设备能力、世界几何登记、当前蒙皮姿态快照、材质身份与 alpha 语义、场景 view / AS 生命周期、revision、每次 camera/render view 请求的资源准备及调度依赖。现有公共灯结构可以继续形成中立的灯数据契约，避免新增第二套不一致的 light identity / 变换 / 强度来源。

公共查询至少分两类：

- `TraceOcclusion`：给定射线及查询规则，返回是否存在有效遮挡；alpha 裁切参与真实求交。
- `TraceClosestHit`：返回最近有效命中的距离、实例/primitive/material 身份、重心坐标等，供后续表面重建。

只判断遮挡时可以在找到有效遮挡物后提前终止。需要最近 blocker 距离时必须使用相应查询能力；遍历中首先接受的命中不保证是几何最近命中，不能把二值 occlusion 的快捷结果当成准确 blocker 距离。

按灯样本查询时，可使用中立的 `SampleVisibility(surface, lightSample, policy)` 适配到射线。函数命名只是设计说明；底层求交无需知道哪个灯是主光。

必须分开 miss、invalid ray、unsupported、unavailable 等状态。不可查询并不等于“完全可见”。初始范围为已支持的不透明 / cutout；未来彩色透射是显式能力扩展。

光源抽样、reservoir、bounce、NPR 响应、NRD 信号及历史仍由各消费者管理。Ho-RT 的启用不隐式打开 DI、GI、Shadow 或时域累计。

## 5. 公共代码、公共资源与查询策略

### 5.1 从代码公共层到资源宿主

抽离前 DI / GI 各自创建、同步和释放 HoRTScene 及 hit adapter。R3 将这些改成消费者 pre-cull 声明请求、host 准备、消费者借用有效 view；创建和释放由 host 负责。仍保留不同 native hit 能力对应的 AS view，光照 Context 和历史继续属于各消费者。资源宿主的验收范围见 §8.5，不能只用搬文件或减少字段数证明资源收益。

### 5.2 共享粒度

世界登记与当前姿态应有公共所有者。AS 可以按兼容的几何集合、排除规则和材质查询能力形成多个 view。当前 DI 的 visibility hit group 与 GI 的 secondary surface hit group 不同，不能直接把其中一个私有 AS 地址交给所有效果就声称完成了共享。

先建立共同资源宿主与不可变输入合约，再用验证证明哪些 view 可以合并。共享几何快照与共享整个 AS 分别计量，不能仅根据少了一个 C# 对象宣称显存或 GPU build 成本下降。

### 5.3 查询规则由用途决定

Cast Off 的表面仍可能需要参与 GI 或反射。单/双面、Shadows Only、灯/物体 linking、材质排除等必须作为明确策略保存，不能把 shadow caster 集合强制变成所有 RT 效果的世界集合。

现有 inline `HoRTTraceScene` 会跳过 non-opaque。R4 前置步骤已将 DI / GI native any-hit 的 alpha 运算收敛到 RT 公共 `HoRTCutoutAlpha.hlsl`，原支持域不变；DI 的轻量 material adapter 移到 RT，并保留 GUID。GI 的完整 surface evaluation 仍由 GI 自己持有。新增 native 最近命中与二值遮挡入口区分距离语义，验收见 §8.5。

### 5.4 帧准备与多相机

由 host 收集消费者声明，准备并冻结当前渲染的场景快照；查询输入声明可读资源与 build 完成依赖。Cast 可在 GB/SB 之后、opaque toon 之前运行，DI/GI 保持自己的后置阶段。初版沿用现有 [Feature 排序契约](Ho-RenderFeatureOrdering.md)：明确配置宿主准备与生产者 / 消费者的列表次序，同事件按入队顺序记录。校验缺失或顺序错误的依赖，不能依靠未声明的全局资源，也不能仅用相同 RenderPassEvent 假定资源已准备好。RenderGraph 不会替应用自动重排尚未生产的 ContextItem。

宿主的 CPU 请求准备、GPU AS build 和效果执行是三个时机。宿主应在消费者记录之前给出冻结输入，AS build 则在首个实际 trace 前有明确命令依赖；不因此把所有效果搬到同一个 pass，也不要求宿主拥有最早的屏幕绘制。具体注册 / 准备 hook 在 R0 核对现有 URP 生命周期后确定。

RenderGraph 对 AS 的真实访问和 build 依赖需要由公共实现显式处理。现有 [HoRTScene.Inputs](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RT/HoRTScene.cs:253) 的 revision 检查应保留并扩展到统一所有权；GPU 使用结束前不能更新或释放对应快照。

世界数据只在兼容域内复用；相机屏幕输入、接收域、输出、采样和历史按 render view 管理。GameView、SceneView、Prefab Stage、不同 RendererData 和过滤配置不能混用。有效性不能只看 `Time.frameCount`。

Ho-RT 缺失、禁用或设备不支持时，消费者报告明确状态，并按各自声明的 fallback 工作；不能隐式再创建自己的隐藏公共底座。

## 6. RTShadow 能否输出全部灯的 Cast

可以。必须先区分“全部灯各自的结果”和“把全部灯合成一个结果”。这里的全部指当前请求范围内、受支持且参与直接投影的灯集合；环境光、发光网格与间接光不自动归入第一版。

### 6.1 优先保留逐灯产品

输出应能够由稳定 `light ID` 查询到自己的可见度，而不是只靠每帧可重排的临时灯索引。可用 texture array、atlas 或稀疏 buffer；存储布局不改变逐灯语义。

每项同时携带有效性及必要域信息：灯身份/修订、camera / surface sample 域、接收域、场景快照、样本预算和过滤状态。第一版不必把所有这些都写成全屏纹理，但必须能验证消费者读取的是正确结果。

有限光源的整灯平均可见度还要注明抽样 measure / 权重。面积光不同方向的可见性与 BRDF、cosine、纹理发光相关；一个平均 V 通常不足以给任意材质精确重建整灯照明。需要严格积分的消费者仍查询自己的实际灯样本。

### 6.2 连续值与 NPR Ramp 合约

正式 NPR 接口必须保留**尚未经过 toon 阈值和着色的浮点可见度**。`V=0` 表示完全遮挡，`V=1` 表示完全可见；半影允许中间值。单灯硬投影只用于建立几何参考，不能凭此宣称生产接口已经满足连续 ramp 的需求。

这里有三种不同的连续量：

| 连续量 | 来源 | Ramp 的用途 |
| --- | --- | --- |
| 法线 / 光方向生成的明暗坐标 | `N·L`、half-Lambert 或已有 NPR 规则 | 物体自身的明暗分段 |
| 单灯有限光源可见比例 | 对同一表面点积分多个实际灯样本 | 对真实半影重新分段或着色 |
| 可选的美术边界坐标 | 约定范围的可见度重建、阴影边界距离等 | 独立于物理光源大小的边缘塑形 |

第一项由材质 / NPR 光照构造。第二项是 RTShadow 的核心生产信号。第三项只有在明确需要时才增加，标为派生美术信号，并注明单位、作用范围与表面关联规则，不冒充物理可见度。

零角度方向光照到一个确定的不透明表面点时，原始遮挡本来是 0/1。同一个点、同一个方向重复查询不会产生半影或连续梯度。有限光源的积分可以产生连续的可见比例；空间子像素积分则处理像素覆盖，两者作用不同。[PBRT：Area Lights](https://www.pbr-book.org/4ed/Light_Sources/Area_Lights)

即使接口使用 float，有限样本也不自动得到无台阶的信号。N 个等权二值灯样本的可见比例只有 N+1 个取值。需要用足够的同帧采样和适当的重建检查实际梯度质量；不能用 8-bit、先阈值、或强行拉高对比度来替代。中间累加使用足够精度，原始估计与重建产品有明确区分。重建需保留深度、法线和对象边界，避免跨表面污染。

建议每个实际空间着色样本的顺序为：

```text
NPR 明暗坐标 h + 该灯的连续可见度 V
    → 按材质各层的接收规则组合成 ramp 坐标 u
    → 材质执行 ramp / 阈值 / 阴影色
    → 当前帧各空间样本的最终颜色合成
```

RTShadow 不提前执行 ramp，也不将法线明暗烘进核心 V。`h` 与 `V` 维持独立输入；组合函数由 NPR 语义决定，不在底座中写死一种乘法。多层 toon 可以共享同一份 V，并分别设置接收强度、border 与 ramp。

当前 lilToon 确实在 [接收 Cast](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:963) 后，才执行各层 [toon 分段](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:1010)。新后端应保持这一输入边界；验证时也不能只看一张“黑白轮廓正确”的图。

如需在几乎零物理半影下仍用 ramp 大幅移动或扩展投影边，单纯的二值 V 信息不足，需要明确的美术边界信号或指定宽度的可见度重建。单张屏幕距离场会受分辨率、遮挡层和跨表面边界影响；最近 blocker 距离只描述深度间隔，也不等同于到投影轮廓的距离。此类扩展应单独设计与验证。

连续值验收至少包括：原始 V 灰度、细半影的数值剖面、不同 ramp threshold 的轮廓、窄 / 多层 ramp 下的 banding 与噪声、动画微动，以及最终空间抗锯齿。不能仅凭浮点格式或后置模糊就判定通过。

### 6.3 连续阴影重建采用 NRD SIGMA

按用户本轮收敛，正式阴影重建优先采用 NRD 的逐灯 shadow denoiser **SIGMA**，通过现有公共 NRD 层接入。SIGMA 负责阴影信号重建，NPR shader 负责 ramp；独立的美术距离场保持为以后确有需求时的扩展。

```text
RT 灯样本的真实遮挡与 blocker 距离
    + 光源大小 / 角度 + 当前表面 guides
    → SIGMA 官方前端编码
    → SIGMA 降噪
    → 官方后端解码得到浮点可见度 V
    → NPR 材质各层的接收与 ramp
```

这里不是把一张任意灰度图直接送进现有 RELAX。SIGMA 有自己的 penumbra / 距离编码与输入约定；需要遵守有效命中、miss、NoL、法线、深度及启用历史时的运动输入规则。需要 blocker 距离时，使用真实最近命中，不能沿用仅为二值遮挡提前结束的任意命中距离。NPR 的修改法线 / half-Lambert 接收域与 SIGMA 官方有效域之间的关系要明确验证，不能把域外数据误报为有效几何可见度。

后端必须使用 `SIGMA_BackEnd_UnpackShadow`。本地 pinned 版本该宏执行平方解码；直接把编码输出当作线性 ramp 坐标，会改变阴影过渡。[本地 NRD 前后端](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/ThirdParty/NRD/NRD.hlsli:941)

预览可以启用 SIGMA 时空稳定；单帧导出提供 `maxStabilizedFrameNum=0` 的无历史模式，单独验证其输入预算和 ramp 下的画质。本地 pinned `SigmaSettings` 也明确该参数为零时禁用 stabilization。[本地设置](D:/Unity_Fork/lilToon-URP-Extensions/Runtime/RTDI/ThirdParty/NRD/Include/NRDSettings.h:463)、[NRD 官方说明](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#best-practices)

逐灯保留 raw 与 denoised 的身份及状态。使用历史时按相机、灯身份及场景有效性隔离；多灯流式无历史处理与持久保存所有逐灯结果是不同资源方案。

当前仓库的公共执行器与 native 调度 ABI 可以作为基础；生产代码目前只有 RELAX 设置绑定及 DI/GI 的 RELAX manifest。R0 已对现用 DLL 实测：NRD 4.17.3、normal encoding 2 / roughness 1，明确支持 `SIGMA_SHADOW`（16）及 `SIGMA_SHADOW_TRANSLUCENCY`（17），两种实例均创建成功，各有 9 pipelines、1 permanent、8 transient。无需为 SIGMA 重编当前 native 插件。接入仍需补匹配版本参数 ABI、Unity shader wrappers、外部输入输出绑定及 GPU 验收；descriptor 成功不等于生产降噪链路已完成。证据见 [R0 inventory](../Tests~/RT/R0/sigma-inventory.json)，可执行 [CPU probe](../Tests~/RT/R0/probe_sigma.py)。

### 6.4 派生输出按需求提供

| 可选产品 | 用途 | 明确限制 |
| --- | --- | --- |
| 指定灯的可见度 | 主光 Cast、角色分层 | 与该灯和表面采样域绑定 |
| 一组 / 全部请求灯的逐灯集合 | 多灯 NPR、后续按灯重组 | 样本与存储成本随请求量增长 |
| 主光 / 补光 / 轮廓光等灯组信号 | NPR 的灯组控制 | 分组规则及合并算子是显式美术约定 |
| 未遮挡与已遮挡的漫反射 irradiance 对 | 测量因直接遮挡损失的入射照明 | 包含选定法线、灯权重与光源域，不是通用几何可见度 |
| 基于上述对的归一化遮挡量 | 某个明确光照模型下的 NPR 遮挡驱动 | 无照明时无有效比值；不能替代各灯 / 各 BRDF 的结果 |

完整 direct diffuse/specular radiance 继续由 RTDI 或指定光照消费者生成，避免 RTShadow 再实现一套竞争的直接光积分器。

### 6.5 一张通用“全部灯 Shadow”没有唯一含义

假设两盏灯的未遮挡贡献分别为 100 和 1。挡住强灯与挡住弱灯时，逐灯 V 分别为 `(0,1)` 与 `(1,0)`；简单平均都得到 0.5，但保留光照分别为 1 与 100。

对**已经固定定义**的未遮挡贡献 `L_i`，可以构造：

```text
L0 = Σ L_i
Lv = Σ (L_i × V_i)
A  = Lv / L0                  // 分母有效时，分量除法
```

这个 A 只对生成它所使用的光照模型成立。漫反射、高光、不同法线或 toon 层换了一套权重，A 也可能改变。不同颜色的灯还可能需要 RGB，不能全部压成灰度而保留等价性。

NRD 官方 SIGMA 文档明确支持逐灯 shadow-only 处理，并讨论了类似的多灯加权合并，同时说明其过滤与适用限制。本轮优先逐灯 SIGMA；该合并方案作为以后有明确消费需求时的参考。[NVIDIA NRD README：SIGMA](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)

### 6.6 全灯能力不要求始终保存全屏全灯

消费者只请求所需灯、接收对象及空间域。只有要做后续逐灯调整时，才需要持久保存对应逐灯产品；只需要已定义的灯组或 irradiance 对时，可以逐灯流式计算并汇总。未查询到的灯必须有显式状态，不能伪装成无遮挡。

存储量也应如实记录：1920×1080、32 盏灯、每灯一张 R16F 已约 126.6 MiB；3840×2160 则约 506.3 MiB，均未包含历史、深度、元数据或其他临时量。全灯是语义能力，具体生产与存储应按需求配置。

## 7. 与 RTDI / RTGI 的关系

RTShadow、RTDI、RTGI 共享场景与查询能力，可以共享兼容的灯数据与采样工具。RTDI / GI 不强制读取 RTShadow 的屏幕 mask：它们可能查询不同灯样本、次级表面或反射中的表面。

已经包含 visibility 的 DI radiance 再乘同一 Cast，会重复衰减。需要复用结果时，必须证明灯样本、表面域、查询规则及权重完全匹配；不能仅凭“同一盏灯”就默认可用。

NPR 消费者应明确每条输入是未遮挡照明、已遮挡照明还是独立可见度，以及由哪个阶段应用一次。Cast、AO、GI 强弱与 toon 固有明暗保留不同通道。

现有 shadow map 与 RTShadow 可以生产同一种受约束的 Cast 接收合约。同一相机、灯与接收域的最终 Cast 要有明确来源选择，避免两路重复应用；具体通过替换还是额外叠加，必须由合成语义明确决定。

## 8. 实施路径与阶段验收

本轮按 **核对基线 → 纯目录迁移 → 建立宿主 → 迁移既有 DI/GI → 单灯 Cast → SIGMA 与 NPR 接收 → 多灯扩展** 推进。每阶段是可单独审查、验证和提交的边界；当前状态全部为待实施，不因本文写出接口就视为完成。

先让现有消费者证明公共资源宿主正确，再增加新阴影功能。SIGMA 的 DLL 能力探测提前做，正式信号接入则等待 Cast 提供真实、正确的距离与表面数据。

### 8.1 目标目录与模块归属

```text
Runtime/
  RT/                  # 由现有 RayTracing 整体迁入，加 HoRTRendererFeature
    Shaders/           # 通用 query / hit / primary / alpha 等公共实现
  RTShadow/            # 新增阴影 feature、采样、逐灯输出、SIGMA 信号适配
  RTDI/                # 保留直接光策略、输出、采样、合成与独立历史
  RTGI/                # 保留间接光策略、输出、采样、合成与独立历史
  NRD/                 # 通用 native ABI、执行器与插件，继续独立
Editor/
  RT/                  # 由现有 Editor/RayTracing 迁入
  RTShadow/            # 新阴影 Inspector 与验证入口
```

`Runtime/RayTracing` 与 `Editor/RayTracing` 迁移后不留平行目录或转发类。C# 公共命名空间同步由 `.RayTracing` 收敛到 `.RT`，`HoRTScene` 等类型名称保留。新 host 管资源，RTShadow 管阴影产品；公共 NRD 不并入某个效果。SIGMA 的通用执行支持进 `NRD`，阴影前后端输入、逐灯状态与 manifest 由 RTShadow 适配。

### 8.2 分阶段交付

| 阶段 | 工作与边界 | 完成条件 |
| --- | --- | --- |
| **R0 基线与能力核对** | 固定当前 DI/GI、CS 与资源基线；确认真实 URP 准备 hook；读取当前 NRD DLL 的支持列表及 SIGMA descriptors | 有可复现的支持 / 缺失结论、当前源与验证对应关系、资源所有权及调度合同；不改生产效果 |
| **R1 纯目录迁移** | RayTracing → RT，保留 `.meta` GUID，更新命名空间、include 和当前验证工具路径 | 源码差异限于迁移与引用；Runtime/Editor 编译及必要 shader / 求交回归通过，DI/GI 画面行为不变 |
| **R2 Ho-RT 宿主与共享场景数据** | 新建 HoRTRendererFeature，提供 renderer / render-view 域内的快照与查询 view；统一几何登记、姿态快照和生命周期 | 两个独立测试消费者取得正确输入，兼容姿态不重复 Bake，依赖 / 失效 / 释放 / 多相机隔离可验证 |
| **R3 DI/GI 资源迁移** | 先 DI、再 GI，从私有 new / Synchronize / Dispose 改为请求 host；保持各自光照算法、输出、材质求值和历史 | 单独与同时启用、开关 / 重建 / resize / 材质与姿态变化通过；匹配输入下 raw / filtered 与基线一致，没有隐藏私有场景回退 |
| **R4 单灯 RTShadow** | 新增独立 HoRTShadowRendererFeature，先做一个方向光的角色 Cast；提供逐灯身份、raw 可见度、真实 blocker 距离与明确有效性 | opaque / cutout / 离屏遮挡 / 投射规则正确，主光接收来源明确，未接 SIGMA 时可独立调试与作硬影参考 |
| **R5 SIGMA 与 NPR 正式接收** | 补通公共 NRD 的 SIGMA 支持，逐灯降噪、官方解码后进入现有 ramp；提供预览与无历史导出模式 | 连续 V、窄 / 多层 ramp、角色微动及冷启动单帧验收通过，历史不串灯，当前固定状态可独立导出 |
| **R6 多灯与派生输出** | 扩展点 / 聚光及经过单独验证的光源形状；按请求生成灯组 / 全部相关灯的逐灯产品 | Light ID / slot 重排、灯增删、不同接收域和逐灯采样正确；按需保存或流式汇总，成本和有效性明确 |

### 8.3 各阶段必须落实的内容

**R0：先消除接入不确定性。** 用当前 pinned NRD 4.17.3 的 DLL 读取 supported denoisers、创建 SIGMA 实例、检查实际资源格式与 pipelines。若当前 DLL 已包含 SIGMA，复用原插件；确实未包含时才设计匹配版本的构建，不为这一步顺带升级 SDK。确认 SIGMA 所需距离、NoL、guides、平方解码及禁用 stabilization 的合同。结果更新到本文 §6.3，不另开新的过程文档。

同时审计 `Create / AddRenderPasses / RecordRenderGraph / Dispose` 与当前 renderer / camera 作用域。确定消费者怎样声明需求、host 在何时冻结快照、何处验证顺序；不以一个跨所有 RendererData 的静态全局 AS 代替所有权设计。记录当前 DI/GI 配置、输出、历史和已知失败作为迁移基线，历史 run / SHA 保留原样。

**R1：迁移与渲染行为分开。** 使用保留文件和目录 `.meta` 的移动，更新当前 C# 引用、Shader include、资源加载及可执行测试 / 生成器路径；核查 `Shader.Find`，保留与目录无关的既有 Shader 名称。保留材质 Shader GUID、shader globals、native ABI 字符串和数值。历史源码快照、run、source-index 和旧 SHA 不批量改写；当前测试若硬编码旧路径，迁移执行入口并标注其与历史记录的关系。

**R2：公共所有权需要覆盖实际资源。** host 持有世界登记和可复用的几何 / 蒙皮快照，`HoRTScene` 继续是其内部实现。view 根据几何过滤、用途策略和 hit-material 能力确定；保留当前 DI / GI 不同 hit group，先允许相同姿态供多个 AS view 使用。此阶段不把所有材质求值重写成一个超级 shader，也不承诺一个 AS 能满足全部策略。

请求至少表达当前 renderer / 相机渲染域、所需集合与查询能力；返回值带可用性、lifetime / revision 与 GPU 使用期。只做遮挡和最近命中是不同能力。相同快照内的相同姿态由共享缓存提供；构建次数、Bake 次数、实际持有 Mesh / buffer 的计数证明复用，不能仅靠把两个 scene 字段包进 host 就算完成。场景 / Prefab Stage / Editor 同帧变化、多相机、重复 Create / Dispose、消费者中途停用和 GPU 使用期均需覆盖。

首版沿用显式 Feature 排序并检查依赖，见 §5.4。生产 host / GB / SB 需要先于相应消费者准备或记录；GPU build 在首次 trace 前建立顺序，消费者 pass 自己声明所有读写。缺 host、错误顺序或 unsupported 返回明确状态，不能读取上一次相机的全局资源。

**R3：一条消费者一条消费者迁移。** DI 与 GI 分别完成审查和提交，保留其几何 / 材质排除、采样参数、灯选择、合成方式、NRD profile、独立历史和失效条件。消费者提供用途与材质 adapter，host 拥有相应 view 及几何数据；消费者停止不释放别的效果仍在使用的共享资源。最终移除 DI/GI 对场景的私有创建、同步与释放，不提供缺 host 时静默重建原私有场景的路径。

资源共享不随意改变 revision 的语义：一个无关 view 的改变不应在没有输入变化时清掉另一效果历史，真实相关变化也不能漏报。DI 单灯精确路径、多灯 / 环境路径、GI raw / NRD / 合成均保留匹配输入的对照。原本失败的质量或 AS 增长夹具继续作为未解决项，不能因本轮迁移修改门槛或改写历史。

**R4：明确 Shadow 与公共几何查询之间的边界。** RTShadow 独立依赖 Ho-RT，先使用 native raygeneration 与现有已支持的 alpha 能力；公共 alpha 判定和支持域归入 RT，DI / GI 的完整材质与辐射求值仍由自身负责。接收集合限角色不代表 caster 只含角色，Cast Off、Shadows Only、单 / 双面与 layer / linking 是 Shadow 的显式用途策略。

零角度方向光验证精确二值遮挡；非零角度验证实际光源样本及正确距离，不能把硬影复制多次称为半影。为 SIGMA 准备的数据按最近有效命中计算，不能使用任意 first-hit 的距离。先保存 raw 数据；无效表面、无请求、未支持和 miss 分开。屏幕输入必须匹配 GB/SB 的相机、jitter、分辨率和 owner。

正式 Cast 通过现有材质接收位置进入，保留各层 receive / Mask / ramp。选定主光与接收域的来源在 RT、当前 CS / URP 回退间明确解析，不能重复叠乘；不影响其他附加灯的原通道。主光和接收域采用稳定身份，接口可表示多灯，但此阶段不预分配全灯全屏纹理。

**R5：SIGMA 作为生产链路，不把 RELAX 改名使用。** 公共 NRD 补类型独立的 settings 提交、匹配的 `SigmaSettings` ABI 及实际格式支持，RTShadow 补按当前 descriptors 生成的 manifest、原版 shader wrappers、输入绑定和官方前后端。原 RELAX 调度、参数和 shader 不因新增 SIGMA 改变。先验证一个独立 SIGMA 输入夹具，再连接真实角色 Cast，并保留 raw / denoised 对照。

预览允许逐相机、逐灯的时空稳定；导出验证 `maxStabilizedFrameNum=0`。同帧多样本若用于提高质量，必须明确匹配该 SDK 的输入统计和编码，不能把平均可见度或平均命中距离随意填进 `IN_PENUMBRA`。高预算无降噪图可作为独立参考。不同模式的 seed / frame index、reset、切镜头、灯移动与 slot 重用均有明确定义。

验收以 SIGMA **解码后的连续 V** 和实际窄 / 多层 ramp 为准，检查 banding、漏光、细发片丢失、拖尾和对象边界。完整本影仍允许为 0，不要求降噪凭空制造内部梯度。最终颜色的空间 AA 与物理半影分开评价。固定场景、时间及样本种子下，冷启动 / 任意帧序结果要符合无历史合同；其他 GI / TAA / 模拟的历史另行处理。

**R6：只在单灯质量闭环后扩展。** 用稳定 Light ID → 本次 slot 的元数据发布逐灯结果，维护灯增删 / 重排 / 类型变化和历史隔离。“全部灯”限定为当前支持且被请求的显式灯集合，不自动包含环境光、所有发光网格或间接光。灯组 / 加权遮挡是明确定义的可选派生输出，完整直接光积分继续属于 DI。只请求主光时不承担全灯存储与追踪成本。

### 8.4 里程碑、验证与停止边界

- **底座里程碑（R0–R3）**：目录归属明确，Ho-RT 是真实资源宿主，DI/GI 已迁入且保持各自行为；共享几何 / 姿态有可测证据，多 view 和多相机生命周期正确。
- **NPR 阴影里程碑（R4–R5）**：独立 RTShadow 可供角色材质使用，SIGMA 输出正确解码的连续 V，保留实际 ramp 语义，预览和无历史单帧分别验收。硬影参考通过不等于该里程碑完成。
- **多灯里程碑（R6）**：多个受支持灯的逐项可见度和按需输出稳定，身份、历史、采样及成本记录完整。

每步按影响范围运行 Runtime / Editor 分程序集编译、相关 shader 编译与 D3D12 RenderGraph / compatibility GPU 检查；涉及 unsupported 则保留 D3D11 安全分支，涉及资源迁移则检查实际 Player 加载和释放。只对必要变化扩大回归，不重复无关实验。验证使用独立工程或隐藏测试进程，遵守用户不操作桌面的约束。

每阶段先固定输入及当前源码，再审实现、检查数值 / 画质 / 生命周期，匹配证据通过后单独提交。新增结果写回本页对应状态及既有测试位置，不再建立多份计划、交接和收尾文档。失败保留原门槛与原因，不能用过程退出 0 或目录去重冒充正确性 / 性能收益。

### 8.5 目标模式进展与每步评价

用户已授权按本路径持续实施；每步独立过滤提交。设计和阶段评价只更新本页，当前任务证据统一置于 `Tests~/RT/`。不操作用户桌面，不改用户工程资产。只在有实现与验证证据时推进阶段状态，不将已派发任务当完成。

| 阶段 | 状态 | 正确性 / 收益 | 成本与偏航检查 | 后续 |
| --- | --- | --- | --- | --- |
| R0 | 已完成 | 637 项 RT 源码 / 资源基线 SHA 已固定；Runtime / Editor 编译 0 errors；现用 DLL 两 SIGMA 描述实例成功；真实 URP hook 已核对 | 生产源码及插件未改，无画质 / 显存收益声明；排除不必要的 native 重编和自动排序设计 | R1 纯迁移 |
| R1 | 已完成 | RayTracing→RT，2098 个产品文件规范化核对仅有允许的命名 / 引用变化，20 份移动 metadata 与基线字节一致；编译 0 errors；10 项真实 GPU / unsupported 回归通过 | 只统一模块归属，无画质 / 性能收益声明；旧验证源码 / SHA 未改，初次冷导入 stale-transform 失败保留，后续同条件原码与当前码通过但原因未证明 | R2 宿主与共享姿态 |
| R2 | 已完成有界验收 | host / 请求 epoch / cache 已实现；RG / compatibility 普通夹具各 93 checks / 11 请求，初始、骨骼和源 Mesh 编辑共 Bake=3，两个 view 均更新；各自退休专项 29 checks / 4 请求，profiles 完整释放；9 项组合回归通过 | 保留多 AS view，初版在 CPU 等待前一 fence 后再修改，无帧时间收益声明；缺失完成 fence 的异常 work 保守隔离，完整取消恢复仍未验收 | R3：先 DI，再 GI |
| R3-DI | 已完成有界迁移验收 | DI 不再私有创建 / 同步 / 释放场景；scene lifetime 变化清对应相机历史，profile 由 host 持有；247 个核心文件及全部 65 个 shader 文件未变，9 项宿主 / Cutout / RELAX / unsupported 回归通过 | 旧全量 core 原码与迁移版均出现同一 4 条 F39 scratch 诊断及 power-proposal 断言，继续拒绝该夹具；不扩称全量 core 或生产双模块共享通过，Player 在组合迁移后验证 | R3-GI |
| R3-GI | 已完成有界迁移验收 | GI 不再私有创建 / 扫描 / 释放场景；81 核心文件及全部 59 shader 文件未变；9 项回归、生产 DI/GI 共享姿态 / 独立历史 / 双 NRD dispatch 两路径、GI Mono Player 两路径通过 | 4 条退出 GC 警告与原 Backbuffer Player 日志 SHA / 精确数量一致，宿主显式退休域为 0；不是全管线退出清理或旧质量缺口通过 | R4 |
| R4 公共查询前置步骤 | 已完成有界验收 | native 最近 / occlusion 入口、共享 alpha 和 ShadowCasters 用途；两路径各 122 checks / 13 请求，另 4 项原回归通过 | 多用途可保留多 AS，不声称最终质量或性能提升 | R4 raw |
| R4 raw Cast | 已完成有界验收；R4 未完成 | 独立 Ho-RTShadow、主方向灯全分辨率 32F raw / status / owner / 最近 blocker；OB 部件接收、同帧有限角度采样及消费资源 fence 释放；8 项组合回归通过 | 真实 lilToon NPR 接收 gate 失败，撤回主光替换；SIGMA 未实现；不能宣称阴影最终画质完成 | 独立 SIGMA 夹具与生产信号，同时保留 R4 NPR 待完成项 |
| R5–R6 | 待实施 | 不提前宣称 | 每步按本节条件复核 | 依次推进 |

R0 采用的宿主准备合同：

1. Ho-RT 显式排在消费者前。host `OnCameraPreCull` 打开本次 epoch，消费者各自的 pre-cull 提交不可变请求；全部 pre-cull 先于 `context.Cull`，随后才是全部 `AddRenderPasses`，两后端共用此顺序。host 在 Add 聚合扫描与同步并冻结，consumer 在 Add 取得自己的 view；不以仅 compatibility 存在的 `SetupRenderPasses` 做公共 hook。
2. 定位与有效性键为 renderer 实例、camera 和单调 render epoch，不能仅比较 frameCount。Create 只创建宿主自有状态，不猜当前 renderer。迟到请求、缺 host、错 epoch 明确拒绝；定位表不拥有一个跨所有 renderer 的全局 AS。
3. 共享蒙皮快照用单调 PoseVersion，每个 AS view 保存已观察的版本。一次 Bake 后所有相关 view 都需要 UpdateInstanceGeometry；不能共享一个被首个同步消耗的 `changed` 布尔。
4. views 保留自己的相关 revision、材质适配能力和 GPU 使用期；场景或 adapter 的销毁不能破坏其他消费者或已记录输入。AS build 仍在明确的首个 trace 之前执行，保留 stale-input 拒绝与非 async unsafe 依赖。

源码依据与冻结基线见 [baseline.json](../Tests~/RT/R0/baseline.json)，复核入口 [verify_baseline.py](../Tests~/RT/R0/verify_baseline.py)，本次分程序集编译记录 [compile.json](../Tests~/RT/R0/compile.json)。旧 GPU 结果仅用于确定已有验证范围；R0 没有把历史 SHA guard 改成当前通过，也未声称进行了新的 GPU 画质验收。

R1 复核见 [migration.json](../Tests~/RT/R1/migration.json) 与 [review.json](../Tests~/RT/R1/review.json)，入口为 `python Tests~/RT/R1/verify_migration.py`、`python Tests~/RT/R1/verify_results.py`。10 项覆盖场景、主表面、蒙皮的 RG / compatibility，DI 两路径真实 Cutout、GI native Cutout 和 D3D11 unsupported。主表面既有夹具按原 Gamma 域断言 `[Gamma]` 参数；Linear 域运行被拒绝，未改断言。旧 A01 对照工程命名空间编译失败、空工程 ShaderGraph cache EPERM 和冷导入失败均保留；正式空 RT 工程通过后才接纳迁移。当前 runner 与初始化入口集中 `Tests~/RT/`，没有批量重写历史测试脚本。

R2 复核为 [review.json](../Tests~/RT/R2/review.json)，入口 `python Tests~/RT/R2/verify_results.py`；重跑 `Tests~/RT/run_validation.ps1 -Stage R2`。最终 9 项使用同一套 1937 个产品 / 验证资源指纹：宿主 RG / compatibility、两条 dispatch 后退休专项、D3D11 unsupported、原独立蒙皮两路径、DI Cutout 和 GI native Cutout。原始中间通过记录不覆盖，最终源码以 review 指定 run 为准。

R2 的实际收益是两个 AS view 共用同一份姿态 Mesh，并对每次相关编辑只 Bake 一次；不同 native hit 能力的 AS 尚未合并，未宣称显存 / 毫秒收益。初版 `LastPrepareWaitMilliseconds` 明示等待成本：CPU 看到前次尾 fence passed 后才修改 Mesh / material / AS，最长等待 5 秒后拒绝本次准备，不超时强行释放。GPU queue wait 不能代替 CPU 释放资格。资源退休由独立帧末 / Editor update 驱动；scene / AS 先释放，pose lease 随后，再释放 profile / cache。

提交前复核修正了两条接口风险并纳入最终测试：有 material factory 必须提供稳定 profile key；所有 scene Read 都通过宿主登记使用，不能从 `view.Scene` 绕过 GPU pin。源 Mesh 原地编辑必须走 host `MarkGeometryDirty(renderer)`，同时刷新所有 view 与共享 cache。已捕获但尾 fence 缺失的异常 / 取消路径仍可能隔离滞留，不冒称完整故障恢复已支持；后续若实际触发，需用真实完成证据恢复，不能靠超时清 pin。R3 迁移仍保持这个边界。

R3-DI 见 [review.json](../Tests~/RT/R3/DI/review.json)、[核心未改动审计](../Tests~/RT/R3/DI/migration.json)。宿主合同两路径各 16 项检查覆盖真实 temporal 执行、缺失依赖、重建 lifetime、profile 与 client 停用及解析能量；原 Cutout 与 RELAX 两路径、D3D11 unsupported、宿主及 GI native 回归合计 9 项通过，1939 项产品指纹未漂移。当前 DI 配置必须显式添加 Ho-RT 并放在消费者前，不自动编辑用户 Renderer 资产；独立测试和 Player 构建入口已补该依赖。

本步未更改光照 shader、NRD profile、Context 或输出算法。旧 `Run` 全量 core 被拒绝后，使用 Git `66a2bb80` 完整原包在同一隔离工程复现同一 4 条 scratch 诊断和 power-proposal 断言，控制记录保留；此旧失败不通过放宽门槛解决，也不反复调 AS flags。`Tests~/RT/run_validation.ps1 -Stage R3DI` 默认运行明确有界的 9 项；`-Case di-core-rg` / `di-core-compat` 仍可显式诊断旧全量路径，但不作为通过证据。收益为消除 DI 私有场景所有权，性能收益尚未测量；GI 和真实双模块 / Player 接入留待下一步骤。

R3-GI 当前证据见 [GI review](../Tests~/RT/R3/GI/review.json)、[核心未改动审计](../Tests~/RT/R3/GI/migration.json) 和 [Player review](../Tests~/RT/R3/Player/review.json)。组合夹具两路径各 18 项检查，真实 DI/GI 使用不同 native profile 与 AS view，同一姿态 Mesh 初始只 Bake 一次、骨骼编辑再 Bake 一次；两路 temporal 和各自 NRD 均实际执行，停用一路不破坏另一路，host lifetime 重建拒绝旧历史。环境和 RELAX 两路径、unsupported、native Cutout 与 DI Cutout 回归共同通过，1941 个产品指纹一致。旧 GI 夹具限定的独立工程名称已扩展允许新的空 RTValidation 工程，不改像素门槛；原专名拒绝运行保留。

两个 GI Win64 Mono / D3D12 Player 使用序列化 host、GI 与 RELAX，逐真实 Update 提交相机请求，原动态 / 能量 / 降噪 / 合成及开关检查通过；打包 native DLL 与当前 pinned DLL 字节一致。4 条 GraphicsBuffer GC 退出警告首次严格审查被拒绝后，查到原 Backbuffer Player 记录也有完全相同的 4 条，SHA 已核验。最终只按精确数量和原记录接纳该已知警告，另要求显式宿主清理与退休域 0；未改全管线 OB / 公共 fallback 清理，不将画面通过等同全部资源清理通过。中间的 `Application.quitting` 候选未改变这组警告、没有资格收益，已撤回；最终 host 生产改动仅新增有效 view 的 profile 借用入口，不重做 R2 生命周期。

本步收益是生产 GI 场景所有权收敛、实际两模块姿态共享；保留多 AS 与 fence 等待，没有 GPU 毫秒收益承诺。DI 的完整 Player 在 mixed diffuse/specular proposal（temporal=False）失败；同一隔离工程切换 Git `66a2bb80` 的迁移前完整原包后复现相同断言，控制记录与失败日志保留。没有更改 DI 算法或将完整测试改为通过。

R3 Player 收尾的两路径有界 DI 探针先执行原解析光 / specular、蒙皮及非 Read/Write Cutout 检查，再执行双灯实际 RELAX dispatch 与解析能量、显式 host 依赖和退休域 0；通过后退出，不执行已知失败的完整 proposal oracle。探针只在隔离工程的副本插入，原完整探针保持，注入源码与产品指纹均留 SHA。四个最终 Player（DI 有界 / GI × RG / compatibility）构建和执行退出 0，native 字节一致。此步收益为验证序列化、实际 Player 原生 / NRD 加载与 host 接入，成本为两组有界构建；不继续在既有 proposal / GC 问题上滞留。下一步进入 R4，不替代旧 GI filtered 质量、F39、完整 DI proposal 或复杂动态 / 1080 预算资格。

R4 公共查询前置步骤见 [Query review](../Tests~/RT/R4/Query/review.json)，执行 `Tests~/RT/run_validation.ps1 -Stage R4Query`。新增 `HoRTTraceClosestVisibility` 返回真实最近有效 blocker 的世界距离、RT 实例 / primitive、重心和正反面；`HoRTTraceOcclusion` 的快捷路径只公开 status，不把 traversal first-hit 距离暴露给 SIGMA。native `Inputs.BuildAndBind` 同时绑定实例 metadata，状态沿用 0 miss / 1 hit / 2 metadata 错配 / 3 invalid ray / 4 unavailable，失败 payload 清零。方向先归一化；mask 和有限区间显式校验。

`HoRTViewRequest.geometryPolicy` 默认 Surface，保留 DI / GI 原双面、Cast Off 仍在场景中的行为；ShadowCasters 用途排除 Cast Off、保留 Shadows Only，按材质 `_Cull` 的 Off / Front / Back 和 Renderer TwoSided 组织 AS culling。请求 layer / 排除材质与用途分开。新 native hit material 只求可见性，不借用 GI 的完整着色 payload；其 alpha 运算与原 DI / GI 两份实现逐操作核对一致，DI/GI 其余核心文件未变。

GPU 夹具在 `BeforeRenderingOpaques` 使用真实 host view，核最近遮挡物、透明洞后的远 blocker、离屏 caster、变换、用途与裁剪模式切换、mask、invalid、fresh generation；镜像缩放与实际 URP raster 的两个观察侧直接对照。RG / compatibility 各 122 checks / 13 请求，原 DI native Cutout 两路径、GI native alpha / mask / UV / backface、原 Surface 场景回归通过。首次缺失 native metadata 绑定、夹具初始反面前提及镜像预期错误都保留失败记录，未通过改像素门槛掩盖。

本步收益为去掉三路 alpha 判定分叉、提供 SIGMA 必需的正确距离与独立 caster 策略；成本为用途不兼容时额外 AS view。没有提高采样预算或增加 temporal 依赖，没有最终阴影清晰度收益声明。停止扩展公共层，下一步直接做独立 RTShadow 的单方向灯全分辨率 raw / 有效性 / 接收域产品；本步骤通过不代表 R4 或连续 ramp 里程碑完成。

R4 raw Cast 的限定验收见 [R4 review](../Tests~/RT/R4/review.json)，入口 `Tests~/RT/run_validation.ps1 -Stage R4`。新增 `Runtime/RTShadow/` 与 `Editor/RTShadow/`，Renderer 列表显式顺序为 Ho-RT、GB、SB、Ho-RTShadow，使用同一 `BeforeRenderingOpaques` 事件；用户 Renderer 资产不会自动改写。Feature 的 Shader 引用在编辑器补齐，需持久保存后打包。只有主方向灯、无 cookie、未限制 Light.cullingMask、Mono 全视口单采样请求被接受；设备或依赖缺失清当前激活，不偷偷创建私有 RT 场景。

场景添加 `Ho-RTShadow Receiver`，关联 OB 组，选择整组或勾选该组已有部件。它与 raster CS 声明独立，没有 shadow-map 接收盒限制；通用对象也可登记 OB 后接收。当前按 16-bit SB owner 请求，未请求状态是 invalid，而不是伪造完全可见。Raw RGBA32F 的四通道为 `[V, 最近实际 blocker 距离, status, owner]`：status 1 有效、0 未请求、2 主表面无效、10+native status 为查询错误；全部 miss 的距离为 65504。valid miss 的 V=1，有 blocker 的单样本 V=0。该阶段不将 N·L、阴影强度、灯颜色或 ramp 烘进核心 V。

非零光源角直径在实际方向圆锥内按均匀立体角抽样；1–4096 个同帧样本，固定 seed / pixel / sample，不依赖时域累计或等待稳定。角度很小时用 `2*sin(radius/2)^2` 保留精度。多样本 V 是等权可见比例，距离通道保存这些样本的最小实际 hit 距离，仅作参考 / 调试，**不能拿它和平均 V 直接填 SIGMA IN_PENUMBRA**。SIGMA 需要自己的官方单样本前端信号。真实空间子像素覆盖仍未实现；提高同帧光源采样不等于几何边缘 AA。

RG / compatibility raw 夹具各 52 checks / 19 请求，包含离屏 ShadowsOnly caster、透明洞后的远 blocker、精确距离、Cast Off、接收部件 / 空选区、GB 半分辨率失配、SB / host 缺失、开关、受限 light linking 拒绝、resize 与执行中 Feature.Create。256 spp 各产生 1244 个半影像素、199 个不同灰阶，固定种子重复结果逐分量一致；不是 SIGMA 或最终窄 ramp 的质量验收。主表面 raster/compute 两路径、host 退休两路径、生产 DI/GI 组合及 DI native Cutout 共 8 项通过，349 个原 DI/GI/CharacterShadow 文件的规范化字节不变。

资源成本为每 renderer / camera 16 B/像素 raw 和 8 KiB 接收位表，另有 shadow 用途 AS。输出按相机和 renderer 隔离；旧输出在 resize / Create / Dispose 时经 host 的实际完成 fence 延迟释放，包含宿主独立退休 pump。公共输入补齐所有 native raygeneration 已声明纹理槽的 fallback，缺少 SB 时安全返回 invalid。没有性能提升或整帧显存预算声明。

**NPR 接收仍未通过，不能关闭 R4。** 真实 lilToon 的 shadow / receive-off / lit 测量均停在 0.001041412，已有 raster-only `ValidateRendering` 在当前隔离环境也失败为 0/0；这只是当前对照，未证明迁移前就存在此问题。隔离诊断曾读到异常材质参数，原因尚不明确，不以猜测修生产材质或常量布局。错误日志、断言和异常进程退出均保留；诊断 Shader 仅写入独立工程且已清除。主光替换 hook 已撤回，原 CharacterShadow 采样算法保持；新增 `HoRTShadowSampling.hlsl` 是尚待接入验收的候选消费者。完整 `-Case shadow-rg` / `shadow-compat` 与 `raster-fallback` 仍是诊断 gate，不属于上述 raw 通过。此点反复调材质已无充分收益，因此停止局部试错，先做能独立验证的 SIGMA 前后端、再回到真实 NPR 接收；R4 / R5 / R6 与最终质量均保持未完成。

<a id="quality-reference"></a>

## 9. 合并后的质量研究结论

正式主线为 **公共 RT 查询 → 逐灯 RTShadow → SIGMA 解码后的连续 V → NPR ramp**。零角度硬影检查几何正确性，有限光源检查半影重建，最终空间采样检查像素覆盖；三个阶段分别验收。

### 9.1 不再重复调查的结论

- 当前角色 CS 的已检查实际资源是 4096² D32、单 mip、未启用动态缩放，不能用 graph 结束后的 4×4 全局默认纹理反推实际资源被降采样。
- PCF 重建比较后的可见度，PCSS 用单深度图及 blocker 近似半影；扩大滤波核不能恢复已丢失的几何。紧投影和提高 map 密度仍有价值，但不是任意近景的精度保证。
- 本地 RTDI `singleDelta` 已有单灯 / 无环境时跳过 Temporal、Spatial 和 RELAX 的路径，清晰硬影不必来自跨帧累计。它目前没有方向光太阳圆盘积分，不能当成已验收的软影参考。
- 固定表面点的零角度硬影是二值；面积光的方向积分提供半影渐变，空间积分处理像素覆盖。SIGMA 的重建输入必须遵守其真实编码和 guide 规则。
- 最终 toon 是非线性操作。原始 V 与 ramp 后颜色都要保存对照；最终空间 AA 需在各子像素重新确定表面并执行材质，不能把中心 mask 放大冒充。
- 准确 RT 仍会暴露低模几何与平滑法线之间的 shadow terminator，也需要稳健的射线起点与 alpha / 双面语义。三角块不能仅凭形状统一归因。

### 9.2 候选技术的保留结论

| 路线 | 本项目结论 |
| --- | --- |
| 紧投影 shadow map + 当前滤波 | 保留作栅格化基线及兼容后端 |
| 多张抖动 map / 更高分辨率 + 最终空间采样 | 可用于无 RT 对照，仍要验证光空间细节与固定成本 |
| Moment / EVSM | 预滤波与宽软影有优势，仍有分布近似，不作为精确硬影上限 |
| Virtual Shadow Maps / SMRT | 世界级密度管理成本较高，不为少数角色优先搬整套分页系统 |
| Alias-Free / Sample-Based Visibility | 非 RT 也可改变采样结构来求精确遮挡，但需专门数据结构与光栅流程 |
| Deep Shadow Maps | 密集毛发、部分透射或体积需求出现后再评估 |
| 独立 RT visibility + SIGMA | 当前选定路线；保留单帧无历史与 raw 对照 |

### 9.3 必须覆盖的质量与导出场景

1. 细发片、亚像素间隙、alpha 洞、双面、Cast Off / Shadows Only 与离屏 caster。
2. 掠射面、近共面遮挡、低模曲面、镜像 / 非均匀缩放及骨骼 / BlendShape 当前姿态。
3. 原始 V、SIGMA 编码 / 解码值、窄 ramp、多层接收和最终颜色分别检查；包括 banding、漏光、轮廓位置、局部误差与细影覆盖。
4. 固定时间的冷启动单帧、随机帧顺序、相机切换及微动；无历史导出不依赖前一个输出帧。其他 GI、TAA、反射或模拟的历史另行处理。
5. 需要空间采样时冻结姿态和 shader 时间，共享该输出时刻的几何快照，完成所有子样本后推进时间。
6. CPU 蒙皮、AS build / update、射线、降噪、额外着色和完整导出成本分别测量；不以 batch 墙钟时间冒充 GPU 时间，也不预报尚未测得的毫秒数。

### 9.4 一手研究来源

- [PCF 与 shadow map 放大误差](https://developer.nvidia.com/gpugems/gpugems/part-ii-lighting-and-shadows/chapter-11-shadow-map-antialiasing)、[PCSS 原文](https://developer.download.nvidia.com/whitepapers/2008/PCSS_Integration.pdf)。
- [同帧高质量空间超采样](https://developer.nvidia.com/gpugems/gpugems2/part-iii-high-quality-rendering/chapter-21-high-quality-antialiased-rasterization)、[PBRT 方向光](https://www.pbr-book.org/4ed/Light_Sources/Distant_Lights)。
- [Alias-Free Shadow Maps](https://users.aalto.fi/~ailat1/publications/aila2004egsr_paper.pdf)、[Sample-Based Visibility](https://publications.graphics.tudelft.nl/papers/616)。
- [Moment Shadow Mapping 作者资料](https://momentsingraphics.de/GDCEurope2016.html)、[Deep Shadow Maps](https://graphics.stanford.edu/papers/deepshadows/)。
- [VSM 与 shadow terminator](https://dev.epicgames.com/documentation/en-us/unreal-engine/virtual-shadow-maps-in-unreal-engine)、[稳健射线自相交处理](https://developer.nvidia.com/blog/solving-self-intersection-artifacts-in-directx-raytracing/)。

旧研究、计划与阶段过程已从现行文件中移除；需要逐项追溯时从 Git `541d06f2` 取回，不按其中的旧“下一项”恢复任务。

<a id="current-helpers"></a>

## 10. 当前已实现的 RT Helper 契约

本节描述底层 helper 契约。HoRTRendererFeature 管理场景 view 和材质 adapter，生产 DI / GI 已改为借用；两者仍拥有自己的输出与历史。宿主不是全局 AS 单例，组合与 Player 的具体资格以 §8.5 为准。

<a id="scene-query"></a>

### 10.1 世界登记、身份与姿态

[HoRTScene](../Runtime/RT/HoRTScene.cs) 使用 Unity 原生 AS，条件为 D3D12 + `SystemInfo.supportsInlineRayTracing`；不支持时 `IsAvailable=false`，不创建 AS / metadata，没有软件 BVH 或屏幕空间回退。

`Synchronize(renderers, mask, excludedMaterials)` 接收与观察相机无关的世界集合；后两个参数可省略。每个 Renderer / submesh 单独登记，要求一材质对应一 submesh、三角形拓扑；支持 MeshRenderer 与 SkinnedMeshRenderer。disabled、inactive、forceRenderingOff、static batch、additional vertex stream、缺失材质等进入拒绝或排除路径。不要传相机 culling results，否则会丢失离屏遮挡物。

默认只登记 opaque；调用者传入共同的 `HoRTCutoutSupport` 和 native hit material 后，DI / GI 均可登记有界 lilToon Cutout。要求 UV0、无 MPB，排除动画主 UV、dissolve、distance fade、dither、多主色、视差等复杂情况。AS 当前关闭 triangle culling，alpha 与表面求值由 adapter 决定；任意 GPU 顶点位移、布料、Terrain、fur、描边壳不在自动支持域。显式登记的几何不会因 `shadowCastingMode=Off` 自动移除，未来 Shadow 的查询策略须单独定义。支持范围见 [DI Cutout](RTDI/技术/Ho-RTDI-Cutout.md) 和 [GI](Ho-RTGI.md)。

蒙皮通过 `BakeMesh(mesh, true)` 获取当前骨骼 / BlendShape 姿态，补偿 Renderer 缩放后由 AS 应用一次 localToWorld。姿态、源 Mesh 或蒙皮质量变化时更新快照及 BLAS，稳定姿态复用；缺失 / 非有限姿态会移除旧实例。源 Mesh 原地编辑必须调用 `MarkGeometryDirty()` 再同步；仅清 TLAS 后重新加入同一个 Mesh 不能保证旧 BLAS 刷新。CPU Bake 与完整 Mesh 副本仍有成本，兼容 pose 已由 host 共享；[场景支持](RTDI/技术/Ho-RTDI-SceneSupport.md) 中的双副本账本属于历史时点，不能用于推算现状。

R2 的 [HoRTGeometryCache](../Runtime/RT/HoRTGeometryCache.cs) 可由宿主注入 `HoRTScene` 第三个构造参数：宿主按本次 union 准备一次，scene views 只 acquire 对应 pose lease。每个 AS entry 比较单调 PoseVersion，所有相关 submesh 均更新；不用被首个 view 消耗的 changed 标记。未注入时 scene 自建缓存，既有独立查询验证保持可用。共享 Mesh 保持稳定，修改前由宿主确认 GPU fence；AS 移除 / 释放后才释放 pose lease。`SkinnedBakeCount` 在共享模式反映缓存实际累计 Bake，而非每个 view 重复计数。

身份契约：

- RT instance ID 从 1 分配，同一 `Lifetime` 内不复用；0 保留。变换、mask、仅更新 metadata 的材质替换可保留身份；移除重加、Mesh / hit material / cutout 分类变化或显式几何刷新会重新登记。
- Material ID 是场景服务内身份，不是 Unity InstanceID、OB ID 或材质参数索引。GPU metadata 为 `uint4(submesh, materialId, primitiveCount, valid)`；`TryResolve` 提供 CPU 关联。
- `Lifetime` 区分服务重建，`Revision` 标记当前输入变化，`StructureRevision` 另标结构 / 材质编辑。历史不能只比较新服务中可能重新从 1 开始的 ID。

### 10.2 场景调度与命中

同步和几何刷新在 Unity 主线程、graph recording 之前完成。RenderGraph 消费者使用非 async unsafe pass，通过 `scene.Read(graph, builder)` 声明 metadata 访问，再于执行回调中 `BuildAndBind(cmd, shader, ...)`。AS 没有原生 RenderGraph 句柄，build、上传和 trace 需要同一 graphics queue 的明确顺序；不能只依赖全局绑定。其他资源依赖由消费者另行声明。兼容路径为 `ReadCompatibility().BuildAndBind(...)`。

输入快照捕获 Revision，record 后修改场景会在执行时拒绝；执行结束前不能 Dispose 或偷偷更换映射。同一服务内仅 dirty 时记录 Build，`BuildCount` 是命令计数而非 GPU 计时；Dispose 可重复调用并释放自有资源。

| 查询路径 | 当前返回与边界 |
| --- | --- |
| 公共 inline `HoRTSceneQuery.hlsl` | opaque 最近命中，跳过 non-opaque；含距离、位置、实例 / submesh / 材质 / primitive 身份、重心坐标和 frontFace，不含 UV、法线或 radiance |
| DI native final visibility | 对支持的 Cutout 执行真实 alpha any-hit，查询所选灯样本的遮挡 |
| GI native secondary material | 从实际三角形读取 UV0～3、几何 / 插值 / normal-map 法线及支持域内材质组分、emission |
| GI native visibility adapter | 与 GI 相同的 alpha 规则，适配结果只保证 status / distance，不提供公共 inline 的 metadata 身份字段 |

公共 inline 调用 `HoRTTraceScene(originWS, directionWS, tMin, tMax, mask)`；需要 `#pragma require inlineraytracing`。方向被归一化，距离均以世界单位计。status 为 `0=miss`、`1=hit`、`2=metadata错配`、`3=无效射线`、`4=不可用`；失败 payload 清零，不能按 miss 消费。世界 miss 的天空响应由光照消费者负责。

<a id="primary-surface"></a>

### 10.3 主表面输入与有效域

[HoRTPrimarySurfaceInputs](../Runtime/RT/HoRTPrimarySurfaceInputs.cs) 与 [HLSL 读取](../Runtime/RT/Shaders/HoRTPrimarySurface.hlsl) 直接借用 GB / SB，不分配第二套 GBuffer，不写相机颜色，也不通过 AC。当前属性为：

| 数据 | 来源与含义 |
| --- | --- |
| positionWS / eyeDepth | SB raw depth + 当前实际逆 GPU VP；对应源像素中心，支持标准透视 / 正交、reversed-Z |
| geometricNormalWS | SB Normal.BA oct 解码的插值网格法线，无 normal map；不是 flat face normal 或 AS 命中法线 |
| shadingNormalWS | SB Normal.RG oct 解码，包含 normal map |
| baseColor / emission | SB 的原始线性 HDR 主色 / 独立自发光组分；主色不自动当物理反射率 |
| roughness / metallic / reflectance | SB Material.RG / Reflection.R；读取层不平方粗糙度或换算 F0 |
| viewDirectionWS / owner | 当前相机观察方向；按需请求 SB 的 16-bit owner，0 身份不使表面失效 |

Material.A 是 `meshNormalAvailable`，旧 / 自定义 writer 未提供时必须为 0 并拒绝读取。GB 的 lilToon 法线实际也经过 normal map / Tesion，不能代替 SB 的明确网格法线组分。DI / GI demand 会请求 SB Normal 高精度附件，优先 RGBA16 UNorm、其次 RGBA16F；无需求时保留紧凑格式，成本与回退见 [SB](Ho-SurfaceBuffer.md)。DI / GI 当前各自的主表面历史为 20 B/像素，两种法线按 2×16-bit oct 一致保存；不是公共共享历史。

在 GB / SB 生产之后调用 `HoRTPrimarySurfaceInputs.Read(graph, frame, builder, includeOwner)`，raster 使用 `Bind(cmd)`，compute 显式 `Bind(cmd, shader, kernel)`。兼容路径为 `BindCompatibility` / `BindCompatibilityCompute`，读取本相机生产记录，不用 CPU `Shader.GetGlobalTexture` 推测 GPU 绑定。Compute 每个已声明槽都要绑定资源；缺失时绑定已有默认资源并用状态拒绝，不能读取未请求的 owner。

HLSL 使用 `HoRTTryLoadPrimarySurface(pixel, surface)`；`AtUV` 包装会定位源像素并吸附中心，不双线性混合两个表面。先通过 GB / SB 深度关联，只有 status 5 成功；0～6 沿用关联层，7 为不支持的相机 / 投影，8 为属性或网格法线标记缺失，9 为非有限数据 / 无效法线。失败只保留 status，其余 payload 清零。

当前支持 Mono、Texture2D、同分辨率单采样域、标准透视 / 正交，可有 lens shift 和本帧 jitter；不支持 oblique / 非标准深度、XR / array，MSAA resolve 与异分辨率无法确认，硬件动态分辨率尚未验收。快照只用于当前相机当前 graph，不跨帧缓存 TextureHandle，也不提供 motion / temporal。未来 RTShadow 的屏幕输入必须匹配相机、分辨率、jitter 与表面；中心吸附不能替代真正的子像素几何采样。

### 10.4 Helper 验证入口

- [HoRTSceneValidation](../Editor/RT/HoRTSceneValidation.cs)：`Run` / `RunCompatibility` / `RunUnsupported` 覆盖初版 inline 的身份、mask、离屏、重心、双面、更新、生命周期和无效输入；D3D12 两路径及 D3D11 unsupported 已验。其默认 Cutout 排除不代表 native 路径不支持 Cutout。
- [HoRTPrimarySurfaceValidation](../Editor/RT/HoRTPrimarySurfaceValidation.cs)：`Run` / `RunCompatibility` 同时检查 raster / compute、像素中心、投影、两种法线、HDR / emission、可选身份及失效清零；编译警告与错误都拦截。
- 初版隔离日志位于 `.codex-research/RTInputValidation`；蒙皮、native alpha、材质命中及 Player 的专项证据分别见 [DI 场景支持](RTDI/技术/Ho-RTDI-SceneSupport.md)、[DI Cutout](RTDI/技术/Ho-RTDI-Cutout.md)、[GI 总说明](Ho-RTGI.md)。这些通过不代表任意材质变形、复杂角色时域或整帧预算已验收。

<a id="baseline"></a>

## 11. 已接纳基线、必须保留的坑与证据

A01 / A02 / U01 是 2026-10-24 已收尾阶段，必要内容合并在这里；过程全文可从 Git `541d06f2` 找回。A02 当时为控制轻量优化范围而搁置共享 pose / primary 提交，这不是本轮 Ho-RT 底座的永久禁令；旧 U02/U03 和 047 的“下一项”也不是当前自动任务。

| 基线 | 已接纳内容与验证 | 限定 |
| --- | --- | --- |
| A01，`6d7d3dc6` | 中立 HoNrdContext / Native / Timing、公共灯 / Cutout / primary ABI；GI 不再依赖 DI C# 策略。新旧 33 帧共 540672 RGBA 分量逐位同，DI 64 组 native 调度逐字节同，固定实例 DI+GI 两路径及 Release 验证通过 | 代码解耦未减少 GPU 资源，显存收益为 0；DI 调度对照不是独立像素真值 |
| A02，`e73782f5` | GI 的 16F signal 在 NRD 消耗后复用为 filtered 发布，取消单独 filtered 分配。两路径各 1318912 RGBA 分量逐位同，原 GI 回归和 Release 304 请求 / 16 checks 通过 | 默认减少 8 B/像素，64² / 32² 实测省 32768 / 8192 B；1080 的约 15.82 MiB 只是布局推算，不是整帧显存 / FPS 实测 |
| U01，`77c49c0c` | GI Inspector 与文档整理，Runtime / Shader / 序列化合同不变，分程序集编译通过 | 仅展示整理；不等于 GI 完整画质、动态或性能验收 |

必须保持：

- 公共 NRD 不内置 DenseDI 策略；调用者提供自己的 profile。DI / GI 的 permanent 历史、信号单位、lobe、reset 与更新时间不能混用。未来共享 primary 历史须先读旧、统一帧末提交，不能由先执行的 Feature 覆写。
- A02 的 signal 在 Resolve 后已是发布单位；Editor 若要读旧 NRD 输入，须预先请求 `ValidationCaptureSignal` 并读取 `ValidationSignal`。启用快照多占 8 B/像素，当帧没有这项节省。raw / NRD32F / permanent 不与其混用；将来开启原生 NRD validation 要重审 IN_DIFF / OUT_VALIDATION 别名。
- 共享姿态需核 source Mesh、骨骼 / BlendShape 版本、同帧多请求、bounds、引用计数和 GPU 生命周期；共享代码、共享 pose、共享 AS 的收益分别证明。
- F39：动态增加 AS 实例时曾有 D3D12 scratch debug 错误，即使进程退出 0 也拒绝验收；固定初始实例夹具通过不能代替增长路径修复。旧静态非零 base 支持、GI filtered 局部 / 全域质量、正常应用帧物体 motion、复杂动态、半 GI 薄边、稳定 1080 整帧预算及完整平台矩阵仍未完整解决。
- 不为让旧 SHA guard 通过而重写 run / source-index / 门槛。A02 合法改变过 A01 源码，U01 又改变两个 Editor 文件，旧整包 SHA 拒绝不能冒称当前回归失败或改历史让它通过。保留原失败，新增变更使用匹配当前源码的验证。
- Shader ABI 字符串、原插件字节 / GUID 与受测参数不能因搬目录或改类型名一起替换。UI 的 InspectorName 可能覆盖 GUIContent，字段扫描通过不等于实际标签已验。

现行组合使用仍遵守各模块合同：同分辨率单采样 GB / SB 先生产，需要天空时启用 Atmosphere；DI 的 Feature 总开关与 Volume 启用覆盖均需生效，GI Volume 则可回退 Feature。DI / GI 同用时避免重复加入主表面直接天空；加法合成保留原照明，参考 PBR 替换按 DI 合同工作，尚未形成完整 lilToon NPR 接收。

证据保留在测试目录，未因本次压缩改写：

- [RenderingArchitecture](../Tests~/RenderingArchitecture/README.md)：A01 ABI、逐位对照、独立历史、固定实例组合与 Release 证据。
- [RenderingReuse](../Tests~/RenderingReuse/README.md)：A02 signal 生命周期、显式快照、资源账本、双路径逐位对照及 Release。
- [StageClosure U01](../Tests~/RenderingUI/StageClosure-20261024/README.md)：文档 / Inspector 字段复核、编译和与 A02 的源差异。
- [RTGI Backbuffer](../Tests~/RTGI/Backbuffer/README.md)：正常自动相机及 512²默认屏幕输出的受限验收；更多未通过项与专项入口看 [GI](Ho-RTGI.md) / [DI](Ho-RTDI.md)。
