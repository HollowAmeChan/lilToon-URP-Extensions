# 调试归属与最终显示

调试由功能模块自己维护。`HoDebugTileRendererFeature` 保留原脚本类型和 GUID，但用户-facing 名称已收缩为 `Ho-DebugPerformance`：它是一个 Ho 渲染体系专用的最终画面性能 HUD，不再承接 APV 或任何模块的集中式 tile 视图。

旧的 `Ho-DebugTile` Volume 类型继续保留以兼容已有 Profile；它现在只提供启用、性能模式和 Scene/Game View 覆盖。未勾选 Override 时使用 RendererFeature 的兜底值。

推荐把它理解成“渲染管线内的诊断 HUD”：RendererFeature 在最终 active color 上叠加文字，Volume 只提供逐相机开关和模式。它不替代 Unity Rendering Debugger、Frame Debugger 或 RenderDoc，也不接管 Feature 自己的图像调试。

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
- 功能模块声明 `HoDebugPerformanceInfo`，列出本功能使用的 `ProfilingSampler` marker 名称；有图像调试目录时可附带在 `HoDebugViewInfo` 中，AC、WeightedOIT、共享 RT 等无需为了计时新增 tile。新增功能需要同时登记计时 marker。
- 普通 pass 用 `HoDebugPerformanceRegistry.EnqueuePass(renderer, camera, pass, performance)` 入队；继承 `HoDebugRenderPass` 的 pass 用 `pass.Enqueue(renderer, camera, performance)`。这两条入口同时登记本次相机渲染参与的功能。关闭的功能、空的 layer stack、单纯清理旧资源的 pass 不登记为活动功能。
- HUD 在最终 overlay 阶段生成列表，按 `renderPassEvent` 升序、同事件实际入队顺序排序；跨多个阶段的 feature 以第一个 pass 定位，所有已声明 marker 仍合并为一行。PLR 的 begin-camera 反射工作、共享 RT 的 AddRenderPasses 准备工作按实际阶段排在普通 GPU pass 前。列表每次相机渲染重建，Scene/Game View 相互独立，不靠旧 recorder 样本猜测是否启用；开启但本帧复用缓存的功能仍保留一行和最后有效值。
- Unity Recorder 提供单帧 CPU/GPU 值，GPU 有延迟，直接读取并合并同功能的 marker，不做跨帧差分。数值刷新时仅对应 G/C 数字变黄；无新样本时保留白色旧值。`LAST` 白字显示距上次有效采样的时间。计时 recorder 本身是各 marker 在 Unity 帧内的全局总量，多相机渲染时不代表单相机独占耗时；活动列表和执行顺序按当前相机筛选。
- Feature 实际渲染 pass 也必须通过 `HoDebugPerformanceRegistry.GetSampler` 取得这些 marker；只用相同字符串重新 `new ProfilingSampler` 不会共享 Unity recorder。
- recorder 按所有视图的 HUD 需求统一管理；一个视图关闭 HUD 不会关闭另一个视图的共享计时。最后一次 HUD 请求后保留一秒录制窗口，避免编辑器分开渲染视图时中断 GPU 延迟回读；之后没有请求则停止录制。
- HUD 显示 `HO FEATURES N ACTIVE OF M`，N 为当前相机参与渲染的 feature，M 为已登记总数。GPU recorder 没有有效样本时显示 `--`。G 只包含声明的执行范围，不代表开关该 feature 后的整帧增量；URP 的共享依赖和等待可能另有成本。共享 RT 行的 C 包含 CPU 场景准备，G 包含加速结构构建；构建嵌在消费者的 GPU scope 内时，两行有包含关系，不能简单把所有行加总。
- VolumeLight 覆盖太阳、局部灯及其阴影、重建、合成和灰尘；RTDI 外层计时覆盖完整 DI 和 NRD，内部诊断 scope 不重复计入；SSS 包含双向漫射和双向透射滤波。PLR 包含实际反射相机渲染、复制、预滤波和合成，未启用全屏合成时仍显示活动的反射工作；反射相机内的其他 feature 与 PLR 有包含关系。
- 全屏类视图调用 `HoDebugGrid.Draw`；GeometryData 的几何绘制复用公共矩形与标签，保持自身 renderer list 和独立深度。
- `HoDebugViewRegistry.AllViews` 是工具和 shader 检查目录，不是渲染分派器。已移除 `HoDebugViewRenderKind`。
- `HoDebugViewRegistry.SystemViews` 仍只收录外部系统视图；当前为 APV Validity / GI / Toon，供工具和历史资源检查使用。Ho-DebugPerformance 不再平铺这些视图。
- 原 DebugTile 中选中的 `object.*`、`geometry.*`、`surface.*` 等模式需改用对应模块入口；旧 RendererData / VolumeProfile 中的字段保留兼容，但不再驱动集中式 tile 渲染。
- All 暂用均匀网格。未来的中央结果布局只需要扩展布局工具，模块 shader 无需增加集中式分支。

## 验证入口

`lilToon.URP.Extensions.Editor.Debugging.HoDebugPerformanceValidation.Run` 验证功能目录覆盖、执行事件排序、同事件稳定顺序、跨阶段去重、相机隔离以及同帧重复渲染时的活动列表重置。

在独立 Unity batch 项目运行 `lilToon.URP.Extensions.Editor.Debugging.HoDebugPresentationValidation.Run`；compatibility 项目启用 `URP_COMPATIBILITY_MODE` 后运行 `RunCompatibility`。验证使用临时场景和运行时 pipeline/renderer 对象，不保存用户资产。

另有 `lilToon.URP.Extensions.SurfaceBuffer.HoSurfaceBufferEmissionValidation.Run` / `RunCompatibility` 验证真实 SB producer、HDR emission 及最终调试显示。

2026-10-08 独立验证：Unity 6000.3.15f1；D3D12 RenderGraph 145 项、D3D11 compatibility 144 项通过，另有 SB emission / All 的两路径渲染回归。具体范围及工作区其他模块的编译限制见 [实施记录](计划/Ho-Debug-Presentation-Plan.md)。
