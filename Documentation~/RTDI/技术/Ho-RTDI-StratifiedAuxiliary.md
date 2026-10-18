# Ho-RTDI：辅助最终样本随机分层生产接入

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第100步生产化第99步候选。aux header新增两半独立抽样与随机交换；native final visibility生成一次pair，opaque shade生成一次pair，native shade读取既有payload不重复RNG。1/PDF、CDF、primary提案、等权合成、两辅助射线、8-byte sidecar及默认Single保持，没有新增设置/资源/ABI。

依据用户fork/固定SDK finalized reservoir合同，分层改变样本间相关性，保留每槽位原CDF边际（理想均匀RNG）。浮点离散误差与有限样本仍存在；不称严格逐位无偏或任意最终过滤都改善。每pair新增一个hash用于随机交换，GPU毫秒未测。

## 接入后实际验收

- native/opaque四灯域通过，生产结果与第99步一致；native frameL1 .294800/.184768/.285091/.290194，对比原.324357/.202892/.311013/.308588。actual inverse PDF、pair两strata资格、均衡两灯不重复、CPU visibility与cache门槛保持。
- 72帧无中间readback的Single/native/opaque、尺寸与资源切换通过：36/18/18帧，retired peak3、最终回收0，单灯/零灯无辅助分配。
- compatibility双相机128点/1024长期均值通过，rawWorst .00512345、filteredWorst .00680914，相机资源隔离通过。旧单帧1% diagnostic仍记录大量失败，不把随机瞬时点值改成新的通过指标。
- 两个Win64 Mono Triple release构建成功，各errors0；actual RG/compatibility Player全部probe pass/exit0。两报告rawMSE .012600466、filteredMSE .000152352、HDR relative .0108911，材质/深度/初始可见性/混合光/环境与时域门槛保持。D3D11 inactive pass/exit0。

第99步动态9/9与两环境路径2/4是同算法隔离证据，第100步没有重复宣称全矩阵。混合天空spec filtered失败继续保留。没有四个Single/Triple × RG/compatibility Player全重建或IL2CPP验收；Single由实际状态切换验证、本步只重建改变的Triple Player。

测试生成器遇到完整生产marker/消费者后直接测试原shader，避免叠加分层。第88步基于独立辅助采样的已拒绝surface RIS生成器明确拒绝在新基线上未评审组合，避免历史实验静默生成错误shader；历史提交仍保留该候选复现源。

日志前缀 `stratified100-`；actual Player报告 `rtdi-stratified100-player-{multi-rg,multi-compat,dx11}.json` 位于隔离工程。Roslyn errors0、无新增DI shader warning/error、diff通过；构建其它模块原有warning不并入本步修改。GI不包含aux helper，无共享合同变更，未追加无关GI构建。

## 后续

当前采样已有具体收益，但混合天空的后续NRD静态搬移与lobe距离合同未完成，GPU预算仍未证明。下一项应分别测primary天空与两辅助局部贡献的实际方差/协方差，确认剩余噪声来源后选择进一步采样或过滤控制，避免把局部收益扩称全管线完成。古堡仍只作合并天空/网格支持审计，不增加PT/GI或最终NPR合成范围。


## 第99步候选对照（历史证据）

> 此节保留正式接入前的候选/评审证据；其中“下一步”“未改 Runtime”仅描述当时状态。当前状态见本页开头和 RTDI 总文档。

第99步回到原CDF与目标，不延续第98步NRD信号域策略。核对用户fork/固定SDK StreamSample/FinalizeResampling、RNG[0,1)合同和运行时CDF区间1/PDF，将两个辅助最终样本分别在CDF下半/上半独立抽样，再随机交换槽位。没有固定反相关索引，也不要求灯顺序按颜色或空间排序。

理想独立均匀随机数下，每槽位是两等概率条件分布的混合，边际仍为原CDF；原1/PDF和两样本等权组合保留。分层降低该辅助平均估计的区间间方差，不宣称消除每通道/最终滤波/任意有限序列误差。浮点实际使用固定23-bit RNG和FP32 CDF，仍有离散误差，独立能量门槛检验保持。

只在隔离compute/ray/header作用域替换。两个native查询、compact8-byte、cache/visibility、primary ReSTIR/目标与NRD不改；inline opaque同一算法，native shade不重复生成已由ray pass产生的随机对。比原两draw多一个RNG hash用于交换，无额外RT ray/资源，GPU毫秒未测。

### 实测

| native 256²灯域 | 原frameL1 | 分层frameL1 |
| --- | --- | --- |
| Spot rapid | .324357 | .294800 |
| Spot step | .202892 | .184768 |
| 32 weak rapid | .311013 | .285091 |
| HDR rapid | .308588 | .290194 |

四项全部通过，step变化点L1 .143168（原.203049）。原raw3%/filtered5%/frame及变化点.35门槛保持。每项128次GPU检查保留actual inverse PDF/identity/CPU alpha visibility，并新增pair覆盖CDF两半的合同；精确平衡两灯不重复选择同灯。32灯允许一个CDF桶横跨两半，不能仅看light index不同就判断正确。

Opaque四项同样全部通过，frameL1 .057575/.082149/.060973/.052548。第二样本保留局部消费，没有为验证结果添加全屏sidecar；该路径没有第二payload逐样本读回，评分不能扩称两payload metadata证明。

真实动态九阶段9/9、exit0，100:1 raw红1.012524/filtered .996599、关闭通道dark0，packed shading分解最大相对误差 .0009674。生产第93步九阶段已通过，不能把本步当作首次修复该失败。

混合天空两路径仍2/4/exit1：none filtered spec红/蓝1.050288/1.048948、TS1.043948/1.050162，保留原失败域；raw3%保持。分层没有解决该静态NRD问题，不据灯域收益声称环境已修好。

Roslyn errors0、GUID唯一、六次实际执行无新增DI shader warning/error或SourceAssetDB时间错误，diff通过。native shade RNG guard修正后实际环境复跑、原结果保持；没有因小代码优化重复完整矩阵。

报告 `rtdi-stratified99-lightdomain-{cutout,opaque}-rg.json`、`rtdi-stratified99-dynamic-cutout-rg.json`、`rtdi-runtime78-env-baseline-stratified99-rg/compat.json`，位于隔离工程。

### 取舍

本步不改Runtime。已有两backend灯域收益、边际/权重/可见性与动态没有新正式失败，值得下一步窄生产接入，随后actual Player及状态切换/相机资源回归；默认Single与旧环境失败保留，不添加设置或改变caster/material合成。无需借本步扩张GI/PT或NRD资源。
