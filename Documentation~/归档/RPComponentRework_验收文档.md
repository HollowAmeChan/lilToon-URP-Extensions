# RPComponentRework：组件边界与验收结论

> 状态：**已收口，契约仍然有效（2026 文档审核后重写）**。本文是 2026-05 RPComponentRework 的收口文档，旧 00-09 分布文档已删除；原验收清单里的 `Ho-MetadataBuffer` 已在 R6／R7 删除（身份归 OB、表面归 SB、合成归 AC），组件边界按当前架构重述。Renderer Feature 清单见 `Ho-管线总览.md` §6，顺序契约见 `Ho-RenderFeatureOrdering.md`。

## 1. 验收结论

概念入口从旧的 `HoAOV` / `HoPost` / `ShoostStack` 迁移为四类职责：

| 职责 | 承担者 |
| --- | --- |
| **语义输入**（可跨 feature 复用，只提供数据） | GeometryBuffer（GB）、ObjectBuffer（OB）、SurfaceBuffer（SB）、AttributeComposite（AC，唯一解压／合成入口） |
| **语义屏幕效果** | ScreenProcess（其余语义屏幕效果）、CharacterSpecialization（角色局部合成）、SubsurfaceScattering、WeightedOIT、GTAO / SSGI |
| **最终图像链** | ImageProcess（只读 camera color / ImageChain） |
| **Lighting / Shadow** | ShadowCast（额外投影光源 atlas 与 receiver 数据） |
| **编排** | DebugTile（只编排 feature-local debug view） |

旧名不允许再作为当前概念名出现：`HoAOV` / `HoAov` / `HoPost` / `Shoost` / `ShoostStack` / `_lilHoAov*` / `HoAOVSSS` / `NeedsAovInput`。静态扫描已确认零命中。

**命名口径**：组件名统一 `Ho-XXX`（**短横线**，如 `Ho-GeometryBuffer`、`Ho-ScreenProcess`）；`Ho` 前缀 + 空格（`Ho GeometryBuffer`）一律不作为组件显示名。

## 2. 组件边界

### ShadowCast

从 URP `visibleLights` 自动收集额外投影灯，**不把 Unity `Light` 的 shadow 开关当收集条件**（附加可见灯即使关了 URP 内置阴影，也由 `Ho-ShadowCast` 自己组织 shadow atlas）；URP main light 仍跳过、交给 URP 内置主光阴影。`Master Shadow Strength` 是总强度，punctual 与 second directional 各自再乘自己的强度项；`Light.shadowStrength` 不参与。负责 visible light 收集、light/caster 的 GameObject layer 与 Rendering Layer 过滤、atlas / block / slice 写入、自己的 debug view。

它**不写**任何语义 buffer。ScreenProcess 若需要 receiver 或 attenuation，必须作为 Lighting/Shadow 资源显式读取，不塞回 buffer。

### 三条生产轴 + AC

GB / OB / SB 是**并列**的三套语义输入，不是彼此的中间结果：GB = 屏幕几何（deferred-style，法线／深度／覆盖率），OB = 身份（跟随材质 pass 与对象语义写入的四层身份 + 覆盖率），SB = 表面数值（五张数值图 + 语义 lane）。

**生产阶段禁止交叉依赖**：GB、OB、SB 互不读取也不反写。AC 是唯一的“解压 typed ID + 同 `SemanticId` 合成”入口，只发布查询门面，不画几何、不写 EXR。消费者（SSS、CharacterSpecialization、ScreenProcess、PLR）可以同时消费多轴，但**结果不再塞回任何基础 buffer**。

`AC` 的语义查询是**遮罩与合成属性的唯一逻辑入口**；`const < surface` 这类映射在本管线里的落地形式就是“AC 查询 vs SB 数值”的分工，不存在 composite RT。

### SubsurfaceScattering

表层色按**通用 base diffuse / albedo** 解释（SB `Color`），材质侧不得把 `_SSSColor` 预混进 albedo，也不得只在 `_UseSSS` 开启时写入——那会把通用 diffuse buffer 变成 SSS 专用中间量。`thickness` / `curvature` / `sssProfileId` / `transmittanceHint` 来自 SB `Classification`；mask 与覆盖率走 AC。SSS 内部降采样／遮罩／扩散链路的纹理只是工作纹理，不构成新的材质语义来源。SSS 不拥有 MaterialBuffer，也不把 composite weight 反写回 buffer；缺少依赖时跳过当前帧并在 Feature Inspector runtime status 暴露缺失项。

### WeightedOIT

只负责透明 accumulation / revealage / composite；不生产语义 buffer，也不拥有 ShadowCast atlas。透明材质若要 ShadowCast receiver，仍走材质 receiver 语义。

### CharacterSpecialization

消费 AC（身份选择、对象语义）与 GB normal / depth；主体／增强轮廓还会读 GB depth 来恢复来源点世界高度。对象语义（Face、FrontHair、Eye、CharacterFull、CharacterBody 等）通过 `Ho-ObjectBuffer Group` 标记。缺少依赖时跳过当前帧并在 runtime status 暴露缺失项。

### ScreenProcess

语义屏幕处理层，按 active layer 聚合真实 buffer 需求：可能读 AC 遮罩／GB normalDepth·depth／显式声明的 ShadowCast 资源；**不默认读** ImageProcess 中间图像、OIT accumulation / revealage、SSS composite weight，以及没有 layer 消费者的未来 channel。缺少当前 layer 需要的项时 shader 可降级执行，但 Volume Inspector 必须显示“需要 / 可用 / 未使用”。

### ImageProcess

最终 image-domain 图像链，只读 camera color / ImageChain；**不读** GB／OB／SB／AC／ShadowCast／SSS／OIT／CharacterSpecialization。需要语义输入的效果必须放到 ScreenProcess。layer 顺序就是执行顺序，不恢复旧 AOV mask / semantic mask / `NeedsAovInput` / AOV composite 入口。

### DebugTile

feature-local declaration + `HoDebugViewRegistry` + 自动 tile。`AllRegistered` 把当前可用的 view 自动排成 tile；它只读取 registry 声明的 RenderGraph 资源并绘制总览，**不接管各 feature 自己的 shader / material / render target**。ScreenProcess 层遮罩不进入 `Ho-DebugTile`（各 layer 有 `debugMask` / `_LayerMaskDebugOutput` 直出）。

## 3. 踩过的坑

1. **资源契约必须三处同时落实**：消费者 `UseTexture` / 明确声明 + 生产者 `SetRenderAttachment`·`SetGlobalTextureAfterPass` + shader 侧真实采样。只改字段名或设计意图会留下**死路径**（曾有 fallback 资源“看起来接好了、实际从未被绑定”）。
2. **同事件顺序只保证 RenderGraph 记录顺序，不保证 GPU 依赖。** 消费者必须显式声明读；若消费者在生产者之前入队，句柄可能在 `RecordRenderGraph` 时无效——应修正 Renderer Feature 列表顺序，而不是把消费者挪到下一个事件（详见 ordering 文档）。
3. **ScreenProcess 之后必须有一个轻量 release / reset pass**：把 GB / Sky 的 shader global 重新绑到 RenderGraph black texture 并清掉 active / valid flag。它的目的**不是销毁 RenderGraph 资源**，而是**截断全局纹理绑定**——否则 ImageProcess、FinalBlit 或后续无语义消费者会在 RenderDoc 里继续持有它们。buffer 禁用或 camera reset 时，公开 global / active 状态也要回到空。
4. **ScreenProcess mask debug 不做 feature 侧独立 debug pass**：mask 本身已有 layer-local 直出（`_LayerMaskDebugOutput`）。“为 debug 再建一套 shader / material / pass”是重复设施；SP 原来的 20 个 rule source 与规则表已作为未使用功能删除，层遮罩保留。
5. **新增 buffer channel 必须先给出命名、编码范围、消费者和 debug 解释，没有真实消费者不默认输出**（当时明确推迟的：GB Motion / Velocity、SB roughness-like / smoothness-like / emission hint、ShadowCast 的 ScreenProcess receiver / attenuation、temporal / wetness / deferred scene / 体积重投影 / motion trail）。
6. **不要把通用输入切成专用中间量**（如 `_SSSColor` 预混 albedo、只在 `_UseSSS` 时写 albedo）：一旦这么做，其它消费者拿到的就不是“表面真值”。
7. **静态扫描不能替代 Unity 实机验收**：旧名零命中、TODO/FIXME 零阻塞项都只是必要条件（当时仓库无 `.sln` / `.csproj`，根本没跑 C# 编译）。

## 4. 实机验收要点（当前形态）

- Renderer Data 能按 `Ho-管线总览.md` §6 的清单添加 feature，Frame Debugger / RenderDoc 中 pass 名称与顺序契约一致。
- 无场景侧控制器时，ShadowCast 仍能从 URP visible lights 生成参与列表；附加灯关闭 URP 内置 shadow 仍被收集，URP main light 仍由 URP 处理。
- GB / OB / SB / AC 能输出几何、身份、表面数值、选择池；缺任一依赖时，SSS / CharacterSpecialization / ScreenProcess 能在 runtime status 里显示缺失项并安全跳过（不得读取上一帧全局，也不得静默使用不一致的相机 RT）。
- ScreenProcess 层遮罩 `debugMask` 能直出采样结果；`Ho-DebugTile` 放在最后时 `AllRegistered` 能显示各 feature 的 tile。
- ImageProcess 不显示 AOV / semantic mask UI，也不读 buffer / ShadowCast 资源。

## 5. 后续边界

以下不是验收阻塞项，只能在有真实消费者后另开任务：GB Motion / Velocity、SB 的 roughness-like / smoothness-like / emission hint、ShadowCast 的 ScreenProcess receiver / attenuation、temporal / wetness / deferred scene / 体积重投影 / motion trail 等扩展能力。
