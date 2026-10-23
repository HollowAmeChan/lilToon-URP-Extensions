# DI / GI / NRD 公共层重构

2026-10-24，用户明确提出架构解耦及内存/显存优化。当前GI研究目标处于暂停；保留047的未提交预审/PlanarReference，不混入本步骤提交。

## A01 预审

源码确认GI的C#运行时依赖DI的NRD实现、nested AnalyticLight、PrimaryHistoryStride及Cutout支持判定；DI运行时代码没有引用GI，主要问题是共享基础设施寄居DI及generic NRD与DI策略混杂。不能把目录/转发减少计成显存节省。

本步抽取中立NRD ABI/时钟/执行器；DI保留时间/稳定历史/Cutout/引导绑定策略，GI直接调用公共执行器及自身RELAX_DIFFUSE配置。公共层不引用DI/GI，也不提供默认DenseDI参数；共享灯光/历史布局/Cutout判定移到RayTracing中立层。保留Shader GUID/原native DLL名称、全部采样/降噪数值/格式与每个channel独立history/reset，避免循环依赖和额外转发链。

先冻结原Context/Native/Timing/Manifest，再以原实现与新实现对相同GPU输入逐位比较，并运行DI/GI真实小场景回归。公共执行器的resource ownership/释放和两路独立池须核查。只按已实际分配格式/尺寸核算persistent/transient/external资源，明确候选共享边界；不强合DI/GI历史，不宣称本步仅搬代码节省显存。若无可靠无损资源复用证据，不在这步更改纹理生命周期。

每步先后review、白名单提交、失败保留与交接继续。

## A01 后审

已抽出中立HoNrdContext/HoNrdNative/HoNrdTiming/资源枚举及原native插件，原插件/移动资产GUID和Shader ABI字符串不变。DI仅保留专用profile/guide策略，GI直接用公共执行器；灯光64B/primary stride20/Cutout判定移到RayTracing。当前C#运行时GI→DI类型引用归零，公共NRD不引用两Feature，采样/NRD设置/资源格式与生命周期不改。

40668最终独立对照actual0、33 GPU帧540672RGBA分量逐位同、64组DI native dispatch/资源/constants/参数逐字节同；单实例reset差异及共同reset恢复成立。40324同输入小矩阵64帧过，44072 RG/47416 compat各243请求/41checks过，28384 RG/49864 compat固定3实例各42请求/10checks过，均diagnostics0/drift0。

失败均保留：52336初编译错误；47860夹具URP未初始化；47868冻结基线误改Shader常量名造成frame0 parity；33060旧合成actual0但2条AS scratch debug拒绝；50752新fixture C#60.字面量错误。只修测试生成/初始化/语法，不放宽GPU逐位/原AS错误门槛。预先冻结原四个文件，所有实际源经726个gzip对象无损归档，14个完整run+初编译源/日志与报告齐全。

实测NRD池DI327696B、GI307216B/64²新旧同。匹配transient4112B，1080算式2081760B≈1.985MiB，非1080预算实测。本步只接纳架构解耦与内部调用迁移，显存没有下降；不能作性能优化验收。后续资源复用先审生命周期/共同primary数据/pose、再测实际收益，不合并独立history或盲降FP32。

过滤提交只纳本步Runtime/Editor类型迁移、新公共层/插件移动、架构文档/三处入口链接和RenderingArchitecture证据；原047未提交过程预审/PlanarReference保留，不改其它任务代码。提交后记录实际hash和复核。

补充Release验证：13960构建actual0/PASS；原始通用runner因3条Shader warning拒绝，逐行比对既有Backbuffer/build-warning-review的历史白名单后确认全为原有SolarHD与lilToon重复keyword，无新增诊断，原run保留diagnostics3。42752隐藏D3D12/Linear Mono Player actual0、304 distinct-Update camera请求/16check全过，运行时Shader/AS/fence/动态链接错误0，NRD运行正常；打包后的HoRTDINRD.dll字节与公共目录原库一致。该项证明公共插件迁移后的Release打包/加载，不扩称自动enabled-camera或复杂Player矩阵完成。

可复现run_player.ps1已实际重跑47316，第二次隐藏Release同样actual0/304请求/16check、运行时diagnostics0；两次均未使用可见窗口授权。
