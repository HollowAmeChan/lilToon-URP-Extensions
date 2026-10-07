# 调试归属与最终显示

调试由功能模块自己维护。`HoDebugTileRendererFeature` 保留原脚本类型和 GUID，但只承接 APV 等外部系统诊断，不再提供 OB / GB / SB / ShadowCast / SSS / PLR 的集中式调试。

## 模块入口

OB、GB、SB、ShadowCast、SSS、PLR、GTAO、SSGI、GeometryData、CharacterShadow、CharacterSpecialization 的调试模式增加 `All = 255`，旧模式数值保持不变。All 枚举模块自己的视图声明，使用与单视图相同的 shader，通过 `HoDebugGrid` 平铺、绘制边框和标签。AC 保留“模式＋语义参数”的独立入口，不增加 All。ScreenProcess 的 layer `debugMask` 继续在原位置控制，但显示后置。

SB All 合并数值面和 legacy semantic lane 的调试需求；关闭后按原消费者登记规则释放需求。GTAO All 额外截取原 Generate 的 Depth / Normal / Motion 诊断分支，并保留 AO 与 Temporal 结果；额外诊断纹理只在 All 开启时生成。

## 捕获与显示

`HoDebugRenderPass` 提供两阶段输出：

1. 各模块在适合自己数据生命周期的位置把诊断绘制到独立目标，显式声明实际读取的纹理或 buffer。结果不写回正常 `cameraColor`。
2. companion presenter 在 `AfterRendering + 4` 执行。RG 直接写最终 active target；compatibility 明确绑定相机最终目标。它只读取模块已经生成的诊断结果，不采样 backbuffer、不重新实现模块资源绑定。

因此正常画面照常经过 Unity post-processing、ScreenProcess、ImageProcess、最终 AA 和缩放，调试结果最后显示。模块的原 `debugPassEvent` 序列化字段仅为兼容旧资产保留，已隐藏，不控制最终显示。Renderer Feature 的列表位置不再决定调试与后处理的先后。

同一相机同时开启多个模块调试时，使用 debug pass 完整类型名的 ordinal 顺序选择一个输出（最小键优先），不依赖入队顺序；排查时应只开启目标模块。多个 ScreenProcess layer 同时请求 `debugMask` 时按 layer 自身顺序选择视图。此规则属于逐相机呈现；相机堆栈若要求把 Base 相机诊断跨相机保留到整个 stack 最后，需要单独的堆栈输出契约。

## 扩展规则

- 新视图在所属模块登记 `HoDebugViewInfo`，将模式映射、shader、读依赖留在模块内。
- 全屏类视图调用 `HoDebugGrid.Draw`；GeometryData 的几何绘制复用公共矩形与标签，保持自身 renderer list 和独立深度。
- `HoDebugViewRegistry.AllViews` 是工具和 shader 检查目录，不是渲染分派器。已移除 `HoDebugViewRenderKind`。
- `HoDebugViewRegistry.SystemViews` 只收录外部系统视图；当前为 APV Validity / GI / Toon。DebugTile 的 All 只平铺这些外部视图。
- 原 DebugTile 中选中的 `object.*`、`geometry.*`、`surface.*` 等模式需改用对应模块入口；不自动修改已有 RendererData 或 VolumeProfile。
- All 暂用均匀网格。未来的中央结果布局只需要扩展布局工具，模块 shader 无需增加集中式分支。

## 验证入口

在独立 Unity batch 项目运行 `lilToon.URP.Extensions.Editor.Debugging.HoDebugPresentationValidation.Run`；compatibility 项目启用 `URP_COMPATIBILITY_MODE` 后运行 `RunCompatibility`。验证使用临时场景和运行时 pipeline/renderer 对象，不保存用户资产。

另有 `lilToon.URP.Extensions.SurfaceBuffer.HoSurfaceBufferEmissionValidation.Run` / `RunCompatibility` 验证真实 SB producer、HDR emission 及最终调试显示。

2026-10-08 独立验证：Unity 6000.3.15f1；D3D12 RenderGraph 145 项、D3D11 compatibility 144 项通过，另有 SB emission / All 的两路径渲染回归。具体范围及工作区其他模块的编译限制见 [实施记录](计划/Ho-Debug-Presentation-Plan.md)。
