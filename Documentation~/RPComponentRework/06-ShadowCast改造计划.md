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
