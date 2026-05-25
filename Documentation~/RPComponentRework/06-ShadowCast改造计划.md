# ShadowCast 改造计划

> 本文约束旧仓库 `Runtime/ShadowCast` 的后续改造。ShadowCast 是独立的 Lighting/Shadow 组分，不属于 MaterialBuffer、GeometryBuffer、ScreenProcess 或 ImageProcess。

---

## 0. 命名与归属

当前旧名：

```text
HoShadowCast
HoShadowCastController
HoShadowCastRendererFeature
```

迁移期可继续使用旧类名，但文档语义上按：

```text
ShadowCast
```

理解。

ShadowCast 负责：

- 收集少量项目级额外投影光源。
- 生成独立 shadow atlas。
- 发布 receiver 采样所需的 light / slice / matrix / PCSS 参数。
- 提供 atlas debug。

ShadowCast 不负责：

- 写入 MaterialBuffer。
- 写入 GeometryBuffer。
- 生成对象/材质语义。
- 作为 ScreenProcess 的默认遮罩。
- 作为 ImageProcess 的图像效果。

---

## 1. 当前事实

旧仓库已存在：

- `HoShadowCastController`：场景级控制器，显式维护额外方向光、聚光、点光列表。
- `HoShadowCastRendererFeature`：安装 atlas pass 与 debug pass。
- `HoShadowCastSampling.hlsl`：材质 receiver 采样入口。
- `Shaders/Debug/HoShadowCastDebug.shader`：atlas debug。
- RenderGraph 路径：使用 `TextureHandle` 和 `RecordRenderGraph()`。
- compatibility path：仍有 `RTHandle` 和非 RG 逻辑。

当前能力：

- 普通 point / spot / directional atlas。
- 第二方向光级联 atlas。
- PCSS soft shadow。
- atlas tile clamp。
- receiver 侧 manual compare / PCSS。
- controller 自动校验数组长度和参数范围。

其中 controller 是旧实现事实，不是后续用户工作流目标。
后续用户只需要在 Renderer Data 里添加 ShadowCast RendererFeature，Feature 根据 URP `visibleLights`、layer mask、light layer、shadow 参数和容量预算自动生成参与列表。

---

## 2. 改造目标

ShadowCast 后续目标不是变成全局阴影系统，而是保持“少量额外强制投影光源”的独立组分。

目标：

- RDG-first。
- atlas 生命周期由 RenderGraph 管理。
- 灯光收集、atlas pack、receiver ABI、debug 分开。
- debug shader 按需启用。
- receiver 参数命名稳定，避免继续膨胀全局状态。
- 不污染 MaterialBuffer / GeometryBuffer。
- 去组件化：普通用户不需要在场景里创建 `HoShadowCastController`。
- RendererFeature Inspector 显示运行时参与灯光列表、跳过原因、slice 范围和 atlas 状态。

---

## 3. 组分拆分

建议把现有大文件逐步拆成局部组件：

```text
Runtime/ShadowCast/
├── ShadowCastRendererFeature.cs
├── ShadowCastSettings.cs
├── ShadowCastFrameCollector.cs
├── ShadowCastAtlasPacker.cs
├── ShadowCastResource.cs
├── ShadowCastPublisher.cs
├── ShadowCastReceiverABI.cs
├── ShadowCastDebugViewInfo.cs
├── Editor/
│   └── Debug/
│       └── ShadowCastDebugShaderCollector.cs
└── Shaders/
    ├── ShadowCastSampling.hlsl
    └── Debug/
        └── ShadowCastDebug.shader
```

旧类名可以逐步迁移，不要求一次性重命名。`ShadowCastController` 如果保留，只能作为高级 override 或兼容桥，不再是普通使用必须添加的组件。

---

## 4. 灯光收集

ShadowCast 的主工作流应由 RendererFeature 自动收集灯光。
旧 `HoShadowCastController` 的资产价值在于收集、排序、容量预算、参数 clamp 和调试报告思路，而不是“必须在场景里放一个控制器”。

保留：

- light priority / stable sort。
- 额外方向光 / 点光 / 聚光的参与规则。
- 跳过 URP main light。
- 参数 clamp。
- editor 里显示 atlas/slice 预算与跳过原因。

改造：

- RendererFeature 直接从当前 camera 的 URP `visibleLights` 收集候选灯光。
- 默认只收集符合 ShadowCast 条件的可见 light，条件包括 enabled、类型、layer mask、light layer、shadow 开关、容量预算。
- 手动灯光列表如保留，只放在 RendererFeature 的高级折叠区，作为补充入口，不作为主流程。
- 把 `BuildFrameData` 从 RendererFeature 大文件拆成 `ShadowCastFrameCollector`。
- 收集结果输出为 frame-local struct，不直接写全局 shader state。
- 收集结果要能被 debug 输出为文本摘要。
- 无有效灯光时必须记录 reset pass，清空 receiver active 状态。

禁止：

- 要求用户创建场景控制器后 ShadowCast 才能工作。
- 从全场景所有 light 做不受 camera visibility / layer / capacity 约束的扫描。
- 依赖 layer/tag/material 名字隐式决定 receiver。
- 把角色语义或对象语义放到 ShadowCast controller。

---

## 5. Atlas 与 RDG

Shadow atlas 是 Lighting/Shadow 资源，不进入 MaterialBuffer/GeometryBuffer。

RDG 规则：

- 普通 atlas 和第二方向光 atlas 都必须是 RenderGraph texture。
- atlas pass 明确写 depth attachment。
- publish pass 显式发布 receiver 需要的 global texture / constant arrays。
- 无灯光时显式 reset global state。
- debug pass 只在 debug 开启时读取 atlas。

长期应删除或冻结 compatibility path 对主结构的影响。

允许的资源：

```text
ShadowCast.Atlas
ShadowCast.SecondDirectionalAtlas
ShadowCast.LightData
ShadowCast.SliceData
ShadowCast.WorldToShadow
ShadowCast.PcssParams
```

这些是 ShadowCast 组分资源，不是公共 MaterialBuffer/GeometryBuffer 通道。

---

## 6. Receiver ABI

当前 receiver 主要通过材质 forward include 调用：

```hlsl
HoShadowCastAttenuation(positionWS)
```

这条路径短期保留，因为角色 forward 渲染仍然是主线。

后续 receiver ABI 应明确：

- 输入：`positionWS`。
- 可选输入：normalWS、material class、receiver flags。
- 输出：shadow attenuation 或分方向/分光源 attenuation。
- 不要求读取 HoAOV。
- 不要求 ScreenProcess 才能接收阴影。

未来可选路线：

### 材质 Forward Receiver

默认路线。lilToon/lilPBR/未来 HoNpr 在 forward shading 中采样 ShadowCast atlas。

优点：

- 和现有材质接入一致。
- 可在光照计算中自然混合。
- 不依赖 screen-space reconstruction。

### ScreenProcess Receiver

可选路线。用于某些全屏风格化阴影、接触阴影或角色/场景后置合成。

要求：

- 必须声明读取 GeometryBuffer.Depth / Normal。
- 必须声明读取 ShadowCast.Atlas。
- 只作为 ScreenProcess effect，不反向要求 ShadowCast 写 MaterialBuffer。

---

## 7. 与 MaterialBuffer / GeometryBuffer 的关系

ShadowCast 可以消费 GeometryBuffer，但不生产它。

允许：

- ScreenProcess receiver 读取 `GeometryBuffer.Depth/Normal` 重建 position 或做 receiver gate。
- 材质 receiver 读取材质自身语义决定 shadow strength。
- 未来 MaterialBuffer 的 `MaterialClass` / `FeatureFlags` 作为 receiver gate。

不允许：

- 把 shadow attenuation 写进 MaterialBuffer。
- 把 shadow atlas 写进 HoAOV。
- 把 receiver mask 作为 ShadowCast 的隐式对象分类系统。
- 为 ShadowCast 单独输出 overall mask。

如果未来需要 `ShadowCast.Attenuation` 作为跨系统复用结果，应作为 Lighting/Shadow 资源单独声明，不进入 MaterialBuffer/GeometryBuffer。

---

## 8. Debug 策略

ShadowCast debug 属于 ShadowCast feature 局部。
debug shader、debug view 描述、tile 短命名和 shader 收集器都放在 `Runtime/ShadowCast` 或 `Editor/ShadowCast` 下。
公共 Debug UI 只读取 `ShadowCastDebugViewInfo` 一类局部描述，不拥有 `ShadowCastDebug.shader`。

Debug view：

```text
ShadowAtlas
SecondDirectionalAtlas
SliceLayout
LightList
ReceiverAttenuation
```

第一批保留：

- Atlas
- SecondDirectionalAtlas

后续可加：

- tile view 显示 slice 短名。
- light list overlay。
- receiver attenuation heatmap。

重 debug shader 必须按需启用：

- `HoShadowCastDebug.shader` 已移入 `Runtime/ShadowCast/Shaders/Debug/`。
- `ShadowCastDebugShaderCollector` 放在 `Editor/ShadowCast/Debug/`。
- RendererFeature 不在 debug off 时查找 debug shader。
- 缺失 debug shader 不影响 ShadowCast 主功能。
- debug shader collection 由用户显式生成或启用。

---

## 9. 与 ImageProcess / ScreenProcess 的边界

ShadowCast 不属于 ImageProcess。

ImageProcess 不能采样 ShadowCast atlas、attenuation 或 light data。
如果某个图像效果需要 ShadowCast 输入，它应迁到 ScreenProcess。

ShadowCast 与 ScreenProcess 的关系：

- ScreenProcess 可以消费 ShadowCast 输出做后置阴影/遮罩。
- ScreenProcess 必须显式声明读取 ShadowCast 资源和 GeometryBuffer 资源。
- ShadowCast 不为 ScreenProcess 生成私有中间 RT。

---

## 10. 执行顺序

建议执行：

1. 文档和 UI 中把 HoShadowCast 归入 `ShadowCast` 组分。
2. 把普通用户工作流改为 RendererFeature 自动收集 visible lights。
3. 把旧 `HoShadowCastController` 降级为高级 override / legacy bridge，或逐步删除。
4. 拆 `HoShadowCastRendererFeature.cs`：collector / atlas packer / publish / debug。
5. 让 debug shader lazy-load，只在 debug 开启时查找。
6. 明确普通 atlas 与第二方向光 atlas 的 RDG resource owner。
7. 清理 compatibility path 对主结构的影响。
8. 固定 receiver ABI 文档。
9. 再考虑 ScreenProcess receiver 原型。

---

## 10.1 2026-05-24 执行记录

已落地 ShadowCast 自动收集第一阶段：

- `HoShadowCastSettings` 增加 RendererFeature 侧主工作流配置，包含 `collectVisibleLights`、`useActiveControllerOverride`、caster layer、atlas、PCSS、第二方向光级联和 debug mode。
- 新增 frame-local `HoShadowCastFrameConfig`，统一承载 RendererFeature 设置与旧 `HoShadowCastController` 覆盖数据。
- `HoShadowCastRendererFeature` 不再因为没有 `HoShadowCastController.ActiveController` 而跳过入队；默认可从当前 camera 的 URP `visibleLights` 自动收集符合条件的 directional / spot / point light。
- 自动收集路径跳过 URP main light，只接收当前 camera 可见、类型匹配、启用且 `Light.shadows != None` 的灯光。
- 旧 `HoShadowCastController` 仍可通过 `useActiveControllerOverride` 作为 legacy manual light list 覆盖路径；启用覆盖时保持旧的手动列表和参数语义。
- compatibility path 与 RenderGraph path 都改为消费同一个 frame config，debug pass 也改为读取 config 的 debug mode，不再直接读取 active controller。
- RendererFeature Inspector 改为显示自动收集、legacy override、atlas、PCSS、第二方向光和 debug 配置；没有 controller 时不再提示功能不可用。

仍待处理：

- `HoShadowCastRendererFeature.cs` 仍是大文件，collector / atlas packer / publish / debug 尚未拆成独立文件。
- 自动收集的 light layer / shadow layer 规则还未细化，当前第一阶段主要依赖 URP visible lights、main light skip、`Light.shadows` 与 caster layer mask。

## 10.2 2026-05-24 执行记录

已继续落地 ShadowCast 运行时诊断与 Inspector 只读状态：

- 新增 `HoShadowCastRuntimeDiagnostics`，把当前帧 ShadowCast 收集结果从 `HoShadowCastRendererFeature.cs` 拆到独立只读诊断出口。
- compatibility path 与 RenderGraph path 都会发布最近一次 frame snapshot，记录执行路径、camera、来源、visible light 数、candidate 数、跳过数、atlas light/slice 数和第二方向光级联状态。
- punctual atlas 收集会记录参与灯光、slice 起点、slice 数、resolution，并记录跳过原因，例如无阴影、容量限制、atlas 已满、构建 shadow slice 失败、重复灯光或 URP main light。
- 第二方向光级联收集会记录参与方向光、cascade slice 起点和 resolution，并保持自动 visible light 路径只接收 `Light.shadows != None` 的方向光；legacy controller/manual path 仍保留旧语义。
- `HoShadowCastRendererFeatureEditor` 新增 Runtime 只读区块，可在 Inspector 中查看最近帧来源、候选数量、参与灯光、跳过原因、punctual atlas 和 second directional atlas 状态。
- 这批改造不改变 receiver ABI，不让 ShadowCast 写入 MaterialBuffer/GeometryBuffer，也不让 ImageProcess 消费 ShadowCast。

仍待处理：

- `HoShadowCastRendererFeature.cs` 仍是大文件，collector / atlas packer / publish / debug 尚未拆成独立文件。
- 自动收集的 light layer / shadow layer 规则还未细化，当前第一阶段主要依赖 URP visible lights、main light skip、`Light.shadows` 与 caster layer mask。

## 10.3 2026-05-24 执行记录

已继续拆分 ShadowCast debug ownership：

- `HoShadowCastDebugPass` 从 `HoShadowCastRendererFeature.cs` 拆到 `Runtime/ShadowCast/HoShadowCastDebugPass.cs`，Debug pass 的 camera color copy、atlas 选择、RenderGraph 输出和 compatibility path 逻辑不再挤在 RendererFeature 大文件中。
- `HoShadowCastRendererFeature` 仍负责 debug 开关、按需 `Shader.Find(HoShadowCastShaderConstants.DebugShaderName)` 和 debug material 生命周期；debug 关闭时会释放 debug material。
- debug shader 缺失时只 warning once，不再入队空 debug pass，ShadowCast 主 atlas / publish 路径不受影响。
- 这次拆分不改变 atlas、receiver ABI、MaterialBuffer/GeometryBuffer 边界，也不改变 ImageProcess 禁止消费 ShadowCast 的规则。

仍待处理：

- `HoShadowCastRendererFeature.cs` 仍需继续拆 collector / atlas packer / publish。
- 自动收集的 light layer / shadow layer 规则还未细化，当前仍主要依赖 URP visible lights、main light skip、`Light.shadows` 与 caster layer mask。

## 10.4 2026-05-24 执行记录

已继续拆分 ShadowCast 主文件里的配置与 frame/atlas 数据边界：

- `HoShadowCastSettings` 与 `HoShadowCastFrameConfig` 从 `HoShadowCastRendererFeature.cs` 拆到 `Runtime/ShadowCast/HoShadowCastSettings.cs`，保持现有序列化字段、active controller override 语义和 frame-local config 解析方式不变。
- `HoShadowCastRenderTargets`、`HoShadowCastFrame`、`HoShadowCastSecondDirectionalFrame`、`ShadowSliceInfo` 与 `HoShadowCastAtlasPacker` 从主文件拆到 `Runtime/ShadowCast/HoShadowCastFrameData.cs`。
- 新增 `HoShadowCastAtlasDescriptors`，把普通 atlas 与 second directional atlas 的 depth RenderTextureDescriptor 创建从 pass 中移出；RenderGraph 与 compatibility path 继续使用同一套 descriptor。
- `HoShadowCastRendererFeature.cs` 现在只保留 RendererFeature 壳、主 ShadowCast pass 和暂未拆出的 frame collector / publish helper；本次不改变 atlas 写入、receiver ABI、debug 按需加载、MaterialBuffer/GeometryBuffer 边界或 ImageProcess 禁止消费 ShadowCast 的规则。

仍待处理：

- 继续把 `BuildFrameData` / `BuildSecondDirectionalFrameData` 及其 light collection helper 拆到 `HoShadowCastFrameCollector`。
- 继续把 `ApplyGlobalData` / reset / receiver global 发布拆到 `HoShadowCastPublisher`。
- 自动收集的 light layer / shadow layer 规则还未细化，当前仍主要依赖 URP visible lights、main light skip、`Light.shadows` 与 caster layer mask。

## 10.5 2026-05-25 执行记录

已继续拆分 ShadowCast receiver global 发布边界：

- 新增 `HoShadowCastPublisher`，集中维护 punctual atlas、second directional atlas 的 shader global array 缓存、receiver ABI 写入和空状态 reset。
- `HoShadowCastRendererFeature.ResetShadowCastState()` 改为调用 publisher 的统一 immediate reset，避免 RendererFeature 壳继续直接维护 receiver global 细节。
- compatibility path 与 RenderGraph path 的 `ApplyGlobalData` / `ApplySecondDirectionalGlobalData` 调用都改为通过 `HoShadowCastPublisher` 发布，主 pass 只保留 atlas 渲染、slice 绘制、camera global restore 和入队逻辑。
- 本次只移动发布代码，不改变 atlas 写入、light collection、debug 按需加载、receiver ABI、MaterialBuffer/GeometryBuffer 边界或 ImageProcess 禁止消费 ShadowCast 的规则。

仍待处理：

- 继续把 `BuildFrameData` / `BuildSecondDirectionalFrameData` 及其 light collection helper 拆到 `HoShadowCastFrameCollector`。
- 自动收集的 light layer / shadow layer 规则还未细化，当前仍主要依赖 URP visible lights、main light skip、`Light.shadows` 与 caster layer mask。

## 10.6 2026-05-25 执行记录

已继续沿 ShadowCast 自动灯光收集线推进，并参考 `D:\Unity_Fork\HoUrp-Extensions` 的 visible light 收集边界：

- 新增 `HoShadowCastFrameCollector`，把 `BuildFrameData`、`BuildSecondDirectionalFrameData`、visible light/manual list 收集、slice 请求计数、矩阵构建、PCSS 参数和 debug frame log 从 `HoShadowCastRendererFeature.cs` 拆出。
- `HoShadowCastPass` 现在只负责 compatibility / RenderGraph pass 记录、atlas 绘制、renderer list 构建、camera global restore 和调用 publisher 发布 receiver 数据。
- `HoShadowCastSettings` 增加 `lightLayerMask`，RendererFeature Inspector 显示 `Light Layer Mask`；自动 visible light 和 legacy manual/controller list 都会经过 light layer 过滤。
- 自动收集路径继续跳过 URP main light，并要求 visible light 类型匹配、启用、light layer 命中且 `Light.shadows != None`；legacy manual/controller list 仍允许 `LightShadows.None`，但也会经过 light layer、类型和启用状态过滤。
- 跳过诊断增加 light layer excluded 原因，便于 Inspector Runtime 区分“灯不可收集”和 atlas/容量/矩阵失败。
- 本次不改变 ShadowCast atlas ABI、receiver sampling ABI、MaterialBuffer/GeometryBuffer 边界或 ImageProcess 禁止消费 ShadowCast 的规则。

仍待处理：

- 继续对齐参考仓库的 second directional atlas block packing：一盏次方向光的 cascade 应作为整体 block 分配，避免写入半套 cascade。
- 继续细化 light layer 与 URP rendering layer / light layer 的关系；当前第一批落地的是 Unity GameObject layer 过滤。

---

## 11. 验收清单

- 无有效灯光时 receiver 全局状态被 reset。
- atlas 和 second directional atlas 都由 RDG 管理。
- debug off 时不加载 debug shader。
- debug shader 缺失不影响主功能。
- receiver ABI 不依赖 HoAOV。
- ShadowCast 不写 MaterialBuffer / GeometryBuffer。
- ScreenProcess 如消费 ShadowCast，必须显式声明资源读取。
- ImageProcess 不消费 ShadowCast。
- 没有场景控制器时 ShadowCast 仍能根据 RendererFeature 设置自动收集灯光。
- RendererFeature Inspector 能显示参与灯光、跳过原因和 slice 分配。
