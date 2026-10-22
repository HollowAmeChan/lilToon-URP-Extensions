# 036 同源输入双独立NRD资格

两个额外RELAX_DIFFUSE native实例读取**同一份实际IN纹理/资源标识**以及value-copy的CommonSettings/RelaxSettings，各有独立pool、constant buffers与OUT。branch0原版、branch1 literal-stock shader10 no-op；原GI baseline照常执行。Editor-only RecordInputs hook位于Common/settings生成后、baseline Record前，Player编译排除；没有改生产参数、Shader或DI。

最初54932在URP RTHandle pool未初始化时失败（实际报告pass=false，尚无比较帧）；wrapper又误把空生成路径当目录Copy，未保存子进程实际退出码。保留initial-allocation与wrapperExit1/childExit未知，**不根据源码推测补成actual1**。修为64²空相机3次request初始化URP（无GI/AS），先持久化child退出元数据，再处理可选路径。

41304修正后64² small actual0：64个确定性信号帧，baseline/branch0始终RGBA逐bit同；branch1在frame24单独reset后只该支变化（最少14596分量不同，超过alpha-only4096），frame48统一reset后恢复同。输入标识相同、可写资源独立、scope hook释放与shader/kernel恢复通过。

38552实际GI短资格actual0、98request，32观察帧RGBA全同；54328正式actual0、1314request，4seed0/104729/5347609/6396185各64warm256观察，1024个观察帧×262144分量 baseline/branch0/no-op branch1全逐bit同。包含warm共1280次shared callback，重置随baseline实际common原值传递。四seed整个raw+filtered时间均值图和逐帧raw均值与026完全一致，observer未改变已验证原输出。

所有有效进程API/Shader/Property/UAV/fence/source drift0，Frame记录保留实际common/settings SHA、reset flag、三输出SHA/bit-diff/maxError。端点/输出hash是证据，类中只读IN绑定与独立OUT/pool/constant检查共同限定范围；不是完整GPU输入逐byte导出或新画质验收。

259精确source、9新小文本、4完整logs（含首失败）、22原capture lossless gzip、生成no-op体/原输入SHA/analysis/verify约11MiB。`diagnosticLogicalBytes`字段只计两个branch的external output/validation，不含native pools/constants、更不是物理VRAM/产品成本。额外三次NRD与CPU读回只是诊断开销。Runtime变更仅UNITY_EDITOR hook，默认null，无Player字段/调用，无新产品用户流程。

已复读并冻结本机DI成熟路线：[DiffuseExchange](di-reference/Ho-RTDI-DiffuseExchange.md)、实际Atrous/Smem派生。DI修复涉及首轮temporal-variance按w²传播、后续pair bandwidth/RGB contrast、拒绝的固定Gaussian系数保留中心；它不是单纯缩迭代/下限/支持域，并有增噪与动态限制。不能直接认定GI最优。

下一037在branch1做一个GI私有、可精确重建的DI diffuse-exchange策略对照，branch0保原版；同源输入、5轮/时间/单位64/格式/SDK不变，specular/F0/DI身份guide不带入。先来源与scope差异审查，再小资格与原新18+旧15/暗控制/full/16phase/noise；不足或hard-regression撤回，不重开F51/F53/F55。全部原质量/static/F39/复杂动态/1080预算和目标active继续。
