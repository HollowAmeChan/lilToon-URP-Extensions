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

## 11. 下一步取舍

| 项目 | 当前判断与推进条件 |
| --- | --- |
| 局部灯功率采样 | 可选路径与可重复基准已交付；后续按目标场景 GPU budget 决定缓存和 tile 规模，保留 Uniform 对照。不能用此结果代表任意灯光空间分布。 |
| Player 资源完整性 | DI/NRD/debug 的 Win64 Mono release 部署及旧 renderer 正常补引用/保存流程已交付。IL2CPP、未处理外部资产的构建迁移仍未证明；GB 泛用 fallback / MSAA 属于输入设施的另一个支持域，不借本步扩张。 |
| NRD 质量与预算 | 静态解析灯 confidence 改善响应，external output 复用减少 16 B/像素，仍有成本和滞后。下一步专项核对薄几何/材质边界、每 lobe hit-distance 代表性。TAA/动态几何与复杂场景性能仍需独立证明，保留 raw/filtered 对照。 |
| ReGIR | 需要世界网格与更多采样资源；先证明较小的局部提案改进仍不足，再评估引入收益。 |
| 最终材质合成 / PT 扩展 | 维持现有范围边界。 |

NRD 参考：[官方集成 README](https://github.com/NVIDIA-RTX/NRD)，以及用户参考工程 Assets/Scripts/NativePlugin/NRD 的 NrdDenoiserHelper / NrdFrameData。官方资料要求对应的辐射、命中距离与表面/运动 guides；本地参考还涉及 NRI/native ABI。后续应以可验证的输入契约和实际接入为单位推进，不能通过搬入整套原生框架或对 raw 输出简单模糊就宣称 NRD 已接好。

本记录随每步评审更新。后续步骤若改变以上优先级，应记录发现、依据和替代选择。
