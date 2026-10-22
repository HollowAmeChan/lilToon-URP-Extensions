# 034 大步长factor2支持域：撤回，配对资格未满足

只Editor GI实例shader10增加一条guard：gStepSize>4且sampleLuma不在center[.5,2]时原权重归零，双零保留。5轮、其它算式/settings/phi/variance/单位64/格式不变；没有扫ratio。源码去该行并恢复include后与stock相同，原文件未改。

39032资格actual0/98请求、31792正式actual0/1314请求，API/Shader/Property/UAV/fence/drift0，typed-copy64过，Native5与shader恢复通过。实际数据记录和严格配对资格分开：**后两个seed raw不完全匹配旧基线，因此名义改善不能标成受控因果收益。**

名义新18/reference L1 .462545→.421666（−8.84%），未达预先固定20%；反例RGB/reference基本同，未触发局部hard-regression。原full仍2/4、pooled RGB−4.691/−5.030/−4.716%仍未全过，phase7/11/11/7→7/10/11/7，无pass→fail；方差约+4%～+11%。这些只是有输入差异限制的对比，不作最终质量/性能结论。

严格复核捕获到：seed0/104729的raw局部样本与preGuard stage逐bit同；seed5347609/6396185不满足。全图逐帧raw均值最大差分别2.21911e−6/1.84949e−6（各767/768 RGB分量不同），27点raw单样本最大差约9.16e−5/.001495。根因未证明，不归因引擎/driver/场景/NRD反馈，也不改阈值掩盖。verify明确保留comparisonQualified=false，不能写“raw完全一致”。

候选撤回/搁置：Castle恢复99f4fac，新增ContrastSupportAudit/meta删除，49216，stock control真实98请求actual0、Native5、F32copy/原scope通过，API/compile/绑定/fence/drift0。原实际源/生成体/SHA与全部失败保留，Runtime一直未改。历史候选runner入口当前已删；重现只能使用归档源在隔离包，不能重加产品。

下一035优先原版默认5的同4seed重现性和无操作clone对照，先查明后两seed raw差来自可重复的观察/资源/seed-reset问题还是基线变化；记录实际shader frame、numeric light/sky/RIS与secondary状态，不能重复参数/MC矩阵或无证AS flags。严格配对恢复前停止新的滤波算法候选。全目标和原F39/static/复杂动态/1080完整预算继续active。
