# Ho-CharacterShadow（CS）计划

日期：2026-09-21。状态：首版实现与验证记录，保留设计边界。本文是角色专用高精度投影的唯一现行文档，已合并并取代旧“专用 Cast 组”占位方案。

本轮已按用户意见收紧范围：**服务 NPR、画面优先；只做天光的角色局部高精度 cast；在 lilToon 中仍是现有环境投影；全场景 caster 候选；不做来源分类或 caster → receiver 美术关系；调试只看投影图。**

这里的“天光”具体指当前 URP 主方向光（太阳光）的实时 cast，不将 SH/APV/天空环境漫反射重新定义为一盏投影灯。实现位于 Extensions 与 lilToon，未修改 HoUrp 源码。

## 0. 首版使用与实际验证

1. 在 Renderer Data 添加 `HoCharacterShadowRendererFeature`。主方向光开启 Shadows，URP 支持主光阴影。
2. 在预制件根节点添加 `Rendering/Ho-CharacterShadow`，引用已有 `HoObjectBufferGroup`。OB 部件中指定的 Renderer 是接收对象；`Receiver Parts` 留空接收全组，也可填写已有部件名。一个 OB 组使用一个 CS 组件。
3. 点“从接收对象计算包围盒”，然后用场景手柄调整 Anchor / Center / Size，确保动作范围留在盒内。自动计算是一次性工具，不会每帧跟随蒙皮收缩。
4. Volume 里覆盖分辨率（`Ho-CharacterShadow Volume` 的运行分组）与 PCF 半径、bias；默认每 tile 2048，最大 16 个接收域、最大 atlas 边长 8192，受设备纹理上限限制。容量不足不降低分辨率，对未分配角色回退普通投影并显示状态。
5. 调试在 Volume 的「调试」分组：`Atlas` 看整图，`Character` 按组件显示的 Tile 编号放大查看，另有两个视图开关（Scene / Game View）。组件 Inspector 显示深度范围和世界单位/texel；场景中可编辑角色盒。当前没有额外的光空间视锥 Gizmo，也没有最终阴影 AOV。

无需改材质；无需为 CS 单独开启 OB 的屏幕纹理绘制。lilToon 原有接收开关、Mask 和各层接收强度继续有效。

当前实现用 `CullContextData.Cull` 和 `CullShadowCasters` 为每个局部域生成独立原生 shadow renderer list；未借用主相机绘制列表。一个隐藏、禁用渲染的正交相机提供完整的光空间 CameraProperties，仅用于构建剔除参数；保留观察相机的场景范围和 LOD 参数。场景 Renderer/Terrain 的 bounds 每相机收集一次用于收紧深度范围，真正绘制仍由 Unity 的 ShadowCaster 列表处理，未用手工 DrawRenderer 复刻规则。后续可在不变更材质接口的前提下优化 bounds 收集。

验证入口：`HoLil/Validation/Validate Ho-CharacterShadow` 检查投影计算；`HoCharacterShadowValidation.ValidateRendering` 与 `HoCharacterShadowValidation.ValidateDistanceRendering` 为独立 Unity batch 工程的 GPU 验证入口，不在用户打开的编辑器里创建测试场景。后者是 §0.1 那个 bug 的常驻回归测试（带级联数 × 距离扫描），**改动 pass 时机、剔除参数或图集生命周期后必须跑**。

已在 Unity 6000.3.15f1 / D3D11 的隔离工程验证：编译；上游 caster 扩大 Z 而不改变 X/Y；无关 caster 不扩大深度；接收盒覆盖；屏幕外 ShadowsOnly 投影；相机拉远 5 倍；Cast Off、组件禁用、接收部件筛选、Feature 禁用/恢复的回退；atlas 调试；lilToon 实际材质的明暗响应。原始 CS 探针测得近处/远处遮挡值 0、取消投影与禁用回退值 1。

修复过的距离回归（第一轮，已被 §0.1 取代但仍保留）：仅替换观察相机的 cullingMatrix/planes/origin，不会替换内部 CameraProperties。原实现拉远后可能得到空 caster 列表，进而用全亮的 CS 替换普通天光阴影。现在由独立正交剔除相机提供一致属性，深度搜索范围不再错误依赖观察相机距离。空 caster 查询仍表示有效的“未遮挡”，但不创建无 caster 的原生绘制列表。这一轮修的是“剔除参数”，**不是**远处阴影消失的真正原因 —— 真正原因见 §0.1。

### 0.1 已修：局部图集与相机距离相关（2026-09-21）

**现象（用户场景）**：相机/角色离远后角色整体阴影消失（CS 与普通主光阴影一起没了）。隔离工程里复现出同一机制的另一种方向：**观察相机在近处时局部图集整块为空**。

**根因**：局部图集的 pass 时机与 URP 自己的逐相机级联 shadow pass 撞在同一个 `RenderPassEvent.BeforeRenderingPrePasses`。此时 Unity 内部“该光源 + 当前相机”的 shadow 状态还没定下来，我们的自定义 split（`ShadowSplitData` + `ShadowDrawingSettings{splitIndex = 0}`）会被这层状态否决：**级联数 > 1 且观察相机不在自己的级联 0 里时，atlas tile 整块为空**，CS 于是静默回退到（已按 shadowDistance 淡出的）普通主光阴影 —— 远处“全部阴影消失”就是这么来的。级联数为 1 时该状态恒定成立，所以现象只在多级联下出现。

**修法**：把 CS pass 的时机提前到 `RenderPassEvent.BeforeRenderingShadows`，在 URP 为本相机渲染级联阴影**之前**构建局部图集，不再受级联状态影响。一行改动：

```csharp
internal HoCharacterShadowPass() { renderPassEvent = RenderPassEvent.BeforeRenderingShadows; }
```

**复现与回归入口**：`HoCharacterShadowValidation.ValidateDistanceRendering`（batch）。相机沿固定方向从 240 m 拉到 8 m，逐个记录 lilToon 探针可见度与 atlas tile 深度。

```text
修复前  casc1 240m..8m  vis=0.000 atlas≈0.98   casc4 60m..8m  vis=1.000 atlas=0.000  ← FAIL
修复后  casc1/casc4 240m..8m 全部 vis=0.000 atlas=0.984                              ← PASS
```

**副作用与残余风险**：该修法依赖 pass 顺序（CS 必须早于 URP 的 shadow pass）。若后续有人把 CS 插到更晚的事件、或引入需要“已渲染的相机阴影”作为输入的逻辑，会重新踩到这个问题 —— 所以距离回归测试必须跟着 feature 一起跑。级联数不再需要改成 1，**PTP 场景保持 `m_ShadowCascadeCount: 4` 即可**。

**排查过程中排除的原因（均有实测数据，记录以免重复调查）**：

| 假设 | 实测 |
| --- | --- |
| 图集是 transient，内存被复用 | 改成 feature 持有的持久 RTHandle + `ImportTexture` 后现象不变（该改动作为安全性修正保留） |
| 剔除参数泄漏观察相机状态（lodParameters / isOrthographic / cullingOptions） | 改成完全由光空间剔除相机生成参数后现象不变（保留） |
| `Allocator.Temp` 数组提前 Dispose（context 命令延迟执行） | 去掉提前 Dispose 后现象不变（保留） |
| caster 材质对相机状态反应（距离淡出 / LOD crossfade） | 换成极简 ShadowCaster 材质后现象相同 |
| 逐 split 的 shadow caster 剔除 | 整个跳过 `CullShadowCasters`，或按级联数发布多份 split，现象都不变 |
| 用普通 `ShaderTagId("ShadowCaster")` 列表绕开 Unity 的 shadow 列表 | 该路径在本 pass 里完全不绘制（atlas 全 0） |
| 级联数本身是原因 | 只是触发条件；提前 pass 时机后级联 1 与 4 结果一致 |

### 0.2 UI 布局（按 Ho-UI 风格规范）

CS 是**逐物体组件**型 feature（接收对象是每个角色自己的声明），不是 OB/SB/AC 那种通道型，所以控制项按“能不能按相机覆盖”分三处：

| 侧 | 分节 | 内容 |
| --- | --- | --- |
| **`HoCharacterShadowVolume`**（调试与逐相机覆盖的落点） | 运行 | 启用、单角色分辨率 |
| | 调试 | 调试模式（`Off` / `Atlas` / `Character`）、`Debug In Scene View`、`Debug In Game View`、单角色 tile |
| **`HoCharacterShadowRendererFeature`** | 运行（兜底） | 启用、单角色分辨率、同时接收域上限、图集边长上限、PCF 半径、深度偏移、法线偏移 |
| | 声明（只读汇总） | 场景里的 `HoCharacterShadow` 组件 → OB 组 / tile / 盒尺寸 / 状态；图集容量与已分配 tile |
| | 调试 | 一行 HelpBox → Volume |
| | 高级 | 渲染时机（只读：固定 `BeforeRenderingShadows`）、调试 Shader、图集与剔除形态（只读） |
| | 运行状态 | 最近一次 `AddRenderPasses` 的结果（`LastCullStatus`） |
| **`HoCharacterShadow`**（组件） | 运行 | 接收组 / 接收部件 / 包围盒锚点 / 中心 / 尺寸 / 边缘回退 + “从接收对象计算包围盒” |
| | 运行状态 | 状态、接收部件匹配数、图集 Tile、投影深度、世界单位每 texel |

约定与偏离说明：

- 色板、分节标题、中文标签 + 英文原名全部按 `Documentation~/Ho-UI_风格规范.md`（运行 / 名称·声明 / 调试 / 高级 / RendererFeature 设置）。
- **调试只有一份真值**：Volume 的「调试」分组。feature 上仍保留 `debugMode` / `debugCharacter` / 两个视图开关作为**兜底值**（Volume 未覆盖时生效，batch 回归测试也直接驱动它们），但 feature Inspector 不再画第二份开关。
- **调试分组没有“强度”曲线**（规范里那一项对 GTAO 是 `AO Debug Pow`）：CS 的调试画面直出光空间线性深度，加显示曲线会让人把亮度误读成深度。这是有意的偏离，写在 Volume 的调试分节里。
- 调试画面只在对应视图开关打开时输出（Scene View 默认开、Game View 默认关），因为它是**直出替换**最终画面。

透明/OIT、XR、大规模角色、动画蒙皮边界与 D3D12 尚未做场景验收；旧式 Execute 路径已提供，但当前验证工程使用 RenderGraph。不要将这些未验收项等同于已支持。真实角色场景还需美术调节包围盒和 bias。

下文是设计约束与后续验收清单；遇到实现状态差异，以本节和源码为准。

## 1. 功能边界

新增独立 `HoCharacterShadowRendererFeature`，显示名 `Ho-CharacterShadow`，简称 CS。预制件挂 `HoCharacterShadow`，指定接收对象和局部包围盒。

CS 生成一张围绕角色接收区的高精度天光深度图：角色自身、场景、其他角色只要符合该天光的普通投影规则，都可以写入这张图。lilToon 采样时，局部覆盖有效就用 CS 替换原来的主光实时阴影采样；没有覆盖就回退原采样。

用户看到的依旧是原有环境 cast：沿用 lilToon 的接收开关、接收 Mask、各层接收强度和阴影着色，不增加一套 CS 明暗层或染色模型。

本次不做：

- 额外方向光、点光、聚光灯、面光源的 CS。
- 多光源 ShadowCast 重构、统一 LightKey/provider 框架或完整 DI。
- 环境影/自身影/头发影分层、caster → receiver 配对与来源分类。
- 脸部专用光照/独立阴影图；脸部常规投影与其他部位一样直接处理。
- 替代现有角色特化的前发假投影等屏幕空间处理。
- lilToon 最终阴影贡献 AOV、阴影单独着色输出或为调试新增材质 pass/MRT。
- 旧占位方案中的远平面专用 cast；它不并入 CS，也不为它保留第二份待办文档。

## 2. 全场景 caster 收集与精度

### 2.1 更多 caster 不会自动摊薄横向精度

方向光使用正交投影，要把两个范围分开：

| 范围 | 由什么决定 | 扩大后的影响 |
| --- | --- | --- |
| 光空间 X/Y，即阴影图拍摄的宽高 | 角色接收包围盒，加过滤保护区 | 相同分辨率下，扩大宽高会降低轮廓采样密度 |
| 光空间 Z，即沿光线的深度范围 | 接收区及可能挡住它的上游场景 | 不改变 X/Y 的 texel 密度，但会影响深度比较精度和 bias |
| caster 候选数量 | 全场景中符合天光投影规则的几何 | 主要增加收集、蒙皮/顶点处理与绘制成本，不会瓜分纹理分辨率 |

例如同样拍摄 2 米宽的接收区域，2048 texel 对应约 0.98 毫米/texel；候选从一个角色增加到整个场景，只要 X/Y 不变，这个值就不变。这是正交投影的尺度示例，不是项目测量。

**因此可以全场景收集，但不能把阴影投影的 X/Y 改为适配整个场景。** 只扩展沿光线方向的 caster 覆盖。把角色盒沿着朝向光源的方向延伸，就形成可能遮挡该角色的柱状区域；墙、树、屏幕外人物都通过相同的深度测试参与。

收集不只是 CPU 列表变长：新增 caster 还要在 GPU 执行 ShadowCaster 绘制。性能不敏感时首版可以接受保守收集与冗余绘制，先确保画面完整。

### 2.2 深度范围不能承诺零影响

near/far 包得过大，有限位数的深度值需要表达更长距离，可能让细小深度差更难分辨，表现为 acne、漏影或 bias 难调。32 位浮点深度也不能代替合理的范围设置。

设计约束：

1. 接收盒 X/Y 固定于角色需求，不随场景 caster 数量扩大。
2. 初版允许按已加载场景的保守有限 bounds 确定上游范围，不设置一个任意的无限远。
3. 优先使用设备支持的高精度 depth format，并保留 near/far 与投影体积可视信息。
4. 可以从全场景候选中，用保守的光空间 XY bounds 相交和上游测试收紧 Z；这是几何正确性/深度精度处理，不需要先建复杂性能剪枝系统。
5. bias 结合世界单位、投影尺寸和深度归一化计算，不照抄普通大范围 CSM 的数值。
6. 动态角色使用当前有效的蒙皮 bounds；过时或过小的 bounds 会漏投影，不能靠扩大一个静态搜索半径解决。

参考：[Microsoft：投影拟合、near/far 与 bias](https://learn.microsoft.com/en-us/windows/win32/dxtecharts/common-techniques-to-improve-shadow-depth-maps)。其中关于紧致投影和光空间深度范围的原则适用于这里；角色局部投影是本项目的具体设计。

### 2.3 “全量”的具体含义与落地路线

全量指**全场景符合这盏天光投影规则的 caster 候选**，不要求用户手填附近环境对象，也不以主相机当前屏幕可见对象作为来源。

原有 light cullingMask / rendering layers、Renderer shadowCastingMode、材质 ShadowCaster pass 等基础规则仍生效。关闭投影的对象不能因为“全量”就重新投影；ShadowsOnly、蒙皮、LOD、地形、裁切材质等需要按实际绘制后端验证。

建议流程：

```text
角色接收盒 → 天光坐标系 → 固定 X/Y + 有限上游 Z
全场景合规 caster 候选 → CS 独立光空间剔除/绘制列表
原材质 ShadowCaster → 角色局部深度图
```

首版不做距离排名、可见性重要性、跨角色共享缓存或自建 BVH。允许提交保守候选，由正常视锥/光栅裁剪排除投影外几何；GPU 裁剪前仍可能发生顶点工作。

实现优先使用 Unity/SRP 自身的场景剔除入口，由 CS 的光空间范围驱动。`ScriptableRenderContext.Cull(ref parameters)` 可以使用自定义剔除参数；不能直接复用相机 DrawRenderers 的可见列表，也不能保留会错误排除屏幕外遮挡的主相机遮挡剔除设置。[Unity：ScriptableRenderContext.Cull](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.Cull.html)

HoUrp 本地源码已有以下接入位置：

- `Runtime/UniversalRenderPipeline.cs`：相机 `context.Cull`，随后初始化主灯/阴影数据并执行 `CreateShadowAtlasAndCullShadowCasters`，再 `AddRenderPasses`。
- `Runtime/ShadowCulling.cs`：生成各灯 shadow split，调用 `context.CullShadowCasters`。
- `Runtime/Passes/MainLightShadowCasterPass.cs`：基于 `ShadowDrawingSettings` 创建 shadow renderer list。

CS 应拥有独立的剔除结果/绘制数据，不能更改主光原有级联的剔除状态。若 RendererFeature 层的时序或 API 不便，可在 HoUrp 增加明确的局部投影剔除入口，向 Feature 提供本帧数据。不要为了强行限制在 Feature 内部而每个角色、每帧 `FindObjectsByType` 后手工复刻全部引擎投影规则。

具体 API 组合、剔除结果生命周期与 shadow list 行为需要在 Unity 原型验证；目前只确认源码接入点，不宣称添加一个 Cull 调用就已经能覆盖所有几何类型。也不能假设现有 native split 数组可无限附加角色切片。

未来性能优化只替换候选生成/剔除/缓存部分，保持同样的接收盒、矩阵和材质采样接口。可在 HoUrp 中逐步加入空间剪枝、跨角色收集复用等，不影响本次材质设计。

## 3. 接入 lilToon：只替换天光 cast 来源

### 3.1 已核实的最小接入点

`lilToon/Assets/lilToon/Shader/Includes/lil_common_macro.hlsl` 的 URP `LIL_LIGHT_ATTENUATION` 有主光 screen/cascade/普通分支，当前计算形如：

```hlsl
atten = MainLightRealtimeShadow(...) * HoShadowCastAttenuation(positionWS);
```

**主光与额外 cast 在计算入口上可分开，但最终仍会相乘。** 这足以局部接入 CS，不要求本次先改成逐灯光照框架。

拟改为以下概念流程，函数名仅为草案：

```hlsl
mainCast = ExistingMainLightSample(...);
mainCast = HoCSResolveMainCast(receiverIdentity, positionWS, mainCast);
atten = mainCast * HoShadowCastAttenuation(positionWS);
```

`HoCSResolveMainCast` 只在当前天光、当前相机、当前对象的 CS 记录有效且接收点位于覆盖范围内时选择局部采样；其他情况原样返回 `mainCast`。局部与普通主光采样在覆盖边缘可以淡入淡出，不能把二者再次相乘。

现有额外 cast 的独立加亮路径与原有乘法行为保持原样。本次不修它的全部设计问题，也不要求旧场景迁移 Legacy 模式。完整整理留到 DI 阶段。

### 3.2 现有材质规则保持不变

`lil_common_frag.hlsl` 已将 `fd.attenuation` 与 `_ShadowReceiveMask`、`_ShadowReceive`、`_Shadow2ndReceive`、`_Shadow3rdReceive` 等结合，再进入原来的 toon 阴影流程。CS 结果继续进入这个入口，不增加另一套来源开关或阴影颜色。

- 组件决定哪些 Renderer 使用局部精度；没有 CS 的对象不受影响。
- 材质已有环境投影接收规则决定是否/如何表现，包括脸上的常规 cast。
- GTAO 的 toon 分界与整体压暗逻辑保留；CS 不写 AO，也不额外压暗整张图。
- SSGI 继续使用原有 source；不把 CS 再乘到 GI 结果。
- 前发假投影等艺术处理继续留在角色特化的屏幕空间流程，CS 不承担它们的分类工作。

首版**不新增 lilToon UI 项**。代码将主光来源选择封装成小函数，为以后 DI 替换留出空间；不提前暴露分类参数。

### 3.3 需要验证的 shader 边界

正常主光 screen/cascade/普通变体都要覆盖；不能只改一个宏分支。继续遵守材质不接收投影的编译/运行规则。若需要在普通 CSM 距离之外使用 CS，必须检查主光变体是否仍存在，并明确其覆盖策略，不能假定宏无条件执行。

局部采样返回值应与被替换的主光采样处于同一语义层，shadow strength、fade、baked/mixed 行为不能重复应用。当前接入口调用的是 `MainLightRealtimeShadow`，不借本次任务擅自重写 lilToon 的全部混合光照。

## 4. 预制件组件与身份

组件保持简单：

- 接收对象：引用 `HoObjectBufferGroup`，选全组或指定现有对象/部件；复用其最终 Renderer 归属。
- 包围盒：Anchor、局部 Center/Size、场景 Gizmo、“从目标 bounds 计算一次”。
- 质量：分辨率、必要的过滤/bias 设置。画面优先，首版不强制自动降质。
- 天光：自动使用与当前 lilToon 主光一致的方向光；没有有效主方向光时停用并回退。

首版使用固定局部盒，可手动覆盖动作范围。动态紧致拟合、额外的脸部盒、多域嵌套不作为开工前提。角色移动时投影跟随；用 texel 对齐、稳定投影尺度与过滤边距处理抖动。

材质关联沿用现有 Renderer 身份，不实例化材质，不替换 sharedMaterial，不要求每角色 MPB。注册表建立“身份 → CS 记录”的映射，shader 从当前 draw 身份查表，不需要采样屏幕 OB 纹理才能接收。

注意：当前 OB 的 `SetShaderUserValue` 写整个 uint，CS 不另占高位写一套 ID。身份构建必须早于消费者 draw；屏幕 OB/AC 的完整渲染不是 CS 的必要前置。共享身份解码应集中封装。

Caster 绘制使用原材质的 `ShadowCaster`，沿用 alpha clip、溶解和顶点变形。不以通用深度 overrideMaterial 替换所有角色材质。

## 5. 资源与替换范围

主方向光下，每个独立角色接收域需要一个局部投影 tile，可统一放 atlas，记录矩阵、tile 范围、有效性与接收身份。无需为自身和环境分配两套纹理。

CS 图可以包含场景/其他角色，但仅指定接收者采用它。角色投地面的普通 shadow map 仍照常绘制，不能把角色从普通主光 caster 中移除。地面影子如果也需要特写，是额外接收区域需求，本次不默认扩大到整个世界。

资源在 Forward 消费前生成。需要正确的 RenderGraph 写/读依赖、相机状态复位与禁用后回退；不能采到别的相机或上一帧角色的 atlas。常规 opaque/cutout 首先闭环；透明/OIT 的消费者依赖和身份变体单独验证，不因它们共享材质函数就宣称自动完成。

世界位置越界、无身份、无主方向光、组件失效等都回退普通主光结果。深度值 1 与“数据无效”分开表达。

## 6. Debug：只看投影图

首版提供：atlas 总览、指定角色 tile 放大、可读的深度映射、包围盒/光空间体积 Gizmo。可附分辨率与 near/far 等元信息。

这条路径读取 CS 自己产生的深度纹理，用独立 debug shader 显示；**不需要修改 lilToon 的 Forward 输出，不新增 lilToon pass，不增加材质 MRT。** 现有 `HoShadowCastDebugPass` 与 `HoShadowCastDebug.shader` 已有相同类型的 atlas 调试形态，可以参考。

“角色表面上的灰度可见性”在技术上也可能通过深度重建和 atlas 查询独立实现，未必需要大改材质，但它不等于经过 lilToon 全部 Mask/toon/透明流程后的最终阴影贡献，本次不做。

要精确单独导出最终阴影贡献会涉及既有非线性材质流程；不为该调试目标拆改 lilToon。验收用 atlas/Gizmo 加 CS 开关的最终画面对照即可。

## 7. 实施顺序与验收

1. **原型**：一天光、一角色、手工盒、原材质 ShadowCaster，验证投影和 atlas 调试。
2. **完整收集**：全场景 caster 候选、独立光空间剔除/绘制，验证屏幕外遮挡和深度范围。该项是正式交付必要条件，不以 self-only 替代。
3. **最小接入**：身份查表，只替换主光采样；保留原有额外 cast、材质接收和 GTAO/SSGI 行为。
4. **画面稳定性**：多角色、动作、切场景/组件删除、SceneView/Game、多相机与边界回退。
5. **之后再优化**：需要时在 HoUrp 剪枝；完整多灯/光追可见性与 DI 单独推进，不再作为 CS 前置步骤。

| 验收场景 | 预期 |
| --- | --- |
| 增加大量无关场景对象 | CS 的 X/Y 与横向 texel 尺度不变 |
| 树/墙/另一个角色在主相机外挡住目标 | 目标仍有正确天光投影 |
| 很远的上游 caster 与脸部近景 | 不裁掉远处遮挡，检查深度精度与 bias |
| 同材质的两个角色，仅一个启用 CS | 只有指定对象使用局部采样，材质资产不变 |
| 粗分辨率 CSM 与 CS 对照 | 有效域内旧主光粗边被替换，没有两张图叠乘的重复影 |
| 保留现有额外 cast、GTAO、SSGI | 三者原路径不因 CS 接入而被重构；整体画面与 CS 开关对照符合预期 |
| 脸部现有接收 Mask、前发假投影 | 材质接收继续生效；角色特化流程保持独立 |
| 角色投地与角色间遮挡 | 落地影保留，其他角色可自然成为 caster |
| 头发 alpha clip、溶解、蒙皮动作 | caster 形状与原材质规则一致 |
| Debug 关闭/开启 | 只切换独立投影图显示，不改变 lilToon pass 布局 |
| 禁用组件/切相机/身份重建 | 不使用过期记录，回退普通主光阴影 |

## 8. DI 的边界与参考

CS 留给未来的是独立的局部深度资源和主光来源选择函数，不预先实现完整 DI 抽象。以后 DI 可替换主光可见性，或在相应路径继续使用 CS；同一遮挡不重复乘算。

现有额外 ShadowCast 的总衰减耦合已在源码确认，但本次不改变它。等 DI 明确接管直接光照时，再统一讨论多灯归属、追踪场景表示和采样方式。NPR 与画面优先的前提保持不变。

公开参考用于验证技术方向，不将别的引擎完整照搬：

- [Epic：Per-object shadows 与对象 bounds](https://dev.epicgames.com/documentation/unreal-engine/stationary-light-mobility-in-unreal-engine)。角色局部投影是成熟方向。
- [Microsoft：Shadow depth maps 精度与稳定性](https://learn.microsoft.com/en-us/windows/win32/dxtecharts/common-techniques-to-improve-shadow-depth-maps)。X/Y 拟合、near/far、bias、texel 对齐需要分别处理。
- [Unity：ScriptableRenderContext.Cull](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.ScriptableRenderContext.Cull.html)。独立剔除的 API 入口；具体集成仍需原型验证。
- [NVIDIA：RTXDI Integration](https://raw.githubusercontent.com/NVIDIA-RTX/RTXDI/main/Doc/Integration.md)。未来 DI 的场景与着色接入是独立工程，不作为本轮前置。
