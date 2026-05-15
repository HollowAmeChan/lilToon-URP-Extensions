# Shoost 后处理文件架构

这份文档记录 `lilToon-Shoost` 后处理移植侧的文件拆分方式。当前拆分优先解决 Editor 文件过大的问题，Runtime 仍保持原结构，等后续多 pass / RenderGraph 逻辑稳定后再继续拆。

## Editor 侧

`ShoostPostProcessStackVolumeEditor` 使用 `partial class` 拆分。主文件负责列表和调度，每个滤镜自己的 UI、默认值修正和条件显示 helper 放到独立文件里。

- `Editor/ShoostPostProcessStackVolumeEditor.cs`  
  Editor 主入口。保留 `OnEnable`、`OnInspectorGUI`、图标开关、图层列表、效果排序、通用行绘制、通用 reset/setter，以及 `DrawElement` 对各滤镜绘制方法的分发。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.ColorGradingCustom.cs`  
  Shoost 调色滤镜。包含普通色轮、对数色轮、六色偏移 UI、色轮纹理、trackball 绘制和调色默认值修正。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.IrisBlur.cs`  
  光圈模糊。包含模糊大小、中心半径、柔和度、中心位置、RGB 分离开关等 UI。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.KawaseBlur.cs`  
  Kawase/旧模糊入口。包含自定义分辨率判断、模糊半径、迭代等 UI。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.RGBBlurV2.cs`  
  通道模糊。只暴露 R/G/B 三个模糊度参数。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.RGBSplit.cs`  
  RGB 分离。包含径向/非径向模式的条件 UI，角度只在非径向模式显示。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.LevelAdjustment.cs`  
  色阶。包含 RGB/单通道模式的条件 UI 和默认无效果参数。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.VignetteCustom.cs`  
  暗角。包含压暗/染色模式的条件 UI，颜色只在染色模式显示。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.Fisheye.cs`  
  Shoost 用户侧的“镜头畸变”。包含强度、缩放、柔和度、圆形黑边等 UI。

- `Editor/ShoostPostProcessStackVolumeEditor.Filter.Distortion.cs`  
  湍流置换。包含速度、强度、纹理等 UI，并负责加载默认置换纹理。

- 其他 `Editor/ShoostPostProcessStackVolumeEditor.Filter.*.cs`  
  已迁出的较小滤镜或占位滤镜，例如锐化、白平衡、胶片、电视、颗粒、像素化、帧率限制等。即使暂时是占位，也先保留独立文件边界，后续补实现时直接进对应文件。

## Runtime 侧

Runtime 文件目前先不强拆，避免在 RenderGraph bug 还没有完全稳定前引入额外变量。

- `Runtime/PostProcessing/ShoostPostProcessRendererFeature.cs`  
  负责 Volume 读取、材质缓存、按插入点分组、Compatibility path 和 RenderGraph path 的 pass 调度。当前也包含 Kawase、Iris、RGBBlurV2、ChangeFrameRate 等特殊多 pass / 跨帧逻辑。

- `Runtime/PostProcessing/ShoostPostProcessEffect.cs`  
  用户侧滤镜 enum、混合模式 enum、插入位置 enum。

- `Runtime/PostProcessing/ShoostPostProcessLayer.cs`  
  单个滤镜图层的数据结构。当前仍使用 `parameters0` 到 `parameters12` 承载不同滤镜参数。

- `Runtime/PostProcessing/ShoostPostProcessEffectRegistry.cs`  
  enum 到 shader name、默认插入点的映射。

- `Runtime/PostProcessing/ShoostPostProcessShaderConstants.cs`  
  shader property id 集中表。

- `Runtime/PostProcessing/ShoostPostProcess*.shader`  
  各滤镜实际 shader。命名保持 `Hidden/lilToon-Shoost/URP/Shoost/...`。

## 新增滤镜流程

1. 在 `ShoostPostProcessEffect` enum 中补效果类型。
2. 在 `ShoostPostProcessStackVolumeEditor.cs` 的图标顺序表、显示名、`DrawElement` 和高度计算里注册入口。
3. 新建 `ShoostPostProcessStackVolumeEditor.Filter.<EffectName>.cs`，把 `Draw...Element`、`Ensure...Defaults`、条件显示 helper 放进去。
4. 在 `ShoostPostProcessEffectRegistry.cs` 中注册 shader 名和默认插入点。
5. 添加或更新对应 shader。
6. 如果不是单次 fullscreen blit，就在 Runtime pass 中补专用调度逻辑。

## 后续拆分方向

Runtime 侧后面可以再拆成：

- `ShoostPostProcessPass.Properties.cs`：统一 shader 参数写入。
- `ShoostPostProcessPass.RenderGraph.cs`：RenderGraph 主循环和通用 blit pass。
- `ShoostPostProcessPass.Blur.cs`：Kawase、Iris、RGBBlurV2。
- `ShoostPostProcessPass.ChangeFrameRate.cs`：跨帧冻结纹理状态。

这一步建议等调色和基础滤镜链路稳定后再做。
