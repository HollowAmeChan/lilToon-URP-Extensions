# 032 单位对齐分母floor候选：拒绝并搁置

**候选未通过，生产和原版5轮保留。** 依据031的实际权重线索，只在当前GI实例替换shader9/10，将精确diffuse亮度分母`1e-4`改为`1e-4/64`；没有改实际signal单位64、variance、phi、5轮、settings、tap cutoff或specular。wrapper/body逐行恢复证明每个shader仅一个token变化，原vendor/Runtime/DI文件未改。

21760短资格actual0，2seed×16warm/16观察、98请求；632正式actual0，固定4seed0/104729/5347609/6396185、各64warm/256观察、1314请求。Native仍9一次+10四次，source scope恢复。API/Shader/Property/UAV/fence/drift0；F32 copy资格过。使用031 stock 27点路径（19有同模型参考、8仅guide），没有再启用debug-UAV观察版作质量记录。

| 同4seed结果 | stock | floor/64 |
| --- | --- | --- |
| 新18/reference L1 | .462545 | .444431（仅−3.916%） |
| 原full通过seed | 2/4 | 0/4 |
| 每seed phase失败 | 7/11/11/7 | 9/14/12/12 |
| 合并full RGB误差 | −4.718/−5.107/−4.807% | −5.029/−5.567/−5.337% |
| 新增phase pass→fail | — | 11项 |

局部改善未达预声明20%，原full和phase退化；19参考点未触发同一局部硬退化筛选，并不能覆盖这些失败。反例(141,157)变化很小：G/reference约.9155→.9134、B .9963→.9937。raw所有局部样本与pre-Atrous原流对stock逐bit同；变化来自候选本身。全域方差各seed仅约−.27%～+.18%，不能当新显著降噪收益。

这否定的是**该固定下限对齐策略足以改善整体**，不否认031观测到floor确实激活。不得围绕epsilon、单位、迭代数继续无依据扫描，也不把新18点微小平均改善接入生产。

过滤后将Castle精确恢复99f4fac，移除新增LumaFloorAudit及meta，候选入口/私有shader覆盖不留在Editor产品代码。43936过滤后原版stock control实际98请求actual0，F32 copy/Native5/原math scope通过，API/compile/绑定/fence/drift0。原候选源/生成体/SHA/结果完整保存，不把当前源标成原实跑源。历史run_validation入口当前已移除；重现须用归档源在隔离包，run_filtered只验证当前stock。

254精确source对象、3实际进程完整logs、27原JSON/bin lossless gzip、生成shader/body、review-contract/analysis/filter-review/verify。均值原5%和全部phase失败原样保留；没有进一步8seed/HDR/Player/生产部署。

下一033先复用031已记录center/sample variance和实际权重，离线核对双向luminance权重/方差依赖与归一化传播，解释为何同一过滤对亮边与反例作用相反；明确静态输入重放不能代替迭代GPU结果。只在出现可区分两类结构的新机制后再开一个局部候选，不再重采既有MC/重复完整矩阵或扫描常量。原F39/static/复杂动态/1080完整预算继续，目标active。
