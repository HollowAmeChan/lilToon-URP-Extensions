# Ho-RTDI 彩灯噪声与复用对照

第38步审计第37步的噪声取舍。最终只增加Editor测试；方差上界与RGB重要性实验均撤回，运行时仍为第37步。新测试有一组能量失败，正式入口保留exit1，不把诊断交付写成质量修复。

## 对照与判定

`HoRTDIValidation.RunReuseNoise` / `RunReuseNoiseCompatibility` 在隔离工程D3D12运行。64²正交白色漫反射Quad、红蓝点灯同位置/左右分离；初始候选1/8各测试none、temporal、spatial、temporalSpatial，共16组。每组清空DI/NRD历史、DI随机帧种子归零，192帧预热、256帧观察central16×16。使用既有独立CPU交点/Lambert/点灯衰减参考，检查RELAX实际调度与时间复用状态，不读滤波结果构造参考。

RGB MSE为红蓝两个通道误差平方的均值；另记录Rec.709亮度MSE，防止亮度稳定掩盖颜色噪声。相关系数对每像素减去其时间均值，再计算水平/垂直相邻像素及同像素lag1的汇总归一化协方差。此处报告红通道，样本数有限，不代表任意距离、时域或场景的相关性。

原raw各通道3%、filtered各通道5%、filtered MSE<raw×.3不变。JSON写入隔离工程`rtdi-reuse38-rg/compat.json`。上一轮单项noise使用32帧/单通道，本表用256帧/双通道，不能把两种指标直接算倍率。

## 生产基线结果

两条路径16组数值一致，15组通过，以下为初始8候选的部分结果：

| 灯位置 / 复用 | raw RGB MSE | filtered RGB MSE | raw亮度MSE | filtered红 / 蓝 |
| --- | --- | --- | --- | --- |
| 同位置 / none | .083336 | .004429 | 小于5e-9（日志八位为0） | .993981 / 1.007712 |
| 同位置 / temporalSpatial | .093934 | .001322 | .00035326 | .993338 / .977530 |
| 分离 / none | .077567 | .002763 | 小于5e-9（日志八位为0） | .991039 / 1.017046 |
| 分离 / spatial | .084631 | .000782 | .00025635 | .972318 / 1.051984，失败 |
| 分离 / temporalSpatial | .084666 | .000935 | .00029723 | .976774 / 1.032854 |

raw邻域相关系数绝对值约不超过.0054、lag1不超过.01；filtered邻域约.45～.68、lag1约.45～.64。滤波自身产生相关性是预期现象；本对照不支持将噪声退步直接归因于raw邻域相关。8候选/无复用时亮度误差很小而RGB误差显著，说明亮度moment不能完整描述该彩灯信号的颜色误差。分离/仅spatial蓝1.051984是有限观察窗口的确定失败，尚不能由此证明无限帧渐近偏差。

## 已撤回的候选

在既有非负过滤系数下，若将输入方差视为给定值，Cauchy–Schwarz给出相关噪声上界`(Σw√v)²`；它不是从当前数据测出的协方差，也不保证moment估计是真实物理方差上界。

- 首轮与后续均按此上界传播：旧32帧noise MSE .001333→.000170，但主表面static/smooth/step蓝1.093062/1.115940/1.124730，完整入口exit1。
- 仅首轮上界：noise .000416，主表面smooth/step蓝1.054146/1.058485，仍失败。
- 在第37步滤波上改统一RAB目标为非负RGB向量长度，保持Shade/物理光强和SDK归一化：新同位置8候选none filtered RGB MSE .004429→.000163，但分离灯none/temporal/spatial/组合蓝1.062703/1.056772/1.070385/1.065104，完整主表面static/smooth蓝1.054504/1.050730，失败。与第36步不同滤波基线的RGB实验分开记录，未采用。

没有通过扩大phi/history、调光强、放宽门槛或关闭某条生产路径使候选通过。

## 参考、复查与下一项

核对用户参考工程`UnityPathTracing-6000.3.15`的`RAB_LightSampling.hlsl`、NRD `NrdSettings.cs`，以及pinned RTXDI InitialSampling/Reservoir目标与逆PDF归一化、NRD moment/RELAX空间传播。官方[GRIS研究](https://research.nvidia.com/labs/rtr/publication/lin2022generalized/)说明复用会引入相关性，[MCMC去相关研究](https://research.nvidia.com/labs/rtr/publication/sawhney2022decorrelating/)讨论复用相关伪影；它们不是本步候选已正确的证明，也未引入MCMC或PT。额外RGB论文PDF无法读取，未用其正文支持实现。

恢复运行时后两条新入口均实际exit1且保存false；第37步完整主表面入口重新qualityPass=true。Roslyn errors0、原始SHA/派生check通过，git运行时代码diff为空。本步只有Editor/文档，不重建相同Player或重复无共享变更的GI。

下一项应核对颜色误差与真实空间彩灯渐变的区分，继续检查首轮/后续权重对称性；任何采样或滤波修复必须同时通过本16组、原主表面、Cutout动态/镜面和实际Player门槛。先处理确定失败，不增加通用PT资源或借古堡天空球调参数。GPU预算和真实motion时序仍未证明。
