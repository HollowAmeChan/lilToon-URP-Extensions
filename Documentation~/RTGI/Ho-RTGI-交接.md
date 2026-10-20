# Ho-RTGI 当前交接

更新：2026-10-20。目标active，未完成。步骤005独立RELAX已提交95120a4；步骤006真实应用帧/空间质量与两个Mono Player已过滤提交035b9f9；步骤007材质排除已过滤提交20449c3；步骤008基础Cutout完整链路已通过Editor/Player并复审，待过滤提交。下一项实际场景输入支持与GPU预算；不再围绕均匀夹具无限调NRD参数。

## 接手顺序与硬性要求

先读本页，再读[实施规划](../计划/Ho-RTGI-v0.1-实施规划.md)、[禁试](Ho-RTGI-踩坑与禁止重复尝试.md)、[过程](Ho-RTGI-过程记录.md)。用户要求：每步事前review→实现→真实验证→复审正确性/收益/退化/成本→过程/踩坑/交接→过滤提交。长期无收益路线搁置，写重开条件；保留失败、不放宽门槛跑绿，不凭局部结果完成整个goal。goal objective已引用规划，工具无法直接改objective，不能虚假完成重建。

若UI看似目标消失先get_goal；2026-10-19已确认status=active。目标是一跳diffuse GI，沿本机UnityPathTracing成熟链路；不扩成通用PT、多跳、镜面GI、SSS或DLSS。不得派生子agent，除非用户或适用AGENTS明确要求。

## 工作区与提交边界

- D:/Unity_Fork/lilToon-URP-Extensions，共享main。自有提交000 7ece0fe、001 6e90d21、002 f6278de、003 a0a89f6、004 82df540、005 95120a4。
- 当前HEAD见过a525c36，另含并行VolumeLight76210e7/a525c36；不是GI提交。当前Atmosphere/VolumeLight仍有他人未提交工作，每次重新看git/index，禁止git add .、回退或纳入它们。
- 006自有未提交：Editor/RTGI/HoRTGIPlayerValidation.cs及meta、Tests~/RTGI/Dynamic、Ho-RTGI说明、RTGI过程/禁试/交接/README与实施规划。无本步Runtime产品算法改动。精确白名单过滤提交，可用commit --only保护并行index。
- 没改用户场景。所有fixture/序列化资产/Player在专用工程；不要占用并行DI的RTInputValidation。

## 工程、进程与源码来源

- Unity C:/Program Files/Unity/Hub/Editor/6000.3.15f1/Editor/Unity.exe，RTX4060，D3D12；本任务专用.codex-research/RTGIValidation。
- 专用工程manifest目前指向实际D:/Unity_Fork/lilToon-URP-Extensions。005测试曾用RTGIValidationPackage隔离包（base82df540+95份GI/API源码），006发现其MonoScript映射空，强制重导入一次无收益，已搁置F19。当前实际工作树脚本映射正常。不要再把旧快照当实时源码或原样build。
- 005的snapshot.json曾被同步脚本覆盖，现已从95120a4还原精确原CRLF字节/SHA；NRD/verify_validation.py仍通过。006旧快照在Dynamic/snapshot*.json只保留失败来源；正式006来源是head a525c36+明确helper/probe SHA，GI生产仍是95120a4。
- 当前本任务没有活Unity/Player进程。正式006 exec session72224终态已收取：RG build27636/Player47792、compat build26056/Player41744全部exit0。Dynamic/formal-run.json和active.json均activeProcess=null。005同样全部结束。不要据旧PID或丢失tool session重启。
- 检查Win32_Process时只输出PID/项目路径，不输出完整命令行（其他用户Unity可能带登录token）。Start-Process隐藏；同项目一次一进程。失败进程只在核对PID和绝对EXE路径后结束。
- 本机参考可运行副本.codex-research/UnityPathTracing-6000.3.15，native源码.codex-research/UnityPathTracing/RenderingPlugin；进程PATH加参考Assets/Plugins/x86_64和Packages/top.kuanmi.native-rendering/Plugins/x86_64。沿已有NRI修复，不改系统/重建DLSS。

## 已交付与关键证据

001/002参考实跑与共同diffuse域：Ho无复用/复用均过原5%，独立墙面积分48²/96²差<.01%；参考仍偏暗，overallPass=false、hoQualityPass=true。失败保留F12，禁止反调Ho gain贴参考。参考IOR1实际F0仍约.04；Ho Gamma-tagged Reflectance需LinearToGammaSpace(.04)，读取实际packet判断。见Tests~/RTGI/Reference/CommonDiffuse。

003次级天空：私有128² PDF/16384 RIS，默认主表面直接天空归DI、secondaryEnvironmentSamples=4；stored DI reservoir先重建真正样本再visibility，有限bounce范围不能当无限天空可见，远挡板验证已过。11项实际exit0，见Environment。

004正常相机加法：默认composite=false/intensity=.25，Volume覆盖；已含接收BRDF/albedo，不重复emission/主色，Gamma线性相加、fog只衰减新光，透明前合成。四组62请求/33checks+两回归全过，见Composite。DI联合夹具必须显式Volume enable，不只是SetActive。

005独立原版RELAX_DIFFUSE：type10，15个DIFF-only vendor wrapper、6 permanent/4 transient池；各相机独立实例/guide/history。共享DI ABI/dispatch只加可选类型/manifest/settings，DI默认不变且RunDenoise回归通过。乘albedo前直接生成信号，成对除/乘64、真实GI距离/normal/viewZ，raw保留并新增RawDiffuse发布/调试。denoise默认false，可Volume覆盖。约79B/像素+tile+192KiB，1080p新增约156.4MiB。

005正式四组Linear/Gamma×RG/compat各243请求/41checks，三项回归，全7进程exit0；均值约+1.1%，逐像素时间方差.1141096→.00000390791。HDR1000、零albedo、首暗帧、camera矩阵、两相机、开关/resize均过。源码/报告SHA与图在Tests~/RTGI/NRD；不宣称任意HDR/复杂场景/GPU预算。

006正式两个Linear D3D12 Win64 Mono Player：各304次camera request、299条有效应用帧记录、16checks。每个真实Update只一次SubmitRenderRequest，frameCount/unscaledTime严格递增；**不是enabled-camera自动呈现**。静态红蓝离屏emission：空间时间均值图L1约10.35%，红/蓝均值约-3%过原5%，方差比约.00107（降约99.89%）；绿近黑按绝对<.001。camera移动59帧保留矩阵历史，移除red首帧raw/filtered=0，几何移动24帧全reset，Volume/denoise启停通过；实际raster+filtered合成误差约.000073。完整payload树/source SHA与帧记录见Dynamic/evidence.json，verify_validation.py通过。两个EXE bootstrap相同不是payload相同。

## 失败与限制，不能遗忘

- 006初始快照m_Script=0/布局错，Player Feature=null；清理二次NRE挂住。核对后结束PID34808，真实exit=-1，报告保留，已修null清理/首依赖检查和构建前MonoScript强制门槛。单次重导入无收益，F19搁置其唯一根因，实际工作区可继续。
- 白色unlit raster不能假定1（实际.2158203），用未合成实际帧作基准；disabled-camera请求要沿DI helper显式VolumeManager.Update。两次误报均修夹具，未改产品gain/Volume；报告按SHA去重并有historical-report-aliases.json映射。
- 005曾有33位GUID/缺using（修正）、快照CRLF触发旧ShadowCast parser（只快照归一化）、DI错误PASS marker（换已有RunDenoise严格门槛）。部分历史同名log覆盖，旧SHA不能验当前同名文件；报告/capture仍保留，后续日志有时间戳。
- 006构建三条非GI警告：SolarHD D3D11除零、lilToon两条重复keyword。实际Player还报GB fallback/resolve shader不可达、退出四条GraphicsBuffer GC清理警告。fixture已有有效lilToon primary，但不是全部GB支持域/完整生命周期通过。
- 尚无正常自动camera呈现、复杂薄墙/小高亮源、GI Cutout/骨骼专门验收、目标场景GPU分段/显存预算、Gamma/D3D11 Player等完整矩阵。几何变更每帧保守reset影响动态收敛，不能当动态表面motion已做。

## 下一动作

1. 006已提交035b9f9。007沿DI第80步，给GI独立excludedRayMaterials并传共享registry，专门Merged子网格实际验证。Cutout只有registry支持不够：inline CULL_NON_OPAQUE与secondary/最终native可见性需完整适配，本步不接。
2. 007先review实际场景输入支持，读DI的天空壳排除/基础Cutout/骨骼路径和GI现有材料/AS合同，确认实际缺口；沿成熟参考适配，不复制DI私有NRD参数。
3. 后续必须补目标场景、有效GPU样本/分段与显存、正常自动相机和完整Player矩阵。沿DI已知隐藏宿主自动camera失败经验，不能把手动请求换名完成该项。
4. 每步保留原门槛和失败，复审收益/退化/成本；无收益路线两轮内搁置并写重开条件。完整goal继续active。


### 007终态补充

新增GI独立excludedRayMaterials（默认空）并接已有scene.Synchronize第三参、高级Inspector。只按Material身份过滤world submesh，不改GB/SB主输入/raster，和DI列表独立。改动在Runtime/RTGI Settings/Feature、Editor Inspector及Composite专门partial入口；没有共享Scene/DI/HLSL变更。

Tests~/RTGI/Composite/run_validation.ps1 -MaterialExclusion：exec session99694终态已收取；四组各12请求/21checks，加空列表原Composite和Gamma NRD compat，共6实际exit0、编译诊断0、marker/fresh/SHA核对通过，当前无本任务活进程。正式run在Tests~/RTGI/MaterialExclusion/results/run.json，active.json已清。只排合并壳后raw 0→约(1,0,.001)，filtered和camera同步增加；恢复首帧归零，同名引用/源资产/identity/等价历史/排除skin不Bake都验证。

首轮Spatial开启时单像素.928被误当解析1，exit1与report保留；夹具隔离空间复用、保留temporal/NRD，.03未放宽，产品采样/增益未改。F23禁止该误用。F22说明Cutout只有registry predicate不足：shared inline跳过nonopaque，而GI secondary/secondary-light/final需一致真实alpha路径；本步没接、不冒称支持。

006已提交035b9f9；当前007待过滤提交白名单为以上GI五个改动源码+新增partial/meta、Composite runner、MaterialExclusion证据/RTGI文档。其他模块仍保留。007新增非空GI Player列表与完整古堡/GPU未验。

下一项008在完整原生Cutout路径/实际骨骼命中和有效GPU预算之间，先核对代码与场景优先级；不得半接Cutout或把hidden手动请求冒充自动camera。完整目标active。

008事前review已写过程。当前尚未实现/运行；路线为GI any-hit + Generate/FarSky去FORCE_OPAQUE，有Cutout时native次级照明与最终visibility/distance，opaque仍原compute/inline。private payload输入sentinel只控制visibility-fastpath，不改公开输出结构/secondary48B；必须三处alpha一致后验收，不半接predicate。


### 008终态（2026-10-20，待过滤提交）

生产：GI私有AnyHit基础alpha、Generate/FarSky去FORCE_OPAQUE，有Cutout时Shade/Temporal/Spatial的同一原SDK函数用native raygen、最终native visibility/distance→原Final/NRD。无Cutout原compute/inline+8B dummy，有时8B/像素（1080p15.82MiB）及一次额外dispatch，GPUms未测。共享仅HoRTScene.BuildAndBind(RayTracingShader)补state向量及原样HoRTSceneHit类型抽头，不改registry/DI算法。Editor-only ValidationBeforeFinal用于阶段冻结诊断，Player无该hook，冻结帧不进能量测量且清历史。SDK/vendor/NRD body参数未改。

Editor正式runner Tests~/RTGI/Cutout/run_validation.ps1，exec48828已收终态：10项全部实际exit0/编译诊断0/marker/fresh/SHA成功。两颜色空间单射线、四组full各284请求/17checks、opaque环境两路径/opaque材质/DI native Cutout。1024条CPU线段+实际meshUV+nearest对照、512遮挡无raw漏光，final/NRD距离一致。半开独立.5积分raw=.4869144(-2.62%)、filtered约.4873～.4891，原5%通过；次级方向光/天空、远挡板均过。

Editor曾被并行Dust编译错误阻断，正式用旧base82df540+124份明确覆盖的RTGIValidationPackage；snapshot.json记录，实际快照不可变逐文件SHA验证。MonoScript映射空问题仍F19，不用快照build。Player前专用工程manifest已恢复实际工作区；当前源HEAD见过6be5bd9等Atmosphere/Dust别人的提交，保留。

Player runner Dynamic/run_validation.ps1 -Cutout，exec98463终态已收：RG build2812/Player44432、compat build44960/Player26604全exit0。两个Linear D3D12 Mono release每条309请求/304有效应用帧/22checks；alpha0原动态/空间/合成过，alpha1首帧旧光0，alpha0恢复，排除最后Cutout释放full buffer/回opaque，恢复及disabled切换通过；额外serialized非空天空材质排除实际运行，补007Player证据。每个真实Update一次请求，不是正常enabled-camera自动呈现。当前Cutout/active.json、results/run.json、player-run.json均无活进程。

Cutout/verify_validation.py通过：原Editor快照、当前Runtime、源码/报告/log/GUID/完整Player payload均核对。Editor快照中的旧Editor Player builder后来为新Player扩展，与当前不同但未用于该Editor矩阵；运行时源码逐项与快照匹配。Player metadata旧step006标签保留player-run-original-label.json，canonical只修步骤编号；新runner以后日志step008，不改历史报告。旧005 snapshot/evidence未覆盖。

失败保留：AlphaMask字段在LIL_RENDER0缺声明→私有补；opaque转Cutout的fixtureTag仍Opaque→正确TransparentCutout；黑albedo/F0=0 roughGGX约.0013合法Schlick擦边→真实包全flags1/z=-.5证明命中，改1024包位置验证，不改shader/gain/遮挡阈值。F24/25记录；并行编译失败无fresh report不得假通过。Source/log/payload保留既有非GI/GB/退出GC警告，不称完整生命周期。

待过滤提交：Runtime/RTGI，Runtime/RayTracing/HoRTScene.cs与Shaders/HoRTSceneQuery.hlsl+HoRTSceneHit.hlsl/meta；Editor/RTGI新增Cutout partial/audit compute及原验证/Player扩展；Tests~/RTGI/Cutout，Dynamic runtime probe/runner和NRD snapshot runner可选metadata参数；RTGI说明/计划/过程/禁试/交接。不纳入任何Atmosphere/VolumeLight他人改动。

下一项009应做GI骨骼snapshot真实命中/UV/法线/变换/BlendShape与遮挡/反弹，再结合实际场景；有效GPU预算/正常自动camera和完整Player矩阵仍必须推进。Goal active，不能完成。先更新008提交号，再事前review，别继续堆均匀NRD调参。
