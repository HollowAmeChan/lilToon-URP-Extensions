# Ho Capsule AO 与 HoAO 附加流程规划

日期：2026-11-25。状态：首版已实现；本文保留设计和审计边界，现行用法与支持范围见 [Capsule AO / Cross](../角色特化/CharacterSpecialization_CapsuleAO.md)。下文完整研究验收清单不等于所有平台与美术场景均已覆盖。

**Capsule AO 归属现有角色特化 RendererFeature，作为已发布 HoAO 的附加计算。只有当前相机存在有效、可写的 HoAO 资源时才执行；它不创建公共 AO 输出，不独立发布有效位，也不直接压暗相机颜色。** 脚部胶囊提供遮挡体，地板接收组件限定参与表面，Capsule／十字模式及外观参数统一放进角色特化 Volume。

HoGTAO 增加默认关闭的“仅发布 AO 纹理”开关：启用时跳过 GTAO 遮蔽计算，发布可写、全白的有效 AO 纹理，供 Capsule AO 等附加项使用。这里的“仅发布”指提供中性的 AO 载体，不是只关闭调试显示或停止相机颜色合成；当前 HoGTAO 正式路径本来就由材质消费纹理。

本规划以 [Rim 研究报告](D:/Unity_Fork/lilToon/docs/ho-rim-zmd-research-report.md) 的模块边界、参数归属与分阶段验收方式为参考。高光、丝绸、清漆和 Rim 不进入本次改动范围。

## 1 已确定的使用形态

| 位置 | 职责 | 用户操作 |
| --- | --- | --- |
| 现有角色特化 Feature | 管理 Capsule AO 的资源、早期附加 Pass 和调试 | 不增加新的 RendererFeature；保留系统开关与必要预算 |
| 角色特化 Volume 堆栈项 | 全局启用、模式、强度、衰减、混合和十字形状 | 在当前相机解析后的 Volume 中统一修改 |
| 脚部胶囊组件 | 提供随骨骼更新的中心、轴、长度、半径 | 挂到脚部并校准几何，不设置独立的效果模式或颜色 |
| 地板接收组件 | 登记同对象的 Renderer | 加上即可登记，不要求填写材质或 AO 参数 |
| 原地板材质 | 继续通过已有实时／混合 AO 设置消费 HoAO | 不新增属性、贴图槽、关键字、Shader Pass 或 Inspector 控件 |

**材质没有新设置，不等于材质关闭 AO 时仍然强制显示。** 最终结果遵守现有 `_AOSource`、AO Mask、实时权重与 AO 响应；Capsule AO 不自动修改材质资产，也不通过 MaterialPropertyBlock 偷改这些值。需要显示效果的地板应已使用现有实时／混合 AO 消费路径。

胶囊几何与效果参数分开。组件可有开箱默认几何和 Gizmo，但鞋子尺寸、脚骨局部轴与偏移仍可能需要校准；全局强度不能代替几何校准。首版采用渲染胶囊组件，不要求物理 Collider，不引入物理碰撞行为。

地板组件默认只登记自身 MeshRenderer。首版支持普通不透明、无顶点位移的静态 mesh；透明、复杂裁剪、蒙皮接收面和自定义顶点形变在扩展验收后再开放。

## 2 ZMD 参考与当前 Ho 实现

### 2.1 ZMD 的脚边 AO

ZMD 当前恢复链使用十个骨骼胶囊，覆盖双腿、双脚、手臂、躯干与头部。局部核沿胶囊轴离散球体，按接收点位置与法线计算遮蔽，累积 Log-SH 后解码为可见度。当前 HoShader 正式适配只将这项结果应用于指定 MenuFloor 平面，不让角色脚消费这一路。

| 参考入口 | 可复用内容 | Ho 首版不继承的限制或资源 |
| --- | --- | --- |
| [骨骼胶囊构建](D:/Unity_Fork/HoShader/ZMD_URP/Runtime/Universal/EndfieldGroundContactOcclusion.cs) | 端点、半径、骨骼运动与影响范围的定义 | 固定十条记录、原骨名、原角色 JSON 与导出坐标修正 |
| [VisibilitySH 核](D:/Unity_Fork/HoShader/ZMD_URP/Runtime/Shaders/VisibilitySH/EndfieldVisibilitySpherePS.hlsl) | 球体近似、法线半球裁切、距离衰减与 Log-SH 思路 | 原常量缓冲布局和屏幕资源别名 |
| [地板解码与接收](D:/Unity_Fork/HoShader/ZMD_URP/Runtime/Shaders/EndfieldGroundContactSH.shader) | SH 解码及接触响应参考 | 专用 MenuFloor、固定平面法线与最终颜色乘法 |
| [当前调度](D:/Unity_Fork/HoShader/ZMD_URP/Runtime/Universal/EndfieldGroundContactOcclusionRendererFeature.cs) | 活动胶囊与逐相机资源的组织 | 每胶囊半分辨率全屏绘制、捕获材质窗口、D3D11 单一支持域 |

当前移植的地板限定不证明原游戏的所有材质消费者都没有胶囊遮蔽。Ho 首版明确只处理登记地板，角色自接收不在范围内。

### 2.2 Ho 已有接口

- [HoGTAO](../../Runtime/GTAO/HoGTAORendererFeature.cs) 在不透明绘制前生成并发布 `_HoAOTexture`，`_HoAOAvailable` 表示本相机存在有效输出。
- [帧内资源](../../Runtime/GTAO/HoGTAORenderGraphResources.cs) 已保存 `aoTexture`，SSGI 等消费者直接借用该句柄；不能只更换 Shader 全局绑定而让这些消费者继续读取旧资源。
- [材质 AO](D:/Unity_Fork/lilToon/Assets/lilToon/Shader/Includes/lil_common_frag.hlsl:405) 已统一组合烘焙与实时可见度，再用于 Toon 明暗和 AO 压暗，无需增加 Capsule 专用材质输入。
- [角色特化 Volume](../../Runtime/CharacterSpecialization/HoCharacterSpecializationEffects.cs) 已有整块效果参数容器。当前角色颜色合成默认在透明之后运行，新增 HoAO 附加计算必须安排独立的早期 Pass。

## 3 HoAO 所有权与启用条件

### 3.1 发布者与附加项分工

发布者负责分配、清理、发布纹理与有效位；Capsule AO 只借用当前发布者明确提供的可写输出，在该资源上追加结果。首版沿用现有帧内 AO 句柄并补齐必要的可写性、语义与相机信息，不预先搭建多生产者注册系统。

Capsule AO 不调用 `SetGlobalTexture` 或 `SetGlobalTextureAfterPass` 重新发布 `_HoAOTexture`，不设置 `_HoAOAvailable`，不替换帧内 `aoTexture`，不释放发布者的纹理。后续其它 HoAO 发布者接入时应遵守同一借用合同，而不是要求 Capsule AO 强制开启 GTAO 算法。

“不主动发布”允许修改已获授权的目标内容；不允许缺少发布者时自己补一张白图、改走颜色叠加或读取上一相机的结果。

### 3.2 必须同时满足的条件

1. 当前角色特化 Volume 启用 Capsule AO。
2. 当前相机的帧内记录提供有效的 HoAO 可见度句柄，且允许颜色附件读写与所选混合。
3. AO 与当前 viewport、尺寸和采样方式匹配；不是默认白纹理、深度／法线调试图或上一帧资源。
4. GB 提供同相机的有效物理深度与法线，足以验证接收面可见性。
5. 至少存在一组影响范围与接收面相交的有效胶囊和 Receiver。

任一条件不满足则跳过本项，保留已有 HoAO 内容。没有发布者时不创建 Capsule 中间纹理、不录制接收绘制；诊断报告实际缺失条件。

录制 RenderGraph 时以帧内资源记录判断，不能通过 CPU 读取尚未执行的 Shader 全局有效位来判断 GPU 状态。Compatibility 路径使用同相机、同次渲染的发布记录和借用 RTHandle，不能依赖全局纹理名称猜测所有权。

## 4 HoGTAO 仅发布 AO 纹理模式

| HoGTAO 状态 | 公共 AO 输出 | 计算与资源 | Capsule AO 行为 |
| --- | --- | --- | --- |
| 关闭／未执行 | 无有效发布 | 无新增输出 | 跳过，不接管发布 |
| 正常计算 | GTAO 可见度 | 保留现有追踪及所选滤波 | 在已有结果上附加 |
| 仅发布 AO 纹理 | 每次渲染清为 1 的有效可写纹理 | 只保留输出分配／清理／发布；跳过 GTAO 计算链 | 可独立贡献胶囊或十字遮蔽 |

“仅发布”开关建议命名 `publishAOTextureOnly`，在 Settings 和 HoGTAO Volume 暴露，默认 false。现有总开关为 false 时仍不发布，不能由此开关绕过总关闭。

模式生效时，应在资源准备与输入请求之前分流，跳过深度金字塔、Bitmask 追踪、Motion 请求、时间累积、空间滤波及相关历史分配。不能只在最后把算好的 GTAO 替换成白图，或继续创建全部材质与历史后声称低成本。AO 载体本身仍有分配、清理与后续 Capsule 的成本。

输出采用现有公共 AO 的尺寸和采样合同：全相机尺寸、单采样、线性可见度、R 为 0 到 1、1 为无遮挡。必须是当前相机实际可写的颜色纹理；共享 `Texture2D.whiteTexture` 只适合无效回退，不能作为附加目标。具体格式保持公共合同，在原型中验证 Blend 能力，首版不同时进行无关的 AO 格式重构。

正常计算与仅发布模式来回切换时，旧 GTAO 历史失效；重新计算不能复用进入仅发布模式前的历史。Capsule 修改最终输出，不写回 GTAO 的追踪／时间历史，避免脚影被反馈累积。

仅发布不因自身要求 GB、Color 或 Motion 输入；Capsule 作为消费者仍要求 GB。调试关闭时不创建调试资源。仅发布模式不触发昂贵的 Motion／Temporal 调试计算；AO 视图可以显示附加后的最终输出，其它视图标明当前模式无对应产品。

## 5 调度与 RenderGraph 接入

```text
同相机 BeforeRenderingOpaques
  GeometryBuffer 发布物理几何
       ↓
  HoGTAO 正常计算或仅发布白色 HoAO
       ↓
  CharacterSpecialization 的 Capsule AO 附加 Pass
    校验借用资源 → 收集胶囊与 Receiver → 修改同一 HoAO
       ↓
  不透明材质读取现有 HoAO

后续阶段
  SSGI／AO 调试等读取同一份附加后的资源
  原角色特化颜色效果按原阶段执行
```

新增 Pass 固定在不透明之前，不沿用角色特化颜色合成的 `AfterRenderingTransparents`。同事件顺序通过 Renderer Data 列表表达：GB → HoGTAO → 角色特化；不采用 `250+1` 一类隐藏偏移。遵守现有 [顺序契约](../Ho-RenderFeatureOrdering.md)。

Volume 堆栈中增加 Capsule AO 的配置入口，不表示它可以拖到地板着色之后才写 AO。它也不进入现有颜色合成循环重复乘一次；UI 中应明确它属于前置 AO 附加阶段。

Capsule 启用条件与眼透、前发投影、脸捕获及其它角色特化效果分开。只开 Capsule 时，不得为了共用 Feature 而拉起眼睛捕获、AC 位平面、方向缓冲或其它无关颜色中间图。

目标 AO 附件必须声明 `ReadWrite` 并保留原内容；GB 和必要的接收输入显式声明读取。后续材质全局纹理依赖、SSGI 的 typed handle 读取与最后写入建立真实 GPU 顺序。借用原句柄但读到附加前版本属于验收失败，不能靠外观接近认定依赖已正确。

## 6 接收面与几何计算

### 6.1 只计算登记表面

Receiver 登记具体 Renderer，组件关闭、对象失活或销毁后移除。运行时不扫描全场景材质、不构建地板复制片、不克隆或修改原材质。

CPU 按胶囊扩大后的影响包围盒与 Receiver bounds 筛选候选，并去重 Renderer／submesh；GPU 在对应地板覆盖内求接收点 P 和法线 N，验证与 GB 获胜表面一致后计算遮蔽。角色或前景挡住的地板不得将 Capsule AO 写到前景所在像素。

不透明之前不能假定主相机深度附件已经包含完整角色。可见性依据 GB 的当前物理深度，使用匹配的 raw／linear 深度定义和像素映射；不能仅把接收 mesh 画到一个空深度附件就认定已排除前景。

GB 法线可能包含法线贴图，不能称为无扰动三角面法线。接收法线选择和背面处理在原型中核对；不为本项额外复制整套 GBuffer，也不读取描边视觉壳作为 AO 几何。

### 6.2 接收覆盖只附加一次

普通单层地板优先验证接收 mesh 直接写 HoAO 的最小路径。每个接收片元应汇总适用胶囊得到一个 C，再与原 G 合成一次，不能将每个胶囊各乘一次原 GTAO 来冒充合并遮蔽。

重复登记、重叠 Receiver 和 submesh 交叠必须纳入测试。若直接重绘不能保证同一可见接收像素只作用一次，应先用私有接收覆盖／深度合并覆盖，再一次附加到原 AO。此类内部资源只在必要时创建，不成为第二个公共 AO 输出；Multiply 模式不允许默许重复绘制导致额外变暗。

### 6.3 性能边界

胶囊以低复杂度遮挡体替代角色三角形查询，不意味着接收像素免费。优化优先级为有效条件早退、局部 bounds 筛选、候选胶囊列表和投影区域限制，再根据实测决定是否需要分块。

相机剔除使用扩大后的影响范围，不能只按脚本体是否在视锥内剔除，否则离屏脚产生的可见接触影会消失。十字模式的包围盒必须覆盖全部暗带臂长和软边。

首版不预先引入全场景胶囊 cluster、角色全身自动拟合或额外 AO 时间历史。CPU、接收绘制与 GPU 局部核的成本分开统计，不只报告附加 Pass 的绘制次数。

## 7 两种遮蔽模式与混合

### 7.1 Capsule 模式

以 ZMD 已恢复核为画面参考：由胶囊端点、半径、P/N 与影响距离求局部遮蔽，多个胶囊按选定核合并后输出标量可见度。算法实现应独立为 HLSL 核，不绑定原常量缓冲、原角色记录或 MenuFloor Shader。

原型比较 Log-SH 核与必要的简化方案，核对接触形状、抬脚变化、斜面连续性和成本，再冻结采样与查表规格。所需查表建立自己的可复现生成入口，不把原游戏私有 payload 作为功能安装前提。简化方案按自己的效果验收，不以“同为胶囊 AO”认定与原式等价。

### 7.2 Cross 模式

十字明确为地面上两条交叉柔软暗带。它是世界空间的风格化接触遮蔽，不是两盏方向光的投影，也不要求通过交叉实体胶囊自然生成形状。

中心跟随脚部胶囊，方向由胶囊／脚的参考轴和全局旋转决定；在接收面上计算两道有限长度的扁平软带。每道沿长度向两头连续渐隐，横向独立羽化，不使用圆头等强胶囊条带。臂长、宽度、夹角与软度由 Volume 统一控制，离接收面越远按高度范围衰减；相机旋转不应使图案跟着屏幕旋转。

```text
Ocross = max(Band1, Band2) × HeightFade × Strength
Ccross = 1 − saturate(Ocross)
```

默认 `max` 避免同一十字中心因两条带重合而重复加深。两只脚产生的多个十字也先汇总为单个 Capsule 可见度，再与 HoAO 合成。台阶、斜面、附近墙面和脚穿地时需明确单侧作用与衰减，禁止不受限地向上下表面投射。

### 7.3 与已有 HoAO 混合

G 和 C 均为线性可见度：1 为无遮挡，0 为完全遮挡。Volume 默认 Min，可选 Multiply。

```text
Cw = lerp(1, C, weight)
Min       Gfinal = min(G, Cw)
Multiply  Gfinal = G × Cw
```

正常 GTAO 模式在已有遮蔽上追加；仅发布模式 G=1，结果自然等于 Cw。Capsule 关闭或无有效接收时保留原 G，不额外清白。

优先使用固定功能混合，在同一 AO 附件上完成 Min／Multiply；Shader 不得同时把该 AO 目标作为 SRV 采样和 RTV 写入。若格式或执行路径不支持所需混合，保留原 AO 并报告原因，不能偷偷替换公共纹理或改用相机颜色叠加。若需要中间结果，只保存局部 C／接收覆盖，最终仍写入发布者原目标。

本项输出标量 AO，因此不提供 Capsule 独立 RGB 染色。压暗颜色和材质响应继续由已有 AO 设置决定；Volume 只控制可见度与形状，不增加与现有 AO 颜色并行的通道。

## 8 参数归属

| 位置 | 参数 | 约定 |
| --- | --- | --- |
| 角色特化 Volume | Capsule AO 启用 | 默认关闭，旧 Profile 不改变画面 |
| 角色特化 Volume | 模式 | Capsule／Cross，全体胶囊统一切换 |
| 角色特化 Volume | 强度与影响／高度衰减距离 | 世界单位与无量纲强度分开标注 |
| 角色特化 Volume | HoAO 混合 | Min 默认，Multiply 可选 |
| 角色特化 Volume | 十字臂长、宽度、夹角、旋转和软度 | Cross 模式显示；无逐脚外观覆盖 |
| 角色特化 Volume | 相机胶囊预算 | 默认 32，上限 64；不另建逐组件外观参数 |
| 角色特化 Feature Inspector | 运行状态与计数 | 展示跳过原因与活动胶囊／接收面数 |
| 胶囊组件 | 中心、轴、半径、长度 | 只描述几何，随 Transform 更新；显式处理缩放规则 |
| 地板接收组件 | 自身 Renderer 登记 | 默认无效果参数；不保存材质接收强度 |
| HoGTAO Settings／Volume | 仅发布 AO 纹理 | 默认关闭，沿用现有 Volume 覆盖规则 |

组件关闭仅移除该几何输入或接收声明，不改变同角色其它组件或 Volume。负缩放、非均匀缩放和无效半径的解释在几何阶段冻结，不以任意 clamp 掩盖失效记录。

## 9 预计改动位置与审计边界

下表为本次实施与审计范围。路径相对于本仓库；lilToon 文件只作既有消费链核对，不在本任务修改。

| 位置 | 预计改动 | 审计重点 |
| --- | --- | --- |
| `Runtime/GTAO/HoGTAOSettings.cs`、`HoGTAOVolume.cs` 及对应 Editor | 增加仅发布开关和条件 UI | 默认行为、Volume 覆盖、无重复参数来源 |
| `Runtime/GTAO/HoGTAORendererFeature.cs` | 早期模式分流、白色输出、计算历史切换 | 仅发布真实跳过追踪和 Motion；发布权不变 |
| `Runtime/GTAO/HoGTAORenderGraphResources.cs` 及对应 Compatibility 记录 | 暴露可借用写入的同相机 AO 资源信息 | 可见度语义、写入能力、SSGI 与 Debug 句柄一致 |
| `Runtime/CharacterSpecialization/HoCharacterSpecializationRendererFeature.cs` | 管理独立的 Capsule AO 早期 Pass | 不拉起眼／脸捕获；不改变原颜色效果时序 |
| `Runtime/CharacterSpecialization/HoCharacterSpecializationEffects.cs`、Settings、Volume 与 Editor | 增加全局效果入口与模式参数 | 整容器 override、旧 Profile 默认关闭、阶段说明 |
| `Runtime/CharacterSpecialization/CapsuleAO/` | 胶囊与 Receiver 组件、登记和局部候选准备 | 无全场景扫描、去重、停用／销毁生命周期 |
| `Runtime/CharacterSpecialization/Resources/HoCapsuleAO.shader` | 胶囊核、十字核、接收面与 HoAO 混合 | 可见表面、单次附加、无 SRV/RTV 反馈；Resources 保留内部 Shader 引用 |
| SSGI、AO Debug、lilToon 现有 AO 读取 | 核对附加后的同一输出；只在真实依赖缺失时修接线 | 不新增 Capsule 材质字段、不改变 AO 响应公式 |

不改普通地板 Shader 资产，不新增公共 Capsule AO 纹理／有效位，不新增独立 RendererFeature，不把本次工作扩展到高光、Rim、RTDI／RTGI 或通用后处理架构。

## 10 实施顺序与验收

1. **白色 HoAO 发布原型**：正常、关闭、仅发布三态；后者输出可写白值且追踪／Motion／历史 Pass 缺席。检查默认行为、模式切换与多相机隔离。
2. **附加合同原型**：不接完整胶囊核，先在指定小接收区写已知 C。原输入 G=.7、C=.6 时 Min=.6、Multiply=.42；目标句柄和发布者保持一致。无发布者时完全不绘制。
3. **接收可见性**：单地板、双地板、前景方块、角色脚、斜面、台阶、重复登记和接收面交叠；对照 GB 深度，保证未登记或被挡住的表面不受影响。
4. **Capsule 核**：双脚静止、抬脚、交叉腿、角色运动与离屏影响；比较核参考与实际接收调用，不只检查函数自己与自己一致。
5. **Cross 核**：十字／X、世界空间方向、交叉中心、两脚交叠、抬脚衰减、软边与包围盒；先比较形状，再评估最终材质画面。
6. **Volume 与角色特化接入**：全局模式切换、组件无外观参数、仅开 Capsule 不触发其它角色效果；旧 Profile 与 Capsule 关闭时恢复原 AO。
7. **消费与执行路径**：lilToon 实时／混合／关闭 AO、SSGI、AO Debug、D3D11／D3D12、RG／Compatibility、多相机、透视／正交及受支持的 MSAA／尺寸变化。每种支持组合分别给结果，未通过的不宣称支持。
8. **成本与实物验收**：分别测仅发布、正常 GTAO、Capsule、Cross 的 GPU 时间、CPU 收集和峰值资源；覆盖多个角色和大地板，保留同镜头同姿态的开关 A/B。无新增输入时不扩大或重复测试。

每阶段保留独立结果与失败条件。原型通过后再更新正式 HoGTAO、角色特化和通道文档；本规划不把源码存在或 Pass 被录制当成效果与性能已验收。

## 11 来源与关联文档

### 当前实施记录

| 阶段 | 结果 | 验证范围 |
| --- | --- | --- |
| 规划基线 | `ffa6cd1e` | 文档与索引 |
| HoGTAO 仅发布与借用合同 | `601990c3` | 隔离 Unity 6000.3.15f1，16 项 EditMode 测试通过；白色输出不依赖 GB／Color／Motion |
| Capsule／Cross 与接收覆盖 | `bd170148` | 私有接收覆盖先合并、同一公共 AO 原地 Min／Multiply，内部 Shader 无目标 SRV 反馈 |
| 扩展验证与收口 | `40f92880`；[验证记录](../../Tests~/CapsuleAO/README.md) | D3D11 / D3D12 各 165/165；实际 URP、lilToon 消费、MSAA、静态合批、Volume 和多相机；Cross 已按反馈改为双头渐隐软带 |

当前 Capsule 核选择最近球的投影盘遮蔽近似，多个胶囊取最大遮蔽后与原 AO 合成；没有采用 ZMD Log-SH 查表链，也不宣称原式等价。Cross 采用两条有限软带。两者均无需原游戏资源。首轮整链测试的 Volume 容器 override 设置遗漏已在测试构造中修正，原失败报告与通过报告分别保留。

- [HoGTAO 现行说明](../Ho-GTAO.md)
- [GeometryBuffer 公共契约](../Ho-GeometryBuffer.md)
- [RendererFeature 顺序契约](../Ho-RenderFeatureOrdering.md)
- [角色特化资源与调度](../角色特化/Ho-CharacterSpecialization_RDG优化.md)
- [ZMD 当前 Feature 接入边界](D:/Unity_Fork/HoShader/ZMD_URP/Documentation~/FeatureIntegration.md)
- [ZMD 地板 AO 与其它 Feature 审核](D:/Unity_Fork/HoShader/ZMD_URP/Documentation~/TechnicalAudit/03-RenderFeatures.md)
- [Rim 研究与模块规划参考](D:/Unity_Fork/lilToon/docs/ho-rim-zmd-research-report.md)
