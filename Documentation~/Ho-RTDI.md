# Ho-RTDI：参考工程移植初版

2026-10-18：[参考工程收敛对照与静止历史修复](Ho-RTDI-ReferenceConvergence.md)已交付。开启NRD时，稳定场景改用较长漫反射历史；固定夹具静止噪声方差约降至旧版的1/19，两条Mono Player路径已验证。严格遮挡阶跃和混合天空镜面仍有明确失败，最终材质合成仍未接入。

2026-10-15状态：[完成度审计](Ho-RTDI-CompletionAudit-2026-10-15.md)。独立DI/受控Mono Player已有交付，mixed镜面质量、正常帧运动合同和有效GPU预算仍未完成；最终材质合成此前不在DI推进范围。持续实验已转为交付审计。

本版沿用用户提供的 `UnityPathTracing-6000.3.15` 的 Unity RTXDI 路径。直接复用 RTXDI 的初始采样、reservoir、时间/空间重采样；适配 RAB bridge 和 pass 绑定，不另写 ReSTIR。已补齐静态几何下动态解析灯的历史数据与索引映射；最终 lilToon 接收/合成尚未接入。GI 是独立 Feature，见文末链接。

## 使用与输出

Renderer 上添加 `Ho-RTDI`，在其前面启用 GB / SB。需要 D3D12 inline RT、Mono、同分辨率单采样的 GB/SB 及已更新的 SB 网格法线布局。世界求交已支持刚性/骨骼opaque和基础lilToon Cutout，具体材质边界见[Cutout说明](Ho-RTDI-Cutout.md)。Cutout pose/transform的NRD历史以[真实最终可见性confidence](Ho-RTDI-VisibilityConfidence.md)处理，实例/源mesh结构变化仍重置；最终NPR接收/合成未接入。

「天空环境光」默认开启，有效输入来自前置Ho-Atmosphere；没有输入时仍只运行解析灯。「环境光候选数」独立于局部预算。当前支持HDR环境PDF/RIS采样、真实世界可见性与独立raw/NRD输出；概率单位、成本、验证及与旧GI天空项的重叠见 [Ho-RTDI-Environment](Ho-RTDI-Environment.md)。

Feature 默认只生产独立直接光，主相机基础画面保持原有渲染。全局 Volume 的 `Ho-RTDI` 提供启用覆盖和 Diffuse / Specular / Combined / Receiver / RawDiffuse / RawSpecular / All 调试；Receiver 绿为有效接收面、红为无效，能区分有效暗部与输入不全。调试在最终后处理后显示，不能把它当成已完成的 NPR 合成。

`finalSampling`默认单样本，可选择三样本以增加两次解析灯最终可见性查询、降低动态多灯噪声。Opaque局部消费，Cutout按相机持有8-byte/像素第二辅助，Sky-only和确定性单灯保留原路径。实际接入、Player验证、显存及仍存在的质量问题见[三最终样本](Ho-RTDI-RuntimeMultiSamples.md)；尚无GPU毫秒收益结论。

合并模型中的天空壳可在「高级 → 不参与求交的材质」指定其材质。相同材质的子网格会从DI求交登记中排除，主画面仍正常绘制；保留的子网格继续遮挡。列表默认空，详见[材质排除与古堡审计](Ho-RTDI-MaterialExclusion.md)。

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
| 前后帧灯数据 / 索引 | 按 SDK `RAB_LoadLightInfo(previousFrame)` / `RAB_TranslateLightIndex` 契约提供实际快照与双向映射；参考 Unity 示例该处原本只做静态 identity 映射 |

SDK 文件只改 include 路径，保留原始算法与许可。`Runtime/RTDI/ThirdParty/provenance.json` 保存原文件 SHA-256，许可说明见同目录 NOTICE 和 NVIDIA LICENSE。只引入 DI 依赖闭包，没有把参考项目的 PT/native/DLSS/NRD 框架搬进来。

## 当前支持域

- 显式 point、spot、directional 灯；point/spot 在 local 分区使用均匀或功率提案，directional 在独立的 infinite 分区均匀选择，由 SDK 合并两个估计。局部灯候选预算 1～32，有方向灯时另取一个方向灯候选。point/spot 使用参考 delta point-light 的 inverse-square 极限，并适配 Unity range/spot cone；directional 当前为 delta 方向光。遵循参考 RAB 的 RT 可见性，独立于 Unity raster `Light.shadows` 开关。
- Cookie、非全局 cullingMask、baked-only 和其他灯型排除，Feature 状态显示排除计数。Rendering Layer/艺术灯光链接、面灯/发光网格尚未接入；当前用于默认灯光链接的参考场景。
- Ho-Atmosphere 提供有效 HDR 时可采环境灯，默认 4 个天空 PDF/RIS 候选与 4 个 BRDF/VNDF 候选，通过 SDK MIS 合并；mesh-light candidates 仍关闭。输入版本变化清历史，不复制另一份天空，详见 [环境光](Ho-RTDI-Environment.md)。
- 世界遮挡支持不透明 MeshRenderer 与 SkinnedMeshRenderer 当前姿态；基础 lilToon Cutout 使用原生材质最终可见性，纹理/UV 不另复制，支持范围与成本见 [Cutout](Ho-RTDI-Cutout.md)。蒙皮更新有 CPU/自有网格成本，姿态变化清历史；血月古堡的天空壳/合并网格不作为阴影画质基准，见 [场景支持](Ho-RTDI-SceneSupport.md)。
- 使用明确的 PBR 参考响应：主色在此响应层映射到 0～1，按 metallic 分出 diffuse 与 F0，perceptual roughness 交给参考 GGX。没有修改 SB 原值或设计 NPR ramp、影色与接收路由。Emissive 不作为 DI 自发光叠加。
- 可选 NRD RELAX，默认关闭，接入与限制见 [NRD 初版](Ho-RTDI-NRD.md)。raw ReSTIR 输出仍可有噪声。单个像素的 colored-light 或重采样结果不要求逐帧等于所有灯的精确和，应以统计均值核对能量。
- 单盏 point/spot/directional 且没有有效环境时，跳过无收益的 reservoir 时空复用与 RELAX，原发布输出保留实际 raw 硬阴影；多灯/天空仍用原完整路径。触发条件、验证与性能限制见 [单灯精确路径](Ho-RTDI-SingleDelta.md)。

## 历史、资源与调度

采样四阶段在一个 graphics-queue unsafe pass 中有序执行，位置为 BeforeRenderingPostProcessing；主表面/AS/灯数据和输出均显式声明依赖。功率模式在同一 pass 内先执行 PDF build / GenerateMips / SDK Presample。初始化写 reservoir 1，temporal 写 2，spatial 写回 1，shade 保存历史 0。SDK reservoir stride 为 24 B，保存三份；只额外保存上一帧必要的 surface guide（raw depth、两种 2×16-bit octa normal、参考材质，20 B/像素），不物化另一套当前主表面属性。

历史逐相机拥有。分辨率、场景 Revision、初始采样数或 ray bias 变化会使旧历史失效；禁用后重新启用也从新历史开始。静态世界的相机运动使用实际前后矩阵生成 SDK 所需的像素位移/深度差，未使用或重新生产 URP motion。几何变换时保守清历史，不宣称支持动态角色 temporal。

灯的位置、颜色、强度、受支持的 delta 灯型变化，以及增删/启停/筛选导致的列表变化，不再整份清历史。每相机保存上一轮实际渲染的灯参数快照，分别绑定 current / previous light buffers；SDK 需要旧 target PDF 时读取真实旧数据。双向索引映射按 Light 的托管引用身份建立，灯在另一轮不存在就返回 -1，由 SDK 丢弃该样本，避免把删除/替换的灯误认成占据同一数组槽的新灯。容量扩展也保留 reservoir 历史。

快照在 pass 记录时捕获，完成调度后推进到下一轮；GPU 上传、两个映射及其 RenderGraph 依赖都显式记录。GI 只在二次命中点调用 DI 初始采样，因此在其共享 bridge 中明确关闭 DI light-history 分支，不增加 GI 资源或改变其历史策略。

没有 previous TLAS；几何变化仍清历史。允许复用时世界几何未变，当前 AS 同时描述上一轮世界，可从旧表面向旧灯做 temporal visibility。此处支持的是动态灯光与静态世界相机重投影，不能据此宣称支持动态角色/几何。

RGBA16F 输出约 16 B/像素（diffuse/specular 两张）；history 16 B/像素；reservoir 三份约 72 B/像素，另有 block padding、灯和 AS。1080p 三类合计约 206 MiB，不含 AS/其他输入/驱动开销。灯历史额外保存一份 64 B/灯参数及双向 4 B/索引，容量取二次幂，另外有 CPU 快照；没有复制主表面图。Windows x64 Mono Player 的受控 64² 输出链路已验收；尚未做生产性能预算。

## 验证

隔离入口 `HoRTDIValidation.Run` / `RunCompatibility` / `RunUnsupported`。验证真实 SDK 四阶段、单灯能量与世界遮挡、多灯统计均值、spot/方向光、独立 specular、历史累积/清理、静态世界相机重投影、输入域拒绝、无自动 camera composite、Volume 调试和资源关闭/销毁。Compute 编译警告和错误均作为失败。

Unity 6000.3.15f1 的 D3D12 RenderGraph / compatibility 均通过；D3D11 不支持分支通过。多灯测试比较空间/时间样本均值，不把单像素噪声误判为能量错误。SDK/Donut 共 22 个源码文件的 SHA-256 与依赖 include 之外的算法正文已逐一核对。

动态灯验证覆盖连续移动、变色/强度/delta 灯型变化、移动进入静态遮挡、增删/替换、空集合恢复与容量增长；实际读回 GPU 双向映射和旧灯参数。空间复用关闭时，检查时间阶段输出的 M 与 age 确认旧样本被选中；最终 shading 重测 visibility 会按 SDK 重置 visibility age，因此不拿最终历史槽中的 age 判断是否复用。动态 target PDF 的 ray-traced MIS 结果存在单像素方差，能量以区域统计均值核对；此处尚未降噪。

分区验证在关闭时空复用、局部候选预算为 1 的条件下，加入 64 盏超出作用范围的局部灯及一盏方向灯，首帧仍得到正确的太阳贡献。混合有效局部灯后的统计均值约 0.36409，参考值 0.36480。方向灯改为点灯时会重排数组，双向历史映射与旧灯型快照也经过 GPU 核对。仅方向灯时仍维持非零的 local 候选参数，以满足 SDK 初始 MIS 计数的除法约定；空 local region 本身由 SDK 跳过，不生成假的局部灯贡献。

分区本身不新增 GPU buffer、pass 或 Feature。后续局部灯采样质量与 NRD 接入的取舍见 [DI 持续推进记录](计划/Ho-RTDI-持续推进记录.md)。

## 可选的局部灯功率采样

Feature 的「局部灯采样」可选择均匀/功率，默认均匀。功率模式沿参考 PdfTexture → box-filter mip chain → Presample 流程，直接调用 SDK `RTXDI_PresampleLocalLights`，初始采样使用 SDK `POWER_RIS`。PDF 为 R32F 的 Morton 方形域；点灯权重使用参考的 4π × 强度亮度，spot 再乘本 bridge 的平方 cone 衰减积分。该权重仅是提案，不改灯光着色；SDK 保留逆选择 PDF 以核对能量。它不包含接收点的距离/BRDF/遮挡，不能取代 ReGIR。

每相机增加 64 × 256 个 uint2 RIS 条目（128 KiB）与一张按局部灯数取 2 次幂的 R32F 完整 mip PDF。每轮按当前灯参数重建，padding 清零；灯重排、功率变化和全黑根节点均经过验证。无局部灯或缺少 R32F UAV/过滤支持时实际使用均匀路径；状态显示实际模式。切回均匀会释放 PDF 与完整 RIS，只保留供编译后资源绑定的单条 8 B dummy buffer。GI 保持未启用 presampling 的编译分支。

均匀模式保持原有四个全屏 kernel；功率模式增加 PDF build、mip generation 与 16384 个预采样线程，不增加全屏 GBuffer、副 Feature 或原生 DLL。模式切换清 reservoir 历史，动态灯本身的参数变化继续使用既有前后快照/映射。

1 盏强灯与 63 盏弱灯同位置、每像素 1 候选、关闭时空复用的基准中，以每像素数学参考归一化后，均匀均值约 1.0033、方差 55.8132；功率均值约 0.9995，方差在 F6 打印精度下为 0。该场景的功率提案与目标几何因子匹配，不能据此推断任意场景都接近零方差。

完整 Power 模式的 D3D12 RG/compatibility、Gamma/Linear，以及 Uniform 两条路径回归通过；包含全黑 PDF、强灯身份互换并保留历史、完整 mip 链、资源释放、动态灯/类型跨分区、旧功能能量及 GI 回归。基准 Stopwatch 包含渲染请求、同步读回、CPU 开销和首次使用成本，不是 GPU kernel 时间；预采样的固定开销是继续保留均匀默认值的原因之一。尚无生产帧率承诺。

共享 SDK 目录为 GI 追加 6 个、为 DI 功率预采样追加 1 个必要头文件（provenance 现共 29 个文件），原始算法均保持不变。参考 GI 链路已接入首版一跳 diffuse 输出，见 [Ho-RTGI](Ho-RTGI.md)；两者均尚未接入最终 PBR/NPR 合成。


## 可选 NRD RELAX

开启 Feature 的 NRD RELAX 降噪后，公开 diffuse/specular 发布 filtered，额外 raw handles/globals 保持原始结果。当前已验证静态 Mono、D3D12 两条路径、Editor Gamma/Linear，以及 Windows x64 release Player 实际执行；IL2CPP、TAA/XR/动态几何与目标性能尚未验收。NRD 只过滤独立 DI 并恢复同一参考材质因子，不负责最终 lilToon 合成。信号、ABI、资源成本、测试与限制详见 [Ho-RTDI-NRD](Ho-RTDI-NRD.md)。

Feature 序列化引用自身 debug shader 和网格标签 shader，Player Raw / Filtered / Receiver / All 网格调试已验收。升级旧 renderer 时，Create 或打开 Feature Inspector 会补齐缺失 shader 并标记待保存；保存后再构建。已有 override 保留，重复检查不会修改完整资产。尚未加入构建时全工程资源迁移。[隔离重载与发布验证](../Tests~/Player/RTDI/README.md)。
