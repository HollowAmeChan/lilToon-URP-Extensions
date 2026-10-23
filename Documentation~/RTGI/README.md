# Ho-RTGI 持续推进入口

本目录为当前任务的持久化入口。目标：沿本机 UnityPathTracing 参考，交付场景可用的一跳漫反射 GI；每步 review、过滤提交、记录收益与失败，并支持压缩后的连续推进。

接手顺序：

1. [实施规划与强制交付条件](../计划/Ho-RTGI-v0.1-实施规划.md)：功能目标、用户要求、范围、阶段与验收。
2. [当前交接](Ho-RTGI-交接.md)：当前真实状态、进程、未提交工作和下一动作。
3. [踩坑与禁止重复尝试](Ho-RTGI-踩坑与禁止重复尝试.md)：已知失败与重开条件。
4. [过程记录](Ho-RTGI-过程记录.md)：逐步依据、实现、验证、review 和提交。
5. [既有原型说明](../Ho-RTGI.md)：历史实现与测试，涉及当前支持域/资源数值时以当前源码重新核对。

状态：目标active。M0、次级天空、正常相机加法合成和独立RELAX_DIFFUSE已有实测；步骤005降噪矩阵/回归通过，步骤006两个Linear D3D12 Mono Player各304次请求证明真实应用时间、非均匀反弹与对应响应/合成。正常enabled-camera自动呈现、复杂薄边/小高亮源、目标场景GPU预算及完整Player矩阵仍在后续范围。代码/文档存在不等于全部功能已验证；DI第108步关闭GI的记录不能充当GI验收。

步骤007已提供独立材质身份过滤；步骤008基础Cutout已通过10项Editor与两个309请求Mono Player，覆盖初始/灯光/天空/重采样/最终alpha、NRD距离和真实应用帧响应。骨骼GI vertex fetch、完整实际场景、有效GPU预算仍未完成，不能由registry或这些局部夹具替代。

步骤009补齐骨骼GI实际UV/法线/BlendShape、遮挡/反弹证据，六项Editor及两个315请求Mono Player通过。完整实际场景、复杂角色动态接收、正常自动camera与有效GPU预算仍继续，不将这些小夹具扩称整体目标完成。

步骤010已实际导入/渲染古堡并输出GI开关图，但发现原版filtered RGB能量门槛失败；两轮色盒诊断搁置、Runtime不改，失败保留。目标active，接下来继续有效GPU取证与该实际质量缺口，禁止用功能四组通过重定义完成。

步骤011已有严格fence/频率/已知负载验证的GPU timestamp，古堡256/512两路径与重建/插桩对照实测通过；这补GPU成本证据，不解除010质量失败，也不等于完整Player帧/显存/半分辨率重建预算。计时桥仅隔离Editor，Release无probe依赖且实跑通过。

步骤012补实际256/512的28项资源清单：自有payload16.588/65.151MiB；AS/pose统计分列。1080p约514MiB仅布局推算，HD首次Unity fence失败后终止，稳定预算不接纳。见[资源证据](../../Tests~/RTGI/Resources/README.md)、F31和[半GI设计](Ho-RTGI-半分辨率设计.md)。下一阶段先验证GI输入/历史/NRD中心映射，半GI尚未实现，010质量失败继续。

步骤013坐标helper已有实际GB/SB GPU对独立CPU几何证据：RG/compat各12case/18190receiver/5961前次depth，补偿后的网格/历史/NRD投影通过，负对照确认会检出漏映射/漏补偿。helper尚未接生产GI或重建，不算半GI交付。两次raster轮廓夹具失败和修正限制保留F32，下一步接GI私有桥和全域输出。

步骤014已实际完成半GI链路/全域重建实验：平面和57.497%payload收益通过，但两路径薄前景能量full约.992、half全0，原5%失败；Runtime/Editor原型已过滤撤回、源码/patch/图归档，F33搁置。当前回到010实际质量调查，不能重复上一段当时计划或以内存收益接纳画质失败。下项先取GI低信号moment/尺度证据，不先认定根因，010/012/正常camera/完整预算缺口继续。

步骤015确认默认64下slow moment精度问题：实际Temporal/Clamp正亮信号m2=0约98%；两个可逆Editor单位对照降至.00315%，物理raw256帧均值完全相同，但RGB仍未全过5%，生产默认/Player仍64。F34排除HistoryFix条件fast alpha，F35停止纯单位调参。下一步全图/16相位与局部阶段误差取证，原失败继续，不挑方便指标。

步骤016 full256²及所有16相位仍失败，原64全图RGB约−7.25/−8.42/−8.78%，否定“只因相位”的解释。DI同box凸裁剪候选恶化RGB/L1，已过滤移出产品并按F36搁置。下一步对高波动热点和安静控制点做同支持域高样本无复用一跳参考/收敛，先确认真值再决定降噪修正；原5%与完整应用验收继续。

步骤017发现并修复大合并蒙皮mesh的AS范围越界：自有pose absolute indices/base0使同场景D3D12错误14→0，顶点字节与三角元组保留；Editor双路径/真实UV命中、两个315请求Release Player过。合法AS重采后15/16选点收敛，暗点8M仍不确定；旧非法AS参考不接纳。全图原5%仍失败，静态非零base尚须安全镜像，正常camera/1080预算继续，见[证据](../../Tests~/RTGI/DenseReference/README.md)。

并行任务共享工作区，提交必须列明路径。避免占用其他任务的验证工程；同一个 Unity 工程一次只能运行一个实例。

步骤018静态owned镜像实际157功能checks完成，但实例3→5增长有scratch debug错误；原Ho及直接Unity API均能复现，初始5干净。多轮无收益方案已撤回、F39搁置，产品保留017实现，static非零base仍未交付。仅接纳最小复现及[失败/恢复证据](../../Tests~/RTGI/StaticMesh/README.md)，不声称具体上游根因。下一项转合法AS古堡独立seed/T/S/none质量对照，所有原质量/应用/预算缺口保留，goal active。

步骤019完成RG256²四模式×8seed矩阵，实际exit0/API0/源漂移0。TS raw相对none全图RGB高25.70/22.21/18.75%，T或S单独仅约2.4～3.0%；所有32段filtered/raw仍原5%失败。当前只有定位收益，Runtime不改。两次byte-gate拒绝、精度合同与最终真实重跑保留F40和[完整证据](../../Tests~/RTGI/Seeds/README.md)。下一项020仅Editor尝试pre-spatial history，同帧T/S保持，再评质量/方差/成本，不关闭复用当完整通过；正常camera/1080/Player等目标仍active。

步骤020 pre-spatial packed history隔离候选有收益：filtered相对finite none从+15.70/+11.58/+8.08%降到+1.22/−1.87/−3.28%，时间方差约降55%；原5%仍全失败、局部仍不足。额外codec假说在此域被反事实控制否定。256 GPU Final约+.018ms，总耗时相近但噪声大。四进程debug0，Runtime尚未改，下一项021直接生产接入及双路径/动态/Player回归。见[F41/证据](../../Tests~/RTGI/HistoryFeedback/README.md)和最新交接，不重跑纯诊断凑样本，目标active。

步骤021已将pre-S packed history接入实际Runtime，仅S开时2→0，S关旧路径保留。双路径真实合同、环境/Cutout/蒙皮回归及两个315请求Release Player过；新增23状态history0=2和反事实见证。生产long filtered相对finite none约+1.77/−1.37/−2.84%，原5%/16phase仍失败。核心功能通过但AS scratch两错误在冻结legacy同样复现，完整debug回归仍未过；5条历史build warning精确审查、不改其它模块。见[真实证据](../../Tests~/RTGI/ProductionHistory/README.md)、F42和最新交接。下一步处理实际NRD余损/精度，所有缺口和goal active保留。

步骤022 GI私有NRD池32F保持scale64，slow moment下溢约99%→.026%、filtered方差约−56%。原stride0过，但fullgreen−5.2075%、8/16phase仍失败；生产格式未改。+2MiB@256，1080+63.28125MiB仅布局投影。F43保存未Prepare/无UAV捕获两类失败和正式重跑，见[精度证据](../../Tests~/RTGI/PoolPrecision/README.md)。下一步023补HDR/生命周期/成本/真实Player再决定默认策略，全部旧缺口和goal active继续。

步骤023已生产GI-only四color池及NRD output32F，DI默认16F不变、单位64。pool-only低信号失败与legacy对照保留；最终1e−4/1/20000双路径原5%通过，HDR m2>65504仍有限，两315-request Player验证最终格式/生命周期/动态。实际fullgreen仍−5.05869%、7phase失败；+2.5MiB@256/79.10MiB@1080仅布局，成本与目标预算不混同。见[完整资格](../../Tests~/RTGI/PrecisionQualification/README.md)、F44和最新交接。下一步024优先真正enabled-camera呈现证据，所有未完成项与goal active继续。

步骤024补齐真实enabled-camera自动调度：用户授权短暂可见窗口，同一RG构建从Hidden改Normal后240连续自动回调/合成/Volume/移动与发光reset通过，compat亦240通过；两hidden失败完整保留，Runtime不改。[自动相机证据](../../Tests~/RTGI/AutomaticCamera/README.md)。相机仍输出64²targetRT，最终backbuffer呈现与视觉QA待025；原质量/预算/F39全部继续，目标active。

步骤025完成默认屏幕目标：RG/compat各240真实自动渲染，serialized-enabled+target=null、512²屏幕/GI、帧末截图与前帧publication对齐，固定sRGB解码后原合成/Volume/移动/发光响应通过。[屏幕证据及10张图](../../Tests~/RTGI/Backbuffer/README.md)。首次缺模块构建失败保留，Runtime无改；这是小场景功能呈现，026回当前生产独立seed全域/局部质量，原5%/7phase/F39/1080完整预算继续，目标active。

步骤026完成当前生产TS默认的8seed全域/局部证据：full误差−4.716/−5.158/−4.823%，3/8 full通过但合并full/9phase/32固定块仍失败；first4探索/last4验证表明单窗极值会缩小，6个x86局部差仍保留。[完整seed/holdout证据](../../Tests~/RTGI/ProductionSeeds/README.md)。无生产改动或画质修复；027先这些新坐标及相邻控制的同域无复用高样本参考+guide，不把raw当真值或盲调NRD，原质量/F39/HD完整预算继续，目标active。

步骤027对6个中心+12相邻新坐标完成每点1048576无复用一跳样本，18/18原收敛标准过；与026 map相比全部18点raw/ref L1 .057、filtered .458，确认局部欠亮。主NRD normal/depth/signal输入匹配，[新参考及guide证据](../../Tests~/RTGI/LocalReference/README.md)完整；尚未定位具体filter阶段，未修复生产画质。028先同坐标4seed局部stage/新参考L1，不补gain/宽sigma/单位格式扫描，全部原全域/F39/HD完整预算继续，目标active。

步骤028实际18点/4seed阶段归因：reference L1首Atrous .131→末.462，主要差在后四轮Atrous；原raw/pub全图帧均值与18点均值完全匹配026，observer未改这些结果。[阶段流与绑定证据](../../Tests~/RTGI/LocalStages/README.md)保留原full绿色−5.107%/8phase失败，无生产改动。029只Editor atrous5→3单候选并核全部点/暗邻/full/phase/噪声，无收益搁置，不扫迭代数；全部原F39/HD完整预算继续active。

步骤029固定Editor GI atrous5→3候选有收益：18点reference L1−33.6%，4seed pooled full过原门槛、phase8→6失败，暗邻未触发预设硬退化；方差+38.6%且仍明显欠亮。[候选原流与作用域证明](../../Tests~/RTGI/AtrousCandidate/README.md)完整，Runtime仍5。030固定3的8seed/旧参考/暗控制/全图/相位/噪声，再动态/HDR/Player/GPU后决定生产；全部原缺口和目标active继续。

步骤030完整8seed/旧参考发现反例：全局atrous3虽改善新18点和pooled full，旧(141,157)G/B超过预声明硬退化界，候选已搁置。Editor入口/override移除并恢复默认5，过滤后实际98请求资格通过；Runtime一直未改。[完整资格/拒绝与原数据](../../Tests~/RTGI/AtrousQualification/README.md)。031保持5，对亮边和反例作逐轮/邻域传播取证后再开局部机制，不扫描2/4，全部原缺口继续active。

步骤031用stock真实逐轮输出说明亮边连续损失、旧反例后轮部分恢复；独立debug权重可重建自身结果但不bit-exact，未替代质量门槛。[原版/权重边界与证据](../../Tests~/RTGI/AtrousPropagation/README.md)。后期luma分母floor在scale64下的激活差异支持032仅GI私有最小阈值单位对齐单候选，默认5及其它设置保持，全部原质量/预算缺口继续active。

步骤032单位域floor候选失败：新18 L1仅−3.9%，原full2→0/4且11个phase pass→fail，已保存失败并移除候选入口/类，stock5过滤后实跑通过。[原失败与撤回证据](../../Tests~/RTGI/LumaFloor/README.md)。033复用现有中心/邻居方差和权重，先离线核对双向/归一化机制，不继续常量/单位/迭代扫描；Runtime未改，全部原缺口active继续。

步骤033复用权重离线重建forward/geometry（31,104 tap0不符），简单luma对称化后期预测太小，不开GPU；既有factor2双向支持只在大步长能区分采样两类位置。[冻结重放与限制](../../Tests~/RTGI/PairwiseWeights/README.md)。034仅step>4局部候选，未修复生产、不能累加静态收益，全部原门槛/预算继续active。

步骤034大步长factor2 guard撤回：名义L1−8.84%不足筛选，且后两seed发现raw配对不满足，不能当因果收益；默认5过滤实跑过。[原数据与配对失败](../../Tests~/RTGI/ContrastSupport/README.md)。035先stock重复/no-op clone和输入状态重现性，严格pair恢复前暂停新算法，所有原缺口active继续。

步骤035当前stockA/B/no-op四seed全raw/pub均值图与端点数值状态逐bit重现，记录序列化与失败停止已修复；历史漏项/device crash/误排/partial全部保留。[完整重现性证据](../../Tests~/RTGI/Reproducibility/README.md)。历史034仍未资格，036同源GI双NRD基线先资格，不重开弱候选，全部原缺口active继续。

步骤036同一实际GI输入双独立NRD基线/no-op通过：1024观察帧完整RGBA逐bit同，baseline整图均值对旧基线不变，单支/统一reset证明历史隔离。[共享输入资格](../../Tests~/RTGI/SharedInput/README.md)。仅Editor hook/诊断，未修画质；037评估DI成熟diffuse策略单分支，不直接认定GI最佳，全部原缺口active。


步骤037：同源GI实测DI diffuse bundle，新18/旧15总体reference L1改善42%/31%、full2→4/4，仍旧196141 B与141157 G/B硬退化，已拒绝生产接纳并撤回Editor覆盖；原stock5过滤复核过。1024帧物理GPU resolve对生产发布bitexact、全部数据/原源/取舍见[DiffusePolicy](../../Tests~/RTGI/DiffusePolicy/README.md)。038先双分支逐轮观察定位旧退化，不能放宽原门槛/直接移植；全部原质量/范围/预算和目标active。


步骤038：同源stock/固定037策略28点×9stage实际RGBA观察合格（1024帧输出对037逐bit不变、112分量F32读取自检、全部stage对原整图copy匹配）。旧反例首轮颜色近似同，后4轮有益恢复减弱；196141后期variance更大仍恢复弱，不能只泛调variance，F58继续拒绝。[阶段证据与图](../../Tests~/RTGI/PairedStages/README.md)。039只一个后轮交换算子消融、其它保持，先短资格再原全部控制/门槛；原质量/F39/动态/完整预算与目标active。


步骤039：同源单归一化算子消融，整图首轮RGBA1024帧逐bit同，收回旧点89～98%退化却丢掉大部分总体收益（新18只改善3.26%<20%），已拒绝/撤回，038观察恢复实跑过。[算子取舍与原证据](../../Tests~/RTGI/ExchangeOperator/README.md)。按用户规则暂搁置局部公式试验，040先核F39原生scratch新机制，不重复018已做的mirror/flags等；无新证据则推进独立复杂动态Player/成本资格。全部原质量/范围/预算和目标active。


步骤040：两Release自动屏幕Player通过受控多接收面/bone+blendshape/移动遮挡/RGB/Cutout/Volume检查，各240camera帧/224GI更新、8固定instance，18实际PNG。发现运动48/48全局NRD reset，当前功能响应不等于动态收敛；已核DI31/32同采样confidence与直接保留历史的旧失败，041先审GI等价信号与oracle，禁止盲换StructureRevision。[实测与审查](../../Tests~/RTGI/ComplexMotion/README.md)。F39同host/有限官方核查无新行动依据未重跑，全部原质量/范围/预算与目标active。


步骤041：原SDK小GPU合约/独立oracle验证GI32无法唯一恢复旧次级DI选择、默认misc/unused丢失；保留同选择比新采样必要。实际GPU同时修正两CPU诊断字段标签，旧全包比较/texture质量不变，历史分项计数须旧schema解码。[采样契约与证据](../../Tests~/RTGI/ReplayContract/README.md)。042做真实T/S/Final来源继承观察后才动态confidence；未改生产reset，190.55MiB/1080候选开销待证，全部原缺口与目标active。


步骤042：真实古堡64²原版/lineage双GI两后端4T/S观察过，各128帧，1,572,864原包及raw/NRD输出逐bit同、2,097,152不可变来源检查，pre-S历史与实际Spatial1 Shade对应成立。capture保留字/UAV两失败完整，常规pass不覆盖age/Jacobian/视野/kind0/3或confidence。[来源证据与界限](../../Tests~/RTGI/SelectionLineage/README.md)。043补分支/生命周期，再当前TLAS同选择重求/oracle；生产reset未动，全部原缺口/预算与目标active。


步骤043：五固定fixture两后端各80帧边界来源观察通过，983040原包和raw/NRD逐位同，age/Jacobian/material/clip/noMatch/kind0恢复/kind3/服务重建有事件与独立谓词核对；disabled可选路径及未执行防御分支另作source可达性说明。[边界证据](../../Tests~/RTGI/LineageBranches/README.md)。044推进真实currentTLAS同选择secondary静态重求/oracle，未改生产历史；全部原质量/范围/预算和目标active。
