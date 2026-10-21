# 步骤019：独立seed与复用模式对照

**本步有定位收益，没有生产画质修复。** 四模式/8个seed矩阵实际完成，时间加空间复用出现明显均值偏移；所有32段的filtered/raw仍未通过原5%门槛。Runtime、Shader、SDK、NRD参数和Player未改。

## 固定协议与实际终态

Editor入口`HoRTGICastleValidation.RunSeedMatrix`。原古堡full256²、Linear、D3D12 debug层、18灯/15实例/12Cutout/次级天空；四模式none/T/S/TS。预先固定seed为0、104729、1153305、2201881、3250457、4299033、5347609、6396185。每段重置GI/NRD历史，64帧warm、256帧观察，实际Editor tick分散：32×320=10240请求，含场景初始32及末尾两次合成请求合计10274。末尾合成只是旧入口功能检查，不是正常enabled-camera或稳定画质验收。

最终PID6104 Unity实际exit0，debug/编译诊断/源漂移全0；functionalPass=true。每段首尾用017 probe对比原full/RAB/main history与reference primary，最大误差约1.9e−6，灯/sky/AS/支持域稳定。实际T状态、shader frame递增受断言约束。最终矩阵的同seed四模式首尾selected secondary记录及完整RIS hash均逐字节相同；记录可从JSON字段重建原48 B并复核hash。这是首尾16点的配对证据，不能外推所有帧/全图光线一律逐字节匹配。

参考沿017同模型无复用高样本结果，使用fixed-reference及fixed-controls覆盖两控制点；15/16收敛，暗(16,16)green仍不确定。点有事后高loss/gain选择偏差，参考共用材质/light实现，不能当独立绝对着色真值或全图验收。

## 结果

全图对比采用8个segment均值；SE按8个seed均值计算，不把相关帧/像素当iid，不提供正式置信保证。

| 模式 | raw相对无复用RGB | 配对差SE / 无复用RGB | filtered相对本模式raw RGB | full原5%通过段 |
| --- | --- | --- | --- | --- |
| none | 0 / 0 / 0% | 0 / 0 / 0% | −15.59 / −17.40 / −17.75% | 0/8 |
| T | +2.84 / +2.84 / +2.91% | .101 / .150 / .105% | −13.11 / −14.91 / −15.53% | 0/8 |
| S | +2.59 / +2.95 / +2.44% | .071 / .130 / .097% | −10.68 / −12.70 / −13.20% | 0/8 |
| TS | +25.70 / +22.21 / +18.75% | .418 / .354 / .333% | −7.96 / −8.70 / −8.99% | 0/8 |

none full raw RGB=(.001228918,.000130884,.000120137)；TS=(.001544720,.000159953,.000142659)。TS filtered仍比无复用raw高约15.70/11.58/8.08%；不能见filtered下降便一律补gain。none完整MC也是有限预算，不能由此称独立全图真值，但模式间偏移远大于该预算下seed均值波动。

点(128,128)红：none raw/reference=.9202（seed SE/reference=.1111），T=.9707±.0739，S=1.2335±.0695，TS=2.1819±.1456；TS filtered/reference=1.7208。点(87,165)TS raw/reference约1.0156±.1631，与017旧固定256窗16.48倍差异明显；有限固定窗口和选择偏差不可忽略。这里“±”只表示一个经验SE。所有点/通道、full/原phase0、每段方差和32帧块均值见`analysis.json`及原始报告，不挑最有利点接纳整体。

## 两次拒绝与精度合同

首资格PID45524 exit1：8段完成，首尾secondary byte hash不全一致，原始run/source/progress保留`rejected-paired-hash`。当时没存逐字段记录，因此具体差异未证实，不能由后续结果补写根因。

加逐字段记录后资格PID11552 exit0，8小段端点逐字节匹配。随后原完整矩阵PID43444仍因byte gate实际exit1，32段完整捕获保留`rejected-matrix-paired-hash`，不得改绿。逐字段复核：504组position有浮点末位差异，hit最大1.22177e−5；miss合成的100000单位远点最大.008789；18组normal差一个oct16分量单位。flags/material/emission/PDF/geometric normal和RIS全相同。没有证明具体产生浮点差异的引擎/调度根因。

后置检查改用原已有hit/world误差.0005上界（primary检查不变），miss检查unit direction≤1e−6及8×FP32 epsilon的合成半径误差，normal允许一个oct16量化步；flags/material/emission/PDF/geometric/RIS仍严格。`paired-contract-correction.patch`表明只改后置比较/报告，不改采样/渲染路径或5%门槛。verifier也对旧记录独立执行这些数值检查。随后完整重跑PID6104真实exit0，最终端点恰好逐字节相同；不声称改后置检查导致GPU数据相同。

## 证据、复现与下一步

`run_validation.ps1 -Mode qualification|matrix`具有独占锁保护、D3D12 debug拒绝、15分钟/fence只停止自有子进程、首尾源hash漂移检查。`run.json`为最终矩阵，另四attempt目录有实际元数据。239个精确source hash中97项引用017已保存的内容寻址blob；其余保存在本步source-blobs，source-files保留原目录对应。大原始JSON仅gzip压缩，解压逐字节及SHA不变，`capture-archives.json`同时绑定原始/归档SHA与字节数；日志完整保存。总证据约3.44MB。

运行`python Tests~/RTGI/Seeds/analyze.py`重算统计，`python Tests~/RTGI/Seeds/verify_validation.py`检查四进程、输入/源码/日志、完整请求和采样计数、逐字段hash、旧失败/精度更正、全部原5%失败与analysis。Runtime没有修改，因此无新Player或GPU成本证明；不要用本诊断CPU/ReadPixels耗时作产品GPU预算。

下一项020先做**仅Editor的pre-spatial history候选**：当前帧保留T/S，Final给buffer0保存buffer2的时间阶段结果，避免把buffer1空间输出跨帧再次反馈；不关闭S，不改gain/sigma/Jacobian。原参考TS buffer schedule同样反馈空间结果，这是一项有依据的策略实验，不宣称发现集成索引bug。Final后写history可避免Temporal读写buffer0的竞态，不新增reservoir资源，但会多一次32 B读取，需要实际收益/噪声/成本与回归证据后再考虑生产。旧5%、static非零base/F39、moment/HDR、正常camera/1080/完整预算仍未完成。
