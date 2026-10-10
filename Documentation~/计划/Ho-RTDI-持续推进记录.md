# Ho-RTDI 持续推进记录

开始日期：2026-10-09。用户授权持续推进 DI；每一步先评审必要性与参考，再实现、验证、复查并独立过滤提交。只提交本步自己的改动，保留其他任务的工作区内容。最终 lilToon PBR/NPR 接收/合成、通用 PT 框架不在当前范围；GI 仅做共享代码的必要兼容与回归。

## 0. 起点复查

起点提交 dd0ea39 已提供静态世界中的动态解析灯历史快照与双向索引映射，前后灯数据由实际渲染轮推进；删除/替换灯映射为 -1。重采样保持原 SDK。现有验证覆盖旧参数、GPU 双向映射、时间阶段 M/age、动态能量与遮挡，不把最终 visibility 更新后重置的 age 错当成未复用。

仍有的实际缺口：全分辨率 raw 输出有方差；局部灯候选均匀采样，灯数/功率跨度大时可能退化；无降噪、生产性能与 Player 验收；不透明刚性支持域和灯光链接限制仍在。工作重点是提升 DI 本身，不把最终合成提前混进来。

## 1. 局部灯 / 方向灯分区采样

**动手前评审：应做。** 原实现把方向灯放在局部灯的均匀池里。局部灯数量大时，方向灯被选中的概率低，既浪费候选，也影响首帧出光。这属于已有 SDK 适配缺口，可在不增加 pass/buffer/Feature 的情况下修复，收益可单独验证。

**参考核对：**

- 用户参考工程 Assets/RTXDI/Scripts/PrepareLight/LightScene.cs 的 GetLightBufferParameters 分别填写 localLightBufferRegion / infiniteLightBufferRegion。
- 同工程 Rtxdi/DI/ReSTIRDIContext.cs 默认使用 8 个 local 候选与 1 个 infinite 候选。
- 原 SDK DI/InitialSampling.hlsl 的 RTXDI_SampleLightsForSurface 已调用 RTXDI_SampleInfiniteLights 并合并估计；继续复用这段算法，不重写采样合并。
- [NVIDIA RTXDI 集成说明](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/Integration.md) 明确区分局部与无限灯光。在线资料仅用于核对契约，实现仍使用本地已校验的 SDK 版本。

**实现：** 把当前灯数组排列成 point/spot 在前、directional 在后的连续分区，传入 local count 与 infinite offset/count。存在方向灯时独立生成一个 infinite 候选，局部候选数沿用原预算。灯跨分区换类型时继续按引用身份映射历史索引。空 local region 仍保留非零 local 参数，避免 SDK ComputeInitialSamplingMisData 的零 MIS 计数；SDK 自身跳过空 region。当前没有环境灯、BRDF 候选，因此这项处理不改变其他采样项。

**验证结果：** D3D12 RenderGraph / compatibility，Gamma / Linear 均通过；D3D11 不支持分支与 GI 两条路径回归通过。64 盏无贡献局部灯加一盏太阳、局部候选数为 1、时空复用关闭时，首帧太阳贡献正确。混合有效局部灯的统计均值 0.36409，数学参考 0.36480。还核对了方向灯改点灯导致的 GPU 索引重排、旧灯型快照和历史保留。Compute 警告/错误作为失败条件。

**完成后复查：** 改动限定在 DI 灯分区、初始采样参数、设置说明和验证。SDK 代码未改；没有增加全屏纹理、GPU buffer、pass、原生 DLL 或合成行为。收益是降低方向灯遭局部灯稀释的方差，不是消除所有噪声。多个方向灯仍从该分区抽一个候选；局部灯仍均匀采样。initialSamples 现在明确表示局部灯候选预算，方向灯候选为额外固定预算。

## 2. 局部灯功率提案

**动手前评审：应做，先量化。** 仅分离方向灯不能解决强弱局部灯混采的问题。基准使用同位置/同颜色的一盏强灯和 63 盏弱灯，关闭时空复用、局部预算 1，以逐像素数学参考归一化，避免把几何梯度当噪声。先测得均匀均值 1.003284、方差 55.813219，证明能量正确但候选选择方差很大。

**参考核对：** 用户工程的 PdfTexture.compute、Presample.compute / PresampleLightsPass；原 SDK PresamplingFunctions.hlsl、LocalLightSelection 的 POWER_RIS 分支；[NVIDIA 的 PDF mip / RIS 集成说明](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/Integration.md)。仅追加一个原 SDK header，29 个原文件的 SHA-256 与 include 之外正文均核对不变。

**实现：** 增加可选 Power 模式，Uniform 保持默认。GPU 写 Morton R32F power PDF，原生 box mip 后调用 RTXDI_PresampleLocalLights，复用 SDK 的逆 PDF、RIS tile 选择与原初始/时间/空间重采样。点灯使用参考功率表达式；spot 按当前平方 cone 模型积分得到提案权重。compact light data 可选项关闭，不复制另一份灯数据。固定 64×256 RIS 条目，每相机 128 KiB，另有随灯数增长的 PDF/mips；无局部灯或格式不支持时使用 Uniform。

**验证结果：** Power 归一化均值 0.999520，方差在 F6 打印精度下为 0；合并灯能量、全黑 PDF、强灯身份交换后保持历史、mip 完整性、资源切换释放、动态灯与分区、两条路径及 Gamma/Linear、D3D11 禁用和 GI 回归通过。基准时长包含 CPU、读回同步及首次使用，不是 GPU kernel benchmark；预采样确有固定开销，未据此切换默认模式或承诺生产 FPS。

**完成后复查：** padding 每轮清零，PDF/RIS 用当前灯顺序与参数；过去的提案概率已包含在 reservoir weight 内，不错误重用当前 PDF 作为过去的概率。2D PDF dispatch 避免大图超过单轴 dispatch group 上限。模式切回 Uniform 释放完整资源，保留 8 B dummy 以满足已编译 shader 的绑定。GI 编译宏保持 presampling 关闭。参考算法和原有输出含义不变。

一次 compatibility 运行在打印 PASS、完成清理后，Unity 的 WorkerManagerASIO 退出栈发生原生崩溃；旧日志保留为 rtdi-power-full-compat-d3d12-exit-crash.log。已重新执行，最终验证要求 PASS、无 Shader 诊断且进程 exit=0，未把仅有 PASS 的崩溃进程计为通过。

**限制：** 此基准中提案和目标几何因子匹配，所以降方差极明显。功率忽略距离、可见性和接收 BRDF；同功率远近灯、被遮挡强灯仍可退化。当前每轮重建 PDF/mips 并预采样，尚未做目标分辨率的 GPU budget、静态 PDF 缓存或自适应 tile 配置。

## 3. NRD RELAX 实际接入

**动手前评审：应做，先核 ABI 与信号。** raw DI 仍有可见方差，单靠提案改进不能覆盖各种灯/材质/遮挡分布。当前 alpha 是接收有效性、RGB 已含参考 BRDF，不能直接作为 NRD 输入。参考 Denoiser.dll 同时包含 NRI/DLSS/NIS、Unity heap hook 和内存控制，完整搬入会超出 DI 目标。

**参考与路线验证：** 读取用户工程 NrdDenoiser / NrdFrameData / NrdPass、原 NRD.hlsli 和 Include ABI；实际调用参考 NRD.dll，确认 4.17.3、编码 2/1、15 个 RELAX pipeline 标识及资源池。选用核心库 CPU 调度 + Unity CommandBuffer 执行原 shader 的路线，完全保留 SDK RELAX 算法，只适配其 custom-engine bindings，不导入 NRI、Denoiser、DLSS/NIS 或 descriptor-heap hook。

**实现与验证：** 新增可选 NRD 开关（默认关）。ShadeNRD 用 SDK material factors 解调，提供真实选中灯距离、编码 normal/roughness、正 viewZ 与静态 world-space 零 MV；原始输出保留。核心库选择 pass/资源/常量，Unity 执行，Resolve 恢复同一因子并写回 receiver alpha。固定版本 ABI、结构大小和 shaderIdentifier 检查；33 个 shader/config/MathLib 原文与 3 个 API header、原 CPU DLL 均记录 SHA。15 个 kernel 编译、实际 GPU 调度、RG/compatibility、Gamma/Linear、镜面/金属、透视、相机移动、域拒绝、raw/filtered 调试与关闭重建通过；原有 DI/power/动态灯与 GI 回归通过。

**发现与修正：** 参考核心库 GetResourceTypeString 的字符串表仍按旧枚举排序，不能据它绑定纹理。使用固定 NRDDescs.h 数值顺序生成 manifest，并由真实 dispatch 验证。默认 50 px specular prepass 在 64² 干净高光场景损失约 25% ROI 均值；dense DI 两个通道都有距离，关闭该 prepass 后约 7%，保留其他默认 temporal/history/A-trous。相应镜面测试已收紧，没有放宽门槛掩盖问题。

**量化结果与限制：** 双色灯 raw 红通道 MSE 0.050401，filtered 0.000049；红/蓝均值约 0.22549/0.22642，数学参考约 0.22。镜面 raw 0.296814，dense-DI filtered 0.276015。滤波仍有空间偏差，尤其默认 diffuse prepass 与材质因子下限；这些数字不是全场景保真或生产性能结论。NRD 增量资源约 277 MiB/1080p，另有 managed 分配和驱动开销。首次手写 plugin meta 不符合 Unity 6 serializedVersion 3 字典格式，已由 PluginImporter 生成并限定 Windows x64，不把测试失败计作通过。

**完成后复查：** 只有 DI 增加信号与降噪调度，未改 GI、最终材质接收、CS 或 ShadowCast。核心库不持有 Unity GPU 设备、command list 或 descriptor heap；native 返回的本轮裸数据及时复制，pass 使用各自 CB 偏移与三帧常量 buffer。NRD 只在开启时加载与分配，缺库/错误 ABI/资源不支持显示状态并回退 raw。仍限静态 Mono；Player、TAA 显式契约、动态几何、真实材质边界与 GPU budget 不能因 Editor 测试通过就视为交付。详见 [NRD 接入说明](../Ho-RTDI-NRD.md)。

## 4. Windows release Player 部署

**动手前评审：应做。** Editor 的 AssetDatabase 自动补 shader、已加载的核心库及即时 shader 编译会掩盖 Player 缺口。NRD 上一步只能算 Editor 交付；需要普通保存的 renderer asset 和独立 EXE 才能证明当前输出链路可部署。只使用隔离工程，不改用户场景、不扩展合成或 PT 框架。

**参考核对：** 再读用户参考的 NativeRtxdiFeature / NrdDenoiserHelper 原生入口与插件引用，保留当前独立 CPU core 路线；对照 [Unity PluginImporter](https://docs.unity.com/en-us/engine/6000.3/script-reference/unityeditor/pluginimporter) 和 [BuildPipeline](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityeditor/buildpipeline) 的普通打包契约。[LightBakingOutput](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/LightBakingOutput.html) 与本地 URP ForwardLights 使用 bakingOutput 读取运行时烘焙状态。

**实现与发现：** 增加隔离构建入口和 Unity 忽略的 Player probe 模板，不把捕获 helper 加入包 Feature 菜单。分别保存 RG / compatibility 模式并构建 Win64 Mono release，检查 renderer 中 DI / 15 个 NRD compute 引用，不用 Always Included 保留整个包。首次真实构建暴露 DI/GI 直接读取 Editor-only Light.lightmapBakeType，已修为 Player bakingOutput.lightmapBakeType，Editor 继续用设置值拒绝尚未烘焙的 baked-only 灯。GI 仅做这一必要编译修正，两条 GI 回归通过。测试脚本的运行时兼容模式切换被 Unity 拒绝，改为两个独立构建；compatibility Blit 不能把 shader-global 名当 temporary RT，用测试 shader 直接采样 globals。这两个测试失败均未计为通过。

**结果：** 两个 release 构建均成功、0 errors；DI/NRD shaders 在 build report 中，packed HoRTDINRD.dll hash 与源相同。实际 D3D12 RG / compatibility EXE exit=0、JSON pass=true，覆盖单灯能量、baked-only 排除/Mixed 保留、金属 diffuse 与独立镜面、相机外遮挡、Power、RELAX 噪声/能量、关闭回 raw/重建。raw MSE 约 0.050401、filtered 约 0.000066，红/蓝均值约 0.227；D3D11 EXE 确认为 Direct3D11 并保持 DI inactive。最初强制 D3D11 的单 API 构建不能启动，已把 D3D11 加入测试 API 列表后重新验收。Roslyn 编译通过，仅有已有非本步警告；没有 DI/NRD shader 或 command-buffer 错误。

**完成后复查与边界：** 生产改动仅灯烘焙模式的跨 Editor/Player API 修正。测试 probe 明确 Dispose 自己的 DI/capture/target；退出时仍有四条 GraphicsBuffer GC 提示，D3D11（未创建 DI GPU context）也相同，不能据此宣称整个 URP 退出已无泄漏。真实 lilToon、MSAA=1 不依赖 GB generic fallback / MSAA resolve，当前两种 Shader.Find 缺失提示保留为已知输入设施边界。DI 隐藏调试 shader 未进入 build report，需要下一步建立自身资源保留引用，且旧 renderer asset 的自动补引用仍须保存；尚未证明旧资产的构建迁移。只有 Mono/64² 受控场景，没有 IL2CPP、目标性能、TAA、动态几何或完整材质边界结论。[复现说明](../../Tests~/Player/RTDI/README.md)。

## 5. DI 调试资源保留与旧资产保存

**动手前评审：应做，限定 DI 自身依赖。** 上一步 build report 缺少 DI debug shader，且 Create 内自动填 shader 没有标记待保存。Player 主输出通过不能证明调试可用，新的测试 asset 显式 SetDirty 也不能证明旧 renderer 的自然保存流程有效。网格模式还依赖共享标签 shader，应作为 DI 直接依赖一并保留。

**参考核对：** 用户工程 NativeRtxdiFeature.AutoFillShaders 末尾显式 SetDirty；[Unity Shader.Find](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Shader.Find.html) 建议保留 shader 引用，未引用的 shader 可能只在 Editor 可找；[SetDirty](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/EditorUtility.SetDirty.html) / [SaveAssets](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AssetDatabase.SaveAssets.html) 对应资产修改与保存。原生库、RTXDI/NRD 算法不需要变。

**实现：** DI 序列化自身 debug 和 grid-label shader，使用正式引用建材质，不依赖默认 shader-name 查找保留 DI debug。Editor 补齐只填缺失引用；固定 NRD 数组扩容用 Array.Resize 保留已有前缀 override，实际变化才 SetDirty。Create 与自定义 Inspector OnEnable 共用这一补齐函数，Inspector 不重建 runtime context。共享网格 helper 仅增加可选显式 shader 参数，原调用方式保持相同行为。两个 debug shaders 在 release build report 中，未扩张 Always Included 列表或引入全工程构建迁移。

**验证与发现：** 三个独立 Editor 进程分别保存空库/部分库旧 renderer、经实际 Create / Inspector 回调修复并 SaveAssets、重启后先读磁盘 GUID 再加载；完整 15 个引用、debug / grid-label、dirty 标记、重复调用不修改、override 与 initialSamples 保留均通过。release D3D12 RG / compatibility 实际 EXE 的 Raw / Filtered / Receiver / All 网格、原 raw/NRD 输出与生命周期通过；D3D11 inactive 通过。新保留的标签 shader 暴露发布编译的 LabelChar 初始化警告，改为明确初始化的选择后消除，字符映射语义相同。测试旧 YAML 的第一次正则过宽，以及构建脚本 Volume component 没有 AddObjectToAsset 导致部署为空，均修正后重新验证；未把失败运行算作通过。

**完成后复查与下一项：** Shader/采样/NRD 输出含义未变，没有额外主表面 buffer、RT 算法、Feature 或最终合成。Player 两条路径 raw MSE 约 0.050401、filtered 约 0.000066，单灯/遮挡/Power/镜面继续通过。共享网格变化由 DI 与 GI 两条路径回归确认。DI Inspector 仍是两个 HelpBox；全局 UI 预算检查仍因已有 GD/AC 三处超预算失败，未改其他任务。旧资产仍需正常打开/保存，没有给未处理的外部资产作批量迁移保证。下一步转回实际运行预算：先量化目标分辨率与 CPU 分配，再据证据减少 NRD 调度热路径开销；材质边界/动态灯响应、TAA/动态几何仍需分别验证。

## 6. NRD 调度热路径与目标尺寸初测

**动手前评审：应做，但先测量。** Record 每轮创建两份 64 KiB 常量载荷、dispatch 数组和每 pass 资源数组；这些是 CPU 调度临时数据，复用不会改变 NRD 算法，也不需要通用 PT/native 框架。此前 483 MiB/1080p 只是公式，需要实际目标尺寸分配和出光验证；测量范围必须说明。

**参考核对：** 用户 NrdPass 通过 native plugin event 调度，未在 C# 每轮复制整套 64 KiB 临时数组；本步继续自己的 Unity Compute 路线，不导入 NRI/heap hook。固定 NRD.h 明确返回内存会在下一次 GetComputeDispatches 覆盖，[官方集成 README](https://github.com/NVIDIA-RTX/NRD) 相同；[Unity SetBufferData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetBufferData.html) 支持 partial copy。

**测量与实现：** 最初 GC.GetAllocatedBytesForCurrentThread 返回零，已拒绝这个弱证据；随后发现 GC.Alloc 的 Value 不是字节数。改用 [官方 Count 示例](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)，已知分配自检后，预热 16 次、实录 128 次，baseline 每轮 15 个 GC 事件、约 54.5 μs。复制区改为持久 int words、资源二维表与 dispatch 缓存，kernel/identifier 缓存；清有效区 padding，保留 native 当前轮复制、独立 pass 偏移与三帧 CB，仅上传有效 stride 区段。优化后稳定 Record 每轮 0 个 GC 事件、约 11.0 μs；确定移除每轮 128 KiB 常量临时载荷，CPU 缓存代价一次性分配，不改变 GPU pools 或 shader。

**验证：** 实际 GPU 读回两个延后 partial SetBufferData，复用/改写 CPU 数组不会污染前一条上传。DI NRD 两条路径的原始信号、噪声、镜面、相机、域拒绝、释放重建回归通过；Editor 统计与前版相同。两个 Win64 Mono release 构建及实际 RG / compatibility EXE、D3D11 inactive、调试与所有原输出回归通过，无 DI/NRD shader/command-buffer 诊断。Roslyn 编译通过；GI 源和共享桥接未变，无需重复无关 GI 功能测试。

**目标尺寸与完成后复查：** 在原有真实 lilToon fixture 中，720p / 1080p 分配、resize/reset、raw/filtered 出光与 alpha 均通过。实际活动 DI＋NRD 载荷为 214.646 / 483.772 MiB；12 次预热后 8 次完整渲染加最终 GPU 读回，RG 均摊 6.143 / 10.770 ms，compatibility 5.387 / 11.333 ms。场景只有一张 quad / 一盏灯 / 一候选 / 无时空复用；时间含 CPU、输入/raster/DI/NRD 与同步，载荷不含输入/AS/driver/cache，不能代表生产帧率或 NRD 单独 GPU 时间。Record 零分配也不代表整个 Feature 零分配，Prepare 的描述名、外部字典/扫描等仍有空间。这个结果证明基建在目标尺寸能执行，同时确认 NRD 的 GPU 资源成本仍高。下一项优先检查动态灯响应和遮挡边界的实际画质，再根据资源使用关系评审可移除的中间纹理与实际 GPU 分段预算；不因微基准变快就扩大支持域。[详细预算与复现](../Ho-RTDI-NRD.md)。

## 7. 动态灯响应与真实阴影边界

**动手前评审：应做。** 原 NRD 验证证明稳定噪声/能量，不证明改变灯光后的响应或确定阴影是否被滤波带亮。用户对漏光的担忧要求把 RT visibility 与降噪输出分别核对。先使用现有 fixture，检查 raw 数学真值、连续点灯、变色/关灯、隐藏于 camera 的世界遮挡；不把修复转成每次灯变化整体清历史或自写后处理。

**参考：** 用户 NrdSettings.cs / 固定 NRDSettings.h 的原 diffuse prepass=30、主/fast=30/6、antilag reset=0.5；[官方 NRD 说明](https://github.com/NVIDIA-RTX/NRD) 对干净信号 prepass 模糊、概率 lobe split 的重建要求、fast history / antilag 作用。实现继续固定 4.17.3 API，不更换新版 ABI/算法。

**证据与实现：** baseline 阴影内部平均误亮 0.012850、最大 0.090759；raw 在同处严格为零。移动太阳新阴影区首轮 residual=0.111626，关灯第 4 轮=0.031174。单独关 diffuse prepass 能消除大部分静态 halo，但点灯运动平均相对误差从 22.79% 变成 30.44%，因此没有只选这项好看的结果交付。比较 12/2、6/1、3/1 diffuse 历史与 SDK reset=1/acceleration=0.6，最终选 3/1，specular 仍 30/6；两个 prepass 关，其余主要项保持默认。只改 DenseDI 预设，不新增纹理、Feature 或自写降噪。

**验证与收益：** RG / compatibility 的质量门槛通过。阴影内部均值/最大值在 F6 精度下归零，亮侧误差 0.000333；移动太阳首轮残光约 0.001938，第 4 轮归零；关灯第 4 轮约 0.000002。双色 raw MSE 0.050401、filtered 0.000117，镜面 raw/filtered 0.296814/0.281728。旧预设 filtered MSE 0.000049，所以改动改善硬阴影与响应，牺牲部分静态噪声平滑，未宣称全面优于旧预设。实际 Player 变色/关闭及所有已有输出回归通过，D3D11 inactive 通过；编译无本步错误，仅既有其他模块警告。

**完成后复查与下一项：** 连续点灯移动的中心平均相对误差仍 18.71%，末轮 ROI raw 0.813635≈参考 0.809308，而 filtered 0.673002；停止后积累能恢复。这是仍存在的响应限制，不能用门槛通过掩盖。明确 raw 正确、filtered 滞后，未把原始 ReSTIR 方差误判为错误；第一次阴影实验的逐像素 raw 数学比较确实受到 reuse 方差影响，已在确定边界测试中关 reuse，动态灯另保留 reuse。下一步继续评审响应/FPS 契约与必要的信号置信度，再审计 GPU 中间输出复用；完整材质边界、specular 动态响应与 BRDF-aware hit-distance 不能由本步代替。详见 [NRD 质量对照](../Ho-RTDI-NRD.md)。

## 8. NRD 时间契约与相机历史秒数

**动手前评审：应做。** 原固定 diffuse 3 帧在 30 / 120 FPS 意味着约 100 / 25 ms，无法作为稳定响应时间。用户参考 CommonFrameInput 默认 timeDelta=0，让 core 自动跟踪；但固定 NRDSettings.h 与 [官方说明](https://github.com/NVIDIA-RTX/NRD) 推荐把积累秒数换算成实际（非 frame-generated）FPS。Unity Editor 实测 unscaled time 会随 frame 推进，render-loop 同帧内保持时间戳一致，具备合适的源契约。

**实现：** 本相机 pass Snapshot 捕获 unscaled sample time，执行时更新相机计时。实际 delta 以 ms 输入 native common；0.1 秒低通的 delta 用于 SDK round(period × FPS)。周期 diffuse 50/16.67 ms，specular 500/100 ms，history fix 50 ms；保留 255 / 3 上限与 fast < main。重复时间戳保留预算；回退/无效/>1 秒间隔清 NRD 历史。整数预算变化才更新 native settings，没有新 GPU 资源、Feature、Shader 算法或最终合成；Editor-only 内部时间注入用于可重复的物理时长测试，不暴露为产品开关。

**验证：** 同样 2.4 units/sec、0.4 秒点灯移动、真实 GPU 下，30/60/120 Hz 对应 diffuse 2/3/6、specular 15/30/60 帧，ROI 平均误差 15.44/17.17/14.22%；两条路径一致。重复时间戳不改 FPS，clock rewind/invalid/gap 状态验证；两秒间隔后关灯首轮没有旧残光。原质量门槛和 NRD 两条路径回归、Win64 release 两个构建与实际 EXE、D3D11 inactive、Roslyn 编译通过。Record 仍测为 0 个 GC 事件。没有更改 GI 或共享桥接。

**完成后复查与下一项：** 本步解决不同采样频率的时间含义，没有解决连续运动本身的滞后，也不是实际 wall-clock Player FPS benchmark。低 FPS 的整数化、高 FPS 的帧数上限与 round 边界仍可能影响响应；1 秒 cut 是项目策略，应在更多相机暂停/恢复场景复查。下一步核对每 lobe 距离与 history confidence 的参考实现，先证明对现有静态世界动态灯有效，再考虑增加输入；GPU 输出复用仍待评审，不扩展通用 PT 框架。详见 [时间与预算](../Ho-RTDI-NRD.md)。

## 9. 参考梯度与 NRD history confidence

**动手前评审：应做，但必须配对样本。** 原来只靠 NRD antilag，持续点灯运动仍偏暗。参考工程与 [NRD 官方 confidence 契约](https://github.com/NVIDIA-RTX/NRD) 使用同一旧样本重算，隔离 lighting delta 与 Monte Carlo 噪声；简单 current raw - previous raw 或按灯位置变化随意清历史都不能作为替代。

**参考与实现：** 实读用户 UnityPathTracing/RenderingPlugin/External/RTXDI/FullSample 的 ComputeGradients、FilterGradientsPass、ConfidencePass，并记录三个原 SHA。复用现有 light identity maps、历史 reservoir 和 16 B guides；每 3×3 选最亮 current/previous lobe，用另一期灯快照重新算同一 light/UV/invPdf，四轮参考 triangular A-trous 后生成低分辨率 diffuse/specular confidence，交给固定 NRD 4.17.3。旧 history 必须保留到 gradient 完成，所以 NRD Shade 先保存旧 luminance，最后 SaveHistory 写回；raw-only 流程不变。静态 revision 下相同解析灯参数可精确跳过重算，避免冗余射线。没有增加 renderer Feature、通用 PT/native framework 或终端合成。

**验证与收益：** 冻结随机双色照明时 confidence 全图 min/mean=1；不会把 RNG 噪声报告为 lighting delta。相同速度 30/60/120 Hz 的 ROI 平均误差从 15.44/17.17/14.22% 降到 9.25/14.45/12.76%。两条路径的原 NRD/质量/时间门槛、raw DI 回归、GI 回归、发布 EXE 及 D3D11 inactive 均通过；无本步 Shader 诊断，Roslyn 编译通过。噪声 MSE 本轮 Editor filtered=0.000257（raw=0.050401），Player≈0.000101，镜面 raw/filtered≈0.296814/0.292526；不把这些不同调度节奏的单次数据当成完整消融证明。

**成本与完成后复查：** 新增 RG16F previous luminance 全尺寸，以及 1/3 尺寸 gradient pair / R8 confidence pair，1080p +11.865 MiB，活动 DI＋NRD 总计 495.637 MiB。增加 gradient、4 filter、confidence、history store 调度；RG 简单同步 batch 为 6.863/13.755 ms（720p/1080p），有明确成本。第一次版重复 visibility ray 的 1080p batch 测约 16.4 ms，静态等参数精确分支降到本轮约 13.8 ms；这不是生产 GPU benchmark。仍限静态 opaque delta 灯域；exact reprojection 和 8-bit history guides 替代参考 temporal sample positions，对薄物体/复杂材质未证明，gradient 跨几何扩散与 BRDF-aware 每 lobe 距离仍需验证。下一步优先减少可证明冗余的 GPU 中间输出，再继续检查这些输入质量，不能把原始算法/复杂域改成易过测试的假信号。详见 [confidence 接入与限制](../Ho-RTDI-NRD.md)。

## 10. NRD external output 原地恢复

**动手前评审：应做，先核对生命周期。** 两张独立 filtered RGBA16F 只接收 Resolve 恢复后的结果，1080p 占 31.641 MiB；若 NRD 下轮需要其旧解调结果就不能复用。对照用户工程固定 RELAX shader、RTXPT 后端材质恢复与实际 core dispatch，不引入新的 Feature、主表面副本或 PT 框架。

**发现与实现：** 最初禁止 external output 的所有 SRV 读取，被实际 Temporal pipeline 4 的读取否定；该读取来自同轮 PrePass，跨帧 history 在 permanent pool。改为逐通道检查本轮先写后读，写入在 dispatch 后才标记。Resolve 用同一 UAV 逐像素读写恢复 RGB/receiver alpha，filtered 发布引用 owned external output；移除两张纹理的分配、重复 RG 依赖和释放，raw 独立。描述检查仅补充固定源码审计，不能证明任意将来 shader 的完整覆盖。

**验证与收益：** Editor NRD 两条路径、动态灯/阴影质量、compatibility 时间线、目标尺寸 resize/reset 通过；兼容路径的 raw/filtered MSE 与镜面值和前版相同。两个 Win64 Mono release 构建及真实 D3D12 两条 EXE、D3D11 inactive 均通过，packed DLL hash 一致。raw/filtered/debug、相机与关闭重建继续有效。Roslyn 无错误，无新增 DI/NRD Shader/command-buffer 诊断；已有其他模块 C# 与 lilToon duplicate-keyword 构建警告保留。实际活动载荷 720p/1080p 从 219.921/495.637 降到 205.858/463.996 MiB，减少 14.063/31.641 MiB。简单同步 batch 为 6.669/14.183 ms，没有证明速度提升。

**完成后复查与下一项：** NRD 算法/预设/输出含义不变，只减少确实冗余的输出存储。固定版本升级仍需重新核对 lifecycle，不能将本结论扩展到其他 denoiser。下一项优先构造薄几何与不同材质交界的真实 raw/filtered 对照，分别检查表面判别、confidence 扩散和 NRD 本身的滤波边界，再决定是否需要输入或权重修正；每 lobe hit-distance 也必须以参考和证据评审。详见 [当前预算与复用约束](../Ho-RTDI-NRD.md)。

## 11. Diffuse 零通道与零 diffuse 材质边界

**动手前评审：应做，先分离 raw 与滤波。** 上一步没有证明薄几何/异材质边界。新增真实 lilToon 的 2px 红蓝纹理、近共面 2/4/8px 全金属条、有深度差 4px 蓝色前景；单 directional 光、关闭 reuse 的确定性 raw 作为 NRD 对照，深度案例另启用 reuse。用户 UnityPathTracing-6000.3.15 的 ShadingHelpers / DI/ShadeSamples 明确保留无 albedo 的 diffuse，再乘材质；固定 NRD 编码与 minMaterial 比较也是已有接口。

**发现、消融与实现：** 旧红/蓝 filtered 从 raw≈0.159 降到≈0.079，还混入另一颜色；金属 diffuse 出现假光。带下限的反除不能恢复已丢失的零通道。按参考的 Lambert 分解直接产生无 albedo 的 NRD signal、最后乘 albedo，单独验证即可修复颜色/金属 diffuse，但 2px 金属镜面仍损失 73.43%。随后使用原 SDK 的 active-diffuse=0 / zero-diffuse=3 分类，minDiffuse / minSpecular=0 开启比较；0/3 在 FP16 normal transport 中精确保存为 alpha 0/1。没有增加纹理、Feature、对象 ID 路由、最终合成或修改原始 NRD 正文；raw 着色表达式与 confidence 配对算法不变。

**验证与结果：** 最终两条 Editor 路径颜色/深度前景最大误差 0.000122（旧为 0.080811/0.018433），2/4/8px 金属镜面相对误差约 0.0071/0.0407/0.0524%，电介质假镜面与金属 diffuse 在 F6 下为零。原 NRD 两条路径、动态灯/阴影、compatibility 时间线通过。两个 Win64 Mono release 构建与真实 EXE、D3D11 inactive 通过；发布新增颜色/金属门槛最大颜色误差 0.000122、金属镜面约 0.227%、假镜面为零，原 filtered MSE≈0.000101。无本步 Shader/command-buffer 错误，Roslyn 无错误，已有其他模块和 lilToon duplicate-keyword 警告保留。

**复查与下一项：** 混合材质 reuse 的首次逐像素解析断言失败（raw≈0.15393 vs 0.15915）；不能将随机归一化信号强制当确定值。已补独立 64 帧统计：diffuse raw/filtered≈0.158370/0.158098，对照 0.159155；specular≈0.640791/0.636575，对照确定性 raw 0.646484。有限相关观察不证明无偏，后续仍需审计归一化/guide 精度。深度前景的相同 directional 入射仅证明颜色恢复与投影，点灯/异法线还需单独验证隔离。本步只隔离 active/zero diffuse，两类别不代表所有材质；彩色/低 F0 镜面的 helper 下限、梯度跨几何扩散、每 lobe 距离仍未解决。下一项检查不同入射的几何边界与镜面颜色/低 F0 的实际 raw/filtered，再据原参考评审信号。详见 [边界对照与复现](../Ho-RTDI-NRD.md)。

## 12. DenseDI 方差与 disabled-HistoryFix 二阶矩

**动手前评审：应做。** 上一步同向 directional 的深度案例不能证明入射不同的隔离。新增点灯薄前景及其解析投影阴影，并核对同类红/蓝金属。旧 raw 正确，filtered 阴影最高 0.153076、彩色镜面约 50.2% 偏暗。逐 pass 读回定位 A-trous 的空间统计；关闭 acceleration 无效，已撤回。

**参考、实现与复查：** 固定 RELAX 短历史分支平均颜色且不计 depth；热分支先混合 RGB/二阶矩再相减，包含邻域固定光照差异。当前 DI 关闭 spatial variance color bootstrap 与稀疏 HistoryFix，diffuse 主预算至少 1，改为平均每像素时间方差。原 vendor 文件和 CPU DLL 不改，单独派生 A-trous helper。强制正常 60 Hz 又揭露 fast specular alpha=hit distance 被 disabled-HistoryFix 的 HistoryClamping 复制成 normal 二阶矩，Temporal alpha≈2230，正确应≈0.077。第二份派生只修该分支的 RGB/alpha 复制。矩阵与 Constant-buffer 双上传 GPU 对照正确，没有做猜测式底层修改。NRD confidence 的 previous guides 也独立于 RTXDI temporal 开关，关闭 reservoir reuse 的改色回归通过。临时 capture hook 已从生产源码清除。

**结果与代价：** 两条 Editor 的明确 60 Hz 边界回归通过：点灯 ROI 最大误差 0.006348、阴影 0.000010；单灯彩色镜面约 0.1465% 误差。两个 release 构建及实际 D3D12 EXE / D3D11 inactive 通过；Player 点灯误差 0.000488、阴影/假镜面为零，彩色镜面约 0.1511%。原 NRD、动态灯/阴影、30/60/120 Hz 时间线、目标尺寸/释放重建通过，无新增 DI/NRD shader/command-buffer 错误，Roslyn 无错误。Editor filtered MSE≈0.000179、Player≈0.000086，raw≈0.050401。物理时间线约 9.337/14.474/12.764%，接近前轮；另一个 bulk render-request 运动 fixture 中心误差约 10.89%（前轮≈6.55%），记录为代价，不混淆执行节奏。没有新增 GPU 资源或 Feature；1080p 活动载荷仍 463.996 MiB，简单同步 RG batch≈10.606 ms，不当成孤立 GPU 性能证明。

**完成后复查与下一项：** 两份派生源码明确保留版权/许可，用 Tests~/RTDI/generate_nrd_variance_adapter.py --check 对原 SHA 和窄变更逐字重建；绑定 identifier 不证明原始算法未改。单灯确定性改善不能代替噪声输入：两方向白灯的彩色金属 raw mean=0.970442≈参考0.969727，filtered=0.816392、假通道平均0.005367，仍约 15.8% 偏暗。下一项优先核对这个实际噪声下的 F0 解调/滤波边界，并检查低 F0 / 不同角度；gradient、每 lobe 距离与动态域仍未证明。详见 [方差与二阶矩证据](../Ho-RTDI-NRD.md)。

## 13. F0 空间条件与历史输运

**动手前评审：应做。** 原两灯彩色金属约 15.8% 偏暗，宽度对照将偏差定位到材质边界：2/8/64px / 整片红色分别约 0.816/0.935/0.958/0.960，raw 正确。核对官方 NRD 材质解调/分类与用户 FullSample 的 floor；没有整体放大输出或套用不可分离的 Lambert 逻辑。

**实现与复查：** 镜面 A-trous 直接读取现有 SB 量化 F0，用 regularized 分量 overlap 条件约束相混，零通道集合不同则拒绝。小步 shared cache / 大步中心 cache 减少重复读取。相机移动又揭示历史混合，借用已有 16 B DI history 的 F0 对有效 bilinear footprint 降低 specular 主/fast 预算；不新建 previous-material texture。diffuse、raw BRDF、Native CPU/library/layout 不变，Temporal/A-trous 是明确派生 body，生成器 `--check` 校验原 SHA 和窄改动。没有新 Feature、全屏 buffer 或 AC 路由。

**结果：** 两条 Editor 的 2px filtered=0.942949 / 参考0.969727，偏暗约 2.76%；低 F0 0.04/0.2、连续渐变与慢相机移动通过，移动总和≈0.971167、颜色比误差≈0.000046。校正了旧移动 metric：max(R,B) 会因 fractional 材质采样下降，绝对色差又混入 MC 亮度，应比较 RGB 总和和归一化 tint。两个 release 构建和实际两条 D3D12 EXE / D3D11 inactive 通过；Player 静态偏暗约1.67%、移动约0.2%、tint约0.005%。测试使用独立 raw-spec capture，不切换 NRD 破坏历史。原噪声/生命周期、薄阴影、动态灯与时间线通过，无新增 DI/NRD shader/command-buffer 错误，Roslyn 无错误，既有其他警告保留。

**代价与下一项：** 无新增生产 GPU 资源，1080p仍463.996 MiB；多了 guide 读取和历史条件，同步 RG batch≈11.615 ms（前轮10.606），不是孤立 GPU 消融。短历史可能增加材质边界噪声，剩余1–3%偏差仍在。下一项检查不同角度/粗糙度/彩色入射光与真实命中距离，不用本轮规则白灯平面代表复杂 GGX 场景或动画/TAA。详见 [条件与完整对照](../Ho-RTDI-NRD.md)。

## 14. 独立 GGX 角度与 HDR 二阶矩范围审计

**动手前评审：应做。** 上一步正面同向白灯不足以证明真实 GGX。新增独立 CPU double D/G/Schlick 参考，两个不同 RGB / 不同角度的方向灯，实际 roughness约0.098/0.298/0.698 × θ0/20/45 的九组合；96次预热/96次观察、固定60Hz。按实际 guide 量化值核对，名义roughness断言失败后仅校正参考输入，不改着色。

**结果与复查：** 两条 Editor 的 raw RGB 和 filtered5%能量门槛通过，filtered误差约0.93–2.20%。进一步提高强度到白灯0.5/0.25，输出均值仍接近参考646，但实际 `gHistory_Spec` mean≈3330、m2=65504、variance≈-11023400，统计已饱和。finite检查无法检出这一失效。官方NRD要求二阶矩平方适配FP16；用户固定核心pool含八张RGBA16中间/历史纹理。`RunSpecularHDRCompatibility`新增明确m2门槛，实测exit1；保留失败，未将它算为通过。

**完成后判断与下一项：** 本步只交付独立验证和支持域边界，无生产代码/资源改变。低强度九组不代表Player角度域或任意HDR。下一项优先做NRD信号归一化及历史尺度契约，保留raw/最终DI单位，验证灯光变化、高峰值、亮暗并存与m2正确性；不能静默饱和或删除HDR案例求过关。详见 [范围缺口和失败复现](../Ho-RTDI-NRD.md)。

## 15. HDR 信号归一化与历史尺度

**动手前评审：应做。** 步骤14已重现有限但错误的二阶矩；仅升级某张 history、取消 gate 或整体提亮都不能解决中间FP16写入。对照用户固定RELAX核心及官方线性缩放建议，范围只涉及DI的NRD输入/历史，不改native框架或最终材质合成。

**实现与中途失败：** FP32暂存解调输入，GPU按组归约当前峰值，RGB目标上限128；raw/输出继续是物理RGBA16F。最初只降尺度导致暗场m2显著偏低，扩展测试明确失败。最终把实际permanent slow/fast RGB、sqrt(slow m2)也纳入范围，安全恢复尺度；派生Temporal把SMB/VMB旧RGB与m2分别乘比例与比例平方，hitT保持原值。借用实际dispatch manifest检查完整性，无硬编码pool索引/新增历史副本。Normalize在缩放前恢复raw并保存旧luminance，避免新增UAV警告及重复光线。

**验证与完成后复查：** 原HDR history mean/m2从约3330/65504变为52.031/2984，variance为正。解调超过FP16的16/8灯、极暗切换、再恢复、同屏高低F0、资源重建都通过两条Editor gate；暗场尺度恢复至1且m2正常。九组独立GGX、原NRD回归、目标分辨率、两个release构建及实际D3D12 Player/D3D11 inactive通过，Player HDR最差filtered能量误差约1.071%。最终无新增DI/NRD shader/UAV诊断；Roslyn/派生重建通过。

**成本和下一项：** 新增16B/px＋16B状态、四次dispatch（两次单线程）；720p/1080p载荷219.921/495.637MiB，简单RG同步batch6.570/12.172ms，不冒充单项GPU时间。全屏尺度仍受亮峰值/保留历史影响，极端动态范围及FP16终端65504上限仍存在。下一项优先验证局部Point/Spot、变化视角/曲面上的hitT/遮挡边缘，按证据决定每lobe距离与采样质量改进；保持最终PBR/NPR合成边界。详见 [HDR域、测量与限制](../Ho-RTDI-NRD.md)。

## 16. 局部灯、斜视角与随机高光审计

**动手前评审：应做验证，不先增加射线。** 单正面方向灯和 diffuse 点灯不能证明局部 specular / 斜视角。对照用户 ShadingHelpers 的 lightDistance 与固定 donor BRDF，并核对 NRD 的 in-lobe / ReSTIR 距离限制。CPU GGX 扩展到任意 V，使用参考实际的 height-correlated Smith G/NdotV；原 NdotV=1 公式只是特例。仅修改 Editor 验证，无生产 Feature、采样、材质或资源扩展。

**通过的域：** 128²透视平面，点/聚光灯 ×roughness约0.298/0.698×视角0/30/60°，逐像素核对平方距离、range和inner/outer锥衰减、GGX及选中灯距离。两条Editor路径通过；raw归一化L1约0.03–0.04%，filtered约0.08–0.6%，距离最大误差约1.3e-5。锥内/过渡/外部样本计数882/4502/760；最初cone未覆盖外部的fixture失败后收窄实际锥角，不取消覆盖条件。独立CPU segment/AABB检查薄遮挡，暗区最大约3.9e-5 diffuse峰值、1e-6 specular峰值；单灯15°/秒绕视镜面L1约1.77%。这些只证明受控平面，不代表曲面或in-lobe契约已完成。

**发现与定位：** 近灯供高光、远灯主导diffuse（约1.6/58距离），真实初始RIS=8＋时空复用时，512轮中心ROI raw镜面比例≈0.973、filtered≈0.814；移动时filtered≈0.845。`RunLocalPairQualityCompatibility`明确以5%静态能量门槛失败。128轮raw偏差4.8%不能直接定性为采样bug；扩大观察并做无/T/S/TS复用消融，raw接近参考但filtered仍低约14–15%。读取真实permanent history并恢复材质/尺度后，history约0.99、final约0.856，主要损失出现在A-trous阶段。它是ROI形状/能量缺口，未测整画面能量守恒。

**不采用的试验：** SDK支持范围内sigma3、更长fast15、关闭antilag均未消除；specular prepass5反而降至约0.694。按fast history计数缩放空间方差的生产原型也未改善（TS filtered≈0.812），已完整撤回，原派生重建再次通过。`RunLocalFilterAudit`的native参数覆写只存在于隔离Editor诊断，不是生产配置或质量通过。日志的0退出只代表观察成功，不能掩盖独立quality gate失败。

**完成后复查与下一项：** 保留所有失败门槛，提交解析验证、stage读回和取舍证据；不把复杂域改成简单域求通过。下一项优先查A-trous置信度、空间权重及局部高光扩散范围，再按证据决定滤波/采样/距离调整。需补曲面、宽域/点列Profile与最终目标场景；没有重新做Player/预算，因为本步生产代码最终未改。详见 [局部灯审计与复现](../Ho-RTDI-NRD.md)。

## 17. Specular 空间权重与方差传播

**动手前评审：应查空间阶段，不增加射线。** 新诊断读取真实reprojection confidence和不同宽度ROI：置信度全1，但final/参考从32²的0.856到全部可见平面的0.913，不能仅解释为光斑扩散到ROI之外。对照用户固定NRD源码及官方A-trous实现，首层又将真实空间亮度差混入输出variance，后续层已用平方权重传播variance，当前DenseDI定义需要一致。

**实现与试验取舍：** 只改DI的派生specular滤波：亮暗端用较小的方差共同决定luminance bandwidth；首层用Σ(w²×temporalVariance)/Σ(w)²传播，避免空间轮廓再次变成noise；以fast预算和已存在history length估计累积颜色方差，相关ReSTIR下仍是近似。原型曾同时改diffuse传播，使diffuse MSE由约0.000179升至0.000543，已撤回该部分。无新RT纹理/射线/Feature/ABI，首层新增576B groupshared history cache与已有R8历史读取。

**收益和未完成项：** 相同随机序列/512轮对照，无复用及T+S的specular归一化MSE从0.037400/0.050194降为0.007037/0.010763；无复用中心/整平面比例从0.856/0.913变为0.984/0.987。T+S另一条固定序列整体ratio约0.963，但原quality序列仍为0.947065（5.293%偏差），原5% gate保持失败；不通过改门槛消除有限样本/历史剩余误差。运动L1从约15.6%降到8.0%，旧HDR/材质边界/时间/噪声回归继续检查。没有宣称整个DI目标已完成。

**新增失败域：** 双灯薄遮挡CPU segment/AABB检查发现raw shadow也不足：off/off为0，T/S/TS约0.830/0.513/0.866；diffuse raw整体约0.797–0.910。切回旧生产shader重复，raw完全一致，因此属于新暴露的采样/visibility输入问题，不能借本步修饰。滤波对受光高光与暗区的改进已单独测量，shadow gate保留失败。下一项优先单Far lamp与actual ray/reservoir读回定位该缺口，再解决固定quality序列剩余偏差和曲面/目标场景支持。详见 [空间修复与限制](../Ho-RTDI-NRD.md)。

**最终验证与成本：** 两条Editor的NRD/边界/九组GGX/HDR/生命周期通过，RG quality/time/尺寸通过，两个release构建和实际D3D12 Player/D3D11 inactive通过。Player部署回归不是新128²场景的支持域证明。Roslyn/派生重建通过，最终无新增DI/NRD诊断。RT载荷仍219.921/495.637MiB，简单RG同步batch6.750/12.799ms；没有新增纹理/pass/射线，但组内cache/读取和方差运算有成本。保留5.293%固定quality与双灯shadow两个失败，不以新观察通过重新定义完成。

## 18. 初始 visibility 使用真实选中样本

**动手前评审：应修raw，不再调滤波。** 对照用户固定InitialSampling/VisibilityTest及官方Bridge的保守visibility契约，先做单Far、单Near、混合1/8候选隔离。单灯和混合1正常，8候选阴影区所有reservoir被清空。独立GPU API探针不含场景ray：1候选SDK返回位置与reservoir位置全匹配，8候选988/4096不匹配。保留复现模板，不能未经证明把它叫作通用DXC/SDK bug。

**实现：** DI关闭SDK内部initial visibility，在返回后由reservoir实际light index/UV重建sample，执行相同的RAB_GetConservativeVisibility与M-preserving discard。原SDK正文、MIS/选择算法和ray策略不变，不增加一条visibility射线，只新增canonical light读取/重建。GI未改。多候选返回sample不再用于丢弃实际可见的另一盏灯。

**结果与复查：** 8候选阴影raw从0恢复至约1.004；完整512轮无/T/S/TS的shadow raw约1.002/1.001/0.998/1.004，diffuse整体约1.000/1.002/0.999/1.005。raw门槛独立通过；temporal-only filtered lit约0.928，原5%门槛仍失败，提示已与raw失败分开。旧固定quality序列5.293%偏差未变化，仍不算完成。

**验证与下一项：** 两条raw动态灯/遮挡回归、Power compatibility、NRD compatibility通过；真实Win64 release两条D3D12 EXE新增8候选遮挡gate通过，比例约0.996/1.002，旧HDR/噪声/F0边界通过，D3D11 inactive通过。Roslyn/派生重建通过，无新增本步Shader/command-buffer诊断。后续先检查temporal-only filtered的受光高光和固定quality剩余误差；再扩大curved/目标场景。未扩展最终PBR/NPR接收、通用PT或ReGIR。详见 [初始visibility修复](../Ho-RTDI-NRD.md)。

## 19. 混合 diffuse/specular 的 DI 候选权重

**动手前评审：先量化，不用亮度补偿。** 用户参考工程的 RAB_LightSampling 和 [官方 Bridge 契约](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md)允许近似 target，最终照明由 reservoir 权重和真实 BRDF 决定。双灯场景中的窄镜面高光与强 diffuse 竞争同一 reservoir，先检查输入方差和 NRD 各阶段，避免把能量变亮本身视为改善。

**实验取舍：** 新增仅 Editor 的四阶段即时 GPU capture。相同 192+128 预热、512 次观察下，基线 temporal-only 的受光区在 Temporal/HistoryClamping/首末 A-trous 比例约 0.937/0.936/0.924/0.910，损失并非主要来自 clamping。关闭 virtual-motion 实验更差；延长 fast history 或关闭 clamp 能抬高均值，但归一化 filtered 镜面 MSE 从 0.118676 增至 0.167437/0.218093，均未采用。原生产 NRD 配置与派生正文保持不变。

**实现与结果：** 仅 HoRTDI.compute 定义镜面 target 权重 4，initial/temporal/spatial/bias correction 共用同一函数；最终 BRDF、RGB、ray 次数不变，GI 未定义该宏。匹配序列中 raw/filtered 镜面归一化 MSE 从 20.399940/0.118676 降至 10.094452/0.052974；diffuse 从 1.168848/0.011119 降至 0.944692/0.010298。原无遮挡 quality 的静态 filtered/reference 从 0.947065 升至 0.981143，运动 0.987345，原 5% 门槛通过；原四模式遮挡检查也通过。权重 4 是本项目质量策略，未证明任意场景最优。

**保留的失败与验证：** 更长预热的匹配 temporal-only audit 受光区仍为 0.935517，约 6.45% 偏暗，5% gate 保持失败；不能用原序列通过替代它。Win64 Mono 两条实际 D3D12 EXE 新混合材质 gate 的镜面/diffuse 比例约 0.980891/0.969552，旧 HDR/边界/初始 visibility/noise 通过，D3D11 inactive 通过。Editor 两条核心/NRD、Power、动态灯、边界、HDR、时间/预算与 GI compatibility 回归通过；Roslyn 和派生重建通过，无新增本步 shader/command-buffer 诊断。自有 720p/1080p 载荷仍约 219.921/495.637 MiB，简单 RG 同步 batch 约 6.809/13.170 ms，含 CPU/完整渲染/读回，不能当作孤立 GPU 性能。

**完成后复查：** 无新生产纹理/pass/Feature/native DLL。阶段 capture 仅诊断入口分配测试纹理，回调在 Player 中被编译移除。下一项继续定位 temporal-only 的相关噪声、history 输运与空间滤波剩余误差；再进入曲面/目标场景，不提前扩张最终材质合成。详见 [混合候选权重](../Ho-RTDI-NRD.md)。

## 20. ReSTIR permutation 与较短默认历史

**动手前评审：检查 sampling 输入，不再放松 NRD。** 同序列比较 ReSTIR maxHistory=1/4/8/20，保持前步 target、NRD、192+128 预热及 512 观察不变。20/8/4/1 的 raw 镜面归一化 MSE 约 10.094/4.740/2.477/1.240，filtered 约 0.052974/0.025909/0.015112/0.008352。逐像素去均值的 lag-one correlation 接近零，不能把改善归因于逐像素相邻帧相关性；更长的空间 ancestry 与历史权重影响仍需另查。

**参考与实现：** 用户 fork 的 ReSTIRDIContext.SetFrameIndex/SetTemporalResamplingParameters 使用 JenkinsHash(frameIndex)；固定 SDK temporal 参数要求 frame-uniform random，permutation 只取低四位。DI 原来直接使用 frame counter，现改为 SDK JenkinsHash。单独 hash、历史20虽然通过长预热亮度 gate，却把该 fixture 镜面 MSE 提高至 0.072785，并使原短运动 raw gate 失败，未把它称为独立质量改善。新建 DI 的默认 maxHistory 从20降为4，两项组合采用；Inspector 改称「历史样本预算」并说明含义。旧 renderer 序列化值继续有效，没有暗中 clamp 到4，范围仍1..32，NRD帧预算不变。

**结果与保留的失败：** 组合后的匹配 temporal-only lit/reference=0.976478，原5% gate通过；raw/filtered镜面 MSE=2.633737/0.016470，diffuse=0.167633/0.005551，均低于步骤19。原四模式shadow gate通过，temporal+spatial镜面filtered MSE=0.013794、lit=0.976302。原固定quality静态filtered=0.971634；64帧运动raw=0.964139，3% gate仍exit1，filtered=0.951544、L1=0.072589。保留原检查，另加 `RunLocalPairMotionConvergenceAudit`：同15deg/s、同15..31度域来回观察512帧，raw/filtered=1.015134/0.991004、L1=0.065639，通过原能量/L1门槛。延长观察不是短序列通过的证据，也没有证明任意场景无偏或默认4最优。

**验证：** 两个Win64 Mono release构建与实际D3D12 EXE通过新增temporal-only混合gate，序列化maxHistory=4，RG镜面/diffuse=0.971927/0.970563，compatibility=0.985094/0.967562，D3D11 inactive通过；旧HDR/noise/边界与初始visibility通过，packed DLL SHA一致。Editor两条核心/NRD、Power、动态灯、边界、HDR、时间/预算回归、Roslyn和派生重建通过，无新增本步GPU诊断。自有载荷仍219.921/495.637 MiB，RG同步batch约6.952/15.388 ms；该fixture关闭temporal且包含CPU/完整渲染/读回，未证明hash的孤立性能。

**完成后取舍：** 只调整 DI frame permutation 和可配置默认值，不修改 SDK 正文、物理照明、NRD配置、GI 或最终材质合成，不新增生产GPU资源/pass/ray。下一项先扩大独立序列与曲面/目标场景，检查短序列波动和长历史的实际收益，再按证据推进环境/更多灯域；不继续靠均值提亮或降低门槛消除失败。详见 [ReSTIR历史审计](../Ho-RTDI-NRD.md)。

## 21. 曲面接收体与 SB 法线精度

**动手前评审：扩大真实几何支持域，应做。** 原验证主要是平面，不能证明曲率跟踪和局部高光。用户参考 RAB_Surface 区分 shading/mesh normal；[NRD 官方输入说明](https://github.com/NVIDIA-RTX/NRD#inputs)指出法线精度影响曲率与镜面跟踪。本步不搬入 PT 框架，使用真实 lilToon 球形网格、现有 GB/SB 和独立 CPU ray/triangle + 插值顶点法线 + 距离衰减/Smith GGX 参考。

**发现与实现：** 128²、两种 roughness、单 Near 与 Near/Far 四种复用状态，共10组。RGBA8 SB 法线导致最大 normal guide 误差约0.571°，单灯低roughness raw镜面L1≈4.300%，viewZ最大误差仅约0.000009；filtered约4.300%，说明不是NRD新增的形状误差。沿用已有camera需求收集，仅实际入队的RTDI/RTGI请求SB原Normal附件提高精度：RGBA16 UNorm优先、RGBA16F次选，不支持时保留紧凑回退。请求不包含AC normal-copy、identity或semantic flags。无新GPU纹理/pass/MRT或Feature，无最终合成改动。

**结果与代价：** 两条路径10组原gate全部通过：法线最大误差约0.0816°；低roughness单灯raw/filtered镜面L1从0.042997/0.042996降至0.000528/0.000946，能量比例0.999493/0.999062。低roughness双灯无/T/S/TS filtered比例约0.9852/0.9953/0.9899/0.9762；高roughness双灯约0.9871..0.9879。旧2%单灯形状、3%raw能量、5%filtered能量与15%filtered L1门槛没有放松。SB原Normal从4到8 B/像素，720p/1080p新增约3.52/7.91 MiB；不是DI内部载荷的节省或免费改进。

**验证：** 两个Win64 Mono release构建及实际D3D12 EXE通过新增64²正交曲面gate，raw/filtered镜面L1=0.000588/0.000814，真实精度请求存在；旧HDR/noise/初始visibility/材质边界与混合/仅时域gate通过，D3D11 inactive通过，packed DLL SHA一致。Editor两条核心/NRD、边界、HDR、动态灯、预算与GI两条必要回归通过，Roslyn/派生重建通过，无新增本步GPU诊断。DI内部载荷仍219.921/495.637 MiB，SB输入增量另计；RG同步batch约6.896/15.109 ms，含CPU/完整渲染/读回，未证明孤立性能。

**完成后复查：** 测试独立放在Editor partial中，不添加Renderer Feature。NRD真实输入guide与CPU网格比较，不以post-frame shader global冒充RG纹理；camera demand读取实际完成的LastSnapshot。兼容路径已检查启停DI时Normal实际格式升降，RG已检查请求清除/恢复，未触发语义资源。当前只覆盖静态、无normal-map、均匀缩放球体、中央32²ROI与同色灯，尚未证明完整曲面运动、轮廓边缘、非均匀缩放、normal-map或任意网格。原短运动失败仍保留；下一步扩大运动/独立序列，并据实际支持缺口推进灯域。

## 22. 下一步取舍

| 项目 | 当前判断与推进条件 |
| --- | --- |
| 局部灯功率采样 | 可选路径与可重复基准已交付；后续按目标场景 GPU budget 决定缓存和 tile 规模，保留 Uniform 对照。不能用此结果代表任意灯光空间分布。 |
| Player 资源完整性 | DI/NRD/debug 的 Win64 Mono release 部署及旧 renderer 正常补引用/保存流程已交付。IL2CPP、未处理外部资产的构建迁移仍未证明；GB 泛用 fallback / MSAA 属于输入设施的另一个支持域，不借本步扩张。 |
| NRD 质量与预算 | 默认短历史、匹配temporal-only与静态曲面10组通过；RT请求SB原法线附件提高精度，1080p额外约7.91 MiB。原64帧运动raw统计仍超过3%，512帧检查通过，两项分别保留；下一项曲面运动、独立序列与目标场景，不能把中央ROI通过视为全域验收。旧资产历史设置不会自动迁移。 |
| ReGIR | 需要世界网格与更多采样资源；先证明较小的局部提案改进仍不足，再评估引入收益。 |
| 最终材质合成 / PT 扩展 | 维持现有范围边界。 |

NRD 参考：[官方集成 README](https://github.com/NVIDIA-RTX/NRD)，以及用户参考工程 Assets/Scripts/NativePlugin/NRD 的 NrdDenoiserHelper / NrdFrameData。官方资料要求对应的辐射、命中距离与表面/运动 guides；本地参考还涉及 NRI/native ABI。后续应以可验证的输入契约和实际接入为单位推进，不能通过搬入整套原生框架或对 raw 输出简单模糊就宣称 NRD 已接好。

本记录随每步评审更新。后续步骤若改变以上优先级，应记录发现、依据和替代选择。
