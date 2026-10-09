# Ho-RTDI：NRD RELAX 初版

Feature 的 **NRD RELAX 降噪** 为可选项，默认关闭。开启后仍保留 raw diffuse/specular；公开的 diffuse/specular 输出改为经过 RELAX 并恢复材质因子的结果。默认不写 camera color，不修改 lilToon 最终接收、CS、ShadowCast 或 GI。

## 参考与实现选择

使用用户参考中的 NRD 4.17.3 CPU 库、RELAX 原始 shader、NRD 前端 helpers 与调度描述。原参考 Denoiser.dll 同时集成 NRI、DLSS、NIS、Unity descriptor-heap hook 和设备内存控制。本步只引入重命名后的 NRD 核心 CPU 库，由它选择 pass、常量和 pool；Unity CommandBuffer 执行对应的原始 ComputeShader，所有 GPU 资源归 Unity 管理。

本地 DLL 实测为 4.17.3，normal encoding=2、roughness encoding=1。15 个 pipeline 的完整 shaderIdentifier（含宏）与生成的 Unity wrappers 逐一对齐。常量缓冲、sampler 和资源声明通过 NRD 提供的 custom-engine 宏适配，算法正文未改。33 个 shader/MathLib/config 文件和 3 个 ABI header 的原 SHA-256 与正文已核对；许可、CPU DLL hash 与出处见 ThirdParty/NRD。

参考：[NRD 官方集成说明](https://github.com/NVIDIA-RTX/NRD)，本地 NRD.hlsli / NRDDescs.h / NRDSettings.h / InstanceDesc / DispatchDesc，以及用户项目的 NrdDenoiserHelper、NrdFrameData、NrdPass。线上资料用于核对原则，实现严格按本地固定版本，而非混用新版 ABI。

## 信号契约

| 信号 | 当前处理 |
| --- | --- |
| diffuse/specular RGB | raw 仍为已有参考 BRDF 光照；用 NRD_MaterialFactors 解调后，调用 RELAX_FrontEnd_PackRadianceAndHitDist |
| signal alpha | 选中灯的距离，point/spot 为实际距离，directional 为 FP16 有限最大值；有效暗面没有选中灯时也提供有效的远距离零信号，不把 receiver-validity 写进 hit distance |
| normal/roughness | 世界着色法线与当前参考 roughness，经 NRD_FrontEnd_PackNormalAndRoughness，固定编码 2/1；使用 RGBA16F 存储编码值 |
| view Z | 正的线性眼空间深度；无效主表面写 1000000，超过 denoising range 500000 |
| motion | 静态世界的 world-space 零位移；不把相机运动混入 MV，相机重投影由实际前后 view/projection 矩阵提供 |
| filtered publication | 按同一材质 helper 恢复 modulation，RGB 保持原 DI 接收含义，alpha 重新写 receiver-validity |

没有复制当前主表面 GBuffer；现有 GB/SB 被借用，新增的是 NRD 实际要求的信号编码。NRD shader 不自行读取/重渲染场景。Native core 使用自动帧间时间跟踪，不硬编码生产帧率。

`HoRTDIRenderGraphResources.diffuse/specular` 与原兼容 globals 发布最终选择的 raw/filtered 结果；新增 rawDiffuse/rawSpecular 与 `_HoRTDIRawDiffuseTexture` / `_HoRTDIRawSpecularTexture` 始终保留 raw。Volume 的 RawDiffuse / RawSpecular 可作同帧对照，原 Receiver 枚举值保持不变。

## 调度、ABI 与历史

在已有 unsafe graphics-queue DI pass 内，ShadeNRD 写 raw 与 NRD inputs，随后执行核心库返回的 dispatch 列表，最后 ResolveNRD 恢复材质因子。pool、signal、output 和 constant buffers 都声明 RenderGraph 依赖，compatibility 使用同一调度函数。没有 Unity native graphics device、command-list 或 descriptor-heap 直接操作。

核心调用返回的指针只在本轮复制描述、资源与常量时读取，不跨下一次 GetComputeDispatches 保存裸指针。每个 dispatch 使用独立常量偏移，帧常量使用三份 64 KiB buffer，避免同轮不同 pass 共用覆盖的 CB 地址。原始 ResourceType 数值以固定头文件为准；该参考库的 GetResourceTypeString 字符串数组顺序已经落后于枚举，不能用它决定绑定。

分辨率变化、禁用/重启、DI 关键设置或世界几何 Revision 变化会重启 NRD 历史；灯参数变化交给 RELAX 的历史/antilag 处理。当前 dense-DI 预设沿用 SDK 默认设置，唯独关闭 50 px specular prepass：两条 lobe 都有有效距离，并未使用概率 lobe split；原 prepass 会抹平干净解析高光。保留 SDK temporal、history fix/clamp 与 A-trous，不另写降噪算法。默认 diffuse prepass 仍保留。

## 验证与边界

- Windows x64、Unity 6000.3.15f1、D3D12 RG / compatibility，Gamma / Linear 通过真实 GPU 验证；15 个 Unity kernel 编译、固定 DLL/ABI、原始 uniform/power DI 及 GI 回归分别验证。
- 双色点灯噪声场景中，raw 红通道 MSE 约 0.050401，filtered 约 0.000049；filtered 红/蓝均值约 0.22549/0.22642，数学参考约 0.22。
- 64² 镜面测试中，raw ROI 均值约 0.296814，默认 prepass 时约 0.222147；dense-DI 预设约 0.276015。该改进不等于无偏、无空间模糊或任意高光都保真。
- 已覆盖选中灯距离、正 viewZ、金属 diffuse、镜面信号、静态世界相机移动、透视相机、输入域拒绝、raw/filtered 调试、关闭释放与重建。无效表面最后仍输出 A=0。

Windows x64 Mono release Player 已独立验收：普通序列化 renderer 引用 DI compute 与 15 个 NRD compute，正常裁剪后实际执行 RG / compatibility，两条路径的单灯、世界遮挡、Power、独立镜面、raw/filtered、RELAX 关闭重建均通过。packed 核心 DLL SHA-256 与源 DLL 相同；D3D11 实际启动并保持 DI inactive。双色灯 Player raw MSE 约 0.050401，filtered 约 0.000066，红/蓝均值约 0.227；仍是 64² 受控场景，不能代表生产画质。验证入口与复现步骤见 [Player probe](../Tests~/Player/RTDI/README.md)。

DI 的 debug / grid-label shader 现由 Feature 序列化持有，正常裁剪后 Raw / Filtered / Receiver / All 网格在独立 EXE 的两条路径均通过。旧 renderer 的缺失引用会在 Create 或打开 Feature Inspector 时补齐并 SetDirty；请保存 renderer asset 后构建。补齐只填空位，NRD 固定库扩容保留已有 override；再次检查完整引用不会弄脏资产。空库/部分库已在三个独立 Editor 进程中验证保存与磁盘重载，没有借 AssetDatabase 的内存状态冒充部署持久化。

当前是 **静态不透明 Mono 的 Editor / Windows Player 初版**。不支持/未验证 IL2CPP、skinned 或运动几何、XR、TAA jitter 的显式 NRD 契约、相机切换/时间跳变的大规模场景、完整材质 ID 边界路由和生产 GPU budget。GB 通用 fallback / MSAA resolve 不在本次真实 lilToon、MSAA=1 接收测试内。没有添加构建时全工程批量迁移；未打开/保存的外部旧 renderer 仍应先完成正常补引用流程。DLL importer 限定 Windows x64。

新增八张信号/输出/恢复纹理合计约 60 B/像素，RELAX pool 约 80 B/像素，另有三个 64 KiB 常量 buffer。1080p 增量约 277 MiB，不含驱动/对齐；与已有 DI 资源合计约 483 MiB 以上。NRD Record 的稳定热路径已复用描述、资源表与常量 CPU 拷贝区，见下节；Feature 的扫描、Prepare、外部绑定与渲染图等仍可能分配，不能把局部零分配写成整帧零分配。

## 调度分配与分辨率预算

NRD Record 缓存固定 denoiser ID、kernel、dispatch 与资源表，直接将本轮原生常量复制到持久 int-word 区，取消每轮 byte[65536] / uint[16384] 的两份临时载荷与整区 BlockCopy。每次仍清零有效区的 padding，复制 native 描述和常量；保留每 pass 的独立 CB 偏移与三帧 GPU buffers。只上传本轮使用的 stride 区段，正常 10 个 dispatch 时约 7.5 KiB。没有跨下一次 GetComputeDispatches 解引用旧 native pointers。

在实际 Unity 6000.3.15f1 D3D12 Editor、RTX 4060、16 轮预热后记录 128 轮 SDK 调度：稳定轮 GC.Alloc 事件由 15 次降为 0 次，命令记录 CPU 平均约 54.5 μs → 11.0 μs。零分配指这一已预热 Record 段；首次创建、其他 Feature 阶段与 native 内存不在计数内。GC.GetAllocatedBytesForCurrentThread 的零值未通过可信测量审查，GC.Alloc marker 的 Value 是耗时而非字节；最终使用官方 Count 事件数，并先通过已知分配自检。移除的 128 KiB 临时常量载荷是数组大小的确定值，不伪装成字节 profiler 读数。

GPU 读回验证了延后执行的两次 partial SetBufferData 各自保存正确的 CPU 数据快照。NRD 的两条路径、噪声/镜面/相机移动/关闭重建与 release Player 回归通过，输出统计保持一致。

| 简单测试尺寸 | DI＋NRD 活动资源载荷 | RG 同步批次均摊 | compatibility 同步批次均摊 |
| --- | --- | --- | --- |
| 1280×720 | 214.646 MiB | 6.143 ms | 5.387 ms |
| 1920×1080 | 483.772 MiB | 10.770 ms | 11.333 ms |

配置为一张真实 lilToon quad、一盏点灯、local candidate=1、无 ReSTIR 时空复用、开启 RELAX。每尺寸预热 12 次，再计 8 次实际完整渲染请求与最终单像素 GPU 读回；按实际 texture 尺寸/格式及 buffer count/stride 核算活动资源。尺寸变化、真实 raw/filtered 能量、alpha 与 dispatch 均验证。载荷不包含 GB/SB、AS、相机目标、驱动对齐或 RTHandle cache；时间包含 CPU、输入生产、DI、NRD、raster 与 GPU 同步，不能当成 NRD 单独 GPU 时间或目标生产场景帧率。

复现入口：`HoRTDINrdRecordingValidation.RunBaseline` / `Run` 与 `HoRTDIValidation.RunBudget` / `RunBudgetCompatibility`，均在隔离工程执行。旧算法 baseline 必须使用优化前的源版本，否则 RunBaseline 只是记录当前版本，不能伪造前后对照。原始报告保留在隔离工程 rtdi-nrd-recording-baseline.json / optimized.json 与 budget 两条日志。参考契约为固定 NRD.h 的返回数据生命周期、[NVIDIA 集成说明](https://github.com/NVIDIA-RTX/NRD)、[Unity partial SetBufferData](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Rendering.CommandBuffer.SetBufferData.html) 与 [GC.Alloc 事件统计示例](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html)。

NRD 本身允许滤波偏差，默认 diffuse prepass 与材质因子下限仍可影响细节/暗材质。应先用 Raw / Filtered 和真实目标场景对照，不把启用开关当作最终画质验收。
