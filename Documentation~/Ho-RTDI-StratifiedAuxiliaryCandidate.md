# Ho-RTDI：两辅助最终样本随机分层候选

第99步回到原CDF与目标，不延续第98步NRD信号域策略。核对用户fork/固定SDK StreamSample/FinalizeResampling、RNG[0,1)合同和运行时CDF区间1/PDF，将两个辅助最终样本分别在CDF下半/上半独立抽样，再随机交换槽位。没有固定反相关索引，也不要求灯顺序按颜色或空间排序。

理想独立均匀随机数下，每槽位是两等概率条件分布的混合，边际仍为原CDF；原1/PDF和两样本等权组合保留。分层降低该辅助平均估计的区间间方差，不宣称消除每通道/最终滤波/任意有限序列误差。浮点实际使用固定23-bit RNG和FP32 CDF，仍有离散误差，独立能量门槛检验保持。

只在隔离compute/ray/header作用域替换。两个native查询、compact8-byte、cache/visibility、primary ReSTIR/目标与NRD不改；inline opaque同一算法，native shade不重复生成已由ray pass产生的随机对。比原两draw多一个RNG hash用于交换，无额外RT ray/资源，GPU毫秒未测。

## 实测

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

## 取舍

本步不改Runtime。已有两backend灯域收益、边际/权重/可见性与动态没有新正式失败，值得下一步窄生产接入，随后actual Player及状态切换/相机资源回归；默认Single与旧环境失败保留，不添加设置或改变caster/material合成。无需借本步扩张GI/PT或NRD资源。
