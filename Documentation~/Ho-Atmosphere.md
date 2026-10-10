# HoAtmosphere：环境输入与空间介质规划

HoAtmosphere 生产供 RTDI / RTGI 查询的全方向线性 HDR 环境辐射，并独立发布盒形空间介质。它不改变主相机天空背景，也不直接绘制丁达尔；太阳 HD 积分/绘制由 [Ho-VolumeLight](Ho-VolumeLight.md) 消费基础数据后执行。

## 底层基建定位（2026-10-10，Box 介质 v0.1 已实现）

用户明确 HoAtmosphere Feature 已固定开启，未来与 GD/GB/OB/SB/AC 一起作为底层基建。按这个前提，HoAtmosphere 统一维护并发布环境辐射与空间介质数据，消费者通过明确契约读取；此前按“可选天空输入 Feature”提出的独立外部发布建议由本条替代。

`HoAtmosphereRegion` 当前保存 Box 的 Bounds/软权重、密度/颜色、Texture3D 动态噪声与体积光参与权重。任意 SDF 纹理和可见灰尘属性/模拟尚未实现。Region/Registry 是世界空间数据来源，Feature 负责准备和发布，内部不混入现有天空 Cache。SDF 权重只控制区域内的显示参与，不改变光束完整照亮范围或遮挡。

VolumeLight 单向消费区域数据，管理自己的灯光绑定、SD/HD、光源深度与积分/绘制。区域 Bounds 可协助确定 HD 的接收/捕获范围，但介质区域外的投影遮挡物仍须保留。AC 接入效果名称、贡献/遮罩与后续导出，不承担大气场或光束渲染。

环境 radiance 按世界方向采样，空间介质按世界位置/时间查询。两域有独立资源、有效性和 revision，由同一个固定基础 Feature 发布。环境来源缺失或环境 Volume 关闭不使有效区域失效；无区域发布空介质。v0.1 多个启用区域会明确报告容量限制并发布空介质。修改区域风/密度不触发环境 Cubemap 重捕获或 GI 的环境历史失效。`HoAtmosphereVolume` 是逐相机环境覆盖/调试，不代替世界空间 Region。

Feature 固定存在，具体捕获/上传/模拟仍按变化和实际需求安排。初版可用区域组件/Registry + 共享采样函数与必要数据发布，无需先分配常驻体积 RT 或新增大气 Feature。灰尘状态/模拟由独立 world runtime 维护，每个逻辑时间步更新一次；Feature 发布供相机观察的数据，不在每次相机天空 pass 中重复推进。

区域用法、`HoAtmosphereMediumResources` / CPU 快照 / HLSL 求值与当前边界见 [Ho-VolumeLight](Ho-VolumeLight.md)。下面的环境使用/API/验证保持既有契约。后续完整职责见 [VolumeLight 规划 §14](计划/Ho-VolumeLight-源码研究与模块规划.md#14-hoatmosphere-共享空间属性最新模块边界)。

## 使用

在 Renderer 上添加 `HoAtmosphere`。默认读取 `RenderSettings.skybox`，即 Lighting 中的 Unity 天空盒材质。支持通过原天空盒 shader 捕获 Cubemap、全景图与程序天空，保留材质自己的 decode、曝光、旋转和 tint。

其他来源为指定天空盒材质、原始 HDR Cubemap 或常量环境。原始 Cubemap 必须已经是可直接采样的 radiance；RGBM 编码资源应通过天空盒材质解码。常量黑色是有效环境，缺失材质/贴图则是无效环境。

Feature 中的强度、染色与水平旋转是兜底值；`HoAtmosphereVolume` 可逐相机覆盖。调试位于 Volume，提供 Radiance 经纬图、Directions、Availability 和 All。Radiance 调试保留 HDR 值，不把显示用的 tone mapping 写回环境输入。

## 生产与缓存

- 生产 pass 固定在 `AfterRenderingPrePasses`，早于 GB/SB 与未来 RT 消费，不依赖主相机 color。
- 天空材质只画天空到六面 HDR RT，不捕获场景几何。默认每面 256；实际分辨率取 16～1024 范围内的二次幂。
- 首选 RGBA16F，不支持时尝试 RGBA32F；两者都无法渲染/采样则输出无效环境。
- 原始 Cubemap 直接引用，不额外复制。常量来源也不分配六面捕获图。
- `OnChange` 检查来源实例、材质 CRC、纹理更新计数与主方向光参数。仅修改 HoAtmosphere 的强度/染色/旋转不重新捕获。
- `EveryFrame` 适合依赖时间或全局变量的动态天空；`Manual` 保留显式刷新的控制。`RefreshEnvironment()` 和 Inspector 的刷新按钮会使缓存失效。
- 更新方式只控制材质捕获；直接引用的 Cubemap 始终跟随原纹理内容，不能用手动模式冻结外部纹理。
- 未写进材质的任意 shader global 不在自动检测范围内，应使用每帧更新或显式刷新。
- 只对启用的 Base Game / SceneView 相机生产。Overlay / Reflection / Preview 等相机不会继承上一台相机的有效性；关闭或销毁 feature 时也复位。

天空捕获图可跨相机复用；采样参数和有效性按相机发布。Revision 是内容与求值参数的相等性标记，不承诺单调递增；仅用于判断历史是否需要失效。

256 分辨率的 RGBA16F 六面图占约 3 MiB，RGBA32F 约 6 MiB，不含驱动额外开销。每次更新绘制六面；缓存命中时只发布输入。RenderGraph 捕获使用显式 CubemapFace 绑定，避开本地 D3D11 的 raster attachment 面选择问题；主相机矩阵在捕获后恢复。

## 消费契约

RenderGraph 消费者从 `ContextContainer` 获取 `HoAtmosphereRenderGraphResources`，检查 `IsValid`，对 `radianceTexture` 声明 `UseTexture(..., AccessFlags.Read)`。Raster pass 可调用 `HoAtmosphereBindings.Bind`（需要声明全局状态修改）；未来 Compute / RayTracing pass 应将相同纹理和向量显式绑定到对应 shader，不依赖 raster 的全局绑定代替 kernel 参数。资源句柄只在当前相机的 graph 内有效，不能缓存到下一帧。

HLSL 引入 `Runtime/Atmosphere/Shaders/HoAtmosphereSampling.hlsl`：

```hlsl
bool valid = HoAtmosphereAvailable();
float3 radiance = HoAtmosphereSampleRadiance(directionWS);
```

方向为世界空间、Unity 的 +X / +Y / +Z 轴。正 90 度水平旋转将原 +Z 内容放到世界 +X。函数归一化输入方向；零方向与无效来源返回零。输出是非负、线性 HDR 的未卷积 radiance，不含接收面的 BRDF、曝光或最终相机后处理。

底层全局绑定为 `_HoAtmosphereRadianceTexture`、`_HoAtmosphereParameters`、`_HoAtmosphereTint`、`_HoAtmosphereConstantColor`。立方体保留来源原始结果，额外旋转/染色/强度由共享求值函数应用；消费者不应只采 cubemap 而遗漏这些参数。兼容模式的后续 pass 使用同一 HLSL 契约。

目前只有 mip 0，没有漫反射卷积、镜面粗糙度预滤波或环境重要性采样 PDF。未来环境灯采样须添加匹配立体角的分布；不能把当前裸图当成已经完成 ReSTIR 的环境灯接口。

## 旧输入的迁移边界

| 现有使用者 | 当前输入含义 | 迁移方式 |
| --- | --- | --- |
| RTDI / RTGI 新功能 | 世界方向的远场 radiance | 直接使用本契约；世界空间遮挡由 RT 查询负责 |
| SSGI 天空回退 | GB 的屏幕内、几何遮罩后的天空 | 保留屏幕可见性判定后再考虑方向查询；不能将所有屏幕追踪 miss 当成无遮挡天空 |
| ScreenProcess SkyTyndall | 当前视角的天空遮罩与深度 | 需要屏幕方向投影与遮罩适配，不直接替换成 cube |
| GB SkyCapture | 当前相机已经画出的可见背景 | 继续保留，与全方向环境不是同一资源 |
| PlanarReflection | 反射相机中的场景和天空 | 保留反射视图生产，不用远场 cube 替代场景反射 |
| SH / APV / 预滤波 IBL | 已积分或带粗糙度的环境响应 | 可统一来源，但仍保留各自积分语义，避免重复叠加 |
| ImageProcess SkyGodRays | 屏幕风格化滤镜 | 不作为物理环境消费者迁移 |

本阶段不为 AC 复制环境输入，不加入 SSS，不设计 lilToon 的 DI/GI 接收和最终合成。

## 验证

隔离工程入口为 `lilToon.URP.Extensions.Atmosphere.HoAtmosphereValidation.Run`；兼容模式使用 `RunCompatibility`，要求工程定义 `URP_COMPATIBILITY_MODE`。测试在 RenderGraph 存活期间采样并写入持久目标再读回，不能用 CPU `Shader.GetGlobalTexture` 代替 GPU 资源验证。

Unity 6000.3.15f1 下，D3D12 RenderGraph、D3D11 RenderGraph 与 D3D11 compatibility 均通过。覆盖六面中心/斜向/角点方向与 HDR、Unity 三类天空来源、参数与 Volume 覆盖、缓存/刷新、跨相机、纹理内容变化、原生存储恢复、主相机绘制（含半分辨率）、缺失/禁用/销毁回退与调试显示。测试日志位于隔离工程 `.codex-research/RTInputValidation/atmosphere-*.log`，不提交生成的工程缓存。
