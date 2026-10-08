# Ho-RTDI：参考工程移植初版

本版沿用用户提供的 `UnityPathTracing-6000.3.15` 的 Unity RTXDI 路径。直接复用 RTXDI 的初始采样、reservoir、时间/空间重采样；适配 RAB bridge 和 pass 绑定，不另写 ReSTIR。尚未接入 GI 或 lilToon 最终接收/合成。

## 使用与输出

Renderer 上添加 `Ho-RTDI`，在其前面启用 GB / SB。需要 D3D12 inline RT、Mono、同分辨率单采样的 GB/SB 及已更新的 SB 网格法线布局。当前世界求交仅支持 HoRTScene 的刚性 opaque MeshRenderer 范围。

Feature 默认只生产独立直接光，主相机基础画面保持原有渲染。全局 Volume 的 `Ho-RTDI` 提供启用覆盖和 Diffuse / Specular / Combined / Receiver / All 调试；Receiver 绿为有效接收面、红为无效，能区分有效暗部与输入不全。调试在最终后处理后显示，不能把它当成已完成的 NPR 合成。

RenderGraph 读取 `HoRTDIRenderGraphResources.diffuse / specular` 并声明读依赖。RGB 为线性 HDR、已经包含参考接收 BRDF 的贡献；A 为接收面有效性，阴影中的有效表面可以 RGB=0、A=1，输入缺失/取样域不匹配为 A=0。兼容全局为 `_HoRTDIDiffuseTexture`、`_HoRTDISpecularTexture` 和 `_HoRTDIActive`。

## 哪些沿用参考工程

| 环节 | 来源 / 本版处理 |
| --- | --- |
| 初始灯候选与 reservoir streaming | SDK `DI/InitialSampling.hlsl`，调用 `RTXDI_SampleLightsForSurface` |
| reservoir 打包、布局、读取 | SDK `DI/Reservoir.hlsl` / `ReservoirStorage.hlsl`，三份 block-linear reservoir |
| 时间复用 | SDK `RTXDI_DITemporalResampling` |
| 空间复用与 bias correction | SDK `RTXDI_DISpatialResampling`，ray-traced correction |
| RNG 与邻域 | SDK RNG；host offsets 适配参考 RtxdiUtils 的 R2/SNORM8 序列 |
| BRDF 和最终着色 | 参考 RAB_Surface / ShadingHelpers 的 Lambert + Donut GGX、最终可见性与 reservoir inverse PDF |
| 主表面 | RAB_GetGBufferSurface 改读 HoRTPrimarySurfaceInputs，不重新渲染/复制当前 GBuffer |
| visibility | RAB_GetConservativeVisibility 改接 HoRTScene 的真实世界求交 |

SDK 文件只改 include 路径，保留原始算法与许可。`Runtime/RTDI/ThirdParty/provenance.json` 保存原文件 SHA-256，许可说明见同目录 NOTICE 和 NVIDIA LICENSE。只引入 DI 依赖闭包，没有把参考项目的 PT/native/DLSS/NRD 框架搬进来。

## 当前支持域

- 显式 point、spot、directional 灯；均匀选择解析灯，point/spot 使用参考 delta point-light 的 inverse-square 极限，并适配 Unity range/spot cone；directional 当前为 delta 方向光。遵循参考 RAB 的 RT 可见性，独立于 Unity raster `Light.shadows` 开关。
- Cookie、非全局 cullingMask、baked-only 和其他灯型排除，Feature 状态显示排除计数。Rendering Layer/艺术灯光链接、面灯/发光网格与天空重要性采样尚未接入；当前用于默认灯光链接的参考场景。
- 初始采样参数明确关闭 environment / mesh-light / BRDF light candidates；相应 RAB 接口只有禁用分支，不伪造这些光照贡献。Ho-Atmosphere 的输入保持独立，当前不作为 DI 环境灯。
- 使用明确的 PBR 参考响应：主色在此响应层映射到 0～1，按 metallic 分出 diffuse 与 F0，perceptual roughness 交给参考 GGX。没有修改 SB 原值或设计 NPR ramp、影色与接收路由。Emissive 不作为 DI 自发光叠加。
- 无降噪；raw ReSTIR 输出可有噪声。单个像素的 colored-light 或重采样结果不要求逐帧等于所有灯的精确和，应以统计均值核对能量。

## 历史、资源与调度

四阶段在一个 graphics-queue unsafe pass 中有序执行，位置为 BeforeRenderingPostProcessing；主表面/AS/灯数据和输出均显式声明依赖。初始化写 reservoir 1，temporal 写 2，spatial 写回 1，shade 保存历史 0。SDK reservoir stride 为 24 B，保存三份；只额外保存上一帧必要的 surface guide（raw depth、两种 octa normal、参考材质，16 B/像素），不物化另一套当前主表面属性。

历史逐相机拥有。分辨率、场景 Revision、灯集合/位置/颜色/强度、初始采样数或 ray bias 变化会使旧历史失效；禁用后重新启用也从新历史开始。静态世界的相机运动使用实际前后矩阵生成 SDK 所需的像素位移/深度差，未使用或重新生产 URP motion。几何变换时保守清历史，不宣称支持动态角色 temporal。

灯变化当前清历史，因此 previous-light 查询只在灯数据完全不变时复用当前 buffer，尚未实现完整的前后帧灯索引重映射。没有 previous TLAS；几何变化清历史后用当前世界 visibility。该限制需要保留，不能据“调用了 temporal”宣称支持完整动态场景。

RGBA16F 输出约 16 B/像素（diffuse/specular 两张）；history 16 B/像素；reservoir 三份约 72 B/像素，另有 block padding、灯和 AS。1080p 三类合计约 206 MiB，不含 AS/其他输入/驱动开销。尚未做生产性能预算与 Player 验收。

## 验证

隔离入口 `HoRTDIValidation.Run` / `RunCompatibility` / `RunUnsupported`。验证真实 SDK 四阶段、单灯能量与世界遮挡、多灯统计均值、spot/方向光、独立 specular、历史累积/清理、静态世界相机重投影、输入域拒绝、无自动 camera composite、Volume 调试和资源关闭/销毁。Compute 编译警告和错误均作为失败。

Unity 6000.3.15f1 的 D3D12 RenderGraph / compatibility 均通过；D3D11 不支持分支通过。多灯测试比较空间/时间样本均值，不把单像素噪声误判为能量错误。SDK/Donut 共 22 个源码文件的 SHA-256 与依赖 include 之外的算法正文已逐一核对。

共享 SDK 目录随后为 GI 追加 6 个必要头文件（provenance 现共 28 个文件），DI 的原始 22 文件依赖闭包保持原算法不变。参考 GI 链路已接入首版一跳 diffuse 输出，见 [Ho-RTGI](Ho-RTGI.md)；两者均尚未接入最终 PBR/NPR 合成。
