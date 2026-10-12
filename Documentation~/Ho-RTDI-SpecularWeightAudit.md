# Ho-RTDI 稳定/动态镜面滤波接纳量

第59步保留原镜面过滤，只在隔离pipeline10统计真实sumW与confidence，解释第58步固定交换为何削弱动态降噪。不改生产算法，不以计数当GPU时间。

## 实际GPU读取

`RunLateSpecularWeightEnvironment`沿原运行时sky-only/mixed none/T/S四组；`RunLateSpecularWeightMotion/Compatibility`沿64 slow/rapid/step三组。派生副本仍使用原row normalization，额外8KiB uint histogram：step2/4/8/16各64个weight bins×8个confidence bins，按实际像素线性采样的spec confidence联合计数。

ROI归一化x[.25,.6875)、y[.25,.75)，与动态CPU评分范围相同；128环境为3584点、512观察，每轮1,835,008次，64动态为896点、256观察，每轮229,376次。同步预热结束再清零，严格核对每轮ROI计数，不读alpha推断权重。统计原子开销仅在这些入口，不能用于性能比较。

报告保留2048个精确整数计数，mean为bin中点近似：weight精度1/64、confidence精度1/8，confidence=1也落在最后bin，不能把meanConfidenceBin当精确平均confidence。sumW≥1归最后bin；本probe不证明权重严格对称或全图守恒。

## 分布差异

| 配置 | step2 meanW | step4 | step8 | step16 | step16 W<.25比例 | confidence<.25比例 |
| --- | --- | --- | --- | --- | --- | --- |
| mixed none | .7082 | .6360 | .5287 | .3882 | .0561 | 0 |
| mixed T/S | .7084 | .6315 | .5089 | .3553 | .0800 | 0 |
| caster slow | .4581 | .2945 | .2227 | .2021 | — | .00498 |
| caster rapid | .4077 | .2929 | .2272 | .2015 | .9781 | .11928 |
| caster step | .3610 | .2666 | .2223 | .2024 | .9683 | .03708 |

中心核固定为.44198²≈.19535。动态末轮sumW约.20，绝大多数观测只接纳很少邻居；原归一化放大这部分邻居贡献，仍提供降噪。全局固定交换把大量质量留回中心，削弱该效果。稳定混合环境接纳量更高，confidence始终高，行为不同。sky-only meanW与mixed接近，不能仅凭有环境开关推断需要哪种过滤。

统计没有改变结果：motion两条全计数与派生统计完全一致，原qualityPass=true，rapid/step保持.343487/.347752；环境入口仍既有2/4、退出1，mixed镜面原偏差保持。诊断JSON为`rtdi-specweights59-<mode>-rg/compat.json`，正式输出另写weights后缀，不覆盖旧对照。

## 复查与下一项

Roslyn errors0、无新增DI Shader诊断、GUID唯一/diff whitespace通过，Runtime/Shader/native/GI/Player未改。三条真实入口结束；不重复未变Player构建。

下一项评估与实际邻居接纳量关联的连续系数：低接纳时保留当前归一化降噪，较高接纳时减少不必要的质量放大，variance按最终系数平方传播。必要时结合实际confidence，但不能仅按环境开关分类或由bin均值推出普适阈值。候选仍须同时通过既有混合能量、动态镜面/三样本、边界、HDR/环境与Player门槛，不调整门槛压线。

第60步[连续策略未解决能量偏差](Ho-RTDI-AdaptiveSpecularExchangeRejection.md)：动态保持通过，mixed红/蓝仍越5%，不接纳生产，也不继续盲调曲线。
