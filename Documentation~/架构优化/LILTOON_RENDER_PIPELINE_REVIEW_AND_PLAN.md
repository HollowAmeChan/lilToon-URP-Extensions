> **已过时（R6/R7）**：本文写作时 MetadataBuffer 还在。它已在 R6（摘槽）／R7（消费者换源 + 整块删除）中删掉：`maskId` 与自定义通道归 OB + AC，surface 族归 SB。当前架构以 `Documentation~/架构优化/Ho-*.md` 与 `CHANGELOG.md` 为准。

# 渲染管线架构评审与规划（lilToon / HoNpr / URP / 渲染环境）

> 状态：Draft v0.3（本地盘点 ✅ + 外部调研 ✅（URP17 源码级 + AOV/Nuke 工业规范），NPR 光照域以本地 HDRP 对照 + 业界为参照）
>
> ⚠ **前提已过时**：本文写于 MetadataBuffer 仍承担"材质/对象语义"的时期，它的 buffer 划分（MetadataBuffer + GeometryBuffer）**不再成立**。当前划分见 **[`Ho-管线总览.md`](Ho-管线总览.md)**：**GB（几何）/ ObjectBuffer（逐物体）/ SurfaceBuffer（表面）三轴 + Ho-AttributeComposite（属性合成与遮罩）**。**继续有效**：功能域盘点、AOV 方案、§7 业界对照、§2 的"边界与耦合判定"方法论；**凡涉及 MetadataBuffer 的段落一律按 v2 读**（其槽位变成 OB / SB 的通道，`custom0~3` 那类匿名通道由 CM 的具名遮罩取代）。
> 用途：回答“我们现在是什么、工业界怎么组织、URP 的边界在哪、功能该放哪层、怎么为未来拆解留接口、怎么接 Nuke 多通道”。
> 背景：**渲染环境**（作品集 / 动画渲染），非性能敏感；主材质 `lilToon`（当前）；`HoNpr`（重生成式材质系统，**已因过重暂停**，只作反例）；管线 URP17 / RenderGraph 主线。

---

## 0. 结论速览（TL;DR）

1. **现状是“中间态”**：扩展侧已有很好的组件分层（`lilToon-URP-Extensions` 收口版），材质侧有完整 toon 功能面 + 材质契约。缺的是：**把现有材质 + 管线扩展对齐成一套“轻边界”，并给出 shadow / AO / GI / 反射 / 透射 / SSS / 多光 七个功能域的统一方案**。`HoNpr` 的重生成式架构已暂停，其教训（见 §2.1）是本评审的“边界第一课”。
2. **边界问题最直接的答案来自你们自己的教训**：`HoNpr`（语义 DSL + FeatureBlock + Generator + Preset 的重生成式材质系统）**已经因“太重、扩展/调试困难”而暂停开发**。它是一次有价值的反例：证明了“结构越重，越难改、越难查、越难往前走”。本评审的态度：**采用它便宜的思考原则（语义优先、Feature Ownership、显式关系、材质语义优先于固定 AOV），坚决不采用它的重结构**。系统边界不是“更牛的框架”，而是“足够轻、可调试、可逐点失效回退”。
3. **关键架构判断（历史）**：本文曾把平面反射归入统一 fullscreen“意图模式”；该判断已被后续 PLR PBR 实现取代。反射以 `ReflectionPipelineDesign.md` 为准，SSS/角色特化仍可使用语义 Buffer + composite。
4. **七个功能域的推荐层**：见 §3。总原则：**静态/风格化的放材质或预计算；屏幕空间的走“语义 Buffer → ScreenProcess 施加”；需要跨帧/跨模块复用的走通道注册表（文档级契约，非运行时框架）；AOV 输出独立成层**。
5. **明确的“不做清单”**（不要把 URP 推成游戏引擎，也不要再造一个重框架）：deferred 全链路、完整 PBR GBR 重建、compute 级完整 SSGI（除非自研立项）、厚度/玻璃体积吸收、多 bounce 实时 GI、每光完整 GI、**以及任何“为将来可能需求”而上的 DSL/生成器/运行时注册框架（HoNpr 已因此暂停）**。

---

## 1. 生态盘点

### 1.1 仓库地图（D:\Unity_Fork）

| 仓库 | 角色 |
| --- | --- |
| `lilToon` | **当前主材质**：角色 toon 主线（生成式 shader 架构 + 材质契约 + 屏幕空间接收端） |
| `lilPBR` | 场景/半写实 PBR 线（packed PBR、SSR、clear coat、SSS、wetness） |
| `lilToon-URP-Extensions` | **管线扩展层**：ShadowCast/MetadataBuffer/GeometryBuffer/SSS/OIT/CharacterSpecialization/ScreenProcess/ImageProcess/DebugTile（已完成 RPComponentRework 收口） |
| `HoNpr` | **已暂停的反例**：下一代语义材质系统（DSL preset/FeatureBlock/Template/Generator/ShaderLibrary）。架构太重，扩展/调试困难，**项目已暂停**；本评审只取其思考原则、不取其结构 |
| `HoUrp-Extensions` | 早期 URP 扩展参考（DebugTile 路线来源） |
| `HoUrp17.3.0` / `HoUrpConfig17.0.3` | URP17 源码 / 配置（对齐对象） |
| `UnityGraphics-6000.3-HDRP` | HDRP 17.3 源码（SSS/DiffusionProfile 等对照） |
| `Unity-ScreenSpaceReflections-URP` | 现成 URP SSR 包（Linear + Hi-Z tracing）——**反射域可直接评估接入** |
| `jp.lilxyzw.liltoon-2.3.4` | 上游 lilToon 2.3.4（差异参考） |
| `MagicaCloth2` / `renderdoc-mcp` / `HoShader` / `HoUnityTools` 等 | 外围（布料 / 调试抓帧 / 工具） |

### 1.2 扩展侧已收敛的组件边界（以 RPComponentRework 收口文档为准）

```text
1. Ho-ShadowCast            额外投影光源收集 + atlas（Lighting/Shadow；不写 Buffer）
2. Ho-MetadataBuffer        材质/对象/mask/surface metadata（maskId/surfaceData/custom0/objectCustom0-1/SurfaceColor/MBufferDepth）
3. Ho-GeometryBuffer        屏幕几何 normal/depth
4. Ho-SubsurfaceScattering  屏幕空间 SSS（profile 驱动、Burley-like、transmission 辅助）
5. Ho-WeightedOIT           加权透明
6. Ho-CharacterSpecialization 眼透/前发/主体轮廓
7. Ho-ScreenProcess         语义屏幕效果（读 Buffer，按 layer 聚合依赖）
8. Ho-ImageProcess          最终图像链（只读 camera color/ImageChain）
9. Ho-DebugTile             自动 debug tile（registry 编排）
```

铁律（已写在收口文档）：Buffer=语义输入；ScreenProcess=语义效果；ImageProcess=图像；ShadowCast=光源；DebugTile=编排；跨模块走注册表；新增 channel 先给命名/编码/消费者/debug；无消费者不输出。

### 1.3 主材质侧现状

- lilToon：一/二/三层阴影（含 strength/border/blur mask、receive mask）、rim、backlight、fake SSS、MatCap、反射（metallic/smoothness/reflectance/cube override）、outline、refraction/gem、fur、glitter；`_ScreenSpaceAOSource`（“AO RT”选择器）接收 URP/HTrace AO；`_HTraceSSGIBackfaceNormalFix` 供 UniversalGBuffer/DepthNormals 修正背面法线。
- 材质语义 pass：`HoMetadataBuffer`（maskId/surfaceData/custom0/objectCustom0-1/SurfaceColor/MBufferDepth）+ `HoGeometryBuffer` + `HoCharacterCapture`（eyeColor/eyeData）。
- 材质契约（`接口契约.md`）：Blender Principled/OpenPBR → glTF `HO_materials_principled_lil` → lilToon/lilPBR，含 toon/unity/extras 子层；`unity.screenSpaceAO.*` 提示已在契约里占位。

### 1.4 已知问题（评审基线）

- SSGI 与 toon 描边语义冲突：描边壳只存在于相机颜色/深度、不存在于 SSGI 法线/层输入 → 发亮白边（**已知 bug**，`LILTOON_KNOWN_ISSUE_OUTLINE_SSGI_GLOW.md` 已记录；原因：HTrace 偏 PBR、不理解非物理表面）。
- 屏幕空间 AO 目前是“采样模式”，且 HTrace 参数多、偏 PBR；材质内 Main Color / Shadow Color Blend 等 toon 化方向没有稳定验收。
- 反射/透射/多光没有统一的“域方案”。
- AOV 未成体系（数据散在 MetadataBuffer/CharacterSpecialization 内部）。

---

## 2. 方法论：边界与耦合的判定（教训提炼 + 本评审）

### 2.1 从 HoNpr 提炼的教训（先立牌：什么是“太重”）

`HoNpr`（DSL preset + FeatureBlock + Template + Generator 的重生成式材质系统）**已被项目暂停**——理由是架构太重、扩展和调试太困难。它证明了：**抽象层级每多一层，调试/扩展的成本就多一档；结构一旦靠近“框架”，就会反过来束缚功能**。

我们保留它的**思考原则**（这些是便宜的、不增加层数的）：

- **语义优先**：先想“这个功能在管线里生产什么语义、参与哪些 pass”，再定参数与 UI——只是思考顺序，不需要一套 DSL。
- **Feature Ownsary**：一个功能应能被完整找到（实现/参数/依赖/消费者/debug），不要散落全文——只是组织习惯，不需要运行时注册表强制。
- **显式关系替代潜规则**：关键关系写清楚（谁写、谁读、什么编码、什么时候失效），不靠命名/keyword/pass 顺序暗含——只是文档 + 命名约定。
- **材质语义优先于固定 AOV**：先问“这个结果要不要被导出”，再决定是否占用通道——只是思考顺序。
- **少开关，多层级**：会改变 pass/资源依赖的能力做成“另一种材质类型/预设文件”，不做成 UI 开关——不增加运行时结构。

我们**坚决拒绝**它的：

- DSL / FeatureBlock / Generator / 派生表那套“生成式结构”；
- “Generated shader 只保留可读装配”带来的**间接层**；
- 为结构正确性引入的校验、审查、事实来源档案等**仪式性基础设施**。

### 2.2 重量测试（新功能准入门槛，本评审新增）

任何新设计/模块必须同时满足以下四条，否则先简化再动手：

1. **调试测试**：5 分钟内能用 Frame Debugger / RenderDoc 找到它，看到它的输入输出；debug view 是**直接加一个 shader 分支或一个 tile**，不需要走注册/生成流程。
2. **扩展成本测试**：新增一个同类功能，改动量是 **1-3 个文件**（一个 RendererFeature + 一个 pass/shader + 一条通道登记），而不是“改 DSL + 改生成器 + 改派生表 + 改注册表”。
3. **无框架测试**：表达这个设计不需要为自己发明新语法/新文件类型/新运行时框架；契约用**文档 + 命名 + 简单 C# 枚举/结构体**即可。
4. **回退测试**：关掉它时，效果是“这个功能没了”，而不是“整条链路不可用/编译不过”。

> 结论：**lilToon 的生成式结构（lilinternal/lilblock → 生成 shader）是已验证可用的“轻生成”，保留；URP-Extensions 的“一个功能一个 RendererFeature + 契约文档”是已验证可用的“轻扩展”，保留。别再叠加任何一层抽象。**

### 2.3 本评审的关键发现：两种集成模式（要统一）

| 模式 | 例子 | 机制 | 优点 | 缺点 |
| --- | --- | --- | --- | --- |
| **A 采样模式** | SSAO | 管线在 forward 前生成全局纹理，材质直接采样并自行混合 | 简单、材质完全可控、立即见效 | 材质知道纹理名（中度耦合）；AOV 里已混合难分离；`After Opaque` 不可用 |
| **B 意图模式** | SSS / 角色特化 | 材质把“想要什么+强度+遮罩”写进语义 Buffer；fullscreen composite 统一施加 | 材质与来源解耦；天然支持 AOV | 链路多一步；需要通道契约 |

**结论（回答“要不要耦合”）**：
- 允许**单向**依赖：管线 → 材质（全局纹理 + 材质开关）只在“低成本、立即见效、不与 AOV 冲突”时用（如 debug、快速开关）。
- **主力约定 = 按功能域选择消费模式**。SSS/角色特化继续走“材质写语义 → composite 施加”，AO 保留采样模式；反射按 `ReflectionPipelineDesign.md` 走 source → 材质响应/专用 resolve，不再归入统一意图模式。
- 好处：① 材质不再知道 AO/GI/反射来自哪个 RT、哪个算法；② AOV 层能拿到“施加前场景状态 + 施加后结果”；③ 未来替换 HTrace（自研 AO/GI）对材质零改动。

### 2.4 URP 官方扩展点（源码级确认，对齐 URP 17 / Unity 6）

**一句话边界：渲染循环属于 URP（不可插队、不可重排），但“画面效果”可以是你的。**

- **官方扩展栈**（从宽到窄）：`RenderPipelineAsset`（语义上可继承，但无官方“部分改造 URP”路线）→ `UniversalRenderer`/`UniversalRendererData`（**sealed = 硬边界**，只能换全新渲染器或改源码）→ **`ScriptableRendererFeature`（官方主扩展点）** → `ScriptableRenderPass`（`renderPassEvent` + `RecordRenderGraph`）→ `AddRasterRenderPass<T>`/`AddUnsafePass<T>`/`AddComputePass<T>`。
- **注入时机**：`RenderPassEvent` 枚举（BeforeRendering=0 … AfterRendering=1000），支持 `+offset` 微调；`OnBeginRenderGraphFrame()` 是最后一个可安全 `EnqueuePass` 的点。
- **RenderGraph 时代的关键语义**：执行顺序由**资源依赖调度**（`UseTexture(depth, Read)` 会强制调度器先产出深度），**不要假设自己排在某个内置 pass 之后，而要声明依赖**；带外部副作用的 pass（写全局纹理/keyword/相机状态）必须 `AllowGlobalStateModification(true)` + `AllowPassCulling(false)`。
- **读相机输入**：`UniversalResourceData`（public）暴露 `cameraColor/activeColorTexture/cameraOpaqueTexture/cameraDepth/cameraNormalsTexture/renderingLayersTexture/motionVectorColor/ssaoTexture/gBuffer/…`；`ConfigureInput(Depth|Normal|Color|Motion)` 声明式请求；shader 侧 `SampleSceneDepth/SampleSceneNormals/SampleSceneColor`。
- **官方“结果喂材质”模式**：`SetGlobalTextureAfterPass` 发布全局纹理（`_ScreenSpaceOcclusionTexture` 等）+ 材质采样函数（`SampleScreenSpaceOcclusion`）——这正是“采样模式”的出处；**它是可行模式，但对本管线不是最优**（见 §2.3）。
- **低成本 AOV/对象级效果原型**：内置 `FullScreenPassRendererFeature`（挂 Material + pass + injection point）与 `RenderObjects`（ShaderTagId 列表 + override material + 事件时机）——AOV 导出/筛选对象的最小成本路径。
- **URP 不提供 / 属于外部边界的**：完整 AOV 系统、toon 语义、metadata、SSGI 槽位（`UniversalResourceData.irradianceTexture` 是 **internal**，第三方 feature 拿不到——这也是 HTrace SSGI 只能作为独立后处理硬接、且无法被材质直接感知的原因之一）。这些只能靠**文档级契约 + 命名约定 + 轻实现**自己守边界。
- **官方文档参考**：[ScriptableRendererFeature 17.0](https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@17.0//api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html)、[RenderGraph 全屏 blur 最小示例（URP17）](https://discussions.unity.com/t/urp-17-rendergraph-api-blur-multi-pass-fullscreen-blur-shader/1576178)、[URP Frame Data textures reference](https://docs.unity3d.com/6000.1/Documentation/Manual/urp/frame-data-textures-reference.html)、[HDRP AOVs（Custom pass variables）](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3//manual/AOVs.html)、[HDRP AOVRequestData](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@13.0/api/UnityEngine.Rendering.HighDefinition.AOVRequestData.html)。

---

## 3. 七个功能域方案（本评审核心）

> 每条：现状 → 问题 → 推荐 → 层 → 耦合 → AOV 影响。

### 3.1 Shadow（阴影）

- 现状：URP 主光阴影 + `Ho-ShadowCast` 附加灯 atlas；材质侧一/二/三层阴影 + ramp + 强度/border/blur mask + receive mask。
- 问题：① 附加灯与材质 toon 阴影的“统一阴影语义”未定（材质只知道 main light 阴影）；② 多光源下材质怎么合并（目前每光各自贡献）。
- 推荐：**阴影 = 材质内 toon 门控（ramp/遮罩）+ 管线内实时光照信息（ShadowCast 输出“多光源衰减”语义进 Buffer，ScreenProcess 无需；材质按需采样一次“阴影因子”全局纹理）**。阴影因子（如 `_ShadowAtlas` 衰减 / main light shadow）作为**新注册通道**（写方 ShadowCast，消费者 材质+ScreenProcess）。
- 层：材质（门控/ramp）+ ShadowCast（收集/生成）+ 通道（衰减因子）。
- 耦合：单向；材质只读“已注册的阴影语义”，不读 ShadowCast 内部 RT。
- AOV：`shadow`（投影阴影）、`shadowmask`（柔和过渡）可分别导出；材质门控后结果进 beauty，门控前可导出 shadow/ramp 系数。

### 3.2 AO

- 现状：采样模式（URP `_ScreenSpaceOcclusionTexture` / HTrace `_HTraceBufferAO`，材质内 remap/contrast/mask/染色 + MainColor/ShadowColor Blend）。
- 问题：与意图模式不一致；HTrace 偏 PBR；颗粒/糊。
- 推荐：**迁移到意图模式**——① 材质写 AO 意图（`aoStrength/mask/applyMode/color` 已有一半）进语义通道；② AO 生产端（现 HTrace，后续自制 toon AO）输出 `_lilScreenSpaceOcclusionTexture` 类 AO 因子；③ ScreenProcess 的 AO 层在 opaque 后按“意图 × AO 因子”统一施加，并保留“材质内直采”旧路径兼容。自制 toon AO 的算法档（小半径 contact、bilateral、蓝噪声、可选 temporal）照 SSAO 总览的备用条线，作为**后续独立 RendererFeature 立项**（复用现有 `_UseScreenSpaceAO` 参数集）。
- 层：生产 = 管线（HTrace/自制）；意图 = 材质（写通道）；施加 = ScreenProcess；风格化 = 通道内 remap。
- 耦合：单向；材质不知道算法。
- AOV：`ao`（AO 因子）、`aointent`（材质意图）、`beauty`（施加后）——天然可分离。

### 3.3 GI（间接光）

- 现状：HTrace SSGI（全屏注入；描边发亮已知 bug）；材质侧仅背面法线修正；无材质内 GI 接收。
- 问题：PBR 语义与 toon 冲突；材质不可控。
- 推荐（分两层）：
  - **静态语义**：预烘焙（lightmap/APV/SH）作为主要“环境间接”来源，通过 HoRP 契约暴露“环境光照/ambient 通道”；材质内 toon 阴影/indirect 继续吃现有 SH/indirect 路径。
  - **动态语义**：屏幕空间 GI 作为**增强**，产出“gi 因子/gi 颜色”进注册通道；材质按 intent（`giStrength/mask`）消费；**描边壳必须被显式排除**（自研 SSGI 时把“描边/非物理表面”作为 receiver/caster 排除位）。
  - **何时自研**：仅当 HTrace 在 toon 上无法调出稳定风格且有明确帧内需求时立项；不追求 PBR 正确性，追求“可控的风格化间接”。
- 层：生产 = 管线（烘焙/屏幕空间）；意图 = 材质；施加 = ScreenProcess；排除位 = 通道语义。
- 耦合：单向；**未来换掉 HTrace 对材质零影响**（这就是“别让材质碰生成逻辑”的实证）。
- AOV：`gi`（间接颜色/因子）、`beauty_directOnly`（直接光）、`beauty`（合成后）。

### 3.4 反射

反射方案、输入槽位、时序和实施路线统一以 [`ReflectionPipelineDesign.md`](../ReflectionPipelineDesign.md) 为准。本文不再重复维护 PLR/SSR 的旧“意图模式 + fullscreen composite”设计。

### 3.5 透射 / 折射

- 现状：lilToon ref/gem 变体（camera color 折射）；transmission 仅 reserved；无真玻璃管线。
- 问题：camera color 读取属于“材质私自读未声明资源”（HoNpr 一类的设计已判定这类越界，其教训保留：**材质读外部资源必须声明**；`Character_LilToon_Refraction` 的前置条件即“camera color / transparent input 契约明确”）。
- 推荐：先立 **HoRP camera color / transparent 资源契约**（材质读 camera color 必须声明）；折射做成 preset（`Character_LilToon_Refraction`），透射语义（厚度/吸收/菲涅尔）进通道；屏幕空间折射复用 OIT 之后的 opaque copy。不做体积吸收/厚介质（过拟合）。
- 层：preset（结构）+ 契约（资源声明）+ 管线（camera color 管理）。
- 耦合：通过契约，不靠命名。
- AOV：`refraction`（折射分量）、`transmission`（透射分量）。

### 3.6 SSS

- 现状：`Ho-SubsurfaceScattering`（意图模式）：材质写 surfaceData（thickness/curvature/profile/transmittance），HoSSS 读 source/diffusion/composite；离 HDRP 17.3 差距 = Burley 本体 + SSSBuffer/backface thickness + 干净 diffuse source。
- 推荐：按 `HoAOVTrueSSSDesign.md` 的 HDRP 对齐线路走：① profile 数据结构补齐（可在插件内做成资产或 Volume，不必照搬 DiffusionProfile 资产）；② diffusion kernel 用 Burley disk（已迈出一步）；③ backface thickness prepass（前端/后端 depth）；④ source 尽量用**干净的 diffuse lighting / irradiance**（不是 camera color）；⑤ 材质内 fake SSS 保留为低配。
- 层：意图 = 材质（surfaceData）；数据 = MetadataBuffer；扩散 = ScreenProcess/SSS pass；profile = 管线资源。
- 耦合：单向。
- AOV：`sss`（散射分量）、`sssintent`（mask）、`diffuseLight`（干净源）。

### 3.7 多光源

- 现状：URP additional lights（cluster/forward+）+ ShadowCast；材质侧只有 main light toon 化。
- 推荐：**光源合并语义**——管线把“有效光照”（main + ShadowCast 附加灯）合并成**有限的通道语义**（如 `mainLightDir/color`、`addLight0..N`），材质只按“toon 门控规则”消费；或按风格化目标直接烘成“光照明 LUT/ramp 场”。不做每光完整 GI。
- 层：光源收集 = ShadowCast + URP；合并 = 管线；消费 = 材质（门控）。
- 耦合：单向。
- AOV：`light0..N`（每光贡献，可选）或 `literally` 合并后的 `diffuse`/`specular` 分离。

---

## 4. Nuke 多通道（AOV）方案

### 4.1 设计原则（教训提炼）

- **语义流优先**：通道 = “材质/管线计算的语义是否需要被导出”，不是固定 RT。
- **Pass declares Need / Material declares Supports-Produces / Resolver 绑定**：AOV 输出层只按“当前帧需要导出的语义集合”去消费 Buffer/ScreenProcess 结果，不永久绑定槽位。

### 4.2 推荐通道集（for toon 角色渲染环境，最小可用）

| 通道名 | 内容 | 消费方 | 现成来源 |
| --- | --- | --- | --- |
| `beauty` | 最终画面 | Nuke 主合并 | camera color |
| `beauty.diffuse` / `beauty.specular`（可选） | 材质分解 | 分层调色 | 材质语义（MaterialSpecular/…需新增通道） |
| `diffuse.albedo`（surface color）| 基础色 | 阴影/IBL 调色 | MetadataBuffer.SurfaceColor ✅ |
| `shadow` / `shadowmask` | 投影/柔和过渡 | 阴影调色 | ShadowCast + 材质门控 |
| `ao` / `aointent` | AO 因子/意图 | AO 调色 | AO 通道（新） |
| `gi` / `giintensity` | 间接光 | GI 调色 | GI 通道（新） |
| `reflection` / `reflectionintent` | 反射 | 反射调色 | 反射通道（新） |
| `sss` / `sssintent` | SSS | 皮肤/透射调色 | HoSSS ✅ |
| `sss.profile` | profile id | mask/选择 | MetadataBuffer.surfaceData.b ✅ |
| `transmission` / `refraction`（可选） | 透射/折射 | 玻璃/头发 | 契约预留 |
| `normal` / `position` / `depth` / `motion` | 几何 | 重投影/深度模糊 | GeometryBuffer ✅（normal/depth），motion 待加 |
| `id.object` / `id.group` | 对象/组 ID | 抠像/通道选区 | MetadataBuffer.maskId/mask ✅ |
| `id.material`（objectCustom bits） | 材质/部件位 | 部件选区 | MetadataBuffer.objectCustom0-1 ✅ |
| `matte.face` / `matte.hair` / `matte.eye` | 部件 matte | 局部合成 | CharacterCapture/MetadataBuffer bits ✅ |

- 推荐最小起步：`beauty + surfaceColor + normal + depth + id.object/id.group + shadow + ao + gi + sss + matte.*`——现有 Buffer 已覆盖一大半，新增成本主要在“导出层 + 通道登记表”（见 §5）。
- **命名与格式（工业事实标准）**：
  - 容器：**OpenEXR**，单 part 多通道（multichannel EXR，通道名 `layer.channel` 点分）是 AOV 最常见形态；multipart/deep 2.0 扩展对 AOV 通常不必要。
  - Nuke 模型：**layer = 通道名前缀**，每层最多 4 通道；保留通道名如 `depth.Z`、`forward.u/v`、`mask.a`；主图用 `+rgba`。
  - 命名流派：Arnold 全小写下划线（`diffuse_direct`、`shadow_matte`）；Redshift/V-Ray 用 raw×filter 配对（`RawDiffuse`×`DiffuseFilter`）；HDRP 用 Pascal 语义名；ID 类事实标准是 **cryptomatte**。
  - 对本管线（Nuke 后期、低成本）建议：**Arnold 风格全小写下划线 + 点分层**（`beauty`、`diffuse_albedo`、`shadow`、`ao`、`gi`、`sss`、`reflection`、`id_object`、`id_material`、`matte_face`…），并把“是否导出”登记在通道表。
- **最低成本原型**：用 URP 内置 `FullScreenPassRendererFeature`（挂一个输出指定通道的 Material + pass）或 `RenderObjects`（ShaderTagId 筛选 + override material）先跑通 2-3 个通道，再外包“多通道 EXR 组装”成编辑器/批处理工具（避免一上来写导出框架）。
- **导出时机**：作为**独立输出层**（比 ImageProcess 更后的“导出 pass”或 Editor/Batch 专用相机二次通道渲染），不干扰实时画面；渲染动画用“帧渲染 + AOV 导出”动作。

### 4.3 建议：AOV 输出层 = 第五层

```text
语义（Buffer）→ 语义效果（ScreenProcess）→ 图像（ImageProcess）→ 输出（AOV/导出）→ 调试（DebugTile）
```

- AOV 层只**消费**声明过的通道语义（写方=各模块、注册表=全局清单），不参与合成。
- 每个通道 = `{ name, producer, consumer, encoding, mask? }`；由 DebugTile/AOV tile 自动展示。

---

## 5. 架构兼容点（为未来拆解留的接口）

1. **通道登记表（channel contract，文档级，非运行时框架）**：升级 MetadataBuffer 的语义清单为一份**契约文档/表格**（命名/编码/写方/消费者/debug/AOV 导出字段）。跨模块数据先登记。**只做约定与命名，不做运行时注册/生成器/自动绑定。**
   - **机制先行、清单随需求长**：登记/命名/编码/生命周期/debug/导出位的“机制”现在定（一天工作量）；具体通道清单**不着急规划**——新功能落地时按“一行表格”登记即可（无消费者不默认输出，与收口文档一致）。
   - **不要“预留剩 RT”**：URP17 RenderGraph 是 transient 声明、按需创建、用完即弃——需要什么语义就声明什么，不需要预分配槽位（比“留空 RT”更省）。
   - **AOV 命名冻结**：一旦某个通道被 AOV 导出（进 Nuke），它的命名与编码就冻结，后续只能新增、不能改（避免破坏已生产的通道资产）。
2. **模块契约（RendererFeature 级）**：每个 feature 声明 `需要/产出/缺失降级/AOV 通道`——对齐收口文档；用 Feature 的 Inspector 只读状态 + 文档呈现，不引入注册框架。
3. **意图模式约定**：仅对 SSS/角色特化等确需 after-opaque 合成的效果使用“材质写意图 → composite 施加”；反射按独立 PBR source/response 契约执行。
4. **材质契约层（已有）**：`principled/openpbr/toon/unity/extras` 为外部 DCC 数据；`unity.screenSpaceAO.*` 等 hint 已是“后处理参数进材质”的合法载体（不走耦合，走契约）。
5. **资源契约（契约即文档）**：材质读 camera color/网格资源必须声明（一个文档级清单 + 命名约定即可，不必做代码级 contract 系统）；是 Refraction/GI 等的前置条件。
6. **材质类型轻收敛**：功能结构收敛为“材质类型/变体”（沿用 lilToon 现有 shader 家族或新增预设文件）；新能力若改 pass/资源依赖则做新类型，不做 UI 开关。**不引入 DSL / 生成器 / 预置体系**。
7. **目录/命名规范**：模块名 `Ho-XXX`（已定）；通道名小写 `.` 层级分组（如 `beauty.sss`）；shader 资源 `Hidden/...` 命名（已定）。
8. **AOV 导出清单**：通道清单 → 导出通道配置，一处声明，Nuke 就绪（文档 + 简单编辑器工具即可）。

---

## 6. 路线图（P0 / P1 / P2 + 不做清单）

### P0（先定架构，低成本高确定性）

1. 通道登记表 v1（一份文档：把 MetadataBuffer/GeometryBuffer/SSS/ShadowCast 的现存通道登记 + 命名/编码/消费者/AOV 字段）。
2. 按功能域约定消费模式 + 现有模块清单对齐（AO 采样、SSS/角色 composite、反射 PBR source/response）。
3. AOV 导出层 v0：多通道 EXR 导出（先覆盖 `beauty/surfaceColor/normal/depth/id/matte` 现有通道）。
4. 资源契约 v1（一份文档清单：材质读 camera color/transparent 资源必须声明；为 Refraction/GI 打底）。

### P1（把主力域做稳）

1. AO：意图模式迁移（材质写意图 + AO 层施加）；HTrace 调参收敛；自制 toon AO 立项（可复用 `_UseScreenSpaceAO` 参数集）。
2. 阴影：ShadowCast “多光源衰减”通道化；材质 toon 门控统一。
3. 多光源：光源合并语义 + AOV 分光（可选）。
4. SSS：Burley 补完 + backface thickness prepass + 干净 diffuse source（按 `HoAOVTrueSSSDesign.md` 后续）。
5. 反射：按 `ReflectionPipelineDesign.md` 的 PLR → SSR → Probe/Sky 路线推进。

### P2（按需立项，先证明需求）

1. 自研 SSGI（排除描边/非物理表面；风格化间接）——**先写“需求 & 验收”立项文档**，再动工。
2. 透射/折射材质类型（lilToon ref/gem 或独立类型，先在资源契约上打底）。
3. 通道扩展（`beauty.diffuse/specular`、`motion`、`id.material` 类）。
4. （若未来恢复 HoNpr 一类系统：再做“材质类型 ↔ 通道登记表”的互相引用，避免两套语义；**在其暂停期不推动**。）

### 明确不做（过拟合清单）

- deferred 全链路 / 完整 GBR 重建（Forward + 语义 Buffer 足够）。
- compute 级完整 SSS（除非皮肤质量需求明确且性能可接受）。
- 体积吸收、厚介质、多 bounce 实时 GI、每光完整 GI。
- 全局 AOV mask 回退（已废弃）；RenderFeature 理解材质内部结构（已禁止）；keyword 表达材质 feature（已禁止）。
- 把 HTrace 算法名/参数暴露进材质面板（已有原则，继续）。
- **重生成式/框架化基础设施**：DSL、FeatureBlock/Generator、运行时通道注册框架、“优雅”的自动派生表——HoNpr 已因此暂停；任何“为将来可能需求”而上的抽象（YAGNI）都进这个清单。

---

## 7. 业界对照速记与参考

### 7.1 业界做法速记（toon + 屏幕空间 + 后期）

| 领域 | 业界参考 | 对我们的启示 |
| --- | --- | --- |
| Toon 阴影 | UTS3（三/多层阴影 + ramp 贴图 + blur/AA；阴影遮罩类材质）、MToon（VRM，轻量 ramp 单层） | 我们的“一/二/三层阴影 + ramp/mask”已是标准做法；不需要更复杂的阴影系统 |
| AO | URP SSAO（depth/normal + bilateral blur，After-Forward 前）、EEVEE GTAO（screen-space，简单）、烘焙 AO | 屏幕空间 GTAO 对 toon 的颗粒/糊是通病；**toon 角色更该“小半径 + 接触暗部 + 材质内 remap”**；值得自制一个“toon AO”但别做算法库 |
| GI | HDRP PathTracer/APV（对质量导向友好）、UE5 Lumen（动态全屏）、SSGI（PBR 假设） | 我们不是游戏，**“烘焙/APV 作静态主源 + 屏幕空间作风格化增强”**比依赖纯 SSGI 更稳；SSGI 只做增强并排除描边/非物理表面 |
| SSS | HDRP DiffusionProfile + SSSBuffer + Burley（`RenderaPipeline.SubsurfaceScattering`，已拉源码） | 本地 `HoAOVTrueSSSDesign.md` 的 HDRP 对齐线路正确：**profile + Burley + 干净 diffuse source + backface thickness**；别学它的“compute 全量”起步 |
| 反射 | 现成 `Unity-ScreenSpaceReflections-URP`（Linear/Hi-Z）、HDRP SSR（half-res + temporal + probe fallback）、planar（镜面） | 反射来源可插拔（cube/planar/SSR）；**收尾选 SSE 时优先现成包 + fallback** |
| 多光 | URP additional lights（cluster/forward+）、UE 多光；toon 一般只对主光做完整风格化 | 多光合并语义 + 材质 toon 门控；不做每光 GI |
| AOV | Arnold `diffuse_direct` 命名、Redshift/V-Ray raw×filter、HDRP Decoupled AOV、cryptomatte、OpenEXR layer.channel | 采用“Arnold 小写 + 点分”命名；通道表登记；`FullScreenPassRendererFeature`/`RenderObjects` 做低成本原型 |

### 7.2 主要参考链接

- Unity URP 17：`ScriptableRendererFeature` https://docs.unity.cn/Packages/com.unity.render-pipelines.universal@17.0//api/UnityEngine.Rendering.Universal.ScriptableRendererFeature.html ；URP17 RenderGraph 全屏效果最小示例 https://discussions.unity.com/t/urp-17-rendergraph-api-blur-multi-pass-fullscreen-blur-shader/1576178 ；Frame Data 纹理参考 https://docs.unity3d.com/6000.1/Documentation/Manual/urp/frame-data-textures-reference.html
- Unity HDRP：AOV（Custom pass variables）https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.3//manual/AOVs.html ；`AOVRequestData` https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@13.0/api/UnityEngine.Rendering.HighDefinition.AOVRequestData.html
- 本地源码：`D:\Unity_Fork\HoUrp17.3.0`（URP17 对齐）、`D:\Unity_Fork\UnityGraphics-6000.3-HDRP`（SSS/DiffusionProfile 对照）、`D:\Unity_Fork\Unity-ScreenSpaceReflections-URP`（SSR）、`D:\Unity_Fork\HoNpr`（反例）
- 材质契约：`接口契约.md`（Blender Principled/OpenPBR → glTF → lilToon/lilPBR）；Blender Principled https://docs.blender.org/manual/en/latest/render/shader_nodes/shader/principled.html 、OpenPBR https://academysoftwarefoundation.github.io/OpenPBR/
