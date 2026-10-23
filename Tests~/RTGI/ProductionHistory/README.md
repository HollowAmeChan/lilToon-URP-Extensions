# 步骤021：生产历史策略已接入，完整质量与AS门槛仍未通过

Runtime改动仅`HoRTGI.compute`的Final历史写入：S开启时，将时间阶段buffer2的packed packet原样复制到history0；S关闭保留原store。当前帧仍用空间结果buffer1求值/最终可见性/NRD。没有新资源、SDK/参数/增益/NRD修改，也没有产品实验开关。Final在T/S之后写0，避免Temporal读写0的竞态。

## 实际生产验证

| 实际进程 | 结果 |
| --- | --- |
| 19188 / 30080 | 原古堡RG/compat，各226请求、8次全视口history检查；exit0/debug0/compile0/source drift0 |
| 47824 / 12240 | 原次级环境RG/compat门槛通过，exit0/debug0 |
| 45552 / 25412 | 原Cutout RG/compat门槛通过，exit0/debug0 |
| 5316 | 原蒙皮GI实际UV/normal/tangent、骨骼/混合权重/Morph与baseVertex命中门槛通过，exit0/debug0 |
| 11072 / 36020 | 两个Win64 Mono Release Player，各315真实Update请求、38项检查、23次history检查；exit0/debug0/runtime diagnostics0 |
| 47984 | 实际生产古堡256观察帧完整阶段审计，debug0/compile0；仅原5%失败，真实exit1，未改绿 |

生产合同入口`RunProductionHistory`及Compatibility直接绑定Runtime compute，不用020候选。S开时全65536 pixels history0精确等于Temporal2，Spatial1/Temporal2实际不同约5.8～6.5万包，排除“未执行S/误读同一buffer”的假通过；S关原路径亦记录。两次/四模式共8检查，环境/Cutout有效，publication完整有限，actor域15实例12Cutout。

## 核心夹具失败与原生错误

23244/40360首个核心RG/compat实际exit1，断言“Spatial未合并邻居”：旧夹具拿history0当Spatial输出。新history0有意保存pre-S，所以只把读点改为实际buffer1，原M>1阈值不变。失败源码/日志保留`rejected-old-spatial-reader`，没有放宽能量/复用门槛。

修正后12884/48892的全部功能断言通过、Unity实际exit0，但debug层各有两条2304 B `ScratchAccelerationStructureData`范围错误，严格runner因此退出1，**核心完整回归未通过**。2260另进程实际绑定020冻结legacy Final，仍exit0/功能通过并出现同样两条2304 B错误；`[HoGIHistoryLegacy] BOUND`和原compute SHA俱全。它复现了已知F39范围问题，不支持归因新历史策略，也没有证明具体Unity/驱动根因。没有关闭debug或再盲调AS。当前保留有收益的局部生产改进，这项硬缺口继续。

## Player与构建警告

构建输出独立`PlayerGISkinnedHistoryFeedback`与Compatibility；资产亦用独立HistoryFeedback前缀，不覆盖017 payload。两个真实build均exit0/0错误。首次17284的成功RG build被包装脚本因5条Shader warning拒绝，元数据/源保留`rejected-build-warnings`，当时未运行Player。

核对017 RG/compat原始build log/SHA后确认5条逐字相同：lilToon及Cutout各两个重复keyword，既有SolarHD的d3d11 Shadows.hlsl除零警告。只允许这5条精确行，其它warning/error继续拒绝；未修改其它模块。没有把diagnostics5改成0。审查后确认实际源码内容未变，复用该RG成功build并保留原run/SHA；compat 47996重新build。最终runtime两路径0 diagnostics，source drift0。`build-warning-review.json`及完整旧日志支持出处。

Player原能量/空间L1/方差、相机移动、灯光/几何变化、Volume/NRD启停、Cutout与骨骼/Morph/baseVertex门槛均保留。各315 requests/310观察记录、59个相机重投影帧、24个变化几何reset；空间L1约.10792/.10794（原<.15），filtered/raw方差比.001053/.001064（原<.4）。源mesh nonreadable/base4、三个owned snapshot base0的原断言继续。每个Player新增23个状态、94208个pixel packet history0=2检查，实际compute名HoRTGI；sample52还要求Spatial与Temporal不同，能拒绝旧反馈。覆盖NRD开/关、Cutout native/opaque、pose改变/失效/恢复。完整payload SHA清单，无HoGIGpuProbe.dll。

依旧是每个真实Update一次SubmitRenderRequest，**不是正常enabled-camera自动呈现验收**。旧probe Save的通用文件名导致最后compat覆盖RG预览，归档两张PNG只代表compat，不补造RG图；两条数据报告/源/payload完整。

## 生产质量与剩余差距

实际生产长窗口full rawRGB=(.001328435,.000140335,.000128173)，filtered=(.001250704,.000129084,.000116730)。filtered相对019有限none均值约+1.773/−1.375/−2.835%，保留020方向；none也是同模型有限预算，不能当独立全图真值。

原filtered/raw fullRGB仍−5.851/−8.017/−8.927%，所有16相位也失败。原相位/门槛及真实exit1保留；不能只挑新比较器宣布完成。当前完整阶段RGB显示：input红.00132840、Pre .00133402、slow Temporal .00127925、Clamp .00127787、最后Atrous .00125113、published .00125070。HistoryFix5仍是条件fast RGB，不把alpha误当slow moment。后续先据这些实际阶段及015矩精度证据处理剩余局部/降噪问题，不盲补gain。

## 复现证据与交接

`run.json`为生产合同；`regression-run.json`/rejected-old-spatial-reader保留最初7个回归；core-corrected与legacy-control保存debug不合格结果；quality保存真实画质失败；player保存原构建拒绝与经精确warning审查后的实际运行。所有进程已结束。

1191个精确source SHA：240引用既有blob，897直接引用未修改的基线Git blob与明确LF/CRLF重建方式，54保存为新blob。这样保留全Runtime构建出处并避免把无关未改代码再复制入提交。`source-files.json`保存原目录对应，verifier批量读取Git对象核对逐字节SHA；基线commit为review.json中的reachable祖先。原大JSON仅gzip、原始/归档SHA与字节数俱全。19个完整日志含017警告对照，归档约5.48MB。现存EXE/完整payload只hash不放进包。

运行`python Tests~/RTGI/ProductionHistory/analyze.py`与`verify_validation.py`验证生产唯一Runtime diff、实际history/反事实、原/修正/legacy失败、精确warning规则、315真实帧/23状态、Release payload以及仍失败的画质/AS门槛。旧020 Generate与verifier要求旧Runtime，按3b6937e历史运行，不回滚此生产改进满足旧SHA。

下一项针对当前剩余质量：先读当前production stage与015 slow-moment/实际绑定，定位Temporal/后续Atrous局部损失和精度；NRD默认64、HDR/动态安全与成本一并保持。F35禁止继续纯单位盲调。F39不重试旧flags/延时；本机Unity RTX参考为Automatic并直接Build，若将来有新的API/分配机制证据才允许有界原生对照。正常enabled-camera与1080/完整预算仍未完成，goal保持active。


041诊断语义更正：当时CPU Packet字段标签偏移与SDK不符，旧normal@12实为misc/M/age、misc@16实为LogLuv radiance、radiance@24实为packed normal。32字节全包Equal覆盖全部word，因此原packed-source复制/texture质量判据仍成立；旧分项normal/radiance计数和JSON标签不能按字面使用。原源/原报告不改，映射和实际GPU ABI验证见[ReplayContract](../ReplayContract/README.md)。
