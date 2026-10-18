# Ho-RTDI 快速镜面置信度与历史审计

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第48步针对第47步快速镜面失败，读取实际GPU置信度/历史，并做跳过动态可见性置信度的Editor对照。没有接受关闭置信度或其他生产改动。

## 参考与读取范围

核对用户fixed FullSample的`RenderingPlugin/External/RTXDI/Samples/FullSample/Shaders/DenoisingPasses/ConfidencePass.hlsl`：梯度经过darkness bias、sensitivity映射，再可选进行短时间滤波。当前HoRTDI从1/3尺寸梯度写R8镜面confidence，RELAX以线性采样读取；本步按当前实际归一化UV与Clamp读取，不把低分辨率像素直接当完整receiver像素。

本地`HoRTDINrdTemporal.hlsl`中，镜面confidence乘到最大普通/快速累积帧数；后续specHistoryFrames还受公共historyLength与重投影限制。因此公共历史计数不等于真正使用的镜面历史权重。`NRD`末Atrous alpha写`max(historyLength-1,0)`，本步加1还原公共长度，未把alpha当方差。另在Temporal dispatch后立即复制`gOut_SpecReprojectionConfidence`，避开后续transient资源复用；值表示几何重投影置信度，不能当实际累积alpha。

`RunCutoutResolutionSpecularConfidence/Compatibility`复用256²、3584点、8候选、均匀灰F0/roughness、192预热/256观察与固定60Hz；CPU GGX及UV/alpha遮挡参考独立。逐帧核对阶段捕获、镜面物理尺度、Far无遮挡、有限confidence/历史及raw红/蓝3%。记录所有点与当帧红灯遮挡状态变化点的统计。step另保留真实变化像素最初三帧误差<.35，不能只评整体frameL1。

`rapidConfidenceOff/stepConfidenceOff`只使用已有`ValidationSkipVisibilityConfidence`对照：在本固定平面/灯光条件下，动态可见性射线关闭，实际读取的confidence=1；最终Shade可见性仍启用，ReSTIR T/S和RELAX保持运行。这不代表任意场景关闭后confidence都等于1。报告与第47步分开，写`rtdi-specularconfidence48-rg/compat.json`。

## 两条实际结果

所有配置的公共历史长度平均31、几何重投影confidence平均1；普通镜面最大配置30帧。两条数值一致。

| 配置 | 平均confidence | confidence<.25比例 | 当帧变化点平均confidence | frameL1 | step变化像素L1 | 最终质量 |
| --- | --- | --- | --- | --- | --- | --- |
| slowTS | .988176 | .006096 | .778687 | .077365 | — | 通过 |
| rapidTS | .852241 | .116107 | .339311 | .507913 | — | 失败 |
| rapidConfidenceOff | 1 | 0 | 1 | .251653 | — | 仅本快速配置通过 |
| stepTS | .964333 | .033263 | .101288 | .357448 | .244762 | 整体噪声失败 |
| stepConfidenceOff | 1 | 0 | 1 | .181519 | .605561 | 变化像素残影失败 |

rapid当帧变化点86080个观测，约53.99%的confidence<.25；step为30720个，约84.30%。未变化点平均confidence分别.905347/.994231。这里的“未变化”只表示与上帧CPU遮挡状态相同，不证明空间邻域或更早历史完全稳定。

关闭动态置信度令rapid RGB MSE从9.922330e-7降到2.599619e-7；step从7.081543e-7降到1.738889e-7，但局部短期残影超过原门槛。正式默认数值与第46/47步一致。实际对照支持置信度影响噪声/响应权衡，不能由此断言唯一根因或把均值改善当完整修复。

## 复查与下一项

诊断报告明确`diagnosticOnly=true`，退出0仅表示读取/参考检查有效。每阶段都累计step变化像素L1，质量标志同时考虑能量、整体frameL1与step短期残影；关闭置信度的step不会因整体均值较低而标绿。生产Runtime/Shader/native/GI/Player未变；保留正式质量失败。

Roslyn Runtime/Editor errors0、GUID唯一、diff whitespace通过。最终两条D3D12诊断各30个阶段结果与5个置信度结果完全一致；原第47步三配置阶段入口回归通过，数值不变。没有新增DI Shader错误，不重建未变的Player/GI。

下一项优先审视非零F0下的原始彩灯采样方差与提案，核对用户RAB目标回调和固定RTXDI归一化，使用隔离shader对照检验能否减少输入噪声，同时保留动态可见性置信度与阶跃门槛。历史上全表面RGB目标曾引入其他质量失败，不能直接推广零F0策略；若重新评估，必须与当前运行时同种子比较，并通过大尺寸镜面、core/彩灯、primary与Player回归才考虑接纳。古堡合并天空模型只作资产支持审计，不扩张合成/PT或把诊断复制当GPU预算。

第49步[隔离RGB提案对照](Ho-RTDI-SpecularRgbProposalAudit.md)降低了原始总RGB MSE，但rapid仍失败，step短期红通道误差从raw阶段就变差；没有接纳到生产。
