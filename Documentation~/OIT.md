# lilToon URP 加权 OIT 实现笔记

> **2026-11-25 静态化状态**：源码已切换为原生 ShaderLab / HLSL 维护。本轮已停止追加测试，GPU 对照、Player 和安装复开等完整验收未完成，未完成项不记为通过；历史记录仅代表其注明日期与范围。

> **状态：现行功能说明（实现笔记）**。2026 文档审核时逐项核对过：`_lilOITEnabled` / `_lilOITActive` /
> `lil_oit.hlsl`（lilToon 侧）、`WeightedOITRendererFeature` 与 `_lilOITAccumulationTexture` /
> `_lilOITRevealageTexture`（本仓）继续使用。2026-11-25 已退役零消费者的 OpaqueCopy、背景 RT 与全局发布；合成仍读取当前相机颜色。材质侧直接维护普通 `.shader` 的 OIT Pass，旧 block/容器生成链已退役。
> 写作时的目标环境是 Unity 2022.3 + URP 14.x；本项目现在跑 Unity 6000.3 + HoUrp17.3.0 fork，机制未变。

本文记录 `lilToon` fork 与配套 `lilToon-URP-Extensions` 包之间已经完成的加权 OIT 集成。

它不是单纯的功能概览，而是一份实现案例笔记。目标是保留架构、实际到文件级别的工作流，以及通过 RenderDoc 抓帧调试时踩到的坑。

## 最终目标

这个功能为 URP 下的 lilToon 透明 shader 增加可选的加权 Order-Independent Transparency，也就是加权 OIT。

当材质启用 `_lilOITEnabled` 时：

- lilToon 会把透明颜色写入 OIT accumulation 与 revealage 缓冲。
- URP Renderer Feature 会把解析后的透明结果合成回相机颜色。
- 只有 skybox 作为背景时也能正常工作。
- 多个透明 lilToon 对象重叠时，不再像普通 alpha blending 那样强依赖对象排序。

最初实现环境（历史记录）是：

- Unity 2022.3
- URP 14.x
- 当时的 lilToon fork shader-template 布局
- `lilToon-URP-Extensions` 作为外部 Renderer Feature 包

## 架构

实现有意拆在两个仓库里。

### lilToon fork 侧

lilToon fork 负责材质属性、普通 ShaderLab Pass 声明与共享 HLSL 片元输出。

它增加了：

- 名为 `_lilOITEnabled` 的材质属性。
- 普通透明 `.shader` 中的 `LILTOON_OIT` Pass，LightMode 为 `lilToonOIT`。
- 共享的 `lil_oit.hlsl` include，用来把 lilToon forward color 转换成 MRT OIT 输出。
- forward shader guard，用来在 OIT accumulation pass 激活时跳过普通 forward transparent pass。

OIT 使用的 shader pass tag 是：

```shaderlab
Tags {"LightMode" = "lilToonOIT"}
```

这是扩展包消费的契约。

### URP 扩展侧

扩展包负责 URP 渲染目标分配和 render pass 时机。

它增加了：

- `WeightedOITRendererFeature`
- `WeightedOITClearPass`
- `WeightedOITAccumulationPass`
- `WeightedOITCompositePass`
- `WeightedOITComposite.shader`
- OIT shader 常量与设置

Renderer Feature 只绘制带有 `LightMode = lilToonOIT` tag 的 pass。除了 OIT include 使用的全局 shader 属性之外，它不需要知道 lilToon 材质内部细节。

## 数据流

预期的每帧流程是：

1. 每个相机开始渲染时，把 `_lilOITActive` 重置为 `0`。
2. 渲染 opaque 对象。
3. 渲染 skybox。
4. 将 OIT accumulation 清成透明黑，将 revealage 清成白色。
5. 设置 `_lilOITActive = 1`，绘制带有 `LightMode = lilToonOIT` 的 lilToon 对象。
6. 将 accumulation 与 revealage 合成到当前 camera color target 上。
7. composite 结束后再次设置 `_lilOITActive = 0`。

当前实现不再捕获 OIT 专属 opaque snapshot，也不覆写 `_CameraOpaqueTexture`。主包原背景采样链已退役，现役 OIT 材质只需 MRT 输出与合成时的相机颜色。

兼容模式的 composite 保留 `_lilOITCompositeSourceTexture` 临时拷贝，避免同一目标同时采样和写入；Render Graph 则读取当前颜色、写入新的颜色目标并更新 `resourceData.cameraColor`。这两条必要合成路径与已删除的绘制前背景拷贝用途不同。

## 加权 OIT 缓冲

shader 会写入两个 MRT 输出：

- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`

lilToon 的 OIT include 当前使用：

```hlsl
output.accumulation = float4(color.rgb * alpha * weight, alpha * weight);
output.revealage = float4(alpha, 0.0, 0.0, 0.0);
```

accumulation 缓冲保存加权后的预乘颜色和总权重。revealage 缓冲由 pass 的 blend state 单独混合。

composite 的解析逻辑是：

```hlsl
transparentColor = accumulation.rgb / max(accumulation.a, epsilon);
transparentAlpha = saturate(1.0 - revealage);
cameraColor.rgb = lerp(cameraColor.rgb, transparentColor, transparentAlpha);
```

## Render Pass 时机

几个重要的 URP 时机选择是：

- OIT accumulation：`BeforeRenderingTransparents`。
- OIT composite：`AfterRenderingTransparents`。

URP 通常会在 opaque 对象之后、transparent 对象之前绘制 skybox。OIT 合成直接读取包含该结果的当前相机颜色，无需提前再捕获一张供材质采样的背景。

## 背景拷贝退役边界

早期折射/抓屏实现曾依赖绘制前的背景快照，以解决只存在 skybox 时采样偏暗的问题。当前主包已删除这条采样链；核对现役 `lilToonOIT` Shader 与相关生产源码后，没有找到 OIT 专属快照的消费者。

因此一并删除 `WeightedOITOpaqueCopyPass`、专属 RTHandle/Render Graph 字段、性能标记，以及 `_lilOITOpaqueTexture` 和 `_CameraOpaqueTexture` 的发布代码。相机自身由 URP 或其他 Renderer Feature 提供的 opaque texture 不属于这次删除范围。以后若增加新的背景采样需求，应重新定义其资源依赖，不能依赖已退役的 OIT 副作用。

## Render Queue 说明

lilToon 透明 shader 有意使用：

```shaderlab
"RenderType" = "TransparentCutout"
"Queue" = "AlphaTest+10"
```

这不是失误。lilToon 的透明 `_ZWrite` 默认也为 `1`。这个组合会让很多 toon/avatar 透明表面表现得更像一个深度稳定、接近 alpha-test 的对象，而不是普通 Unity 透明对象。

这对常规 lilToon 渲染很有用，尤其是头发、睫毛、叠层衣物这类角色部件。对这些部件来说，稳定的自遮挡通常比物理正确的 alpha 排序更重要。

不过，这个队列选择会让 OIT 复杂化：

- `AlphaTest+10` 仍然在 opaque/alpha-test 范围里。
- 它在 URP 中会早于 skybox 渲染。
- 常规 forward 渲染可能出现在 OIT pass 之前。

最终实现没有全局修改 lilToon 的队列。把所有透明 shader 改到 `Transparent` 会过度改变现有材质行为。因此，OIT 被实现为一个可选 pass 和 Renderer Feature 路径。

如果未来版本选择把 OIT 材质强制放进 queue 3000，应该把它做成材质级 opt-in，并先确认 OIT accumulation 和 composite pass 一定有输出。调试时过早强制 queue 3000 曾经导致 OIT 材质消失，因为普通 forward pass 已经被跳过，但 OIT accumulation 还无效。

## `_lilOITActive`

`_lilOITActive` 是扩展包与 lilToon shader 代码之间握手用的全局 shader 状态。

在常规 forward shader 路径中：

```hlsl
clip(0.5 - _lilOITEnabled * _lilOITActive);
```

这会阻止启用了 OIT 的材质在 OIT accumulation pass 激活时同时走普通 forward 路径绘制。

重要规则：

`_lilOITActive` 必须针对每个相机重置为 `0`。

它是全局状态。如果某个相机把它留在 `1`，editor preview 或 scene camera 路径可能会丢失 OIT 材质，而非 OIT 材质仍然继续绘制。扩展通过注册 `RenderPipelineManager.beginCameraRendering`，在每个相机开始前重置该值。

## Render Target 尺寸与 MSAA

这个实现里最大的实际 bug 不是 shader 数学，而是非法的 render target 绑定。

RenderDoc 显示了这条 D3D 警告：

```text
Invalid output merger - Depth target is different size or MS count to render target(s).
```

两次抓帧暴露了两个变体：

- OIT accumulation 是 `238x790`，但 camera depth 是另一个尺寸。
- OIT accumulation 是 `1056x790` 非 MSAA，但 camera depth 是 `1056x790 MSAA8x`。

发生这种情况时，OIT accumulation pass 可能会静默失败，写不出有效结果。如果普通 forward 路径又被跳过，OIT 对象就会消失。

最终规则是：

- Full scale OIT 保留 camera descriptor 的 MSAA sample count。
- Half/Quarter OIT 强制 `msaaSamples = 1`。
- 只有当 color 和 depth 匹配时，accumulation pass 才绑定 camera depth：
  - width
  - height
  - volume depth
  - anti-aliasing sample count
- 如果不匹配，该 pass 只绑定 OIT MRT。

这样可以避免非法 output merger 状态，并让功能在 Game view 和 Scene view 中都能工作。

## lilToon Fork 中变更的文件

### 现行普通 ShaderLab 源码

OIT 的直接材质入口是以下四个文件：

- `Shader/lts_trans.shader`
- `Shader/lts_trans_o.shader`
- `Shader/lts_twotrans.shader`
- `Shader/lts_twotrans_o.shader`

当前 8 个材质入口中，这四个透明 Shader 直接声明 `LILTOON_OIT` Pass；
无消费者的旧共享入口 `Shader/ltspass_transparent.shader` 已退役。实际维护以 Pass 声明为准，
不能只按 `lts*` 文件名前缀推断透明能力。Lite、Tessellation、Overlay 与旧
`lts*_onetrans*` 命名文件不再是本专用包的维护入口；早期 use-pass block
组装过程属于历史实现，普通透明行为以这里列出的四个材质源码为准。
当前四个直接材质入口不依赖容器或自定义生成器。

OIT Pass 继续使用：

```shaderlab
Name "LILTOON_OIT"
Tags {"LightMode" = "lilToonOIT"}
ZWrite Off
BlendOp 0 Add
BlendOp 1 Add
Blend 0 One One
Blend 1 Zero OneMinusSrcColor
```

自定义 Shader 可以使用 Unity 原生 `UsePass`，但目标 Shader 必须实际提供
`LILTOON_OIT`，并共享正确的材质属性与片元输入；这不恢复旧 `.lilblock` DSL。

### 材质属性与 Inspector

普通透明 Shader 的 `Properties` 包含：

```shaderlab
[lilToggle] _lilOITEnabled ("Weighted OIT", Int) = 0
```

`lilMaterialProperties.cs` 绑定该属性；现行入口 `lilNextInspectorGUI.cs` 的
透明材质设置显示 Weighted OIT。维护属性名、类型或默认值时直接修改正式
`.shader`，同时核对材质管理器与预设；没有全局 Shader 生成设置页。

### Shader include

当前共享实现包括：

- `Shader/Includes/lil_feature_config.hlsl`：专用功能集合。
- `Shader/Includes/lil_oit.hlsl`：OIT MRT 输出和全局状态。
- `Shader/Includes/lil_common_input.hlsl`、`lil_common_input_base.hlsl`：普通输入声明。
- `Shader/Includes/lil_pass_forward_normal.hlsl`：正常 Forward 与 OIT 共用片元路径。

`LIL_OIT_PASS` 将片元返回类型切换为 MRT 并调用 `LIL_OIT_RETURN(...)`。
旧 `lil_common_input_opt.hlsl`、Lite 片元和 block 资源不再作为现行源码。
修改 Pass/pragma/状态时编辑普通 `.shader`；修改共享算法时编辑 HLSL，由 Unity
标准导入器编译。不要恢复在安装、API 切换或构建前后重写 Shader 的逻辑。

## URP 扩展包中变更的文件

### Runtime/OIT/WeightedOITRendererFeature.cs

负责 render pass 编排：

- 按相机重置全局 OIT 状态
- 分配 OIT RT
- 清理 accumulation/revealage
- 绘制 `lilToonOIT` shader-tag pass
- 合成回 camera color
- 释放 RTHandle

### Runtime/OIT/WeightedOITShaderConstants.cs

集中管理 shader 名称和 property ID：

- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`
- `_lilOITCompositeSourceTexture`
- `_lilOITActive`

### Runtime/OIT/WeightedOITComposite.shader

Fullscreen composite shader。它会采样：

- `_BlitTexture`，即当前 camera color
- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`

然后把加权透明颜色解析到 camera color 上。

### Runtime/OIT/WeightedOIT.hlsl

保留给扩展包使用者的 package-side helper include。lilToon fork 使用自己的 `lil_oit.hlsl`，由普通 ShaderLab Pass 包含，直接消费相同 OIT 全局协议。

## 调试时间线与坑点

### 1. 只有 skybox 背景时画面偏暗（历史）

早期背景采样拿不到包含 skybox 的纹理，曾通过绘制前拷贝并发布 `_CameraOpaqueTexture` 解决。这条记录只适用于已经退役的背景采样实现；当前颜色合成不依赖该拷贝，不能据此恢复 OpaqueCopy。

### 2. 强制 queue 3000 导致对象消失

现象：

- 把 OIT 材质移到 transparent queue 后对象消失。

原因：

- 普通 forward pass 被 `_lilOITActive` 跳过。
- OIT accumulation pass 因为 render target 绑定非法而失败。

经验：

- 队列问题确实存在，但它不是第一个要修的 bug。
- 在修改材质队列行为前，一定要先确认 accumulation 和 composite 确实在产出结果。

### 3. RenderDoc 暴露非法 output merger 状态

现象：

- OIT 效果在 Scene view / camera preview 中不稳定或不可见。

RenderDoc 警告：

```text
Invalid output merger - Depth target is different size or MS count to render target(s).
```

修复：

- Full-scale OIT RT 匹配 MSAA。
- 只有 depth 兼容时才绑定 camera depth。

### 4. Preview/camera 状态可能丢失 OIT 对象

现象：

- preview 路径里 OIT 对象丢失，而非 OIT 对象可见。

原因：

- `_lilOITActive` 是全局状态。

修复：

- 在 `RenderPipelineManager.beginCameraRendering` 重置 `_lilOITActive`。
- 同时保留 preview reset pass 作为防御性兜底。

## 如何验证

在 Frame Debugger 或 RenderDoc 中查找：

```text
Ho-WeightedOIT Clear
Ho-WeightedOIT Accumulation
Ho-WeightedOIT Composite
```

确认：

- 存在 OIT accumulation draw call。
- 没有出现 `Invalid output merger` 警告。
- 绑定 depth 时，`_lilOITAccumulationTexture` 的尺寸和 MSAA 与 camera depth 匹配。
- skybox 可以透过只启用 OIT 的对象看到。
- OIT 和非 OIT 透明对象可以共存。

如果 OIT 对象消失：

1. 检查 composite shader 是否找到。
2. 检查 `Ho-WeightedOIT Accumulation` 是否有 draw call。
3. 检查 RenderDoc 警告里是否有 RT/depth 尺寸或 MSAA 不匹配。
4. 检查 `_lilOITActive` 是否在 accumulation pass 外卡在 `1`。
5. 检查材质 render queue override；手动强制 queue 3000 可能会掩盖其他 bug。

## 扩展包与 lilToon 的契约

package/fork 边界刻意保持得很窄。

lilToon 承诺：

- 启用 OIT 的透明 shader 暴露 `_lilOITEnabled`。
- 支持 OIT 的 shader 包含带有 `LightMode = lilToonOIT` tag 的 pass。
- 该 pass 写入 MRT accumulation/revealage 输出。
- 常规 forward pass 尊重 `_lilOITActive`。

扩展包承诺：

- 分配并绑定 MRT。
- 设置 OIT 全局常量。
- 在 accumulation 阶段只绘制 `lilToonOIT` pass。
- 将最终结果合成回 camera color。
- 为每个相机重置全局 OIT 状态。

保持这个契约足够小，可以让未来 lilToon shader 修改更容易，也让 URP Renderer Feature 代码独立于 lilToon editor 内部实现。

## 后续修改的实践建议

- 不要在没有检查现有 avatar/toon 材质行为的情况下全局修改 lilToon 的 transparent queue。
- 合成读取当前相机颜色；若新增材质背景采样，单独声明其资源与时机。
- 除非尺寸和 MSAA 匹配，否则不要把 camera depth 绑定到缩放过的 OIT RT。
- 不要在 accumulation pass 结束后让 `_lilOITActive` 保持开启。
- 尽早使用 RenderDoc。最终最快定位修复的是 D3D output merger 警告，而不是 shader 侧视觉猜测。
- `Assets/lilToon/Shader/*.shader` 是正式 ShaderLab 源码；功能宏维护在 `Includes/lil_feature_config.hlsl`，共享算法维护在普通 HLSL。直接导入与编译这些源码，并核对实际 Pass，不再运行 block/容器生成流程。
