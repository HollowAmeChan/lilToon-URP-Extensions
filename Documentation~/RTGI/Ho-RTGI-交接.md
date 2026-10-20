# Ho-RTGI 当前交接

更新：2026-10-20。目标active，未完成。步骤005独立RELAX已提交95120a4；步骤006真实应用帧/空间质量与两个Mono Player已过滤提交035b9f9；步骤007材质排除已过滤提交20449c3；步骤008已提交6ebe065；009已提交4ccd02b；010古堡接入/filtered失败证据已过滤提交010b8cd，qualityQualified=false；011有效GPU timestamp/古堡分段已过滤提交63db17e；010质量仍失败，下一项012显存/分辨率/应用条件。下一项实际场景输入支持与GPU预算；不再围绕均匀夹具无限调NRD参数。

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


### 008终态（2026-10-20，已提交6ebe065）

生产：GI私有AnyHit基础alpha、Generate/FarSky去FORCE_OPAQUE，有Cutout时Shade/Temporal/Spatial的同一原SDK函数用native raygen、最终native visibility/distance→原Final/NRD。无Cutout原compute/inline+8B dummy，有时8B/像素（1080p15.82MiB）及一次额外dispatch，GPUms未测。共享仅HoRTScene.BuildAndBind(RayTracingShader)补state向量及原样HoRTSceneHit类型抽头，不改registry/DI算法。Editor-only ValidationBeforeFinal用于阶段冻结诊断，Player无该hook，冻结帧不进能量测量且清历史。SDK/vendor/NRD body参数未改。

Editor正式runner Tests~/RTGI/Cutout/run_validation.ps1，exec48828已收终态：10项全部实际exit0/编译诊断0/marker/fresh/SHA成功。两颜色空间单射线、四组full各284请求/17checks、opaque环境两路径/opaque材质/DI native Cutout。1024条CPU线段+实际meshUV+nearest对照、512遮挡无raw漏光，final/NRD距离一致。半开独立.5积分raw=.4869144(-2.62%)、filtered约.4873～.4891，原5%通过；次级方向光/天空、远挡板均过。

Editor曾被并行Dust编译错误阻断，正式用旧base82df540+124份明确覆盖的RTGIValidationPackage；snapshot.json记录，实际快照不可变逐文件SHA验证。MonoScript映射空问题仍F19，不用快照build。Player前专用工程manifest已恢复实际工作区；当前源HEAD见过6be5bd9等Atmosphere/Dust别人的提交，保留。

Player runner Dynamic/run_validation.ps1 -Cutout，exec98463终态已收：RG build2812/Player44432、compat build44960/Player26604全exit0。两个Linear D3D12 Mono release每条309请求/304有效应用帧/22checks；alpha0原动态/空间/合成过，alpha1首帧旧光0，alpha0恢复，排除最后Cutout释放full buffer/回opaque，恢复及disabled切换通过；额外serialized非空天空材质排除实际运行，补007Player证据。每个真实Update一次请求，不是正常enabled-camera自动呈现。当前Cutout/active.json、results/run.json、player-run.json均无活进程。

Cutout/verify_validation.py通过：原Editor快照、当前Runtime、源码/报告/log/GUID/完整Player payload均核对。Editor快照中的旧Editor Player builder后来为新Player扩展，与当前不同但未用于该Editor矩阵；运行时源码逐项与快照匹配。Player metadata旧step006标签保留player-run-original-label.json，canonical只修步骤编号；新runner以后日志step008，不改历史报告。旧005 snapshot/evidence未覆盖。

失败保留：AlphaMask字段在LIL_RENDER0缺声明→私有补；opaque转Cutout的fixtureTag仍Opaque→正确TransparentCutout；黑albedo/F0=0 roughGGX约.0013合法Schlick擦边→真实包全flags1/z=-.5证明命中，改1024包位置验证，不改shader/gain/遮挡阈值。F24/25记录；并行编译失败无fresh report不得假通过。Source/log/payload保留既有非GI/GB/退出GC警告，不称完整生命周期。

待过滤提交：Runtime/RTGI，Runtime/RayTracing/HoRTScene.cs与Shaders/HoRTSceneQuery.hlsl+HoRTSceneHit.hlsl/meta；Editor/RTGI新增Cutout partial/audit compute及原验证/Player扩展；Tests~/RTGI/Cutout，Dynamic runtime probe/runner和NRD snapshot runner可选metadata参数；RTGI说明/计划/过程/禁试/交接。不纳入任何Atmosphere/VolumeLight他人改动。

下一项009应做GI骨骼snapshot真实命中/UV/法线/变换/BlendShape与遮挡/反弹，再结合实际场景；有效GPU预算/正常自动camera和完整Player矩阵仍必须推进。Goal active，不能完成。先更新008提交号，再事前review，别继续堆均匀NRD调参。

008已按白名单过滤提交6ebe065，所有进程已结束、verifier通过。009尚未动手；下一轮先看当前git/index和本页最新段落，继续骨骼/实际场景与有效GPU预算，不将已完成的Cutout当整体goal完成。


### 009终态（2026-10-20，已提交4ccd02b）

没有生产Runtime变更。Editor/RTGI新增SkinnedHit和SkinnedLighting partial，既有Secondary字段升Renderer用于skin；Player builder/probe与runner加可选Skinned模式。独立矩阵测UV0/UV1、normal/geo/normal-map、骨骼混权、Morph、非均匀/负行列式镜像；真实离屏skin emission/caster、non-readable、无效姿态/恢复和稳定缓存全部过。

Editor六项正式exec73874已收终态：两颜色空间hit各140checks；四组full各187请求/25checks，全真实exit0/编译诊断0/marker/fresh/SHA成功。独立RTGIValidationSkinnedPackage快照base fa64cc6+128覆盖，保留008旧包不覆盖。源码与报告在Tests~/RTGI/Skinned。

两个Linear D3D12 Mono release Player exec79406已收终态：RG build48096/Player50096、compat build40024/Player32924，全exit0，各315请求/310有效应用帧/30checks。Bone/Morph/invalid移走首帧蓝光0、恢复重亮，skinned Cutout/材质排除/动态/合成都保持。每真实Update一请求，非自动enabled-camera。Skinned/verify_validation.py已核对全部源、不可变快照、报告、实际日志与完整payload；当前active.json和两run均无活进程。

专用GI项目manifest现回实际工作树。其他任务Atmosphere/Dust代码仍可能修改，不纳入。009待提交白名单：Editor/RTGI本步Secondary/Environment/Player与两个新partial/meta、Tests~/RTGI/Skinned，Cutout/Dynamic runner和NRD snapshot runner的可选Skinned/独立SnapshotName扩展，RTGI文档/计划。无Runtime/RTDI/vendor变动。

没有新生产故障，不反改BakeMesh(true)；F26仅记录夹具负行列式覆盖复核。稳定pose复用有效，动画每变姿态保守reset及CPU Bake成本尚需场景/性能评估，不称动态表面motion。既有构建/GB/退出GC警告保留。

下一项010转实际血月古堡场景与有效GPU记录。只读发现DI隔离工程RTInputValidation/Assets/mmd场景测试/血月古堡依赖副本；此前PID49064活着，须每次再查，不操作别人工程/进程。GI工程还没导入该场景；需在自己的项目复制依赖/必要gltf importer或导出原生fixture，源资产不写回。Read-only场景支持审核不等于画质/预算交付。

RenderDoc技能已读；本机CLI D:/Unity_Fork/renderdoc-mcp/bin/renderdoc-cli.exe可子进程PATH调用，help仅事件/资源/捕获等，无duration接口。MCP也无GPU counters。DI既有隐藏宿主自动相机0事件、GPU duration0样本与无capture的失败已读，不同条件无新证据别盲重试；更不能用CPU时间替GPU。需要准备能实际呈现/计时的具体夹具再验，goal继续active。

009已过滤提交4ccd02b，verifier再次通过，生产Runtime未修改，全部进程结束。010尚未动手，下一轮直接进入自己的GI工程中的实际场景/有效GPU取证；不要继续同类小夹具或重置目标。


### 010终态（2026-10-20，已提交010b8cd；质量明确失败）

新增Editor/RTGI/HoRTGICastleValidation.cs/meta和Tests~/RTGI/Scene。生产Runtime/Shader/NRD未改。GUID/URI闭包从DI隔离副本只读复制54文件49.94MB与私有UnityGLTF2.19.5包到GI自有工程；dependencies.json含源/目标/package SHA，运行后均未变。专用GI manifest有gltf及lilToon-gltf扩展引用，独立项目内生成缓存，没保存用户/源场景。

正式四项128/256×RG/compat接入exec77434终态已收：PID43816/50076/33092/53100全exit0/编译诊断0/marker/fresh/SHA成功，15实例、12Cutout、1skin snapshot、18灯。全16384/65536接收像素有效，15340/61390非零GI，.25合成变化11692/46793像素；稳定无额外Bake。图已实际查看，贡献较弱，不夸收益。普通camera before/quarter/one和raw/filtered PNG及GI显示曝光×100图在Scene/images，曝光不改EXR/生产数据。

质量失败不能遗忘：64额外预热/64窗RGB约低9%，实际exit1（exec34750、PID53940）。Stage审计exec79440/PID45940 actual exit1：输入红均值.001492365近raw .001492383；Prepass .001478197、Temporal .001384652、Fix .001463385、Clamp .001383324、Atrous .001353189。长窗256预热/256观察exec96346/PID50600仍(-5.95%,-6.54%,-7.03%) exit1。

仅Editor已有ValidationSettings的色盒sigma4/6两轮：exec56695/PID50444、82647/PID48324均exit1；误差(-4.41%,-5.18%,-5.68%)、(-4.13%,-4.95%,-5.42%)，仍过不了5%，边际小且噪声增。搁置更宽盒/盲history/gain，Runtime保留fastSigma2。F27/F28明确禁止改门槛/用max-channel均值或x100预览冒充能量。原版/长窗/stages/4/6五份失败报告及真实exit/SHA在Scene/rejected-runs.json。

Scene/verify_validation.py通过的含义是“四个功能接入真实通过、五个质量真实失败且源未变”，evidence.qualityQualified=false，不是绿色画质。源builderSHA和功能run是在最终源码重跑后的版本。当前所有本任务进程结束，Scene/active.json无活进程。010不接纳filtered质量，完整goal保持active。

待过滤提交仅上述Editor新工具、Tests~/RTGI/Scene及RTGI文档/计划。资产/包/EXR/log只在隔离工程，不纳入仓库；其他任务文件不碰。旧场景小夹具成功不能覆盖这个真实失败。

下一项011有效GPU取证/预算可独立推进，但必须保留当前质量缺口。RenderDoc技能已读，CLI工具本机存在但无duration接口，DI隐藏宿主计时0样本/自动camera0事件/无capture经验禁止原样盲重试。已只读核对Unity6000.3 PluginAPI IUnityGraphicsD3D12v7/v8：CommandRecordingState提供正在录制的commandList，GetFrameFence/GetNextFrameFenceValue可验证GPU完成；ConfigureEvent区分DontCare(render线程commandList)与Allow(submission线程queue)。可在自有测试工程做限定GPU timestamp桥，先空区间/已知Compute负载、queue频率/fence/新鲜tick严格校验，再挂GI阶段。不要创建新D3D12 device/NRI/DLSS，不污染产品DLL或改系统；源码/编译器已定位Unity PluginAPI与VS2022 Community D:/Microsoft Visual Studio/2022/Community。若该方法无有效GPU数据仍记录失败，不能CPU替代。正常enabled-camera呈现/用户场景漫游仍独立未验，需实际可呈现条件。

010已过滤提交010b8cd，verifier再次通过且明确qualityQualified=false，所有本任务进程结束。011限定GPU timestamp诊断尚未动手，先写事前review并严格校验GPU完成/频率/空区间/已知负载，不改目标或假报GPU数据。


### 011终态（2026-10-20，已提交63db17e）

限定GPU桥已有有效数据，不再是0 duration。源码Tests~/RTGI/GpuTiming/Native/HoGIGpuProbe.cpp，DLL只生成在GI项目Assets/Plugins/x86_64。官方Unity IUnityGraphicsD3D12v7，ConfigureEvent：初始化Allow queue线程读频率/创query，Begin/End DontCare当前commandList EndQuery，Resolve在camera外另事件；目标frame fence完成后读READBACK。无device创建/NRI/DLSS/descriptor hook，HoRTDINRD未变。初strict empty失败报告保留，empty允许有序同tick，非空Compute必须正。

Runtime自有三个文件只加UNITY_EDITOR条件hook/标记（Context/Feature/Composite），无Shader/SDK/NRD算法改变。Editor/GpuClock侧Record/Collect，Raster内只EndQuery，Resolve延后避免原生renderpass限制。Total不含Composite，AS稳定无build可0，强制frame0有真build。

正式Tests~/RTGI/GpuTiming/run_validation.ps1，exec97588已收：qualification49120，256RG51280/compat50740，512RG50460/compat54488，total-only43680，rebuild54928全exit0/编译诊断0/fresh/marker/SHA成功。40帧每marker40样本（PDF冷1），有效频率1GHz、slot唯一/fence已完成。Total median 256RG1.18784/compat1.360896，512RG3.991552/compat3.926016ms；Composite .016384/.019456/.023552/.03072ms。Total-only1.191936近全marker1.18784；AS强制frame0 2.694144ms、PDF.016384ms。p95波动与所有tick/fence在报告，不宣称稳定预算。

插件隔离配置初发现Linux/OSX默认enabled，已禁所有Standalone，只有Windows Editor/x64；模板ConfigureEditorPlugin.cs复制自有Assets/Editor执行。exec53148终态已收：配置42036 exit0，Release build28628 exit0。额外PlayerGISkinnedNoGpuProbe目录不覆盖历史，实跑PID57360 exit0、315请求，全旧Skinned/Cutout/动态/合成过，payload没有HoGIGpuProbe.dll。release-regression.json保留。TestsGpuTiming/prepare_probe.ps1可编译+配置；源码VS2022/SDK/UnityAPI/DLL hash都记录，binary不入产品。

GpuTiming/verify_validation.py已过：7真实GPU进程、known-load/fence、当前8份源码、日志/报告、原生源码/DLL与Release无probe都核对。evidence.gpuCostQualified=true且filteredQualityQualified=false。当前所有本任务进程结束，active.json/run.json无活进程。其它Ho-GD plan未跟踪文件属别人，不暂存。

待提交仅上述3 Runtime RTGI文件的Editor hook；Editor/RTGI GPUClock/Qualifier+meta、Probe.compute/meta、Castle GPU入口、Player隔离Release入口；Tests~/RTGI/GpuTiming及RTGI文档/计划。无需更改DLL/vendor/系统/其它工程。旧010场景verifier源码SHA现因合法GPU入口变化需按010历史commit用，不能覆盖新工作去满足旧verifier。

下一项012显存与实际分辨率/输入域重建、完整应用条件。512/256是整个camera变域比较，不冒称已实现全分辨率GB/SB上的半GI；Total不含GB/SB/Atmo/Forward及CPU扫描/Bake，非完整presented-frame。需要记录实际context/AS/pose资源payload并再决定优化。正常enabled-camera呈现仍未验。010原版RELAX长窗RGB约低6～7%质量失败，sigma4/6宽色盒已搁置，不重启gain/更宽sigma。Goal active，不能完成。

011已过滤提交63db17e，GPU verifier再过，全部进程结束。012尚未动手；只读预算/资源后再设计半GI输入映射与边缘重建，不把低camera分辨率对照冒称半GI交付。需继续正常自动camera/完整Player与010实际质量缺口，goal active。

### 012终态（2026-10-20，已提交ca3797f；HD失败保留）

get_goal确认原目标仍active，未新建/替换。新增Editor/RTGI/HoRTGIResourceAudit.cs/meta，Castle验证器仅扩矩形尺寸/资源入口；没有任何Runtime/Shader/NRD改动。反射读取实际buffer/RTHandle/RELAX pools/constants/scene mapping，按对象去重、实际格式/尺寸/mip/stride算payload；另读Unity AS GetSize、pose Mesh运行时统计，不混称物理VRAM。

Tests~/RTGI/Resources/run_validation.ps1实际exec42925终态已收：256 PID52360、512 PID56712实际exit0/marker/fresh/编译诊断0，40个fenced GPU样本/marker。资源28项，payload16.5877/65.1510 MiB，AS API均16.52295MiB，自有251321顶点pose Mesh Unity统计28.53864MiB。NRD79B/pixel+tile+192KiB确认。1080同布局计算513.9966MiB（reservoir191.25、NRD156.4209），只能推算，没实跑成功。

1080 RG PID44476在GPU Clock同步出现多次D3D12Fence::Wait got小于目标；核对本任务projectPath/记录PID后Stop-Process终止，真实exit−1、无fresh report；compat未执行。run.activeProcess=null，无本任务Unity进程。host-observations.json整卡约7.3～7.6/8GiB，不能证明唯一原因；未动其它应用/进程/驱动/TDR。F31禁止原条件盲重试。256/512本轮Total median24.969/117.351、p95 138.807/125.692ms，远慢于011；源码完全相同且host条件异，保留原始tick但不接纳稳定预算或算法退化结论。

初编译exit1是GraphicsBuffer.name setter-only，失败run/log/sourceSHA在rejected-compilation.json；失败源码副本.codex-research/HoRTGIResourceAudit-step012-name-getter-rejected.cs.txt。修正后两个正式实际退出0。原执行runner保留source-at-run/run_validation.ps1并对应SHA；最终runner加自身进程fence/15分钟中止、默认不再跑HD，-IncludeHd必须-HostConditionNote写具体新条件。新增控制仅PowerShell语法过，未重复GPU实跑，不冒称新证据。

Resources/verify_validation.py过：当前所有实跑C#及Runtime源码SHA、原runner归档、真实log/report/错误、28项公式/alias/NRD池/mesh、GPU fence/tick全部核对；resourceAccountingQualified=true、completed GPU samples valid，actual1080Qualified=false、stableGpuBudgetQualified=false、filteredQualityQualified=false、halfGiImplemented=false。011旧GPU verifier因Castle源码合法变化应按其commit审阅，不覆盖新代码满足旧SHA。

已完成本步事前/事后review及新半GI设计文档：参考CameraFrameStateRtxdiExtensions的render/rect/output/previous尺寸和NativeGBufferConstants/OutputBlit，不声称参考scale/DLSS已经提供Ho的全GB/SB半GI重建。DI ResolutionQuality只是整相机尺寸验证。GI当前直接借DI RAB pixel/full primary，Final depth UV/previous reconstruct/Temporal/NRD都同尺寸；2×2选2g+1时实际中心相对GI比例中心偏移.5full pixel，必须统一处理，奇数尺寸需专门证明/回退。下一项013先做独立坐标映射/历史/NRD投影小验证，再接GI私有bridge与全域深度/法线/validity重建；默认full，不只缩dispatch，不改DI公共pixel语义或用低图albedo串色。新条件/低驻留有界夹具后才恢复HD GPU资格和大场景。

待过滤提交白名单：Editor/RTGI/HoRTGIResourceAudit.cs/meta、HoRTGICastleValidation.cs；Tests~/RTGI/Resources整个目录；Documentation~/RTGI的README/过程/踩坑/交接/半分辨率设计，Documentation~/Ho-RTGI.md和计划。无Runtime/Player改动，不重复相同Player build；别人的Documentation~/计划/Ho-GD-Outline-SourceDepth-Plan.md不纳入。010原版古堡filtered能量−6～−7%、sigma4/6两轮搁置未解除；正常enabled-camera、完整Player/应用预算仍未完。Goal active，不可标complete。

012已按上述白名单过滤提交ca3797f，提交后Resources verifier再次通过；index空，仅别人的Ho-GD plan未跟踪。原始JSON/实跑C#和runner归档用局部.gitattributes保留逐字节SHA，当前C#与归档仅容许换行等价；最终runner后加保护未冒称执行。全部本任务进程结束。下一轮直接做013映射的独立实际验证，不重复HD，不把新资源账单或设计当成半GI画质/性能交付。

### 013终态（2026-10-20，已提交4950433；网格合同通过，生产半GI未接）

新增Runtime/RTGI/HoRTGIGrid.cs/meta和Shaders/HoRTGIGrid.hlsl/meta，只有可复用内部helper，未改任何既有Runtime/Shader/NRD算法，当前生产路径仍full。参数float4=(fullW,fullH,scale,offsetFullPixels)；偶数且两维>=2时requestedHalf取scale2，否则明确scale1。代表fullPixel=s×g+floor(s/2)、primaryUV=gridUV+offset/fullSize、gridUV=primaryUV−offset/fullSize，其中offset=(s−1)/2。ProjectionForGrid在Unity逻辑projection上row0/row1减row3×2offset/fullSize，再由原GL.GetGPUProjectionMatrix做RT转换；必须和NRD输入点一致。CanReuseHistory只允许inputW/H和scale完全相同。

Editor/RTGI/HoRTGIGridValidation.cs/meta与Shaders/HoRTGIGridAudit.compute/meta为真实GB/SB+Compute夹具，无GI/AS/native GPU probe/NRD过滤。CPU ViewportPointToRay对两个变换quad独立求交；GPU读取primary/device depth，并测当前规则grid投影/反投影、上次真实depth的physical/NRD重建、当前world投上次grid。12个受控RenderRequest包含full/half、移动透视/正交、offcenter、resize/scale、odd和single-row/column。不是正常enabled-camera或真实Update motion验收。

正式exec24368终态已收：RG PID37868、compat50528均actual exit0/编译诊断0/fresh/marker/SHA通过，每条18190receiver、5961history、1443foreground。world最大.001916来自8×1 full极端宽视野，half均<1e−4；normal .006913、depth .001686、primaryUV8.43e−8、gridUV3.21e−7、previous world7.15e−7、previous NRD7.54e−7、temporalUV1.48e−5。原门槛未放宽。漏NRD补偿负对照half各case最大.052～.108world error，漏primary映射3.75～4.09，证明原.002门槛检错有效；full两个负对照等价原路径。

首轮exec52335/PID52740、诊断exec86584/PID40264均actual exit1，唯一失败是最后full-moving像素18,31 GPU选foreground/CPU选back。double独立交点foreground local x−.5000122，距投影边缘约.000192px；最终只在1/256px投影轮廓带允许两个独立ray/plane候选，仍全像素位置/法线/深度原门槛，每条仅2点在带、18188点唯一候选。不是生产映射bug，也没放宽误差/跳过点。Tests~/RTGI/Grid/rejected-initial与rejected-edge-diagnostic保留失败report/run/logSHA及全部执行源码逐字节归档，edge-analysis.json/F32说明。最终source-at-run也保留所有9份执行源码与逐字节SHA；当前源码仅允许checkout换行等价，局部.gitattributes保护证据。

Tests~/RTGI/Grid/verify_validation.py已通过上述完整核对；gridContractQualified=true、productionHalfGiImplemented=false、nrdFilteringQualityQualified=false、actual1080Qualified=false。所有本任务进程结束，run.activeProcess=null；旧012 verifier仍可核对（既有源没变），010质量失败/012HD失败不改。

下一项014需要实际生产适配和重建，不能继续停留纯数学夹具：GI自己的RAB必须以GI bounds/history stride读低网格、current以helper映射full primary、previous以实际fullUV+physical inverse VP重建；GenerateSecondary也直接加载映射fullPixel。Final保存depth必须同代表fullUV，Temporal投影physical previousUV后转gridUV。NRD.Record接受ProjectionForGrid后的逻辑projection并保留其previous；camera/scene history用physical VP和同grid，尺度/尺寸变reset。保留DI公共RAB原语义，不用DI的full-pixel函数直接吃GI index。SDK空间半径明确转换GI单位以保持原屏幕像素足迹。再做全分辨率depth/normal/validity筛选与demodulated→目标full albedo重建，thin缺样定义/成本必须验；PublishedDiffuse/raw/debug/composite完整receiver输出合同不能变。生产默认full；尚不能只加half开关/缩dispatch。具体约束见半分辨率设计。

F31仍禁原HD条件盲重试；需要有界低驻留/同步夹具或具体新host条件且先恢复GPU资格。010原版RELAX古堡RGB均值−6～−7%与sigma4/6两轮搁置仍未解除，正常自动camera/完整Player/稳定预算继续。当前无新生产回归需要重复旧Player；真正接GI阶段后必须重新跑必要GI/NRD/Cutout/动态/Player门槛。

待过滤提交白名单：上述4个Runtime helper/meta、4个Editor审计/meta、Tests~/RTGI/Grid整个目录（包含失败和源归档），Documentation~/RTGI的README/过程/踩坑/交接/半分辨率设计和计划。无其他Runtime/DI/vendor/native/场景变更；别人的Ho-GD plan未跟踪不纳入。完整goal active，不能complete。

013已按白名单过滤提交4950433，Grid verifier提交后再次通过；012资源verifier亦通过。所有本任务进程结束、index空，仅别人的Ho-GD plan未跟踪。下一轮直接推进014生产GI映射与全域重建，不重复纯网格/静态小夹具。原质量/HD/自动camera/Player预算缺口继续保留，goal active。

### 014终态（2026-10-20，已提交ee962e4；half实际原型失败并撤回）

已实际做完GI半网格/全域重建实验，不是继续纯数学。临时七Runtime文件接Generate映射、GI私有current/previous RAB、Final depth/history、Temporal grid UV、NRD补偿projection、空间半径缩放；内部raw demodulated，双full输出按深度/共面/normal四邻居重建后乘full目标albedo一次。只有Editor非序列化半GI入口，默认full/Player未开放。

正式Tests~/RTGI/HalfGrid/run_validation.ps1 exec92640终态已收：RG PID10316、compat32560各169受控请求真实exit1、编译诊断0/marker/fresh/SHA成功、无fence。functionalPass=true但qualityPass=false。64²红蓝平面raw最大误差.00781，filtered full .00879/half .00928；scale切换首帧raw/NRD reset、stableTemporal、full输出/实际.25合成、65×63 odd全尺寸/释放half outputs过。实际payload full1227104B/half521556B，减少57.497%（含两full输出，不是VRAM/GPUms/1080证据）。无Cutout/Player/正常camera证明。

明确拒绝原因：一个位于偶数列x8、半个full像素宽的白色前景，30个内部有效receiver full raw≈.9921875、filtered≈.9919271，half二者全0、alpha仍valid。所有低图代表点都在后方红蓝面，正确的depth拒绝防串色同时使前景缺光；不是调权重/更宽邻域能补的。原5%/局部最小值门槛不放宽，images/rg和images/compat保存full白线→half黑线，已实际查看。F33搁置相同代表点+四邻居路线，重开必须真实full receiver缺样补偿和动态/噪声/完整成本新证据；不要继续堆平面小夹具或产品化half开关。

初轮exec63281/PID48012因夹具重复F23（spatial ReSTIR单帧当逐像素能量）在full-flat先失败raw max1.996，保留rejected-single-frame/run/report/logSHA/完整源码。修正仅fixture spatial=false，temporal/NRD保留，生产参数与5%不改；这个错误不是正式half失败根因。受控同应用帧请求/固定60Hz NRD测试钟不是实际Update motion验收。

**已过滤撤回：** 七个Runtime文件（Context/Feature/DebugPass及compute/raytrace/ApplicationBridge/Resampling）全部按e7fcb40恢复逐字节原源码，SHA与012记录完全一致；没有使用reset/restore覆盖他人改动。新Editor/RTGI/HoRTGIHalfValidation.cs及meta移出产品，精确归档rejected-editor，当前Runtime/Editor无diff，仅保留013 helper。原型全执行源码在source-at-run（含原runner）、rejected-runtime.patch对基线git apply --check过，reversion.json绑定恢复SHA/patch/8张PNG SHA。root runner仅后加缺入口拒绝，未冒称执行新保护。Unity恢复编译exec45443/PID57944 actual exit0/C#0/fence0，restored-compile.json有日志/SHA；未重复或冒称Player/完整画面回归。

HalfGrid/verify_validation.py已核对正式两条真实失败、前置夹具失败、实际资源公式/完整发布/薄前景0、精确源/图/patch、当前七文件还原及Unity编译。halfQualityQualified=false、halfProductionRetained=false，Grid/Resources旧verifier也再过。所有本任务进程结束、run.activeProcess=null、restored compile finished=true。Tests~归档约.65MB/199文件，源meta及原样JSON用局部.gitattributes保留字节，属于可review复现证据，不把拒绝代码编进产品。

**下一项015切回010实际质量：** 禁止照旧重启014原型。新待验证线索：GI Runtime/RTGI/NRD/HoRTGINrdContext.SignalScale=64；实际古堡输入微弱，NRD RGBA16 permanent/transient中的平方矩是否精度不足？本地固定NRD RELAX_TemporalAccumulation.cs.hlsl:470–471确实luma²，:618写accumulated float4；HistoryClamping:319起使用矩和moment correction。DI技术Ho-RTDI-NRD.md约243–253有亮暗moment饱和/缩放/旧history量纲实测，必须多参考。先用已有Editor ValidationDiffuseStage捕获真实Temporal/Clamp moment、同像素RGB和信号范围/量化，关联局部误差，再考虑可逆单位缩放（输入除/输出乘同尺度、历史尺度一致）。当前只是新假说，不认定根因，不把改单位当gain，也不盲改sigma/history。F27仍禁止更宽盒/补亮；010原长窗−6～−7%质量失败和五份失败报告不覆盖。

012HD/fence/稳定预算和正常自动camera/完整Player矩阵仍未完成；当前有015有意义独立工作，不算blocked。待过滤提交仅Tests~/RTGI/HalfGrid和RTGI文档（README/过程/禁试/交接/半GI设计/计划），没有Runtime/Editor/vendor/native/场景变更；别人的Ho-GD plan不纳入。Goal active，不可complete。

014已按上述白名单过滤提交ee962e4，HalfGrid verifier提交后再次通过；产品Runtime/Editor无diff，index空，仅他人Ho-GD plan未跟踪。所有进程结束。下一轮直接做015原版GI低信号moment真实取证，不重启已搁置half，不把新线索先认定根因；010/012及完整应用条件继续，goal active。
