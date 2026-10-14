# Ho-RTDI：弱彩灯重要性目标隔离候选

第92步只修改隔离compute的ApplicationBridge副本。核对用户参考工程 `RtxdiApplicationBridge/RAB_LightSampling.hlsl` 的表面target与固定SDK `DI/Reservoir.hlsl` 的StreamSample/FinalizeResampling：target用于选择及重用归一化，不等于实际着色值。候选将零F0分支从 `length(max(0, contribution))` 改为它的平方根，降低强弱贡献比例；源PDF、RIS预采样、初始8候选、历史4、空间4、最终预算、真实BRDF/radiance和NRD均保持。

目标非负、原有非零贡献仍有支持，零贡献仍为零。初始/时间/空间及偏差修正统一消费同一个目标回调。非零F0亮度分支保持；材质相似性仍拒绝跨提案类别的复用。native只作最终求交/实际shade梯度、不调用target，无需替换ray shader；两个fresh辅助样本的1/PDF提案保持。

## 实测

| 100:1变化，96帧、前16帧不计 | raw红比解析值 | filtered红 | primary红相对时间方差 | raw红相对时间方差 | 九阶段 |
| --- | --- | --- | --- | --- | --- |
| 原Power | 1.116960 | 1.086514 | 165.8726 | 45.3320 | 8/9 |
| 候选Power | 1.014597 | .999766 | 15.0090 | 7.6370 | 9/9 |
| 原Uniform实际对照 | .927651 | .920332 | 105.7181 | 30.36 | 未全部通过 |
| 候选Uniform | .974715 | .993279 | 14.0642 | 7.5269 | 9/9 |

Power两路径一致，实际两进程exit0；Uniform候选exit0、原Uniform对照exit1。辅助红均值.971107相同；主样本相对时间方差减少约90.95%，这是同固定序列弱红通道的观察，不代表任意亮色/材质/视点的全图噪声结论。

其它独立验收全部实际exit0：

- Single的256/512 F0=0/1边界、左右交换、none/TS，32区域通过，最大frameL1 .126764；相似性保护没有绕过。
- Single的256/512 Cutout慢速/快速/阶跃六项通过；最大frameL1 .253375，changedL1 .340854/.330108，保持.35门槛但余量有限。
- Single环境常量raw=(1.002,.501,.250)；HDR cubemap旋转0/90、tint/强度通过CPU半球积分3%门槛；非零F0宽GGX环境raw .027451、filtered .027151，对照 .027514通过。

报告 `rtdi-tempered92-{power,uniform}-candidate-cutout-rg.json`、`power-candidate-cutout-compat.json`、`uniform-baseline-cutout-rg.json`、`rtdi-tempered92-f0boundary-rg.json`、`rtdi-tempered92-single-motion-rg.json` 位于隔离工程；天空日志 `tempered92-environment.log`。首份运动报告proposal字段继承production标签，文件名、scoped shader与执行入口确认是候选；字段代码已修正，未手改原数值报告。Roslyn errors0、GUID唯一、Shader/diff通过。

## 取舍

本步不改Runtime。证据足以进入下一步窄生产接入审查，仍需默认Single完整primary/noise与实际Player验证、共享GI合同回归。平方根多一项算术，没有新增pass/资源/射线；GPU毫秒仍未证明，不称零成本。该改进只适用既有零F0 RGB提案类别，不宣称解决所有PBR镜面、多灯分布或天空过滤问题。古堡只作支持检查，最终NPR合成不纳入本步。
