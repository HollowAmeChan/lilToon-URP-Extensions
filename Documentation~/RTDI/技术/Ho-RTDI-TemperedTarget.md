# Ho-RTDI：零F0弱彩灯提案生产修复

> 状态：技术专题与接入证据。步骤中的测试数值及“下一项”保留当时语境；当前默认值、支持范围与剩余问题以 [Ho-RTDI 总文档](../../Ho-RTDI.md) 为入口。

第93步将第92步隔离验证的平方根RGB重要性接入共用ApplicationBridge：只在 `HoDIUsesRgbImportance` 成立（DI启用且量化F0全零）时改变选样target。实际Shade、BRDF、Fresnel、source PDF、SDK、采样数、历史预算、默认Single、辅助1/PDF、NRD和材质类别保护不改；没有新增设置/pass/资源/射线。

弱彩灯在既有96帧100:1变化里，主reservoir贡献偏高且方差大。目标由完整RGB贡献长度改为其平方根，使弱贡献得到更多相对选样权重；初始、时间、空间、偏差修正仍用同一目标。非零F0亮度分支保持。零贡献保持零，真实输出由最终权重和原radiance/BRDF求值，没有对画面做gamma或tone-map。

沿用户fork target/SDK合同生产化；测试生成器增加明确marker及exact-expression守卫，识别已生产化的目标，不重复开平方，也不接受其它未评审表达式。GI明确 `HO_RTDI_RGB_IMPORTANCE_ENABLED=0`，仍走原亮度分支；实际共享GI合同回归通过，未改GI功能。

## 接入后验收

- 默认Single RenderGraph的16组reuse/noise通过、完整primary motion质量通过，实际exit0。
- 生产Triple实际动态九阶段全部通过：100:1 weak red raw1.014597、filtered .999766；原3%/5%/dark .001门槛不变，实际shader结果与第92步候选匹配。
- 两个Win64 Mono release构建成功；实际Single RG与Triple compatibility Player全部probe pass/exit0，D3D11 inactive pass/exit0。Single/Multi filtered MSE .000526306/.000219972，HDR相对误差 .010916/.010861，材料边界、深度隔离、彩色镜面、环境和初始可见性门槛保持。
- 共享GI RenderGraph实际五阶段、secondary analytic/sky/emissive能量、world遮挡、temporal/spatial/history通过。没有把GI文件间接引用等同实际运行。

第92步额外Single32项256/512 F0边界、六项运动、HDR旋转天空是同表达式的隔离证据；第93步没有宣称全部再跑一遍。运动changedL1余量仍较小。未做IL2CPP或四个Player预算/路径组合全面重建，也不将通过的两个组合扩称全平台验收。

日志前缀 `tempered93-`；实际Player报告 `rtdi-tempered93-player-{single,multi-compat,dx11}.json`，位于隔离工程。Roslyn errors0、diff通过，DI/GI无新增shader warning/error。release日志仍有SolarHD/Shadows除零和lilToon重复keyword警告，在第84步原构建日志也存在，本步没有混入这些其它模块修改。

## 剩余限制与下一项

弱红primary相对时间方差在隔离固定序列165.87→15.01；该结果不代表任意材质/视点/灯域普适改善。GPU毫秒仍未证明，平方根额外算术不称零成本。保留混合天空/局部灯的非零F0镜面filtered能量失败、最终NPR/GI接收边界；古堡仍只作特殊合并几何支持审计。

下一步回到已知混合天空镜面filtered偏高的可控失败：raw能量已接近解析值，应先定位真实NRD阶段与信号合同，核对用户NRD参考及固定vendor实现，不按失败样例直接改输出或放宽5%门槛。


## 第92步候选对照（历史证据）

> 此节保留正式接入前的候选/评审证据；其中“下一步”“未改 Runtime”仅描述当时状态。当前状态见本页开头和 RTDI 总文档。

第92步只修改隔离compute的ApplicationBridge副本。核对用户参考工程 `RtxdiApplicationBridge/RAB_LightSampling.hlsl` 的表面target与固定SDK `DI/Reservoir.hlsl` 的StreamSample/FinalizeResampling：target用于选择及重用归一化，不等于实际着色值。候选将零F0分支从 `length(max(0, contribution))` 改为它的平方根，降低强弱贡献比例；源PDF、RIS预采样、初始8候选、历史4、空间4、最终预算、真实BRDF/radiance和NRD均保持。

目标非负、原有非零贡献仍有支持，零贡献仍为零。初始/时间/空间及偏差修正统一消费同一个目标回调。非零F0亮度分支保持；材质相似性仍拒绝跨提案类别的复用。native只作最终求交/实际shade梯度、不调用target，无需替换ray shader；两个fresh辅助样本的1/PDF提案保持。

### 实测

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

### 取舍

本步不改Runtime。证据足以进入下一步窄生产接入审查，仍需默认Single完整primary/noise与实际Player验证、共享GI合同回归。平方根多一项算术，没有新增pass/资源/射线；GPU毫秒仍未证明，不称零成本。该改进只适用既有零F0 RGB提案类别，不宣称解决所有PBR镜面、多灯分布或天空过滤问题。古堡只作支持检查，最终NPR合成不纳入本步。
