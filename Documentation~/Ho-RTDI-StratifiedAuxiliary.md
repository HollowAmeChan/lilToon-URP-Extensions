# Ho-RTDI：辅助最终样本随机分层生产接入

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
