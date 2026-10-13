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

## 22. 曲面运动与历史表示审计

**动手前评审：扩大验证，不盲目改滤波。** 前步静态球体中央ROI不足以证明曲率跟踪。按NRD官方world-space MV契约及用户current/previous RAB_Surface，保持静态对象MV=0、使用实际相机矩阵，在同一球体上做0..16度、15deg/s、60Hz、64帧观察；每姿态用独立CPU ray/triangle参考。两种roughness×单Near/双灯无/T/S/TS×frame起点0/1024/4096，共30组/路径，各组重启history并预热192帧。

**结果：** RG/compatibility均通过原3%raw能量、2%单灯raw形状、5%filtered能量与15%filtered L1门槛。raw镜面比例0.986156..1.016857，filtered镜面0.989396..1.011930，filtered diffuse0.979710..0.994401；镜面filtered L1=0.024551..0.129224、归一化MSE=0.000847..0.025721。双灯T/TS已有约11.5..12.9%形状/噪声L1，不能把通过等同于精确保真。三起点是独立帧序列，不是三个场景。Roslyn通过，无新增本步GPU诊断；未改生产代码/资源，没有重新构建Player或证明对象运动/轮廓/TAA/normal-map。

**发现与下一项：** 当前SB高精度normal与DI/GI的8-bit历史表示不一致。新增 `RunCurvedHistoryAudit` 读取实际SB/16-byte GPU历史，静止同灯/材质/CPU位置下计算独立DI评分：最大法线差≈0.5801°，评分L1≈0.8821%、最大差≈7.7467%。这是接口量化差异的实测，不是GPU偏差证明或原平面64帧失败的归因。下一项修当前/历史法线表示，评审共享buffer布局与成本，用本步多序列和必要的GI/Player回归验收；最终合成保持原范围。详见 [运动与历史审计](../Ho-RTDI-NRD.md)。

## 23. 当前/历史法线表示一致性

**动手前评审：应修接口，不调亮度。** 前步测得静止同表面评分最大差约7.75%。核对用户current/previous RAB_Surface与官方Bridge一致性契约，将两种法线当前/历史都统一到2×16-bit oct，同时保留既有8-bit材质响应。历史从四到五个uint、16到20 B；共享header和host stride同步更新DI/GI，NRD F0 history reader同步字段偏移。不是新增GBuffer副本、Feature或PT接口。

**首项验证：** 真实GPU history的shading/mesh normal与SB坐标中央1024像素0不一致，host/Marshal stride=20；独立CPU评分L1在F9精度下0、最大差约0.000000167，新增编码/评分硬gate通过。该接口差异已消除，但不能据此宣称所有GPU估计无偏或原平面64帧失败已修。当前/历史同codec也覆盖FP16输入回退，材质/depth、SDK reservoir、采样预算与NRD配置不变。

**回归与收益边界：** 两条曲面运动各30组、静态曲面、两条核心/NRD、边界/HDR、Power/时间/预算与必要的GI两条路径通过。曲面运动镜面filtered比例0.989390..1.011075，L1最高0.131369；部分MSE略高于旧布局，不作普遍降噪改善结论。原平面64帧raw=0.964139的3%gate仍失败，512帧raw/filtered=1.015134/0.991015继续通过，分别保留。DI实测载荷223.437/503.547 MiB，SB增量另计；同步RG batch约6.815/13.238 ms，不能当孤立性能。

**部署与复查：** 两个Win64 Mono release构建与实际D3D12 EXE通过曲面/混合/仅时域/HDR/noise/初始visibility等旧gate，D3D11 inactive通过；packed NRD DLL SHA一致。Roslyn/派生检查通过，无新增本步GPU诊断。共享GI只同步20-byte host/writer/reader，NRD材质历史guide同步字段偏移，当前normal与previous同codec。原ABI/材质量化、SDK算法、预算和最终接收未扩张。

**成本与下一项：** 每个DI/GI history相对旧布局720p/1080p增加约3.52/7.91 MiB，SB原normal附件的增量另算；同时启用DI/GI各有一份history。无新纹理/pass/ray，有额外normal codec运算。后续依结果复查运动与实际场景支持，再评估环境/更多灯域，不继续围绕单个平面序列调NRD。

## 24. HoAtmosphere 环境光进入 RTXDI

**动手前评审：补灯域，应做。** HoAtmosphere只提供radiance与版本，没有环境PDF。核对用户PolymorphicLight.EnvironmentLight、固定SDK Presampling/InitialSampling以及官方采样API，DI自行构建128²等立体角luminance PDF mip，再调用原SDK环境RIS与环境候选采样；不用uniform假扮importance，不新增天空Feature或最终合成。

**实现与取舍：** 独立环境候选预算默认4、天空选项默认开，只有前置HoAtmosphere输入有效才分配PDF/RIS。环境使用保留index，解析灯分区/Unity Light映射不变。条件solid-angle density=N²/(4π)，target/final统一除条件密度，源概率来自SDK texel RIS。稳定版本缓存PDF，每帧更新RIS；天空内容/旋转/tint/gain变化重置DI/NRD，缺失路径清零pooled参数并绑定black cube。没有保存第二份天空。NRD confidence阶段也显式绑定环境输入。首个prototype因Texture2D元素类型不符固定SDK签名编译失败，已修正，失败不计通过。

**验证：** RG/compatibility常量天空raw≈(.996,.498,.249)，参考(1,.5,.25)，无/T/S/TS的3%条件通过；解析灯+环境MIS、封闭世界遮挡、HDR六面Cubemap与yaw0/90、gain/tint的独立半球积分、black/disable/absent生命周期通过。宽GGX参考≈.027514，raw≈.027515、NRD≈.027149，5%条件通过。旧两条核心/NRD、Power/边界/HDR/动态灯/时间/曲面运动/预算通过；GI独立性fixture显式关闭DI环境项后两条回归通过。解析-only预算仍223.437/503.547 MiB，同步batch约7.398/15.666 ms，含CPU/完整渲染/读回，不能作生产性能结论。

**部署复查：** 两个Win64 Mono release构建与实际D3D12 EXE通过常量天空新gate，raw/NRD比例≈1.005200/1.001977、封闭遮挡0，旧域检查通过；D3D11 inactive通过，packed DLL SHA一致。Roslyn/派生检查通过，无新增本步GPU诊断。Player只有64²常量天空，不算HDR Cubemap/窄镜面验收；原平面短运动失败保留，最终材质合成继续延期。

**成本与边界：** 新环境PDF全mip约.0833 MiB，256×64 uint2 RIS段.125 MiB，共约.2083 MiB/context，独立于画面尺寸；只在有效环境时持有，与local Power共用不同RIS段。每帧预采样/候选/可见性成本另计。尚无BRDF/VNDF环境MIS、窄镜面/小太阳、mesh emitters或最终NPR接收结论。旧GI primary-ray miss包含直接天空，与DI环境相加会重叠；保留两者独立开关和旧GI语义，叠加时选择一份直接贡献，后续再按bounce归属拆分。详见 [环境光接入](../Ho-RTDI-Environment.md)。

## 25. VNDF/BRDF 环境候选与 MIS

**动手前评审：先量化，应补提案。** 旧天空luminance PDF在rough≈.098的常量环境下raw/NRD比例约.9047/.4361、归一化MSE约802.06/7.6348，原5%gate失败。使用double自适应积分解析窄峰，避免粗采样oracle失真。把天空预算从4增加到8仍为.9296/.4900、MSE441.51/4.2156，证明仅加预算不足。

**参考与实现：** 核对用户RAB_Surface、Donut sampler、固定SDK SampleBrdf/MIS与Heitz VNDF论文。保留vendor，DI bridge新增余弦/GGX VNDF混合提案及匹配的`D*G1(V)/(4*NdotV)`方向pdf，不直接借用命名helper中的另一表达式。默认4天空+4BRDF，0可对照；纯lobe自适应，两种响应时0.5分配。BRDF query实际opaque世界几何，miss才采天空，cutoff保持0、距离上限100000。没有新增全屏纹理/pass或改最终BRDF/NRD，没有扩张mesh emitters或共享GI的BRDF实现。

**初项结果：** 同总候选预算的新窄镜面raw/filtered比例.997485/.985933，MSE5.234457/.029439；三roughness均通过5%。30/60度×三roughness的独立H-space积分通过。128² Cubemap仅- Z面中央2×2像素HDR1000/.02的小源，独立方形角域积分≈17.720746，raw/filtered≈.994679/.983982，通过。旧常量天空、Cubemap旋转/gain/tint、解析灯MIS、封闭遮挡和生命周期回归通过。

**部署与回归：** 两个Win64 Mono release构建与实际D3D12 EXE通过新增窄镜面天空gate，raw/filtered≈.996075/.985061；旧diffuse天空/封闭遮挡0、曲面/混合/visibility/HDR/noise通过，D3D11 inactive通过，packed DLL SHA一致。旧两条核心/NRD、材质/HDR边界、曲面运动及必要的GI回归通过，Roslyn/派生SHA检查通过，无新增本步GPU诊断。小亮源和斜视仍是Editor验收，不把常量Player当全场景证明。

**复查与成本：** 默认候选与几何查询数量增加，没有新增GPU常驻载荷也不是免费优化；相同预算对照用于判定提案收益，不当作孤立GPU性能。仍未证明任意窄镜面、小太阳方向/尺寸、动态normal-map、对象运动或最终接收。旧GI直接天空重叠与原平面64帧失败继续保留，后续按实际路径归属/支持缺口推进，不用NRD亮度补偿隐藏它们。详见 [环境MIS](../Ho-RTDI-Environment.md)。

## 26. 血月古堡支持审计与蒙皮世界遮挡

**动手前评审：按用户选定场景，应先补几何。** 源工程只读，依赖闭包共38资产约48 MiB，保留GUID/导入metadata/材质remap并复制匹配UnityGLTF到隔离工程。展开后的18盏灯为9Point/8Spot/1Directional，均Realtime、无Cookie、默认cullingMask，已有灯域足够；主模型却是单骨骼SkinnedMeshRenderer、15submesh/241472tri，旧registry完全排除，只剩测试球。先补不透明蒙皮当前姿态，Cutout仍明确排除，不把整个mesh当opaque。

**实现与参考：** HoRTScene为每个skin持有一份当前姿态Mesh，骨骼相对矩阵/BlendShape/源Mesh/质量变化才CPU Bake并UpdateInstanceGeometry，稳定姿态复用；推进Revision，DI/NRD保守清历史。Pose更新前后身份/图捕获约束和旧实例释放保持一致，无新Feature、纹理或native接口。核对Unity6.3 BakeMesh/手动BLAS更新与用户RTXPT per-renderer GPU repack；前版false参数在非均匀缩放实测双重缩放，修true后两条gate通过。GPU路径作为成本替代参考，本步不引入其Native/PT框架。

**结果与验证：** 实际古堡登记由1增至4（主体天空/古堡/地板三opaque submesh共13573tri，另测试球）；仍排除12Cutout/227899tri。两条GPU求交覆盖双骨骼、混合权重、BlendShape、独立距离/barycentrics/身份、非均匀/镜像缩放、离屏/缓存、非readable/source替换/显式编辑、无效姿态/启停/销毁/捕获后mutation；两条DI骨骼移走/归位及原SDK域通过，旧rigid/GI各两条回归通过。两个Mono Win64 release及实际D3D12 EXE蒙皮shadow0/open≈.222046，旧HDR/NRD/曲面/环境/VNDF等gate通过；D3D11 inactive与Roslyn通过，无本步新Shader诊断。原平面64帧失败保留，没有实际古堡完整画面或最终接收结论。

**复查与成本：** 实际首次CPU登记约22.75..77.29ms，64次静止平均.038.. .041ms且0额外Bake；自有完整Mesh计数29924936B≈28.54MiB，不含AS/驱动或GPU帧耗时。DI/GI独立HoRTScene会各持一份，不能宣称廉价动态角色或时域运动支持。复现copy脚本与隔离审计保留，源资产SHA复核无变动，没有修改源场景/renderer。下一项优先正确lilToon Cutout候选UV/alpha/cutoff；它关系到大多数剩余几何，优先于更多灯型/ReGIR/NRD参数。详见 [场景支持与验收](../Ho-RTDI-SceneSupport.md)。

## 27. lilToon Cutout 原生最终可见性

**动手前评审：应补最终裁切，不扩张PT。** 用户参考anyhit与官方RTXDI桥允许保守可见性跳过alpha-test、最终再求真实遮挡。古堡12材质均无多主纹理/视差/dissolve，基础alpha域足够推进。用户同时指出主模型含球形天空壳且未拆分，改用可控小场景定量验收，古堡仅做资产覆盖，隔离审计允许临时排除天空材质；不编辑/拆分/保存源资产。

**实现：** DI显式提供已知Cutout predicate和私有原生命中Material，非opaque登记；默认HoRTScene/GI仍opaque。inline候选/时空校正/BRDF提案统一CULL_NON_OPAQUE，Spatial后、Shade前原生最终射线读取同一reservoir/RAB方向距离与真实Mesh UV、借用源Texture2D，匹配Color.a/cutoff/AlphaMask/UV/纹理采样，透明IgnoreHit。Shade/NRD/历史使用实际最终visibility，SDK/BRDF/NRD不变。Opaque共用私有Material，未另复制纹理/UV，无新Feature/native接口，alpha参数/纹理内容变更清历史。

**验证：** 两条64²硬件路径通过常量/Color/cutoff相等、四mask及scale/value、纹理revision、骨骼/生命周期与NRD全遮挡/透光。独立CPU几何+采样oracle的8组transform/rotation/Point/Bilinear/混合wrap有4835遮挡+1335透光像素通过，102边界明确跳过；初次参考float共边漏两面已用临时GPU数据确认并修参考容差，诊断代码全部撤去。双灯仅Near遮挡、512帧×无/T/S/TS能量比例.996001/1.005337/.996663/1.003256，两条3%gate通过；Spot/Directional、独立环境壳shadow0/open≈(1,.5,.25)通过。古堡全登记16/12Cutout、排除天空后15/12，前者0排除、后者单一测试天空排除，不是完整画面/漏光结论。

**部署与复查：** 三进程旧renderer资源补引用/保存/重载通过，两个Win64 Mono release与实际D3D12 EXE使用non-readable mesh/texture通过Cutoutshadow0/open≈.222046与骨骼更新，旧NRD/HDR/环境/VNDF/曲面等gate通过；D3D11 inactive、旧DI/NRD两条、共享GI两条及rigid/skin求交通过，Roslyn/派生SHA通过，无新增本步Shader诊断。源hash复核发现场景层级/Volume有外部更新，模型/材质/纹理等其余清单一致，保留隔离快照且未回写源工程；工作区过滤与语义边界复核，不宣称原平面短运动失败已修。

**成本与下一项：** 有Cutout时逐相机一张R8 visibility，720p/1080p约.879/1.978MiB、R32回退四倍。最终射线替代原Shade inline终测，不是额外复制天空/GBuffer；新增native dispatch/anyhit/SBT和更多AS几何的实际GPU成本未量化。仅LOD0，复杂alpha/PB/动态UV/任意变形仍排除，GI Cutout secondary未扩张。下一项先在受控局部场景评审边缘、运动/历史与成本，再据证据优化；古堡合并天空不当作调NRD/采样依据。详见 [Cutout](../Ho-RTDI-Cutout.md)。

## 28. 局部 Cutout 质量与单灯精确路径

**动手前评审：先测形状与成本，不调亮度。** 继续用可控平面、alpha洞与骨骼caster，独立像素参考，避免古堡合并天空干扰。核对官方NRD信号/运动契约和用户ComputeGradients/Confidence及固定SDK。单灯空域复用raw总能量近1但逐帧方差L1≈.208/.226，NRD staticS/TS能量≈.925/.916；无复用的camera motion仍有约10%L1及2pixel margin约5%leak。

**实现与取舍：** 单个解析delta灯且无有效环境，没有灯/UV随机自由度，跳过Temporal/Spatial。保留实际Initial、主表面/灯历史及必要frontend，NRD已分配时把raw复制到原有发布输出，跳过Confidence/RELAX/Resolve；多灯/天空仍原完整路径，回到降噪时history失效。不是vendor/NRD参数修改、亮度补偿、新Feature或最终接收设计，也不宣称显存节省。

**验证与限制：** 两条64帧×896像素的static/camera/caster及静态T/S/TS均raw/发布一致，能量≈.999689/.999687/.999672，L1≈.000311/.000313/.000328，1/2pixel margin leak0，原物理shape gate通过。单灯历史断言改查有效精确输出；原动态transport/age fixture用0通量控制保持真实多灯调度，snapshot按identity取索引，原非零双灯统计/RELAX与Cutout复用gate另保留。GPU scope/采样入口已补，但批处理样本稀疏不稳定、未达32样本条件；RenderDoc注入超时、无capture且进程已终止，不给GPU预算结论。正式计时与真实多灯边缘/运动仍未完成。

**完成前复查：** 旧核心、NRD两条、Cutout两条，材质边界/HDR/环境/VNDF/曲面、必要GI两条回归通过；两个Mono Win64 release构建与实际D3D12原gate/Cutout、D3D11 inactive通过，Roslyn/派生SHA通过。无native/vendor/历史ABI修改，目标继续保持完整DI范围，下一项真实多灯局部质量与可复现GPU成本。详见 [单灯精确路径](../Ho-RTDI-SingleDelta.md)。

## 29. 真实双灯局部质量与失败基线

**动手前评审：必须验证多灯，不能以单灯替代。** 独立Cutout平面、Near红/Far蓝两非零点灯，各自解析能量与独立阴影参考。64²/896ROI，192预热，静态512帧、相机/caster运动256帧；无/T/S/TS加全透光控制，正式保存JSON/像素均值CSV。核对用户ComputeGradients/ConfidencePass、本地confidence及pinned NRD history/edge契约，不先调参数。

**结果：** 两条raw分通道能量均在3%内，但filtered质量未通过：无复用静态红1.143309/蓝1.028107、meanL1 .129259；静态S/TS约.187941/.190498形状L1；相机TS蓝1.063002；caster每帧Revision重置，均值接近1但frameL1 1.060215。全透光无复用控制通过，TS仍蓝1.063502，问题不局限alpha命中。正式5%分通道能量/15%meanL1/35%frameL1 gate输出qualityPass=false；诊断exit0不冒充通过，实际正式运行exit1确认。Roslyn通过，无生产/Shader/native变化。

**完成后复查与下一项：** 本步交付未达标基线，不宣称问题已修或DI完成。静态颜色/形状偏差与运动历史清空分开定位，先对照输入、confidence、clamping/spatial与世界变化策略，禁止亮度补偿或用单灯收缩验收。无新Feature、不改古堡/接收设计，完整目标继续；GPU计时仍未达有效证据。详见 [双灯质量审计](../Ho-RTDI-MultiLightQuality.md)。

## 30. Cutout 漫反射亮度边界与回归

**动手前评审：沿用真实多灯失败门槛，分阶段对照。** 核对用户NRD设置、固定NRD头及Atrous亮度权重。仅改一项的Atrous=1、历史8帧、phi=.5三个对照：前两项仍静态红+9%/+13%，后者静态/TS/全透光TS通过。独立重跑原配置仍静态红1.143329/蓝1.028095，meanL1 .129241，支持边界滤波问题判断；诊断多一帧，不冒充逐样本一致。

**实现与取舍：** 全局phi=.5使原opaque filtered MSE升至.004099，放弃全局改动。仅DI有Cutout最终可见性时收紧diffusePhiLuminance=2→.5；无Cutout还原2，每帧向native更新切换。五次Atrous、历史预算、specular、ReSTIR/native/Shader/guide ABI均不变，无新资源或pass；Editor单项A/B不加产品开关。这是相机级条件，任意Cutout场景噪声仍要实测。

**实际结果：** 两条原8组完整对照raw仍3%内，filtered原门槛7组通过：静态红1.004658/蓝.980818、meanL1 .057627，camera红.993814/蓝.993022、meanL1 .059636，静态TS meanL1 .083784。caster均值接近1、frameL1仍1.059944，因此qualityPass=false和正式失败保留，不宣称动态遮挡物修复。普通opaque noise filtered MSE .000183恢复原表现；Cutout/opaque/Cutout配置.5/2/.5及filtered能量.997938/.999160/.989462，两条通过。

**完成前复查：** 核心两条、NRD两条、Cutout两条、材质边界、HDR镜面、环境VNDF、曲面回归通过；Runtime/Editor Roslyn及NRD派生SHA通过。两个Win64 Mono release构建errors0，实际EXE D3D12两条与D3D11 inactive均pass=true，无新增DI shader诊断，仍有原lilToon重复keyword警告。未改共享GI行为、原古堡资产或最终接收，不重复无关GI测试。过滤提交本步owned文件；GPU预算仍未证实。详见 [Cutout滤波](../Ho-RTDI-CutoutFiltering.md)。

**下一项：** 核对世界revision与NRD主表面历史的区别；现有confidence的相同灯shortcut只在static-world成立，且保守射线跳过Cutout。先用可复现诊断评审保留历史的噪声收益/拖影失败，再设计真实动态可见性confidence，不能简单取消全局reset上线。

## 31. 动态遮挡历史收益与残影失败对照

**动手前评审：不能直接放宽world reset。** 固定世界revision混合pose/transform/实例集合，现有confidence相同灯捷径和保守inline跳过Cutout，尚无真实动态alpha变化信号。核对用户RTXDI固定FullSample ComputeGradients的同reservoir再求可见性、官方NRD History Confidence的同采样梯度契约。

**实现：** 仅Editor隔离入口对比原重置/保留NRD世界历史，ReSTIR temporal仍原策略。原慢速caster、正向快速caster、16帧阶跃，独立CPU逐像素alpha参考且断言Far始终无遮挡；新增最差帧/阶跃前三帧变化像素红通道误差，恒亮蓝不稀释。无生产开关、资源、Shader/native/vendor或合成修改。

**证据与取舍：** 慢速frameL1 1.064071→.125951，快速1.024179→.237205，raw各case完全相同且3%内。但阶跃变化像素L1 .157649→.570931，整体frameL1却.204079→.176262，单看均值会误判。新增变化像素35%门槛后保留历史qualityPass=false，不上线忽略世界变化。原保守策略仍生产默认；下一步做真正动态最终可见性confidence后再评审保留历史，不能用平均噪声掩盖残影。

**验证与复查：** Runtime/Editor Roslyn通过，两条硬件对照、正式失败入口和默认核心回归核对后独立过滤提交；无需重复Shader/native/Player未变的构建或GI测试。详见 [动态可见性](../Ho-RTDI-DynamicVisibility.md)。完整DI目标继续，GPU预算和任意动态主表面仍未证明。

## 32. 同历史采样的动态最终可见性confidence

**动手前评审：必须补真实变化信号，不能直接取消reset。** 用户固定RTXDI FullSample同reservoir再求可见性及官方NRD同采样梯度契约作为依据。native raygen读取上一帧实际Shade的样本/UV/权重、灯映射/主表面，在当前TLAS求opaque/Cutout anyhit，比较已存亮度；不混新RNG。检查3×3历史格内至多9个样本，含旧亮度0，保留各lobe最大相对变化，复用原gradient/confidence/history资源与原4字节payload，无新Feature/纹理/buffer/native ABI。

**调度取舍：** ReSTIR仍拒绝完整world变化；仅Cutout最终可见性路径，NRD对pose/transform保留历史并用真实confidence，对新增StructureRevision硬重置实例/材质/掩码/源mesh及显式geometry dirty。追踪skinned原source，防止同拓扑换源误归pose；共享GI仍原Revision策略。世界/灯稳定跳过新射线。4/2次梯度模糊扩散低confidence，快速运动噪声仍高；原生格内信号直接转换/采样后通过运动与残影门槛，静态compute处理保留。

**立体部署反馈：** 平面通过后，新增Player实心Cutout cube移动，phi=.5/sigma2蓝≈.918失败；仅phi1阶跃蓝≈.923仍失败。GPU阶段即时副本按实际scale恢复单位，发现裁剪与反馈压低稀疏高亮样本；固定phi1下sigma2→4，阶跃Temporal/Clamping/final≈.971260/.960514/.967756→1.001048/1.000647/.998615。最终Cutout phi1/sigma4，无Cutout仍2/2，时间预算/其他NRD设置不变，显式切换；sigma共用于两lobe，新增动态镜面仍需后续验证。未改vendor/派生算法/DLL，不补偿亮度。

**质量与余量：** 原双灯8组正式两条qualityPass=true，全部raw3%内、filtered各通道5%/meanL1<.15/frameL1<.35；caster frameL1 .127042。额外慢/快/阶跃生产frameL1 .128248/.213909/.179925、变化像素.209548<.35。静态红1.045045/全透光TS蓝1.046886近门槛，保留余量，不宣称普遍无偏。CPU native gradient oracle216格/169变化，两条通过；匹配FP16解析容差.003及导数/量化传播confidence.03，不改质量gate，稳定跳射线与结构reset通过。

**部署与复查：** 核心、NRD、Cutout、scene skin与必要共享GI两条及HDR回归通过；最终profile切换能量.997681/.999160/.990697，opaque noise filtered MSE仍.000183。Runtime/Editor Roslyn及派生SHA通过；两个Win64 Mono release errors0，实际D3D12 EXE通过原gate及non-readable cube连续/阶跃，RG frameL1 .157265/.173242、变化像素.109085，compat .164167/.172591、变化像素.113743；D3D11 inactive通过。Player部分蓝能量≈.950761近5%边界，不能扩张结论。初次测试入口namespace拼错只重跑正确入口；CPU oracle补FP16/解析误差传播后通过，未改生产梯度换通过。无新增DI shader诊断，原lilToon keyword/C#警告另计。

**成本与下一项：** 有变化时最多额外1可见性射线/有效历史像素，scope已加，无新显存；实际GPU预算未证实。下一步验证动态Cutout镜面、移动primary、灯/身份变化及余量，再优化采样成本，不放大为整古堡质量或最终NPR接收。过滤提交本步owned代码/验证/文档。详见 [动态visibility confidence](../Ho-RTDI-VisibilityConfidence.md)。

## 33. 动态Cutout镜面独立质量门槛

**动手前评审：** 第32步sigma影响两lobe，必须验证specular，不能用diffuse代替。核对用户固定ComputeGradients的同reservoir重求和本地Raygen的GGX/FP16亮度；沿用独立解析GGX/Smith/Schlick参考、输入F0/roughness量化、点灯衰减及CPU alpha几何，不改生产参数。

**实现与结果：** 原多灯fixture可显式评分raw/filtered specular，报告增加signal，独立前缀不覆盖旧diffuse。F0=10/255、roughness=102/255，原8组两条正式qualityPass=true；static红/蓝.984996/.991624，camera meanL1 .028293，caster frameL1 .108094。额外慢/快/阶跃frameL1 .110073/.343487/.347752，阶跃变化像素.309657<.35，两条正式通过。快速/阶跃非常接近上限，峰值.591305仍保留，不宣称完全平滑或任意材质通过。

**验证、范围与下一项：** Runtime/Editor Roslyn通过，旧diffuse八组正式回归通过；本步只Editor验证/文档，生产、shader/native/GI与Player模板均未变，不重做无关构建。复查信号选择/独立GGX和alpha参考、原gate/失败语义，过滤提交owned文件。接下来单独审计移动primary：当前IN_MV黑纹理/worldMotion只表示零对象运动，用户参考和NRD契约需要对应表面运动；已找到HoURP17 motionVectorColor与lilToon MotionVectors pass，先检查覆盖/时机再直接消费，不引入AC复制/新Feature。详见 [Cutout镜面](../Ho-RTDI-CutoutSpecular.md)。

## 34. 移动主表面梯度起点修正与完整失败基线

**动手前评审：** caster通过不能代表primary运动。保持红/蓝灯及远处Cutout稳定，移动实际Quad深度，参考用当前平面交点+Lambert/GGX，不沿用z=0。用户FullSample旧sample surface加worldMotion再求当前TLAS；本地旧worldPos可能在移动表面后方，假自遮挡。真实motion仍未接，先修正已重投影匹配表面的射线起点。

**实现与证据：** Native gradient从匹配current.worldPos重求旧reservoir/UV/权重，4行注释/赋值，无新资源/参数。64² central256、192+256帧，static/smoothZ/stepZ两lobe。平滑diff/spec frameL1 .457433/.503624→.093396/.049972；阶跃.134944/.305553→.105428/.051900，raw全3%内，两条一致。但static diffuse红/蓝.946252/1.086598，smooth .940400/1.108769，仍5%gate失败；静态偏差独立于运动。originPass=true/qualityPass=false分别保存，完整入口实际exit1，不用起点15%noise gate替代完整质量。

**验证、范围与下一项：** 起点入口两条通过，原CPU native gradient oracle两条与Cutout diffuse/specular八组通过；Roslyn/派生SHA通过。两个Mono release errors0、实际D3D12 EXE两条原gate与primary spec起点raw .998981/1.012912、frameL1 .047741通过，D3D11 inactive通过；无新增DI Shader诊断。无共享GI变化，不重复GI。复查源位置修正的近邻对应限制和完整false，过滤提交本步owned代码/文档。下步验证URP/lilToon已有motion的真实帧时机/覆盖/符号和当前主表面关系，直接借用/转换不走AC；静态局部diffuse能量偏差另查，不靠运动向量消除或放宽gate。详见 [移动主表面](../Ho-RTDI-PrimaryMotion.md)。

## 35. 既有motion协议与逐请求历史限制

**动手前评审：** 先核对HoURP17 motionVectorColor/MotionVectorRenderPass、lilToon MotionVectors及NRD符号契约，再读取实际GPU源，不先假定每次请求可用。64²正交Quad x0/.125/.25/0，同应用帧两条UV x0/.062469/.124939/0、y0及同帧gate通过，是累计到应用旧矩阵。兼容初次把shader全局当临时RT读0，改全局采样后与RG一致。

**失败与取舍：** 跨Editor update和Player手动请求未满足增量.0625；自动Player的捕获计数仅手动预热1次，无后续捕获，隐藏普通启动也未证明可用。补空读回/本帧计数断言，源probe明确false/exit1；不把FrameCount/旧0值当有效源，不推断唯一原因。未给NRD绑定未知motion，产品不额外请求pass或纹理；借用/读回仅验证启用，Player CaptureMotion只属隔离fixture。原生产/参数/native/GI/最终接收不改。

**验证与下一项：** Roslyn、两条同帧gate、原diffuse8组通过；两个Mono release errors0，默认实际D3D12两条原suite及D3D11 inactive通过，源probe实际失败保留。复查资源读声明、compat全局采样、有效捕获和产品无死成本，过滤owned提交。实际自动渲染时序/覆盖/旧帧仍待验证；静态局部diffuse能量偏差不依赖它，下一项先分阶段定位该偏差继续推进DI。详见 [motion协议](../Ho-RTDI-MotionProtocol.md)。

## 36. 静态彩灯的后续Atrous阶段定位

**动手前评审：** 第34步static控制也失败，不能靠motion解决。核对pinned NRD头/RELAX及原RAB/SDK目标契约，以即时GPU阶段副本按真实scale恢复单位、包含0输入像素，固定原64² central256/192+256观察。

**证据：** raw红/蓝1.001966/.981079，Temporal1.000924/.979831、Clamping1.000925/.979830、首Atrous .997767/.981529，后续最终.946180/1.086831。无Cutout/关闭clamp/延长history/关闭ReSTIR spatial不能解决；仅3次滤波静态最终.992161/.993944。NRD范围[2;8]，旧AtrousOne实际上请求非法1再归下限，本步修为明确2并锁定A/B五次baseline，保留历史名字与更正说明。

**未接受与范围：** 迭代/phi分辨率候选主表面可过，但镜面阶跃.357087或Player蓝.920688失败；RGB importance raw正确却带来滤波/阴影/残影失败；分lobe半径派生候选也Player阶跃蓝.928451失败。所有生产、RAB、NRD参数/派生shader/生成器实验均撤回，不放宽原门槛，不用单灯/局部通过替代目标。保留阶段入口和正式完整Compatibility入口，当前qualityPass=false/exit1。

**完成前复查与下一项：** Runtime/Editor Roslyn、原SHA/派生check通过；恢复后阶段current/atrous3复现、完整gate实际失败。过滤仅Editor探针/参数范围/文档，无生产/GI/Player模板变化，不把实验构建失败冒充部署验收。下一项按方差与颜色梯度响应改善空间阶段，继续保留立体/镜面压力门槛和完整DI范围，motion源/GPU成本仍待证实。详见 [阶段审计](../Ho-RTDI-PrimaryFilteringAudit.md)。

## 37. diffuse方差传播和固定交换权重

**动手前评审：** 保留全部原gate，核对pinned Smem/Atrous及已采用specular传播。diffuse首轮邻域moment差混入真实照明梯度，后续中心var/range及各行接受sumW造成不对称彩灯混合。单pair改善static但移动仍失败，历史均值除数候选不足且增噪，撤回。最终首轮保持RGB/range原处理，以w²传播temporal spread；后续pair min variance和固定Gaussian交换，拒绝系数留中心，不按接受sumW归一化；variance按系数平方，lastpass historyLength保留。

**结果与取舍：** 主表面三diffuse原5%完整两条正式通过：static红/蓝.983276/1.016998、smooth .976124/1.041844、step .978639/1.043692，frameL1 .083512/.075203/.086434；原spec输出一致。Cutout diffuse/GGX八组及额外spec运动原两条gate通过，镜面近上限余量仍未改善。opaque noise filtered MSE .000183→.001333，raw .050401不变，原<raw×.3通过但绝对noise约7倍；接受完整能量与仍约97%方差抑制的实测取舍，不声称降噪全面变好。随机offset/资格不一定互惠，不声称严格全图守恒。

**验证与范围：** boundaries/颜色/F0/金属/薄阴影/深度、HDR、环境GGX/MIS、曲面通过；Roslyn及原SHA/派生check通过。两个Mono release errors0，实际D3D12两条原suite/不可读资产Cutout及primary起点通过，立体连续/阶跃noise约.207/.234高于旧值但原gate通过，D3D11 inactive通过，无新增DI shader诊断。未改NV原始源/native/资源/5次调度/目标/Shade/NRD参数/GI；生成器和两份DI派生shader owned过滤提交。下步验证相关样本/材质/灯/尺寸并改善noise，真实motion与GPU预算仍待验，不扩张接收/AC/lilPBR/SSS/PT。详见 [diffuse交换](../Ho-RTDI-DiffuseExchange.md)。

## 38. 彩灯噪声与复用对照

**动手前评审：** 第37步能量改善但噪声退步，不能盲目抬方差。核对用户RAB/NRD设置及pinned SDK归一化，参考GRIS/MCMC原始研究。新16组对照包含初始1/8、同位置/分离红蓝灯、四种复用，统一种子与192+256帧、CPU解析参考；分别记录RGB/亮度MSE和去每像素时间均值的空间/lag1相关。

**证据与候选：** raw邻域相关很低，而8候选无复用亮度MSE小于5e-9但RGB MSE .077567～.083336，不能只看亮度。生产两条15/16组通过，分离8候选spatial蓝1.051984失败，入口保留false/exit1。全阶段相关上界noise .000170但primary蓝最高1.124730；仅首轮上界noise .000416但蓝1.058485；RGB向量目标同位置新MSE .000163却分离灯蓝最高1.070385。均撤回，不放宽门槛。

**验证与下一项：** 最终只Editor探针/文档，运行时diff为空；恢复后原完整primary qualityPass=true，新16组两条一致且正式失败保留，Roslyn/原SHA/派生check通过。不重复未变的Player/GI。下一项区分颜色噪声与真实彩灯梯度，审计权重对称性，修复需覆盖新16组与原运动/镜面/Player，不为古堡合并天空或通用PT扩张。详见[噪声对照](../Ho-RTDI-ReuseNoise.md)。

## 39. 双灯阴影内部的真实NRD门槛

**动手前评审：** 核对用户RAB/NRD及pinned Smem/Atrous。首轮接受sumW与luma不足以描述彩灯噪声，但降噪不能牺牲阴影。固定系数首轮增噪且新spatial-only仍失败；两端方差加RGB目标仍primary/动态spec失败；几何3×3预滤波旧gate通过并降低MSE，却需真实阴影审计。

**覆盖与证据：** 旧薄阴影/单灯core走single-delta直通，不能验证本改动。离线双灯CSV发现几何预滤波core最大.062893～.066808，相对生产.023282～.026633放大约2.5倍，撤回。正式双灯fixed diffuse四组加入独立CPU 5×5阴影mask、168 core像素/512帧逐像素均值最大值，沿用2%标准。两条raw core=0、filtered最大2.3%～2.7%，虽旧整体均值通过，新完整入口false/exit1，未放宽门槛。

**验证与下一项：** 最终仅Editor/文档，运行时diff为空；两条新core失败复现、原完整primary true，Roslyn/原SHA/派生check通过。不重复相同Player/GI。下一项用既有阶段捕获定位Temporal/Clamping/首轮/后续对同一core的红光混入，再同时修复局部阴影与彩灯噪声，保留动态镜面/Player，不扩大PT/合成/古堡编辑。详见[阴影core](../Ho-RTDI-CutoutShadowCore.md)。

## 40. 后续Atrous的RGB差判定

**动手前评审与定位：** 同CPU 5×5 core/168像素读取实际Temporal/Clamping/首Atrous/后续，即时副本恢复本帧GPU scale、逐帧捕获和最终输出一致性断言。早期三阶段core红0，后续才2.3%～2.6%。核对用户RAB/NRD及pinned HistoryClamping已有RGB绝对差度量；后续diffuse range从abs(L(A)-L(B))改L(abs(A-B))，避免相反RGB变化抵消。首轮/物理Shade/提案/参数/资源/native/spec/GI不改。

**结果与取舍：** 两条双灯8组含2% core正式通过，最大1.7198%～1.9375%；新16组正式通过，分离8候选spatial-only蓝1.051984→1.004080。原完整primary、动态spec/HDR/环境/边界/曲面通过。旧noise MSE .001333→.002776，primary diffuse frameL1升至.125～.135，仍过原gate但噪声增加。统一种子core最高.019488，余量小，不宣称普遍无偏。

**Player复查与部署：** 新旧运行时初次mixed spec均约1.394失败；raw诊断约1.418、SB量化输入正常。发现CPU相机清空targetTexture后aspect1.789/窗口1898×1061，与64²请求不符；核对URP绑定/恢复源码，fixture固定目标aspect并断言，保留原独立参考/gate和raw日志，撤去临时SB捕获。两个Mono release errors0，实际两条全suite及D3D11 inactive通过；立体连续/阶跃frameL1约.234/.272高于旧值，RG阶跃蓝.951655近门槛。Roslyn/原SHA/派生check通过，无新增DI shader诊断。过滤owned提交，不改旁边仓库，不扩张合成/PT。

**下一项：** 改善噪声并扩展材质/灯/尺寸，在新的RGB判定下保留全部core/彩灯/动态镜面/Player验收；实际motion/GPU预算仍待证实。详见[RGB差判定](../Ho-RTDI-RgbContrast.md)。

## 41. 零F0表面的RGB重要性

**动手前评审与证据：** 第40步阴影通过但噪声增加。新RGB判定下重试首轮几何预滤波，noise .000299却core2.08%～2.31%，撤回。实际四组/六阶段读取RGB/亮度时间方差，正确区分Temporal moment、首轮variance与末轮history alpha；基线Temporal RGB var .018286而luma约1.6e-9，后续几乎不降颜色噪声。核对用户RAB及pinned SDK/NRD，不直接增加未知RGB历史资源。

**实现与结果：** 统一目标回调只对量化F0三通道全0使用完整贡献的RGB向量长度，其他F0保留原亮度；不关Fresnel/改Shade/光强，不增pass/射线/资源/参数。两条原双灯8组和新16组通过，默认8候选同位置none filtered MSE .005872→.002550；core最大1.04%～1.24%。512帧最终RGB var .003856→.001667。非零F0镜面/primary与边界/环境/HDR/曲面通过。0↔1/255时间切换两条raw3%/filtered5%/frame.35通过。单候选无复用noise .002776未改善，空间材质边界尚需扩大验证。

**部署、范围与下一项：** 两个Mono release errors0，实际两条全suite及D3D11 inactive通过。立体连续/阶跃frameL1 .179656/.233885、蓝能量.990885/.991820改善，但阶跃变化像素误差约.134→.277329，仍过.35，短期响应待继续改进；单候选无复用Player MSE仍.000526。Roslyn/原SHA/派生check通过，无新增DI shader诊断，GI/合成/native不变。下一项扩大材质/灯/尺寸及GPU成本验证，评审单候选/非零F0颜色误差和短期残影，保留所有core/能量/动态镜面/Player门槛，不扩张通用PT。详见[零F0 RGB采样](../Ho-RTDI-ZeroF0RgbSampling.md)。

## 42. 相邻策略的材质相容性与尺寸验收

**动手前评审与失败：** 零/非零F0切换不能代表空间边界。新增左右半Quad F0=0/1、布局交换、64/128、none/TS共8配置/32区域，固定60Hz/192+512、CPU解析与缺失绿色通道检查。基线两条30/32，64混合TS右蓝filtered .919141、edge raw/filtered .961776/.929789失败。均匀0/1四区域对照各通过，保留正式失败语义，不放宽门槛。

**实现与结果：** 核对用户RAB/pinned SDK material-similarity hook，在原阈值前限制空间邻居使用同一提案策略，predicate与目标回调共用。是本应用稳定性约束，SDK允许不同目标，不宣称必须相同；边界减少复用机会，不改Shade/参数/资源/射线。两条32/32数值一致，原右蓝恢复.992275、edge .997412，64/128最小blue .966966/.968828，最大frameL1约.12。旧core/16组/primary/动态spec/边界/HDR/环境及时间切换回归通过。

**共享与下一项：** 发现GI bridge直接包含共享DI，41目标变化曾传入GI，补充更正历史说明；本步编译隔离GI目标与新guard，必要GI两条Editor GPU回归通过，不推进新GI。两个Mono release errors0，实际DI两条全suite/D3D11 inactive通过，立体阶跃frame约.234、变化像素.277/.285仍过门槛但需改善；GI未新增Player验收。Roslyn/原SHA/派生check通过，无新增DI/GI shader诊断。过滤owned提交；下一项更多材质/灯/更大尺寸、GPU预算和动态短期响应，不扩张最终合成/PT。详见[策略边界](../Ho-RTDI-ProposalBoundary.md)。

## 43. 独立Development Player GPU计时入口

**动手前评审：** 现有Editor样本不足，先核对Core ProfilingSampler及Unity6 FrameTiming/Recorder原始API，GPU数据延迟三帧且有计时开销。新增独立Development build目录/参数，每应用帧一次请求，512² base/opaque/movingCutout三状态，160帧/舍弃36，源标记复用，不修改产品Shader/资源。区分可用/新鲜frame记录、正GPU时长、相机事件、CPU标记和调度；不足-1/false/exit1，异常与Release误用均明确退出。

**证据与限制：** RTX4060/D3D12、Development和FrameTiming均启用、GPU timer1GHz；三状态相机事件160/新鲜frame记录124，但GPU时长/marker均0样本。移动Cutout CPU标记及gradient调度124。隐藏非batch手动仍缺GPU时长，自动相机相机事件0，不能据此猜唯一原因或替代GPU预算。完整帧/状态差值即使取得也不等同单DI/pass成本，不做基于假GPU时间的优化。

**验证与下一项：** Development和两Release build errors0，计时实际exit1、错误build类型也exit1并有JSON；普通实际DI两条完整suite及D3D11 inactive通过，Roslyn/提交检查通过。过滤仅测试构建/fixture/文档，产品DI/GI不变。下一项扩大尺寸质量、选择有真实帧/呈现/query证据的计时采集，GPU预算仍未证明；不扩张合成/PT。详见[GPU计时审计](../Ho-RTDI-GpuTimingAudit.md)。

## 44. 256/512静态质量扩展

**动手前评审：** GPU预算仍未证明，但实际尺寸质量可以独立验证。核对用户NRD矩形/资源尺寸及本地pool，完整渲染256/512GPU目标，CPU评分世界区域采用stride2/4网格并独立评分近缝2px，不用缩小GPU工作量或称全图逐像素。保留原采样/过滤参数与门槛，固定60Hz、192+512帧。

**结果：** F0左右0/1、布局交换、none/TS共32项两条一致通过，最低blue .980066/.980538、最大frameL1 .123415/.126900。新双灯Cutout静态四组，每组3584点、core1536/1664；最大core均值泄漏约.48%～.62%、raw core正确，能量3%/5%和frame.35通过。像素半径改变世界足迹，未将其宣称为世界尺度一致或动态/specular/1080验收。

**验证与下一项：** Roslyn/新旧入口/评分范围/原失败语义核对，旧64/128边界和64core检查，产品Runtime/Shader/Player/GI无变更，不重复相同构建。过滤仅Editor验证/文档。下一项动态更大尺寸/灯分布及真实应用帧/GPU预算，保留所有原门槛，不扩张合成/PT。详见[尺寸质量](../Ho-RTDI-ResolutionQuality.md)。

## 45. 256/512动态caster验收

**动手前评审：** 静态通过不能说明大尺寸移动阴影稳定。核对fixed FullSample旧sample/worldMotion重求与本地gradient/world-structure调度、用户NRD frame接口；复用CPU大小可选平面/alpha参考，每帧更新骨骼位移。GPU完整256/512、3584世界区域网格点，192预热/256观察固定60Hz，不消费未知URP motion。

**结果与余量：** 两条slow/rapid/step六组正式通过，raw3%/filtered5%/frame.35保留；slow约.113～.118、rapid约.243、step约.167～.172。阶跃真实变化像素最初三帧误差.329556/.314681<.35，但256接近门槛，不能用整体均值掩盖或宣称任意动画稳定。gradient调度255/256/15符合运动和稳定跳过，非GPU计时证明。

**验证与下一项：** Roslyn/新报告/原失败语义、旧大尺寸静态及64双灯core回归通过，无生产/Player/GI变更，不重复相同构建。过滤Editor验证/文档。下一项动态短期响应余量、更多灯/primary/尺寸与实际帧，1080/大尺寸spec/GPU预算/真实motion仍未证明，不扩张合成/PT。详见[动态大尺寸](../Ho-RTDI-LargeDynamicCutout.md)。

## 46. 大尺寸动态镜面失败基线

**动手前评审：** 第45步只证明漫反射，旧64镜面不能代表256/512。复用可控Cutout场景、独立CPU Smith GGX与每帧遮挡参考，SB量化F0=10/255、roughness=102/255。核对用户NRD参数接口/固定头文件及fixed FullSample receiver/worldMotion可见性参考；不改古堡合并模型，不接未知URP motion。

**结果与拒绝：** 默认两条各3/6，rapid frameL1=.507913/.539075，256 step=.357448失败；平均raw3%与filtered5%均通过。phi=2对照rapid=.469027/.508687仍失败，Atrous7/8=.497639/.517001且256 step=.352287仍失败，均不接受为生产修复。报告区分default/phi-two/radius并记录实际参数，保留false/exit1，不覆盖默认或放宽.35。

**下一项：** 用已有镜面阶段捕获，先定位256 rapid的raw、Temporal、Clamping、首/末Atrous与最终发布误差及缩放一致性，再决定运行时改动。只改本步Editor与文档，原大尺寸漫反射及64镜面作共享夹具回归。详见[失败基线](../Ho-RTDI-LargeDynamicSpecular.md)。

## 47. 大尺寸镜面阶段定位

**动手前评审：** 第46步快速噪声参数对照失败，先定位而不继续无依据调参。复用既有镜面阶段接口，固定256/3584点，slowTS/rapidTS/rapidNone三配置，192+256与独立CPU参考。核对NRD材质去调制/还原与Normalize/Resolve；初次仅HDR缩放被一致性gate拒绝，修正为固定均匀正交场景的raw/归一化输入比例，并全观测点检查末阶段/发布RGB。

**结果：** 两条diagnosticOnly真实检查通过，正式rapid质量仍失败。rapidTS raw/Temporal/Clamping/首/末/发布frameL1=1.023884/.653306/.650717/.578224/.507934/.507913；rapidNone最终.530498，slowTS .077365。空间阶段在减小误差，关闭ReSTIR复用不足以解决。pipeline10每帧四轮、1024复制，不能当GPU成本；比例还原仅适用于固定均匀灰F0/正交场景，不外推复杂材质。

**下一项：** 读快速confidence与实际历史，区分累积利用和输入噪声的贡献后再选运行时改动，保留正式能量/噪声、changed-pixel/core/Player门槛。仅Editor/文档，Roslyn errors0，两条阶段检查通过，不重建未变Player/GI。详见[阶段证据](../Ho-RTDI-LargeSpecularStages.md)。

## 48. 快速镜面置信度与历史审计

**动手前评审：** 第47步空间阶段持续减噪但rapid仍失败；核对用户ConfidencePass sensitivity/darkness/短时滤波与本地confidence/Temporal限制，直接读实际低分辨率线性confidence、末Atrous公共history及Temporal立即复制的重投影confidence，不扩张运行时接口或误用transient终态。

**结果与拒绝：** 两条公共history31/重投影1，配置specFrames30；rapid平均confidence=.852241、当帧变化点.339311，step变化点.101288。跳过动态visibility confidence使rapid frameL1 .507913→.251653，但step变化像素最初三帧L1 .244762→.605561，超过.35，拒绝为生产修复。报告每阶段step changedL1并纳入质量标志，diagnosticOnly退出0不替代质量；默认仍失败。

**下一项：** 先评估非零F0彩灯原始提案方差，以隔离shader而非新增产品参数做同种子对照；旧全表面RGB失败保持，只有现有大尺寸/step/core/primary/Player全过才考虑接纳。仅Editor/文档，正式报告分开。详见[置信度证据](../Ho-RTDI-SpecularConfidenceAudit.md)。

## 49. 隔离非零F0 RGB提案

**动手前评审：** 第48步关闭confidence导致step失败，保留动态可见性、改查输入。核对用户RAB目标契约及固定Reservoir归一化。只生成隔离compute/bridge，将零F0限定扩为RGB全表面；生产spec proposal4/物理Shade/NRD不变，报告明确rgb-all且不覆盖默认，入口拒绝非隔离工程/变化源码。

**结果与拒绝：** 两条仅2/6，rapid=.470918/.499755仍超.35；step整体虽改善，变化像素=.459106/.445930失败。256阶段rapid raw MSE约降51%、发布约降34%，但step raw短期红误差.231293→.452006，发布.244762→.459106，不能全部说成历史残影。阶段confidence/公共history读取有效；拒绝推广非零F0 RGB，默认正式回归原3/6及数值保持。

**下一项：** 核对单reservoir/单最终样本的通道方差、Cutout额外样本可见性与成本，再选择DI采样改动，不继续无依据调NRD或增加初始数来掩盖最终样本限制。Roslyn/隔离Shader/两条正式失败与RG阶段检查通过，只过滤Editor/文档，不重建未变Player/GI。详见[提案证据](../Ho-RTDI-SpecularRgbProposalAudit.md)。

## 50. 独立第二最终样本隔离原型

**动手前评审：** 单最终样本RGB提案权衡不能只调NRD。核对用户两份ShadeSamples单reservoir输出、SDK Stream/Finalize及块地址；inline跳过non-opaque，因此辅助样本必须走native any-hit。空间完成后复用slot2，既有R8临时编码两visibility位，原ReSTIR与独立均匀解析灯估计作平均；不增buffer/纹理。旧平均亮度不能与单primary重追比较，gradient改从旧primary缓存恢复对应物理亮度。

**结果：** 两条256/512镜面与漫反射各六组、64多灯/core通过。镜面rapid=.343677/.344150近.35，step变化像素=.268236/.256798；core四组最大.00708.. .00781<2%，raw红0。GPU slot2/primary采样与CPU alpha缓存核对各2048次通过，辅助选灯随帧改变，默认正式回归仍原3/6与数值。所有输入/Shader副本仅隔离工程，生产不变，Roslyn/Shader/GUID/diff检查通过。

**限制与下一项：** 原型只接native Cutout visibility且解析灯，无Cutout控制仍单样本；天空MIS/多灯分布/聚光灯/HDR/实际部署和额外射线成本未证明。先扩大灯域，处理均匀辅助退化与环境归属后再考虑产品接入，不以资源不增声称GPU免费。详见[原型证据](../Ho-RTDI-PairedFinalSamplePrototype.md)。

## 51. 第二最终样本的灯域退化

**动手前评审：** 两点灯不能代表古堡多灯；核对局部PDF/功率cone shaping，原baseline与paired分开评分spotRapid/spotStep/32灯强弱/hdrRapid。256/3584点与192+256固定时钟，独立CPU GGX/cone/alpha；Far/弱灯在完整blocker左侧且骨骼非负移动，Near预计算交点并逐帧抽查原参考。

**结果：** 两条baseline0/4、paired2/4。paired spotRapid .360202失败、spotStep .226086/changed.264976通过、HDR .343575且scale1/256通过。32灯主采样仍Power，均匀辅助raw normalized MSE 1.289396→7.764652，filtered红/蓝.819039/.875879失败，拒绝生产接入；不放宽门槛或用平均raw正确替代噪声。

**下一项：** 辅助正功率/CDF提案需对应实际灯索引和区间PDF，保留RGB短窗口、alpha/灯启停变化门槛。环境/opaque/Player/成本仍待处理。仅Editor/文档，原两点灯六组回归、Roslyn/Shader/GUID/diff通过。详见[灯域证据](../Ho-RTDI-PairedLightDomainAudit.md)。

## 52. 辅助RGB功率CDF隔离原型

**动手前评审：** 均匀辅助在弱灯群产生大权重，主Power不是问题。依据点/spot shaping构建maxRGB强度CDF，按实际灯索引快照排序、FP32真实区间PDF归一化；不改原ReSTIR/平均系数/物理Shade/alpha/gradient。只增加隔离structured float buffer，2/32灯8/128 bytes，GPU读完释放。

**结果：** 两条3/4，32灯filtered红/蓝.983309/.991945、frameL1=.344326、raw normalized MSE7.764652→.577339恢复；spotRapid仍.360202失败。每配置64次真实GPU index/inversePDF检查及逐帧索引guard通过，两灯CDF等价Uniform数值保持。严格区间坍缩拒绝，未证明动态逐帧CDF/多相机或GPU预算。

**下一项：** 在CDF基础上评估更多独立辅助估计的噪声/射线收益，不通过样本依赖加权或未知ReSTIR实际概率的MIS引入偏差。环境/opaque/Player/成本仍待完成；仅Editor/文档，Uniform回归/Roslyn/Shader/GUID/diff通过。详见[CDF证据](../Ho-RTDI-PairedPowerCDFPrototype.md)。

## 53. 第二独立辅助估计与实际代价

**动手前评审：** 两样本CDF spotRapid仍.360，试固定`.5主+.25辅助1+.25辅助2`而非样本依赖加权。沿SDK packing/归一化与真实CDF PDF，第二抽样单独native any-hit并用24-byte natural buffer保存，三visibility位暂存R8，主history/gradient/NRD/距离guide保持。

**结果：** 两条4/4，spotRapid=.324357、step变化像素=.203049、多灯/HDR≈.308。每配置128次GPU index/PDF与两辅助/主缓存CPU alpha核对通过；64对抽样中相同30/27，非重复覆盖有效。另独立GPU计数审计：每辅助256帧16,777,216条，即65,536/帧，主spot略少；计数不包括gradient/inline，也不是GPU时间。

**限制与下一项：** 第二辅助256²增1.5MiB，1080估47.46MiB尚未测试；相对原单样本增两条最终射线。环境/opaque/动态CDF/多相机/Player/GPU预算仍未完成，不能称生产可用。两条正式/计数/旧CDF回归、Roslyn/Shader/GUID/diff通过，仅Editor/文档。详见[质量与代价](../Ho-RTDI-SecondAuxiliaryPrototype.md)。

## 54. 辅助样本与环境贡献归属

**动手前评审：** 辅助只采解析灯，直接平均会减半天空；核对用户ShadeSamples/SDK环境MIS和当前native，已正确绑定Ho-Atmosphere，不改输入设施。隔离修正天空主样本权重1、解析主.5、两辅助各.25；只辅助生成要求解析数>0，避免sky-only不追主可见性。

**结果：** 128/512观察独立常量绿色天空GGX积分+红蓝解析参考，透明Cutout强制native路径。Naive0/4，sky-only0/mixed绿约.5；修正两条2/4，raw绿约1，mixed镜面filtered红1.050745或蓝1.050032仍失败。原baseline mixed还存在约-.2负输出，完整记录并拒绝，不 clamp。原解析四组回归4/4，生产不变。

**下一项：** 捕获NRD阶段定位mixed能量/负值后再修，不放宽5%或称整个环境支持已交付。opaque/动态CDF/多相机/Player/GPU时间待完成；Roslyn/Shader/GUID/diff通过，仅Editor/文档。详见[环境对照](../Ho-RTDI-AuxiliaryEnvironmentPartition.md)。

## 55. 混合环境NRD阶段定位

**动手前评审：** 第54步raw正确但filtered失败，按既有4/6/9/10双lobe接口立即复制，固定128/512观察与独立参考；diffuse恢复scale、uniform ortho spec恢复材质/scale，并全点检查末轮/发布一致，alpha不当方差。

**结果：** baseline diffuse Temporal非负，Clamping min≈-1.099/-1.003，最终≈-.200/-.194；原型T/S Clamping仍2次负值。镜面原型首Atrous红/蓝约.992/.982或.989/.985，后续转1.051/1.049或1.044/1.050，明确两项问题。partition两条24记录完全一致、诊断0；正式54回归仍2/4与数值，无生产更改。

**下一项：** 先修Clamping物理RGB非负/moment一致，保持派生可重建与upstream SHA及旧core/彩灯/动态/环境门槛，再处理镜面后续滤波。仅Editor/文档，Roslyn/Shader/GUID/diff通过。详见[阶段证据](../Ho-RTDI-MixedEnvironmentStages.md)。

## 56. 漫反射历史的非负物理域

**动手前评审：** 55已定位Clamping负RGB，不在发布端掩盖。仅DI派生RADIANCE在colour box/acceleration/reset后、moment更新前约束normal/fast diffuse为非负，moment随实际颜色修正。原源码/版权/SHA、specular/SH、参数/资源/native不变，生成器唯一边界guard可重建。

**结果：** baseline mixed Clamping/发布负值全消失，原型TS的2次负值也消失；平均diffuse能量微变仍5%内，core最大四组.0104.. .0124保持。大尺寸动态diffuse、64动态镜面、两条primary、两条16彩灯回归通过；混合镜面偏差原样保留，非本步已解决。

**部署与下一项：** 两个release errors0，实际D3D12两条与D3D11 inactive均退出0，旧环境/HDR/边界/NRD/多灯/Cutout门槛通过；Roslyn/派生SHA/DLL/hash/diff通过。过滤DI Shader/生成器/文档，无GI引用该NRD body。下一项镜面后续Atrous；三样本仍隔离，opaque/动态CDF/Player集成/GPU预算待完成。详见[修复](../Ho-RTDI-NonnegativeDiffuseHistory.md)。

## 57. 镜面后续固定交换隔离对照

**动手前评审：** 55末Atrous偏差已定位；核对vendor/fork逐像素sumW归一化与既有diffuse固定系数，仅spec输出采用拒绝质量留中心/variance系数平方，邻域/带宽/参数/F0保持。实际index10 main0作用域替换并恢复，不声称随机偏移/几何项非对称下严格守恒。

**结果：** 原型环境两条4/4，mixed spec红/蓝1.0507/1.0488→1.0255/1.0173、TS1.0444/1.0500→1.0228/1.0205；原单样本同场景也4/4。阶段9不变，10改善，物理scale/发布一致；恢复原入口仍2/4，未污染。生产不变，Roslyn/Shader/GUID/diff通过。

**下一项：** 可重建DI派生候选后广验动态/primary/曲面/边界/窄环境/小太阳/HDR/core/彩灯/Player，噪声可能变差，不能只凭能量通过发布。三样本/opaque/动态CDF/GPU预算仍待完成。详见[对照](../Ho-RTDI-LateSpecularExchangeAudit.md)。

## 58. 全局镜面固定交换候选拒绝

**动手前评审：** 57能量通过不能代表噪声，在可重建派生临时RADIANCE候选下依次核对动态/primary/曲面/边界/rough环境/斜视/小太阳/三样本。原SHA/参数/可见性保持，不改.35。

**结果与撤回：** 64动态rapid .343487→.365093、step .347752→.380824失败，拒绝；其他列出的RG入口通过，三样本spotRapid余量也从.324→.348变小。运行时/生成器恢复原版，Editor motion两条作用域复现相同false/exit1；恢复生产64镜面通过、原环境2/4失败保持，不做已失败候选的无关Player接纳构建。

**下一项：** 读实际后续邻居质量/normalization/confidence的稳定/动态差异后选择策略，不盲调比率压线。Roslyn/原SHA/派生与diff检查通过，最终只Editor/文档。详见[拒绝证据](../Ho-RTDI-LateSpecularExchangeRejection.md)。

## 59. 稳定/动态真实滤波权重分布

**动手前评审：** 全局fixed失败，不盲调。隔离原row normalization pipeline10以8KiB联合hist读实际sumW/confidence，四个step严格核ROI计数；均值按bin中点近似，非性能计时或严格守恒证明。

**结果：** 稳定mixed meanW step2≈.708到16≈.355/.388；动态rapid≈.408到.202，末轮97.81% W<.25接近中心.195，step96.83%。低接纳区原归一化仍放大少量邻居实现降噪，解释fixed退化。动态两条计数/统计完全一致且原.343/.348通过；环境仍2/4失败，不改输出。

**下一项：** 按邻居接纳量评估连续系数保留低接纳降噪，variance随最终权重平方；不按环境开关或bin均值硬分类，不压线。仅Editor/文档，Roslyn/Shader/GUID/diff通过，Runtime/Player不变。详见[分布](../Ho-RTDI-SpecularWeightAudit.md)。

## 60. 连续邻居接纳量策略拒绝

**动手前评审：** 用59真实分布定义软exchange=a²×confidence，邻居scale从1/sumW到1，拒绝质量留中心，variance随最终系数平方；不按环境开关分类，明确实验曲线非最优。

**结果：** 两条动态3/3、rapid/step .343609/.347919保持；环境仍2/4，mixed spec红1.051969、TS蓝1.051402失败，部分比原版稍差，拒绝。生产未改，恢复原入口数值保持，Roslyn/Shader/diff通过；不继续盲调指数或已失败候选Player构建。

**下一项：** 核对normal/view权重与正交frustum契约：位置helper区分ortho但view按位置归一化，先对照再判断，不假设为全部根因。保留DI范围与完整门槛。详见[拒绝](../Ho-RTDI-AdaptiveSpecularExchangeRejection.md)。

## 61. 正交镜面view契约对照

**动手前评审：** fixed NRD位置重建区分ortho，但view按位置归一化；核对用户Relax.cpp frustum常量，以并行-gFrustumForward替换ortho分支，透视表达/归一化/参数不变。分别9+10和仅10作用域副本，恢复原shader。

**结果：** 两条后续-only及两条9+10环境均2/4，mixed spec红/蓝约1.057/1.057、TS1.050/1.058，比原更偏；raw/diffuse保持，拒绝当前能量修复。后续-only动态两条3/3、9+10动态RG3/3，未据step改善称整体更优。恢复原环境2/4、动态3/3与数值保持。

**下一项：** 核对透视/正交偏差覆盖及noise-dependent range/random offset具体项，不继续按代码直觉改表达或盲调曲线。只Editor/文档，Roslyn/Shader/diff通过，无生产/Player变化。详见[对照](../Ho-RTDI-OrthographicSpecularAudit.md)。

## 62. 透视混合环境覆盖

**动手前评审：** 用户实际场景透视，orthographic差异并非能量修复。128/FOV40沿同一输入，逐像素V/distance/GGX与随NdotV的double天空积分，缓存1e-6精度，不套正交常量或改Shader。

**结果：** 单样本/三样本两条各2/4，raw spec均5%内，mixed filtered蓝约1.155～1.188/1.161～1.182，透视更明显；diffuse/非负保持，原正交回归原值。只是中心ROI，不能据此断言全图同幅增能。

**下一项：** 全有效平面与嵌套ROI积分/梯度，区分空间搬移和权重问题后再选过滤/guide改动。仅Editor/文档，Roslyn/Shader/diff通过，生产/Player不变。详见[透视证据](../Ho-RTDI-PerspectiveMixedEnvironment.md)。

## 63. 中心偏差与有效平面空间积分

**动手前评审：** 透视中心偏差不等于全图增能。独立per-pixel参考与真实alpha关联覆盖13456有效点，嵌套64/32/16及outer/annulus严格分解，raw/published/reference/excess同时记录。

**结果：** 三样本两条12记录一致、诊断0；all filtered红/蓝≈.98/.98，outer≈.975/.96，inner32≈1.08/1.16～1.18；原单样本也同样趋势。中心与外围存在空间搬移，整面仅1～3%亏损，不能继续按中心比值描述同幅全图增能。原中心正式回归仍2/4失败，门槛未删。

**下一项：** 保持局部高光/梯度而非强行全图校能，核对spec lobe/view/range与空间支撑，兼顾噪声边缘。仅Editor/文档，Roslyn/GUID/Shader/diff通过。详见[空间证据](../Ho-RTDI-SpatialSpecularAudit.md)。

## 64. 镜面RGB range距离拒绝

**动手前评审：** diff已有L(abs RGB)避免抵消，spec仍abs亮度差。核对vendor/fork，在隔离index10仅替换距离表达，其余带宽/归一化/参数/采样保持。

**结果：** 两条透视中心蓝1.161/1.182→1.065/1.079改善但仍5%外，动态rapid .343487→.382012、step .347752→.401667失败，拒绝。原恢复透视2/4、动态3/3保持；Runtime/Player不变，Roslyn/Shader/diff通过。

**下一项：** 同时核对颜色镜面噪声/moment与高光梯度，不只收紧range或改normalize。只Editor/文档，原门槛/DI范围保持。详见[拒绝](../Ho-RTDI-SpecularRgbDistanceRejection.md)。

## 65. 混合镜面实际方差与moment语义

**动手前评审：** RGB range失败，先核对真实统计不增资源。128/1024点逐像素512时间RGB/luma方差，避免空间梯度混入；4/6 alpha为raw moment spread，9为scalar variance，10 history不当variance。

**结果：** mixed原型Temporal luma variance≈1.48～1.63e-5、alpha spread≈1.02～1.07e-3，首Atrous scalar≈2.15～2.26e-5 vs实际luma≈3.62～4.29e-6；不支持在此认定luma遗漏颜色波动。原单样本类似。两条24记录一致，原stage/RGB比例保持、诊断0，正式失败不变。

**下一项：** 核对first range与variance propagation对raw moment的样本换算一致性，再过形状/动态/core/Player等门槛，不先加RGB covariance或调phi。仅Editor/文档，Roslyn/Shader/diff通过。详见[统计](../Ho-RTDI-MixedSpecularMomentAudit.md)。

## 66. 镜面normal预算方差对照拒绝

**动手前评审：** 65的统计差异不是直接实现bug。检查sampleSpecularVar已经/varianceSamples，range与w²传播共用；不重复除预算。仅隔离first9将fast上限替成normal，保持moment/采样/参数/归一化。

**结果：** 两条透视mixed蓝1.161/1.182→1.090/1.102有所减少但仍失败；动态rapid .343487→.384149、step .347752→.398122失败，拒绝normal全局预算。原透视2/4、动态3/3及数值恢复，Runtime/Player不变，Roslyn/Shader/diff通过。

**下一项：** 核对实际Clamping/fast colour变化及不确定度影响或实际采样/guide接入收益，不继续固定预算试值。完整DI目标/原门槛保持。详见[核对与拒绝](../Ho-RTDI-SpecularNormalBudgetRejection.md)。

## 67. 稳定mixed真实Clamping改色

**动手前评审：** 预算静态/动态表现不同，直接拷贝4/6 normal和各自fast输出。按各pass resource descriptor找pool并立即copy，修正初次permanent假设被guard拒绝；不读fast alpha当moment。512×1024观测与同CPU参考，阈值仅隔FP16末位。

**结果：** 三样本none/TS normal改色13/16个观测，比例.000025/.000031，平均变化约3.50e-7/2.75e-7；normal-fast距离约.207/.214明显但clamp改变罕见。两条一致，原stage/最终比例保持，不能推广至动态caster或精确独立预算。

**下一项：** 动态读取同样变化与实际采样接入/opaque/动态CDF/多相机未完项并行评审，避免项目变成固定NRD系数试值。只Editor/文档，Roslyn/Shader/GUID/diff通过，生产不变。详见[改色](../Ho-RTDI-ClampingColourAudit.md)。

## 68. 无 Cutout 辅助样本执行

**动手前评审：** 两辅助原型在 native pass 生成，opaque 不调度该 pass，不能从 Cutout 对照推定可用。对照 fork ShadeSamples/SDK visibility 和现有 inline bridge，只在隔离 opaque ShadeNRD 生成/缓存两次真实 CDF 样本；不引入透明标记物。

**结果：** 两条四组全部通过，GPU inverse PDF/cache/CPU遮挡匹配；rapid/step/32灯/HDR frame L1=.062609/.099089/.062986/.059072，step changed=.075235。opaque rapid每帧reset、step15次，不是保留历史；native counters未启用，零值不是零成本。原Cutout四组回归数值保持。首次GUID导入失败已修复后完整重跑。

**下一项：** 动态灯集/CDF帧同步与资源生命周期，之后多相机/Player/成本；混合环境镜面正式失败仍保留。只Editor/文档，生产/GI/native不变。详见[opaque审计](../Ho-RTDI-OpaqueAuxiliaryAudit.md)。

## 69. 动态灯集/CDF与实际样本分解

**动手前评审：** 静态CDF不可直接迁入产品；fixture同步更新CDF后核对实际collector、GPU双向identity map/辅助PDF与逐点primary+auxiliary重构。不引入重复生产collector。opaque零灯读旧scratch已修复。

**结果：** 两路径short Cutout6/8、opaque5/8正式失败；映射/PDF通过、raw重构误差最大约.000974。100:1红raw1.149来自primary1.264/aux1.036，2048帧收敛1.011；不能据此删除短窗口失败。关红raw0但filtered峰值.00157/.00269超门槛，零灯/恢复正确，长窗口仍有残留。

**下一项：** 灯集增删/类型变更的NRD历史失效策略，先隔离证据再生产单样本/动态/Player回归；保留普通移动/功率confidence及原环境失败。仅Editor/文档，Runtime/GI/native不变。详见[动态CDF审计](../Ho-RTDI-DynamicAuxiliaryCDFAudit.md)。

## 70. 灯集变化的NRD历史失效

**动手前评审：** GPU raw已无关灯通道，filtered残留；生产单最终样本也复现。隔离只失效NRD，再按实际snapshot identity/type接入，保留ReSTIR历史与普通功率/运动confidence。核对fork identity与NRD CLEAR_AND_RESTART契约。

**结果：** Cutout/opaque关红峰值.001019/.002186→.000582/.000586，关蓝亦在.001内；production与候选数据差0，GPU映射/raw分解及首帧reset检查通过。同数量替换灯检查通过。动态灯域整体仍有原有3组失败，不宣称全通过。

**回归：** 两条16组reuse/完整primary、两条256/512 diffuse六组、两条64 spec三组通过；64 diffuse step .352322旧失败用原HEAD复跑指标完全相同。两release构建0、实际D3D12两Player及D3D11不支持分支均pass/0；Roslyn/vendor派生/nativeSHA/diff通过。外部SolarHD及lil关键词警告保留。

**下一项：** 每相机DI状态/资源隔离，再推进采样原型接入；保留100:1短窗、混合镜面及64diffuse失败。灯集启停clear/短时噪声与GPU预算待实际评估，未改GI/最终材质合成。详见[历史失效](../Ho-RTDI-LightDomainHistoryReset.md)。

## 71. 交替多相机实际隔离与保留raw失败

**动手前评审：** 每相机Dictionary不足以证明资源/输入正确。沿fork NRD矩阵/分辨率/frameIndex契约，交替64²点灯与96×80方向灯，各两灯保证NRD真实调度；复用隔离Player下游capture。

**结果：** 四入口实际280render/80GPU隔离/87局部reset/skip/dispose检查通过，absent colour0、两路径一致。改变A类型/尺寸、跳过恢复A、销毁B不修改另一相机reservoir/历史。完整物理quality仍失败：raw最大1.399、filtered .810，首次点灯raw已有约5.2%差异，不能宣称多相机完整可用。

**下一项：** 同fixture关闭ReSTIR reuse并读primary/packed估计，先拆分采样、输入坐标/尺寸与CPU参考，不猜测改GB/SB或系数。只有Editor/文档，Roslyn/Shader/GUID/diff通过、生产/Player不变。详见[隔离与失败](../Ho-RTDI-MultiCameraAudit.md)。

## 72. 多相机reuse控制与判据复查

**动手前评审：** 先同fixture关闭reuse，分别开temporal/spatial；不把瞬时raw大偏差直接归给输入。核对fork/SDK选中来源pi与各来源target*M的MIS归一化。

**结果：** no reuse四入口全部0，raw/filtered最大.000651/.004506。各复用模式仍按旧瞬时门槛失败，但阶段raw积分约.9955～1.0096、filtered .9868～1.0033；实际weight2.71/3.69/4.80符合非恒定MIS来源权重，不能要求每次2。第71步随机单帧raw1%判据不适合采样期望验收，保留其诊断，未证实主表面或复用bug。

**下一项：** 逐像素长期均值与区域积分验收，分离期望、噪声、状态隔离及动态响应；保留其他真实质量失败。仅Editor/文档，无生产输入/算法改动。详见[复用控制](../Ho-RTDI-MultiCameraReuseControl.md)。

## 73. 多相机逐像素长期均值

**动手前评审：** 区域积分可能掩盖局部偏差；保留瞬时诊断，1024交替帧/点、128点独立参考均值门槛raw3%/filtered5%，16个64帧block只作统计诊断。旧瞬时入口/报告未删除，生产算法不改。

**结果：** Cutout/opaque两路径四入口均0，raw均值最大1.9428%，filtered1.3835%/1.1723%；no reuse对照.0651%/.3629%。逐点及动态阶段积分、2328render/80GPU隔离/2135reset/skip/dispose/颜色检查全部通过。瞬时raw1.399仍记录，不据其修改MIS或GB/SB。

**下一项：** 第二fresh解析辅助样本的24-byte sidecar消费字段/更小存储，保持真实PDF、可见性及输出一致性，不改SDK ABI或提前产品化。仅Editor/文档，Roslyn/Shader/GUID/diff通过，其余真实质量失败保持。详见[逐点验收](../Ho-RTDI-MultiCameraPointMeans.md)。

## 下一步取舍

| 项目 | 当前判断与推进条件 |
| --- | --- |
| 局部灯功率采样 | 可选路径与可重复基准已交付；后续按目标场景 GPU budget 决定缓存和 tile 规模，保留 Uniform 对照。不能用此结果代表任意灯光空间分布。 |
| Player 资源完整性 | DI/NRD/debug 的 Win64 Mono release 部署及旧 renderer 正常补引用/保存流程已交付。IL2CPP、未处理外部资产的构建迁移仍未证明；GB 泛用 fallback / MSAA 属于输入设施的另一个支持域，不借本步扩张。 |
| NRD 质量与预算 | 当前/历史normal codec已统一，真实GPU history与SB匹配，评分表示差异消除；仍需分别核对运动误差和原平面64帧失败。SB输入及每个DI/GI history各增加1080p约7.91 MiB，下一项转向实际场景/灯域支持，保留既有失败，旧历史预算设置不会自动迁移。 |
| ReGIR | 需要世界网格与更多采样资源；先证明较小的局部提案改进仍不足，再评估引入收益。 |
| 血月古堡几何支持 | 基础Cutout和动态最终可见性confidence已接入；第40步新core/彩灯与实际Player门槛通过，但core/Player蓝光余量小、噪声增加，继续扩大验证。合并天空/模型只作支持审计。GPU计时仍不足、动态额外射线成本待测，CPU pose28.54MiB/服务，不扩张PT。 |
| 环境灯 / GI路径归属 | DI HDR天空PDF/RIS及BRDF/VNDF MIS已验证常量窄镜面、斜视与特定小亮源；下一项实际场景与DI/GI直接天空路径归属。现有开关可显式选择一份直接天空，尚未补secondary环境NEE，动态/任意天空质量仍待验证。 |
| 最终材质合成 / PT 扩展 | 维持现有范围边界。 |

NRD 参考：[官方集成 README](https://github.com/NVIDIA-RTX/NRD)，以及用户参考工程 Assets/Scripts/NativePlugin/NRD 的 NrdDenoiserHelper / NrdFrameData。官方资料要求对应的辐射、命中距离与表面/运动 guides；本地参考还涉及 NRI/native ABI。后续应以可验证的输入契约和实际接入为单位推进，不能通过搬入整套原生框架或对 raw 输出简单模糊就宣称 NRD 已接好。

本记录随每步评审更新。后续步骤若改变以上优先级，应记录发现、依据和替代选择。
