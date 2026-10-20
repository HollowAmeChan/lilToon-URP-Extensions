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

长期无收益路线的记录模板：问题与基线 → 假说/参考 → 尝试和真实成本 → 哪些指标改善/退化 → 拒绝或搁置原因 → 禁止原样再试的条件 → 重开所需新证据。新记录必须关联过程步骤与报告，不只写“试过，不行”。

F01～F06主要来自DI/参考历史，不伪装为本轮GI实测失败。F08～F10为步骤001的协议/诊断陷阱，修正后重新执行；步骤005独立降噪有均值/时间方差实测收益，但不能扩称为动态或完整目标场景质量结论。
