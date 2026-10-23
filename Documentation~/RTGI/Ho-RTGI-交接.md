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

### 015终态（2026-10-20，已提交419e4ef；矩精度实证，RGB仍失败）

没有半GI复活。新增Editor/RTGI/HoRTGIMomentAudit.cs/meta，Castle仅加moment/实际绑定入口与数据。每阶段对stride4同receiver采1048576样本，分物理luma档、记录m2=0/mean²亏损/饱和、最差12个同像素input/raw/filtered/stage。Runtime仅HoRTGINrdContext增加UNITY_EDITOR ValidationSignalScale/冻结AppliedSignalScale及变单位清NRD历史，Feature resolve改读同AppliedSignalScale；Player getter返回原常量64，原Shader/NRD参数/DI/vendor/native/资源没变，不声称新Player回归。

实际三组256额外预热/256观察：baseline64 exec1794/PID42056，scale1 exec39224/PID46844，scale.125 exec44355/PID37392均Unity实际exit1、编译诊断0/fresh、moment audit完整、原5%失败。三组256条raw frame RGB均值逐条完全相同。RGB误差依次(-5.94572,-6.53538,-7.02845)% / (-6.21986,-5.31728,-4.69941)% / (-6.16697,-5.25227,-4.60308)%。滤波方差2.3066e−8→1.4943e−8→1.4836e−8，有矩/方差收益但不能掩盖红/绿失败。

真正slow Temporal4/Clamping6：基线正均值m2=0约98.458/98.519%，scale1 .704/.729%，.125 .003154/.003154%；Temporal明显m2<mean²188211→69685→90（已允许.5最小half单位舍入），每阶段1048576样本。基线平均物理m2≈7.208e−5低于平均mean²≈8.624e−5；scale1 m2≈2.320e−4、mean²≈8.638e−5。该采样网格未见饱和，不外推HDR1000/完整全域安全。换尺度还改变epsilon/舍入相对大小，不能认定矩是全部/唯一根因。两个单位试验后停止继续缩scale，F35禁止.0625/.015625/按通道调或默认改1；生产仍64。

**重要口径错误及更正：** 初baseline把HistoryFix5 gOut_Diff alpha也称slow moment，实际错误。真实DLL绑定探针exec99994/PID16272 actual exit0：Temporal slow transient0/fast1，Fix读0、条件写1；Clamping slow读0、fast1，slow写permanent0/fast1。Fix稳定早退后留下fast alpha0，不能聚合slow统计。baseline报告/源中的5错误标签保留，baseline-interpretation.json明确排除；新helper只认4/6。PrePass hitT与Atrous variance也不认m2。F34记录，不再只凭float4正文/函数名判断。

scale1原runner marker错写HoGICastle，没匹配原失败异常，脚本也exit1；scale1-original/run.json保留marker=false。报告fresh/audit完成、原异常/实际exit俱全，verifier独立核对，不把false改true。后续marker已修正完成.125/绑定。root runner当前只跑小64²绑定探针，历史metadata在baseline-original/scale1-original/scale0125-original/bindings-original，所有执行源码逐字节归档，DLL只hash不复制。results三份质量失败和绑定pass均保留。

Moments/verify_validation.py通过：4个实际进程/源码/log/report，三组相同raw、4/6统计与bins/局部物理换算、真实slow/fast别名、三组原5%失败、默认64/fastSigma2。旧资源/HalfGrid verifier当前Runtime/Builder SHA因合法诊断变更过时，按对应历史commit核对，不回滚本步迎合旧hash。所有本任务进程结束，所有run activeProcess=null。其它Ho-GD plan仍未跟踪，不纳入。

**下一项016：** 暂不直接改NRD派生或生产信号域。先在同一长窗口保留原stride4 offset(0,0)统计，再补full图、16个stride4相位及局部raw→Temporal/Clamping/Atrous→filtered空间误差；已有ReadPixels拿完整数组，额外统计不需新GPU产品pass。可比较原64与已测.125单位以分开precision影响，但不能再试新单位数值或挑相位。若全图也偏暗，沿DI的Ho-RTDI-DiffuseConvexClamping.md/第82～84步看局部裁剪证据后做有界GI私有候选；不先假定其能解决。若发现相位偏差，也必须保留旧失败并有科学口径证明，不用更方便指标静默替代原5%。NRD大范围HDR/动态/history单位安全、010质量/012HD/正常enabled-camera/完整Player预算仍需后续交付。Goal active，不complete也不blocked。

待过滤提交仅Runtime/RTGI/NRD/HoRTGINrdContext.cs与HoRTGIRendererFeature.cs诊断hook，Editor Castle/MomentAudit/meta，Tests~/RTGI/Moments，RTGI README/过程/禁试/交接/计划。没有产品scale默认更改、half、Shader/DI/vendor/native/用户场景变更。每步review/过滤/失败/交接约束继续。

015已按白名单过滤提交419e4ef，Moments verifier提交后再过；index空，仅他人Ho-GD plan未跟踪，全部本任务进程结束。下一轮直接进入016完整图/16相位与局部阶段取证，原stride0与质量门槛不删，不再缩单位或改生产默认。Goal active，未complete。

### 016终态（2026-10-20，已提交c8a83dc；全图仍失败，DI候选拒绝）

新增Editor/RTGI/HoRTGISpatialAudit.cs/meta；Castle加两个完整域审计入口/报告，Primary嵌套20B struct仅改internal供Editor helper访问。Runtime/Shader/DI/vendor/native没有修改；默认/Player仍64。CPU读取已有full数组累积raw/filtered每像素时均/方差、全图、全部16相位、7个阶段RGB及最大损失/增加各12点；不增加产品GPU pass。HistoryFix5明确fast/条件Fix RGB，未再算slow moment。

Tests~/RTGI/Spatial/run_validation.ps1 exec36028已收：原64 PID56596、已测.125 PID8564各32初warm+256settle+256窗，实际exit1/编译诊断0/marker/fresh/完整audit，无fence。full65536像素×256=16777216样本；16相位各1048576。原相位(0,0)与015一致，全图原64 RGB(-7.24767,-8.41522,-8.77551)%、.125(-7.56407,-7.34002,-6.54992)%，所有相位也全失败，不能挑相位或换统计静默抹掉旧5%失败。full时均L1 .191213/.211314，filtered方差2.788e−8/1.774e−8。

全图原64红均值raw/input .001547174/.001547158，Pre .001533215，Temporal slow .001469922，fast/条件Fix .001523014，Clamp .001468388，末Atrous .001435536，published .001435040。热点(87,165) raw红均值.0247237、input/prepass近同，Temporal约.011862、最后约.00363～.00372；raw方差.0025700而filtered约3e−5，有限窗raw很不稳定。定位了阶段/空间分布，但还无独立高样本真值，不把所有差值直接解释为真实物理能量偏差。

**DI候选已实际执行并拒绝：** 参考DI技术Ho-RTDI-DiffuseConvexClamping.md/第82～84及Editor/RTDI/HoRTDIDiffuseSegmentValidation.cs。只从original NRD HistoryClamping替换一个diffuse轴向clamp块为同YCoCg box fast→slow线段交点；没复制DI其它private策略，box/sigma/历史/acceleration/reset/moment correction不动。自有GI项目Assets/HoRTGIClampingCandidate生成独立body/wrapper；当前GI context.core.shaders[6]临时替换，kernel0/15项库/compile消息核对，finally restoredOriginal=true。

候选exec62237/PID37456实际exit1/compile0/fresh/完整audit，原相位(-6.9120,-5.7901,-4.9390)%、full(-8.3104,-7.8915,-6.9069)%、L1 .213782均劣于.125对照，Clamping负分量计数352→4不是质量成功。三组full raw256帧均值逐条完全相同。sourceSHA9CDAC4A0810C72FC2DA936B8BBB8633A9F27881B6930E5375D3F3B8E008E40FC，生成body5AF5AFADC1A1359E812673FCC4BDD2241182336C582D179C63B891580F519D1A。F36拒绝其作为本场景能量修复，不否认DI保色适用域；没有新的GI颜色平面/独立真值/完整收益证据不重试。

**过滤状态：** 候选新Editor helper/meta及Castle candidate入口均移出产品，精确源归档convex-original/source与candidate；生成body/wrapper、DI参考本体、原报告/SHA保留。Castle按baseline-original源码精确恢复，仅保留全域审计；root runner恢复只跑两个原64/.125审计。candidate-review.json绑定恢复源码及拒绝原因。过滤后Unity编译exec17362/PID41164 actual exit0、C#0/fence0；未冒称新Player/完整渲染回归。Spatial verifier过：三真实exit1、均值/样本守恒、旧相位、raw逐帧不变、局部/阶段、候选单块/生成SHA/恢复/编译。Runtime零diff，所有进程结束；旧Moments verifier因合法Castle扩展当前SHA过时，按419e4ef核对，不覆盖新源满足旧hash。

**下一项017（先独立参考，不盲改滤波）：** 先读现有Tests~/RTGI/Reference、对应Editor验证、本机UnityPathTracing GI/BrdfRayTracing及ShadeSecondarySurfaces。对016最强损失点和安静控制点建立同full256 primary/material/light/sky/Cutout的一跳漫反射高样本无时间/空间复用估计，分独立批次看收敛/不确定性。不要把高方差256窗raw当真值，也不要以少数点通过替代完整场景验收；原5%失败留着。

可行实现方向（尚未实施）：Editor专用参考Feature在相同full相机GB/SB后读取primary，独立Context把(x=sampleID,y=receiverID)展开为4096×16/32样本；包装原GI shader前先include原Primary header，再定义MappedPrimaryLoader用receiverID查full pixel，再macro仅替换随后GI源码中的HoRTTryLoadPrimarySurface调用。这样保留原一跳Generate/secondary SDK RIS/true alpha/final，跳过Temporal/Spatial，不新造通用PT/多跳。Generate和RAB current都必须用映射；Final history的UV按样本域本来不对但参考历史不读，不能拿它的depth作primary一致性证明。另测实际RAB world/normal/material vs full primary，确认灯activeCount=18/sky/exclusion/AS/参数一致，再出样本统计。小独立GPU buffers，先1批再递增批次，不重开1080/大camera/fence条件；同GPU fence错误仍中止自身任务。

推荐复用既有HoRTGIShadingCommon：ShadeSecondary直接写reservoir1，所以无复用参考只需GenerateSecondary→ShadeSecondaryNative→TraceFinalVisibility（有Cutout）→包装Final compute，initial flags0/2/black事件仍算样本；不复制材质BRDF/样本权重。为Reference compute显式绑定primary，raygen先Bind同full globals。新Feature用HoRTPrimarySurfaceInputs.Read/scene.Read声明RG依赖，借同一场景/18活灯/已生成环境PDF/RIS（不能把lights容量32当18，尾部未知）；批次更换随机seed，注意共享RIS表相关性，按批次判断收敛而非假定全iid。也可先选择更简单已有Reference探针扩展，但必须保持同支持域/不改变RNG原始主结果。所有这些仅是下一步设计，不是已证明的参考。

017后再据真值决定GI采样还是过滤改动；015矩精度虽确有问题但纯单位已F35停止，F27宽sigma/history/gain、F31HD、F33half、F36本候选继续搁置。正常enabled-camera/完整Player预算仍未交付，goal active不complete。待过滤提交仅Editor SpatialAudit/meta+Castle、Tests~/RTGI/Spatial、RTGI README/过程/禁试/交接/计划；他人Ho-GD plan未跟踪不纳入。

016已按白名单过滤提交c8a83dc，Spatial verifier提交后再过；Runtime零diff、index空、全部本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮直接017同域高样本无复用一跳参考及收敛，先读旧Reference/本机RTX，不重复已拒绝候选或新单位调参。所有原质量/HD/应用条件未完成项继续，goal active。

### 017终态（2026-10-21，已提交5456782；修复owned-skin AS越界，参考仍有边界）

真实参考过程中发现比滤波更前置的问题。两长批device removal后，官方`-force-d3d12-debug`小批3432虽exit0，实际14条BuildRaytracingAccelerationStructure vertex buffer范围越界；旧regex只匹配“D3D12 ERROR”漏“validation”，debugErrors=0被明确拒绝，原参考结果撤销真值资格。日志887a0006且not out of Local memory，不能甩锅VRAM或认定HUNG唯一原因。只在本任务GI项目启用debug，无驱动/TDR/其它进程操作。

CPU layout：古堡1_mesh为SkinnedMeshRenderer，251321顶点、position stream0 stride40，VB10052840B；15submesh中sky0排除，其余14均非零baseVertex、局部indices。driver声明跨度10052812=(N−1)×40+12却从base偏移地址开始，超出placed resource。新Runtime/RayTracing/HoRTRayMeshIndices.CanonicalizeOwned在原HoRTSkinnedMesh.BakeMesh(true)后，仅自有可读pose上重写absolute indices、base0；保留submesh/topology/bounds、顶点/UV/normal/tangent/source/raster。UInt16能容纳就保留，否则UInt32。Unity会重算firstVertex为实际min，不称返回first全0；修复靠base地址归零。稳定pose不重复Bake；变化pose有O(index) CPU/临时数组，当前724416 indices约2.90MB，尚无动画CPU收益结论。

同场景debug13720/最终29476均actual exit0/API0，14→0；CPU canonical-layout28044证明所有vertex stream SHA与absolute三角元组不变、14处规范化。新共享非零base场景RG12436/compat58844各145checks，GI native UV/normal/tangent/mixed bone/Morph57560共141checks全exit0/validation0；unused UV poison确保错误index也不会侥幸过。原source保base4/局部indices，owned snapshot base0/absolute，非readable源/稳定缓存/生命周期均过。

Player新增BuildSkinnedBaseVertex/Compatibility，独立Assets/RTGIPlayerProbeBaseVertex与输出PlayerGISkinnedBaseVertex/PlayerGISkinnedCompatibilityBaseVertex，不覆盖旧payload。两Win64 Mono Release build54680/45800、实际Player52168/58604全部exit0，各315请求/35checks；三个serialized nonreadable source保base4、真实owned snapshot base0/absolute，原动态/Cutout/启停/合成过。完整178/177 payload SHA在player-payloads.json，无HoGIGpuProbe.dll；仍每真实Update一次SubmitRequest，不称正常enabled-camera呈现。

**参考工具/源：** 新Editor/RTGI/HoRTGIDenseReferenceFeature/Audit及Shaders/HoRTGIDensePrimary.hlsl、DenseReference.raytrace/compute，Castle加诊断入口。先原Primary header声明，再macro仅映射后续GI primary loader到full pixel；原Generate/secondary SDK RIS/native alpha final visibility/Final，跳过GI T/S/NRD，输出FP32。场景/AS/main history/18活灯（容量32非18）与sky/RIS借同full256相机，main采样不改。probe同时匹配full/主history/compute RAB/raygen world/normal/albedo，ray-compute误差0；scene/light/sky signature与几何/normal/albedo采样期稳定。它共用材质/light实现，是同模型高样本参考，不是独立绝对着色真值或全图验收。

初始化早于URP resource pool导致NullRef、include pragma没导出Final分别真实exit1，精确源/日志保留rejected-early-allocation/rejected-missing-kernel。root compute显式Final/Probe/use_dxc/target6/inlineRT，Raytrace显式depth1。初小512×2 functional通过；旧AS参考16批4096仅1/16收敛，旧512×2048一度14/16但后来API错误暴露后均不接纳真值。原4096×256同帧长循环和旧AS512×16384跨tick长批分别device removal−1/无终态报告；后者仅terminal=false1024批检查点，不能当完整结果。known-load恢复57032有24个有效fence/tick样本，恢复小负载≠大场景保证。

为控制调度/资源生命周期，Castle私有Run改async void，仅denseScheduled分支await NextEditorTick；非scheduled旧入口依旧同步无await路径。actual frame/renderedFrame递增、有非终态检查点，不是普通camera自动绘制。移除了旧同步长循环入口；当前root runner仅小debug复核。发生非零exit留下空锁，只在独占打开成功证明无人持有后清该单个本任务项目锁；未清活锁/操作其它工程。Get-CimInstance本机一度无输出退出1，改用项目锁独占探测；不要因查询失败重启未知进程或输出完整带token命令行。

**合法AS后数据：** post-fix full21152实际exit1仅原5%失败，debug0；fullRGB仍(-7.2477,-8.4152,-8.7755)%与修复前数值相同，不能由AS修复宣称图像门槛通过。fixed-reference44048实际exit1/functional true/debug0，每点512×2048=1048576，14/16统计收敛；fixed-controls29832再512×16384=8388608样本，约438.5sec无API/fence/device错误，(240,32)收敛，(16,16) green-SE2.316%仍超预设2%。合并仅15/16选点收敛，最后暗点保留不确定，不改标准/无限采。

合法数据示例(87,165) ref红.001500、原256窗raw16.48倍、filtered2.48倍；(185,180) filtered约ref.49倍。有过亮/过暗，有限窗raw不能一律当真值，不能整体补gain。选点含事后最大损失/增加点，存在选择偏差，不推断全图resampling偏差。收敛用跨独立seed批次SE≤2%及前后半均值差≤5%（暗零另有绝对判据、非黑albedo要求足够positive样本）；共享RIS表有批内相关性，不拿所有ray当iid。原5%与所有失败不覆盖。

**证据/状态：** Tests~/RTGI/DenseReference有全部attempt run、结果、device/validation审查、更正、CPU/Editor/Player/payload；source-index.json→source-blobs按SHA保留所有精确版本，去重原source目录，native DLL只hash不复制。player-source单独保留实际build代码。verify_validation.py已过：14→0、精确vertex/index语义、145×2/141、两315请求Player、合法AS后15/16及未解全图。所有本任务进程结束、run/player-run active=null；他人Ho-GD plan仍未跟踪不纳入。旧014/015/016当前source SHA因合法改动过时，按各自commit核对，不能回滚新工作去迎合旧verifier。

**下一项018优先安全覆盖：静态MeshRenderer非零baseVertex尚未修复！** 本场景唯一static Sphere base0，不能泛化owned-skin修复到一般static。沿现有scene ownership做自有canonical静态镜像，原raster/source不动，保留绝对三角元组/全部vertex attrs/material/submesh与identity合同；可读源可clone，非readable源要核对实际GPU只读回拷/创建镜像路径，不能靠Editor-only读取或跳过几何制造漏光。先最小多submesh/base fixture、debug层0、native UV/normal/Cutout、非readable Player与生命周期/成本，再接真实scene。不要只加拒绝predicate冒称支持或修改源mesh。可重用HoRTRayMeshIndices但只对owned/readable mesh调用。

随后用合法AS/同primary匹配/多个独立seed评估main ReSTIR T/S/none模式对已收敛选点的误差，区分有限窗方差与系统偏差；不只围着固定seed0的raw256窗调filter。先看现有M0与本机RTX，不直接抄混合lobe FinalMIS：参考GetGIMISWeight含primary spec/rough BRDF，纯diffuse下会退化为.125 fresh混合，域不同必须推导/验证，非自动修复。018后再推进M2/HDR/moment、正常automatic-camera、合法AS后1080/完整预算（F31新条件须记录），未完成goal不能complete。每步review/过滤/失败/交接继续。

017待过滤白名单：Runtime/RayTracing/HoRTRayMeshIndices.cs/meta与HoRTSkinnedMesh.cs；Editor GI Dense/mesh-layout及wrapper/meta、Castle/SkinnedHit/Player扩展，Editor RayTracing场景测试；Tests~/RTGI/DenseReference和Dynamic/HoRTGIRuntimeProbe.cs；RTGI说明/计划/README/过程/禁试/交接。无DI算法/NRD Shader/vendor/native/源场景改动，索引规范化共享服务会影响DI需保留上述回归证据。

017已按白名单过滤提交5456782，DenseReference verifier提交后再次通过；index空，仅他人Ho-GD plan未跟踪，全部本任务进程结束。下一轮直接018静态nonzero baseVertex安全镜像/只读源，不重复旧非法AS参考或无上限采样。既有5%、moment/HDR、正常camera/1080与完整预算仍未完成，goal active不complete。

### 018终态（2026-10-21，待过滤提交）：静态候选搁置，原生增长复现

本轮不要从上一段“下一项018”重新实现mirror！它已真实完成候选、157功能checks，并因D3D12 scratch错误拒绝。原Ho无mirror共享/独立mesh增长亦各API1；初始5 API0。Static、AS重建、FastBuild/MinimizeMemory、跨tick、跳过新增update均无收益且撤回；F39禁止原样重试。不是新的画质/性能收益，static非零base支持仍未修复。

Editor/RayTracing/HoRTAccelerationGrowthProbe是保留的独立Editor最小复现，直接Unity API，无HoRTScene/GI。root runner实际PID19372初始5/build1 exit0/debug0，41084先3后5/build2 exit0/debug1，范围2560 B，严格runner实际exit1。FastBuild旧对照2432 B，其余2560 B。这里只证明API调用序列可复现，不证明具体引擎/驱动根因，不证明ray-hit正确。不要在下一轮继续无界追scratch唯一原因。

产品Runtime的HoRTScene/HoRTRayMeshIndices与共享HoRTSceneValidation仅自有修改按6ed9b41恢复，当前这三文件无git diff；017owned-skin修复保留。new mirror/partial/copyProbe移出产品，rejected-candidate.patch可review/apply但未接纳。Tests~/RTGI/StaticMesh保留15个实际进程、138个去重精确源、完整日志/SHA、review和恢复核对，约2.06MB；source-files/index映射旧归档路径，raw报告pass只是功能完成，早期runner移除debug guard的结果均拒绝。capability报告原run元数据缺失已明示，不能编造出处。verifier通过仅指拒绝/恢复证据自洽。所有本任务进程结束、activeProcess=null；独占锁保护继续，无他人进程操作。没有新Player/完整画面验收。

**下一项019可直接做：** 本机RTX GI/BrdfRayTracing、ShadeSecondarySurfaces和M0已有模式合同先复读；在017合法AS/同full256 primary/18灯/sky/Cutout古堡条件下，建立有限预算的多个独立seed main raw T/S/none模式误差对照。优先已收敛的15个参考receiver；暗(16,16)green SE2.316%保留不确定且不无限加样。选点有事后挑选偏差，必须记录，不替代全图5%。采样与filtered逐点/全图统计分开，先判断窗口方差与系统偏差再决定改哪层。可先仅Editor诊断，不先改NRD、默认gain或MIS权重。

原全图RGB(-7.25,-8.42,-8.78)%、moment/HDR/正常enabled-camera/1080与完整预算继续未完成；F27/31/33/35/36/37/39继续。当前目标工具查到active，objective引用实施规划内六项用户强制要求，不能为重写字符串虚假完成/重建目标。每步review→实证→收益复盘→禁试/交接→白名单提交继续。

018提交白名单：Editor/RayTracing/HoRTAccelerationGrowthProbe.cs/meta；Tests~/RTGI/StaticMesh；RTGI README/过程/禁试/交接/实施规划。别人的Ho-GD-Outline-SourceDepth-Plan.md仍未跟踪，不纳入。下一轮先看本节最新提交状态，不重跑旧15个进程凑次数。

018已按白名单过滤提交`ad900e7`，StaticMesh verifier提交后再次通过。生产Runtime/共享场景测试无diff、index空、全部本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮直接019合法AS古堡的独立seed/模式误差对照，不重复F39。目标仍active，未complete。

### 019终态（2026-10-21，待过滤提交）：完整seed矩阵定位TS偏移

已完成019，不要重复18增长调参或重跑19凑样本。新Editor/RTGI/HoRTGISeedAudit.cs/meta及Castle仅Editor接入口，Runtime/Shader/SDK/NRD/Player未改。RG古堡256²/18灯15instance12cutout/sky与017合法AS相同；none/T/S/TS×seed{0,104729,1153305,2201881,3250457,4299033,5347609,6396185}，每段64warm+256观测。最终PID6104真实exit0，debug/compile/source drift0，32段10240请求，报告10274含初始32及两次末尾合成检查。每段full/原phase0、16点、方差和32帧块均值完整；actual frame/T与域断言过。最终同seed四模式端点selected secondary及完整RIS逐字节匹配，primary error约1.9e−6。仅RG请求相机，无新compat/Player/正常enabled-camera/GPU成本结论。

none full rawRGB(.001228918,.000130884,.000120137)；TS(.001544720,.000159953,.000142659)，TS相对none +25.698/+22.209/+18.747%，配对差SE相对none约.418/.354/.333%。T或S单独约+2.4～3.0%，TS组合偏移远大于seed间波动。所有32段filtered/raw原5%仍失败；TS filtered比none raw仍高15.70/11.58/8.08%，不能只因NRD使其下降便加gain。点(128,128)红TS/reference=2.1819±.1456，filtered/reference=1.7208；T raw=.9707±.0739，none=.9202±.1111。±仅一个经验seed-SE；参考仍15/16收敛、暗点不确定、事后选点偏差与共用模型边界均保留，不是全图绝对真值。

**失败原样保留：** 首资格45524实际1，8段byte gate失败但无字段，不补写原因。加字段资格11552实际0。原完整矩阵43444实际1，32段捕获后byte gate失败：504组position小差（hit最大1.22177e−5，100000单位假miss端点.008789）、18组normal差1 oct16步，flags/material/emission/PDF/geometric/RIS全同。仅后置检查改用原world .0005、miss unit direction/FP32半径和normal量化步，所有primary/5%不变。最终重跑6104实际0且恰好端点bitwise相同，不把比较器变更说成GPU差异根因。F40及paired-contract-correction.patch说明边界，旧失败不改绿。

Tests~/RTGI/Seeds：四真实attempt metadata、完整日志、239精确source hash（97引用017 blob）、analysis.py/json、capture-archives.json及gzip原始JSON（解压逐字节/SHA不变）约3.44MB；verifier已过，包含raw/filtered/full/phase/block/primary/source/15参考与4进程。大报告读取需gzip，不要Get-Content -Head打印单行百万token JSON；用Python解析只选字段。所有本任务进程结束，run.activeProcess=null，未操作他人工程/进程。旧DenseReference verifier当前Castle SHA因合法诊断变更过时，按5456782核对，不回滚新源迎合旧hash。

**下一项020具体动作：先Editor私有pre-spatial history候选。** 已核对本机Assets/RTXDI/Scripts/Rtxdi/GI/ReSTIRGIContext.cs:234起，参考TS也把Spatial结果反馈至下帧，与Ho当前1→history0语义相同，不能称已发现索引集成错误。019数据支持有界尝试：只改Final history写0时取buffer2 Temporal结果；当前帧仍读1进行S后的最终可见性、求值和NRD，T/S不关闭。必须在Final写，不能Temporal边读history0边写而产生跨线程竞态。没有新reservoir allocation，但会多32 B/receiver读取，要量成本。

可在自有Unity项目Assets生成原HoRTGI.compute独立body（include改绝对package路径以保持原义），只替换Final那一行store的value；保留所有kernels/pragmas，reflection暂换gi私有compute shader，finally恢复。不要动raytrace/SDK/源Shader/默认参数，不同时改NRD单位或Jacobian。先小资格核对非TS不受影响/实际kernel/原还原，再按相同8seed配对baseline/candidate TS看raw与参考/filtered/方差/空间细节/成本；不要仅最有利点或更方便门槛。若有收益再生产及RG/compat/Cutout/动态/Player回归，无收益就撤回记禁试。原质量/static非零base F39/moment-HDR/正常camera/1080/完整预算仍缺，goal active。

019提交白名单：Editor/RTGI/HoRTGISeedAudit.cs/meta与HoRTGICastleValidation.cs；Tests~/RTGI/Seeds；RTGI README/过程/禁试/交接/实施规划。其它Ho-GD plan仍未跟踪，不纳入。每步review、实证、收益复盘、过滤提交、失败禁令/交接继续。

019已按白名单过滤提交`4019bd7`，Seeds verifier提交后再次通过；Runtime无diff、index空、四个本任务进程均已结束，仅他人Ho-GD plan未跟踪。下一轮直接020仅Editor的pre-spatial history候选，不重跑19矩阵凑样本；原5%/static/HDR/正常camera/1080/完整预算继续未完成，goal active。

### 020终态（2026-10-21，待过滤提交）：候选收益已证，下一轮直接生产接入

020已做完，不要重跑纯诊断或继续codec猜测。新增Editor/RTGI/HoRTGIHistoryFeedbackAudit.cs/meta，Castle加入口，SeedAudit.Snapshot增加显式selected-modes合同（默认四模式不变）；Runtime/Shader/SDK/NRD/Player均未改。参考packed Spatial反馈与Ho额外repack两变量拆legacy/packed Spatial1→0/packed Temporal2→0。自有Assets/HoRTGIHistoryFeedback的三compute，includes等义规范化/只替换一处Final历史store；8kernel、真实pass绑定与finally原shader重绑定过。

质量：40296资格24段三项×四模式×2seed×16warm/16观察，总803请求；10500矩阵24段三项×TS×8原seed×64warm/256观察，总7715请求。四进程（含30436 GPU资格/35936成本）全actual exit0/debug0/compile0/source drift0。96端点×65536=6291456 packet检查无差，三个policy同seed首尾secondary/RIS全bitwise同；legacy本身也exact packed Spatial，因此codec假说在此域拒绝，不能认定偏亮根因。none/T/S小资格逐帧full与选点publication三项全同。

**实测收益：** pre-S raw相对019 none全图RGB+7.73/+6.77/+6.28%（原+25.70/+22.21/+18.75）；filtered相对none+1.22/−1.87/−3.28%（原+15.70/+11.58/+8.08）。raw/filtered平均时间方差约−9.17%/−55.04%；15收敛选点45分量非加权相对L1 raw .41785→.13950，filtered .65422→.46454（选择偏差/暗分量放大，非全图指标）。(128,128)红raw/ref2.1819→1.1333、filtered1.7208→1.0944；(185,180)filtered仍仅.520。全24段full/phase0原5%均失败、15/16参考与暗点不确定仍保留，不冒称全面画质修复。

成本：known-load24样本empty/128/2048 median0/.006144/.039936ms合格；3项×40帧×12marker=1440完整fence/slot/freq区间。legacy/pre-S Final median .027648/.046080ms（+.018432）；GI.Total1.043456/1.018880ms，p95大且顺序固定，不称总性能提升。无新reservoir，多另一buffer32B/receiver读取（非DRAM/VRAM实测），无1080/新Player证明。已有Editor sidecar SHA/meta绑定，无产品native依赖变化。

证据Tests~/RTGI/HistoryFeedback约7.03MB：四run精确日志/源；246source hash中234共用既有blob；generated源码/单行patch；gzip原JSON（解压逐字节SHA原样）；12张2048观察帧/policy raw/filtered时均EXR与×100PNG及image SHA；analysis/verify均过。已实际查看原/候选filtered预览，不能当正常相机合成或全场景真值。capture DTO沿用019的旧protocol说明，矩阵仅TS，actual mode字段/24段范围为准。大JSON用Python+gzip只取需要字段。当前原版结果逐帧与019 TS完全一致。旧Seeds当前source SHA因合法Editor扩展过时，用4019bd7历史核对，不回滚新源。

**021直接开始生产实施：** 改Runtime/RTGI/Shaders/HoRTGI.compute的HoGIFinal末尾历史写：S flag开时原样copy packed buffer2（Temporal结果）到0，S关保留原RTXDI_StoreGIReservoir(reservoir,...0)，不引入未测HDR codec变化。当前帧读1、native final visibility、NRD/T/S参数和3-buffer allocation不变。必须Final后写，不能在Temporal读0时并行写0。无产品实现细节开关。候选当前S关复制1与旧store在此域相同，但生产仍保留旧store更窄变更。

接着实际生产history源合同断言+原GI核心/环境/材质/Cutout/蒙皮RG/compat回归；动态响应与两独立Win64 Mono Release Player，必须新增S开启的新历史路径，不能只跑旧为了F23而S关闭的解析夹具。沿017 Player Build/RuntimeProbe扩展，给本轮独立输出/资产前缀，不覆盖旧payload；验证新增shader真实包含、nonreadable/skinned/Cutout原合同、无HoGIGpuProbe.dll。生产最终source绑定到实际验证，性能可作256局部证据而不宣布完整预算。若有硬退化则撤回；原5%/HDR/正常enabled-camera/static F39/1080与完整预算继续，goal active。

**工具警告：** 020 Generate要求原HoRTGI.compute里单个legacy store，改生产后它会拒绝；不可原样调用错标baseline。旧020证据用本步历史源码/生成compute重现，后续若继续用该工具必须显式支持新默认与legacy反事实，所有SHA重记，不覆盖此轮结果。当前全部四进程结束、run active=null、Runtime无diff。白名单仅新Editor helper/meta、Castle/SeedAudit、Tests~/RTGI/HistoryFeedback及RTGI五文档；他人Ho-GD plan未跟踪不纳入。每步review/过滤/收益/禁试/交接继续。

020已按白名单过滤提交`3b6937e`，HistoryFeedback verifier提交后再次通过；Runtime无diff、index空、四个本任务进程均已结束，仅他人Ho-GD plan未跟踪。下一轮直接021生产pre-S packed history与实际S开路径/RG/compat/动态/两Release Player回归，不再重复20隔离矩阵；旧质量与预算缺口继续，goal active。

### 021终态（2026-10-21，待过滤提交）：生产pre-S已接入，后续转剩余质量

021已实际实施，不要回到020“待接入”。Runtime唯一diff为HoRTGI.compute Final：S开packed Temporal2→history0，S关原StoreGIReservoir；当前Frame用Spatial1/final visibility/NRD不变，无新allocation/参数/SDK/产品开关。Final后写避免T读写0竞态。新ProductionHistoryAudit直接绑定Runtime，19188/30080 RG/compat各226请求/8全viewport checks，S开0=2且Spatial1与2实际不同约5.8～6.5万包，exit0/API0/compile0/drift0。环境双路径各138帧23checks、Cutout双路径各284帧17checks、蒙皮base/UV/normal/Morph141checks均过debug0。

**核心未全过：** 原23244/40360 actual1为旧夹具误读history0验证SpatialM，改读实际1、M>1不变后12884/48892功能全过actual0，但各两条2304B scratch越界，严格runner均1。2260实际绑定020冻结legacy Final也同样两条（BOUND marker/源SHA），不是这次history引入，具体根因未知；不得把core列为全面通过。F39旧AS调参保持搁置。源码/原失败/修正/legacy全保留。Legacy control不回滚生产，只在独立fixture用冻结compute。

**两个Release Player过：** 新独立Assets/RTGIPlayerProbeHistoryFeedback及Compatibility，输出PlayerGISkinnedHistoryFeedback/CompatibilityHistoryFeedback；旧017输出不覆盖。RG成功build17284被新wrapper因5历史warning拒绝，017两build原log/SHA证明逐字同五条后仅精确行白名单（无其它模块改动），源未变复用RG原成功build；compat47996新build。诊断5保留，不称0。实际Player11072/36020 exit0/debug0/runtime diagnostics0/source drift0，各315真实Update请求/310记录/38checks、23状态94208 packet0=2；sample52实际T且1/2不同。原nonreadable base4/三canonical snapshot、红蓝非均匀场/原15%L1/降噪方差/59相机重投影/24几何reset、Volume/NRD/Cutout/骨骼/Morph/invalid pose/合成全通过。L1约.10792/.10794、variance ratio.001053/.001064。完整payload SHA，无HoGIGpuProbe.dll。仍请求式，不是普通enabled-camera。旧Save通用PNG文件名被compat覆盖RG，只归档compat两图，数据两份完整。

**实际生产质量仍失败：** 47984真实exit1，仅旧5%异常；API/compile/drift0，256观察/full/16phase及stage完整。rawRGB(.001328435,.000140335,.000128173)、filtered(.001250704,.000129084,.000116730)，filtered相对019有限none+1.773/−1.375/−2.835%，保留020收益；但原filtered/raw−5.851/−8.017/−8.927%，16phase全失败，不删门槛。当前slow stage红input .00132840、Pre .00133402、Temporal4 .00127925、Clamp6 .00127787、lastAtrous .00125113、publish .00125070。5仍条件fast不作m2，参考15/16/选择偏差不解除。

Tests~/RTGI/ProductionHistory已核对：run.json合同；rejected-old-spatial-reader原7项/两失败；core-corrected/legacy两debug不合格；rejected-build-warnings原成功build被拒；player真实运行与完整payload；quality真实失败。19完整logs含017warning对照，1191source SHA（240旧blob、897 reachable基线Git blob+LF/CRLF精确重建、54新blob），不把无关未改Runtime重新复制入包。大报告gzip逐字节/SHA原样，约5.48MB，analysis/verify过只代表证据自洽。全部本任务进程结束/active=null。他人Ho-GD plan仍未跟踪不纳入。

**022下一动作：** 当前生产Stage表已给出余损，不再重跑20全矩阵或宽sigma/gain/纯单位扫描。先复读015 moment实际绑定（Temporal slow transient0→Clamp slow permanent0，HistoryFix条件fast）、当前Native manifest和所有moment/variance读写者，核对默认64低信号范围，再提出一个GI私有、HDR/历史语义明确的精度候选；先Editor真实stage/全域/局部验证且记录资源与成本，再决定是否生产。不能因某选点或moment恢复宣布完整画质通过。原5%、HDR/局部、normal enabled-camera、1080/完整预算与static/F39都未完成，goal active。

F39只读可比较本机UnityRtxdiFeature.Automatic/direct Build与Ho manual mesh config/cmd Build，NativeRtxdiGPUScene.AddInstance是NativeRender别名，不能误称同一个Unity API。无新因果/API/分配机制不要重启旧flags/延时/AS重建矩阵。当前有其它实质质量工作，不blocked。正常camera应读DI原记录后再定新可测机制，不能请求式冒称自动帧。

021白名单：Runtime/RTGI/Shaders/HoRTGI.compute；Editor ProductionHistoryAudit/meta、Castle/HistoryFeedback.Equal可见性/PlayerBuilder/CoreValidation读点与legacycontrol；Tests~/RTGI/Dynamic/HoRTGIRuntimeProbe.cs；Tests~/RTGI/ProductionHistory；RTGI说明/计划/README/过程/禁试/交接。后续旧019/020 verifier current SHA过时要用各commit历史，不回滚生产。每步review/收益/过滤/禁试/交接继续。

021已按白名单过滤提交`a93d4dd`，ProductionHistory verifier提交后再次通过（明确保留core debug与原5%失败）。实际生产Shader改动已保留、两315请求Release Player通过；index空、所有本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮022从当前生产NRD stage/真实moment绑定处理余损，不重复20隔离或F39旧调参；正常camera/HDR/局部/static/1080完整预算继续，goal active。

### 022终态（2026-10-21，待过滤提交）：NRD池32F候选，下一步HDR/动态/成本

022已完成两组正式比较，不重跑纯精度矩阵。Runtime/RTDI/NRD/HoRTDINrdContext仅增加UNITY_EDITOR的ValidationPoolFormat及AllocatePool调用，默认null；只在当前GI core实例设置，DI/Player生产行为不变。GI格式默认仍16F、scale64，021pre-S仍生效。新Editor/RTGI/HoRTGIPoolPrecisionAudit及Shaders/HoRTGIPrecisionCopy.compute/meta，Castle加scoped策略/报告与float32 stage capture。所有RGBA16F pool才改32F，其它池与external signal/output16F不变，原NRD/epsilon/settings不动。

正式baseline37676实际1原5%失败、rgba32 26252实际0原stride0过；API/compile/binding/drift0，各256settle/256观察+32warm，两组256 raw frameRGB逐条相同。4池permanent0/1、transient0/1；真实Native绑定全记录。Temporal/Clamp每阶段1048576样本，positive m2=0由98.9772/98.9967%变.025995/.025996%，32F各仍有256例，网格未见饱和，不称全修好/HDR安全。fullRGB error(-5.8515,-8.0171,-8.9279)%→(-4.5586,-5.2075,-4.9433)%，fullgreen仍失败、16phase8失败，不能stride0换门槛；filtered方差1.261775e−8→5.534688e−9，约−56.14%。NRD池2818304→4915456 B，+2097152=2MiB@256；1080同布局+63.28125MiB是投影，非VRAM/实际HD/成本。没有新HDR/动态/Player证据。

**两个夹具陷阱/全部原样：** 47656资格实际1因设置策略后尚未Prepare；补仅资格一次render，48792实际0，64 tiny float components bitcopy/4池32F过。9360/24456首长实际1/0但捕获目标没UAV，Property错误被旧Shader-error regex漏计，所有stage/moment为0，明确拒绝为精度证据；publication变化不等于capture有效。改enableRandomWrite、Copy前guard、runner绑定错误检测，重新执行37676/26252获非零真实moment。F43记录，原counter0/源/报告不改绿。

Tests~/RTGI/PoolPrecision约.98MB/六进程、精确源与完整log、原JSON gzip、pool/binding、analysis/verify。250source SHA仅9新blob，其余共用旧blob/Git-backed，verifier过包含原错误和full失败。当前所有本任务进程结束、run active=null，仅他人Ho-GD plan未跟踪。旧021 verifier当前CastIe/core SHA因合法Editor hook过时，按a93d4dd历史验证，不回滚新工具。

**023下一动作要具体：** 候选有收益但先补HDR/格式生命周期/成本/动态资格，再决定生产。可给shared NRD core增加实例级构造参数（默认false，DI原格式不动），GI才显式启用32F；Editor ValidationPoolFormat若设定，应从native原格式构建baseline优先覆盖，避免新默认后baseline误变32F。当前typed float4 Shader可读32F已实测，不需要改Native DLL或NRD Shader。公共signal/output保持原16F和单位64，避免未知HDR单位变化。

建议先解析发光平面，S关+T开以免F23单像素MC误判，低信号1e−4至20000物理值（/64后312.5，m2可>65504）实测有限/正确能量/slow m2超half而不溢出，明确所测HDR范围而非任意HDR。默认TS另验实际pool格式+pre-S packet合同/材质与相机变化reset。再resolution64→96→64/启停/释放、已知负载GPU资格与两政策40sample成本，两个独立Release Player应加四pool32F与动态/23history检查，不只Editor setter。验证硬退化或成本不合格则撤回，保留2MiB@256额外payload与未实际1080边界。

Fullgreen/其它8phase/局部质量仍缺；F35不重启纯scale试值/gain/宽sigma，F39不重试旧AS参数；normal enabled-camera、HDR最终域、1080/完整预算继续，goal active。候选若通过上述资格再接生产，不能因stride0绿色complete。每步review/收益/过滤提交/禁试/交接继续。

022白名单：Runtime/RTDI/NRD/HoRTDINrdContext.cs仅Editor hook；Editor Castle/PoolPrecisionAudit/meta与float copy shader/meta；Tests~/RTGI/PoolPrecision；RTGI README/过程/禁试/交接/实施规划。无DI生产策略/Shader/Native/Player/用户场景更改。

022已按白名单过滤提交`189fcd9`，PoolPrecision verifier提交后再次通过；生产池格式/单位仍16F/64，021pre-S保留，index空，六个本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮023直接HDR/生命周期/GPU成本/真实Player资格，再决定GI-only高精度默认，不重跑纯格式/单位矩阵；fullgreen/其它phase/局部与所有原缺口继续，goal active。

### 023终态（2026-10-21～22，待过滤提交）：GI32颜色链生产策略，转正常camera证据

023已验证并保留新策略：HoRTDINrdContext optional highPrecisionColorPool默认false，GI ctor显式true，所以DI默认16F不变；Editor override接收native format优先作baseline。四GI颜色池32F，再把GI共用PrePass/final output改32F；signal/normal/physical filtered原格式、单位64、原NRD/Shader/参数不变。pool-only真实低1e−4失败促成output-only有界后续试验，有收益才保留，没有继续扩其它格式。阶段捕获自动用production float32 copy，不开启selector。

**最终资格：** 38732 RG与36228 compat三个解析发光范围1e−4/1/20000原5%全部通过，变化首帧reset，slow m2低2.388e−12、高97653>half上限仍有限；default core control四half、GI四32F、96→64/停用handle全release/重建/二camera与最终32尺寸原NRD检查过。22实际进程log各有真实exit，不捏造范围外HDR。原夹具48476/17312 Color.white×amp缩alpha导致发光再乘alpha失败；显式alpha1后34708/12596低signal仍真失败，47580/37696延迟失败收全HDR/lifecycle仍1，13764 legacy16低control也失败。所有原源/结果保留，不抬阈值。pool-only数据冻结在pool-only，不当最终output32证明。

**真实默认最终Player：** build42680/43124、Player2932/17740 all0/debug0/runtime diagnostics0/drift0，新PrecisionOutput资产/输出避免覆盖pool-only旧PrecisionPool。两315真实Update、39checks、23pool+23history，实际flagtrue/四32F/无half颜色池/signal16/output32，270off旧handle release；原nonreadable/base4/canonical3skin、Cutout/骨骼/Morph/invalidPose/能量/方差/相机/几何/Volume/NRD/合成门槛继续。空间L1 .105689/.105668<.15，无GPU探针DLL，完整payload SHA。pool-only6252/3340记录及payload亦保留。两backend截图独立保存。仍SubmitRequest，不能算自动camera。

**实际默认全图：** 16612长窗actual0/stride0过、API/compile/drift0，但fullRGB−4.49450/−5.05869/−4.72749%，7phase失败。不能只因fullgreen差.05869就调阈值或宣布complete。保留21/22/23原失败、局部与有限MC/选点不确定。当前阶段资料可作后续独立seed/局部诊断，不无限重跑固定窗口。

**资源/成本：** +四pool2MiB+output.5MiB=2.5MiB@256，1080+79.10156MiB仅payload投影，无物理VRAM/实际HD。32200 GPUclock资格有效；43588/19896 pool-only计时虽有效却极不稳（Total47/38ms、NRDp95 25/13ms），事后快照GPU100%/6296MiB仅观察，不归因/干预其它进程。最终8372实际40帧Total median1.042432/p95 1.891328ms、NRD .129024/.131072ms，无跨负载性能提升结论，稳定目标/Player预算未合格，F31保持。

证据Tests~/RTGI/PrecisionQualification约6.14MB，1197精确source SHA仅25新blob，其余共用Git/旧blob；22实际进程logs、所有早期失败/原始/最终report与独立两套payload、范围/格式/生命周期/40样本与analysis/verify通过。run.json=pool-only低signal最终失败组，output32-run/final-run=最终范围+质量，final-player-run=最终Player，final-gpu-run=最终cost，勿把单个Root run当全部成功。pool-only重命名source目录已在source-files映射。所有本任务进程结束/active=null；他人Ho-GD plan未跟踪。旧022 verifier currentSHA因新生产策略过时，用189fcd9历史，不回滚新代码迎合旧hash。

**024下一任务先正常enabled-camera呈现：** 功能改善已入生产，不再只做小NRD平面或阈值边缘调参。先读DI hidden-host原证据及具体flags/启动方式，核对是否试过不带-batchmode但-WindowStyleHidden、启用camera+targetRT/OnDemandRendering。只有有新机制才有界实跑；真实自动endCameraRendering连续帧+GI/Volume开关/输出读回，不能SubmitRequest/Camera.Render替代。宿主约束明确Start-Process仅Hidden，用户未明确请求可见交互窗口时不能私自ShowWindow；如果新机制仍不可，先准备具体可review场景/步骤，再说明约束来源请求所需可见测试，不泛泛停工。

其余目标仍未完成：full/7phase/局部、AS/F39/static非零base、HDR更广支持、真正normal camera、实际1080/完整预算。F39旧flags/延时/重建不重试，NativeRtxdiGPUScene是NativeRender别名不是Unity overload；有新API/分配机制证据才重开。goal active，不complete也不blocked。

023白名单：Runtime RTDI core构造/format、RTGI NrdContext；Editor NrdValidation partial/新PrecisionRange/meta、Castle/PoolPrecision capture、PlayerBuilder；Tests DynamicRuntimeProbe、PrecisionQualification；RTGI产品说明/计划/README/过程/禁试/交接。无用户场景、DI策略/Shader/Native DLL修改；五已知build warning只沿021逐字白名单。

023已按白名单过滤提交`e5ff691`，PrecisionQualification verifier提交后再次通过；最终GI-only四32F池/output32、单位64/DI默认不变，原失败保留。两个最终315请求Player与HDR/生命周期通过，fullgreen/7phase与稳定目标预算仍未过；index空、22个本任务进程结束，仅他人Ho-GD plan未跟踪。下一轮024优先normal enabled-camera连续呈现，先读DI原hidden条件，有新机制才试；goal active。

### 024终态（2026-10-22，待过滤提交）：真实自动targetRT相机已过，下一025屏幕目标

先复读DI MotionProtocol/Timing后，新增Automatic独立资产/Player/probe，serialized Camera.enabled=true +64²target，无任何Render/SubmitRequest/Volume.Update/预热。两hidden RG实际exit1：第一target未created、第二显式GPU Create后created，均180Update/0事件；失败保留。用户随后明确授权“允许短暂可见窗口测试”，不要再次索要同一授权，也不要扩大为长时可见窗口。

同一RG build/payload只Hidden→Normal，43576在8.15s自行exit0；compat独立build44364/Player45248在8.30s exit0。各241Update/240连续真实endCameraRendering/240前帧记录/10checks，runtime API/Shader/绑定0、source drift0；build五条历史warning逐字保留。全部RGB加法误差<8.80e-5/6.86e-5（quarter/one）、Volume-off32帧恢复raster、re-enable首帧reset、30移动帧无reset、发光off首帧redraw/filtered0+reset。没有手动请求，RG/compat各自报告false/true。

边界：相机输出64²ARGBHalf targetTexture，窗口可见仅是正常自动调度条件；尚未证明target=null的backbuffer呈现/屏幕截图/视觉QA。Runtime完全未改、未测新GPU成本。全域green−5.05869%/7phase、局部/参考偏差、static非零base/F39、1080/完整预算都继续。不要回去调阈值、扫描纯单位/格式/挑phase、重启F39旧矩阵或F31同条件HD。

Tests~/RTGI/AutomaticCamera verifier已过；1163source对象5新文本、7完整logs、两失败和两成功、最终两payload SHA无HoGIGpuProbe.dll。原首binary被重建覆盖，manifest保留不冒称可现场验证。archive_evidence.py仅在本机最终输出存在时复核payload，verify无需那些二进制；source-index重建精确历史。所有本任务进程结束，两run active=null。未跟踪Ho-GD Outline计划为他人改动，不能stage。

**025直接动作：** 独立backbuffer夹具/输出，serialized-enabled且targetTexture=null；需要CaptureScreenshotIntoRenderTexture/EndOfFrame之类捕获时先核本机Unity API与色域/坐标，不能手动提交渲染。保持真实callback/previous-frame证据和原强度/Volume/动态门槛，短暂可见测试已获授权。场景尺寸/纹理尺寸与屏幕大小分列；成功补截图/可见呈现，失败保留后review，不扩大为完整画质/GPU预算。每步前后review、收益/成本/禁试/交接与过滤提交持续，目标active。

024已按白名单过滤提交`0d80f85`，AutomaticCamera verifier提交后再次通过。两visible Player各240真实自动targetRT渲染，两个hidden失败保留，全部本任务进程结束、index空，仅他人Ho-GD计划未跟踪。下一025直接默认backbuffer呈现/色域/捕获时序资格，短暂visible授权已获；原质量/F39/HD缺口与目标active继续。

### 025终态（2026-10-22，待过滤提交）：默认screen自动呈现通过，下一026回质量

新HoRTGIBackbufferProbe和BuildBackbuffer/Compatibility，独立资产/Player，serialized-enabled+target=null；实际512²屏幕/GI，WaitForEndOfFrame捕获已完成屏幕，下一Update核对callback/capture前帧。固定sRGB→linear及独立raster基线资格，未用手动Render/SubmitRequest/Volume.Update。用户短暂可见测试授权沿用、每次15s上限，不扩长时窗口。

首build39064 actual1因为隔离工程没有ScreenCapture模块，失败与原manifest/源码/log归档；只隔离项目补本机内置模块1.0.0，产品依赖无改。最终build44940/38344、Player13180/44248 all0；RG/compat各241Update/240连续callback/240前帧记录/11checks，测量跨度4.514/4.576s。RGB加法最大quarter .00471446/one .00643197<原.015；Volume-off32帧恢复raster、re-enable首帧reset、移动30帧全不reset、发光off首帧raw/filtered红0+reset。两backend标记false/true，runtime API/Shader/绑定/fence0、drift0；build五条历史warning保留。两条旧未用GB fallback/MSAA资源缺省提示也保留，不称全日志无warning。

10张真实屏幕PNG/尺寸SHA完整，已查看RG raster/one与compat emission-off；纯平面灰→红蓝渐变→红off蓝留吻合，非复杂场景视觉验收。Tests~/RTGI/Backbuffer verifier过，1164source SHA仅6新小文本、5完整logs、原失败/最终payload无HoGIGpuProbe.dll/环境API原文。全部本任务进程已结束、active=null、Runtime无diff；他人Ho-GD计划排除。

**026直接动作：** 默认targetRT与screen自动可用性已证，不重复隐藏无事件。回到当前生产默认的独立seed全域/局部质量证据：先复读019 SeedAudit和016局部/空间域定义，沿同场景同支持域、保留原5%及全部16phase，明确当前023精度/pre-S结果与早期019代码差异；不重复四模式大矩阵、不拿选择偏差参考作绝对真值。先提出一个有界、能区分剩余损失与seed方差/局部结构的测量，再据实际结果选择算法，不能为了green5.05869%调阈值/挑phase/继续格式单位扫描。F39/static非零base、完整场景动态/HD/完整Player预算继续，目标active。

025已按白名单过滤提交`ba76e3b`，Backbuffer verifier提交后再次通过，环境XML及source blob已核对Git暂存原字节SHA。默认screen两Player各240自动帧与10张屏幕图完成，首次缺模块失败保留；所有本任务进程结束、Runtime无diff、index空，仅他人Ho-GD计划未跟踪。下一026当前生产独立seed全域/局部质量边界，保留原5%/7phase/F39/HD缺口，目标active。

### 026终态（2026-10-22，待过滤提交）：当前TS八seed完成，027先新局部参考

三个Editor实际进程38940资格/39616正式/44052编码修正资格均exit0、API/编译/Property/UAV/fence/drift0；请求98/2594/98。生产Runtime/参数/NRD算法/SDK不改。SeedAudit新增显式TS-only和全部16phase，SpatialAudit仅CPU map导出；正式8seed原列表，各64warm/256观察、256²/18灯/15实例/12Cutout/天空/alpha/primary域合同。report.pass/seedFunctional只是测量及功能，不等于质量过。

当前full RGB误差−4.71577/−5.15807/−4.82336%，配对差seed经验SE/raw .05379/.11936/.08255%；3/8 full过，pooled full仍失败，pooled9/16phase及32/64固定32²块失败，各seed phase7/11/11/8/8/9/11/7失败。绿色距5%仅1.32经验SE，不过度确定、也不放宽或挑seed。filtered/raw下降不等于独立物理真值误差；所有旧门槛/参考15/16/选择偏差保留。

first4seed探索top12、last4核验全部同点：探索首位(221,184)从.01454缩至.000127±.000172经验SE；x86在y206/227/255/252/204/226仍.00159～.00252亮度差、各约>4.5经验SE、RGB约−35～−66%。全部64块/16phase/12点包括反向与缩小都在analysis。不能据此将raw当真值或直接改NRD。

编码修正：初脚本默认Windows locale使Castle源为GBK；原两实际run字节完整归档，修正为显式UTF8且Unicode文本同，44052重编译/场景资格0、两个publication map逐字节同。encoding-repair含源/图SHA；不要把历史run换标新source。后续读写显式UTF8。Tests~/RTGI/ProductionSeeds约12.6MiB，239source仅5新blob、3完整log、3JSON+12maps lossless gzip（两同map复用）、analysis/verify过。最终qualification除一行progress选中模式标签修正外对应当前代码；reporting-review以精确字节替换绑定该纯报告修正（false传modes，复用已编译签名，不改GPU/最终report）。matrix使用旧编码字节，文字语义不变及实际支持域已证。所有自有进程终态、active=null；Runtime无diff，仅他人Ho-GD计划排除。

**027直接动作：** 先复读016 Spatial README关于有限窗raw不能当物理损失、017 DenseReference同域MC协议。对上述6个x86位置及预先固定相邻x85/x87控制点取得无复用高样本一跳参考及primary/normal/depth/albedo/输入guide证据，核对新像素与相同材质/光/sky/AS域，不拿旧参考15点代替新坐标。参考若未收敛则保留未定，不能用filtered/raw直接定性NRD错误。随后才考虑局部阶段/机制候选，拒绝gain/宽sigma/单位格式扫描/重复四模式大矩阵。原全图5%/所有phase、static非零base/F39、复杂动态与1080/完整Player预算继续，目标active。

026已按白名单过滤提交`ecd9c6a`，ProductionSeeds verifier提交后再次通过（保留原pooled质量失败，3个实际进程均结束）。最终progress标签纯报告修正另有精确字节review，未冒称GPU重跑。index空、Runtime无diff，仅他人Ho-GD计划未跟踪。027直接新x86六位置及相邻控制的同域无复用高样本/primary/guide，不把raw当真值，不重复本轮完整8seed或旧四模式，不调门槛；原质量/F39/复杂动态/HD预算与目标active继续。

### 027终态（2026-10-22，待过滤提交）：18局部参考全收敛，028定位NRD阶段

新LocalPixels固定y204/206/226/227/252/255、每行x85/86/87，共18。Castle LocalReference入口与接收点选择、DenseFeature actual mainContext、DenseAudit首尾NRD guides/finite guard，仅Editor。Runtime/SDK/vendor/NRD设置无改。18160两批资格36请求、22748 2048批完整2082请求、36548最终finite guard后资格36请求均actual0，API/编译/绑定/fence/drift0。

同256²古堡/18灯/15实例/12Cutout/次级天空，8 local/4 env/.001 bias/1000，原一跳无T/S/NRD参考、主相机生产TS+NRD，黑样本计入。每点1048576样本，18/18原SE≤2%/split≤5%/positive/低信号全部过，max相对SE .84477%、split2.97214%、min positive250448。原full/history/ray primary一致性通过，ray差0、primary约1.89e−6，场景/light/sky及normal/albedo稳定。仍same-model非绝对真值/全图验收。

对026 8seed map同坐标，中心6 raw/ref L1 .067544、filtered .494028；邻12 .046567/.423096；全部18 .056952/.458213。中心RGB总量raw +1.490/−1.242/−.994%，filtered −42.161/−59.933/−62.224%；逐点filtered/reference .280～.651。全部18与heldout后4seed数据保留，证明这些位置欠亮不是仅raw单窗极值；不能外推全部画质。report.mainFrames0，旧mainRaw/mainFiltered未测，比较只来自026 maps。

首尾主guide16F normal/signal/raw/filtered、viewZ32F、scale64；NRDConfig2/rough1 pack误差≤.000228882、decoded normal≤.000324177、viewZ0，input remod在max5e−6/.002raw界，maxabs约1.077e−5。前4行x85较中心depth低约.155、法线平行且albedo约.02→.4；后两行左邻已在后平面。输入匹配不等于具体filter因果，不能现在断言HistoryClamping或PrePass根因。

正式run在新增finite guard前；唯一源diff已记review，原所有guide独立finite，最后36548资格编译/实际验证新guard。split verifier首次decimal64不符，按原C# LINQ Average(float selector)返回float再赋double及IEEE32批数据重建后全过，未改原门槛/数据。Tests~/RTGI/LocalReference约2.5MiB，242精确source/9新文本、3完整logs/3原JSON gzip/026输入SHA/analysis/review/verify；当前根源对应最终资格，旧reference字节仍完整。全部本任务进程已结束、active=null，Runtime无diff；他人Ho-GD计划不能纳入。

**028直接动作：** 复用同18点新参考，预声明4seed0/104729/5347609/6396185，各64warm+256观察；先真实绑定/32F typed copy self-test及UAV/F43 guard，再捕获NRD input、PrePass、Temporal slow、条件HistoryFix/fast、Clamp slow、first/lastAtrous、physical publication。每阶段按同像素实际albedo×64重调制，分seed报告局部/reference L1，同时保留原全域/16phase/5%门槛及raw相同检查；5不是slow m2，不能误标。按实际主损失阶段再定一个有机制候选，不无依据关闭某阶段/宽sigma/gain/格式单位扫描，不复活F36同box线段clip。无需重跑027 1M参考或026完整8seed，只引用精确输入SHA。原full绿色−5.158%、9phase/32块/F39/static/复杂动态/1080完整预算继续，目标active。

027已按白名单过滤提交`d099dff`，LocalReference verifier提交后再次通过；18/18新局部参考原收敛过，主guide输入匹配，局部filtered欠亮有同模型参考支持但未修复/未定位阶段。3个实际进程终态，index空、Runtime无diff，仅他人Ho-GD计划未跟踪。028直接同18点/4个预声明seed的实际32F stage与新参考误差，不重采本轮MC或重复0268seed；原全域/phase/F39/static/复杂动态/HD完整预算继续，目标active。旧verifier涉及current-source SHA时用所属历史提交，不回滚当前代码凑旧检查。

### 028终态（2026-10-22，待过滤提交）：Atrous后四轮是主要局部差，029单候选

新HoRTGILocalStageAudit/meta+Castle flag/报告，仅Editor。固定027同18点，4seed0/104729/5347609/6396185，各64warm/256观察；先资格2seed×16/16。43892/14436 actual0，98/1314请求，API/compile/Property/UAV/fence/drift0；copy self-test64分量逐bit过，capture256²RGBA32F/UAV true。primary与新参考/原full/history/ray合同、18灯/15实例/12Cutout/sky/alpha与设置通过。

HGS1原流每帧有shader/app帧、6writer count、9stage×18RGB。3/4/5/6/9各写1，10写4，复制末10；3 external、4 transient0 slow、5 transient1条件fast/Fix、6 permanent0 slow、9 transient0、10 transient1/0/1/external。5不是slow m2。每帧实际history albedo×64重调制；负值保留。四seed raw+filtered全图逐帧RGB均值均与026同seed逐条同，18点raw/pub均值也逐bit同；别外推全纹理逐帧byte同。

相对02718点参考L1：raw .094054、input .094075、Pre .094619、Temporal .112661、Fix/fast .097161、Clamp .113427、first Atrous .130687、last .462370、publish .462545。首Atrous RGB−12.987/−12.023/−12.743%，末−38.709/−57.173/−60.188%，主要差在首轮之后四次Atrous。没存10中间轮，不断言某一轮单独有错；同模型选点非全图真值，未修复生产质量。4seed full2/4过，phase失败7/11/11/7；pooled fullRGB−4.71818/−5.10748/−4.80745%仍失败、8phase失败。raw/filtered全域时间方差均值4.26391e−6/5.60620e−9。

复读本机参考Relax.cpp步长1<<i、runtime Atrous shader与参考LF逐byte同、step>4随机offset；code-references原文/SHA保留，不误称当前DLL常量反编译证明。LocalStages约2.3MiB，246source/7新blob、2完整logs、2JSON+6streams lossless gzip、输入SHA/analysis/verify。当前根源对应actual runs，所有自有进程终态active=null，Runtime无diff，他人Ho-GD计划不纳入。

**029直接动作：** 只Editor GI实例ValidationSettings设atrous=3（原5），保留前3轮移去后两大步长；不要改其它settings/采样/单位/格式/时域策略。沿同18点及4seed先资格+实际stage，Native writer count应9一次+10两次（3总），raw必须匹配，全部18点含暗邻与旧full/16phase/noise均报告。重点对新参考L1/细节、全域原门槛和方差取舍，不按加权L1掩盖暗邻硬退化。无实质收益或有硬退化搁置、不扫2/4；有收益才做8seed/旧合格参考点/动态/HDR/两Player/GPU后决定生产，不能跳过资格。028helper可继续用，verifier当前严格baseline10×4，候选另开证据目录/合同，不覆盖028或改绿。原F39/static/复杂动态/HD完整预算继续，目标active。

028已按白名单过滤提交`90fc233`，LocalStages verifier提交后再次通过：copy/真实binding与writer count/全部stage原流/原质量失败保留，raw与pub观察一致性均通过。两个自有进程已结束、index空、Runtime无diff，仅他人Ho-GD计划未跟踪。029直接GI私有atrous5→3单Editor候选，先原18点/4seed/full/phase/暗邻/噪声取舍，有收益再8seed/旧参考/动态/HDR/Player/GPU，不扫其它迭代数、不提前生产化；全目标和原缺口继续active。

### 029终态（2026-10-22，待过滤提交）：Atrous3有收益，030更广资格，Runtime仍5

Editor-only settings scope只改GI atrous5→3，148B唯一差offset92 uint；记录Native Applied3，9一次+10两次，finally hook/capture恢复，之后正常render实际回5。其它settings/采样/单位64/生产32F/Shader/SDK/Runtime/DI不改。8792短资格98请求、632正式1314请求 all actual0，API/compile/Property/UAV/fence/drift0，F32 bitcopy资格过。

正式前review-contract：固定18点L1改善至少20%；原full/phase通过项不转失败；逐点/通道absolute error增加超过max5e−6/ref5%/2×组合配对seed与参考经验SE判硬退化；filtered/raw全域方差比.15是筛选代理，不替代真实夹具，SE不提供正式CI。所有原质量门槛不动。

同4seed0/104729/5347609/6396185、64warm/256观察，18点L1 .462545→.307095，改善33.6076%。原full2→3/4，pooled RGB−4.53772/−4.54476/−4.09065%通过full；phase7/11/11/7→6/4/11/5，pooled8→6失败，无pass→fail。暗邻部分通道微增，但无预声明硬退化；仍局部欠亮、一个seed full/6phase失败。方差均值5.60620e−9→7.76781e−9（+38.56%），candidate/raw .001822。不是全质量/动态/Player/GPU资格完成。

raw全帧均值+局部所有样本、input/Pre/Temporal/条件fast/Clamp/first Atrous局部原流均对028逐bit同。数据与新增scope恢复可核验，Editor override不能标成production。Tests~/RTGI/AtrousCandidate约2.2MiB、246source/3新blob、2完整logs、2JSON+6streams lossless gzip、输入/review-contract/analysis/verify过，全部自有进程终态/active=null，Runtime无diff，他人Ho-GD计划排除。

**030直接动作：** 固定同一atrous3，先完整8seed/旧17合格参考点与暗控制、full/全部16phase/全域及局部noise；旧16点参考含15合格/暗点未定必须原样保留，不挑点或用新18点替代。可复用SeedAudit productionOnly真实probe+每seed full map，Editor settings scope只改3并验证Native counts/恢复，raw与0268seed必须匹配；对0294seed局部结果可交叉复核。新增候选完整数据仍与生产5隔离，不覆盖旧证据。若继续有收益，再动态/HDR/两Release Player/GPU，最后才决定生产；不扫2/4或直接关闭降噪。原F39/static非零base/复杂动态/1080完整预算继续active。

029已按白名单过滤提交`e53709e`，AtrousCandidate verifier提交后再次通过。固定3轮局部L1−33.6%、pooled full过但6phase仍失败、方差+38.6%，只进入更广资格。Native设置只改4字节、真实3轮与回5已证；两个自有进程终态，index空/Runtime无diff，仅他人Ho-GD计划未跟踪。030直接固定3的8seed/旧合格参考与暗控制，再动态/HDR/Player/GPU；不要先改Runtime，不扫描2/4，原所有缺口/目标active继续。旧source-SHA verifier用所属提交，不回滚当前代码凑绿。

### 030终态（2026-10-22，待过滤提交）：固定3轮搁置，默认5保留

完整8seed旧16 probe/full map已实际完成：19396资格98请求、39616矩阵2594请求 all0，设置148B仅offset92 uint5→3、Native3/hook恢复/后续5实证，API/compile/绑定/fence/drift0。旧16含15合格+未定(16,16)保持，结合新18共34点33合格全部评估，原full/16phase门槛不动。

新18 L1 .458213→.300045（−34.52%），旧15 .306745→.293238（−4.40%），full3→7/8、pooled RGB−4.53312/−4.59497/−4.11332%过，phase9→8失败/无pass→fail；方差+40.79%。但是(141,157)G/B按预声明筛选硬退化：G mean/ref .911986→.847986，abs误差增加2.42011e−5>界1.89071e−5；B .996998→.908287，增加8.56060e−6>5e−6。R也更暗但未越界。加权L1不能覆盖反例，因此eligible=false，固定3轮搁置，不继续此候选生产资格，不扫2/4。

8张raw均值全图和全部逐帧raw RGB均值对026逐bit同，029重合4seed新18均值逐bit同；实际变化不是采样换预算。Runtime/DI/SDK/Shader一直未改。过滤已把Castle/LocalStageAudit/SeedAudit恢复90fc233默认5诊断版，移除candidate入口/override/记录专用逻辑；原候选源和全部真实数据保存，不能把历史source重标当前。19648过滤后实际98请求资格0，copy64 bitexact，Native9×1+10×4=5，API/compile/Property/UAV/fence/drift0。所有自有进程终态/active=null。

Tests~/RTGI/AtrousQualification约12.8MiB，250source仅8新文本、3完整log、3JSON+12map/streams lossless gzip、原输入/34点/analysis/contract/filter-review/verify。current-source由filtered-run对应，历史candidate用归档源重现；当前run_validation.ps1的候选入口已移除，不直接运行它，也不回滚产品凑旧SHA。run_filtered.ps1仍验证默认5。只两Editor回撤最终diff，新证据/RTGI六文档白名单，Ho-GD计划不纳入。

**031直接动作：** 保持5，扩充逐轮观察到反例(141,157)及相邻控制，并保留新18点；028只保留pipeline10末轮，需按每个10的ordinal保存输出/输入，结合几何/亮度邻域与权重解释哪些传播帮助/伤害两类位置。先做新观察自身不改结果的qual，再有限predeclared seeds，不重跑1M参考或完整8seed。判明机制后才选择一个局部候选，不再全局降低迭代数/宽sigma/gain/单位格式扫描，不只针对新18点。原full/所有phase/F39/static/复杂动态/1080完整预算及目标active继续。

030已按白名单过滤提交`6e8e6a1`，AtrousQualification verifier提交后再次通过。完整8seed/34点发现旧141157 G/B硬退化，固定3轮已搁置，Editor入口与override撤回默认5并实跑资格过；Runtime一直未变。三个自有进程均结束、index空，仅他人Ho-GD计划未跟踪。SeedAudit工作树与HEAD原字节SHA相同，已刷新索引stat，不增加伪diff。031保持5，为亮边和反例取逐轮/邻域传播新证据，不扫全局迭代数；全部原目标/缺口active继续。

### 031终态（2026-10-22，待过滤提交）：逐轮相反作用与luma floor假说，032单候选

新HoRTGIAtrousTrace/meta，LocalStageAudit增加propagation可选分支，Castle可选27点：新18+旧141157+3×3其余8邻；19合格参考/8无参考仅guide。默认stock5/scale64/production32F/参数/Runtime/DI/SDK不变。五实际进程：16584初debug资格98请求0、6360 stock control98请求0、9188 stock4seed正式1314请求0、40664最终debug资格98请求0、45656 debug4seed正式1314请求0；API/compile/Property/UAV/fence/drift0，F32 copy64分量bitexact。

重要边界：初debug并未通过exact observer，保留initial-debug源码/原报告（map差max2.38e−7）。stock对照与026全raw/pub均值图逐bit同，质量stage只用stock。debug原算式未改（去附加块后逐行同）但compiled浮点有差，正式mapmax1.49～2.98e−8、local intermediate约3.26e−9/pub个别3.81e−6；只用于独立权重解释，重建自身RGB误差≤7.34e−11/sumW≤1.20e−7，不称stock逐byte同，不改原质量门槛。shader10/kernel仅当前GI实例替换、finally恢复。

原版结果：新18 L1 first .130687→step2 .215150→4 .306872→8 .386896→16 .462370。反例Clamp .04152，first .14924，后.14305/.15325/.12229/.11055，蓝/reference first.8259→末.9967；全局删后两轮会删恢复作用，F51不重开。新增HGP1存12stage/27RGB及6writer count；HGW1存选定观察帧每4轮27点37float4，含center、var、guide、offset/sumW/output、8tap与实际权重因子。主权重只在0/32/.../224/255采样，不作全帧保证。

机制线索：6亮边中心step16平均负亮度贡献约−3.744e−4、暗于center一半的donor占8.16%总权，反例step8/16正贡献约4.620e−5/2.220e−5且该类暗donor0。源码phi2/sqrt(var)分母floor1e−4；记录variance显示亮边step8/16激活比例78.7/97.2%，反例13.9/69.4%；step16 phi×sigma中位数4.85e−5/7.88e−5。实际luma因子反推inverse~10000，支持floor在scale64下可能过宽的单位域假说，尚未证明修复。

Tests~/RTGI/AtrousPropagation约24.4MiB，255source/12新文本、5完整log、47capture gzip、5模式生成body/wrapper原文SHA、observer-review/analysis/verify通过。所有进程终态active=null，Runtime无diff，Ho-GD计划排除。旧HGS1 baseline入口仍可用；propagation新增stock control/control-matrix与debug qualification/matrix，不能把两类数据混成exact结论。

**032直接动作：** 只Editor GI私有Atrous diffuse luminance分母floor按AppliedSignalScale64作物理单位对齐（1e−4/64），保持5轮/phi/variance/实际RGB单位/格式/其它原算式不变；确切定位luma分母的1e−4，不能全局替换shader里tap cutoff1e−4或specular常量。先单scope/source diff及真实作用资格，再固定4seed所有新18+反例/暗邻，并完整8seed旧15控制/full/16phase/noise取舍；不得只看加权L1，失败隔离撤回。此是基于实际权重的最小阈值候选，不是重开F35单位1/.125或F51迭代扫描。其后必要动态/HDR/Player/GPU前不得生产化；所有原full/phase/F39/static/复杂动态/1080完整预算继续active。

031已按白名单过滤提交`99f4fac`，AtrousPropagation verifier提交后再次通过。stock全图时间均值图逐bit相同、逐轮两类相反作用已证；debug非exact，独立数值边界/重建与初失败保留。五自有进程终态、index空、Runtime无diff，仅他人Ho-GD计划未跟踪。032仅GI私有diffuse luminance分母floor随scale64对齐的单候选，默认5/其它算式与参数不动，先新旧控制/full/phase/noise再资格，不能跳过141157或重开迭代/纯单位扫描；全目标和所有原缺口继续active。

### 032终态（2026-10-22，待过滤提交）：floor/64候选失败撤回，033复用权重核对

测试只GI实例shader9/10 diffuse亮度分母floor1e−4→1e−4/64，逐体一个token、其它原算式/settings/5轮/phi/variance/真实signal单位64/格式不改。21760短资格98请求、6324seed正式1314请求 all actual0、Native9×1+10×4、scope还原、API/compile/Property/UAV/fence/drift0、F32 copy64过；031 stock27点路径，无debug权重shader混入质量数据。

候选不合格：新18 reference L1 .462545→.444431（−3.916%，不足原20%筛选），full2→0/4过，phase7/11/11/7→9/14/12/12，11个原pass→fail；pooled RGB−5.02872/−5.56743/−5.33700%，旧141157 G/B微变，19点无局部hard不能压过全域退化。方差近似不变，各seed约−.27～+.18%；raw全部局部样本/preAtrous流逐bit同，输入没有换。策略拒绝/搁置，不扫描其它epsilon/单位/迭代，也不继续8seed/Player/HDR资格。

过滤后Castle精确恢复99f4fac，新增HoRTGILumaFloorAudit.cs/meta已移除，入口/覆盖不保留；Runtime/DI/vendor一直未改。43936 stock control实际98请求0，Native5/copy64/scope/原math资格过，API/compile/绑定/fence/drift0。原候选所有源/生成体SHA与报告完整留存，不重标当前源。Tests~/RTGI/LumaFloor含254source、3完整log、27capture gzip、输入/contract/analysis/filter-review/verify；current-source对应filtered-run，历史候选runner入口已删，不能直接调用或回滚产品凑SHA。自有进程终态active=null，Ho-GD计划不纳入。

**033直接动作：** 复用031已有HGW1 center/sample variance、geometry/normal/material/luma因子/真实tap权重，离线检查luma权重双向方差依赖、归一化导致的亮度传播与两类结构差异，解释只调floor为何无效。固定原新18+反例与邻点，不能只挑亮边；静态输入重放不是完整迭代反馈或GPU资格。只有新机制能在现有反事实中区别两类位置，才再定一个局部GPU候选；不重采1M参考/重复旧完整矩阵/扫常量。默认stock5保持，F51/F53继续搁置，原full/phase/F39/static/复杂动态/1080完整预算和目标active继续。

032已按白名单过滤提交`d9d4459`，LumaFloor verifier提交后再次通过。固定floor/64候选局部仅−3.9%、full2→0和11phase退化已拒绝，原候选源/生成体/失败完整；新增类/meta/入口已移除，stock5过滤后实跑过。三个自有进程终态、index空、Editor/Runtime无最终diff，仅Ho-GD计划未跟踪。033先复用031权重/两侧variance做离线双向归一化机制核对，不重跑旧GPU矩阵/MC、不扫常量，原全部目标/缺口active继续。

### 033终态（2026-10-23，待过滤提交）：离线核对完成，034仅大步长factor2支持域

无新GPU/Unity/MC，Runtime/Editor无diff；复用031 4seed×9帧×4轮×27点。forward luma max差3.013e−7/weighted output7.34e−11；27真实world/depth透视拟合rank4/cond4169.7、max residual3.93e−6，31,104安全tap几何判断0不符。仅样本一致性，未直接读native frustum或证明全场景。

两固定结构重放：正反luma几何均值新18 deltaL1 step2/4/8/16=−.00635/−.00446/−.000779/−.0000875，后期预测太小，不试GPU。031既有[.5,2]亮度bin双向支持新18=−.01649/−.02065/−.01907/−.01795；反例step2 +.00206，step8/16仅1.4e−8/1e−9（全tap保留），所以只考虑大步长。原每轮输入冻结，不是完整逆kernel/迭代反馈/动态/GPU质量，不能把单轮变化叠加作承诺。原27点/19参考+8无参考guide/全部输出变化与分seed结果都保留。

Tests~/RTGI/PairwiseWeights有压缩输入SHA/源码公式/analysis/verify，默认5、所有旧失败/范围保持，Ho-GD计划排除。033是机制筛选收益，没有修复交付。

**034直接动作：** 一个Editor私有GI shader10候选，只有gStepSize>4时令sampleLuma处于centerLuma[.5,2]才保留原权重，双零保留；对称支持域非单向提亮，5轮/单位64/格式/phi/variance/原geometry-normal-material和其它算式不变。factor2只沿031既有bin，不扫阈值。先源码精确diff/作用域恢复/实际资格，再同4seed全部19参考+8邻/full/16phase/noise，不能漏141157；有收益再旧15/全8seed/动态/HDR/Player/GPU，失败撤回搁置。原F51/F53继续，全部原full/phase/F39/static/复杂动态/1080完整预算与目标active。

033已过滤提交`0955bd9`，PairwiseWeights verifier提交后再次通过。无新GPU/产品代码改动，原输入/公式/几何核对与冻结反事实范围明确。后期luma对称化预测太小搁置，034仅既有factor2大步长双向支持单候选，实际资格/新旧点/full/phase/noise前不称修复；Ho-GD计划排除，目标active。

### 034终态（2026-10-23，待过滤提交）：guard候选撤回，035先stock配对重现性

39032短资格98请求、31792四seed正式1314请求 all actual0，API/compile/绑定/fence/drift0、F32copy64/Native5与source scope恢复过。只GI shader10 step>4对称factor2 guard，去一行恢复stock，原Runtime/DI/vendor/其它settings/单位64/格式保持。

数据只能叫名义对比：new18 L1 .462545→.421666（−8.84%，不足20%），counter近似同，full2/4、pooledRGB−4.69053/−5.02999/−4.71627%，phase7/11/11/7→7/10/11/7，无pass→fail、方差+4～11%。最终pair核对：seed0/104729 raw/local/preGuard全部同；seed5347609/6396185 raw不满足逐bit，全帧RGB均值max2.21911e−6/1.84949e−6（各767/768变化），local样本max9.155e−5/.001495。comparisonQualified=false，不能宣称因果收益，根因未知，别无证归因。

原源/生成体/所有结果完整保存后，Castle恢复99f4fac，新增ContrastSupportAudit/meta已删除。49216过滤后stock control实际98请求0、Native5/copy64/scope和API/compile/绑定/fence/drift通过。Runtime一直未改，所有自有进程终态/active=null。Tests~/RTGI/ContrastSupport含源码/生成体/3logs/27capture/analysis/filter-review/verify，当前源码对应filtered-run，历史candidate入口已删，不能回滚产品凑SHA。Ho-GD计划不纳入。

**035直接动作：** 暂停算法候选，先stock默认5同4seed的原版重复以及无操作shader clone对照，保持原27点/相机/时间预算；检查后两seed raw是否重现差异，记录实际shader frame、numeric lights/sky/RIS及secondary记录，必要逐字段比较而非仅单个hash。若stock原版本身变化，先定位状态/seed-reset/环境输入；若仅clone引入，定位观察/资源副作用。源/日志/失败都保留，严格pair恢复前不再试滤波参数或RGB算式。不要重采MC/跑旧AS flags或无新机制完整大矩阵。原质量/F39/static/复杂动态/1080完整预算及目标active继续。

034已按白名单过滤提交`5095ddc`，ContrastSupport verifier提交后再次通过，明确保留comparisonQualified=false及后两seed raw不匹配。名义收益不足、不接纳因果质量；候选类/meta/入口已撤回，stock5实跑过，Runtime/Editor无最终diff。三自有进程终态/index空，仅Ho-GD计划未跟踪。035先stock重复/no-op clone及实际输入状态定位，严格pair恢复前停新滤波候选；所有原目标/质量/F39/动态/HD预算继续active。

### 035终态（2026-10-23，待过滤提交）：当前stock/no-op严格重现，保留历史异常

新InputStateAudit/meta+可选端点记录，数值灯72Vector4/primary135uint/RIS16384/secondary27及完整缓冲hash/probe；DenseFeature记录sky数值，Trace支持无debug的literal-stock clone。Runtime/DI/vendor无改动。28112短资格/31692初stock虽actual0，JSON漏原native struct数组，已保留initial-numeric-omission而不补写。

53896修正后资格DEVICE_HUNG/removal/Unmap actual1073741845/debug11/无fresh report，发生在首快照前，原因未证实。PowerShell在外部Python失败后误排7312 stock，核自有PID/路径/启动时间后停止actual−1，partial/abort-review保留；已加EAP/LASTEXITCODE与runner numeric字段门槛。未动TDR/driver/他人进程，显存7302MiB/util50%仅相关观测。

恢复：6968 empty/no-AS小known compute24samples/fence actual0/debug0；20912 CPU实际JsonUtility roundtrip actual0；显存下降后44040短场景98请求0/数值与hash资格过，资格mode限120s。随后53112 stockA、50368 stockB、50088 no-op all0，各1314请求，原4seed0/104729/5347609/6396185、64warm/256观察、27点/5轮/原支持域，API/compile/绑定/fence/drift0。三组全raw/pub时间均值图对026同seed逐bit同，彼此全部endpoint数值灯/sky/RIS/full secondary/primary hash与选中字段同；noop body只include改定位、无新算式，shader/kernel恢复。

初stock灯/sky/RIS/primary对当前A同、secondary whole hash不同；27选点只position/normal末位差，hitmax9.26e−6、missdir7.82e−8/radius.01324、normal1oct16步，均符合019界。关联不证明raw漂移或TDR根因，历史cause仍unknown；当前严格重现不回填034资格。端点不是所有帧全部输入。

Tests~/RTGI/Reproducibility含255source、8场景logs+2辅助logs、77原capture gzip、数值schema/abort/恢复与所有失败/analysis/verify。全部自有进程已终态、无active；Ho-GD计划排除。是记录/编排/重现性收益，不是画质修复/新性能预算。

**036直接动作：** 使用同一GI输入的两个独立NRD baseline/no-op分支，先证明输入纹理/矩阵/时间/重置和输出等价、互不污染，后续仅一分支实验；这样避免再靠跨进程raytrace完全一致推断重建差异。先小负载/短资格，不立即增加大场景/HD驻留；所有Native描述符/时间/回调范围与恢复要审查。若选择新算法，必须新鲜stock配对与原旧15/新18/暗控制/full/phase/noise等资格，F51/F53/F55不自动重开；原全质量/F39/static/复杂动态/1080完整预算与目标active继续。

035已按白名单过滤提交`bff5850`，Reproducibility verifier提交后再次通过。当前stockA/B/no-op对旧全raw/pub均值图逐bit同、数值端点一致；历史漏序列化/devicehung/显式abort与partial均保留，不确认历史唯一根因。十个自有进程全终态、index空、Runtime无diff，仅Ho-GD计划未跟踪。036先同一GI输入双独立NRD baseline/no-op分支资格，保持host/短资格/失败停止约束，旧F51/F53/F55不自动重开，原全部目标/质量/F39/动态/HD预算继续active。

### 036终态（2026-10-23，待过滤提交）：同源GI双独立NRD基线通过，037评估DI diffuse策略

Runtime GI NrdContext仅UNITY_EDITOR增ValidationRecordInputs定义/Invoke，Player预处理排除。callback在实际common/settings生成后、baseline core.Record前；2额外type10 RELAX_DIFFUSE core共享IN标识和值拷贝common/settings，独立pool/constants/OUT。branch0stock、branch1literal-stock no-op10，baseline不变，hook/shader/kernel/资源finally释放。没有改设置/5轮/实际Signal64/32F/Shader/DI。

首54932失败：URP pool未初始化导致Prepare null，0比较帧，wrapper又null生成path Copy失败，actual child exit未保存（wrapper1、child未知，别补成1）。initial-allocation原源/状态/报告/log/review保留。修为64²空相机3request先initURP、metadata在可选copy前persist。41304小64² actual0，64输入帧branch0始终baselineRGBA逐bit同；frame24只reset branch1使24～47每帧至少14596分量不同（>alpha4096），frame48统一reset后重同，历史独立。

38552实际GI短98request/32比较帧actual0，54328正式1314request/1024观察帧actual0，4seed0/104729/5347609/6396185各64warm256观察。每帧262144分量 baseline/branch0/no-op都逐bit同；含warm1280次actual callback。时间/矩阵/reset直接same actual values；原全raw/pub时间均值图与逐帧raw对026全同，observer未改变这些原结果。有效进程API/compile/Property/UAV/fence/drift0。

SharedInput约11MiB，259source/9新文本、4完整log/22capture gzip、Frame.common/settings/output SHA及diff、初失败/生成体/输入/analysis/verify。diagnosticLogicalBytes仅2branch external output/validation，不含pools/constants也不是VRAM；extra2NRD/readback不当产品预算。所有进程终态active=null，Ho-GD计划排除，原质量/full/phase未修复。

**037直接动作：** 已读并冻结DI技术Ho-RTDI-DiffuseExchange.md及实际HoRTDINrdAtrous/Smem派生（SharedInput/di-reference）。以原stock作为branch0，在branch1对一个GI-private diffuse策略bundle做可精确重建评估：首轮只temporal variance用w²传播（不混入空间均值差），后续pair bandwidth/RGB contrast与拒绝Gaussian mass留center。保持5轮/时间历史/原资源/单位64/原参数，specular/F0/DI身份guide不带入；不要简单复制BOTH manifest。先审实际DI body与NRD_DIFF变体的exact patch/源SHA，再64²/实际短资格，之后旧15+新18/暗控制/full/16phase/noise/重置与后续部署。若无实质收益或硬退化就撤回，不重开cap3/floor/ratio扫描。此是计划M2允许的同Ho输入原版/DI派生对照，不能自动认为DI成功就GI接纳。原F39/static/复杂动态/1080完整预算和目标active继续。

036已按白名单过滤提交`dfa3dd4`，SharedInput verifier提交后再次通过：64²历史reset隔离、实际GI1024观察帧全RGBA逐bit等价、旧baseline全图均值不变、初allocation/wrapper失败完整保留。四自有进程终态、index空，Runtime只有UNITY_EDITOR hook/Player预处理排除，Ho-GD计划未跟踪不纳入。037基于已冻结DI DiffuseExchange实际派生，仅branch1 GI-private diffuse bundle同源对照，先exact source/小资格再新旧控制/full/phase/noise，原全部目标/质量/F39/动态/HD预算继续active。


### 037交接终态（2026-10-23，待过滤提交）：DI diffuse同源实测有总体收益、旧控制硬退化拒绝

**实际范围：** 按预审只Editor branch1 pipeline9/10的一个固定DI mature diffuse bundle（Smem预模糊temporal variance、w²传播；后续pair min variance/RGB contrast、拒绝Gaussian质量留center）。原stock构造两个DIFF body，逐anchor/DI片段/SHA可重建，无spec/F0/guide/参数/5轮/单位64/格式/Runtime/vendor/DI改变。主baseline与branch0原版逐帧RGBA bitexact，branch1有实际变化。

**验证：** 四自有进程29048小64²/64帧、17236真实短98请求/32比较帧、31812正式1314请求/1024观察帧、13432过滤stock98请求/32比较帧，均actual0。有效过程API/compile/Property/UAV/fence/drift0、hook/Shader/kernel/pool/OUT恢复释放。正式1280含warm实际callback，Native9×1280+10×5120保持5轮。同源settings/common/time value-copy，物理RGBA16F GPU resolve先用branch0对生产发布逐帧校准1024次0差；stock raw/pub均值图与026逐byte同，候选raw均值及逐帧raw实际同，comparisonQualified=true。

**收益与失败：** 固定4seed64warm256，新18 reference L1 .462545→.266596（−42.36%），旧15 .304146→.208852（−31.33%），full2→4/4过，pooledRGB−4.167/−4.035/−3.671%，phase失败7/11/11/7→3/3/8/2，无pass→fail，但pooled仍3失败。方差×2.94/3.55/8.49/3.17，filtered/raw .00496～.00744过静态筛选，未验证动态。旧196141 B误差增6.299e−6>界5.913e−6；141157 G增2.171e−5>1.891e−5、B增8.084e−6>5e−6。旧B/reference .9435→.8732、.9963→.9126。原硬门槛不放宽，eligible=false，拒绝生产接纳，不继续8seed/HDR/Player/GPU。

**过滤/成本：** 原候选所有源字节/2patch/5Shader/meta/工具、40真实capture lossless gzip、4完整logs、272source对象/SHA和全部34点/相位/方差分析保留Tests~/RTGI/DiffusePolicy约20.6MiB，verify通过。新增类/meta/5shader/meta/覆盖入口已移除，Castle/LocalStage/SharedInput精确恢复HEAD源；过滤后stock/no-op32帧RGBA bitexact。当前build只写Tests重建目录，历史runner拒绝缺入口；不能回滚产品凑旧SHA。额外2NRD/多RT/读回只诊断，不叫产品GPU预算。四进程终态/active=null，白名单只证据和六RTGI文档，Ho-GD计划排除。

**后审：** 是有因果资格的机制收益与失败边界，未交付生产修复。F58禁止因聚合改善直接重开该完整bundle或放宽旧控制；不扫描phi/epsilon/迭代/ratio、不按坐标修复。038先保持同源两个独立分支，用原27+196141至少28点实际Pre/Temporal/Clamp/首Atrous/各ordinal的RGB/variance观察定位旧退化；observer先不改037原版和候选输出，才有限新采证据。新机制前不定算法变体，不重采MC/无新目的完整矩阵。所有原质量/F39/static/复杂动态/1080完整预算和总目标active继续。


**038直接执行顺序：** 先git status/goal/最新交接核现状；勿找已移除类，037 source-index/source-files中保留实际源码/生成Shader与SHA。恢复实验应新建Editor观察类而非改变Runtime，GI主baseline/branch0继续stock5，branch1固定037 math，IN/common/settings同源，OUT/pool/constants独立。对两分支在相同cmd拷真实stage output，不能额外debug-UAV改公式却称bitexact；先比037 qualification原baseline/candidate完整mean map和原27局部，再确认新增196141参考/guide一致及物理resolve校准。只观察所需有限seed，定位退化开始/后续恢复后才论一个新机制。保持F31/F39/F51/F53/F55/F58范围和全部未完成目标。


037已按白名单过滤提交`520935e`，DiffusePolicy verifier提交后再次通过。四个自有进程actual0/全部终态，1024观察帧同源stock与物理GPU校准bitexact；固定DI diffuse bundle新18/旧15总体收益42%/31%、full4/4，但旧196141 B和141157 G/B三通道硬退化，拒绝接入生产，候选覆盖/新增类/Shader/入口均移除并stock复核过。40真实capture/4logs/272源对象和全部收益/失败/取舍留存，Runtime/Editor无最终diff。提交后仅校正JSON属性变更留下的索引行尾缓存，HEAD与worktree原字节相同，没有重写证据。index空、仅他人Ho-GD计划未跟踪。

038继续同源stage观察：原27点加196141至少28点，对stock与固定037策略读取实际Pre/Temporal/Clamp/首Atrous及每个后续ordinal的RGB/variance；先短资格证明observer不改037两分支原输出/物理resolve，再有限seed定位旧退化与帮助传播，新机制后才定候选。不得放宽硬门槛或扫描参数/坐标特判，不重采旧MC/无目的大矩阵；F39/static/复杂动态/1080完整预算、原全部目标和每步review/过滤提交/踩坑/交接约束继续active。


### 038终态（2026-10-23，待过滤提交）：同源逐轮RGB/variance资格过，旧反例在后四轮恢复不足

**范围/资格：** 新Editor HoRTGIPairedStageAudit/meta、独立Gather shader/meta及Castle/LocalStage/SharedInput可选接线。原生产stock5/scale64/GI32F、Runtime/vendor/DI未改，037固定策略只在隔离Assets按5份原字节/SHA重建，用于观察，不接纳F58。保留原27点+196141=28点；两branch共享实际IN/common/settings/time，独立历史/OUT/pools/constants；只在原dispatch之后gather真实RGBA，不给滤波body注入debug UAV。112 tiny/negative/large F32分量自检bitexact，9stage原27点稀疏读取重调制对main F32整图copy逐帧bitexact。

1108短98请求/32观察帧、18596正式1314请求/1024观察帧 actual0，原4seed64warm256；API/compile/Property/UAV/fence/drift0，自有进程全终态/active=null。新增196141 full/RAB/history/ray/reference guide max9.336e−7。两branch完整raw/pub mean maps、候选原27点逐帧物理pub对037同模式逐bit同；branch0对main每帧demod RGBA同，物理GPU发布校准1024次0差。此资格允许解释阶段，非策略接纳。

**发现：** 28点Pre/Temporal/HistoryFix/Clamp全部RGBA两支相同；第一Atrous新18 L1 stock.130687/DI.130145近似同，后4轮stock .215150/.306872/.386896/.462370，DI .159167/.192195/.225485/.266351。旧141157 G/B首轮近似同，B/ref .82590/.82572，末.99666/.91288；196141 B首轮.75986/.75822，末.94380/.87350。原三通道物理pub硬退化精确同037，拒绝不变。收益与旧反例损失主要都在后4轮，不能归因早期history或只看整体误差。

首轮实际variance141157 stock7.276e−8/DI7.588e−9、196141 3.004e−7/8.064e−8；但196141 step4/8 DI反而约3.3/5.6倍stock仍恢复弱，排除“所有位置都只是variance太小”的笼统修复。141157 Clamp9邻真实moments离线重构preblur原/DI mean7.456e−8/6.673e−8、phi2带宽mean .0005026/.0004760、floor均0，首轮RGB几乎不变；恢复首轮blur不足以解释完整退化。尚不能把w²/min-pair/RGB/exchange各自因果分开。

**语义/边界：** Pre alpha hitdistance；Temporal/Clamp alpha二阶矩（离线variance近似，不冒充GPU直出）；本轮settled HistoryFix fast/responsive alpha全0，不当variance；9/101/102/103实际variance；104 historyLength禁止当variance。不逐通道乘albedo平方换variance。图表末stage是NRD RGB重调制、非物理RGBA16F最后量化值。4seed经验/同模型参考不是正式CI/独立真值/全图MC。没有新算法/MC/8seed/HDR/Player/GPU资格。

**过滤/成本：** Tests~/RTGI/PairedStages保留264原源对象、2完整logs、38 lossless captures、HGB1两支9stage28点RGBA、observer/analysis/figure/contract/filter-review/verify，全部原SHA与不变性核过。合格Editor观察工具保留，只测试入口开启、默认stock/noop路径不变、finally恢复；新增values8064B+coords224B只是sparse payload，外加2NRD/RT/读回不作产品VRAM/性能预算。可选Matplotlib图初默认/内置Python缺库，pip user与target冲突后显式--no-user隔离target装，仅plot进程路径使用，原环境/Unity不动，失败与版本/图SHA保留。白名单只4新Editor文件/3既有Editor接线、本轮证据和六RTGI文档，Ho-GD计划排除。

**039直接动作：** 现有实际证据允许一个单算子消融：仅把后4轮的固定center留重恢复stock行归一化，其余037 Smem temporal-var/w²、min-pair/RGB/settings/5轮/单位/格式不动。检验固定交换是否压制有益后期恢复，不预称修复。保持mainstock，把两独立branch设固定037参考/单算子变体以same input比较；物理校准改用main输出，不能沿用branch0==stock断言。先短资格两支stage9 RGB/variance bitexact、第一后轮同输入处出现算子差，再固定4seed旧15+新18/暗控制/full/phase/noise原门槛；失败保留消融并撤回，不扫描常量/迭代/坐标特判，不额外seed救回F58。所有原质量/F39/static/复杂动态/1080完整预算与总目标active继续。


038已按白名单过滤提交`c0ad3e2`，PairedStages verifier提交后再次通过。两自有Unity实际0且终态、112 F32 gather分量/1024物理校准/原27点九阶段copy逐帧bitexact、两支完整mean map和候选原27逐帧pub对037逐bit同；新增196141 guide通过。Editor观察工具合格保留、只专用入口激活，Runtime/vendor/DI/生产默认未改，F58仍拒绝。264源/38captures/2logs/阶段图与失败依赖记录完整，index空，仅他人Ho-GD计划未跟踪。

039以新阶段证据做单交换算子消融：固定037参考与只恢复后轮stock行归一化的变体，保留first Smem temporal-var/w²、min-pair/RGB、5轮/scale64/格式/参数。mainstock继续；两branch改参考/变体时务必同步物理校准和观察断言，不得沿用branch0==stock，优先同源三输出避免历史配对。短资格stage9两实验支RGBA相同、stage101同输入出现唯一算子差，再原四seed/旧15+新18/暗控制/full/phase/noise原门槛；不能称已证明exchange唯一原因。失败撤回保留，不以额外seed、常量扫描或位置特判救回，合格才完整部署资格。全目标与原质量/F39/static/复杂动态/1080完整预算、每步前后review/过滤提交/踩坑/交接持续active。


### 039终态（2026-10-23，待过滤提交）：归一化消融确认取舍，拒绝并暂搁置局部公式试验线

**设计与验证：** mainstock、branch0固定037 DI参考、branch1只将后4轮diffuse交换block恢复stock行归一化，RGB/variance一起沿原表达式，Smem temporal-var/w²、pair/RGB/其它参数/5轮/64/32F不动。源码逆替换逐byte同、wrapper仅include名变；三输出同实际IN/common/settings/time和独立资源。物理校准独立main输出，不误用branch0==stock。

四自有进程34400初局部资格98请求/32帧、19860补强98请求/32帧、39728正式1314请求/1024帧、25112过滤038模式98请求/32帧 actual0、API/compile/Property/UAV/fence/drift0、全部终态/active=null。初资格不补写整图证明；补强两个F32 RT每帧比较首轮整图262144RGBA，正式268435456分量/1024帧全bitexact，差异从首个后轮算子开始。112F32 gather过；main与DI参考完整NRD逐帧SHA、参考28点九stage/物理mean map对038同，guide max9.336e−7。只归一化干预含后续variance反馈，不夸成唯一因素。

**机制收益/质量失败：** 旧141157G/B、196141B收回DI损失89.386/91.025/98.145%，原一半恢复预测过，无新硬退化。但新18 L1 .462545→.447479仅−3.257%（DI为.266596），旧15 .304146→.284750仅−6.377%（DI.208852）。full仍2/4、DI4/4；pooledRGB−4.483/−4.809/−4.548%、phase7/11/11/7→7/8/10/6无pass→fail但pooled6失败，方差×1.77/2.34/7.17/2.01、filtered/raw .00299～.00628。原20%new18门槛未过，eligible=false，拒绝生产，不继续8/HDR/Player/GPU；机制成立不能代替足够产品收益。

**过滤/交接：** Tests~/RTGI/ExchangeOperator约41.3MiB，272源、4logs、72lossless captures、初/补强/正式源与生成体/全部34点与full/phase/noise/图/contract/analysis/filter-review/verify保留。新增ExchangeAudit/meta与3个既有Editor exchange接线/入口移除、精确恢复1cb030f，过滤038实际资格过；Runtime/DI/vendor一直未改。诊断额外2F32copy/第三physicalRT/2NRD/读回不作产品预算。只证据和六RTGI文档白名单，Ho-GD计划排除。

**按用户规则搁置：** 029以来局部公式方向虽有机制认识、始终无可保留生产改进，暂搁置归一化/DI局部公式试验线，不再排插值/phi/epsilon/迭代/ratio或位置特判。质量缺口不关闭，未来新的结构性机制/参考链路证据再开。F58/F60继续拒绝，不把单一消融成功算目标完成。

**040直接动作：** 转原实际交付缺口。先核F39最小原生增长probe、018失败原源和当前引擎/host/官方诊断是否提供新机制；静态owned mirror/base0已在018做过，原Ho/纯Unity3→5也scratch错而initial5干净，不能重新抄017称新方案。禁止盲试flags/Static/recreate/delay/capacity或原样重跑15矩阵。无可行动新证据则保留static缺口，推进固定实例复杂动态Player/全帧成本独立验证（不能冒称F39解决），原质量/F39/static/复杂动态/1080完整预算与总目标仍active。


039已按白名单过滤提交`8b376bc`，ExchangeOperator verifier提交后再次通过。四自有进程actual0/终态，正式1024帧整图首轮RGBA全同、main/DI参考对038全NRD输出逐帧SHA同；单归一化干预收回旧点89～98%损失却只留新18约3.26%改善，eligible=false，变体类/meta/模式/入口撤回，038观察恢复实跑过。272源/72captures/4logs/全部收益失败/算子图/初局部与补强资格完整留存，Runtime/Editor无最终diff，index空，仅Ho-GD计划未跟踪。

按用户长期无可保留收益需搁置要求，局部归一化/DI公式试验线暂搁置，不接着扫插值/参数/迭代或依据现有两端数据再排公式。040改推进其它实际交付缺口：先读F39 StaticMesh README/归档源/原生HoRTAccelerationGrowthProbe与当前引擎/host/官方诊断，旧mirror/base0和原Ho/纯Unity3→5均scratch错、初始5干净，不能再抄canonical helper当新方案或原样重跑flags/recreate/delay/capacity。只有新的可行动证据才重开AS实验；否则显式保留static缺口，推进固定实例复杂动态Player和完整帧成本独立验证，不冒称F39解决。完整画质/静态/动态/1080预算及总目标和每步review/过滤提交/踩坑/交接规则继续active。


### 040终态（2026-10-23，待过滤提交）：两后端多接收面动态Player通过，证实48/48全局reset限制

**实际范围/收益：** 新ComplexMotion独立probe和Player builder可选入口（旧默认不变），全Runtime/Shader/DI/vendor/生产参数无改动。固定8初始ray instance、3owned skin snapshot、1Cutout；额外倾斜/移动primary接收面、持续bone+blendshape发光面、移动ray-only遮挡，末段Cutout/红蓝开关/Volume关开/强度.5。复用017已验证sourcebase4构建/ownedbase0代码，本步未重新逐mesh枚举该metadata；不是F39增长资格或复杂大场景MC画质。

RG build36184/Player10356、compat build35500/Player40688 all actual0，serialized enabled camera/target=null/512屏幕，240原生连续camera callback与同帧WaitForEndOfFrame截图，无人工render/request/Volume.Update。各240观察含Volume-off16，实际224 GI更新连续；生命周期4.384/4.331秒不是全process/GPU预算，外部10秒可见界限沿用户授权。每后端22checks、instances8/skins3/cutout1全程；48motion实际Bake48、StructureRevision恒2、停姿Bake0并恢复temporal。

三ROI全部RGB合成误差强度1 max.007227/.007213、.5均.005216<原.015；Volume-off屏幕恢复raster误差0。Cutout封闭首帧RGB raw/filtered0；蓝关首帧旧蓝max.0002688<.005、红关首帧旧红0，另一色保留，Volume重开首帧reset。RG/compat raw三ROI逐帧float相同，filtered最大差.0006831/screen.0004029真实保留。build仅原五条精确warning，runtimeD3D12/compile/Property/UAV/fence/drift0；原GB fallback缺省提示仍在、夹具全lilToon路径不证明fallback支持。18真实PNG已看RG合成和compat运动，受控色块/多表面/位移符合预期。

**新瓶颈/参考核对：** 两持续运动段均48/48 NRD reset、GI raw temporal0，当前全scene.Revision令NRD无时间累积，必须记为收敛限制，不能因响应checks通过称动态画质完成。已读并冻DI31/32过程、VisibilityConfidence/currentDI、PrimaryMotion与固定RTXDI FullSample NRD：DI直接忽略world reset虽降噪，阶跃变化像素L1 .5709>.35失败；后续真实同reservoir/light采样当前TLAS重求confidence才保留NRD。GI现在黑MV/白confidence且无同类动态保护。不能直接换StructureRevision/强设CONTINUE，GI reservoir保守失效继续；下一步先审同GI历史采样重求的元数据/RNG/primary对应与独立oracle可行性，再限域生命周期候选，不重开局部滤波参数线。

**F39有限审计：** 当前Unity6000.3.15f1/RTX4060/617.42同018。最小probe确实Add后Build，旧mirror/base0已做、原Ho/纯Unity3→5也scratch错、initial5干净。有限核UnityAPI/Microsoft scratch契约/Unity内存文章/本版notes未识别可行动新fix，不推断所有上游/后续版本均无fix，URL/局限native-audit保留；未重跑旧flags/mirror/growth矩阵，F39仍开。

**过滤/失败保留：** 1163去重精确source对象、4完整build/player logs、两套全payload逐文件SHA/无HoGIGpuProbe.dll、2原report lossless gzip、18PNG/analysis/contract/filter-review/reference/verify完整。初RG wrapper同ordered记录persist后再append成双entry，原JSON与源码保留initial-metadata，去重为一次真实build/Player；compat wrapper已修、无重跑RG。offline verifier最初把Unity空error字符串误当null，按无error文本修、原报告SHA不变。只保留Editor可选构建/本轮证据/六RTGI文档白名单，Ho-GD计划排除。截图/读回/反射JSON开销不叫产品全帧GPU成本，原全质量/F39/static/复杂动态MC/环境混合/1080完整预算及总目标active。


040已按白名单过滤提交`b4e8b65`，ComplexMotion verifier提交后再次通过。RG36184/10356、compat35500/40688四自有进程actual0/终态，各240真实camera帧/224GI更新、22checks、8固定instances/3skin/1Cutout，18真实PNG及完整两payload核SHA；Runtime/Shader/DI未改，Editor仅可选构建入口/场景。1163原源/4logs/初RG双entry元数据与修正保留，当前source看compat，旧RG按原snapshot。index空，仅Ho-GD计划未跟踪。F39同host/有限主源核查没有新行动依据，未重跑旧矩阵，不能称已解决。

041先沿用户要求看DI过程/固定参考而非再扫滤波：已冻DI31/32持续记录片段、DynamicVisibility/PrimaryMotion/VisibilityConfidence与代码；直接保NRD平均噪声虽降但阶跃changedL1 .5709失败，生产依赖同旧采样当前TLAS重求confidence。GI motion实测48/48 reset/raw temporal0是新收敛瓶颈，但当前GI黑MV/白confidence且未接相同保护，禁止一行改StructureRevision/CONTINUE。先逐字段核GI历史packet/secondary transport/来源pixel-frame/RNG、primary匹配、灯/环境域，建立同历史GI采样重求和独立oracle/同源比较所需契约；缺元数据明确记录，不能新采样充当confidence。保留GI reservoir保守失效、旧颜色/Volume硬重置，正确性和变化像素残影/噪声证据后才限域生命周期候选。归一化/phi/迭代公式线继续搁置，原全质量/F39/static/复杂动态MC/环境混合/1080完整预算及全目标与review/过滤提交/踩坑/交接规则active。


### 041终态（2026-10-23，待过滤提交）：同采样信息必要性/SDK GPU契约通过，CPU诊断字段标签修正

**源码结论：** 实际secondary48B不跟随GI选样，选中DI light/UV/invPdf未存；GI32只位置/normal/LogLuv聚合radiance/GI weight/M/age，age非sourcepixel。NRD来自Spatial1真实Shade，history0却pre-S2，不能互换。不能由位置/颜色反查来源，也不必重生成旧RNG/RIS候选：先保存原选中选择和DI invPdf（非GI weight），还需原secondary BRDF的source-viewDir/sample-kind、T/S来源继承与实际lastShade捕获、当前secondary匹配和两段visibility。详contract，尚非生产实现。

**GPU与独立oracle：** 20788初actual1，HLSL保留字point造成3 compile error，未写buffer后的CPU错不代表ABI失败；原源/log/report/binary保留。修名lightPosition、同步/后dispatch检查。45904小D3D12 compute/no-AS actual0、compile/Property/UAV/fence/debug/drift0。原SDK Pack/Unpack/Combine直接执行，64案例：32旧GI32字节碰撞而当前旧选择响应不同；64默认roundtrip应用misc/unused丢失；64quantile实际选B48次。opaque aux四word按select bool复制，独立sentinel不作transport权重，未证明真实Shade/高层T/S关联。双点灯Lambert GPU AABB与独立CPU中平面oracle max3.674e−8<1e−6；64同选择静态bitexact，换另一选择负对照32静态假变化、碰撞中16漏真实变化/16假变化。非真实TLAS/alpha/area/env/spec replay或confidence资格。

**修一个诊断实错：** SDK GI32偏移0pos/12misc/16LogLuv/20weight/24normal/28unused。旧HistoryFeedback.Packet与RuntimeProbe.Packet把12标normal、16标misc、24标radiance；未来已校正并加GI32-v2。两个CPU类型经实际GPU sentinel/Marshal offset核对，1024次逐word翻转保证Equal/Same仍覆盖32B。旧全包byte-copy判据和texture质量/能量结论保留；旧normalMismatch实misc、旧radianceMismatch实normal，未另记全局true radiance mismatch，只有全包0才可推全部字段同。原报告/源不改，两历史README加显式标签提醒，映射legacy-layout-review。

**过滤/成本/边界：** 保留新Editor helper/shader/meta、两个CPU诊断布局/schema修正、本轮证据及文档，不改Runtime/vendor/DI/NRD/重置。ReplayContract有262源对象/2完整logs/4lossless报告binary、初失败/独立oracle/源映射/contract/filter-review/verify，全部SHA核过。两个GraphicsBuffer逻辑payload75,776B不是总GPU分配；假设16B×3pool+48B lastShade在256²6MiB、1080约190.55MiB/相机，未含gradient/对齐/原生分配，16B是否充分仍待证，不直接申请1080。无Player/AS重跑，生产motion48/48 reset仍未修。Ho-GD计划排除，原所有质量/F39/static/复杂动态/环境/1080预算和总目标active。

**042直接动作：** 低分辨率真实GI T/S/Final来源继承观察资格，而非继续toy或放开reset。评审GI私有helper寄存器source-pointer/sidecar在Load/选中/Store传播，原SDK/DI不改、GI32字段/原结果须对照；覆盖初始、temporal所有分支/fallback、空间选样、S-off、pre-S packed history copy与Spatial1 lastShade，碰撞不能靠float匹配。证明真实关联与主输出无影响后才当前TLAS同旧选择重求/独立oracle/变化像素响应。保留GI reservoir保守失效和旧色/Volume硬重置，confidence正确性与成本前不改NRD生命周期；局部phi/归一化/迭代线继续搁置。


041已按白名单过滤提交`aa2bf78`，ReplayContract verifier提交后再次通过。20788初编译actual1完整保留，45904 actual0/终态：原SDK64case、32GI字节碰撞、64默认metadata丢失、quantile48选择、1024双CPU逐word比较和独立oraclemax3.674e−8。已修未来HistoryFeedback/RuntimeProbe的GI32-v2诊断标签，历史源/报告不改、原full-byte/texture质量仍有效，分项标签更正两历史README。262源/2logs/4lossless captures/契约/原失败/成本假设完整，Runtime/vendor/NRD/生产reset无diff，index空，仅Ho-GD计划未跟踪。

042不再重复toy合约：按ReplayContract/contract.md做低分辨率真实T/S/Final来源继承观察。优先保存实际次级DI选中lightData/UV/invPdf及原secondary source-viewDir/kind，避免恢复旧RNG/RIS表；aux16B仅候选、flags/normal/量化充分性待证。GI私有helper可评审寄存器source-pointer、Load/选择/Store的sidecar继承，但必须含初始生成、temporal fallback/无效、spatial/S-off和Final的pre-S raw copy/真正Spatial1 lastShade；不改原SDK/DI、不按float反匹配，须主包/原结果对照与真实多灯/环境/Cutout观察。手工Combine ledger只opaque字段见证（7.5sentinel不作oracle权重2），不是已有真实来源实现。先证明该关联再当前TLAS同选择重求/独立oracle，端点当前secondary也必须匹配，不能只测开区间primary->旧点visibility。NRD confidence正确性/变化像素残影/噪声/能量与成本前不放开历史，GI reservoir保守失效、旧色/Volume硬重置继续；hypothetical1080额外190.55MiB不可直接分配。全质量/F39/static/动态/环境/1080预算及完整目标与review/过滤提交/踩坑/交接继续active。


### 042终态（2026-10-23，待过滤提交）：真实GI来源继承的常规T/S/Final两后端观察资格通过

**实际实现/范围：** 64²合法古堡18灯/环境/Cutout，原版和私有lineage GI feature同相机/设置/seed/scene。Editor生成5份私有Shader/helper（原许可与exact patch/SHA保留）：寄存器source-pointer随原SDK Combine选样继承，Load标源pool地址、Store拷sidecar，初始保存真实DI lightData/UV/invPdf/source-viewDir（无DI UV域kind）；origin stamp只审计。Final补pre-S metadata copy、捕获真正Spatial1 lastShade。原GI32外存字段不变，Runtime/vendor/DI/NRD/reset未改，原scope默认不启用诊断。

**有效资格：** 49456短32frame、45856RG/47228compat各128frame actual0，4种T/S开关各8/32frame。有效进程compile/Shaderwarning/Property/UAV/ASdebug/fence/drift0，终态/active=null、hook恢复/trace关停/资源释放。每正式后端原3pool GI32共1,572,864包逐bit同，raw/NRD publication全RGBA逐bit同且逐帧双SHA保留。独立Python不可变initial ledger按(origin frame,pixel)查并核metadata/position bits/age/stamp，不按float反查；各2,097,152次来源检查。T-only/TS旧帧来源116198/116328，S-only/TS不同origin71507/47244；history0对pre-S Temporal2（S-off对应Spatial1）和实际lastShade对Spatial1包/meta/origin都同，两后端数据结果一致。

**量化/覆盖界限：** 每后端105670 selected normal word与initial不同但原版/trace完全同，属于原SDK编码往返；LogLuv差0，不得按normal word等initial反查来源。initial kinds仅1/2。本轮没有穷尽age-limit/Jacobian拒绝/disocclusion/camera-motion fallback、kind0/3、world/light/alpha/Volume/resize/重建所有分支；预审全面覆盖要求尚未全部证明，pass只限已观察常规路径。未作当前TLAS transport replay、confidence、动态收敛、Player/1080预算接受。

**两失败保留：** 49216actual1：拷贝Shader使用HLSL保留字linear有4compile error，同时private FinalNRD12UAV warning；未写stagebuf见证空，旧runner还查256错路径fresh=false，实际64report/partial bin原字节手动保留，不补写fresh。38748actual1：修pixelIndex/路径/编译与非空检查后12UAV warning仍拒绝。将原5个额外UAV合为一个RW uint4记录buf，原数学不变后才有效资格，无D3D12豁免warning。

**过滤/成本/数据：** 保留合格Editor controller/capture/entry/meta和Castle可选接线；Runtime/vendor无diff。lineage/origin/lastShade/5stage capture逻辑payload1,966,080B=1.875MiB，不含第二GI AS/NRD/engine/readbacks，非产品预算。SelectionLineage有278源对象、5完整logs、10report/stream，完整大流xz无损（原raw SHA重验，约42.3MiB），初两失败源码/report/partial stream和全部case全保留，verify独立核所有包。当前源对应compat，旧源独立归档；只本轮Editor/证据/六RTGI文档白名单，Ho-GD计划排除。生产motion48/48 reset与原所有质量/F39/static/复杂动态/环境/1080缺口仍active。

**043直接动作：** 先补未证的真实分支/生命周期来源观察，需显式event/场景见证，不凭常规pass给age/Jacobian/fallback补资格，保持同源原输出严格对照。随后小尺寸原DI选择静态当前TLAS重求与独立oracle：所选来源的secondary材质/normal/原viewDir需可重建，诊断可临时丰富记录但内存/生命周期明示、不能直接推广1080或把辅助字段当充分ABI。当前secondary点要验证、两段visibility、旧零信号、量化/SignalScale64/原weight一致，不新RNG。选择关联/实际重求/变化像素残影/噪声/能量和成本都过后才NRD生命周期候选；GI reservoir保守失效、旧色/Volume硬重置继续，局部滤波公式线搁置，全目标active。


042已按白名单过滤提交`543325c`，SelectionLineage verifier提交后再次通过。两初actual1（49216 capture关键字/错256path、38748UAV warning）原源/数据保留；49456短actual0、45856RG/47228compat各128frame actual0，正式每后端1,572,864原包与raw/NRD输出逐bit同、2,097,152不可变来源检查，pre-S history与Spatial1 actualShade对应成立。278源/5logs/10lossless captures约42.3MiB（大流xz rawSHA复核），Editor诊断合格保留、Runtime/vendor/生产reset不动。工作树clean；期间他人独立提交8d7c657“描边计划”已作为父提交保留，未纳入本轮白名单/未改写。

043必须补已明确未证明的age/Jacobian/disocclusion/camera视野、kind0/3和资源生命周期来源分支，需实际event/场景见证，不将042常规pass扩成全覆盖。保持真实双GI原包/物理输出严格同源对照；normal codec105670word变化在原版也有，别用normal/radiance float反查来源。补齐关联后才小尺寸静态当前TLAS同DI选择的secondary重求/oracle，必要诊断可临时保留原secondary材质/normal数据以核viewDir/量化，但成本明示且不推广1080。主点到旧点开区间可见性不证明当前secondary仍存在；旧零/双段shadow/alpha/env及原weight/SignalScale一致需独立验证。当前GI reservoir保守失效/旧色Volume硬重置和NRD reset都保持；局部滤波公式线继续搁置，原全质量/F39/static/复杂动态/环境/1080预算及完整目标与review/过滤提交/踩坑/交接规则active。


### 043终态（2026-10-23，待过滤提交）：真实边界事件/生命周期来源两后端通过

**范围/实际：** Editor私有Temporal只增加event，单RW uint4记录buf追加N个word，不新增UAV槽；固定64²五fixture双GI原版/记录版：ageLimit2、相机平移/两帧转向、cullingMask0恢复、finiteRange.01+direct-sky黑结果、同实例集合feature服务重建。生产Runtime/vendor/DI/NRD/reset和参数不改，不是滤波调参或F39增长修复。47056短40frame、48524RG/17244compat各80frame allactual0、compile/warning/Property/UAV/ASdebug/fence/drift0、终态/active=null。

每正式后端983040原GI32包与raw/NRD全部RGBA逐bit同，1245184不可变initial source/meta/position bits/age/stamp检查；history/pre-S和lastShade/Spatial1对应保留。年龄拒绝16687，原ageBefore+1确实>limit；相机case Jacobian拒绝981、material27、neighbor6961、noMatch4556、projectionInvalid8192，原Jac非有限/>10/<.1条件核过。kind0/primaryInvalid16384、恢复时prevSurfaceInvalid4096/noMatch4096，无效帧全部M0/meta0。有限距离kind3 58529，仍计黑样本M；服务generation1→2，重建后不继承旧来源/ledger，前帧完成读回后才释放hook/buf，两feature同seed，新control context刷新后基础验证继续。两后端事件/来源结果一致。

**可达性/边界：** production zero-init的fallback/permutation/activecheckerboard0未开启，不为coverage打开，也不声称执行过。previousReservoirInvalid事件0；source invariant有效primary→flags1/2/3→MakeM1，T/S纳入有效current并累积M，完整history正M，新/resize无效history先禁T；故matched有效previous primary却M0非正常producer预期，不是该防御分支GPU执行/故障注入或未来普遍证明。Reachability逐源SHA保留。material拒绝已真实27次，不能继续标未覆盖。没有当前secondary transport重求/confidence/动态MC/Player/1080预算接受，生产motion reset不动。

**过滤/成本：** 合格Editor事件/fixture/重建支持保留；事件比042多64KiB/64²，诊断逻辑buffer2031616B=1.9375MiB不含第二GI AS/NRD/engine/readback，非产品VRAM/时间。LineageBranches有272精确源/3完整logs/6lossless report+stream、大流xz原rawSHA复核，独立Python重算全部包/source/event谓词；无失败降级。只本步Editor/证据/六RTGI文档白名单，完整目标及原质量/F39/static/动态/环境/1080预算active，局部滤波公式线仍搁置。

**044直接动作：** 已有普通+边界真实来源证据，推进64²真实currentTLAS同选择secondary静态重求/oracle，不继续仅toy。需从选中来源获取原secondary材质/normal与source-viewDir，诊断可临时丰富记录（明确payload/限制，不照搬1080）；原DI light/UV/invPdf/原weight、emission/quantization/SignalScale64保持，禁止新RNG。先静态同选择输出核对与独立oracle，normal codec往返不能误当来源错；当前secondary端点匹配/主面起点/两段visibility、零→亮/alpha/世界域变化再验证。原GI reservoir保守失效、旧色Volume硬重置和NRD reset在正确性/响应/噪声/能量/成本前都不放开，每步review/过滤提交/踩坑/交接继续。


043已按白名单过滤提交`e05b4d7`，LineageBranches verifier提交后再次通过。47056短40frame、48524RG/17244compat各80frame actual0/终态，所有shader/ASdebug/绑定/fence/drift0；每正式后端983040原包/raw/NRD逐bit同、1245184不可变来源检查。Age16687、cameraJac981/material27/noMatch4556/clip8192、kind0 16384/恢复、kind3 58529、服务代际1→2均有真实事件/独立原谓词和旧来源失效核对。272源/3logs/6lossless captures约27.8MiB，Runtime/vendor/reset未改，index与工作树clean。他人描边提交仍保留。

044按已建立的普通+边界来源观察推进静态真实currentTLAS同DI选择secondary重求/oracle，不继续仅增加toy覆盖。审计允许在64²私有记录暂存原secondary材质/normal（明确payload）以重建实际来源，验证原lightData/UV/DI invPdf/source-viewDir（量化影响单独核）及emission、GI编码/SignalScale64/旧weight。不得新RNG/反查float来源；实际Spatial1 lastShade与preS history仍分开。先同世界/同选择输出对照、独立oracle，再世界变化时当前secondary端点/主面匹配和两段visibility、旧零信号/alpha/灯环境域；选择来源资格本身不等于confidence/动态画质。原GI reservoir保守失效/旧色Volume硬重置与NRD reset都在正确性/响应/噪声/能量/成本前保持。SDK可选fallback/permutation/checkerboard未启用；matched validprevious M0防御分支没执行，当前producer invariant不是普遍/故障注入证明，未来producer变更须重审。局部滤波公式线继续搁置，原全质量/F39/static/复杂动态/环境/1080预算及完整目标与review/过滤提交/踩坑/交接规则active。


### 044终态（2026-10-23，待过滤提交）：真实currentTLAS同DI选择secondary静态重求通过

**实现/范围：** Editor64²诊断暂存来源secondary48B和exact source-view float3，真实DI lightData/UV/invPdf、原emission/normal/材质随来源继承。独立CaptureReplaySource在Generate后读取原输入，避免原Shader局部量观测；单RW buf，产品Runtime/vendor/DI/NRD/reset不改。原FinalVisibility后/Final前native重求Spatial1来源的secondary->同灯/环境样本currentTLAS visibility/radiance，无新RNG；重复primary->cachedPoint开区间对原visibility，仅静态限定、端点geometry尚未验证。

35372短16frame、43728RG/34696compat各64frame allactual0、compile/warning/Property/UAV/ASdebug/fence/drift0、hook/资源恢复。原GI32全部pool与raw/NRD所有RGBA逐bit同；每正式后端262144 selected samples exact-view replay LogLuv与cache0差、primary visibility0差，实际解析灯107499/环境135571。packed16view有2756 word差，radiance maxabs.0159502、relative p99.00030657/max.00156663，不当strict等价/充分16B记录。

**独立oracle：** double oct/material解码+Lambert/GGX/Smith/Schlick和sample radiance/DI invPdf/pdf+emission重组，component门槛max(5e-6,.003*abs(ref))，正式maxabs.000818014但0超界；实际light buffer解析attenuation maxabs.0000413005/0超界，env UV/解析方向maxabs7.628e−7<1e−5。GPU visibility/env texture radiance是记录输入，不冒称独立CPU TLAS/alpha/环境纹理oracle。两后端结果一致。

**严格失败/隔离：** 46880初actual1，前2frame exactreplay/输出正常，1002frame1293包parity错；47620 capture-only禁replay dispatch仍同点actual1，只有packed radiance1176951723/1176689579不同，其它packet字段同。原数据/源/partial2frames保留，不放宽。把exactview/材质读取从原Shade局部路径移到独立观察dispatch后严格parity恢复；不进一步无证归因特定编译/排序或驱动。初partial不是整组资格、capture-only未写replay buffer不是transport证据。

**过滤/成本：** 保留Editor可选静态replay/独立观察/失败witness和验证入口，SDK原文件不改。SecondaryReplay281源对象/5完整logs/10lossless report-stream、原两失败/CPUoracle/各量化差/contract/filter-review/verify齐全；完整大流xz原raw SHA复核。逻辑buffer3538944B=3.375MiB/64²非总VRAM/第二GI AS-NRD/射线/读回预算。只本步Editor/证据/六RTGI文档白名单，旧他人描边提交保留。未接动态confidence，生产motion reset限制仍在，全质量/F39/static/动态MC/环境/1080预算和总目标active。

**045直接动作：** 受控current secondary端点重建/normal/material匹配和previous actual lastShade重求。保持原choice/weight/原sourceview、当前matched primary，固定实例移动secondary/遮挡和零→亮/alpha几何oracle，不把开区间到旧点可见或旧材质重放当当前端点仍有效。正确性/变化像素响应/噪声/能量/成本之后才NRD生命周期，GI reservoir保守失效与旧色Volume硬重置继续；局部滤波公式线搁置，每步review/过滤提交/踩坑/交接继续。


044已按白名单过滤提交`5abcdf1`，SecondaryReplay verifier提交后再次通过。46880初parity/47620禁replay同点parity actual1两失败完整；独立Generate后capture隔离后35372短16、43728RG/34696compat各64frame actual0。原GI32/raw/NRD所有RGBA逐bit同，每正式262144samechoice exactview currentTLAS secondary replay cacheLogLuv0差，primary开区间visibility0差；107499解析/135571环境真实选择。CPU独立BRDF/解析灯/方向oracle无超max(5e-6,.003ref)component，GPUvisibility/env纹理不是独立CPUoracle。packedview2756word差/maxrel.00156663单列，16B充分性不接纳。281源/5logs/10lossless captures约39.4MiB，Runtime/vendor/reset未改，工作树clean。

045从静态cached材质重放推进**previous actual lastShade在当前世界的secondary端点有效性**：受控固定实例移动secondary/遮挡、静态matched primary先行，用旧choice/DI invPdf/sourceview/GIweight，当前TLAS重建命中/normal/material及两段visibility，与独立简单几何oracle核；只到旧点的开区间通畅不足以证明旧secondary存在。当前私有Final之前lastShade区仍是previous帧，可评审其读取/重投影时序，Final才覆盖；不能拿当前Spatial1重求冒称旧历史。必要新增小尺寸诊断字段/当前命中payload与成本明示，原缓存数据不能当新表面。逐步零→亮/alpha/灯环境域及currentprimary匹配，正确性/变化像素响应/噪声/能量/成本前不改NRD生命周期；GI reservoir保守失效、旧色Volume硬重置保持。原F39/static/全质量/动态MC/环境/1080预算和全目标与review/过滤提交/踩坑/交接规则active，局部滤波公式线继续搁置。
