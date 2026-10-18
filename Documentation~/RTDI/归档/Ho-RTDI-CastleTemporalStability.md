# Ho-RTDI：静止古堡视点的时间波动

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第87步扩展第86步实际帧审计，区分固定材质纹理与随机采样波动。没有修改生产配置默认值或shader；复杂场景只测有限采样的稳定性，不作为解析能量/无漏光基准。

同一512²源视点，64帧预热、64观察帧，按隔离固定60Hz sample time驱动NRD。每4像素取一个固定点（16,384点），逐点每RGB通道Welford时间方差，再跨点平均；不将空间石纹/亮度梯度混入噪声。实际raw/filtered同时读取，报告mean和历史reset/temporal/gradient调度。

三样本RG/compat两入口和single RG对照均实际exit0、valid receiver与有限非负输出检查通过。两路径三样本数据相近，float末位差异保留。

| 预算 | raw时间方差 | filtered时间方差 | raw平均RGB | filtered平均RGB |
| --- | --- | --- | --- | --- |
| single | .00181607 | .0000589784 | .0147895 | .0140662 |
| triple | .00241545 | .0000662423 | .0147987 | .0139958 |

观察64帧全部temporal复用，NRD reset0、native gradient0，符合静止输入；并非每帧清历史造成预览不收敛。三样本filtered相对raw方差降低97.26%，single也降低约96.75%。这些是真实时间统计，仍不代表GPU耗时。

这个视点里triple raw方差比single高33.00%，filtered高12.32%。不能拿第77步动态caster收益宣称所有静态场景都更稳定；也不能从64样本或全图mean证明精确无偏，filtered略暗仍须独立shape/能量检查。source scene由不同贡献/遮挡/BRDF组成，global auxiliary RGB-power proxy不等于每个表面最好的提案。

记录`rtdi-castlestability87-triple-rg/compat.json/png`和`rtdi-castlestability87-single-rg.json/png`。预览是最后观察帧、同Reinhard/gamma显示映射，不是时间平均或最终NPR结果。原16帧预览/报告仍保留。Roslyn errors0、diff whitespace通过，无新增DI shader诊断，源资产未保存/修改。

下一项应在现有DI范围核对辅助提案与表面响应的匹配，并用相同完整场景/独立动态fixture衡量收益与成本；在证明前保持single默认、triple可选，不按这个单视点盲目扩大样本数或修改NRD参数。GPU预算仍未证明，其它有效质量失败和最终合成边界保持。
