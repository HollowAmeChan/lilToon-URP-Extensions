# Ho-RTDI：NRD RELAX 技术专题与接入记录

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

## 2026-10-18 当前状态

NRD 默认关闭。开启后，稳定半秒的相机改用 .5 秒 diffuse 主历史，变化及半秒响应窗口使用 .05 秒；60Hz约为30/3。Fast diffuse 与镜面预算保持，离开稳定状态仅重启一次。判定是逐相机全局，持续动态尚无逐像素长历史方案。固定夹具静止方差降至旧版约1/19，两条真实Mono Player已验证；严格casterStep与mixed天空镜面仍失败。详见[参考收敛审计](../审计/Ho-RTDI-ReferenceConvergence.md)。其后已新增可选的正常相机参考PBR合成，使用已发布的filtered纹理；该合成与NRD输入/历史是两个合同，现行接入方式见[总文档](../../Ho-RTDI.md)。

生产 diffuse 已包含[非负历史约束](Ho-RTDI-NonnegativeDiffuseHistory.md)与[颜色凸裁剪](Ho-RTDI-DiffuseConvexClamping.md)，vendor与CPU库仍固定。下文保存最初接入及步骤6—23的详细实现/测试语境，其中“当前预设”“3/1历史”和显存表指写作时状态，不能覆盖上述最新策略或视作当前完整资源预算。

## 原始接入合同与逐步验证记录

Feature 的 **NRD RELAX 降噪** 为可选项，默认关闭。开启后仍保留 raw diffuse/specular；公开的 diffuse/specular 输出改为经过 RELAX 并恢复材质因子的结果。默认不写 camera color，不修改 lilToon 最终接收、CS、ShadowCast 或 GI。

## 参考与实现选择

使用用户参考中的 NRD 4.17.3 CPU 库、RELAX 原始 shader、NRD 前端 helpers 与调度描述。原参考 Denoiser.dll 同时集成 NRI、DLSS、NIS、Unity descriptor-heap hook 和设备内存控制。本步只引入重命名后的 NRD 核心 CPU 库，由它选择 pass、常量和 pool；Unity CommandBuffer 执行对应的原始 ComputeShader，所有 GPU 资源归 Unity 管理。

本地 DLL 实测为 4.17.3，normal encoding=2、roughness encoding=1。15 个 pipeline 的 shaderIdentifier（含宏）与 Unity wrappers 的资源布局逐一对齐。常量缓冲、sampler 和资源声明通过 custom-engine 宏适配。33 个 shader/MathLib/config 文件和 3 个 ABI header 的原 SHA-256 与正文保留；当前 DenseDI 另有明确派生的方差、HistoryClamping 与 F0 条件 body，见文末，不能把布局核对当成原始算法未改的证明。许可、CPU DLL hash 与出处见 ThirdParty/NRD。

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
| filtered publication | 先撤销 GPU 线性信号尺度，再由 diffuse albedo / 同一 specular helper 恢复材质；RGB 保持原 DI 接收含义，alpha 重新写 receiver-validity |

没有复制当前主表面 GBuffer；现有 GB/SB 被借用，新增的是 NRD 实际要求的信号编码。NRD shader 不自行读取/重渲染场景。帧间时间与历史预算现在使用相机实际提交的 unscaled sample time，详见后面的时间契约。

`HoRTDIRenderGraphResources.diffuse/specular` 与原兼容 globals 发布最终选择的 raw/filtered 结果；新增 rawDiffuse/rawSpecular 与 `_HoRTDIRawDiffuseTexture` / `_HoRTDIRawSpecularTexture` 始终保留 raw。Volume 的 RawDiffuse / RawSpecular 可作同帧对照，原 Receiver 枚举值保持不变。

## 调度、ABI 与历史

在已有 unsafe graphics-queue DI pass 内，BeginNRDScale 初始化尺度状态，ShadeNRD 写 FP32 解调暂存并按线程组统计峰值，MeasureNRDHistory 计入旧 RGB / 二阶矩，SelectNRDScale 选择共同尺度。NormalizeNRD 在缩放之前发布物理 raw 并保存旧 raw luminance，然后打包缩放后的 NRD inputs；随后执行核心库返回的 dispatch 列表，最后 ResolveNRD 撤销尺度并恢复材质因子。pool、signal、output、scale 和 constant buffers 都声明 RenderGraph 依赖，compatibility 使用同一调度函数。没有 Unity native graphics device、command-list 或 descriptor-heap 直接操作。

核心调用返回的指针只在本轮复制描述、资源与常量时读取，不跨下一次 GetComputeDispatches 保存裸指针。每个 dispatch 使用独立常量偏移，帧常量使用三份 64 KiB buffer，避免同轮不同 pass 共用覆盖的 CB 地址。原始 ResourceType 数值以固定头文件为准；该参考库的 GetResourceTypeString 字符串数组顺序已经落后于枚举，不能用它决定绑定。

分辨率变化、禁用/重启、DI 关键设置或世界几何 Revision 变化会重启 NRD 历史；灯参数变化交给 RELAX 的 confidence / antilag。DenseDI 关闭两个 prepass、短历史颜色预滤波和稀疏 HistoryFix；两条 lobe 都有有效距离，没有概率 lobe split。历史以秒定义，60 Hz 时 diffuse 主/fast 为 3/1、specular 为 30/6；低 FPS diffuse 至少保留一份旧样本用于时间方差。antilag acceleration=0.6、reset=1。SDK temporal / A-trous 调度继续使用，两个派生 body 的具体变化及代价见文末；没有另建整套降噪器或每次灯变化整份清历史。

## 验证与边界

- Windows x64、Unity 6000.3.15f1、D3D12 RG / compatibility，Gamma / Linear 通过真实 GPU 验证；15 个 Unity kernel 编译、固定 DLL/ABI、原始 uniform/power DI 及 GI 回归分别验证。
- 双色点灯噪声场景中，raw 红通道 MSE 约 0.050401，filtered 约 0.000049；filtered 红/蓝均值约 0.22549/0.22642，数学参考约 0.22。
- 64² 镜面测试中，raw ROI 均值约 0.296814，默认 prepass 时约 0.222147；dense-DI 预设约 0.276015。该改进不等于无偏、无空间模糊或任意高光都保真。
- 已覆盖选中灯距离、正 viewZ、金属 diffuse、镜面信号、静态世界相机移动、透视相机、输入域拒绝、raw/filtered 调试、关闭释放与重建。无效表面最后仍输出 A=0。

Windows x64 Mono release Player 已独立验收：普通序列化 renderer 引用 DI compute 与 15 个 NRD compute，正常裁剪后实际执行 RG / compatibility，两条路径的单灯、世界遮挡、Power、独立镜面、raw/filtered、RELAX 关闭重建均通过。packed 核心 DLL SHA-256 与源 DLL 相同；D3D11 实际启动并保持 DI inactive。双色灯 Player raw MSE 约 0.050401，filtered 约 0.000066，红/蓝均值约 0.227；仍是 64² 受控场景，不能代表生产画质。验证入口与复现步骤见 [Player probe](../../../Tests~/Player/RTDI/README.md)。

DI 的 debug / grid-label shader 现由 Feature 序列化持有，正常裁剪后 Raw / Filtered / Receiver / All 网格在独立 EXE 的两条路径均通过。旧 renderer 的缺失引用会在 Create 或打开 Feature Inspector 时补齐并 SetDirty；请保存 renderer asset 后构建。补齐只填空位，NRD 固定库扩容保留已有 override；再次检查完整引用不会弄脏资产。空库/部分库已在三个独立 Editor 进程中验证保存与磁盘重载，没有借 AssetDatabase 的内存状态冒充部署持久化。

当前是 **静态不透明 Mono 的 Editor / Windows Player 初版**。不支持/未验证 IL2CPP、skinned 或运动几何、XR、TAA jitter 的显式 NRD 契约、相机切换/时间跳变的大规模场景、完整材质 ID 边界路由和生产 GPU budget。GB 通用 fallback / MSAA resolve 不在本次真实 lilToon、MSAA=1 接收测试内。没有添加构建时全工程批量迁移；未打开/保存的外部旧 renderer 仍应先完成正常补引用流程。DLL importer 限定 Windows x64。

当前六张 NRD 信号/输出纹理合计约 60 B/像素（两张 radiance staging 为 FP32）；材质因子在 external output 上原地恢复，不再另分配两张 filtered 纹理。RELAX pool 约 80 B/像素，另有三个 64 KiB 常量 buffer、16 B scale buffer 和后文的 confidence 资源。1080p 实测 DI＋NRD 活动载荷约 495.637 MiB，不含输入、AS、驱动/对齐与缓存。NRD Record 的稳定热路径已复用描述、资源表与常量 CPU 拷贝区，见下节；Feature 的扫描、Prepare、外部绑定与渲染图等仍可能分配，不能把局部零分配写成整帧零分配。

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

当前周期为 diffuse 主/fast 50/16.67 ms、specular 主/fast 500/100 ms；主历史上限 255，fast 小于主预算，diffuse 主预算下限 1 为时间方差保留两次观察。步骤 8 的 history fix 50 ms / 3 帧预算已在步骤 12 关闭。时间回退、无效值或相机超过 1 秒无样本触发 NRD clear/restart；该 1 秒界限是项目的暂停/恢复策略。时间缩放不改变 unscaled 契约。额外采样仍推进 sampler frame index，不把一帧中 100 次 SubmitRenderRequest 解释成真实 6000 FPS。

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

## DenseDI 方差、短历史与镜面二阶矩

步骤 12 增加点灯 (0,0,-1)、深度 -0.35 的四像素蓝色前景和 z=0 白色背景，逐像素核对距离/余弦衰减与投影到背景的更宽阴影。原始 raw 符合解析真值，旧 filtered 阴影最高 0.153076、全 ROI 最大误差同值；同类两像素红/蓝金属的单灯镜面平均相对误差约 50.2%，出现 0.013237 的假颜色。

逐 pass GPU 副本确认 PrePass / Temporal / HistoryClamping 仍输出正确的零阴影，第一次 A-trous 才填亮。关闭 acceleration 没有改善，已撤回该尝试。固定版本短历史 variance 分支先平均颜色，且 depthW=1；热分支的 3×3 helper 则先平均 RGB/二阶矩再相减，也把邻域确定的光照/材质差异算入“噪声”。[原参考对应的上游 shader](https://github.com/NVIDIA-RTX/NRD/blob/master/Shaders/RELAX_AtrousSmem.cs.hlsl)仍采用这种空间统计；这里的更改是 DenseDI 取舍，不宣称是普适的上游修复。

当前 DenseDI 禁用短历史颜色 bootstrap（varianceHistory=0）与稀疏 HistoryFix（settings=0；core 实际阈值为 1）。diffuse 主预算下限 1，保留两次观察用于时间方差；30/60/120 Hz 的既有主/fast 预算不变。独立派生的 A-trous body 只将 computeVariance 改为每像素 max(m2-luma²,0) 的 Gaussian 平均，排除邻域均值差异。其余 kernel、资源/常量布局、MathLib 与 CPU DLL 未换版本；首次/断史时不再依赖邻域颜色预平均，噪声需要后续时间观察与 A-trous 抑制。

正常 60 Hz 验证进一步发现 HistoryFix 关闭时的 alpha 混用：Temporal 的 fast specular alpha 存命中距离；固定 HistoryClamping 在 historyLength≤1 复制整个 fast float4，方向灯的 65504 就进入 normal history 的二阶矩。实测之后的 Temporal alpha 达到约 2230，而稳定红色信号的正确二阶矩约 0.077。第二份派生 body 只在 gHistoryFixFrameNum==1 的复制分支采用 fast RGB、保留 Temporal 二阶矩，继续执行原 moment correction；HistoryFix 开启的原分支不变。修后 alpha≈0.077。独立 GPU probe 同时验证矩阵列布局和两次延后 Constant-buffer 上传快照正确，未凭猜测更改矩阵或 buffer 所有权。

NRD confidence 也改为按其自身历史有效性读取 previous guides，独立于是否启用 RTXDI temporal reservoir；采样阶段的开关不变。单灯改色且 reservoir temporal 关闭时，实际 confidence 下降的回归已通过。临时逐 pass 复制/反射工具保留在隔离研究日志，生产调度没有这些 hook。

| 指标 | 当前结果 |
| --- | --- |
| Editor 128² / 60 Hz 点灯深度 ROI | 平均绝对误差 0.000423，最大 0.006348，阴影最大 0.000010 |
| Editor 单灯红/蓝金属 | 相对误差约 0.1465%，假通道 F6 下为零 |
| Win64 Player 64² 两条路径 | 点灯最大误差 0.000488、阴影为零；彩色镜面误差约 0.1511%、假通道为零 |
| 稳定双色灯噪声 | Editor raw MSE≈0.050401、filtered≈0.000179；Player filtered≈0.000086 |
| 物理时间线 30 / 60 / 120 Hz | 平均相对误差约 9.337 / 14.474 / 12.764%，与前轮接近 |
| 自有载荷 720p / 1080p | 205.858 / 463.996 MiB，不增 GPU 资源；简单 RG 同步 batch 6.405 / 10.606 ms |

同一 render-request 运动 fixture 的中心误差约 10.89%，前轮约 6.55%；它受实际 clock budget 和新增的低 FPS 最小历史影响，不能隐去这项代价或把不同节奏当成因果证明。单独注入时间的 30/60/120 Hz 对照只变化约 0.09/0.02/0.005 个百分点。同步 batch 含 CPU / raster / DI / NRD / GPU 读回，不是孤立 GPU 性能证明。

两条 Editor 边界/NRD、动态灯/阴影、compatibility 时间线、目标分辨率与生命周期通过。两个 release 构建和实际 D3D12 EXE / D3D11 inactive 通过；没有新增 DI/NRD shader 或 command-buffer 错误，Roslyn 无错误，已有其他模块与 lilToon duplicate-keyword 警告保留。

**仍未解决：** 60 Hz、两个同向白色方向灯 intensity=0.5/0.25、每轮一个 infinite candidate 的彩色金属，64 次观察 raw 平均 0.970442（解析参考 0.969727），filtered 仅 0.816392，假通道平均 0.005367。单灯通过不能代替这个随机输入的支持；下一项优先评审 F0 解调、材质边界及噪声下的滤波耦合。低 F0 / 不同角度与粗糙度、材质动画、动态几何、TAA、gradient 跨边界扩散仍需证明。未设计最终 PBR/NPR 合成。

原始 36 份 vendor 文件的 SHA 保持原记录，两个派生 body 位于 Runtime/RTDI/Shaders/NRD，原版权/许可保留。执行 `python Tests~/RTDI/generate_nrd_variance_adapter.py --check` 校验原 SHA 并逐字重建“一个 helper / 一个分支”改动；去掉 `--check` 可重新生成。ShaderIdentifier 仅验证绑定布局，不能宣称算法 body 与原版完全相同。验证入口为 `RunBoundaries` / `RunBoundariesCompatibility`（明确 60 Hz sample timeline），以及原 NRD/质量/时间/预算与 Player probe。

## 随机输入下的 F0 条件与历史输运

步骤 13 先改变条纹宽度，原实现同一两灯/采样配置的 filtered 均值从 2px 的 0.816392 到 8px 的 0.934920、64px 的 0.957986、整片红色的 0.959881；raw 始终约 0.969727。偏差随材质交界密度变化，不能用整体提亮修正。[官方解调/材质分类原则](https://github.com/NVIDIA-RTX/NRD)和用户 FullSample 的 DemodulateSpecular(max(0.01,F0)) 仍有非零下限，零通道的信息不能反除恢复。当前仍保留 NRD specular helper 和原 raw GGX，增加材质条件来限制空间/历史信号相混，不声称实现了精确可分离的 Fresnel 表示。

`HoRTDINrdMaterialGuide.hlsl` 直接 Load 现有 Mono SB color / material / reflection，按 DI bridge 相同的 8-bit 量化得到 F0。引导向量为 F0 加 NRD 的 0.02 正则常量（不是再次计算完整 envBRDF）；同类 guide 权重为 dot(min(a,b)²)/dot(max(a,b)²)，相同值权重严格为 1。量化后的零通道集合不匹配则拒绝：这只是保守的材质匹配，不意味着零 F0 在掠射角没有 Fresnel。条件只乘在 specular A-trous 权重上，diffuse 不变；小步 kernel 在 groupshared 缓存 guide，大步 kernel 缓存中心 guide。无需按颜色生成 ID，也不新增完整材质纹理。

只加空间条件后，相机以每样本 1/8 pixel 移动的颜色条纹仍偏暗约 20%；NRD 旧输入的非线性解调不能直接在不同 F0 间插值。Temporal 现在借用已存在的 16 B DI 主表面历史中的 F0，根据有效 bilinear footprint 的最低材质匹配度降低 specular 主/fast 历史预算；零通道变更不继承旧预算。历史无效、域外或非有限 UV 返回零。原矩阵、motion、NRD pool、资源布局和 DLL 不变；没有增添 previous-material 输出。

移动相机时原 metric 也需修正：fractional 主表面采样本来可能同时含红/蓝，且 MC 亮度每轮不同。因此移动测试比较 RGB **总和**及归一化颜色比，与同帧 raw 的颜色比对照，不把 max(R,B) 的下降或单帧亮度误差当作色彩失败。部署测试新增独立 raw-specular 捕获，NRD 始终开启，避免开关 NRD 销毁待测历史。

| 明确 60 Hz 的 Editor 对照 | raw / filtered / 参考 | 其他指标 |
| --- | --- | --- |
| 2px 红/蓝、两白色同向方向灯 | 0.970442 / 0.942949 / 0.969727 | 偏暗约 2.76%，假通道均值约 0.000275；原偏暗约 15.8% |
| 8px / 64px / 整片红色 | filtered 约 0.959436 / 0.959968 / 0.959881 | 继续在 5% 能量门槛内 |
| F0 0.04 的 2px 颜色 | 0.038077 / 0.037573 / 0.038028 | 假通道约 0.000001 |
| F0 0.2 的 2px 颜色 | 0.193972 / 0.191952 / 0.193945 | 假通道约 0.000002 |
| 连续红/蓝 F0 渐变，RGB 总和 | 0.969773 / 0.958483 / 0.969727 | 能量误差约 1.16% |
| 相机缓慢移动，RGB 总和 | 0.968610 / 0.971167 / 0.969727 | 颜色比误差约 0.000046 |

两条 Editor 路径均通过这些门槛，原 NRD 噪声/镜面/相机/输出调试/释放重建、动态灯/阴影与 compatibility 时间线继续通过。Win64 Mono release 两个构建及实际 D3D12 EXE / D3D11 inactive 通过：Player 两灯 filtered 0.953528 / 参考0.969727（约 1.67% 偏暗），移动总和0.971560、颜色比误差0.000046；原有 Player 噪声 MSE≈0.000086–0.000102。没有新增 DI/NRD shader/command-buffer 错误，Roslyn 无错误，已有 lilToon duplicate-keyword 与其他模块警告保留。

代价是额外材质读取和 specular history 条件。自有 GPU 载荷仍为 205.858 / 463.996 MiB（720p / 1080p）；简单 RG 同步 batch 本轮约 6.408 / 11.615 ms，前轮约 6.405 / 10.606 ms，不宣称没有 GPU 成本或是严格 GPU 单项消融。靠近材质变更的历史更短，可能增加噪声；约 1–3% 的剩余偏差仍未消除。条件/Shader 从固定源码生成，可用原 `--check` 重建验证，原 vendor 正文不改；当前派生包括 Temporal、A-trous、A-trousSmem 与 HistoryClamping。

**下一项：** 本轮只有正面平面、两同向白灯和规则颜色，尚未证明不同光角、粗糙度、彩色入射光、复杂形状与粗糙/低 F0 混合；世界/材质动画、TAA、XR 未扩张。优先扩大真实 GGX 与命中距离对照，再评审是否需要改变 specular signal 表示。保持 raw / filtered 独立，不设计终端 PBR/NPR 合成。

## GGX 角度/粗糙度审计与 HDR 范围缺口

步骤 14 使用 CPU double 的独立 GGX D/G 与 Schlick Fresnel 计算，核对实际解析灯 RGB、旋转和 8-bit F0。两彩色方向灯 intensity=0.02/0.01，入射角为 θ / -0.7θ；64² 正面平面、2px 红蓝金属、一个 infinite candidate，明确 60 Hz。每组预热 96 次、观察 96 次；按输入 guide 的实际量化 roughness 而不是名义 Material 数值计算参考。名义 0.1/0.3/0.7 经 SB/DI 量化后约为 0.0980/0.2980/0.6980；第一次以名义 0.1 断言实际 roughness 的失败已纠正，不借这个差异改着色。

| 实际 roughness | θ=0° filtered 总能量误差 | θ=20° | θ=45° |
| --- | --- | --- | --- |
| 0.0980 | 1.44% | 1.02% | 2.20% |
| 0.2980 | 1.76% | 1.25% | 1.46% |
| 0.6980 | 0.93% | 1.38% | 1.27% |

`RunSpecular` / `RunSpecularCompatibility` 两条 Editor 路径的九组 raw RGB 数学门槛及 filtered 5% 总能量门槛通过。raw 容差为 max(2e-7, reference RGB sum ×4%)，没有用 1e-4 固定容差掩盖低至 1e-5 的离轴信号。参考公式按用户 ShadingHelpers / donut brdf 核对，CPU 单独实现。这个结果扩大了低强度角度域，不证明任意 HDR、粗糙形状、Point/Spot 或视角都正确；没有改生产着色/滤波，也没有重新声称 Player 的角度域已验收。

高强度案例：roughness≈0.0980、两同向白灯 intensity=0.5/0.25。raw 平均≈646.167、filtered≈640.069、独立参考≈646.029，表面上误差只有约0.9%；但 NRD 的 `gHistory_Spec` 中 mean≈3330、二阶矩仅65504，variance≈-11023400。全 pool 的 finite 检查仍通过，说明有限/输出均值接近不足以证明内部统计正确。

这是**步骤 14 明确重现的 HDR 二阶矩饱和**（步骤 15 的修复见下节）。`RunSpecularHDR` / `RunSpecularHDRCompatibility` 明确检查 m2≥0.99×mean²；步骤 14 compatibility 实测 exit=1，日志为 `HDR second moment saturated below mean squared`。不要把这个失败算作九组角度 gate 的通过，也不要将数值直接当作负噪声再置零验收。原 NRD pool 的四张 permanent / 四张 transient RGBA16 支持有限动态范围；[官方 NRD 范围建议](https://github.com/NVIDIA-RTX/NRD)明确二阶矩的平方需落在 FP16 内，并建议较宽范围信号先作适当压缩/线性缩放。

下一项最高优先级为 NRD 信号归一化与历史尺度契约：保持 raw RGB 和最终 DI 单位，避免瞬时尺度变化损坏历史，证明亮/暗并存、灯光动态与高峰值的 m2/能量。不能用整体提亮、静默饱和或取消高强度测试代替；也不能只升某张 history texture 而忽略中间 RGBA16 写入。本步只交付验证/证据，没有增加 Feature、生产纹理、GPU pass 或改变 PBR/NPR 合成。

## GPU 线性 HDR 信号域与历史恢复

步骤 15 保留原失败门槛，按[官方线性缩放建议](https://github.com/NVIDIA-RTX/NRD#other)把解调信号限制到 RGB≤128，为 FP16 二阶矩保留余量。两个输入改为 RGBA32F 暂存，避免峰值选择之前被 front-end / FP16 裁剪；raw 与发布输出仍是 RGBA16F、物理 DI 单位。16 B GPU 状态记录峰值、当前尺度与前轮尺度，不做逐帧 CPU 读回。Shade 按 8×8 组归约峰值；新调度为 Begin、MeasureHistory、Select、Normalize，后两者中的 Select / Begin 是单线程。

第一次只允许尺度下降，亮场统计恢复但暗场 mean≈0.001457、m2≈1.07e-6，明显小于 mean²；新增门槛再次失败，不能归咎于几个 half ULP。最终选择同时纳入旧 slow/fast RGB 的最大分量与 slow sqrt(m2)，将它们除以前轮尺度恢复成物理范围后与当前输入峰值一起归约。通过实际上一轮 Temporal manifest 借用 permanent pool，检查资源种类与四个输入完整性，不硬编码 pool index、不复制历史。由此可以安全增大尺度，保留有效历史，同时恢复暗处精度。

派生 Temporal 在 SMB / VMB 取样后把旧 RGB 乘 current/previous、旧 m2 乘其平方，fast alpha 的 hitT 不动；同轮其他 NRD 阶段天然处在新域。Resolve 撤销尺度再恢复材质，原 confidence 仍吃物理 raw。Normalize 在缩放前写 raw、采集前轮 raw luminance，避免 ShadeNRD 新增状态超过八个 UAV；没有增加重复光线或额外 capture pass。比例、native 版本/ABI、pool 格式与原 vendor 正文仍可独立核对，generator `--check` 包含这项派生。

| Editor，明确 60 Hz，两条路径相同 | 结果 |
| --- | --- |
| 原 HDR 0.5/0.25，参考≈646.029 | filtered≈639.597；history mean≈52.031、m2≈2984，variance≈276.749 |
| 提高至 16/8，参考≈20672.93 | raw≈20674.16、filtered≈20443.01，scale=1/2048；解调 staging 已超过原 FP16 输入范围 |
| 降至 0.0005/0.00025，参考≈0.646029 | filtered≈0.638；scale 从 1/1024 安全恢复至1，mean≈2.984、m2≈10.023、variance≈1.117 |
| 恢复 0.5/0.25 | filtered≈638.022，scale=1/64，m2≈2722，variance≈284.109 |
| 同屏 F0=1 与10/255，16/8照明 | filtered≈20393.74 / 804.404，参考≈20672.93 / 810.703，两区各自通过5%门槛 |
| 释放重建 / resize | 新状态初始化、尺度恢复与能量门槛通过 |

原九组角度、两条 Editor NRD 回归、目标分辨率通过；两个 Win64 Mono release 构建和实际 D3D12 EXE / D3D11 inactive 通过。Player 四段 HDR 最差 raw / filtered 平均相对误差约0.119% / 1.071%，原噪声 MSE≈0.000086、点灯边界与 F0 输运门槛继续通过。最终日志没有新增 DI/NRD shader、UAV 或 command-buffer 诊断；其他模块与 lilToon 旧警告不计入这个结论。Roslyn 与派生源码重建通过。

代价：+16 B/像素与16 B状态，720p /1080p 活动载荷≈219.921 /495.637 MiB；简单 RG 同步 batch≈6.570 /12.172 ms（含 CPU、全部渲染及读回，不是孤立 GPU benchmark）。多一次全尺寸历史范围读取与归一化，且归一化阶段借用主表面恢复 raw。全屏共同尺度仍受最亮输入/保留历史约束，极端同屏动态范围可能使很暗的统计失去精度；最终 FP16 输出的65504上限也仍在，未声称无限 HDR。当前验证是受控静态表面/方向灯，Player 不读内部 pool；不是动态几何、TAA、XR 或生产场景证明。

完成后判断：这一修复有明确正确性收益，成本已量化。下一项优先核对 Point/Spot 与变化视角/曲面上的 specular hitT、遮挡边缘和能量，评审信号/距离是否需要按 lobe 区分；随后再决定优化 staging / peak reduction，不先扩张灯域、PT 框架或最终 PBR/NPR 合成。

## 局部灯与斜视角：通过域和仍失败的随机高光

步骤16对照用户 `RayTracing/ShadingHelpers.hlsl` / donor `brdf.hlsli`，新增独立CPU透视射线/平面交点、Point/Spot平方距离及range/锥衰减、height-correlated Smith GGX、segment/AABB遮挡参考。原GGX验证只涉及NdotV=1，扩展V后采用实际的correlated G/NdotV，不能直接推广separable G1积。当前仍从SB量化roughness与F0后计算DI；F0=10/255，名义roughness0.3/0.7实际约0.298/0.698。

`RunLocalLighting` / `RunLocalLightingCompatibility`：128²透视平面，Point/Spot ×两个roughness×视角0/30/60°，一个候选、无ReSTIR复用，64轮预热/16轮观察、明确60Hz。每像素核对raw、filtered和选中灯距离；RGB照明为非白色，当前误差统计用R通道（固定灰F0、两灯同色）。两条路径通过：

| 检查 | 结果与实际覆盖 |
| --- | --- |
| 静态解析照明 | raw归一化L1约0.03–0.04%；filtered diffuse/specular约0.08–0.60% |
| selected-light hitT | 最大误差约1.3e-5，不把接收alpha或相机距离当作灯距 |
| Spot角域 | inner/transition/outside样本882/4502/760；最初fixture未覆盖外锥，收窄至inner6°/outer20°后通过覆盖门槛 |
| 薄世界遮挡 | 每种灯320个有正入射的遮挡样本；独立CPU visibility正确，filtered L1约0.24–0.60% diffuse、0.25–0.28% specular |
| 暗区最大值/受光峰值 | Point两路为0；Spot约3.9e-5 diffuse、1e-6 specular |
| 单Point，15°/秒绕视 | raw L1≈0.035%、filtered specular L1≈1.77%，64次运动采样 |

这只证明固定材质平面上的局部灯/斜视角/薄遮挡/单灯绕视，不证明曲面、每个ReSTIR样本都在specular lobe内或最终NPR合成。对照[NRD输入契约](https://github.com/NVIDIA-RTX/NRD)后，selected-light distance仍有in-lobe和多lobe代表性限制；用户DI helper产生lightDistance也不足以独立证明NRD tracking正确。当前没有据此增加一条反射追踪射线。

**保留的失败：** `RunLocalPairQuality` / `RunLocalPairQualityCompatibility`在同一场景增加远Point(-30,0,-50)、intensity25000、range100，近Point供中心高光、远灯主导diffuse；两灯同色，距离约1.6/58。真实initial=8、temporal+spatial开启，192预热/512观察。compatibility中心32² ROI的raw镜面均值/解析值≈0.973010、filtered≈0.814340；绕视raw≈0.976933、filtered≈0.844867。静态5%能量gate明确exit1：`Paired local-light NRD static energy bias exceeds 5%`。它是ROI亮度/光斑形状缺口，还没有整画面能量守恒的测量。

128轮初测raw偏差约4.8%，不能据此直接宣称RIS有偏。`RunLocalPair`扩展到512轮，并独立消融复用，得到下面这一次观察；ROI/调度/随机序列都明确，不能把有限样本当成无偏证明：

| ReSTIR temporal/spatial | raw specular比例 | filtered specular比例 | raw diffuse比例 |
| --- | --- | --- | --- |
| off/off | 1.000828 | 0.855860 | 0.999572 |
| on/off | 1.009871 | 0.847682 | 0.998054 |
| off/on | 1.000627 | 0.848728 | 0.999673 |
| on/on | 1.012106 | 0.859024 | 1.001706 |

`RunLocalFilterAudit`只在隔离Editor中通过真实native SetDenoiserSettings作受控诊断；clock预算稳定后覆写，确认Record未因预算变化撤销覆写，生产DenseDI保持原配置。它不把诊断0退出记为质量通过。通过实际Temporal manifest读取permanent history，每8轮用同像素raw/staged比值恢复其材质因子与尺度，测量空间滤波前的物理历史亮度：

| 参数试验，无ReSTIR复用 | history/参考 | final/参考 | 绕视final/参考 |
| --- | --- | --- | --- |
| 基线sigma2、fast6、acceleration0.6、reset1 | 0.989741 | 0.855980 | 0.841579 |
| 支持范围内sigma3 | 0.989554 | 0.854461 | 0.850136 |
| fast15 | 0.987055 | 0.853251 | 0.850280 |
| acceleration/reset均0 | 0.989722 | 0.855850 | 0.853715 |
| specular prepass5 | 0.805617 | 0.694448 | 0.705294 |

固定核心把HistoryClamping写到这些permanent颜色，再供A-trous读取，antiFirefly在DenseDI关闭，因此主要剩余损失在空间阶段。不要把它未经验证地归因到hitT，或放宽fast-clamp参数求过关。一个按fast历史计数缩放specular空间方差的派生原型也没有改善（TS final≈0.811816），已撤回；原vendor/派生SHA重建通过。没有保留该cache、GPU成本或参数改动。

本步最终只有Editor验证/诊断及文档变化；两条支持域gate、Roslyn、派生重建通过，双灯quality gate保持失败。没有新增生产资源、Feature、参数、射线或改PBR/NPR合成，也未重新声称Player/曲面/TAA域通过。下一项查A-trous置信度、空间权重、不同宽度ROI与整光斑的扩散/能量；尤其需要区分真实高光轮廓、随机输入噪声与reprojection relaxation，再决定适当的算法修复。

## Specular 空间方差传播与成对边缘权重

步骤17先通过 `RunLocalSpatialAudit` 读取实际Temporal输出的reprojection confidence和外部lighting confidence；该静态用例都是1。宽度32/64/96/128的ROI中，旧final/解析值约0.856/0.881/0.900/0.913。128覆盖全部可见平面（按独立平面交点排除边界外像素），因此除了ROI外扩散，还存在整体下降。原permanent颜色历史约0.989；本轮修复限制于它之后的DI specular滤波。

对照用户固定源码与[官方A-trous shader](https://github.com/NVIDIA-RTX/NRD/blob/master/Shaders/RELAX_Atrous.cs.hlsl)，采用三项DenseDI适配，原vendor正文/ABI/pool和raw不改：

- specular range bandwidth同时使用两个端点较小的方差。精确的暗端可以拒绝亮端的噪声尺度，不再单由bright center较大的variance放宽范围；后续step同样采用这一对称规则。它是保守边缘策略，不是声称两端差值的精确概率方差等于min。
- 首层原来混合M2后减去混合RGB的亮度平方，会重新把真实光斑空间变化算进variance。现在从每像素时间方差用Σ(w²v)/Σ(w)²传播，和后续A-trous的传播形式一致。
- 首层估计累积specular颜色的不确定度时，用max(1,min(historyLength,fastFrames+1))除时间方差。clamping可能采用更短fast颜色，因此不直接使用完整main预算；相关ReSTIR输入和自适应历史下仍是近似，不承诺统计无偏。增加的是576 B/组的history-count cache及已有R8输入的读取。

最初同时改diffuse传播，原noise MSE约0.000179变为0.000543，已撤回diffuse部分，最终回到0.000179。单独成对权重、单独传播、两项合并及历史计数的消融都在真实shader执行；各自不能代替完整验证。之前步骤16“只除方差/fast计数”的失败没有被删除，本步作用于成对权重与输出variance的整体契约。

同一诊断入口、随机序列、192预热/512观察，对照baseline源码并恢复最终代码：

| 初始8候选，状态 | 旧final/解析值 | 新final/解析值 | 旧/新归一化specular MSE |
| --- | --- | --- | --- |
| 无时空复用，中心32² | 0.855860 | 0.984282 | 0.037400 / 0.007037 |
| temporal+spatial，中心32² | 0.828719 | 0.963095 | 0.050194 / 0.010763 |
| 无复用，全部可见平面 | 0.913263 | 0.987245 | — |
| temporal+spatial，全部可见平面 | 0.898673 | 0.975442 | — |

MSE以解析reference平方和归一，包含bias和随时间的noise；raw分别为1.669559/1.893981。这里有明确总误差下降，不能只用均值变亮宣称有效。单Point绕视L1约1.17%；原另一个固定quality随机序列（`RunLocalPairQualityCompatibility`）静态ratio仍为0.947065、运动ratio约0.982016、运动L1约8.04%，静态5%门槛仍exit1。没有调整门槛或全局RGB增益，不能把另一个通过的观察替代这项失败。

**新增双灯遮挡失败：** `RunLocalPairShadow`用相同CPU segment/AABB visibility，近灯薄遮挡、远灯仍贡献。raw shadow/参考在无复用/T/S/TS为0/0.829793/0.513458/0.865544，diffuse raw总比例约0.797–0.910。切回baseline重跑，raw数据完全一致，说明这是已有sampling/visibility域缺口，尚未证明是实现还是输入契约的问题。旧filtered shadow比例为0.932/3.168/1.564/2.941，受光区0.712/0.519/0.693/0.626；新分别为0/0.816/0.506/0.852、受光区0.982/0.932/0.969/0.972。滤波已减少泄漏/偏暗，但raw问题未修复，shadow gate保持exit1。

两条Editor的NRD、F0/薄遮挡边界、九组GGX、HDR范围、释放重建回归通过；RG的quality/time、目标尺寸通过。两个Win64 Mono release构建与实际D3D12 EXE、D3D11 inactive通过，packed NRD ABI不变。Player旧噪声MSE约0.000086–0.000102，noisy metal均值0.953351/参考0.969727、HDR最差约1.070%能量误差；这是旧部署fixture回归，没有把新128²双灯支持域算成Player验收。最终没有新增DI/NRD Shader/UAV/command-buffer诊断；Roslyn和派生重建通过，其他模块旧警告保留。

自有载荷仍为720p/1080p约219.921/495.637 MiB；简单RG同步batch本轮6.750/12.799 ms，前轮6.570/12.172 ms，含完整CPU/渲染/读回，不是孤立GPU性能。除了上述组内cache、history读取和额外方差运算，没有新增纹理、pass或光线；成本有变化，不宣称免费。

完成后判断：这一局部修复有明确的镜面总误差收益，保留两个失败门槛。下一项优先用单Far lamp和实际ray/reservoir数据定位双灯遮挡的raw缺口，再处理固定quality序列的剩余偏差、曲面和目标场景。保持最终lilToon PBR/NPR接收与合成边界。

## 初始 visibility 与选中 reservoir 的一致性

步骤18从raw隔离开始，生产滤波配置不变。单Far在有薄遮挡的场景仍全可见，shadow raw/reference≈0.999629；单Near正确被挡，混合1候选约1.001557；混合8候选则为0，第一次读回的256个shadow reservoir全部invalid。这与场景ray/材质的普遍错误不符，缺口在多候选初始阶段。

独立GPU API探针只给synthetic surfaces和两个analytic lights，不包含场景AS或visibility。固定shader SDK调用返回后，比较returned sample.position与reservoir的light index/UV重建position：1候选0/4096不一致，8候选988/4096不一致；所有reservoir有效。于是内部initial visibility可能针对另一candidate执行，导致visible stored light被丢弃。[官方Bridge契约](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md)要求initial保留/丢弃与复用bias-correction使用同样的visibility；用户fork有内部同名检查，但当前Unity DXC执行的returned sample不能作为可靠的选中来源。未证明具体编译器或一般SDK原因，不修改vendor正文或冒称升级。

现在DI把 `enableInitialVisibility`设0，SDK返回后由实际reservoir index/UV调用相同polymorphic sampler，执行相同保守visibility和 `StoreVisibility(...,0,true)`。保留M的discard政策、选择target/MIS和final visibility。仍是每个有效初始样本一次visibility ray，没有新GPU纹理/pass；代价是一次canonical light读取/重建。当前analytic delta域UV不改变采样位置；未以此声明未来mesh/environment域已完成。GI没有修改。

| 512次观察、薄遮挡，initial=8 | shadow raw/reference | diffuse raw总比例 | filtered受光高光比例 |
| --- | --- | --- | --- |
| 无复用 | 1.002468 | 1.000011 | 0.982341 |
| temporal-only | 1.000642 | 1.002335 | 0.927634 |
| spatial-only | 0.998263 | 0.999417 | 0.976023 |
| temporal+spatial | 1.003937 | 1.004691 | 0.987721 |

独立raw gate已通过；filtered gate仍失败于temporal-only的受光高光，原5%条件保留。`RunLocalPairShadow`现在明确分开raw/filtered消息，不再把它概括为ray失败。原无遮挡quality固定序列ratio仍0.947065，另一项5% gate也没有被删除。有限MC观察不是无偏证明。

SDK探针模板与复现说明位于 [InitialSampleProbe.md](../../../Tests~/RTDI/InitialSampleProbe.md)，只需复制到隔离Editor目录，不注册Feature；0退出代表观察完成，不是SDK contract通过。`RunLocalVisibilityAudit`保留四个真实场景隔离项。Player新增近灯被挡、Far可见、initial8/no-reuse的部署gate，D3D12 RG/compatibility实际EXE比值约0.995916/1.001791；普通序列化依赖、旧HDR/F0/点灯边界/noise通过，D3D11实际启动保持inactive。两个release构建、两条Editor raw动态灯/遮挡、Power compatibility、NRD compatibility、Roslyn与派生重建通过，没有新增本步Shader/UAV/command-buffer诊断；其他模块警告另计。Player新增采样会推进随机序列，旧统计数值变化不算严格单项消融。

完成后判断：修复了会漏掉可见灯的实际raw错误，且支持域仍为现有analytic/static opaque/Mono。下一项优先temporal-only filtered的剩余高光损失与固定quality序列，再进入曲面和目标场景。最终材质接收/合成继续保持原范围。

本轮预算复查：自有载荷仍为720p/1080p约219.921/495.637 MiB，简单RG同步batch约6.977/13.555 ms，前轮6.750/12.799 ms。含完整渲染和GPU读回，不是初始light重建的孤立GPU成本，也不以不同随机序列的单次时间代表严格消融。没有新增常驻GPU资源或ray次数。

## DI 混合 diffuse/specular 候选权重

步骤19先保留原物理输出与 NRD 配置，用 `RunLocalHistoryAudit` 追踪双灯薄遮挡 temporal-only 的真实 GPU 输出。它在原 192 轮预热后额外预热 128 轮，观察 512 轮；`RunLocalHistoryFastAudit` / `RunLocalHistoryNoClampAudit` 使用相同帧序列，仅通过真实 native settings 暂时覆盖历史参数。仅 Editor 的回调在 pipeline 4/6/9/10 dispatch 后即时复制 specular 输出，再用当前 staging/raw 比例恢复物理单位，按受光区统计。四张测试纹理只属于诊断入口，回调为空时没有 capture 分配，Player 编译移除该代码。

基线受光区 Temporal/HistoryClamping/首末 A-trous 比例为 0.936861/0.935774/0.923750/0.910389。fast history 延长至 15 或设为 main history 并关闭 acceleration/reset 后，均值提高，但相同序列的 filtered 镜面归一化 MSE 从 0.118676 增至 0.167437/0.218093，L1 从 0.235646 增至 0.273859/0.311256。关闭 virtual-motion 的实验同样更差。这些实验没有进入生产设置，不能以平均亮度替代质量判断。

现在只在 `HoRTDI.compute` 定义 `HO_RTDI_SPECULAR_PROPOSAL_IMPORTANCE=4`，target 为 luminance((Lambert*albedo + 4*GGX)*radiance)/solidAnglePdf。初始选择、时空复用与 bias correction 全部调用相同 target；最终 `RAB_SurfaceEvaluateBrdfTimesNoL` 和 Shade 仍用原物理 BRDF，reservoir 的 inverse PDF 恢复真实贡献。改变的是候选分配，不是输出增益。[官方 Bridge 契约](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md)允许近似评分；用户参考 `UnityPathTracing-6000.3.15/Assets/Shaders/RayTracing/RtxdiApplicationBridge/RAB_LightSampling.hlsl` 也注明该契约，但其现有策略仍是物理 BRDF luminance。系数 4 是本项目针对混合 lobe 的质量策略，不是 upstream 默认值或任意场景的最优证明。GI 使用共享 bridge 的原分支，实际 compatibility 回归通过。

| 相同长预热、temporal-only、512 次观察 | 基线 | 镜面 target 权重 4 |
| --- | --- | --- |
| raw specular / reference | 0.991061 | 0.989989 |
| raw specular 归一化 MSE | 20.399940 | 10.094452 |
| filtered specular 归一化 MSE | 0.118676 | 0.052974 |
| filtered specular / reference | 0.910873 | 0.936219 |
| filtered specular L1 / reference | 0.235646 | 0.173647 |
| raw diffuse 归一化 MSE | 1.168848 | 0.944692 |
| filtered diffuse 归一化 MSE | 0.011119 | 0.010298 |
| filtered diffuse / reference | 0.955380 | 0.965401 |

MSE 用解析 reference 平方和归一，包含噪声和偏差；该 fixture 中两个 lobe 的总误差都下降。新受光区 Temporal/HistoryClamping/首末 A-trous 为 0.955614/0.954932/0.946942/0.936107，thin-shadow raw/filtered 比例为 0.998797/0.983049。**更长预热匹配序列的 lit filtered/reference=0.935517，约 6.45% 偏暗，原 5% gate 仍 exit1。** 它继续作为剩余问题，不把另一序列通过当作全域验收。

原 `RunLocalPairQualityCompatibility` 固定序列现在通过：静态 raw/filtered 比例 1.003165/0.981143，filtered L1=0.078130；运动 0.992741/0.987345，L1=0.062395。原 `RunLocalPairShadow` 四模式通过，lit 比例依次 0.980064/0.953853/0.976589/0.991964。序列之间不同的随机历史会影响结果，只有匹配对照表用于这次严格单项比较。

两个 Win64 Mono release 构建与实际 D3D12 RG/compatibility EXE 通过新增的 64²混合材质部署 gate：192 预热、256 观察、central16²、initial8/no-reuse，独立 GGX/reference 下 filtered specular=0.980891、diffuse=0.969552，均满足 5%。旧初始 visibility≈0.995916、点灯/颜色/F0 边界、HDR 最差约 1.028%、noise 回归通过，D3D11 实际 EXE inactive 通过。64²部署 gate 没有证明 128² temporal-only 的失败已消失。Editor 两条核心/NRD、Power、动态灯/边界、HDR、时间线/目标尺寸以及 GI compatibility 通过；Roslyn 无错误、派生生成器 `--check` 通过，无新增本步 shader/command-buffer 诊断，其他模块既有警告另计。

没有新增常驻 GPU 资源、pass、ray 或 native 依赖；720p/1080p 自有载荷仍约 219.921/495.637 MiB。简单 RG 同步 batch 约 6.809/13.170 ms，含 CPU/完整渲染/读回，不是单项 GPU 成本。接下来优先继续量化 temporal-only 的相关噪声、history 输运和 A-trous 剩余误差，再扩大曲面与目标场景；保持最终 lilToon PBR/NPR 接收/合成的原范围。

## ReSTIR 历史样本预算与 permutation 审计

步骤20保持步骤19的候选 target、NRD 配置和物理 Shade，先在同一薄遮挡、temporal-only、192+128 预热/512 观察序列中比较 ReSTIR 历史上限。`RunLocalReservoirHistoryOneAudit` / `FourAudit` / `EightAudit` / `TwentyAudit` 显式覆盖 1/4/8/20；`RunLocalHistoryAudit` 现在测生产默认值。这里的 maxHistory 是固定 SDK `min(MaxM, maxHistoryLength * curSample.M)` 的历史样本数量上限，不是 NRD 历史帧数，也不是所选样本的最大 age。

| 旧 frame-counter permutation，ReSTIR maxHistory | raw specular 归一化 MSE | filtered specular 归一化 MSE | lit filtered/reference |
| --- | --- | --- | --- |
| 20 | 10.094452 | 0.052974 | 0.935517 |
| 8 | 4.739896 | 0.025909 | 0.969939 |
| 4 | 2.476860 | 0.015112 | 0.980582 |
| 1 | 1.239605 | 0.008352 | 0.982272 |

新诊断按每像素的 511 对相邻帧误差去均值，再汇总 covariance/variance；lag-one 分别约 -0.0042/-0.0074/0.0010/-0.0024，均接近零。它没有证明逐像素相邻噪声相关，也不能排除空间 transport、多帧 ancestry 或共享 reservoir 的相关性。表中长历史增加了该 fixture 的 raw 方差，不能据此宣布长历史在所有场景有害。

用户参考 `Assets/RTXDI/Scripts/Rtxdi/DI/ReSTIRDIContext.cs` 的 `SetFrameIndex` 与 `SetTemporalResamplingParameters` 使用 `JenkinsHash(frameIndex)`。固定 SDK `ReSTIRDIParameters.h` 要求 frame-uniform random，`ReservoirAddressing.hlsl` 只取低四位。DI 原来直接传 frameIndex，每16帧重复 permutation offset；现在使用现有 SDK `RTXDI_JenkinsHash`，所有像素仍共享一个值，不修改 vendor。单独 hash 在 maxHistory20 下 lit=0.952955，通过5%门槛，但 raw/filtered镜面 MSE=11.906299/0.072785，比旧counter更差；不把这个均值变化单独计作质量提升。

采用 hash 与较短默认历史的组合。新建 `HoRTDISettings.maxHistory=4`，Inspector 为「历史样本预算」，tooltip说明与NRD不同并建议从4开始。范围仍1..32，已有 renderer 资产的序列化20或其他值继续生效，没有静默覆盖、自动迁移或强制4；长历史可按场景显式比较。NRD main/fast帧预算与现有材质支持域均未调整。

| 同一匹配长预热 temporal-only | 步骤19（counter，20） | 新默认（hash，4） |
| --- | --- | --- |
| raw specular / reference | 0.989989 | 1.002485 |
| raw specular 归一化 MSE | 10.094452 | 2.633737 |
| filtered specular 归一化 MSE | 0.052974 | 0.016470 |
| filtered specular L1 / reference | 0.173647 | 0.095937 |
| lit filtered/reference | 0.935517 | 0.976478 |
| raw diffuse 归一化 MSE | 0.944692 | 0.167633 |
| filtered diffuse 归一化 MSE | 0.010298 | 0.005551 |
| filtered diffuse / reference | 0.965401 | 1.002761 |

当前默认的匹配 temporal-only 原5%gate通过；四模式薄遮挡的 lit 比例为 0.980064/0.976316/0.976589/0.976302，shadow raw/filtered 均通过。temporal+spatial 的镜面归一化 MSE=2.253991/0.013794、diffuse=0.192115/0.006241。没有增加生产纹理、pass、ray或NRD调度；每像素多一次现有整数hash，实际预算另测，不宣称零成本。

**保留短运动失败：** `RunLocalPairQualityCompatibility` 的原静态 filtered/reference=0.971634，64帧运动 raw=0.964139 超过3%gate，filtered=0.951544、L1=0.072589。原方法与门槛保持不变，仍exit1。独立 `RunLocalPairMotionConvergenceAudit` 用同15deg/s、同15..31度域往返512帧；其前64帧数据与原失败完全相同，128/256/512帧 raw比例约0.982864/0.991638/1.015134，512帧filtered=0.991004、L1=0.065639，通过原能量/L1门槛。更长观察说明统计结果对长度敏感，没有证明短序列已经可靠，也不代表任意场景无偏。后续须增加独立序列/曲面/目标场景，避免只围绕单个平面序列调参数。

实际 Win64 Mono release 两条 D3D12 EXE 通过新增 temporal-only 混合材质部署 gate：序列化 maxHistory 均为4，RG specular/diffuse 比例=0.971927/0.970563，compatibility=0.985094/0.967562，均满足5%。原 no-reuse 混合 gate、初始visibility、点灯/F0边界、HDR与noise继续通过；D3D11实际EXE保持inactive。旧Player noisy镜面、HDR测试的RNG序列因新增观察而改变，当前HDR最差约1.227%、filtered噪声MSE约0.0000915/0.0000907，不把它们当作严格单项消融。两个构建0 errors，packed DLL与源SHA相同。

Editor两条核心/NRD、Power、动态灯质量、边界、HDR九组与范围、30/60/120Hz时间线、目标尺寸回归通过；Roslyn无错误、派生生成器`--check`通过，无新增本步DI/NRD shader/command-buffer诊断，既有其他模块警告另计。没有修改共享GI代码。720p/1080p自有载荷仍219.921/495.637 MiB；简单RG同步batch约6.952/15.388 ms，前步6.809/13.170 ms。该fixture没有启用temporal，含CPU/完整渲染/读回，不能把时间差直接归因于hash或视为孤立GPU成本。

## 曲面接收体与 RT 请求的 SB 法线精度

步骤21新增 `RunCurved` / `RunCurvedCompatibility`，使用真实 lilToon、GB/SB、128²透视、均匀缩放的 Unity 球网格。CPU 对中央32²像素求最近 ray/triangle hit，再插值顶点法线；独立距离/范围衰减、Lambert 和 height-correlated Smith GGX 给出参考，F0=10/255、roughness来自已知量化布局。没有用 raw 输出当真值或假设网格是解析球体。192预热/256观察、roughness约0.298/0.698，单Near确定性与Near/Far的无/T/S/TS四种状态，共10组。检查真实NRD输入法线/viewZ、raw能量/单灯形状、filtered能量/L1/MSE。

原 SB Normal 使用 RGBA8 UNorm，曲面guide最大法线误差约0.5713°；viewZ最大误差仅0.000009。低roughness单灯raw/filtered镜面L1=0.042997/0.042996，归一化MSE=0.002379/0.002376，证明主要形状误差已经在输入/raw阶段。高roughness单灯raw L1=0.006555。[NRD官方输入说明](https://github.com/NVIDIA-RTX/NRD#inputs)强调法线精度影响曲率与镜面跟踪；用户参考RAB_Surface也区分shading与无normal-map的mesh normal。本步保留已有组件含义，不用GB的扰动法线替换SB。

实际入队的 RTDI/RTGI 在现有camera consumer registry中请求 `HighPrecisionSurfaceNormal`，SB按已冻结请求为原Normal附件优先选择RGBA16 UNorm，次选RGBA16F；平台都不支持则保留紧凑UNorm回退。请求不包含AC normal-copy、identity、attribute或semantic flags，AC没有因此多一份主表面副本。两条路径均以相机请求决定格式，纹理名、RG/BA oct编码、七个MRT、material标记、DI物理BRDF/采样/NRD与最终合成均未变。没有活跃RT请求时恢复紧凑格式，不让普通SB消费者承担这项精度成本。

| 两条路径同数据、低roughness单Near | 旧RGBA8 | RT请求高精度 |
| --- | --- | --- |
| normal guide最大角误差（度） | 0.571298 | 0.081565 |
| raw specular L1/reference | 0.042997 | 0.000528 |
| filtered specular L1/reference | 0.042996 | 0.000946 |
| raw specular/reference | 0.996480 | 0.999493 |
| filtered specular/reference | 0.996080 | 0.999062 |

高roughness单Near raw/filtered镜面L1约0.000364/0.000897。新低roughness双灯无/T/S/TS filtered比例约0.985215/0.995309/0.989856/0.976155，L1约0.0425/0.0597/0.0726/0.0812、归一化MSE约0.002817/0.006029/0.009381/0.012167；对应旧MSE=0.005049/0.008947/0.010782/0.012550。高roughness双灯filtered比例约0.9871..0.9879。两条路径10组均通过原2%单灯raw形状、3%raw能量、5%filtered能量、15%filtered L1及guide阈值，没有放松门槛。

测试是单独Editor partial，不注册Feature。首轮post-frame全局纹理转RenderTexture的读取方式失败，改为读取实际NRD输入guide；它来自DI真实消费的SB normal，不直接验证SB BA或任意normal-map。完成相机使用真实 `LastSnapshot`，不在渲染结束后创建新的空请求快照。兼容路径验证了启停DI时SB Normal的实际格式升降，RG验证请求清除/恢复，两者均无语义资源需求。

SB原Normal从4到8 B/像素，720p/1080p增加约3.516/7.910 MiB，不计driver/alignment；没有新增附件数量、生产pass或ray。当前0.0816°残余还包含writer的half输出与NRD guide编码，不能只按16-bit UNorm理论精度推断实际误差。两种16-bit格式均不支持的平台未证明这项精度收益。DI内部载荷仍219.921/495.637 MiB，现有预算统计不包含SB输入，应把上述增量单独计入；RG同步batch约6.896/15.109 ms，含CPU/完整渲染/读回，未证明孤立GPU速度变化。

支持边界仍是静态无normal-map、同色双灯、均匀缩放球体、中央ROI；曲面运动、轮廓、非均匀缩放、normal-map/任意网格仍待验证。原64帧平面运动raw失败没有被这项静态曲面结果替代。

两个Win64 Mono release构建0 errors，实际D3D12 RG/compatibility EXE通过新增64²正交曲面部署gate：独立mesh ray/triangle与Smith GGX，192预热/32观察，中央16²，单候选无ReSTIR复用，raw/filtered镜面L1=0.000588/0.000814，满足原2%/5%条件，且真实camera demand含高精度请求。测试球体先停用再延后销毁，恢复序列化Quad与原灯/材质；旧初始visibility、混合/temporal-only、HDR/noise/F0/点灯边界通过，D3D11实际EXE inactive通过，packed NRD DLL SHA与源一致。当前Player HDR最差约1.196%、filtered noise MSE≈0.0000992；新增曲面观察推进RNG序列，不当作单项消融。

Editor两条核心/NRD、材质/点灯边界、HDR九组与范围、动态灯、目标尺寸以及必要的GI两条路径回归通过；Roslyn无错误，派生重建`--check`通过，无新增本步DI/NRD shader/command-buffer诊断，其他模块既有警告另计。GI只补同一个SB精度请求和释放声明，没有改变GI算法。

## 曲面相机运动、独立序列与历史表示缺口

步骤22只扩展Editor验收，未改生产采样、NRD或资源布局。`RunCurvedMotion` / `RunCurvedMotionCompatibility` 使用前步球体/同色Near-Far灯，在两种roughness、单Near及双灯无/T/S/TS五种状态下测三个frame-index起点0/1024/4096，共30组/路径。每组先关闭NRD、清除DI/NRD history-valid、设定可复现frame起点，再开启NRD预热192帧；随后64帧按15deg/s、60Hz从0到16度绕视。CPU提前为每个相机姿态缓存真实网格ray/triangle位置与插值法线，观察时重新计算该姿态的V/局部灯/GGX参考。没有用上一帧或raw结果代替真值。

静态世界的NRD world-space MV保持0，相机运动由当前/上一帧矩阵描述，符合[官方运动输入契约](https://github.com/NVIDIA-RTX/NRD#non-noisy-inputs)；用户参考RAB_Surface也以current/previous g-buffer和相应矩阵读取表面。三个frame起点同时改变ReSTIR/NRD的帧序列，不是三个独立场景或物理时间轨迹；单灯确定性case的序列一致是预期行为。

| 两条路径各30组、64帧运动 | 最小 | 最大 |
| --- | --- | --- |
| raw diffuse/reference | 0.991727 | 1.008307 |
| raw specular/reference | 0.986156 | 1.016857 |
| filtered diffuse/reference | 0.979710 | 0.994401 |
| filtered specular/reference | 0.989396 | 1.011930 |
| filtered specular L1/reference | 0.024551 | 0.129224 |
| filtered specular 归一化MSE | 0.000847 | 0.025721 |

所有组通过原3%raw能量、2%单灯raw形状、5%filtered能量、15%filtered L1门槛。低roughness单灯raw L1≈0.000445、filtered≈0.034569；双灯T/TS的filtered L1约0.115..0.129，已有明显的运动形状/噪声误差，不能因gate通过宣称精确保真。两条路径数据相同，无新增本步shader/command-buffer诊断，Roslyn无错误。当前只覆盖固定球体的相机运动和中央32²ROI，未证明对象运动、轮廓、TAA、normal-map或任意网格；没有新增本步Player运动验收，前步Player只证明静态部署。

**新的待修缺口：** 静态SB升为16-bit后，DI/GI仍把两种法线各以2×8-bit oct存入16-byte主表面历史。当前`RAB_GetGBufferSurface(false)`读取SB高精度normal，previous分支读取这份8-bit历史，表面材质/位置不变时也可能给候选不同评分。`RunCurvedHistoryAudit`明确只在隔离compatibility项目、固定16-byte布局下读取实际SB Normal与GraphicsBuffer历史；以同一个CPU mesh位置、同一灯/材质计算DI现有diffuse+4*specular评分，仅改变来自两份实际数据的法线。测得normal最大差0.580135°、target L1≈0.008821、最大相对差≈0.077467。该CPU诊断不是GPU bias证明，也没有把它认定为原64帧平面失败的唯一原因。

下一项优先评审并修复当前/历史法线表示的一致性，保留现有PBR量化和资源接口；量化历史buffer布局/显存成本，再用这里的独立序列、原静态/平面失败、必要的共享GI回归及实际Player检查。不要仅因这些较宽的能量门槛通过就跳过该接口差异，也不要继续调NRD或输出增益来补它。

## 当前/历史法线的统一表示与20-byte布局

步骤23按前步实际SB/历史读回补齐接口一致性。[官方Bridge契约](https://github.com/NVIDIA-RTX/RTXDI/blob/main/Doc/RtxdiApplicationBridge.md)允许近似target，但评分规则必须一致；用户参考RAB_Surface对current/previous读取相应normal/geometric-normal纹理。我们此前当前帧从高精度SB读取，而历史只保留8-bit oct；本步不以提高输出或调整NRD消除这项差异。

新增共享 `HoRTDIPrimaryHistory.hlsl` 定义五个uint、20-byte primary history，DI/GI host buffer均使用同一stride，NRD F0历史guide同步读取新字段位置。当前bridge先把shading/mesh normal统一编码解码为2×16-bit oct，保存与previous分支使用同一codec；这样SB为16-bit UNorm或FP16回退时，两帧仍采用一致表示。depth保持32-bit，diffuse/validity和F0/roughness仍各4×8-bit，灯/SDK reservoir布局、采样预算、物理BRDF、NRD配置与最终材质合成不变。

| 每像素20-byte GPU布局 | 字节偏移 | 含义 |
| --- | --- | --- |
| depth | 0 | 原始device depth的float bits |
| shadingNormal | 4 | 两个16-bit oct坐标 |
| geometricNormal | 8 | 两个16-bit oct坐标；SB的无normal-map网格组分 |
| diffuse | 12 | RGB albedo/validity，各8-bit |
| specular | 16 | RGB F0/roughness，各8-bit |

`RunCurvedHistoryAudit`现在检查host/Marshal均为20-byte，并从真实GPU history读回两种normal word，对实际SB的16-bit坐标逐像素核对。中央1024像素0不一致；静止同灯/材质/CPU位置下的候选评分L1在F9精度下为0，最大相对差约0.000000167（约0.0000167%），旧16-byte历史为0.008821/0.077467。新增硬gate要求编码全匹配及最大score差<0.0001；它证明该数据表示差异已消除，不是全部GPU估计无偏的证明。无新增本步shader诊断，原vendor/派生NRD正文SHA检查继续通过。

历史buffer相对原布局每像素增加4 B，每个DI/GI context的720p/1080p新增约3.516/7.910 MiB；若两个Feature同相机都启用，各自历史都计入。前步SB normal附件的增量另外计算，且共享SB只算一次。没有增加纹理、pass或visibility ray；当前bridge增加normal codec运算，不宣称免费或孤立GPU速度提升。新增共享header只服务既有DI/GI历史，没有扩大PT框架或最终材质接收。

两条路径曲面运动各30组继续通过，新的raw镜面比例0.985020..1.015488、filtered镜面0.989390..1.011075、filtered L1=0.024551..0.131369。没有普遍MSE改善结论：低roughness/seed0的T-only filtered MSE从0.025721变为0.026657，TS从0.023142变为0.024181；raw分别0.462135→0.457503、0.825769→0.838535。这步修复评分输入一致性，未据单个序列声称滤波更好或消除所有偏差。静态曲面10组、两条核心/NRD、边界、HDR、Power、30/60/120Hz时间线、目标尺寸及必要的GI两条路径回归通过。原平面64帧raw比例仍0.964139，3%gate保持exit1；独立512帧raw/filtered=1.015134/0.991015、L1=0.065642，通过。

实际DI自有载荷720p/1080p为223.437/503.547 MiB，旧219.921/495.637 MiB；SB输入增量另计。简单RG同步batch约6.815/13.238 ms，含CPU/完整渲染/最终读回，不是codec的孤立GPU成本。代码/host布局一并重编，旧运行中context依正常重建处理，不把20-byte buffer绑定给旧16-byte reader。

两个Win64 Mono release构建0 errors，实际D3D12 RG/compatibility EXE和D3D11 inactive通过。Player曲面raw/filtered L1≈0.000588/0.000814，混合/仅时域两种lobe的原5%条件、初始visibility、F0/点灯边界、HDR/noise及资源重建继续通过；HDR最差约1.196%，filtered noise MSE≈0.0000992/0.0000868。不同实际执行节奏/RNG历史的数据不作严格性能或质量消融。packed NRD DLL SHA与源一致。Roslyn无错误，NRD原始SHA/派生生成器检查通过，无新增本步DI/NRD shader或command-buffer诊断；已有lilToon duplicate-keyword与其他模块C#警告另计。
