# lilToon URP Extensions

这个包用于整理 lilToon 在 URP 下需要的自定义 renderer 扩展、实验性渲染功能和后续可投入实际使用的画质功能。

当前第一阶段已经落地的是 lilToon 透明材质用的 Weighted Blended OIT：

- URP `ScriptableRendererFeature` 入口；
- accumulation / revealage 渲染目标；
- 最终相机颜色合成；
- 给 lilToon 透明 pass 使用的材质和 shader 集成点。

## 安装

把这个文件夹作为本地 Unity Package Manager 包加入项目。

```json
{
  "dependencies": {
    "jp.lilxyzw.liltoon.urp.extensions": "file:../lilToon-URP-Extensions"
  }
}
```

这个包目前面向 Unity 6000.0 和 URP 17.3.0。项目里还需要对应的 lilToon fork。lilToon 暂时作为 peer requirement 处理，这样本地 `Assets/lilToon` 安装不会破坏 UPM 依赖解析。

## 当前状态

Weighted OIT 已经作为第一个可用里程碑实现：

- `WeightedOITRendererFeature` 分配 accumulation 和 revealage RT；
- skybox 后复制 opaque camera color，并暴露为 `_lilOITOpaqueTexture` 和 `_CameraOpaqueTexture`；
- accumulation pass 只绘制 `LightMode = "lilToonOIT"` 的 shader pass；
- composite pass 把 accumulation / revealage 合回相机颜色；
- `_lilOITActive` 每个相机都会重置，只在 accumulation pass 绘制时启用；
- RenderGraph 和非 RenderGraph 路径都已实现。

配套的 lilToon fork 提供 `_lilOITEnabled`、`LILTOON_OIT` pass 和 `lil_oit.hlsl`。

实现说明、调试方法，以及 skybox 背景、render scale、MSAA、Scene view 等边界情况见 `Documentation~/OIT.md`。

## Shoost 风格后处理

`ShoostPostProcessRendererFeature` 是把 Shoost 后处理移植到 URP 的入口。它按 HTrace 风格安装由 Volume 驱动的 pass；真正的图层列表由 Volume profile 里的 `Shoost Post Process Stack` 控制。这个列表可以像 Shoost 图层一样启用、禁用、重排、添加和删除效果。

当前框架已经包含：

- Volume 图层栈容器；
- Shoost `BeforeStack` / `AfterStack` 插入分组；
- ping-pong 全屏 blit 调度；
- 通用 shader 参数；
- 默认图层 blit shader；
- RenderGraph 和非 RenderGraph 路径。

单个 Shoost 效果还需要继续从参考包、Cpp2IL renderer 流程和 RenderDoc shader dump 里逐个移植。

重要事项：URP 内置后处理由 `UniversalRenderer` 内部注入。只要相机开启了 `Render Post Processing`，它就可能运行 URP Bloom、Tonemapping、FXAA、Color Adjustments 等内置 pass；这些 pass 不会出现在 renderer feature 列表里。把 Shoost 图层和 URP 内置后处理混用时，一定要把这件事算进顺序判断里。

设置和移植说明见 `Documentation~/PostProcessing/ShoostPostProcessStack.md`。

## 平面反射

`LILPlanarReflectionSurface` 是共享的平面反射 runtime，适合平面镜、抛光地面和水面一类对象。把它加到反射 mesh 上，然后使用会采样 `_LILPBRPlanarReflectionTexture` 的 shader / material。

修改后的 lilPBR shader 已经有 `Planar Reflection` foldout，并通过 per-renderer material property block 使用这张反射纹理。

设置说明见 `Documentation~/PlanarReflection.md`。
