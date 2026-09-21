# Changelog

## Unreleased

- **逐物体阴影（CS）：「阴影软边」从 PCSS 分组挪到「运行」**（用户指出：关掉 PCSS 它照样起作用，那就不该放在 PCSS 里）：
  - 字段本身没变（`softnessRadius`，米），只是**换了分组与名字**：原来叫「最低软度」待在「软阴影（PCSS）」里，
    现在叫**「阴影软边（米）」**，放在 Volume 与 feature 的**「运行」**分节（紧跟「单角色分辨率」）——
    它是**始终生效**的基础滤波（盖几何锯齿），PCSS 是在它之上再按遮挡距离加半影；PCSS 分组里只留 PCSS 专属的六项。
  - 两处 HelpBox 相应改写（说明"始终生效"、单位是米、以及"关掉 PCSS 只剩这一档"），volume/feature 的 summary 摘要
    也带上了它。
  - 文档：Ho-UI_风格规范 §6 CS 行、Ho-CharacterShadow-Plan §0.3/§0.5 同步。

- **逐物体阴影（CS）：把"这一帧到底用了哪组软阴影参数"做成可见**（用户报"PCSS 永远启用"）：
  - LastCullStatus 追加一段 `pcss=on/off,min=…m,max=…m,soft=…,<质量档>,vol=n`：**vol=n 是被 Volume 覆盖掉的
    字段个数**。运行状态一节直接能看到本帧实际生效的开关与参数 —— "关了没反应 / 改了没反应"时先读这一行：
    若 feature 上关了而这里是 `pcss=on`（或 vol>0），说明 Volume 里的覆盖赢了（Volume 面板 Add Override 会把该组件
    所有字段都设成覆盖态，包括「启用 PCSS」），去 Volume 改或把该字段的覆盖勾掉。
  - feature 与 Volume 的「软阴影（PCSS）」两处 HelpBox 写明这条优先级，并说明**关闭 PCSS 后仍会保留「最低软度」
    那档抗锯齿滤波，想完全硬边就把最低软度设为 0**。
  - 回归：ValidatePcss 增加断言"关闭 PCSS 必须真的到达渲染器"（`LastCullStatus` 含 `pcss=off`）；
    实测 `pcss=off,min=0.005m,max=0.04m,soft=2,Ultra,vol=0`，开关通路正常。

- **逐物体阴影（CS）：修掉"PCF 锯齿依旧 + PCSS 一片噪声黑点"**（用户实测截图反馈）：
  - **根因（一个）**：软阴影半径原本用 **texel** 当单位，而 PTP 的 tile 是 4096、盒子只有 1~2 m ⇒
    **1 texel ≈ 0.6mm**。于是：半径 1 texel 的 3×3 PCF 等于没滤波（几何锯齿原样保留），
    而为了看得见把 PCSS 半径拉到 12 texel 时，24 个采样铺在 12 texel 的盘上只有 1 个/25 像素²，
    立刻变成颗粒；再加上旋转角按 atlas texel 取（相邻像素共用一套图案）⇒ 噪声表现成 texel 大小的方块。
  - **半径全部改成世界单位（米）**：`softnessRadius`（最低软度，默认 5mm）、`pcssBlockerSearchRadius`（默认 2cm）、
    `pcssMaxPenumbraRadius`（默认 4cm）；shader 用 C# 发布的 `_HoCSParameters[slice].w`（该 slice 的 1 texel = 多少世界单位）
    换算成 texel。换分辨率不用重调，也不会再出现"4096 上 12 texel 只有 7mm"这种隐性硬边。
  - **采样预算收窄**：换算后的 texel 半径按 `sqrt(sampleCount * 6.25)` 收（64 采样 ≈ 20 texel 上限），
    防止细 tile 上出现稀疏大盘；想更软又不出噪点的做法写进 UI 提示（降「单角色分辨率」到 1024/2048 或提高质量档）。
  - **采样图案换掉**：固定 3×3 网格 → 旋转盘（黄金角螺旋）；旋转角按**世界位置**取（逐像素去相关，不再出方块）；
    blocker 盘里没找到遮挡物但中心被遮挡时按"半影最大"处理（早期在这里退回硬 PCF，硬/软像素混杂也是斑点来源）；
    给「最低软度」加了下限（PCSS 永远不会比 PCF 更硬）；采样数不再在关闭时归零（那会把 PCF 退化成单点采样、
    边缘变成抖动噪声边）；blocker 深度偏移默认给 0.0005（压相邻像素间 blocker 数量跳变）。
  - 采样上限提到 **32/64**，档位 8/16、16/32、24/48、32/64，默认档 **Ultra**（CS 只作用在角色附近的屏幕区域）。
  - **回归**（`ValidatePcss`，D3D11 + D3D12 都跑）：新增**斑点指标**（4 邻里有 ≥3 个反相的孤立像素）与
    "用户现场那一档"用例（4096 tile + 默认参数）：
    `pcf width=2px speckles=0 | pcss width=22px core=0.000 lit=1.000 speckles=0 | softness0 width=2px | 4096-tile width=6px speckles=0`。
  - 文档：`Ho-ShadowCast-PCSS.md` §6 补"半径用世界单位/采样预算/采样图案"三节与量错过三次的坑。

- **逐物体阴影（CS）：补上 PCSS 软阴影**（用户反馈"PCF 边缘锯齿感比较严重"）：
  - 图集采样从"固定半径 3×3 PCF"升级成 PCSS：blocker search → 用平均遮挡深度估半影 → 按半影做可变半径滤波。
    形状/质量参数与 `Ho-ShadowCast` 那套同构：`pcssEnabled` / `pcssQuality`(`Low`·`Medium`·`High`·`Ultra`) /
    `pcssSoftness` / `pcssBlockerSearchRadius` / `pcssMaxPenumbraRadius` / `pcssDepthBias`，发布
    `_HoCSPcssParams` = `(enabled, softness, blockerRadius, maxPenumbraRadius)`、
    `_HoCSPcssParams2` = `(depthBias, blockerSamples, filterSamples, 0)`。
  - **降级即回退**：关闭 / 半影放大 0 / 采样数 0 / 搜索盘里没有 blocker → 回退原来那条 PCF（半径仍是 feature 的「PCF 半径」），不是另一套 shader。
  - **半影公式用物理形式**（`(接收距离 - 遮挡距离) / 遮挡距离`，reversed-Z 下用 `1 - z`）：ShadowCast 那边除的是接收深度，接收深度接近 0 时半影会被放大到把阴影核心糊亮（实测漏光到 0.376）；换公式后同参数核心保持 0.000。
  - **参数按 UI 规范分两处**：新「软阴影（PCSS）」分节，Volume 是逐相机真值、feature 是兜底（`HoCharacterShadowRenderConfig.Resolve()` 每相机解析一次，`Build`/帧数据只读解析结果）。
  - **采样上限防漂移**：`HoCharacterShadowShaderContract` 镜像 HLSL 的 `HO_CS_MAX_PCSS_BLOCKER_SAMPLES = 16` / `HO_CS_MAX_PCSS_FILTER_SAMPLES = 32`，`Validate()` 解析 HLSL 比对（batch 里也跑）。
  - **新回归 `HoCharacterShadowValidation.ValidatePcss`**（D3D11 + D3D12）：标准 PCSS 摆法（接收面正对光源、投影物悬在光源与接收面之间），量 10%–90% 边宽（沿图像梯度方向）：
    `pcf width=11px core=0.000 lit=1.000 | pcss width=59px core=0.000 lit=1.000 | softness0 width=11px` —— 边缘明显变宽、
    核心不漏光、外侧无光环、softness 0 精确回到 PCF。
  - 文档：`Ho-ShadowCast-PCSS.md` 新增 §6（CS 的那一套 + 公式差异 + 量错过两次的坑）；`Ho-CharacterShadow-Plan.md` §0.4 与 §0.3 UI 表同步。

- **逐物体阴影（CS）：隐藏光/隐藏剔除相机的销毁改成延迟执行**（补上一条的收尾）：
  - `Object.Destroy` 在编辑模式下非法（`Destroy may not be called from edit mode!`），而 `CoreUtils.Destroy`
    在编辑器里走 `DestroyImmediate`，`Dispose()` 又会被 `Create()` 从渲染 / Inspector 回调里调到
    （`Destroying GameObjects immediately is not permitted during rendering callbacks`）。三条路都不能直接用。
  - 新增 `DestroyTemporary()`：播放模式用 `Object.Destroy`，**编辑模式把 `DestroyImmediate` 推迟到
    `EditorApplication.delayCall`**（那时已经出了回调）。对象是 `HideAndDontSave`，晚一帧销毁无副作用。
  - `ValidateSceneShadows` 增加一段生命周期检查：模拟 `SerializedObject.ApplyModifiedProperties → feature.Create()`
    两次，断言三类消息一条都不出现、且中间一次渲染 CS 仍然工作。

- **逐物体阴影（CS）：开 CS 后场景其他物体丢普通投影 + 远处阴影消失，一次修掉**（承接上一条的修法）：
  - **根因**：Unity 的 shadow renderer list 是**按光源一帧一份**提交的，和传入的 `CullingResults` 无关。
    CS 原先借场景主光做局部剔除/绘制，于是和 URP 的相机阴影图抢同一份状态：
    我们早于 URP 阴影阶段建列表 → **相机阴影图变成我们的局部盒**（其他物体全丢普通投影，实测
    `without CS=0.008 / with CS=0.803`）；放到 URP 之后 → **我们的图集整块为空**（`atlas=0.000`）。
    上一轮把 pass 提前只是把第二种表现换成了第一种。
  - **修法**：CS 用自己的**隐藏方向光**（`HideFlags.HideAndDontSave`，方向/剔除层跟随主光，`color` 黑 +
    强度 0.001，因此不参与场景光照、不占相机灯光名额、争不到主光）。该灯只在
    `beginCameraRendering` 之后到本 pass 记录期间开着（该回调早于 URP 的 `context.Cull`），
    相机永远看不到它；每个接收域占这盏灯自己的一个 split 索引。**不再与主光共享任何阴影状态。**
  - **新增回归测试 `HoCharacterShadowValidation.ValidateSceneShadows`**：URP/Lit 地面 + 场景 caster
    （再用 lilToon caster 跑一遍），CS 开/关两测断言普通投影都在、场景亮度不变、日志里没有 SRV 跳过。
    修复后 `lit=0.803, URP shadow=0.008/0.008 (CS off/on)`，D3D11 与 D3D12 都 PASS；
    `ValidateRendering` / `ValidateDistanceRendering` 一并 PASS。
  - 被排除的假设（含"只跳过 `CullShadowCasters` 仍丢阴影"/"只跳过列表创建则正常"这类关键对照）
    记在 `Documentation~/计划/Ho-CharacterShadow-Plan.md` §0.1。

- **OB 身份表 SRV 在 D3D12 上被跳过（`_HoObjectBufferEntries`）**：
  - **现象**：编辑器启动时 `d3d12: Fragment Shader "lilToon" requires a buffer (SRV) "_HoObjectBufferEntries" ...
    Skipping draw calls to avoid crashing.`（D3D11 只是静默读到 0，语义位全 0）。
  - **修法两层**：① `HoObjectBufferRegistry.Release()` 释放后立刻改绑占位缓冲（三个 1 行 buffer + 计数 0，
    语义等于"没建表"），并在 `[InitializeOnLoadMethod]` 与 `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`
    各绑一次 —— **任何时刻都不允许 SRV 为空**；② `HoSurfaceBufferSemanticPass.Setup` 显式
    `HoObjectBufferRegistry.EnsureBuilt()`：语义 lane 的 shader 读 OB 表，不能假设 OB feature 已经跑过。
  - 详见 `Documentation~/计划/Ho-CharacterShadow-Plan.md` §0.2（含"为什么两层都要"）。

- **UI 风格整理（续）：OB / AC 对齐规范，并修正规范里过期的调试视图清单**：
  - `HoObjectBufferRendererFeature` 的「调试」分节不再画第二份开关（`debugMode` / 两个视图开关仍在 settings 里作兜底，
    Volume 覆盖时以 Volume 为准），只留一行 HelpBox 指到 Volume 并列出真实视图名。
  - `HoAttributeCompositeRendererFeature` 面板重排为 **运行（兜底）/ 声明（只读汇总）/ 调试（一行 → Volume）/ 高级**，
    全部走 `LilUrpEditorSectionGui` + 规范色板；`HoAttributeCompositeVolumeEditor` 同样加 运行 / 调试 分节。纯 UI，
    不改运行时行为（AC 的 debug 字段本来就是 `[NonSerialized]` 运行时载体）。
  - **修正 `Ho-UI_风格规范` §6 里过期的调试视图清单**（OB / SB / AC）：原清单含未实现的视图（OB 的
    Facing / 溢出 / 未声明 ID / owner 对齐参考、SB 的 SurfaceOwner 与单独的 owner mismatch、AC 的合成属性图 /
    object·surface sample）。现按各自 DebugMode 枚举逐条核对改写，并标明"还没实现"的部分
    （OB resolve 已不留 dropped 计数，真需要时按 OB 架构 §5.11 再做）。
  - §6 顶部加**落地状态**说明：AC / CS 是现行；OB 的 feature 实际分节比表里多「覆盖率（自建 MSAA）」「Selection（R1 兼容层）」
    两节，组表/条目表在 `HoObjectBufferGroup` 组件上；**SB 的 feature 目前是 Unity 默认面板**，表里 SB 的 feature 部分仍是目标形态。
    OB 那两行过期的内容（"朝向图开关"、feature 上的组表）按代码改写。

- **逐物体阴影（CS）：UI 按 `Ho-UI_风格规范` 重排，调试入口移进 Volume**：
  - 新增 `HoCharacterShadowVolume`（`Post-processing/Ho-CharacterShadow/逐物体阴影`）。运行 = 启用 + 单角色分辨率；
    调试 = 调试模式（`Off` / `Atlas` / `Character`）+ `Debug In Scene View` / `Debug In Game View` + 单角色 tile。
    启用 / 分辨率 / 调试因此变成**逐相机可覆盖**，未勾选覆盖时用 feature 的兜底值（`Resolve()` +
    `overrideState` 判定，不写回 feature 资产）。
  - `HoCharacterShadowRendererFeature` Inspector 重排为 **运行（兜底默认值）/ 声明（只读汇总）/ 调试（一行 → Volume）/
    高级（时机·Shader）/ 运行状态**，全部走 `LilUrpEditorSectionGui.DrawSectionHeader` + 规范色板，中文标签 + 英文原名。
    「声明」列出场景里每个 `HoCharacterShadow` 组件 → OB 组 / tile / 盒尺寸 / 状态、图集容量与已分配 tile；
    「高级」把渲染时机做成只读行（固定 `BeforeRenderingShadows`）并写明"放到更晚会重新丢远处阴影"；
    「运行状态」读 `LastCullStatus`。feature 上**不再画第二份调试开关**（避免两份真值，字段仍作兜底保留）。
  - `HoCharacterShadow` 组件 Inspector 重排为 **运行 / 运行状态**（状态、接收部件匹配数、图集 Tile、投影深度、
    世界单位每 texel），字段加中文 `[InspectorName]` / `[Tooltip]`。
  - 调试画面改为**按视图开关输出**（Scene View 默认开、Game View 默认关）：它是直出替换最终画面，默认不该动 Game View。
    调试分组**没有强度曲线**（CS 直出光空间线性深度，加曲线会让人把亮度误读成深度），这是对规范「调试固定内容」的有意偏离。
  - 验证：`ValidateRendering` 增加 Volume 覆盖子测试——Volume 关启用 → 回退普通投影 `1.000`；Volume 单角色分辨率 `R512`
    → `_HoCSAtlasSize.z = 512`。两个 batch 入口（`ValidateRendering` / `ValidateDistanceRendering`）全 PASS。
  - 文档：`Documentation~/Ho-UI_风格规范.md` §6 增加 CS 分节与两条偏离说明；
    `Documentation~/计划/Ho-CharacterShadow-Plan.md` §0.2 增加三处 UI 布局表与使用步骤更新。

- **逐物体阴影（CS）：修掉“相机拉远后角色整体阴影消失”**（用户场景实测 bug）：
  - **根因**：局部图集 pass 的时机（`BeforeRenderingPrePasses`）与 URP 自己的逐相机级联 shadow pass 撞在同一事件，
    Unity 内部“该光源 + 当前相机”的 shadow 状态会否决我们的自定义 split。URP Asset 的
    **`m_ShadowCascadeCount > 1`** 且观察相机不在自己的级联 0 里时，atlas tile 整块为空 → CS 静默回退到
    已按 `shadowDistance` 淡出的普通主光阴影，看起来就是“离远了阴影全没了”。
  - **修法**：pass 时机提前到 `RenderPassEvent.BeforeRenderingShadows`，在 URP 渲染本相机级联阴影**之前**
    构建局部图集。**不再需要把级联数改成 1**，PTP 场景保持 4 即可。
  - **回归测试**：新增 `HoCharacterShadowValidation.ValidateDistanceRendering`（级联数 {1,4} × 距离 240→8 m
    扫描 lilToon 探针可见度与 atlas tile 深度）。修复前 `casc4 60m..8m` = `vis=1.000 atlas=0.000`（FAIL），
    修复后两档级联全距离 `vis=0.000 atlas=0.984`（PASS）；`ValidateRendering` 一并 PASS。
  - 排查过程中同时保留了三处安全性修正（持久 RTHandle 图集、剔除参数完全来自光空间正交相机、
    `Allocator.Temp` 数组不再提前 Dispose）；被排除的假设记在
    `Documentation~/计划/Ho-CharacterShadow-Plan.md` §0.1，避免重复调查。

- **文档分布整理（撤销 `架构优化/`，现行移到外层、过程记录进 `归档/`）**：46 篇（45 篇 + 新增 `文档索引.md`）重新分布，`Documentation~/` 现在是"外层 = 定型功能/架构 + 少量主题文件夹"：
  - **外层 19**：索引、总览与契约（`Ho-管线总览.md` / `Ho-ChannelContract-v1.md` / `Ho-RenderFeatureOrdering.md` / `Ho-UI_风格规范.md`）、四轴（`Ho-GeometryBuffer.md` / `Ho-ObjectBuffer.md` / `Ho-SurfaceBuffer.md` / `Ho-AttributeComposite.md`）、屏幕效果（`Ho-GTAO.md` / `Ho-SSGI.md` / `Ho-ShadowCast-PCSS.md` / `Ho-已知问题-描边SSGI白边.md`）、光照探针接入说明、`OIT.md` / `TransparentPass.md` / `PlanarReflection.md` / `ReflectionPipelineDesign.md` / `MaterialGradient.md`。
  - **主题文件夹**：`角色特化/`（3）、`后处理/`（6，原 `PostProcessing/` 改名，`Images/` 随之迁移）、`架构边界/`（2）、`计划/`（2）。
  - **`归档/`（14）**：旧管线草案 v0.1、CB 规划、旧评审稿、HoAOV/HoSSS 设计记录、RPComponentRework 验收、描边语义调查、GTAO MSAA 记录、GTAO/SSGI 对齐工作表、ReSTIR 共享层报告、HTrace GTAO 参数档案、GradientInvestigation、DepthOfField 记录、CS 浏览器重构记录——保留作资料与"坑"的来源，不再作为现行依据。
  - **改名对齐内容**：`GeometryBuffer.md` → `Ho-GeometryBuffer.md`；`LILTOON_CHANNEL_CONTRACT_V1.md` → `Ho-ChannelContract-v1.md`；`LILTOON_RENDER_FEATURE_ORDERING.md` → `Ho-RenderFeatureOrdering.md`；`LILTOON_GTAO_PLAN.md` → `Ho-GTAO.md`；`LILTOON_GI_PLAN.md` → `Ho-SSGI.md`；`LILTOON_SHADOW_PCSS_PLACEHOLDER.md` → `Ho-ShadowCast-PCSS.md`；`LILTOON_KNOWN_ISSUE_OUTLINE_SSGI_GLOW.md` → `Ho-已知问题-描边SSGI白边.md`。
  - **引用修复**：全部相对/绝对 md 链接、正文路径提及、以及 6 处 extensions 代码注释里的 `Documentation~/PostProcessing/*.md` 同步更新；新增 `Documentation~/文档索引.md` 作为唯一入口（分类 + 一句话 + 归档理由）。
  - **核查**：46 篇 H1 各一个、链接 0 悬空、旧目录/旧文件名提及 0 残留、无编码损坏；三个检查器全通过。


- **文档全量审核（`Documentation~/` 45 篇，只动文档、不改代码）**：按「架构形式 + 设计目的 + 踩过的坑」重审每一篇，把声明与**当前 GB/OB/SB producer + AC compositor** 实现逐条对齐（grep 核对代码事实），分 17 次提交（批 1 → 批 8d，extensions 仓）。要点：
  - **改名/合并/归档**：三篇地基文档改名 `Ho-ObjectBuffer.md` / `Ho-SurfaceBuffer.md` / `Ho-AttributeComposite.md`；两份管线草案合并成 `Ho-管线总览.md`，v0.1 归档；`Ho-CharacterBuffer_规划.md` 606 → 121 行（保留 K=N 容量分析、MSAA 官方规则、非线性 AA 禁令、业界依据与迁移落点）。
  - **调查/工作表压缩成「结论 + 坑」**：描边语义调查、GTAO MSAA 轮廓白线、HTrace GTAO 参数档案、GTAO/SSGI 对齐工作表、SSGI ReSTIR 共享层评估、RPComponentRework 验收、CS 的 RDG 逐趟清单（495 → 130 行）。
  - **修正失实陈述（按代码/资产核对）**：PCSS 实为**已实现且默认开**（原文写“暂不做”）；`Ho-管线总览` 的 Feature 清单补齐 OB/SB/AC（12 → 14 项）；**通道契约 v1 的“生产端”整列从 MetadataBuffer 改为 OB/SB/AC**（`surfaceColor`→SB `Color` 且 A 不再是 coverage、`maskId`→OB 身份池、`surfaceData`→SB `Classification`+`Material.b`、`reflectionMaterial`→SB `Material`/`Reflection`；`maskcoverage.*` 段作废，AC lane 覆盖率原生带 AA）；CS 的 `SemanticMask Blur` 那一趟与整套“读取抗锯齿掩码”开关**已不存在**（语义位由 `HoCharacterObjectSemantic.shader` 从 AC Selection 池按覆盖率烤出）；反射文档的 P0 输入迁移标记完成；脸色扩散/眼透/SSS 设计文档的语义来源改按 AC/OB/SB 口径。
  - **工程资产重扫（写进光照/探针说明）**：`PC_Renderer.asset` 现有 4 类已删脚本的 Missing Script 死条目（MB / HoAov / HoPostProcess / Shoost）与 7 组同名重复 feature；Ho-SSGI 与 HTrace AO/SSGI 当前均 `m_Active: 0`；`lilToonSetting.json` 仍是 `LIL_OPTIMIZE_USE_LIGHTMAP=false` / `USE_PROBEVOLUMES=true`；生成 Shader 52 → 54、APV 命中 47 → 49。
  - **结构收尾**：45 篇全部带状态头（现行 / 已收敛 / 已归档 / 占位 / 调查），H1 各恰好一个，相对 md 链接无悬空；`.codex-research/*` 一律标注为不在仓库的本机私有脚本。
  - 新增 `Ho-CharacterShadow-Plan.md`（CS 独立 feature 规划，尚未实现）入库。

- **MetadataBuffer（MB）整块删除**：maskId / 自定义通道由 OB + AC 接管，surface 族（表面色 / 厚度 / 曲率 /
  材质 / 反射 / 分类）由 SB 接管。分两步走，每一步都保持树能跑：
  - **R6-1**：删掉 MB 的 `HoMetadataBufferSurfaceColor` 独立 pass（22 个 lilblock + `fragMetadataBufferSurfaceColor`），
    连带删掉只为它而活的 `MBufferDepth`（含调试视图与 ScreenProcess 的黑色兜底）。
  - **R6-2**：主 pass 摘掉 `SurfaceData` / `ReflectionMaterial` 两个槽，附件 **6 → 4**
    （`0 MaskId / 1 Custom0 / 2 ObjectCustom0 / 3 ObjectCustom1`），调试模式重排为
    `1 Mask / 2 Id / 3 Flags / 4..7 Custom0-3 / 8..15 ObjectCustom0-7 / 16..19 RSUV`
    （枚举、shader 的 `mode == N`、视图登记表三处同步改；旧场景里存的调试号会落到别的视图上，不做映射）。
  - **R7-1**：SSS 的 `HoSSSCoverage` 从 MB 的 `maskId.r` 改成 AC 的 `HoAC_TotalCoverage`（同一个覆盖率）；
    顺带删掉 PLR 里一行没人用的 MB 句柄。
  - **R7-2**：ScreenProcess 的图层遮罩从 MB 覆盖率改成角色覆盖率（AC/OB），并**修好 texel size**：
    以前读全局纹理的 `_TexelSize`（恒为 0），DropShadow 的"按像素扩张 / 羽化"半径一直没生效。
  - **R7-3**：DebugTile 的 MB 格子与视图登记退役；PLR 那一格改用 OB 覆盖率 + SB 的 Material/Reflection，
    并把它的 mode 3/4/5 对齐枚举（原先画的是 wetness / normalStrength，与 `HoPlanarReflectionDebugMode` 对不上）。
  - **R7-4**（跨仓 lilToon）：删 22 个 lilblock 的 `HO_METADATA_BUFFER` pass 与 `_HoMetadataBuffer*` 材质属性 /
    inspector 分节；`lil_pass_metadata_buffer.hlsl` 只剩 GB 的那一半，改名 `lil_pass_geometry_buffer.hlsl`；
    `PropertyBlock` 里 SB（`HoSurface`）直接顶掉 MB 的枚举槽位（后面的块号不变）。
  - **R7-5**：`Runtime/MetadataBuffer/` 与 `Editor/MetadataBuffer/` 整目录删除（含 Clear / Fallback / DebugView
    三个 shader）；`HoFaceAxis` 搬到 `Runtime/ObjectBuffer/`（按 int 序列化，数值未动）。
  - **踩过的坑（记住）**：① MB 的调试枚举重排会改旧场景里的调试显示，不是错误但会"换视图"；
    ② `lil_pass_object_buffer.hlsl` 与 `lil_pass_outline_normal_depth.hlsl` 也在 include 那个文件
    （前者靠调 MB 的 frag 拿 clip 副作用）—— 删 include 前先跑 `check_shaders.ps1`，它正好抓出这两处；
    ③ `HoFaceAxis` 这类"住在待删命名空间里的公共枚举"要先搬再删。
  - **未处理**：`D:\Unity_Fork\lilPBR` 的 MB pass 与 `_HoMetadataBuffer*` 属性仍留着（MB 删掉后那趟永远不会被绘制）；
    资产里 5 个挂着 `HoMetadataBufferGroup` 的旧场景保留 Missing Script。
- 新增 `.codex-research/check_compile_liltoon.ps1`：用同一套 Unity 参考程序集单独编译 `lilToon.Editor`
  （lilToon 侧的 Editor 代码在这里是独立程序集，之前的检查器看不到它）。
- 修复：**SB 的材质 pass 之前根本编译不过** —— `lil_pass_surface_buffer.hlsl` 里指向 `HoSurfaceBufferCommon.hlsl` 的 `#include` 在上一轮改注释时被误删，于是 22 个 lilblock 的 `HO_SURFACE_BUFFER` pass 全部报 `undeclared identifier 'HoSurfaceOwnerEncode'`：pass 不存在 ⇒ SB 画不到像素 ⇒ owner 恒 0（调试里 Owner 全红、五张数值图全是"没人写"色）。已补回 include。
- 新增 `.codex-research/check_shaders.ps1`：**shader 侧的静态闸门**。只查本仓命名空间（`Ho*` / `lilHo*` / `LilHo*`）的调用可达性、先用后定义与 include 解析 —— C# 的 Roslyn 检查器看不到 HLSL，而这类错误的症状恰好是"整屏什么都不输出"，与原因离得很远（本次就是它漏掉的第二例）。已做负向测试（抽掉 include 立刻报出那两个调用）。
- **Ho-SurfaceBuffer（SB）数值面落地**（规划 R3-sb，三轴里最后一条缺失的轴）：回答"表面是什么样"，几何在 GB、身份在 OB、合成在 AC。
  - **新轴 `Runtime/SurfaceBuffer/`**：feature（高级设置 + 兜底默认值）+ **数值 pass**（一趟几何 pass 写五张图 + internal owner：`Color` RGBA16F / `Normal` octa RGBA8 / `Material`(perceptualRoughness·metallic·thickness) / `Reflection`(reflectance·plrStrength) / `Classification`(sssProfileId·curvatureHint·transmittanceHint·materialClassId) / `Owner` R16_UNorm）+ **自用深度** + RDG 与兼容两条路径 + 资源集 `HoSurfaceBufferRenderGraphResources` + Volume 调试直出（五张 + **owner 对齐视图**：绿 = 与 OB 层 0 一致、红 = 不一致或没人写、洋红 = OB 没产出）。
  - **owner = pixel validity 的唯一判据**：数值图里的 0 是合法值，只有 owner 能区分"写了 0"和"没人写"（规划 §0.1 / §4.8）；owner 就是 RSUV 的低 16 bit（= OB 写的 `partId`），所以 AC 之后能用它跟 OB 层 0 的 IdentityId 逐像素对齐。规划里写的 `R16_UINT` 改成 `R16_UNorm`：0..65535 逐值精确，而且消费端不必换成整数纹理通道。
  - **透明不生产**：队列上限压在上不透明段末尾（`GeometryLast`）—— 多层透明加不出唯一的前表面真值（规划 §0.5），先明确"不生产"，等策略定下来再放开。
  - **材质侧（跨仓 lilToon）**：新增 `lil_pass_surface_buffer.hlsl`（由 metadata pass 的 surface 部分派生，**不用 MPB**）+ 22 个 URP lilblock 的 `LightMode=HoSurfaceBuffer` pass + 4 个新材质属性 `_HoSurfaceThickness` / `_HoSurfaceCurvature` / `_HoSurfaceTransmittanceHint` / `_HoSurfaceMaterialClassId`（**不再新增 `_HoMetadataBuffer*`**）。
  - **本轮刻意不做**：SB 的 MSAA semantic lane pass（`_HoSurfaceSemanticOwnerMS` / `Lane{0..7}MS`）与 AC 的 surface sourceMode 合成、`Classification` 的消费者迁移（SSS 仍读 MB）、DebugTile 登记。
  - 文档：SB 规划加「落地状态」并逐行标注执行表进度。

- **Ho-AttributeComposite（AC）起步**（规划 R3-obj）：**语义遮罩的唯一逻辑入口**从纸面变成代码，范围是 object 来源的子集。
  - **`HoSemanticSchema`**：由 `HoObjectBufferPartTags` 生成 8 条 object-only lane（**SemanticId = 位序 + 1、LaneIndex = 位序、objectTagBit = 位序**）——词表只有一份，不为 AC 另造名字；带唯一性 / 范围 / lane 上限校验。runtime catalog 按 **LaneIndex**（不是声明顺序）编译成 GPU 常量表，变脏重建时上传。
  - **`SemanticResolve`**：一趟全屏 pass 读 OB 身份池（4 层 + 覆盖率）与部件行标签，按 lane 算 `Σ cov_i · 该层带不带这一位`，写 4 张 RGBA8（每张两条 `(SemanticId, coverage)`）= **AC Selection 池**。所有消费者共用这一份，不再各自解码 OB；兼容（非 RenderGraph）路径一并接线。
  - **`HoAC_*` 查询门面**（`Runtime/AttributeComposite/Shaders/HoACQuery.hlsl`）：`Identity / Group / Layer0Group / Predicate / TotalCoverage / Selection`（按 lane 取覆盖率并**校验图内 SemanticId**）；`HoAC_Attribute` 本轮恒 0 并在注释里写明"等 SB/R4c"，不假装有。
  - **资源集 + 消费者登记**：`HoAttributeCompositeRenderGraphResources`（Selection 池 + 身份池引用）；`HoAttributeCompositeConsumerRegistry` 记录谁读了哪些名字，解析不到在面板与诊断里报出来（规划 §3：登记是诊断，不是权限）。
  - **面板**：feature 只放高级设置 + **只读**的 schema/catalog 汇总 + 消费者登记表；调试入口在 Volume（lane 覆盖率 / lane SemanticId / catalog 三个模式 + 整屏直出，没产出时暗红，与 OB 同一约定）。
  - **第一个消费者：角色特化**已切过来 —— 删掉自己"读 OB 身份池 + 部件行表"的解码，改读 AC 的 Selection 池并转置成自己的位平面（图记在它自己名下，规划 §9.2）；同角色判定改用 `HoAC_Layer0Group`；输入自检改为「OB 身份池（经 AC 引用）/ AC 语义槽 / GeometryBuffer」。**行为与迁移前逐位一致**（debug 3/4/8/9–15/18–21 不变）。
  - **本轮刻意不做**：surface 来源与 `SurfaceOnly / Union / SurfaceOverride / Intersection`（等 SB 落地）、`AttributeComposite` 数值属性（`constant < surface`）、16 lane 的 MRT 分批（现在固定 8 lane / 4 张）、DebugTile 的 AC 九宫格（需要给 Debug 轴加 `HoDebugViewRenderKind`，AC 自带整屏调试不受影响）。
  - 文档：AC 规划加「落地状态」与 R3-obj 行；`语义掩码.md` v2 的"谁解压"改成 AC。

- `Ho-ObjectBuffer`：组件新增**「赋值方式」**（面板底部，作用于整份部件列表；**默认覆盖**）——把"一个物体被多个部件条目命中"这件事显式化（规划 0.3.15）。
  - **覆盖**（默认）：条目顺序即优先级，**排在下面的条目接管上面条目里的同一个物体**，同组内不再报冲突 —— "顶上放一条『全体』（勾全角色），下面放人体 / 脸 / 前发…"这种用法不再需要反选没归属的物体、也不用给每个部件重复勾位。
  - **指定**：审计模式，重叠 = 配置错误并在面板底部逐条列出谁赢谁输，同组内**取条目顺序在前者**。顺带把两处裁决对齐了：RSUV 的写入（`ApplyIdentity`）此前只按条目顺序、冲突面板（`ResolveAssignments`）按距离，同组重叠且距离不同时两者会互相矛盾，现在统一为条目顺序。
  - **两种模式都不做标签继承**（刻意）：palette 一行 = 一个部件 = 一份标签掩码，像素里只有 `partId`；做继承会让"同一部件里被接管与没被接管的物体"需要两份标签、一行装不下，只能再靠校验强制拆部件。所以条目的标签就是它拿到的那些物体的全部标签（想让它们同时保有上一条的位，就在下面那条里一起勾）。
  - 跨组规则不变（离 Renderer 更近者胜，且一律报冲突）。

- `Ho-ObjectBuffer` **R2（第二步）：角色特化的语义源整支切到 OB**（规划 0.3.14）。**语义 = 身份 × 标签 × 覆盖率**：新增 `HoCharacterObjectSemantic.shader` —— 一趟全屏 pass 读 OB 身份池的 4 层 `(组, 槽位)` 与逐层覆盖率，按部件行表的标签位掩码把该层覆盖率累加到对应通道，打成两张位平面（通道布局与从前的 `objectCustom0_3` / `objectCustom4_7` 一致：`全角色/脸/前发/眼睛` + `眼透区/配件/人体/预留`）。**多归属直接累加**（"整角色 + 脸"同时成立），因此：
  - **前发投影 / 眼睛透过 / 脸色扩散 / 两条轮廓全部拿到真实覆盖率**——以前只有脸那一支碰巧有效、前发与眼透区读不到（MB 的 `objectCustom` 位本来就没被写对），现在四支都按标签工作；像素旋钮（柔化 / 羽化 / 扩张 / 模糊半径）语义不变。
  - **那套「掩码抗锯齿副本」整体删除**（`HoCharacterSemanticMaskBlur.shader` + pass + 5 个「读取抗锯齿掩码」开关 + 「抗锯齿宽度」）：覆盖率本身就是 MSAA resolve 出来的连续场，再滤波只会把已经正确的边缘搅糊（规划 0.3.x「不再把 bit 通道当普通 UNORM 过滤」）。读取一律点采样。
  - **同源判定改口径**：同角色比较从 `MaskId.g` 换成 OB 身份池层 0 的**组字节**（`Id0.r`），与眼透角度表的行号同一套编号（眼睛捕获里写的角色 ID 也改成这个组字节，两边单位统一为字节值）。
  - **材质捕获 pass 也改吃标签**：`LilHoCharacterCaptureShouldDraw()` 由"读 objectCustom 位"改成"读 RSUV 的 `partId` → 部件行表的标签"（`脸` → 脸捕获、`眼睛` → 眼捕获）；标签位名 `HO_OBJECT_TAG_*` 落在 `HoObjectBufferPalette.hlsl` 作为 HLSL 侧唯一权威。没有 OB 身份（RSUV = 0）的物件标签恒 0 ⇒ 两遍都不画。
  - **角色特化不再读 MetadataBuffer**（maskId / objectCustom / SurfaceColor 全部退出；SurfaceColor 那份 coverage 由标签覆盖率替代），feature 的输入自检改为「OB 身份池 / OB 语义位平面 / GeometryBuffer」三项；OB 不在 renderer 里时整支 no-op 而不是"退化成硬边"。
  - **顺带修掉一个静默失效**：屏幕空间的 texel size 以前读 `_HoMetadataBufferMaskIdTexture_TexelSize`（全局纹理没有这一项、实际为 0），"按像素扩张 / 羽化"的半径可能一直没生效；现在由 C# 显式发布 `_lilHoCharacterScreenTexelSize`。
  - 增强轮廓的「来源通道」枚举改名 `HoCharacterSemanticChannel`（值仍是位序号 0..7，与标签位序对齐）。
  - 文档：`语义掩码.md` 升 v2（盒子核服务已删、OB 就是"拿回亚像素相位"的落地）、眼睛透过 / 脸色扩散 / 通道契约 / 浏览器说明同步。

- 修复 `Ho-ObjectBuffer` 抽屉：**Renderer 列表为空时表头不收拖拽**——「Renderer（0）」那一行原来只在列表非空时才是拖放目标，空列表下往它上面放没任何反应（只有下面那条 16px 提示条收东西），看着像整个列表都不收。现在表头恒定接拖拽。

- `Ho-ObjectBuffer` **R2（第一步）**：眼透相机角度修正切到 OB 口径——角度表数据源改为 `HoObjectBufferGroup`（行号 = **OB 组 ID**，朝向取「朝向参考系」），屏幕空间查表键改为 **OB 身份池层 0 的组字节**（`Id0.r`，新增 `ResolveObjectBufferGroupId`，并加 `_HoObjectBufferValid` 兜底），于是多角色同屏不再跨 ID 平均、且不再依赖眼睛捕获缓冲里的预乘角色 ID。眼睛**遮罩**链路仍走 MetadataBuffer，随 R3/R4 的消费者迁移一起切。

- `Ho-ObjectBuffer`：**R1 收口**（规划 0.3.11 的四项）——① 发布 `requested` / `actual` 采样数：全局 `_HoObjectBufferRequestedSamples` / `_HoObjectBufferActualSamples` + 「Sample Count」调试视图（绿 4x / 橙 2x / 红 1x）+ 降级时告警一次，C# 侧读 `HoObjectBufferPass.LastActualSamples`；② 部件行表的全局名改为 `_HoObjectBufferEntries`（与 §1.2 对齐）；③ 删掉选择层里没人读的溢出计数；④ 把"身份池溢出在 N ≤ 4 下结构性不可能"写成结论（0.3.2）。**回归验证器已实跑通过**（`Id0=(1,1,1,1)` / `CoverageTotal=(1,1,1,1)` / `Valid=(0,0.796,0,1)` 绿哨兵 / `Id3=(0.271,0.271,0.271,1)` 背景灰），调查期那个硬编码 PTP 场景路径的场景诊断已删除，验证器保留为整链自检。

- `Ho-ObjectBuffer`：部件条目的「类别」正名为**「标签」（位掩码、可多选）**——`HoObjectBufferPartTags` 覆盖「全角色 / 脸 / 前发 / 眼睛 / 眼透区 / 配件 / 人体」+ 一个预留位，面板上用 Unity 自带的**位掩码下拉**（不另画控件，位多了也不会把面板撑长；显示名取自枚举的 `InspectorName`，加一位只改枚举）。**多选是必须的**："整角色 + 脸"同时成立时单值枚举表达不了；它与 MetadataBuffer 的 `objectCustomMask` 对等，并且是**角色侧唯一的语义扩展点**（角色有固定词表，场景才用自由创建的选区，见规划 0.3.12 / 0.3.13）。palette 部件行空出来的 `category` 列改名 `reserved` 恒 0（行仍是 64 B，不动跨仓契约）；选区条目的 `tags` 仍无输入——选区的语义由它的名字承担。边界不变：材质类语义（皮肤 / 不透明测试 / 半透明…）是**表面**语义，归 SB。

- `Ho-ObjectBuffer`：**朝向改为按身份查表**（规划 0.3.1 重写）——朝向是"每个角色一份"的常量，消费端拿像素里层 0 的获胜身份取组查它即可，所以不再计划逐像素的 `_HoObjectBufferFacingTexture`（留到"同一部件内部朝向逐像素不同"这类需求出现时再开）。会**相对身体转动**的部件（头 / 脸 / 前发）可在条目上填「朝向覆盖」（留空 = 继承组）；「朝向参考系」从「组设置」里独立出来，组这一级只剩它这一项共享常量。顺带删掉调试期的控制台刷屏（每条 RSUV 写入的前 5 条 + 汇总 + palette 表转储）。

- `Ho-ObjectBuffer`：**组 ID 改为自动分配**——注册表认领已落盘的值、把没号或撞车的补到最小可用号并在编辑器期写回组件（组数超过 255 时明确报错）；组件上不再手填，于是"两个组件抢同一个号"从硬错误变成不可能。顺带撤掉「组级标签」（没有任何消费端读它，"整组"语义用组 ID 判定即可）与「优先级」（裁决改为离 Renderer 更近者胜、距离相同用组 ID 定序）。详见规划 0.3.10。

- 新增 **`Ho-ObjectBuffer` R1**：用“per-pixel 只存 IdentityId + coverage，其余按 ID 查表”替换 MetadataBuffer bit mask 身份路径。MetadataBuffer 暂时并存。
  - **组件**：`HoObjectBufferGroup`（Add Component: `Rendering/Ho-ObjectBuffer Group`）维护组/部件表并把 16-bit `group:8 | slot:8` 写入 RSUV；重复组 ID 使冲突组全部失效并报错。
  - **覆盖率**：自建 MSAA（R1 固定 `R16_UNorm`）与相机 AA 解耦，resolve 按整数 ID 数票取 4 层；深度平票改在 linear eye depth 上比较，修复 reversed-Z 方向错误。
  - **lilToon 跨仓 pass**：22 个 URP `.lilblock` 模板新增 `LightMode=HoObjectBuffer`，复用 MetadataBuffer 已验证的 alpha-mask/dissolve/dither/cutout 逻辑；opaque fallback 仍作非 lilToon 兜底。
  - **调试**：`HoObjectBufferVolume` 提供 ID0-3 / total coverage / layer coverage / valid 直出；`object.*` 视图已注册进 DebugTile。无产出时暗红，unknown palette 为洋红。
  - **验证**：Unity 6000.3.15f1 实际编译 / Shader 导入通过；`RSUV → HoObjectBuffer pass → 自建 MSAA → resolve 数票 → palette → 调试视图` 整条链已在 PTP 场景实测闭合（组1 = `0x0100`、组2 = `0x0200`，层0 逐组上色，层1 在轮廓像素上给出第二个身份）。实测闭合的四条硬约束与"层1 边缘是设计结果"的判据见规划 0.3.9。
  - 编辑器菜单 `HoLil/Validation/Validate Ho-ObjectBuffer R1` 是最小闭环回归：断言 Id0 亮且中性（身份没有被写死成常量）、覆盖率为 1、Valid 是绿哨兵、单物体层3 是背景灰；验证相机放在 y=1000，不受当前场景内容影响。

- 修复：**切换场景后新场景渲染不出来（黑屏）、只能重启**，报 `MissingReferenceException: The object of type 'UnityEngine.Texture2D' has been destroyed`，栈顶为 `HoCharacterEyeAngleTable.Upload`。
  - 根因：`HoCharacterEyeAngleTable` 以 `Camera` 为键缓存"每相机一张"的表纹理，而 `RemoveStaleTables` 用 `camera == null` 判活。Unity 的 `UnityEngine.Object.==` 被重载为"已销毁对象的任何比较都返回 true"，于是**重载 / 域重载后连存活相机也被判成 stale**，其表纹理被 `CoreUtils.Destroy` 销毁，同一帧紧接着的 `Upload` 又去访问这张纹理。异常落在 `AddRenderPasses` 内部，会中断整条相机渲染录制的后续步骤，表现就是新场景什么都渲染不出来。
  - 修复（两层，任一层单独成立即可自愈）：① 判活改为 `ReferenceEquals(camera, null) || camera == null`；② 表纹理改为"上传前检查有效性，失效就在原条目上重建"（`EnsureTexture`），并在销毁纹理后给条目置 `textureDestroyed` 标记而不是摘掉条目，因此 `Release()` / 场景卸载触发的资源回收也不会再让后续 `Upload` 落到已销毁对象上。
  - 顺带：表纹理名不再带相机名后缀（`_lilHoCharacterEyeAngleTable_<相机名>` → `_lilHoCharacterEyeAngleTable`），避免逐场景切换时不断累积名字各异的泄漏纹理；`GetOrCreateEntry` 不再在构造条目时创建纹理，创建与重建统一收敛到 `Upload` 一处。

## 0.2.0

- 角色特化：眼透新增**相机角度修正**。
  - `HoMetadataBufferGroup` 新增"面部朝向"（Transform + 脸前/右/上三轴枚举）与 `TryGetWorldFacing()` 世界朝向入口（可供 SDF 等后续消费者复用）。
  - 新增 `HoCharacterEyeAngleTable`：每个渲染相机一张 256×1 角度表，CPU 在 `AddRenderPasses` 时机按相机计算平转角/俯仰角（atan2 全角域）并绑定为全局纹理；多相机/双窗口各自正确，无判定、无模式分支。
  - `Composite` 新增 `ResolveEyeAngleFactor`：视锥内 1 / 视锥外 0（柔化过渡），仅作用于眼透不透明度，前发投影 receiver 与原始 revealMask 不受影响。
  - 参数（Settings + Volume）：启用相机角度修正、角度修正强度、平转半角范围（默认 90°）、俯仰半角范围（默认 60°）、角度柔化（默认 40°）；默认关闭，行为与旧版一致。
  - 调试模式：`EyeAngleFactor (16)`、`EyeAngleTable (17)`。
  - 文档：新增 `Documentation~/CharacterSpecialization_EyeReveal.md`。

- 角色特化：**语义掩码抗锯齿**（feature 级公共服务）+ 前发投影的边缘锯齿修复。
  - 根因：FrontHair/Face 等语义来自 MetadataBuffer 的 `objectCustom` 位（契约就是 `RGBA(bits)`，单采样 0/1），任何把它当轮廓用的消费端都只能拿到硬边。旧的 9 抽整数偏移 + 点采样核只能把边放在整 texel 上：仿真的竖直边响应只有 `0 / 0.285 / 0.715 / 1`（两级 2px 平台夹 0.43 台阶），把「柔化像素」调大只会拉宽平台、不减小台阶 —— 表现就是"柔化无效"，且边缘随相机整 texel 跳动。
  - **新增服务**：`HoCharacterSemanticMaskBlur.shader` 把两张 `objectCustom` 纹理**整体**过一次盒核（每个通道本身就是一个语义的 0/1 场，盒核逐通道运算，不串道）产出抗锯齿版，一次 pass、两个 MRT、一帧一次，所有消费端共用。原位图不改，也不要把这条规则套到 `maskId`/`surfaceData`/`custom0`/`eyeData` 上。
  - **开关按效果就近放置，没有总开关**：顶部只有一个「抗锯齿宽度」（不带分组框/描述框，说明在 Tooltip 里），每个效果的分区里各有一行「**读取抗锯齿掩码**」（前发投影 / 脸色扩散 / 眼睛透过 / 主体轮廓 / 增强轮廓）—— 勾选的效果读抗锯齿版，取消则读原始 bit（硬边）。默认：前三个开，两个轮廓**不读**。五个全关时这趟 pass 不跑（零开销）。着色器侧由 `_HoCharacterSemanticMaskOptions`（x=副本存在，y/z/w=三个效果的勾选）与轮廓源 pass 的逐 pass 开关驱动。
  - **读取只有一处决策**：`SampleSemanticBit(uv, channel, useAntiAliased)` 在"副本存在且该效果勾选"时读副本、否则读原始 bit；`SampleSemanticEdge` 在读副本时退化为一次双线性读取，否则退回固定 1px 盒。因此前发投影的接收面（原来把投影硬裁在发际线、导致"下半段模糊、上半段是硬边"）、眼透区域、脸色扩散、眼睛透过闸门、主体/增强轮廓的语义源都吃到抗锯齿版，且与任何相机 AA 设置无关（MSAA/FXAA/TAA 都关着也照跑）。
  - 盒核而不是高斯：同宽度下高斯把权重堆在坡道中段，台阶比盒高 ~2.2 倍。实测跨竖直边逐 texel 台阶：旧核 `0.285 0.000 0.430 0.000 0.285 0.000`（有平台）、高斯螺旋 `0.033 0.243 0.441 0.248 0.035`、盒 `0.200 0.200 0.200 0.200 0.200`（= 1/宽度，二值输入下的理论下限）。宽度在着色器内下限 1px（0.5px 半径在单像素内仍跳 0.84），`柔化像素` 仍是各效果自己的艺术柔化，叠加在副本之上。
  - **删除「避开前发」参数**（Settings / Volume / Editor / 材质参数打包全部移除，`_HoCharacterHairShadowParams1` 重排为 x=spread, y=blend mode, z=use reveal area）：它本来就是多余的 —— receiver（Face 标记）已经把投影限制在脸上，而"减去前发自身 footprint"只会在投影与投影源重叠处切掉一圈半暗，表现就是边缘一圈叠不上去。现在 `shadowMask = shiftedHair × receiver × same`。
  - 通道契约：新增 `maskcoverage.low/high`（`LILTOON_CHANNEL_CONTRACT_V1.md`），生命周期为 RenderGraph transient / 兼容路径的两张 feature RT。
  - 上限说明：线性模糊只能把台阶做小做匀，边的**位置**永远量化在 texel 上；要拿回亚像素相位必须让掩码自带逐 sample 覆盖（MSAA / 超采样捕获），仍不在本次范围。
  - 文档：新增 `Documentation~/架构边界/语义掩码.md` —— bit 位存掩码的结构性弊端（为什么当轮廓用必然是硬边）、RSUV 装不下 per-pixel 覆盖、线性滤波的上限，以及本轮踩过的 7 个坑（滤波器抽头落在整 texel、二值闸门硬裁、拿未模糊 bit 做算术、透明队列故意丢 alpha、"通道预算"误判、依赖外部 AA、混合模式对比度伪装成几何问题），并附要保留的实现清单、新特性约束与亚像素相位的遗留方案。`MSAA.md` 已加交叉引用。

## 0.1.0

- Added the Unity Package Manager manifest.
- Added runtime/editor assembly definitions.
- Added the initial Weighted OIT renderer feature entry point.
