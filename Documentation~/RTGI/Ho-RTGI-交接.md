# Ho-RTGI 当前交接

更新：2026-10-20。目标active，未完成。步骤005独立RELAX已提交95120a4；步骤006真实应用帧/空间质量与两个Mono Player已过滤提交035b9f9；步骤007材质排除已过滤提交20449c3；步骤008已提交6ebe065；009已提交4ccd02b；010古堡接入/filtered失败证据已过滤提交010b8cd，qualityQualified=false；011已取得有效GPU timestamp与古堡分段，待过滤提交；010质量仍失败。下一项实际场景输入支持与GPU预算；不再围绕均匀夹具无限调NRD参数。

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


### 011终态（2026-10-20，待过滤提交）

限定GPU桥已有有效数据，不再是0 duration。源码Tests~/RTGI/GpuTiming/Native/HoGIGpuProbe.cpp，DLL只生成在GI项目Assets/Plugins/x86_64。官方Unity IUnityGraphicsD3D12v7，ConfigureEvent：初始化Allow queue线程读频率/创query，Begin/End DontCare当前commandList EndQuery，Resolve在camera外另事件；目标frame fence完成后读READBACK。无device创建/NRI/DLSS/descriptor hook，HoRTDINRD未变。初strict empty失败报告保留，empty允许有序同tick，非空Compute必须正。

Runtime自有三个文件只加UNITY_EDITOR条件hook/标记（Context/Feature/Composite），无Shader/SDK/NRD算法改变。Editor/GpuClock侧Record/Collect，Raster内只EndQuery，Resolve延后避免原生renderpass限制。Total不含Composite，AS稳定无build可0，强制frame0有真build。

正式Tests~/RTGI/GpuTiming/run_validation.ps1，exec97588已收：qualification49120，256RG51280/compat50740，512RG50460/compat54488，total-only43680，rebuild54928全exit0/编译诊断0/fresh/marker/SHA成功。40帧每marker40样本（PDF冷1），有效频率1GHz、slot唯一/fence已完成。Total median 256RG1.18784/compat1.360896，512RG3.991552/compat3.926016ms；Composite .016384/.019456/.023552/.03072ms。Total-only1.191936近全marker1.18784；AS强制frame0 2.694144ms、PDF.016384ms。p95波动与所有tick/fence在报告，不宣称稳定预算。

插件隔离配置初发现Linux/OSX默认enabled，已禁所有Standalone，只有Windows Editor/x64；模板ConfigureEditorPlugin.cs复制自有Assets/Editor执行。exec53148终态已收：配置42036 exit0，Release build28628 exit0。额外PlayerGISkinnedNoGpuProbe目录不覆盖历史，实跑PID57360 exit0、315请求，全旧Skinned/Cutout/动态/合成过，payload没有HoGIGpuProbe.dll。release-regression.json保留。TestsGpuTiming/prepare_probe.ps1可编译+配置；源码VS2022/SDK/UnityAPI/DLL hash都记录，binary不入产品。

GpuTiming/verify_validation.py已过：7真实GPU进程、known-load/fence、当前8份源码、日志/报告、原生源码/DLL与Release无probe都核对。evidence.gpuCostQualified=true且filteredQualityQualified=false。当前所有本任务进程结束，active.json/run.json无活进程。其它Ho-GD plan未跟踪文件属别人，不暂存。

待提交仅上述3 Runtime RTGI文件的Editor hook；Editor/RTGI GPUClock/Qualifier+meta、Probe.compute/meta、Castle GPU入口、Player隔离Release入口；Tests~/RTGI/GpuTiming及RTGI文档/计划。无需更改DLL/vendor/系统/其它工程。旧010场景verifier源码SHA现因合法GPU入口变化需按010历史commit用，不能覆盖新工作去满足旧verifier。

下一项012显存与实际分辨率/输入域重建、完整应用条件。512/256是整个camera变域比较，不冒称已实现全分辨率GB/SB上的半GI；Total不含GB/SB/Atmo/Forward及CPU扫描/Bake，非完整presented-frame。需要记录实际context/AS/pose资源payload并再决定优化。正常enabled-camera呈现仍未验。010原版RELAX长窗RGB约低6～7%质量失败，sigma4/6宽色盒已搁置，不重启gain/更宽sigma。Goal active，不能完成。
