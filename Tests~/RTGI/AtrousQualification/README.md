# 030 完整8-seed资格：固定3轮候选搁置

**候选未通过，生产保留5轮。** 新18点与总体均值的改善不能抵消旧合格控制点的硬退化。未扫描2轮/4轮，也未进入HDR/Player/生产部署。

19396资格actual0（2seed×16warm/16观察，98请求）、39616正式actual0（原8seed，各64warm/256观察，2594请求）。148B settings只有offset92 uint5→3，所有观察帧实际Applied3及Native Atrous总3，结束后hook恢复、正常render Native回5。API/Shader/Property/UAV/fence/drift0。旧16点primary/full/history/ray/support合同通过，15参考合格、暗(16,16)仍未定；新18点从完整map读取，并对029重合4seed交叉核验。

| 8seed结果 | 5轮baseline | 3轮candidate |
| --- | --- | --- |
| 新18点/reference L1 | .458213 | .300045（−34.52%） |
| 旧15合格点/reference L1 | .306745 | .293238（−4.40%） |
| full通过seed | 3/8 | 7/8 |
| 合并full RGB误差 | −4.716/−5.158/−4.823% | −4.533/−4.595/−4.113% |
| 合并phase失败 | 9/16 | 8/16 |
| filtered全域时间方差均值 | 5.80513e−9 | 8.17309e−9（+40.79%） |

仍有一个seed full和8合并phase失败。旧16（含未定）+新18共34个点全部报告，33合格点按029预声明error-increase界筛查；相同规则没有为本次失败放宽。

旧控制点(141,157)是拒绝依据：

| 通道 | 参考 | 5轮均值/参考 | 3轮均值/参考 | absolute error增加 | 预设硬界 |
| --- | --- | --- | --- | --- | --- |
| G | .000378142 | .911986 | .847986 | 2.42011e−5 | 1.89071e−5 |
| B | .000096500 | .996998 | .908287 | 8.56060e−6 | 5e−6 |

R同样变暗（.8862→.8414），增加尚未越原筛选界。G/B超界，因此即使加权L1改善、原phase无pass→fail，仍不接纳。经验SE仅是原筛选辅助，未变成正式CI或物理真值保证。8张全图raw均值图及全部逐帧raw均值对026逐bit相同；新18点与029重合4seed均值逐bit同，数据变化不能归因采样更换。

过滤：将Castle、LocalStageAudit、SeedAudit恢复到提交90fc233的默认5轮诊断版本，移除candidate入口/覆盖/专用设置记录逻辑。原实际候选字节全部在source-index和source-blobs，未重标历史源码。19648过滤后重新编译并真实98请求资格actual0、copy64分量逐bit过、Native每帧9一次+10四次（5总）；原采样/Runtime不变。`filter-review.json`及verifier绑定前后SHA和精确Git字节。

250精确source对象、8个新增小文本、3完整logs、3原JSON+12原maps/streams lossless gzip，约12.8MiB。`run_validation.ps1`是历史候选入口（当前已删除）；重现须从归档源在独立验证包恢复候选，不往产品重加入口。`run_filtered.ps1`验证当前默认5轮。分析/原参考输入SHA/全部34点/原full和phase/设置scope/过滤资格均可核验。

下一031不再扫描全局迭代数：保持5轮，比较新18点与反例(141,157)及相邻控制的逐轮Atrous输入/输出，必要时读取实际邻域权重，区分哪些空间传播帮助或伤害不同结构。先补反例的阶段/邻域证据，再定一个局部机制；不得用本次局部收益直接全局cap3，或重跑已有1M参考/完整8seed而无新机制。原F39/static/复杂动态/1080完整预算继续，目标active。
