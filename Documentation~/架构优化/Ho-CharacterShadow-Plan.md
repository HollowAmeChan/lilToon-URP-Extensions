# Ho-CharacterShadow（CS）调查与规划

日期：2026-09-21。状态：设计建议，尚未实现；本轮只调查源码与公开资料，未运行 Unity 场景或测量 GPU 性能。

**用户已明确的最高优先级：管线主要服务 NPR；PBR/光追计算用于扩大 NPR 的表达空间；性能不敏感，画面效果优先。** 因此本规划以可控的造型、稳定的阴影边界、动画连续性与分层调色为验收目标，不以物理无偏或最低 GPU 成本作为艺术决策的前提。下文的原始可见性准确性是可编辑输入的可靠性要求，不约束最终必须写实。

调查基线：Extensions `4d55cc8`，lilToon `5e9e50a`，HoUrp17.3.0 `50c4e04`。下文区分现有行为与拟新增契约；README 中部分 MB/旧角色捕获描述已落后于代码，以当前 OB / GB / SB / AC 实现为准。

## 1. 建议先确定的方向

新增独立 `HoCharacterShadowRendererFeature`，显示名 `Ho-CharacterShadow`，简称 CS。它为指定角色接收区域提供按灯索引的高精度实时可见性，不输出最终阴影颜色，不把所有灯的阴影压成一个全局暗化因子。

预制件挂 `HoCharacterShadow` 组件，引用 OB 身份组，指定接收部件、包围盒与质量。通过对象身份和全局 GPU 表关联 lilToon，不实例化或覆写材质。材质选择是否采用局部精度覆盖及如何风格化；角色灯光配置决定主造型光、补光和阴影控制关系。

第一版以“主方向光 + opaque/cutout 接收”完成闭环，范围缩小是为了验证职责，而非因性能削减质量。最终可交付模式推荐**角色接收区域的完整局部阴影**：角色自阴影和能挡住角色的场景投影一起渲染。只画角色的自阴影可用于技术原型，但不能宣称已经干净替换角色上的普通阴影。多角色数量、分辨率与 caster 范围按画面需求扩展，不先制定低预算硬上限。

CS 先接入按灯可见性入口，再扩展额外灯。未来 DI 复用入口和对象配置，按能力选择 CS、普通 shadow map 或光追可见性。完整光追已经覆盖同一束光时，不再叠乘 CS。

与旧规划的关系：`LILTOON_SPECIAL_CAST_PLACEHOLDER.md` 曾设想在旧 ShadowCast 内增加 CharacterFace 组。本轮用户已提出独立 CharacterShadow Feature，因此这里给出独立生命周期、接收身份与逐灯接口的新方案；复用底层绘制能力，不受旧占位文档“只加一个组”的形态约束。旧文件中的 `shadow.face` / `shadow.add0..N` 是通道规划，不能当作本轮已核实可用的逐灯 shader 接口。未来 AOV 应按“原始可见性 / NPR 塑形结果 / 最终阴影贡献”明确登记，不能只按纹理名称猜语义。

## 2. 业内做法与能借鉴的部分

| 方法 | 主要解决什么 | 对本项目的意义 |
| --- | --- | --- |
| Per-object / inset shadow | 为单个动态对象分配局部阴影投影 | 与“包围盒特写 shadowcast”最接近 |
| 场景 CSM + 局部高精度投影 | 大范围覆盖与重点区域精度分工 | 保留普通阴影承担世界覆盖，CS 处理角色接收区域 |
| Contact shadows | 在屏幕深度里短距离步进，补细小接触阴影 | 可选补充，不能提供完整场景可见性 |
| Ray-traced shadows | 查询接收点到光源的几何遮挡 | 可替换某盏灯的阴影来源，不必重写角色材质美术逻辑 |
| ReSTIR DI | 对直接光照的灯/光源样本进行重要性采样和时空重用 | 解决多光采样成本；不是独立的场景表示，也不是 AO |

Epic 的 stationary light 文档明确区分 world → movable object 与 movable object → world，并强调 per-object shadow 依赖准确 bounds。这支持我们分开设计“谁接收”和“谁投射”，而不是一个 Renderer 列表同时隐含两种职责。[Epic：Stationary Light Mobility](https://dev.epicgames.com/documentation/unreal-engine/stationary-light-mobility-in-unreal-engine)

微软的 shadow map 技术资料说明了紧致光空间投影、near/far、bias 与 texel 对齐的关系。CS 的精度来自把有限 texel 分配到小区域；仅提高纹理分辨率不能处理抖动、漏掉投影物或 bias 错误。[Microsoft：Common Techniques to Improve Shadow Depth Maps](https://learn.microsoft.com/en-us/windows/win32/dxtecharts/common-techniques-to-improve-shadow-depth-maps)

HDRP 的 contact shadows 在屏幕深度中短距离 ray march；其 ray-traced shadows 则可替换 opaque 的 shadow maps，并在超出配置灯数时回退 shadow map。注意“结果存屏幕空间”不等于“仅用屏幕深度追踪”。这是我们区分屏幕追踪、硬件光追与最终可见性纹理的参考。[Unity：Contact Shadows](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Override-Contact-Shadows.html)；[Unity：Ray-traced Shadows](https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@17.0/manual/Ray-Traced-Shadows.html)

RTXDI 将 light sampling/resampling 与应用提供的材质、场景表示、ray tracing、G-buffer/API 分开；集成需要当前/上一帧灯索引映射和最终样本着色。因此“增加 DI Feature”实际会涉及管线协议，不能只替换一个 atlas pass。[NVIDIA：RTXDI Integration](https://raw.githubusercontent.com/NVIDIA-RTX/RTXDI/main/Doc/Integration.md)

2025 年的 *Many-Light Rendering Using ReSTIR-Sampled Shadow Maps* 研究用 ReSTIR 选择高质量 shadow map 更新对象，其他灯使用近似 shadow map。它证明重采样与 shadow map 可以组合；它是研究方案，不代表本项目应直接照搬或能直接复用现有图集实现。[NVIDIA Research：论文页面](https://research.nvidia.com/labs/rtr/publication/zhang2025many-light/)

## 3. 本地管线的实际情况

### 3.1 旧 HoShadowCast 的问题在接收语义

已确认的源码行为：

- `Runtime/ShadowCast/Shaders/HoShadowCastSampling.hlsl` 中，各方向灯、局部灯与第二方向光路径分别累乘阴影，`HoShadowCastAttenuation()` 再合并为一个标量，并带最小衰减下限。
- lilToon 的 `lil_common_macro.hlsl:831/835/839` 把该总衰减乘进 `MainLightRealtimeShadow(...)`。
- 同文件 `lilGetAdditionalLights()` / `lilGetAdditionalLightHDR()` 累加灯颜色与距离衰减，未把每盏灯的 `shadowAttenuation` 用于各自贡献；HDR 加亮和其他补光路径又使用旧总衰减。
- `HoShadowCastLightShadowAttenuation()` 已有单灯查询雏形，但混入 light influence、shadow strength 和下限；不宜原封不动充当新的原始几何可见性接口。

这意味着：补光 B 被挡住时，不仅 B 的贡献可能变暗，主光 A 和其他补光也可能被共同压暗。如果用户希望补光阴影压住整个人物，这是合法的 NPR 美术意图；目前的问题是这种跨灯控制被隐含在通用阴影查询中，无法明确区分“本灯遮挡”与“整体造型暗化”，不能只靠增加 PCSS 样本或调整 bias 解决。

目前没有逐场景验证其视觉影响程度，不推断所有现有画面都错误。既有风格可能依赖这个行为，迁移必须保留显式 Legacy 模式与前后对照。

可保留部分：图集分配、不同灯型投影、深度绘制、过滤算法、诊断与 C#/HLSL 契约校验。需要重做的核心是灯身份、原始可见性和着色消费。旧总暗化若有表现价值，应迁移为显式“造型阴影/跨灯暗化”控制，而不是直接删去或继续冒充逐灯 visibility。

### 3.2 身份链路已存在

`HoObjectBufferGroup.ApplyIdentity()` 为 MeshRenderer / SkinnedMeshRenderer 调用 `SetShaderUserValue(partId)`；低 16 位为组/槽身份。注册表负责分配、重建与生命周期。

**当前写入的是整个 uint 值。** 不能让 CS 私占高 16 位再单独写入，否则会被 OB 下一次赋值清掉。也不应再造独立角色 ID 系统。

拟复用：

```text
HoObjectBufferGroup → Renderer 身份
HoCharacterShadow → 身份到 CS receiver 配置的映射
CS Feature → 按角色/灯生成 atlas、矩阵与有效性表
lilToon Forward → 当前 Renderer 身份 → CS 查询 → 本盏灯的阴影输入
```

这里可以不采样屏幕 OB 纹理：当前 draw 已知道自己是谁。需要在共享身份查询层增加面向当前 Renderer 的轻量 helper，集中维护 packing，不能每个 shader 自己解码。现有 `HoACQuery.hlsl` 是屏幕语义/属性查询门面；屏幕调试、未来屏幕 DI 仍经 AC 查询身份与 coverage。

只需依赖 OB 身份注册与生命周期，不必为了 CS 强制渲染整套 OB/AC 屏幕缓冲。实现时要显式确保 registry 在消费者 draw 前完成构建；当前已有身份变量/instancing pragma，但 Forward/OIT/实例化变体仍须逐项编译验证。

### 3.3 GTAO 和 SSGI 并非同一种“阴影”

- GTAO 在 `BeforeRenderingOpaques` 生成 `_HoAOTexture`，依赖先产生的 GB。
- `lil_common_frag.hlsl` 的 `lilCalcAO()` 合并 AO Map 与实时 AO；`lilGetShading()` 用 `_AOStrength` 改 toon 分界，`lilApplyAODark()` 用 `_AODarkStrength` 压暗颜色。
- SSGI 默认在 `AfterRenderingOpaques` 追踪，后续加算 GI；当前 Composite 是 `cameraColor + GI * intensity * receiverValid`。
- SSGI shader 的若干重用/滤波阶段将 AO 差异当引导权重。不能把这理解为“SSGI 已经统一使用 GTAO 来遮挡全部间接光”。

因此现状是美术化 AO + 屏幕 GI，而非已经完成 direct/indirect 分离的统一光照积分。CS 加入后可做到资源与调度不冲突，但默认参数是否过黑仍需场景验收。

### 3.4 ShadowCaster 和 RenderGraph

旧 ShadowCast 复用原材质 `ShadowCaster` pass，不使用通用 overrideMaterial。lilToon 的 caster 进入共用 alpha 处理，CS 应保持同样做法，继承 alpha clip、顶点变形、材质剔除等行为。

旧 pass 使用相机 `cullResults` 通过 DrawRenderers/RendererList 重绘。该代码没有为 CS 的局部光空间构建独立剔除结果，不能据此承诺屏幕外遮挡完整；新实现必须专门验证并建立光空间 caster 收集/剔除。

HoUrp 的 `DrawObjectsPass.cs:301` 对 opaque 使用 `UseAllGlobalTextures(m_IsOpaque)`，transparent 没有相同的全量导入。OIT 也有独立绘制链。因此 `SetGlobalTexture` 并不自动完成所有消费者的 RenderGraph 读依赖；后续透明/OIT 接入必须显式处理 atlas 与 buffer 生命周期。

## 4. 必须分清：特写哪一部分阴影

| 关系 | 第一版建议 |
| --- | --- |
| 角色 → 自身 | CS 高精度覆盖 |
| 场景/其他角色 → 该角色 | 完整局部模式必须收集这些 caster |
| 该角色 → 地面/世界 | 继续由普通场景 shadow map 承担 |
| 角色 A → 角色 B | B 的完整局部投影需包含 A；否则用明确的场景后备 |

“指定对象”默认表达**接收高精度阴影的对象**。完整局部模式里的 caster 还包括光线上游场景。组件可显示模式说明，避免用户以为只拖了角色就只画角色几何。

推荐把 CS 定义为 receiver-focused local shadow。未来若要角色投在地面的影子也高精度，需要额外指定接收地面区域或扩大/新增投影，不能认为角色包围盒会自动覆盖长长的落地影子。

### 4.1 为什么不能简单相乘

以下 V 均为 0=遮挡、1=可见：

- 同一遮挡的两个过滤结果都是 0.5，相乘变 0.25，产生重复压暗。
- `min(V_scene, V_self)` 是偏保守的临时补充，不能去掉 scene map 已有的粗糙自阴影轮廓。
- 直接用只含自身的 `V_self` 替换场景结果，会丢掉墙/树投到角色上的阴影。
- 普通 shadow map 只存最近深度，没有足够信息在采样阶段可靠减去某角色的自阴影。

### 4.2 推荐的替换语义

完整局部模式为同一盏灯重新绘制能够遮挡接收区域的所有相关 caster，产生 `V_local`：

```text
V_realtime = lerp(V_scene, V_local, localCoverageAndTransition)
```

成立条件：local 投影包含所声明范围内的遮挡；该点落在含过滤边距的有效接收区；本帧 atlas/灯映射有效；接收对象有权限使用此记录。

权重为 1 时替换场景实时采样；超出覆盖或不可用时返回场景后备，不把“查询失败”混同“确定未遮挡”。随后统一处理 light shadow strength、距离淡出与已有的 baked shadow/shadowmask 混合策略；不能用裸 CS 深度测试无条件覆盖最终混合结果。

如果为了更严格拆分而采用“环境-only + 角色-only”两张图，同一光源样本可以组合几何遮挡，但对已经各自滤波的两个软阴影值直接相乘仍是近似。第一版不采用全局排除角色 caster 的办法，避免破坏角色落地影子和角色间投影。

## 5. 组件、Feature 与 lilToon UI

### 5.1 预制件组件草案

`HoCharacterShadow`：

- `Object Group`：引用现有 HoObjectBufferGroup；没有时可由显式设置操作创建/配置，不能静默重写已有部件。
- `Receivers`：默认全组，可选已有部件/具名选择；CPU 解析为最终 Renderer/身份集合，复用 OB 冲突裁决。
- `Bounds Anchor`、局部 `Center/Size`、Gizmo。默认稳定手工 bounds，提供从指定 Renderer bounds 计算一次的按钮。
- `Bounds Mode`：固定局部盒 / 自动跟随；自动模式有 padding、立即扩张和延迟收缩，保证动作不被旧边界裁掉。
- `Light`：第一版默认主方向光；后续允许指定角色主造型光，而不强迫它永远等于 URP 自动选择的最亮主光。真实 LightKey 与艺术用途分开记录。
- `Quality/Priority`：分辨率档、屏幕占比优先级、远处淡出；全局预算归 Feature。
- 高级区：bias 微调、环境 caster layer 策略、覆盖诊断；light layer/cullingMask/shadowCastingMode 按真实灯语义执行。

同一个 Renderer 默认只能属于一个有效 CS 接收配置；以后允许“全身 + 脸部特写”时再定义明确的优先级和单一来源选择，不把两个域的阴影相乘。

### 5.2 不覆写材质

Caster 端使用原材质的 ShadowCaster pass；receiver 端修改 lilToon 共用 shader 接口并查全局表。CS Component 不调用 `.material` 克隆材质，不覆盖 sharedMaterial，也不需要每角色 MaterialPropertyBlock。

全局资源只是 transport：现有 shader 仍需显式新增查询代码；任意第三方材质不会因为挂了组件而自动接收 CS。未来 lilPBR/其他 shader 接入同一门面即可。

### 5.3 材质 UI 保持很小

建议在现有阴影接收区域显示“角色局部阴影：自动 / 使用场景阴影”，默认自动；后者表示绕过局部覆盖，仍接收正常阴影。已有“不接收阴影”设置继续统一生效。

可在高级区提供局部覆盖权重/mask，但它应表示 scene 与 local 的选择/过渡。风格化 UI 应按用途命名，例如投影阴影颜色、边缘硬度、脸部接收 mask、对明暗分界的影响，而不是按后端命名“CS 专属颜色”。这些艺术参数可以存在，且不应因迁移到光追而失效；第一版优先复用现有 toon 阴影控制，确认存在表达缺口后再新增。

NPR 后续重点包括 caster → receiver 控制（例如头发投脸、衣服投身体、脸部屏蔽某类自阴影）。单张只含最近深度的图不能在采样时任意拆出 caster 来源：若要独立调节“环境影/自身影/头发影”，需要按指定关系另绘深度层或提供具有相应分类能力的追踪查询。画面需求成立时接受额外绘制成本；不靠粗暴全局 mask 假装已完成分类。

组件决定谁、哪盏灯、覆盖哪里；Feature 决定预算和生成；材质决定如何表现。共享材质被多个角色使用时，各自通过 Renderer 身份选不同记录，材质资产不存角色 ID。

## 6. 投影与运行时资源建议

### 6.1 主方向光

1. 把组件局部盒的八个角转换到世界/光空间，用光空间 XY 包络构建正交投影。
2. 包围盒控制 receiver 区域。沿光线上游扩展 caster 搜索体积，并结合场景遮挡范围确定 near/far；不能只用角色自身 Z 范围。
3. caster 搜索的有限距离必须可诊断。若不能保证替换所需的遮挡覆盖，不能把局部结果标成完整有效；退回普通阴影或明确采用近似模式。
4. XY 留 PCF/PCSS 过滤保护区；atlas tile 有 guard band，tap 不越界，不采到邻居角色。
5. 固定/分档投影尺寸 + 光空间 texel 对齐。仅把中心取整不能解决自动 bounds 每帧缩放造成的抖动；灯方向变化、角色动画仍需实测。
6. bias 按世界单位/texel 尺度推导，区分 depth、slope、normal bias；使用与 caster/receiver 一致的 reversed-Z 和矩阵转换。

精度直觉：投影宽 2 米 / 1024 texel ≈ 1.95 毫米/texel；宽 40 米 / 2048 ≈ 19.5 毫米/texel。这里只比较假设的投影宽度，不是本项目的测量，也不能由体积大小直接推导所有视角的最终像素精度。

第一版先固定 PCF 验证稳定性。清晰、连续、可控的 toon 边界优先；软阴影不是自动更好。PCSS/物理半影作为后续可选输入，光源角度/半径要与灯类型一致；艺术化边缘再单独重映射，不能直接把旧功能所有软阴影旋钮复制过来。

### 6.2 容量与灯型

资源占用按“角色 × 灯 × 投影切片”而非角色数量计算。第一版只做单主方向光，可比较 1024/2048/4096 等分辨率，以近景发丝、鼻影、衣褶和动画稳定性决定默认质量；不先承诺低预算，不承诺固定毫秒数。保留占用诊断用于防止资源耗尽，不主动以性能为由降质。

聚光灯需从光位置建立透视投影并裁剪有效接收域；点光一般按 cubemap 多面处理，不能直接复用方向光正交盒。面光源一个中心投影不足以表达每个 emitter sample 的精确可见性。它们都是后续阶段。

D32 图集 2048² 纯深度数据约 16 MiB，4096² 约 64 MiB，不含驱动对齐/额外资源。性能还受蒙皮角色重复绘制、环境 caster 数、过滤 tap 与多相机影响。缓存需要角色动画、灯、caster 场景变化等失效依据；不能只看根 Transform 不动就缓存。

### 6.3 模块拆分草案

- `Runtime/CharacterShadow/HoCharacterShadow.cs`：预制件配置与注册。
- `HoCharacterShadowRegistry`：接收身份解析、冲突诊断、组件生命周期。
- `HoCharacterShadowRendererFeature` / Pass：每相机计划、剔除、绘制与资源发布。
- FrameData / RenderGraphResources：atlas、矩阵、tile/coverage、接收索引与 light 映射。
- `Shaders/HoCharacterShadowSampling.hlsl`：纯查询，不写最终颜色。
- 共享 `HoLightVisibility` 契约：位于不依赖某个具体阴影 Feature 的轻量公共层；先定义少量真实后端所需接口，不提前建大框架。
- Editor：组件 bounds Gizmo、预算/有效性 Inspector、Ho-DebugTile 入口。

每相机重置 active/count，未执行时有效后备；隔离 SceneView、Game、反射/预览与 camera stack，不能使用上一相机记录。RenderGraph 中显式发布 atlas 并声明消费者依赖。兼容模式是否纳入首版由当前项目运行配置确认，不能只因旧 Feature 支持就宣称新功能也支持。

## 7. 与其他系统的合作契约

对每个接收点 x 和真实灯 L，概念上应先算：

```text
direct contribution(L, x) = light radiance × distance/cookie terms
                            × visibility(L, x) × material response
```

各灯贡献再进入指定的 toon 光照策略。这只是可靠的底层输入，不是最终画面必须遵守的物理着色公式。允许“某盏灯的影子控制所有灯明暗”，但必须是显式的风格化路由，不能成为所有 provider 默认附带的行为。

推荐分三层：① 几何可见性（场景/CS/追踪）；② NPR 阴影塑形（阈值、边缘、区域 mask、caster/receiver 关系）；③ toon 着色（阴影颜色、明暗层次、补光和跨灯控制）。主造型光负责大明暗，补光可以只提暗部或保留底色，轮廓光可有独立接收策略。上述策略可以有意偏离写实，同时仍可明确追踪每个输入来源。

建议接口表达：

```text
QueryVisibility(receiverIdentity, LightKey, positionWS, normalWS, lightSample)
  → visibility, valid, coverage, source, revision
```

`LightKey` 表示稳定的真实灯身份；URP visible-light index、Forward/Forward+ shader index、旧 atlas slot 都是每相机/每帧映射，不可互当身份。主光和额外灯重选时也要更新映射。第一版 lightSample 可退化为方向光方向；面光源/DI 扩展需要光源采样点，单个 lightId 不够。

原始 visibility 不混距离衰减、灯强度或美术下限。局部数据无效时由路由器选择普通来源；烘焙阴影与灯 shadow strength 在统一层只应用一次。

| 系统 | 保留职责 | 与 CS 的协同 |
| --- | --- | --- |
| URP 普通阴影 | 世界覆盖、角色投地、普通接收者 | CS 有完整覆盖时对相应 receiver/light 替换实时查询，其他区域后备 |
| HoShadowCast | URP 不覆盖或定制的额外灯阴影生产 | 迁移为单灯 provider，不能继续作为总暗化乘数参与新路径 |
| CS | 指定角色的高精度局部可见性 | 不自带光，不单独压暗 camera color |
| GTAO | 近场环境遮蔽与既有 toon 美术控制 | 与逐灯 visibility 分开；不要再加入统一阴影乘积 |
| SSGI | 屏幕间接光估计及其重用/滤波 | 使用已包含正确直接阴影的 opaque source，不把 CS 再乘到整张 GI |
| DI | 直接光照的采样、可见性求解与着色协调 | 对每束光选择有效 provider，使用自己的估计与历史契约 |

AO 的长期目标是区分 ambient/indirect occlusion 与显式 toon 风格控制；当前两个 AO 输出不能悄悄改义，也不应为了 PBR 规范强制取消整体压暗或 ramp 推动。GI 已解析出的几何遮挡与 AO 半径有重叠时，若仍叠加压暗，应属于用户可调的风格选择；可靠的几何遮挡输入不重复计入，艺术化暗化可以明确叠加。

第一版 CS 不重构 AO，只提供分离调试：scene visibility、CS visibility、最终逐灯 visibility、AO、GI。用组合开关判断是否重复压暗。

## 8. 通向 DI 的分阶段路线

### S0：契约与旧路径隔离

建立稳定 LightKey 和每帧映射；抽出按灯可见性接口，保留 Legacy 总衰减仅服务旧模式。审计所有补光分支，包含 HDR/非 HDR、vertex/pixel、Forward/Forward+；不能只修一个宏。

新模式的基础路由保证主光只使用主光遮挡，补光先各自乘可见性再聚合；旧的跨灯暗化另建显式 NPR 控制。逐像素 CS 不允许退化成仅顶点采样。角色重点灯优先确定性枚举，作为高质量正式路径和后续 DI 对照，不仅是待替换的临时方案。

退出条件：双灯测试中遮挡 B 不削弱 A；灯顺序和主光选择改变后仍映射到正确灯；无 CS 时新路由退回普通结果。

### S1：CS 几何原型

一角色、一主方向光、手工 bounds、原材质 ShadowCaster、固定 PCF、身份查表、关闭即回退。可先在孤立角色场景验证 self-only 的矩阵/精度，但该阶段不作为完整替换交付。

### S2：CS 可用版本

补齐环境上游 caster 剔除、完整局部覆盖与场景替换；预算、边界淡出、动作 bounds、跨相机状态和诊断。验证角色投地仍由普通阴影保留。通过组合测试后再给材质提供默认 Auto。

### S3：额外灯迁移

HoShadowCast 的 atlas 与 caster 路径改为单灯后端，清除新模式中的总乘法依赖。逐步开放额外方向光，再评估 spot/point 的收益和切片预算；旧场景保持显式 Legacy，迁移结果可对比和回滚。

### D0：先实现稳定、可验证的 ray visibility

先对一盏灯和受控接收表面做直接可见性追踪，与确定性 shadow-map 路径对照，不急于上 reservoir。

- 屏幕 depth/Hi-Z ray march 可复用 SSGI 的部分几何设施，但屏幕外、背面、被前景挡住的几何缺失仍存在。miss/离屏只能给“不确定”，需要后备，不能断言可见。
- 若要求完整场景硬件光追，需要独立验证加速结构、蒙皮更新、alpha-test/双面几何与平台能力；现有 SSGI 没有替我们完成这些工作。
- 优先让追踪输出按灯 visibility，在 Forward 前可用，lilToon 继续计算自己的 toon 响应。D0 不必先建立能完全重建 lilToon 的延迟材质系统。

### D1：选择是否、何时加入 ReSTIR DI

画面效果优先时，ReSTIR 不是必经步骤。对于数量可控的角色造型灯，优先确定性的逐灯光照与充足 ray samples；不要为减少射线引入明暗分界跳动或去噪糊边。只有许多灯/面积光使重采样有实际价值时，再加入候选 PDF、灯样本、reservoir 权重、时空重用、当前可见性验证与去噪。SSGI 的调度、motion、深度金字塔、调试方式可复用思路，但不能直接复用 GI reservoir 的 payload/归一化语义。

决定角色造型的主灯/重点补光建议继续确定性计算，其余灯可选择随机估计；必须明确灯集合互斥或使用正确的估计权重，不能两边完整计算后意外相加。toon ramp 是非线性的，逐灯评估后求和与“总光照过一次 ramp”并不等价；应先选定美术目标，再选择能稳定表达该目标的方案。无偏估计本身不能保证阈值化后的 toon 轮廓稳定，不能拿物理指标代替动画画面验收。

如果 DI 输出最终已着色 RGB，它必须接管相应直接光分量，不能像现在 SSGI Composite 一样在已有 direct lighting 上再加一次。若采用延迟计算，还要确认 SB/AC 是否具备所需 albedo、normal、粗糙度、toon 参数；现有 surface 属性不能默认代表完整 lilToon 材质。

直接光结果应在 SSGI 获取 source 前完成。DI 与 GI 分开拥有资源和历史，避免新 direct 被旧 SSGI history 重复带回。灯/角色身份重分配、来源切换、材质模式变化都要有历史失效或版本策略。

### D2：CS 的去留按覆盖决定

可保留为：无光追设备的高质量后备、透明接收路径的局部 atlas、确定性角色主灯、短程/屏幕追踪缺失时的局部来源。

若某接收点/光源样本的完整光追已经给出可靠结果，CS 对该样本应旁路；不承诺其渲染 pass 永远有必要。可长期复用的是组件配置、身份、预算意图与查询契约。

**普通方向/点光 shadow map 不能代替任意面光源样本的精确 ray visibility。** 使用 CS 作软阴影或面光源近似时，应记录近似模式，不能声称保持原 ReSTIR 估计器的无偏性。

## 9. 首版验收与未决信息

| 测试 | 必须观察到 |
| --- | --- |
| 同一材质的两个角色，只启用其中一个 CS | 只有指定 receiver 使用局部表，材质资产不变 |
| 低分辨率主光阴影 + 角色近景 | CS 有效域中旧粗糙自阴影不残留；关闭恢复原结果 |
| 角色走到墙/树影下，遮挡物移到屏幕外 | 外部遮挡保留，不依赖主相机可见 Renderer 列表 |
| 角色投地和角色相互遮挡 | 地面投影保留，角色 B 不漏掉 A 的遮挡 |
| 主光 A + 补光 B，分别遮挡 | 每盏灯只损失自己的贡献 |
| 同一盏灯同时具备普通图、旧图、CS | 调试显示唯一有效路由，无重复叠乘 |
| 转身、抬手、裙摆、头发 alpha clip、溶解 | bounds 不裁切；caster 和可见材质形状一致 |
| bounds 边缘/预算不足/远处淡出 | 有效回退，无 atlas 串色和突然漏光 |
| SceneView/Game/多相机/组件删除和重载 | 身份与资源及时重建，不沿用别的相机记录 |
| CS × GTAO × SSGI 开关组合 | 区分逐灯遮挡、toon AO 与 GI；允许有意的深阴影，消除不可独立控制的重复暗化/计光 |
| 主造型光/补光/轮廓光分别调整 | 保持预期大明暗，补光不因隐藏的总阴影乘法失去可控性 |
| 静帧与动画近景对照 | 发丝、鼻影、衣褶边界清晰稳定，无为省采样导致的去噪糊边/拖影 |
| RenderGraph 与后续透明/OIT 模式 | 无隐藏资源读依赖、无旧帧/错误前表面阴影 |

已明确：NPR 优先、画面优先、性能不敏感。仍需在实现前从项目配置/场景实测补充：目标设备与图形 API（决定可用追踪能力）、角色/灯数量、实际 renderer 模式、透明头发需求、是否有 mixed/baked lights，以及用户希望“角色投地”是否也属于特写范围。这些不阻止本轮确定接口和最小范围，但会影响下一阶段优先级。不重新要求用户给低性能预算作为开工条件。

建议下一步实施内容限于 S0 + S1 的可审查原型，随后用 S2 的场景门槛决定默认启用。暂不同时重写 GTAO、SSGI 和完整 DI。
