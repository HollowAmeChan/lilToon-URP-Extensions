# Ho-RTDI：可控100:1灯变化的能量收敛

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第89步不依赖古堡几何。补两个实际 Runtime 长窗口入口，仅将既有可控动态 fixture 的 powerTemperature 阶段延长到2048帧，其它阶段仍96帧；原96帧正式门槛、报告与失败保持。使用生产 triple analytic，不调用第88步隔离候选，也不修改 Runtime 或 NRD。

核对用户 fork `Assets/Shaders/RTXDI/DI/TemporalResampling.hlsl` 和本地固定 SDK：当前/历史 reservoir 重估当前表面 target、映射灯身份、按历史 M 与对应 target 完成归一化。长窗口是否改善不能代替对具体贡献与时间相关性的检查。

实际128² Cutout fixture 三彩灯，红 .4、蓝40，绿3000K；固定256点、跳过每阶段前16帧。GPU reservoir/compact sidecar 与独立CPU解析辐射分解使用相同观察点。输入无限定天空壳或复杂合并模型因素。

| 观察 | raw red比解析值 | filtered red | filtered green | primary red | auxiliary red |
| --- | --- | --- | --- | --- | --- |
| 生产96帧 | 1.116960 | 1.086679 | .951847 | 1.263596 | .971107 |
| 88候选96帧 | 1.089986 | 1.084586 | .963501 | 1.263596 | .917111 |
| 生产2048帧 | 1.012215 | 1.001083 | .964248 | 1.018977 | 1.006180 |

两路径2048观察报告一致，全部九阶段按原 raw3%/filtered5%/dark .001 检查通过、实际进程exit0。powerTemperature重置0、confidence1，实际packed分解最大相对误差 .00097275，小于 .003。未新增shader warning/error，Roslyn errors0。报告 `rtdi-runtime78-dynamic-long-cutout-rg/compat.json`，日志 `convergence89-rg/compat.log`，位于隔离工程。

这说明该固定样本序列长窗口更接近解析值，不能证明任意动态无偏或96帧可用。短窗口偏高主要来自 primary；第88步辅助候选没有改变这个 primary 序列，其总输出较低也包含辅助弱红灯的不足采样，不据总误差下降单独接纳。长观察改变后续阶段的 frame index，因此其余阶段通过不是原96帧全部随机序列的重放。

下一项应在相同可控灯变化分别关闭时间/空间复用，比较主样本方差、分块时间均值与有效能量，判断是否存在较长相关性。不能直接按100:1阈值清历史、提高默认样本、放宽96帧门槛或将2048帧结果冒充实时收敛。古堡仍只用于复杂场景支持检查。
