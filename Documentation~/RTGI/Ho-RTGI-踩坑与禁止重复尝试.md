# Ho-RTGI 踩坑与禁止重复尝试

日期：2026-10-19。本文记录已知陷阱、被拒绝路线和重开条件。DI 历史经验与 GI 本轮实测分开标注；禁止重复的是具体已否定条件，不将某个算法永久封禁。

| ID / 来源 | 陷阱或禁止重复的做法 | 允许重开所需证据 |
| --- | --- | --- |
| F01 / DI 第108步 | 参考 UI 打开 NRD，不检查 WITH_NRD 与实际 filtered 消费就声称启用降噪；修改系统 PATH 或重新修 Unity/系统 D3D12 来解决已有隔离补丁能处理的问题 | 实际 shader 宏/编译依赖、写入与合成纹理证明路径变化；先复用已有进程级依赖与 NRI 补丁 |
| F02 / DI 参考对照 | 直接用参考 DI+GI+RR 最终画面比较 Ho diffuse-only raw，并将稳定性差距归因于 GI 重采样 | 共同支持域、同场景/曝光/预算以及 raw/filtered/合成分量已分别捕获 |
| F03 / DI 距离审计 | 把参考 prior DI 的 hit distance 或合成 helper 的 alpha 无条件作为独立 GI 几何命中距离；用灯距简单平均/设零替代协议核对 | 对应 lobe、GI 初始/重用样本与实际 NRD alpha 的独立验证，并有单变量收益/代价证据 |
| F04 / DI motion 审计 | 把同应用帧多次 RenderRequest 当多个运动历史帧，或把 URP 2D MV 未转换接给 NRD | 真实自动帧、current/previous 对应、符号/深度/表面域的有效捕获与断言 |
| F05 / DI 多个拒绝实验 | 在没有新阶段证据时重复调固定空间交换、RGB guide、VNDF 概率或延长所有历史来压静态门槛；通过放宽阈值接纳退化 | 新输入/定位结果能解释旧失败，保留同一静态与动态对照，并明确预算 |
| F06 / 参考协议 | DI 第108步对照明确关闭 GI，将该历史通过记录引用为参考 GI 已跑通 | GI 显式开启的实际进程、设置、写入纹理、报告与退出码 |
| F07 / Ho 原型 | 删除 miss/unsupported/零贡献样本重新归一化，或与 DI 重复叠加主表面天空 | 路径归属、支持域/PDF 与黑样本计数的能量验证 |
| F08 / GI步骤001实测 | 从 Native 可能被 NRD 重打包/处理的 DiffuseLighting 跨帧相减获得 GI；首次8帧稳定性断言失败。禁止通过放宽断言接纳 | 使用已存在的独立 GI debug 输出并确认实际写入，或提供同帧阶段捕获；失败报告保留于 Tests~/RTGI/Reference/results |
| F09 / GI步骤001实测 | 以 `_IOR=1` 假定参考二次表面 F0=0，或把参考合并的 diffuse+specular 当作 Ho diffuse-only 能量参考 | 读取实际 secondary packet、核对 BRDF 转换，并统一支持域/独立 diffuse 输出后再做每通道能量结论 |
| F10 / GI步骤001复审 | 因两个 secondary struct 都是48 B而复用字段顺序；按标准 GPU float 格式解码本地名为 UFLOAT 的自定义归一化打包 | 逐字段对照本地 HLSL/CS布局与实际状态位；按对应版本真实 codec 解码，不凭名字推断 |
| F11 / GI步骤002实测 | 向Gamma-tagged Reflectance直接写.04便宣称线性F0=.04；实际GPU只得到约.0039。禁止以GUI数值相同替代物理输入相同 | 进行正确的颜色空间转换，并读取/断言实际secondary打包；本组正确值为Ho`FF0A0A0A`与参考约.03995 |
| F12 / GI步骤002暂缓 | 为贴齐参考的偏暗结果调整Ho的增益/权重或放宽5%门槛；在无新证据时反复追参考唯一根因 | 先有独立积分或高精度共同域基准。当前Ho两种复用均过5%，参考仍失败；重开参考差异调查须有新的阶段/PDF/输入定位证据和明确用户可见收益 |
| F13 / GI步骤003验证 | 切换工程色彩空间后假设首个editor请求已有有效GB/SB域；也不能在主测量中忽略持续无效receiver来“通过” | 遵循已验证的三次editor-update有界warmup，随后逐次严格验证。首次Gamma失败已保留；普通应用帧/Player冷启动仍须单独验证 |
| F14 / GI步骤003修复 | 将有限GI反弹追踪范围miss当作无限远天空可见，导致范围外挡板漏光 | 直接天空模式单独进行远处可见性查询；当前1500单位挡板/1000反弹范围通过。不得为了减少射线又合并两种范围语义 |
| F15 / GI步骤004夹具 | 联合DI测试假定仅SetActive(true)即可生产，忽略并行DI新增的Volume显式启用契约，导致空context；禁止以空/黑输出充当联合验证 | 为夹具明确启用DI Volume并验证实际context/输出；原失败保留，不为配合旧测试回退DI策略 |
| F16 / GI步骤005资产导入 | 手写33位GUID使Unity跳过新增NRD context，表现为namespace/type缺失；禁止只看文件存在便跳过导入检查 | 所有新增meta的GUID为32位且唯一；实际Unity编译/dispatch通过。首轮退出1日志摘要保留 |
| F17 / GI步骤005隔离快照 | 并行VolumeLight未完成编译时改动其源码来让GI测试通过，或不记录快照来源；git archive的CRLF又触发旧ShadowCast宏解析错误 | 只使用HEAD加明确GI/API覆盖的隔离包，记录base/source SHA及唯一LF传输归一化；不修改生产ShadowCast/VolumeLight，保留失败 |
| F18 / GI步骤005 runner | 给RunConvergence错误配置通用PASS marker；进程实际exit0且CAPTURE_COMPLETE，但runner拒绝。禁止为跑绿随意降低检测要求 | 核对真实入口及其完成合同；保留拒绝run，改为已有RunDenoise能量/噪声/生命周期实际门槛，再验证exit/marker/diagnostics |
| F19 / GI步骤006 Player序列化 | 隔离快照中的CLR类型能CreateInstance，便假定MonoScript也已正确导入；首个Player中GB/SB/GI/Volume m_Script为空、布局不符，实际Feature引用null | 构建前MonoScript.FromScriptableObject/GetClass必须匹配；原快照一次强制脚本重导入仍为空，禁止原样再build。当前工作树映射恢复，先用其继续，暂缓追快照导入唯一根因 |
| F20 / GI步骤006探针退出 | 主失败为Feature引用null，清理再次无条件feature.Dispose导致退出前二次异常、进程挂住 | 清理可处理缺失序列化依赖，报告保留主错误；只在核对PID与绝对EXE路径后结束本任务失败进程，记录人为终止exit=-1，不冒称正常exit1 |
| F21 / GI步骤006夹具假设 | 将白色unlit材质的实际camera color假定为1，或对disabled-camera请求路径省略DI helper已有的显式Volume更新，导致误报合成/Volume策略失败 | 用此前实际raster基准加同帧GI；请求前更新Volume stack。两份拒绝报告保留；不能为夹具改生产增益或Volume策略 |
| F22 / GI步骤007代码复核 | 仅把DI SupportsCutout predicate接给GI registry就声称Cutout完整可用；共享inline显式CULL_NON_OPAQUE，而GI的次级命中/次级灯/最终重连还无一致alpha路径 | 先补完整native命中/可见性合同，再用实际孔洞、遮挡、重连和secondary-light验证；本条是代码合同风险，不伪称已经做过Cutout实验 |
| F23 / GI步骤007夹具 | 用开启空间重采样后的单像素MC值断言统一发光面的解析常量；首轮.928失败 | 过滤夹具隔离空间复用并保留temporal/NRD reset，或使用有精度依据的空间/时间均值。门槛不放宽，生产算法/增益不为该误用调整 |
| F24 / GI步骤008夹具 | 黑albedo且F0=0便要求受天空照亮的rough-GGX次级面严格零；实测约.0013，真实包却flags1/z=-.5、diffuse/spec RGB0、emission0 | 阅读当前BRDF：Schlick仍有(1-V.H)^5。初始命中用真实包/位置验证，shadow能量另验；不改生产gain/BRDF消掉合法擦边反射 |
| F25 / GI步骤008原型 | LIL_RENDER=0直接使用未声明的AlphaMask数值字段；由opaque换Shader却保留Opaque RenderType；或把并行C#失败后旧report当fresh结果 | 私有适配显式声明AlphaMask字段，夹具使用真实TransparentCutout域，退出/时间戳/源码绑定一起核对。正式隔离与Player实际源分开记录，不能以旧文件存在当通过 |
| F26 / GI步骤009夹具复核 | 两个负缩放轴行列式仍正，却把该case命名为镜像并声称验证winding反转 | 检查真实变换行列式；本步在正式运行前改用单负轴并通过GI属性/normal-map gate。此为夹具覆盖复核，不伪称发现生产变换缺陷 |
| F27 / GI步骤010实际场景 | 古堡“能渲染/变化像素>0”便声称filtered质量通过；或对约9%的RGB均值下降继续加gain、放宽5%或无界增加history/fastSigma | 64窗失败，256长窗仍约-6～-7%；stage显示输入准确，主要时间/色盒链下降。sigma4/6两轮仍失败且边际小，搁置更宽盒；重开需新阶段/局部误差及动态/细节证据。Runtime原版不改，qualityQualified=false保留 |
| F28 / GI步骤010统计 | 用max(R,G,B)的单帧平均，直接证明RGB能量损失或改善；把独立GI曝光×100预览与正常camera按同曝光比较 | 逐通道时间均值与方差分开记录；显示曝光只影响标明倍率的PNG，EXR/生产增益保持，不能以预览亮度替数据 |
| F29 / GI步骤011计时 | 把合法空GPU区间tick相等当无数据，或反过来把未完成/零起点/非空零时长当有效GPU成本 | 非零频率/起点、唯一slot、已完成目标fence与tick顺序同时验证；只允许空区间量化零，已知非空负载必须正并按负载扩展。原strict失败保留 |
| F30 / GI步骤011隔离 | query resolve直接放native raster render pass，或仅关闭AnyPlatform/Win64就声称测试DLL Editor-only（Linux/OSX仍默认enabled） | EndQuery标记位置不变，Resolve延后camera外；明确禁所有Standalone，实际Release payload无probe DLL且功能实跑。不能给产品加计时native依赖 |
| F31 / GI步骤012资源及HD | 把descriptor payload/Unity Mesh运行时统计称为物理VRAM；整卡高占用且HD连续D3D12Fence::Wait后原样跑下一路径或继续凑样本；用HD推算冒充成功 | 256/512实际清单接纳，AS API/pose统计单列。HD PID44476真实exit−1无报告，compat未执行；整卡7.3～7.6/8GiB仅相关观测，不证明根因。禁止原条件重试/改TDR/终止其它进程；重开须具体新host条件或有界低驻留/同步夹具证据，先恢复非空已知负载/fence资格再做HD。runner仅停止自身进程、15分钟上限；1080约514MiB只作布局投影，stable budget/actual1080均false |
| F32 / GI步骤013独立几何夹具 | 把无限精度CPU轮廓中心覆盖当作raster唯一允许表面；发现孤立depth/normal误差就放宽world门槛或改生产投影 | 两次真实exit1定位唯一像素CPU foreground local x−.5000122（距边缘.000192px）、GPU覆盖foreground。最终只在投影轮廓1/256px带允许两个独立ray/plane候选，全部位置/法线/深度仍原门槛，不跳过点；每条2点在带，18188点唯一候选。保留失败/源/诊断，不能扩大band遮盖错映射，也不能据此声称薄边GI重建通过 |
| F33 / GI步骤014半GI重建 | 固定2×2代表点没采到薄前景，仍用四邻居/更宽邻域/背景填充或gain声称恢复；只凭57.5%payload下降接纳half | 实际RG/compat各169请求exit1：平面/输出/历史通过，但x8白色薄前景30点full约.992、half全0，原5%失败。Runtime/Editor实验已过滤撤回、精确源/patch/图归档，产品full。禁止原样四邻居或扩大域重试；重开需要真实full receiver缺样补偿及动态/噪声/资源与GPU成本证据。当前搁置，继续原实际场景质量；首轮F23单帧误判也保留，不能作half正式失败根因 |
| F34 / GI步骤015 moment口径 | 仅见HistoryFix正文float4，便把其gOut_Diff alpha一律当slow moment；或把PrePass距离/A-trous variance混进m2 | 实际DLL证明Temporal slow transient0/fast1，HistoryFix条件写fast1且稳定早退。只统计4/6无条件slow；baseline5错误数据/标签原样保留但排除。必须看实际descriptor alias与shader早退，不靠函数名/固定pool猜；单位1 runner marker漏匹配同样保留false，用fresh报告/实际异常独立核对，不改历史绿色 |
| F35 / GI步骤015单位敏感性 | moment精度恢复就声称古堡RGB修复，或继续减scale/按通道调单位/盲改默认64 | 单位64→1→.125，slow m2=0从98%降至.00315%，raw256帧均值逐条相同；RGB仍原5%失败，最后约(-6.17,-5.25,-4.60)%。两轮后停止纯单位调参，生产仍64。新策略需HDR/动态/历史与完整质量证据，不能gain/改sigma/挑通道替代；下一步全图/相位/局部误差取证也必须保留原stride0失败 |
| F36 / GI步骤016完整域/DI候选 | 把旧stride4失败归咎相位便换指标；或见DI保色成功/负分量减少便直接移植并称GI能量通过 | 实际full64/归一化.125与16相位全失败，fullRGB约−7～−9%。同box凸裁剪只改一块、负分量352→4，却使RGB/L1更差，候选scope恢复并移出产品、精确归档。拒绝其作为本场景能量修复，不否认DI保色适用域；无新的GI颜色平面/独立真值与质量收益证据不原样重试。热点raw方差很大，须先高样本无复用参考/收敛匹配，不把有限窗raw或子集当完整真值 |
| F37 / GI步骤017采样与调度 | 同帧长循环发生device removal后原样重启；把非终态checkpoint或大量但不收敛样本当真值；锁文件存在就删/持锁仍开工程 | 两次−1/无报告保留；只在独占打开成功确认无人持有后清本任务空stale lock。小负载资格不是大场景保证。改实际Editor帧小批并记录ray/compute输入一致、场景域稳定，合法AS重采后15/16收敛；暗点8M green-SE2.316%仍不确定，禁止放宽2%或无限加样本。下一轮改方差/独立seed设计，不以子集替全图 |
| F38 / GI步骤017 GPU AS范围 | 仅凭退出0/finite/映射匹配便接纳GPU结果，或把d3d12 validation error漏计成0；无证据归因VRAM | 首debug exit0但14条AS顶点范围越界，旧regex漏validation。实际251321顶点stride40、14个非零base偏移导致全mesh声明跨度超VB；自有skin快照absolute index/base0令14→0且顶点字节/三角元组不变。旧真值撤销资格，必须重采；not out of Local memory原日志保留。静态base仍需处理，不能泛化修复或称HUNG唯一根因已证实 |

长期无收益路线的记录模板：问题与基线 → 假说/参考 → 尝试和真实成本 → 哪些指标改善/退化 → 拒绝或搁置原因 → 禁止原样再试的条件 → 重开所需新证据。新记录必须关联过程步骤与报告，不只写“试过，不行”。

F01～F06主要来自DI/参考历史，不伪装为本轮GI实测失败。F08～F10为步骤001的协议/诊断陷阱，修正后重新执行；步骤005独立降噪有均值/时间方差实测收益，但不能扩称为动态或完整目标场景质量结论。

## F39 / 步骤018：静态镜像与原生AS增长

静态mirror157功能checks完成仍出现scratch范围错误，不能把exit0或report.pass当debug资格。原Ho共享/独立源3→5也失败，初始5干净；分tick、Static、重建AS、FastBuild/MinimizeMemory、跳过新增geometry update均无收益，全部撤回。直接Unity API最小入口也复现：初始5 API0，3→5 API1，2560 B scratch范围；FastBuild对照2432 B。尚未证明Unity内部或驱动唯一根因，不应泛化成已知上游bug。

**禁止原样重试：** 继续盲调上述flags/容量/延时；关闭debug、只测初始实例、跳过问题几何便声称static支持；把早期移除debug guard的脚本成功当通过；修改017已通过的skin修复迎合此问题。

**允许重开：** 新的原生scratch分配诊断、明确引擎/host修复，或有可解释机制的新方案，必须同初始/增长对照API0，再补原计划UV/normal/Cutout、只读Player/生命周期/成本。候选完整源与失败见Tests~/RTGI/StaticMesh，生产已恢复。先继续合法AS古堡的独立seed/复用质量对照，static非零base缺口保留，不能被其它局部成功解除。

## F40 / 步骤019：配对字节一致性与有限窗raw口径

不要把浮点secondary结构byte hash不相同直接解释为路径/随机种子改变：原32段exit1逐字段只见hit位置≤1.22177e−5、远处合成miss≤.008789及normal一个oct16量化步，flags/材质/emission/PDF/geometric/RIS都相同。后置精度合同使用原world .0005界限、miss单位方向及FP32半径、normal一个量化步；保留实际失败/精确源，不能改历史exit/marker，不能放宽primary或5%。首个无字段资格失败的具体差异仍未知，不用后续记录补写原因。最终重跑端点bitwise全同，也不声称比较器修正让GPU数据变同。

8seed实测TS raw相对none fullRGB高25.70/22.21/18.75%，T或S单独约2.4～3.0%；TS filtered又比none raw高15.70/11.58/8.08%，原filtered/raw5%仍全失败。禁止按某固定256窗raw补gain/挑点/关闭复用便称默认通过；参考15/16且有事后选点偏差，不能升级为独立全图真值。SE按seed均值，不把相关帧/pixels当iid、不把±SE说成严格置信。

下一项可做有机制的pre-spatial history候选，同帧T/S保留、history0只存Temporal结果；原参考也反馈Spatial，不能先认定索引bug。需要同输入/同seed的真实质量/方差/成本与回归收益才接纳；无收益就撤回，不继续堆任意混合权重或盲调Jacobian/NRD。证据Tests~/RTGI/Seeds。

## F41 / 步骤020：打包编码归因、pre-S收益与成本边界

不要只见Ho额外decode/encode就认定codec导致TS偏亮。三项实跑中legacy与packed Spatial的逐帧publication完全相同；legacy32个端点共2097152个pixel packet与源Spatial全bitwise相同。此域额外编码无误差，packed Spatial只增加Final成本，没有画质收益；无新HDR/gamut非幂等证据不沿此假说反复改codec/SDK。

pre-S有收益：filtered相对finite none均值从+15.70/+11.58/+8.08%到+1.22/−1.87/−3.28%，filtered方差约降55%，但raw仍高6～8%、15选点filtered相对L1仍.4645、原5%全部失败。禁止子集/选择偏差当全图真值、删掉旧门槛、只测S关便声称新默认回归。下一步生产S开路径/动态/Player必须实际验证；S关保留原store以限定未测HDR行为。

有效GPU Final约增加.018432ms，Total中位相近但顺序固定/p95波动大，不称总性能提升，不用256外推1080或将逻辑32B读取当DRAM/VRAM。证据Tests~/RTGI/HistoryFeedback，当前Runtime未接入。020 Generate以旧legacy store生成候选，生产改后不可原样调用并错标baseline，必须历史源码或显式适配。保留全部原失败与目标缺口，不能complete。

## F42 / 步骤021：观察正确阶段、已知警告与核心debug资格

生产history0改pre-S后，不能继续拿它判断Spatial是否合并。只将夹具读点改为actual Spatial1，M>1阈值不变；原两actual1保留。修正core actual0仍两条2304 B scratch错误，冻结legacy实际绑定也同样两条；不能因为功能/exit0放行debug，更不能把它无证归因新Shader或泛化为已确认Unity bug。F39仍开，不重试旧参数。

成功build0/errors0也可能被过严包装器因旧warning拒绝。只能在核对017原log/SHA后允许逐字相同五行；新/GI warning与任何error继续拒绝。保留原metadata诊断5，记录复用的真实build出处和无源变更；不改SolarHD/lilToon其它任务代码凑绿。Player图通用文件名导致compat覆盖RG，不能冒称有两份截图。

021生产long全域相对finite none改善至约+1.77/−1.37/−2.84%，但原filtered/raw5%及16phase仍失败、正常camera/HDR/局部/1080预算未完。两315-request Player和S开packet合同是实际新路径证据，不能扩大为普通enabled-camera或完整GPU预算。当前策略可保留为局部改善，目标不complete；020工具依赖旧store，使用历史版本而不是回滚新Runtime满足旧SHA。

## F43 / 步骤022：格式生效、typed copy与矩精度口径

设置selector不等于实际pool已换：首资格未执行Prepare即Snapshot实际1，保留并补真实render。float32 copy self-test通过也不能证明所有捕获目标有UAV flag：首两长run的阶段目标无UAV，Unity Property绑定错误被旧regex漏计，RGB/m2全0。禁止拿finite zero当有效或把旧counter0改绿。捕获目标需正确格式+UAV，Copy前显式guard，runner匹配Compute shader Property/UAV类错误；源/报告/失败原样保留。

正式四池RGBA32F保持scale64使slow m2=0从约99%降至.026%、方差约−56%，但fullgreen仍−5.2075%、8/16phase失败。禁止stride0过就宣布整体通过、继续挑phase或把旧half严重误差容差当32F精确界。无新纯单位扫描，F35继续。+2MiB@256、+63.28125MiB@1080只是布局payload；没HDR/动态/Player/GPU成本证据前，不盲改所有NRD或按投影重试HD。当前只GI Editor实例；下一步资格要保持DI默认、格式生命周期和历史重置。

## F44 / 步骤023：HDR输入alpha、external output精度与范围口径

发光RGB缩放不能用Color.white×amp同时缩alpha；lilToon混合再次乘alpha，初两actual1保留。改RGBA(...,1)后低信号仍失败，不能继续甩给输入；pool-only16output与legacy16F对照都实际失败。只提升GI external output（PrePass/最终共享）32F后，1e−4/1/20000原5%两路径通过，原失败/低信号对照保留。不得用早期pool-only Player来证明最终output32；最终独立新build/Player/payload/格式断言俱全。

HDR slow m2约97653有限不等于任意HDR安全。最终fullgreen仍5.05869%/7相位失败，禁止放宽/挑phase或继续无依据扩格式。extra79.10MiB@1080是payload投影；pool-only高负载GPU计时有数十ms尖峰，事后100%util非因果，最终40区间也不等稳定目标预算。禁止跨负载称加速/按投影重试HD或停其它进程。normal enabled-camera仍缺，315 SubmitRequest不得换名验收。

## F45 / 步骤024：隐藏窗口无自动回调与呈现边界

DI已试hidden普通启动，不能只去batch重复。GI新serialized-enabled/target试验中，target未created和显式Create后都180Update/0事件；Create仅分配资源，不能算渲染。用户明确授权后同一RG构建只改Normal窗口，240真实连续回调通过；compat亦240通过。保留两hidden失败，不继续同条件反复启动、不将具体引擎/驱动根因写成定论，不以手动Render/SubmitRequest替代。可见Player每次限15秒、自行约8秒退出，授权仅短暂测试。

本步相机仍输出64²targetRT，Unity窗口512²尺寸不代表512²GI，更不证明backbuffer最终屏幕或画质视觉QA。下一步target=null必须显式验证捕获时序/色域/坐标，不放宽加法阈值迁就sRGB读数；截图读取不得变成手动渲染。全局fullgreen5.05869%/7phase、F39和1080完整预算不由240事件解除。原失败binary后来被重建覆盖，仅manifest及源/log存在，不冒称全部旧payload仍可现场校验。

## F46 / 步骤025：截图模块与屏幕颜色空间

本机API DLL存在不等于隔离项目已引用模块；首次build39064 CS0103实际失败，须在隔离manifest启用com.unity.modules.screencapture，不能改产品依赖/反射绕过或把未运行当Player失败。ScreenCapture只读取帧结束屏幕，必须对应真实enabled-camera callback；禁止把截图调用伪装成渲染请求。固定逐像素sRGB→linear并以独立raster线性基线资格，不按合成结果挑映射/放宽阈值。

两个最终512²默认screen Player各240自动帧过，截图误差最高.006432<原.015；不能拿屏幕量化结果替代HDR/原5%质量。10张真实屏幕图是纯接收面，不等于古堡复杂视觉QA。两个既有未用GB fallback/MSAA shader缺省提示完整保留，不能称所有日志无warning或支持那些路径。fullgreen/7phase/F39/1080预算继续，不以默认screen呈现完成全目标。

## F47 / 步骤026：源码编码、seed边界与局部极值的选择偏差

Windows Python默认Path.write_text可能把原UTF8中文C#写成GBK，Unity本机仍可能正确编译/加载场景，成功日志不能替代源字节审查。026保留原GBK run，显式UTF8修正且Unicode文本严格相同，重新资格实际0、两map逐字节同；禁止删除原SHA/伪称原run用新UTF8字节。以后显式encoding。

当前8seed TS pooled绿色−5.15807%，配对差经验SE/raw .11936%，3/8 full过；5%边界不能按单窗5.05869%定得过准，也不能因波动放宽/挑seed。pooled仍full不通过、9/16phase、32/64固定块失败，原5%保留。raw/filtered差不是独立物理真值误差，report.pass/functional仅测量功能，不接纳质量。

探索首位(221,184)差.01454在holdout只.000127±.000172 SE，禁止围绕这样的事后极值盲调。6个x86跨seed保留较大差，只支持调查结构；旧017高样本参考不覆盖这些坐标，不能把raw视真值/直接修改NRD。重开算法候选前先新坐标及相邻控制的同支持域无复用高样本/primary/guide；旧参考15/16/选择偏差、F35/F36/F39/F31不解除。

## F48 / 步骤027：新选点参考、guide与统计精度

026 6个跨seed位置不能拿旧017参考点代替，也不能直接把raw当真值。027固定18点新参考每点1M，全部原收敛过，证实中心filtered相对同模型参考约剩.28～.65；仍有选择偏差/共用材质light，不推断全图。mainFrames0的旧mainRaw/mainFiltered不是测量均值，必须显式引用026实际map。原full绿色−5.158%/9phase等失败继续，不以局部真值接纳整体。

Guide原pack/decode和depth、输入remod均过，不能继续无依据改normal编码/单位/格式来碰质量。相邻平面/材质边界是相关证据，未定位具体NRD阶段/根因；先实际stage+新参考L1，再选机制，禁止直接补gain、宽sigma、复活F36线段box。新增finite guard防NaN绕过比较，正式run源在guard前、最终短资格在后，必须保留唯一源diff及原数据finite证据，不改历史SHA。

JSON float批均值要恢复IEEE float32；原C# LINQ Average(float selector)返回float后才赋double。若以decimal64直接算很小的split，会误报不一致；按既有精度重建，不放宽原2%/5%门槛或修改数据来凑绿。verifier既验证字节也从批次独立重算，功能exit0/guide正确不等于画质修复或GPU预算。

## F49 / 步骤028：Atrous阶段定位与候选边界

同18点新参考+4个预声明seed实际stage表明L1首Atrous .131→末.462，input/Pre/Temporal/Clamp较小；不再无依据优先改normal编码/单位/宽sigma。5是transient1条件HistoryFix/fast，10会写4次，末copy仅代表最后一次，不能把pipeline序号当单次dispatch或把5当slow m2。必须逐帧写计数、真实资源binding、32F typed copy/UAV与物理albedo×64。

原raw/filtered全图逐帧均值与18点均值都和026同seed完全匹配，但这不是全纹理每帧逐byte证明。4seed子集L1不能与旧8seed当同预算回归。pooled full仍绿−5.107%、8phase失败，不以局部定位解除。copy/readback非产品成本。029只一个有依据的atrous5→3 Editor候选，噪声与暗邻点要一起评估；无收益或硬退化搁置，不扫2/4，也不直接生产化；F36/F35/F39/F31继续。

## F50 / 步骤029：有收益的迭代候选也必须面对噪声与局部代价

固定5→3得到18点L1−33.6%、4seed pooled full过、phase8→6失败，但不是全部修复：仍一个seed full/6phase失败，局部欠亮明显，暗邻一些通道小幅变差，方差+38.6%。不能只报L1/少两dispatch就称全质量/性能完成，不能拿全域方差代理替代解析/动态/Player。每点每通道和原full/phase全部保存；经验SE筛选不是正式置信保证，也不放宽原门槛。

作用域要实证：148B仅offset92由5改3、Native 9×1+10×2、释放hook/capture后正常render回5。不得把Editor override的结果标成生产默认/Player已过，Runtime仍5；无必要不扫描2/4。030先固定3的8seed/旧合格点/暗控制资格，有收益再动态/HDR/两Player/GPU后取舍。旧F36/F35/F39/F31继续。

## F51 / 步骤030：全局5→3在更广控制失败，搁置迭代扫描

4seed/新18点改善不等于全场景策略可接纳。完整8seed/旧15合格参考发现(141,157)G .912→.848、B .997→.908参考比值，abs error增加超过原硬界；尽管新18 L1−34.5%、旧15加权L1−4.4%、pooled full过，仍拒绝。不能删旧点、放宽/后改筛选界，或让加权平均压过反例。1个seed full/8pooled phase仍失败，方差+40.8%也不能忽略。

固定全局3轮搁置，不扫描2/4，不继续该策略Player/HDR/生产接入。Editor candidate入口/覆盖逻辑已移除，三文件恢复90fc233默认5，过滤后实际渲染资格重跑过。当前历史候选runner入口不再存在，重现只能使用归档精确源在独立验证包，不能重加产品入口。031需反例与亮边的逐轮/邻域权重差异新证据后才开局部机制；不得只围绕新18有利点优化。F35/F36/F39/F31继续。

## F52 / 步骤031：debug shader非bit-exact，不能充当质量记录

加UAV即使不改原算式也可能改变compiled浮点结果：首次功能actual0但均值图不逐bit同，不能改成exact资格通过。stock27点对照与旧全图map逐bit同，因此质量stage只用stock，debug独立权重只作数值解释；正式debug差有量化记录和重建误差，不放宽原5%门槛。去附加块应逐行恢复stock，kernel/shader finally恢复，所有生成体/SHA保留。

反例首Atrous受损、后大步长部分恢复，而亮边逐轮变暗，证明F51全局3轮不可重开。低signal下亮度权重floor1e−4在后期可能主导，亮边/反例的激活程度不同，当前只是新可测机制。032只允许GI私有diffuse floor随scale64对齐的单候选，5轮/其余参数/信号单位不变，不能借此重启1/.125纯单位或迭代数扫描、只优化亮边不验旧点。8邻点没有MC参考，guide不能伪装真值；9采样权重帧也不能泛化全帧保证。

## F53 / 步骤032：floor激活证据不等于调小floor能修复

GI私有diffuse分母1e−4/64（9/10各仅一个token）保持5轮/单位64/其它参数仍失败：新18 L1只−3.9%，full2→0/4、11个phase pass→fail、pooled RGB约−5.03/−5.57/−5.34%。不可用局部微小平均改善或19点无hard-regression掩盖旧full/phase退化，不改预声明20%/原5%门槛。floor确实激活不证明它是主根因；固定策略搁置，不继续epsilon/scale/迭代扫描。

候选入口/私有shader类/meta已从Editor移除，Castle恢复99f4fac，过滤后stock实际资格通过；原源/生成体/失败保留，历史runner入口不再存在。033应先利用现有center/sample方差与权重作离线双向/归一化核对，给出能区别亮边与反例的新机制，不能无依据叠更多阈值/增益。静态输入反事实不是完整GPU递归效果，仍需独立实际资格。

## F54 / 步骤033：冻结重放不是GPU修复，对称luma预测太小

原forward/geometry一致性已核对，不能无依据改坐标/阈值。仅luma正反向几何均值的后期L1预测仅约.0000875，搁置该GPU方向；反向方差不是完整反向kernel，未知reverse几何/jitter/归一化不能臆造。031固定factor2支持域在反例step2会微退化，不能全局启用；最多step>4单候选。

每轮独立使用原输入，不能累加重放收益或当递归/动态/全场景验收。8邻点无MC真值必须保留限制，不扫ratio/epsilon/迭代或删反例。有实际GPU收益还要旧15/全8seed/full/phase/noise和后续资格，F51/F53继续。

## F55 / 步骤034：弱名义收益与raw配对不满足，先停滤波候选

step>4 factor2支持域名义L1仅−8.84%未达原20%，更重要是后两seed raw不匹配旧基线：full逐帧均值max2.219e−6/1.849e−6，不能继续写raw相同或用该比较接纳质量。前两seed同，不代表后两同；根因未证明，不强行归因GPU/driver/场景/共享资源。保留actual0功能完成与comparisonQualified=false，两者不是矛盾。

候选入口/类/meta撤回、默认5复核过；不扫描ratio。035优先stock四seed重复/no-op clone及frame/数值light/sky/RIS/secondary状态定位，严格paired恢复前不试新的滤波参数/算式，不重跑无关MC/AS旧矩阵。033静态预测也不能覆盖这个真实输入差异；F51/F53继续搁置。

## F56 / 步骤035：数值快照漏序列化、失败后误排与恢复边界

未Serializable的GPU struct数组会被JsonUtility忽略，actual0和hash不能证明数值记录齐全；用明确Vector4/uint字段并先真实roundtrip/计数/hash资格。外部python exit1不会天然令PowerShell停止；曾误排下一Unity，已仅停自有子进程并留actual−1/partial。后续EAP+LASTEXITCODE和numeric门槛必须显式检查，不能失败自动串行。

一次资格出现DEVICE_HUNG/removal/Unmap及fatal1073741845、无fresh report，序列化遗漏与device异常分开，不无证归因。先empty/no-AS小compute/fence与CPU schema资格、再新的host观测/短场景，不能原样反复重开大矩阵、改TDR/杀其它进程；当前A/B/no-op重现通过也不抹掉历史。

历史selected secondary末位差在019原界内，不等于证明raw差异原因。当前严格pair可过，不给034原结果补发资格；端点hash不代表全帧输入。后续需新鲜stock配对或同一GI输入双NRD分支，不重启已搁置滤波候选。

## F57 / 步骤036：共享输入资格与URP初始化/退出码边界

空场景不代表URP pool已初始化，初小fixture在Prepare失败，尚无shared frame；wrapper又因null路径Copy丢actual child exit。只能保wrapper1/child未知，不能从EditorApplication.Exit源码补观察值。先64²空相机3request初始化（无GI/AS），先persist退出元数据，可选路径有值才Copy。旧F17同类初始化已知，不再省warmup。

同源输入的3个instance输出bit同/单支reset只影响该支/统一reset重同，证明诊断资格，不代表质量或性能。IReadOnly IN标识、独立OUT/pool/constants与Native数据拷贝要保持；不能嵌套调用后继续用上个native pointer。诊断资源字段只partial logical payload不能叫VRAM。DI成熟diffuse策略也只能GI private单分支对照，不能混入spec/F0或无控制直接生产化。


## F58 / 步骤037：DI成熟diffuse bundle的总体收益不能豁免旧控制

同GI实际输入独立stock/DI-derived NRD对照、1024物理发布校准bitexact、新18误差−42%/旧15总体−31%、full2→4/4，仍旧196141 B和141157 G/B三通道超过预设硬退化界。该完整bundle拒绝生产接纳/搁置，源/失败/收益全部保留；不能靠总体改善、DI成功、噪声筛选通过或门槛临界就改标准。四seed经验SE不是正式置信保证，不直接用额外seed重开预先声明的拒绝路径；先查机制。

Smem variance预模糊也是DI diffuse策略一部分，不能只拷w²输出却称完整DI策略；DI BOTH/spec/F0/material guide不可整份复制到GI DIFF。物理输出要GPU RGBA16F还原并以原版逐帧校准，不能只对demodulated signal评分或CPU还原无资格。新增读回/两NRD为诊断成本，不是产品预算。

后续允许的是新阶段证据：同源两支原27点加196141，实际每ordinal的RGB/variance，先证明观察不改037输出，再定位哪一阶段伤旧点；不是直接重复接纳该bundle或扫phi/epsilon/迭代/ratio、坐标特判。原F51/F53/F55仍搁置；原质量/F39/static/动态/1080缺口不因本轮partial收益关闭。


## F59 / 步骤038：阶段alpha语义与复合策略归因

不能把所有阶段alpha都当variance：Pre是hitdistance，Temporal/Clamp是moment，本轮settled HistoryFix fast alpha0不代表估计variance0，最终Atrous alpha是historyLength；只有9/101/102/103直出variance。Temporal/Clamp由luma/M2离线重构是近似，不能冒充shader同次算出的值，不把demod variance逐通道乘albedo平方。输出RGB重调制与最后RGBA16F物理发布有量化差，必须分开。

阶段3/4/5/6全RGBA同、首Atrous颜色近似同而variance差大，旧点/新18的主要相反变化都在后4轮。另旧196141后期DI variance更大仍恢复弱，禁止直接“提高所有variance”或只恢复首轮blur当修复；目前复合策略w²/min-pair/RGB/交换未分开因果。一个有目的的算子消融可以取证，不能以本轮观察通过为F58策略接纳或换松门槛/扫参数。

独立gather读取避免031 debug-UAV重编译改结果的问题，仍要先112分量F32自检、对原整图stage27点bitexact、对037两branch完整pub map/逐帧27值bitexact和新guide资格；不能只凭公式相同称observer无影响。可选图表依赖不要动全局环境；本机default/bundled Python缺Matplotlib，隔离--target时需--no-user覆盖pip user配置，失败记录与GPU验证分开。


## F60 / 步骤039：恢复旧点不等于同时保留总体收益，暂停局部公式线

同源整图首轮输入逐bit同，单行归一化干预收回旧141157G/B、196141B约89～98%损失，足以支持算子影响；新18改善只3.26%而DI有42%，不得用机制预测通过替换原20%/全控制产品门槛。变体已撤回，原F58仍拒绝。不能在两个归一化端点间扫插值、phi/epsilon/迭代/ratio/位置特判，或把“无新硬退化”当足够收益；029以来此局部公式线长期无可保留生产改进，按用户要求暂搁置，新的结构性证据以后再开。

branch0换成DI参考时必须独立main物理校准、更新断言；baselineMismatches>0是预期，不能仍把branch0当stock。初28点资格不是整图证明，保留初记录，再补强262144RGBA/帧双SHA，不能给历史结果补写已观察。后轮干预同时影响variance反馈，不冒称仅RGB系数或唯一因素。

切换F39前先读018原证据：owned静态mirror/baseVertex0已经试过，原Ho及纯Unity3→5也触发scratch范围，初始5干净。重新照搬017 canonical helper不构成新机制，不重复flags/Static/recreate/delay/capacity。F39保持缺口，可推进固定实例动态Player/成本独立验证但不把它当静态增长资格。


## F61 / 步骤040：动态响应覆盖不是时域收敛，不能无confidence照搬DI的StructureRevision

两实际Player运动48/48全局reset，停姿恢复temporal，表明现保守策略的累积限制。DI31直接保留NRD虽平均噪声改善却阶跃changedL1 .5709失败，DI32用了真实同采样可见性confidence，PrimaryMotion还要求当前匹配表面射线起点。GI白confidence/黑MV不能凭DI代码一行StructureRevision/CONTINUE就上线；先审GI历史样本、secondary transport重求及独立oracle，不把新RNG变化混入confidence，不解禁原GI reservoir保守失效。不重开归一化/phi局部试验线。

240 native camera frames含16 Volume-off，只有224 GI更新，不能把全callback当GI dispatch。ROI运动响应范围包含真实信号，不是噪声MSE；约4.4秒Start后生命周期不是总窗口时长/GPU时间，截图读回开销不作产品预算。F39原生增长仍缺口，8固定实例动测不等于增长支持。有限官方搜索未找到对应fix不等于全上游无fix，不据一般allocator文章盲试旧flags。

先persist元数据再append同一个ordered记录会制造重复entry；保留原JSON/源码后去重，不能数成两次执行或重跑已过Player。每backend独立snapshot。JsonUtility空string序列化为""，不能只允许null错误字段，但必须结合pass/exit/marker/所有数值门槛，不能用空串绕过真错。


## F62 / 步骤041：GI32无次级选择来源、空闲字不会自动传，CPU标签不能代替字节布局

原SDK32B信息碰撞证明相同位置/normal/LogLuv/weight/M/age可对应不同次级DI选择，运动后要不同重求。当前secondary缓存也不随T/S选样。不能从位置/RGB反匹配、把age当sourcepixel、写misc/unused就认为SDK默认Pack/Combine会保留，或用新RNG替代同选择。低级Combine bool+opaque ledger只是字段复制见证，不是实际Shade/T/S关联或动态confidence。NRD实际Spatial1样本与pre-S history0分开，端点需验证，主点到旧位置的开区间visibility不证明secondary仍在。

旧CPU Packet语义标签顺序错位但全word比较覆盖仍完整；实际GPU sentinel验证SDK offsets12misc/16LogLuv/24normal，已改未来schemaGI32-v2。历史normal/radiance分类计数不能按字面使用；原全包0 mismatch/texture质量仍成立，原JSON不改、明示解码映射。不把语义标签修正误报为生产GPU ABI更改。

HLSL point是保留字；lazy compute编译可能在首次dispatch才暴露，GetComputeShaderMessages之前空不等于编译成功。保留20788初编译失败，未写buffer导致的后续CPU错不是ABI证据；同步并在readback后复查编译信息。两个buffer75,776B只是payload，假想1080辅助记录190.55MiB不是已实现/已证明充分的VRAM预算，先低分辨率和生命周期/成本，再重求与生产策略。


## F63 / 步骤042：观察Shader也有编译/UAV预算，常规来源pass不替代全分支或confidence

linear是HLSL interpolation关键字，不能用作局部变量；初capture未写buffer时即使原包/图未变也没有来源证据，必须post-read编译检查与sourceChecks非零。旧runner查256而实际64的fresh=false原metadata保留，手动补存actual64report不能补写成fresh通过。private FinalNRD12UAV warning不能因D3D12就放行；合并5额外UAV为一个uint4缓冲后才0warning资格。

来源查不可变origin ledger而非float坐标/颜色；实际SDK normal encode/decode可改变word，本轮105670次但原版/trace同，不把它当meta串线或反向匹配证据。Spatial1实际Shade和pre-S历史0不能混用，S-off/packedcopy也要拷metadata。

RG/compat常规4T/S/首帧失效/多帧前源和空间源已过，未穷尽age/Jacobian/camera/disocclusion、kind0/3和资源生命周期；必须明确partial coverage，不把pass扩成动态confidence/重求正确或生产历史可放开。逻辑1.875MiB不含额外GI AS/NRD/engine/读回，完整流lossless xz压缩不能丢失败/抽样冒充全部帧。


## F64 / 步骤043：分支应有实际事件或可达性说明，重建不能继续用旧context

生产fallback/permutation/checkerboard为0，不为了穷尽SDK打开新功能；previousM0防御分支0事件需说明有效primary→MakeM1/T/S累积/完整history及无效先禁T的不变量，不写成该分支已执行或永远不可达。Age/Jacobian/material/noMatch/clip/kind0恢复/kind3本轮有真实事件，并核原谓词；事件可重叠，不能把bit计数当互斥路径数。诊断ageLimit2/range.01等同时施于原版/记录版，不是生产调参。

feature.Create重建会销毁旧context/AS/NRD，必须前帧GPU读回完成后清hook/释放aux、丢旧ledger、新context同步seed，后续基础验证重新取gi.LastContext，不能继续读旧local context。固定同实例重建不等于F39原生3→5增长修复。失效帧允许无source但必须M0/meta0/entry事件，普通有效场景不能用空见证过测试。

来源资格和事件覆盖不证明同secondary transport/currentTLAS replay/confidence。下一先原材质/normal/viewDir/DI选择/旧weight/单位与codec可重建，再独立oracle和端点/双段visibility；不新RNG、不按float反查、不无保护保NRD，不推广诊断buffer开销为产品预算。


## F65 / 步骤044：精确方向观测会影响严格parity，静态重放不证明运动端点有效

直接在原Shade增加exactview/材质读取时1002frame1293包radiance word与原版不同；禁额外replay射线仍同，不能归因额外射线或驱动，也不能放宽“原结果不变”门槛。独立Generate后观察dispatch恢复严格parity，保留两失败及各源码。具体编译/排序机制未单独定位，不把它泛化成已确认优化器bug。capture-only未dispatch的replay buffer不可作oracle。

exactsourceview重求与GIcache LogLuv全同，packed16view仍2756编码差，0.0015666relative max不能忽略；未证明16B记录充分。CPUoracle阈值是绝对/相对结合，HDRmaxabs.000818不是固定5e-6以内；GPU visibility/env纹理采样作为输入不等于CPU独立验证TLAS/alpha/环境。

旧secondary材质+开区间primary->旧点通畅，不证明当前geometry仍在旧点。静态replay pass不可直接改StructureRevision/CONTINUE，下一要previous lastShade/current端点及matched primary、双段visibility、零→亮/alpha/域变化oracle，记录和量化/原weight匹配后再动态confidence与成本。原rawGI保守失效/旧色Volume硬重置保持。
