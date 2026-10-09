# Ho-RTDI：NRD RELAX 初版

Feature 的 **NRD RELAX 降噪** 为可选项，默认关闭。开启后仍保留 raw diffuse/specular；公开的 diffuse/specular 输出改为经过 RELAX 并恢复材质因子的结果。默认不写 camera color，不修改 lilToon 最终接收、CS、ShadowCast 或 GI。

## 参考与实现选择

使用用户参考中的 NRD 4.17.3 CPU 库、RELAX 原始 shader、NRD 前端 helpers 与调度描述。原参考 Denoiser.dll 同时集成 NRI、DLSS、NIS、Unity descriptor-heap hook 和设备内存控制。本步只引入重命名后的 NRD 核心 CPU 库，由它选择 pass、常量和 pool；Unity CommandBuffer 执行对应的原始 ComputeShader，所有 GPU 资源归 Unity 管理。

本地 DLL 实测为 4.17.3，normal encoding=2、roughness encoding=1。15 个 pipeline 的完整 shaderIdentifier（含宏）与生成的 Unity wrappers 逐一对齐。常量缓冲、sampler 和资源声明通过 NRD 提供的 custom-engine 宏适配，算法正文未改。33 个 shader/MathLib/config 文件和 3 个 ABI header 的原 SHA-256 与正文已核对；许可、CPU DLL hash 与出处见 ThirdParty/NRD。

参考：[NRD 官方集成说明](https://github.com/NVIDIA-RTX/NRD)，本地 NRD.hlsli / NRDDescs.h / NRDSettings.h / InstanceDesc / DispatchDesc，以及用户项目的 NrdDenoiserHelper、NrdFrameData、NrdPass。线上资料用于核对原则，实现严格按本地固定版本，而非混用新版 ABI。

## 信号契约

| 信号 | 当前处理 |
| --- | --- |
| diffuse RGB | raw 仍为 Lambert × albedo × 所选灯贡献；NRD 直接接收乘 albedo 之前的 Lambert lighting，避免反除丢失零颜色通道 |
| specular RGB | raw 仍为参考 GGX 光照；用 NRD_MaterialFactors 的 specular 因子解调后，调用 RELAX_FrontEnd_PackRadianceAndHitDist |
| signal alpha | 选中灯的距离，point/spot 为实际距离，directional 为 FP16 有限最大值；有效暗面没有选中灯时也提供有效的远距离零信号，不把 receiver-validity 写进 hit distance |
| normal/roughness | 世界着色法线与参考 roughness，固定编码 2/1；RGBA16F 存储。material class=0 表示 active diffuse，3 表示全部 diffuseAlbedo 为零；不是逐对象/材质资产 ID |
| view Z | 正的线性眼空间深度；无效主表面写 1000000，超过 denoising range 500000 |
| motion | 静态世界的 world-space 零位移；不把相机运动混入 MV，相机重投影由实际前后 view/projection 矩阵提供 |
| filtered publication | diffuse 乘当前 albedo，specular 按同一 NRD helper 恢复；RGB 保持原 DI 接收含义，alpha 重新写 receiver-validity |

没有复制当前主表面 GBuffer；现有 GB/SB 被借用，新增的是 NRD 实际要求的信号编码。NRD shader 不自行读取/重渲染场景。帧间时间与历史预算现在使用相机实际提交的 unscaled sample time，详见后面的时间契约。

`HoRTDIRenderGraphResources.diffuse/specular` 与原兼容 globals 发布最终选择的 raw/filtered 结果；新增 rawDiffuse/rawSpecular 与 `_HoRTDIRawDiffuseTexture` / `_HoRTDIRawSpecularTexture` 始终保留 raw。Volume 的 RawDiffuse / RawSpecular 可作同帧对照，原 Receiver 枚举值保持不变。

## 调度、ABI 与历史

在已有 unsafe graphics-queue DI pass 内，ShadeNRD 写 raw 与 NRD inputs，随后执行核心库返回的 dispatch 列表，最后 ResolveNRD 恢复材质因子。pool、signal、output 和 constant buffers 都声明 RenderGraph 依赖，compatibility 使用同一调度函数。没有 Unity native graphics device、command-list 或 descriptor-heap 直接操作。

核心调用返回的指针只在本轮复制描述、资源与常量时读取，不跨下一次 GetComputeDispatches 保存裸指针。每个 dispatch 使用独立常量偏移，帧常量使用三份 64 KiB buffer，避免同轮不同 pass 共用覆盖的 CB 地址。原始 ResourceType 数值以固定头文件为准；该参考库的 GetResourceTypeString 字符串数组顺序已经落后于枚举，不能用它决定绑定。

分辨率变化、禁用/重启、DI 关键设置或世界几何 Revision 变化会重启 NRD 历史；灯参数变化交给 RELAX 的历史/antilag 处理。当前 dense-DI 预设关闭 diffuse/specular prepass：两条 lobe 都有有效距离，未使用概率 lobe split；空间 prepass 会抹平干净解析高光并把确定的可见性边界带亮。历史以秒定义，60 Hz 时 diffuse 主/快速为 3/1、specular 为 30/6；antilag acceleration=0.6、reset=1，其他主要参数保留原 SDK 默认值。使用 SDK temporal、history fix/clamp 与 A-trous，没有自写降噪算法或对每次灯变化整份清历史。数值与时间换算按本节测试取舍。

## 验证与边界

- Windows x64、Unity 6000.3.15f1、D3D12 RG / compatibility，Gamma / Linear 通过真实 GPU 验证；15 个 Unity kernel 编译、固定 DLL/ABI、原始 uniform/power DI 及 GI 回归分别验证。
- 双色点灯噪声场景中，raw 红通道 MSE 约 0.050401，filtered 约 0.000049；filtered 红/蓝均值约 0.22549/0.22642，数学参考约 0.22。
- 64² 镜面测试中，raw ROI 均值约 0.296814，默认 prepass 时约 0.222147；dense-DI 预设约 0.276015。该改进不等于无偏、无空间模糊或任意高光都保真。
- 已覆盖选中灯距离、正 viewZ、金属 diffuse、镜面信号、静态世界相机移动、透视相机、输入域拒绝、raw/filtered 调试、关闭释放与重建。无效表面最后仍输出 A=0。

Windows x64 Mono release Player 已独立验收：普通序列化 renderer 引用 DI compute 与 15 个 NRD compute，正常裁剪后实际执行 RG / compatibility，两条路径的单灯、世界遮挡、Power、独立镜面、raw/filtered、RELAX 关闭重建均通过。packed 核心 DLL SHA-256 与源 DLL 相同；D3D11 实际启动并保持 DI inactive。双色灯 Player raw MSE 约 0.050401，filtered 约 0.000066，红/蓝均值约 0.227；仍是 64² 受控场景，不能代表生产画质。验证入口与复现步骤见 [Player probe](../Tests~/Player/RTDI/README.md)。

DI 的 debug / grid-label shader 现由 Feature 序列化持有，正常裁剪后 Raw / Filtered / Receiver / All 网格在独立 EXE 的两条路径均通过。旧 renderer 的缺失引用会在 Create 或打开 Feature Inspector 时补齐并 SetDirty；请保存 renderer asset 后构建。补齐只填空位，NRD 固定库扩容保留已有 override；再次检查完整引用不会弄脏资产。空库/部分库已在三个独立 Editor 进程中验证保存与磁盘重载，没有借 AssetDatabase 的内存状态冒充部署持久化。

当前是 **静态不透明 Mono 的 Editor / Windows Player 初版**。不支持/未验证 IL2CPP、skinned 或运动几何、XR、TAA jitter 的显式 NRD 契约、相机切换/时间跳变的大规模场景、完整材质 ID 边界路由和生产 GPU budget。GB 通用 fallback / MSAA resolve 不在本次真实 lilToon、MSAA=1 接收测试内。没有添加构建时全工程批量迁移；未打开/保存的外部旧 renderer 仍应先完成正常补引用流程。DLL importer 限定 Windows x64。

当前六张 NRD 信号/输出纹理合计约 44 B/像素；材质因子在 external output 上原地恢复，不再另分配两张 filtered 纹理。RELAX pool 约 80 B/像素，另有三个 64 KiB 常量 buffer 和后文的 confidence 资源。1080p 实测 DI＋NRD 活动载荷约 463.996 MiB，不含输入、AS、驱动/对齐与缓存。NRD Record 的稳定热路径已复用描述、资源表与常量 CPU 拷贝区，见下节；Feature 的扫描、Prepare、外部绑定与渲染图等仍可能分配，不能把局部零分配写成整帧零分配。

## 调度分配与分辨率预算

NRD Record 缓存固定 denoiser ID、kernel、dispatch 与资源表，直接将本轮原生常量复制到持久 int-word 区，取消每轮 byte[65536] / uint[16384] 的两份临时载荷与整区 BlockCopy。每次仍清零有效区的 padding，复制 native 描述和常量；保留每 pass 的独立 CB 偏移与三帧 GPU buffers。只上传本轮使用的 stride 区段，正常 10 个 dispatch 时约 7.5 KiB。没有跨下一次 GetComputeDispatches 解引用旧 native pointers。

在实际 Unity 6000.3.15f1 D3D12 Editor、RTX 4060、16 轮预热后记录 128 轮 SDK 调度：稳定轮 GC.Alloc 事件由 15 次降为 0 次，命令记录 CPU 平均约 54.5 μs → 11.0 μs。零分配指这一已预热 Record 段；首次创建、其他 Feature 阶段与 native 内存不在计数内。GC.GetAllocatedBytesForCurrentThread 的零值未通过可信测量审查，GC.Alloc marker 的 Value 是耗时而非字节；最终使用官方 Count 事件数，并先通过已知分配自检。移除的 128 KiB 临时常量载荷是数组大小的确定值，不伪装成字节 profiler 读数。

GPU 读回验证了延后执行的两次 partial SetBufferData 各自保存正确的 CPU 数据快照。NRD 的两条路径、噪声/镜面/相机移动/关闭重建与 release Player 回归通过，输出统计保持一致。

以下为步骤 6 的历史测量，尚未加入 confidence 或合并 external output；当前载荷见文末。

| 简单测试尺寸 | DI＋NRD 活动资源载荷 | RG 同步批次均摊 | compatibility 同步批次均摊 |
| --- | --- | --- | --- |
| 1280×720 | 214.646 MiB | 6.143 ms | 5.387 ms |
| 1920×1080 | 483.772 MiB | 10.770 ms | 11.333 ms |

配置为一张真实 lilToon quad、一盏点灯、local candidate=1、无 ReSTIR 时空复用、开启 RELAX。每尺寸预热 12 次，再计 8 次实际完整渲染请求与最终单像素 GPU 读回；按实际 texture 尺寸/格式及 buffer count/stride 核算活动资源。尺寸变化、真实 raw/filtered 能量、alpha 与 dispatch 均验证。载荷不包含 GB/SB、AS、相机目标、驱动对齐或 RTHandle cache；时间包含 CPU、输入生产、DI、NRD、raster 与 GPU 同步，不能当成 NRD 单独 GPU 时间或目标生产场景帧率。

复现入口：`HoRTDINrdRecordingValidation.RunBaseline` / `Run` 与 `HoRTDIValidation.RunBudget` / `RunBudgetCompatibility`，均在隔离工程执行。旧算法 baseline 必须使用优化前的源版本，否则 RunBaseline 只是记录当前版本，不能伪造前后对照。原始报告保留在隔离工程 rtdi-nrd-recording-baseline.json / optimized.json 与 budget 两条日志。参考契约为固定 NRD.h 的返回数据生命周期、[NVIDIA 集成说明](https://github.com/NVIDIA-RTX/NRD)、[Unity partial SetBufferData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetBufferData.html) 与 [GC.Alloc 事件统计示例](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)。

NRD 本身允许滤波偏差，材质因子下限、时空积累与空间滤波仍可影响细节/暗材质。应先用 Raw / Filtered 和真实目标场景对照，不把启用开关当作最终画质验收。

## 动态灯与阴影边界复查

本轮先保留原预设建立 baseline，再分别比较取消 diffuse prepass、12/2、6/1、3/1 的 diffuse 历史与 SDK antilag。原引用项目也使用 30 px diffuse prepass、30/6 历史与半量 antilag reset；[官方说明](https://github.com/NVIDIA-RTX/NRD) 提醒较干净信号可能被 prepass 模糊，固定 NRDSettings.h 提供 fast history / antilag 的含义。当前为两个都有效的 dense lobe，取消 prepass 不等同于概率 lobe split 的集成方案；后者仍需要重建与预滤波。

| 受控场景 | 原预设 | 当前预设 |
| --- | --- | --- |
| 256² 定向光阴影，距边界至少 8 px 的阴影内平均误亮 | 0.012850 | F6 精度 0.000000 |
| 同区域最大误亮 | 0.090759 | F6 精度 0.000000 |
| 确定照亮区域平均误差 | 0.011347 | 0.000333 |
| 太阳移动后新阴影区首轮残光 | 0.111626 | 0.001938 |
| 全灯关闭，第 4 次渲染残光 | 0.031174 | 0.000002 |
| 64² 移动点灯中心平均相对误差 | 22.79% | 18.71% |
| 双色灯 filtered MSE（raw=0.050401） | 0.000049 | 0.000117 |

动态点灯/变色/关灯使用 ReSTIR temporal + spatial、8 个局部候选；阴影边界单独关闭 ReSTIR reuse，让 raw 有严格的数学可见性真值。遮挡物在 camera culling mask 外、在 RT 世界内，接收平面的材质/法线/深度连续。48 轮预热后检查 y=64..191、x=104..215 的内部阴影与照亮侧；新增方向光角度后再检查真正从亮转暗的区域。质量门槛分别检查阴影均值/最大值、亮侧误差、旧颜色/关灯残光及移动误差；RG / compatibility 通过。对照图保留在隔离工程 nrd-shadow-quality-baseline.png / rg.png / compat.png，左为 raw、右为 filtered，以 1/π 归一并作显示 gamma。

阴影和响应明显改善，代价是静态双色噪声 MSE 高于长历史旧预设；当前仍约比 raw 低 430 倍，filtered 红/蓝均值约 0.2128（参考约 0.22），镜面 raw/filtered 约 0.296814/0.281728。Win64 release Player 两条路径的变色/关灯、原有能量/噪声/镜面/遮挡/Power/debug/lifecycle，以及 D3D11 inactive 均通过；Player filtered MSE 约 0.000085～0.000101。

**仍有的限制：** 上述固定 60 Hz 预算的移动点灯末轮 ROI raw=0.813635、数学参考=0.809308，而 filtered=0.673002；静止再积累 40 轮后 filtered=0.821692。因此连续变化仍有实质滞后，不能宣称解决动态灯保真。变色第一轮也保留旧成分；短历史是响应/噪声折中，复杂材质/灯分布、specular 动态响应与 TAA 仍需验证。概率采样的每 lobe hit-distance 代表性也须单独核对，不能从这一组解析灯直接推广到任意 ReSTIR/路径追踪信号。

## 相机时间与历史秒数

固定 NRDSettings.h 的 GetMaxAccumulatedFrameNum 明确推荐以积累秒数换算帧数。当前相机 pass 记录 `Time.unscaledTimeAsDouble`，在实际执行 NRD 时推进本相机时间状态，并把时间差乘 1000 传入 CommonSettings.timeDeltaBetweenFrames（该字段单位为 ms）。相同 Unity frame 内额外请求不推进 FPS 估计；first sample 使用 60 Hz seed。正时间差用 0.1 秒时间常数低通，按 `floor(period / filteredSeconds + 0.5)` 换算预算，更新后的整数预算有变化才调用 SetDenoiserSettings。

固定周期为 diffuse 主/fast 50/16.67 ms、specular 主/fast 500/100 ms、history fix 50 ms。主历史上限 255，fast 保持小于主预算，history fix 按固定版本上限 3 裁剪。时间回退、无效值或相机超过 1 秒无样本触发 NRD clear/restart，避免把旧画面接上；该 1 秒界限是本项目的暂停/恢复策略。时间缩放不改变此 unscaled 契约。额外采样仍能推进 sampler frame index；只是不把一帧中 100 次 SubmitRenderRequest 解释成真实 6000 FPS。

| 相同速度、0.4 秒点灯移动的受控时间线 | diffuse 主/fast | specular 主/fast | filtered ROI 平均相对误差 |
| --- | --- | --- | --- |
| 30 Hz | 2/1 | 15/3 | 15.44% |
| 60 Hz | 3/1 | 30/6 | 17.17% |
| 120 Hz | 6/2 | 60/12 | 14.22% |

时间线由 Editor-only 内部注入，使用真实 GB/SB、ReSTIR reuse、原生 NRD 和 GPU 读回；灯的速度/时长一致，不能把它称作三种真实 wall-clock Player 帧率 benchmark。30/60/120 的预算与输出、重复时间戳、时间回退、无效时间、两秒间隔后旧灯消失均验证；RG / compatibility 一致。现有阴影/变色/噪声/镜面/生命周期与 release Player 回归通过，Record 稳定轮仍 0 个 GC 事件。复现入口 HoRTDIValidation.RunTiming / RunTimingCompatibility。

该契约使不同帧率的响应时长接近，未消除约 14～17% 的连续移动误差；不与上一组中心单像素误差直接作同指标比较。低帧率整数化、高帧率 255 帧上限、整数预算边界的轻微摆动仍属于限制；没有做 frame generation、TAA 或 XR。后续置信度接入见下节。参考为用户 NrdDenoiserHelper / NrdSettings、固定 NRDSettings.h，以及 [NVIDIA FPS / history confidence 说明](https://github.com/NVIDIA-RTX/NRD)；没有混入新版本 ABI。

## 静态世界的 analytic-light history confidence

本步采用用户 fork 中 RTXDI FullSample 的 ComputeGradients / FilterGradientsPass / ConfidencePass 思路，而非用两张随机 raw 图直接求差。每个 3×3 stratum 选择当前/上轮最亮的 diffuse 或 specular 样本，保存其 reservoir 的 light ID / UV / inverse PDF，并用另一轮的真实灯快照重新评价同一代表样本。增删灯映射为 -1 时给全量变化；冻结灯光时随机样本变化不产生梯度。RGB / luminance 的 FP16 精度与存储信号匹配。

既有三份 reservoir 与 16 B 表面历史继续复用。ShadeNRD 先保存旧 raw 的 RG16F luminance，再输出当前 raw；旧 history reservoir / guides 在梯度完成前保留，由最后的 SaveHistory 写回。raw-only 路径保持原有 Shade 存储流程。表面位置通过静态世界的相机矩阵重新投影，法线/位置不匹配时不比较该对；这里没有 previous TLAS 或运动几何。

原始梯度为 abs(old-new) / max(old,new) 的分子与分母，经过四轮参考 3×3 A-trous triangular filter，再用 exp2(-12) darkness bias、sensitivity=8 转成两路 R8 confidence。没有增加 confidence temporal history；4.17 支持 lower-resolution confidence，由 NRD 线性采样。接受的静态 revision 内，若选中解析灯的全部参数相同，则同一表面的重算必然相同，直接得到零梯度，避免不必要 visibility ray 和旧 guide 精度误差。关闭/重启、resize、时间 cut 等仍通过原 NRD history reset。

| 同速时间线 | 仅时间预算的旧平均误差 | 加 confidence 后 |
| --- | --- | --- |
| 30 Hz | 15.44% | 9.25% |
| 60 Hz | 17.17% | 14.45% |
| 120 Hz | 14.22% | 12.76% |

RG / compatibility 的原质量门槛、相机运动、变色/关灯、阴影边界、Metal/specular、域拒绝、生命周期和时间线均通过。冻结的随机双色灯 confidence 全图 min / mean=1，证明没有把 RNG 变化误报为光照变化。该 Editor fixture 的 filtered MSE 约 0.000257、raw=0.050401，红/蓝均值约 0.213；镜面 raw/filtered 0.296814/0.292526。旧预设/时间测试的数值保留作历史记录，不把不同执行节奏的单次噪声数据当成严格独立消融。Win64 release Player 两条路径与 D3D11 inactive 通过，Player filtered MSE 约 0.000101，raw 输出保持一致。

新增资源为全分辨率 RG16F previous sampled luminance，以及 1/3 线性尺寸的两张 RGBA16F gradient 和两张 R8 confidence。1080p 活动载荷增加 11.865 MiB。步骤 9、尚未合并 external output 时，DI＋NRD 为 495.637 MiB；720p 为 219.921 MiB。该轮简单同步 batch RG 测为 6.863 / 13.755 ms（720p / 1080p）；相对先前约 11 ms 的 1080p 链路有实质开销，仍不代表 NRD GPU 单独时间或生产场景 FPS。增加的调度为 gradient、4 filter、confidence 与 full-resolution history store；只有 NRD 开启时分配/执行。

**限制：** 连续运动仍有滞后。Exact reprojection 取代参考的 temporal-selected pixel positions，只承诺静态世界；8-bit 旧材质 guide、薄几何、深度/法线判别与亮样本选择仍可能误判。参考 A-trous 不含新推荐的几何权重，跨材质/几何的 gradient 扩散尚需专项检查。固定三个 delta 灯型没有重新随机生成光点，reservoir ID / UV 就是同一个代表样本；环境/面灯或 probabilistic lobe split 接入时不能沿用这一结论。每 lobe hit-distance 的 BRDF 代表性仍未证明。来源与原 SHA 记录在 ThirdParty/confidence-provenance.json，NVIDIA 部分沿用原许可，未导入 native renderer / PT 框架。[官方 confidence 契约](https://github.com/NVIDIA-RTX/NRD)。

## External output 原地恢复与资源复用

固定 RELAX 4.17.3 的 PrePass 写 external radiance output，Temporal 随后读取这轮结果，最终 A-trous 再写最终输出。跨帧 history 位于 permanent pool；不能把“output 会被读取”直接理解为上一帧历史，也不能假定所有 NRD denoiser 都有这一生命周期。本轮对照原始 shader、用户参考中的 NRD/RTXPT 后端材质恢复以及实际 dispatch 描述，确认当前完整尺寸路径允许在最后 ResolveNRD 中原地恢复材质因子。

Resolve 通过同一个 RWTexture2D 逐像素读写，避免同轮把同一资源分别绑定为 SRV 与 UAV。公开 filtered diffuse/specular 直接引用这两张 owned output，raw 仍独立；释放时只释放拥有的六张 NRD 信号/输出纹理。RenderGraph 也只声明这一组资源。调度检查分别追踪 diffuse/specular 本轮的写入，若首次 SRV 读取早于写入则拒绝；这只是固定源码审计的补充，UAV 描述本身不能证明任意未来 shader 已完整覆盖所有像素。

| 简单测试尺寸 | 复用前载荷（已含 confidence） | 当前活动载荷 | 减少 | RG 同步批次均摊 |
| --- | --- | --- | --- | --- |
| 1280×720 | 219.921 MiB | 205.858 MiB | 14.063 MiB | 6.669 ms |
| 1920×1080 | 495.637 MiB | 463.996 MiB | 31.641 MiB | 14.183 ms |

减少量为两张 RGBA16F 的 16 B/像素，不包含输入/AS/driver/cache；计时沿用上述简单 fixture 和最终读回同步方式，本轮没有证明速度提升。RG / compatibility 的能量、噪声、镜面、相机移动、raw/filtered 调试、域拒绝和释放重建通过；目标尺寸 resize/reset、动态灯/阴影质量与 compatibility 30/60/120 Hz 时间线通过。兼容路径数值与前版相同：raw MSE=0.050401、filtered=0.000257，specular raw/filtered=0.296814/0.292526；RG 不同实际执行节奏下 filtered=0.000184。两个 Win64 Mono release 构建、真实 D3D12 两条 EXE 与 D3D11 inactive 通过，Player filtered MSE≈0.000085/0.000101；packed DLL hash 相同。无新增 DI/NRD shader 或 command-buffer 错误，已有 lilToon duplicate-keyword 构建警告与其他模块 C# 警告保留。

首次“external output 不可作为 SRV”检查被实际 Temporal 读取否定，随后改成并验证本轮先写后读；失败运行没有计为通过。此项没有改变 NRD 算法、滤波设置或接收含义。尚未证明的薄几何/材质边界、动态几何与每 lobe 距离代表性仍需独立检查。后端原始参考：[用户 fork 内 RTXPT 对应上游](https://github.com/NVIDIA-RTX/RTXPT/blob/main/Rtxpt/NRD/DenoiserNRD.hlsli)，运行行为仍以本地固定 NRD 版本为准。

## Diffuse 颜色通道与零 diffuse 材质边界

步骤 11 的真实 lilToon / 128² fixture 使用单个正面 directional delta 光与显式 Lambert 真值。2 像素红/蓝 point-sampled 纹理、近共面 2/4/8 像素金属条和有 0.35 深度差的 4 像素蓝色前景分别隔离颜色、材质与几何影响。确定性 NRD 门槛关闭 ReSTIR reuse，深度前景另启用 reuse；不能把带随机归一化的 raw 每个像素都强行要求等于解析值。

旧实现先把 albedo 乘入 raw，再除以带 0.02 下限的 NRD diffuse factor。零通道的 lighting 已经丢失，空间滤波会将其当作暗信号；恢复时的下限还会生成假颜色/金属 diffuse。用户工程 Assets/Shaders/RayTracing/ShadingHelpers.hlsl / DI/ShadeSamples.hlsl 保留 demodulatedDiffuse，再乘 albedo 得最终 DI。当前按这个严格可分离的 Lambert 分解直接生产 NRD diffuse signal，最后乘当前 albedo。raw 表达式、采样/visibility、confidence 的配对照明计算不变；specular 继续使用 NRD helper，因为它不能照搬 Lambert 分解。

只改 diffuse 分解已经恢复颜色/深度前景和金属 diffuse，但金属镜面仍被周围零 specular 信号严重压暗。固定 NRD 的 normal encoding 2 支持 2-bit 分类，原来统一 ID=0 且 minMaterial=4 禁用比较。现传 active-diffuse=0 / all-zero-diffuse=3，RELAX 两条 minMaterial 设为 0；Temporal、HistoryFix、A-trous 使用原始 CompareMaterials。0/3 对应存储 alpha 0/1，避免 1/3 或 2/3 经 FP16 传输后与历史 R8 ID 不精确相等。分类直接来自已有量化 albedo，包括全金属与全黑 diffuse；不是新的主表面副本、对象 ID 或完整资产路由。[官方材质解调与分类说明](https://github.com/NVIDIA-RTX/NRD)，具体实现按固定 NRD.hlsli / NRDSettings.h / RELAX shader 核对。

| 确定性边界指标 | 旧实现 | 仅 diffuse 分解 | 分解＋分类（最终 Editor） |
| --- | --- | --- | --- |
| 2px 红/蓝最大绝对误差 | 0.080811 | 0.000122 | 0.000122 |
| 2px 金属镜面平均相对误差 | 73.43% | 73.43% | 0.0071% |
| 4px 金属镜面平均相对误差 | 42.03% | 42.03% | 0.0407% |
| 8px 金属镜面平均相对误差 | 14.49% | 14.49% | 0.0524% |
| 电介质最大假镜面（4px case） | 0.011414 | 0.011414 | F6 精度下 0 |
| 4px 有深度差的蓝色前景最大误差（reuse） | 0.018433 | 0.000122 | 0.000122 |

最终两条 Editor 路径相同。关闭 reuse 的金属 diffuse 为零，邻近 active diffuse 平均相对误差 <0.023%；输入 class alpha=1 的实际 GPU 传输通过。单独启用 temporal＋spatial、48 次预热和 64 次观察后，邻近 diffuse raw/filtered 均值 0.158370/0.158098，解析值 0.159155；金属 specular 为 0.640791/0.636575，确定性 raw specular 参考为 0.646484。有限、相关的 64 帧数据不是无偏证明，后续应继续检查异材质归一化/guide 精度。

原 NRD 两条路径、动态灯/阴影质量、30/60/120 Hz compatibility 时间线、raw/filtered/debug 与释放重建回归通过。Editor raw MSE=0.050401、filtered=0.000257，specular raw/filtered=0.296814/0.293737。Win64 Mono release 两个构建与实际 EXE、D3D11 inactive 通过；Player 新增的 64² 两像素颜色与四像素金属边界最大 albedo 误差 0.000122、镜面相对误差约 0.227%、假镜面为零，原 filtered MSE≈0.000101。没有新增 GPU 资源、Feature 或最终材质合成。无 DI/NRD shader/command-buffer 错误，Roslyn 编译无错误；其他模块及已有 lilToon duplicate-keyword 警告保留。

**边界：** 只证明这些静态 opaque 受控案例。深度前景案例使用相同入射 directional 光，证明颜色恢复/投影与 reuse 输出，不单独证明不同入射光照下的 NRD 深度隔离；后续需要点灯/异法线的边界对照。两个类别不能隔离所有材质，特别是同类的彩色/低 F0 镜面仍使用带下限的解调，不能宣称完整纹理保真；梯度滤波跨几何扩散、每 lobe 距离、动态材质/几何、TAA 与复杂场景仍需检查。首次混合材质 reuse 实验采用了不成立的逐像素确定性断言（raw≈0.15393 vs 0.15915），已改为独立连续统计；失败实验没有计作通过，也没有借调降 NRD 门槛掩盖 raw 差异。下一项检查不同入射光照的几何边界与镜面颜色/低 F0，再据参考评审信号表示。
