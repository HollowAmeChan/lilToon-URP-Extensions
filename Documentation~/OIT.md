# lilToon URP 加权 OIT 实现笔记

本文记录 `lilToon` fork 与配套 `lilToon-URP-Extensions` 包之间已经完成的加权 OIT 集成。

它不是单纯的功能概览，而是一份实现案例笔记。目标是保留架构、实际到文件级别的工作流，以及通过 RenderDoc 抓帧调试时踩到的坑。

## 最终目标

这个功能为 URP 下的 lilToon 透明 shader 增加可选的加权 Order-Independent Transparency，也就是加权 OIT。

当材质启用 `_lilOITEnabled` 时：

- lilToon 会把透明颜色写入 OIT accumulation 与 revealage 缓冲。
- URP Renderer Feature 会把解析后的透明结果合成回相机颜色。
- 只有 skybox 作为背景时也能正常工作。
- 多个透明 lilToon 对象重叠时，不再像普通 alpha blending 那样强依赖对象排序。

实现目标环境是：

- Unity 2022.3
- URP 14.x
- lilToon fork 的 shader-template 布局
- `lilToon-URP-Extensions` 作为外部 Renderer Feature 包

## 架构

实现有意拆在两个仓库里。

### lilToon fork 侧

lilToon fork 负责材质属性、shader 模板组装，以及 shader 片元输出。

它增加了：

- 名为 `_lilOITEnabled` 的材质属性。
- 包含 `LILTOON_OIT` pass 的 URP use-pass 模板。
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
- `WeightedOITOpaqueCopyPass`
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
4. 在 skybox 之后，把 camera color 复制到 `_lilOITOpaqueTexture`。
5. 将 OIT accumulation 清成透明黑。
6. 将 OIT revealage 清成白色。
7. 绘制带有 `LightMode = lilToonOIT` 的 lilToon 对象。
8. 只在绘制 OIT accumulation pass 时设置 `_lilOITActive = 1`。
9. 将 accumulation 与 revealage 合成到 camera color target 上。
10. composite 结束后再次设置 `_lilOITActive = 0`。

在 OIT draw 期间，背景拷贝也会作为 `_CameraOpaqueTexture` 发布。这样现有的 lilToon 背景/折射宏仍然可以采样到包含 skybox 的有效背景，不需要重写所有 shader 侧的采样点。

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

- opaque 背景拷贝：skybox 之后，OIT accumulation 之前。
- OIT accumulation：`BeforeRenderingTransparents`。
- OIT composite：`AfterRenderingTransparents`。

URP 通常会在 opaque 对象之后、transparent 对象之前绘制 skybox。这个顺序是正确的，不应该绕开它。OIT 系统需要的是一张在 skybox 之后捕获的背景纹理，而不是再手动绘制一次 skybox。

## 背景与 Skybox

早期有一个 bug：当 OIT 对象背后只有 skybox 时，对象会显得太暗。

原因是 shader 侧的背景采样路径最终依赖 `_CameraOpaqueTexture`，但在这个自定义时机里，URP 的 opaque texture 路径不一定会给 OIT shader 一张包含 skybox 的纹理。

有效的解决方案是：

1. skybox 之后，把 camera color target 复制到 `_lilOITOpaqueTexture`。
2. 将这张纹理同时绑定为：
   - `_lilOITOpaqueTexture`
   - `_CameraOpaqueTexture`
3. 同时更新 `_CameraOpaqueTexture_TexelSize`。

这样既保留了 lilToon 现有的 `LIL_GET_BG_TEX` / `LIL_GET_GRAB_TEX` 风格路径，又能给 OIT shader 正确的背景。

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

### 基础 shader 资源

透明 shader descriptor 已切换到支持 OIT 的 URP use-pass block。受影响的系列包括：

- `lts*_trans*.lilinternal`
- `lts*_onetrans*.lilinternal`
- `lts*_twotrans*.lilinternal`
- `lts*_overlay*.lilinternal`
- lite 和 tessellation 变体

这些文件会选择类似下面的 block：

- `DefaultUsePassOIT`
- `DefaultUsePassOutlineOIT`
- `DefaultUsePassTwoSideOIT`
- `DefaultUsePassOutlineTwoSideOIT`
- `DefaultUsePassOverlayOIT`

### URP shader 模板

OIT pass 被加入到隐藏 pass shader：

- `Assets/lilToon/CustomShaderResources/URP/DefaultTwoSide.lilblock`
- `Assets/lilToon/CustomShaderResources/URP/DefaultLiteTwoSide.lilblock`
- `Assets/lilToon/CustomShaderResources/URP/DefaultTessellationTwoSide.lilblock`

该 pass 使用：

```shaderlab
Name "LILTOON_OIT"
Tags {"LightMode" = "lilToonOIT"}
ZWrite Off
BlendOp 0 Add
BlendOp 1 Add
Blend 0 One One
Blend 1 Zero OneMinusSrcColor
```

### URP use-pass 模板

新的 URP use-pass 模板会把透明 shader 路由到 OIT pass：

- `DefaultUsePassOIT.lilblock`
- `DefaultUsePassOutlineOIT.lilblock`
- `DefaultUsePassTwoSideOIT.lilblock`
- `DefaultUsePassOutlineTwoSideOIT.lilblock`
- `DefaultUsePassOverlayOIT.lilblock`

这些模板保留常规 lilToon pass，并额外加入：

```shaderlab
UsePass "*LIL_PASS_SHADER_NAME*/LILTOON_OIT"
```

### 材质属性与 Inspector

透明属性 block 增加：

```shaderlab
[lilToggle] _lilOITEnabled ("Weighted OIT", Int) = 0
```

Inspector 变更：

- `lilMaterialProperties.cs` 绑定 `_lilOITEnabled`。
- `lilPropertyGroupDrawerBaseSetting.cs` 只在 URP 透明材质中显示该 toggle。

### Shader include

变更/新增的 include：

- `Shader/Includes/lil_oit.hlsl`
- `Shader/Includes/lil_common_input.hlsl`
- `Shader/Includes/lil_common_input_base.hlsl`
- `Shader/Includes/lil_common_input_opt.hlsl`
- `Shader/Includes/lil_pass_forward_normal.hlsl`
- `Shader/Includes/lil_pass_forward_lite.hlsl`

`lil_pass_forward_*` 会在 `LIL_OIT_PASS` 下把 fragment return type 切换成 MRT 输出，并调用 `LIL_OIT_RETURN(...)`，而不是常规输出。

## URP 扩展包中变更的文件

### Runtime/OIT/WeightedOITRendererFeature.cs

负责 render pass 编排：

- 按相机重置全局 OIT 状态
- 分配 OIT RT
- 复制包含 skybox 的背景
- 清理 accumulation/revealage
- 绘制 `lilToonOIT` shader-tag pass
- 合成回 camera color
- 释放 RTHandle

### Runtime/OIT/WeightedOITShaderConstants.cs

集中管理 shader 名称和 property ID：

- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`
- `_lilOITOpaqueTexture`
- `_lilOITCompositeSourceTexture`
- `_lilOITActive`
- `_CameraOpaqueTexture`
- `_CameraOpaqueTexture_TexelSize`

### Runtime/OIT/WeightedOITComposite.shader

Fullscreen composite shader。它会采样：

- `_BlitTexture`，即当前 camera color
- `_lilOITAccumulationTexture`
- `_lilOITRevealageTexture`

然后把加权透明颜色解析到 camera color 上。

### Runtime/OIT/WeightedOIT.hlsl

保留给扩展包使用者的 package-side helper include。lilToon fork 目前有自己的 `lil_oit.hlsl` include，因为它已经集成进 lilToon 的 shader 生成管线。

## 调试时间线与坑点

### 1. 只有 skybox 背景时画面偏暗

现象：

- OIT 对象背后有 opaque 对象时看起来正确。
- OIT 对象背后只有 skybox 时看起来太暗。

原因：

- OIT shader 的背景采样没有拿到包含 skybox 的纹理。

修复：

- 在 skybox 之后复制 camera color。
- 在 OIT draw 期间把该拷贝发布为 `_CameraOpaqueTexture`。

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
lilToon Weighted OIT Opaque Copy
lilToon Weighted OIT Clear
lilToon Weighted OIT Accumulation
lilToon Weighted OIT Composite
```

确认：

- 存在 OIT accumulation draw call。
- 没有出现 `Invalid output merger` 警告。
- 绑定 depth 时，`_lilOITAccumulationTexture` 的尺寸和 MSAA 与 camera depth 匹配。
- skybox 可以透过只启用 OIT 的对象看到。
- OIT 和非 OIT 透明对象可以共存。

如果 OIT 对象消失：

1. 检查 composite shader 是否找到。
2. 检查 `lilToon Weighted OIT Accumulation` 是否有 draw call。
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
- 提供包含 skybox 的背景纹理。
- 将最终结果合成回 camera color。
- 为每个相机重置全局 OIT 状态。

保持这个契约足够小，可以让未来 lilToon shader 修改更容易，也让 URP Renderer Feature 代码独立于 lilToon editor 内部实现。

## 后续修改的实践建议

- 不要在没有检查现有 avatar/toon 材质行为的情况下全局修改 lilToon 的 transparent queue。
- 不要手动绘制 skybox 来替代背景纹理。
- 除非尺寸和 MSAA 匹配，否则不要把 camera depth 绑定到缩放过的 OIT RT。
- 不要在 accumulation pass 结束后让 `_lilOITActive` 保持开启。
- 尽早使用 RenderDoc。最终最快定位修复的是 D3D output merger 警告，而不是 shader 侧视觉猜测。
- 把生成的 `Assets/lilToon/Shader/*.shader` 文件当作验证输出。长期编辑应放在 `.lilinternal`、`.lilblock`、editor binding 或 HLSL include 文件中。
