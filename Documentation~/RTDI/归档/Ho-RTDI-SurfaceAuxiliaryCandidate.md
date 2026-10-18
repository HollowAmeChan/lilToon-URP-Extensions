# Ho-RTDI：表面响应辅助 RIS 隔离候选

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第88步只添加 Editor 隔离实验，没有修改 Runtime、默认预算、NRD 参数或最终材质合成。古堡包含合并模型和天空壳，继续仅作接入兼容性及有限时间稳定性观察；不作为解析能量、无漏光或普适降噪验收。

## 依据与契约

核对用户参考工程 `UnityPathTracing-6000.3.15/Assets/Shaders/RTXDI/DI/Reservoir.hlsl` 的 `RTXDI_StreamSample/RTXDI_FinalizeResampling`，以及 `InitialSampling.hlsl` 的表面 target 与源 PDF。保留现有混合功率 CDF，每个辅助最终样本从四个 fresh 候选按当前表面 BRDF target 选择；compute/native 使用相同镜面提案系数。最终权重为 `sum(target_i / pdf_i) / (4 * selectedTarget)`，不是 `1 / selectedPdf`。

两个辅助样本仍各最多一条最终可见性射线，没有辅助历史或空间复用，8-byte compact sidecar 保持。每像素辅助 target 评估从两次变成八次，不能据射线数不变宣称成本不变。没有有效 GPU 毫秒证据。

候选仅在 `.codex-research` 工程生成 shader，scoped override 恢复 Feature 原引用。四个候选 target 全零时返回空 reservoir，native 明确跳过求交；compute 已有有效性分支。依赖 header 同步导入，内容不变不重复写入，消除先前隔离实验的 SourceAssetDB 修改时间错误。

## 实测与限制

| 检查 | 结果 |
| --- | --- |
| 古堡 512²，64 warm + 64 observe | 两路径 exit0，reset0；raw 方差 .00144223，filtered .0000422212 |
| 对比现有 triple | raw 方差下降40.29%，filtered下降36.26%；特定视点观察，不是普适验收 |
| 独立256²灯域，Spot快速变化/Spot阶跃/32弱灯/HDR | 两路径四项全部通过，原 raw3%、filtered5%、L1门槛保留 |
| RG天空/局部灯独立能量 | 3/4通过；mixed无复用 filtered specular red=1.050715，仍超5%，exit1 |
| RG动态九阶段，真实Cutout | 8/9通过；100:1 powerTemperature raw red=1.089986、filtered red=1.084586，exit1 |

灯域 frameL1 为 .331145/.210258/.314291/.317238，对比现有 triple .324357/.202892/.311013/.308588，并非全面改善。动态其余阶段、真实身份映射、CPU遮挡与实际shade分解通过；关闭红/蓝通道读到零，但不将有限样本等同任意动态质量保证。

GPU probe 检查有效 light identity、有限非负 finalized weight、第一辅助 M=4、compact visibility 和 CPU 独立 alpha 遮挡。compact 不存 M/target，未从该读回重建四个候选权重和；归一化正确性由参考契约及独立解析能量结果支持，不把 metadata 检查表述为逐候选逆 PDF 证明。

报告位于隔离工程：`rtdi-surfaceris88-castle-rg/compat.json`、`rtdi-surfaceris88-lightdomain-rg/compat.json`、`rtdi-runtime78-env-baseline-surfaceris88-rg.json`、`rtdi-surfaceris88-dynamic-cutout-rg.json`。guard/import 修正后重跑古堡RG、灯域compat、动态RG，无新 shader warning/error 或 SourceAssetDB 时间错误。Roslyn errors0。

## 取舍

不生产化。古堡视点的改善不足以抵消可控灯域未改善、已有正式能量失败和成本未测。下一步应优先分解可控100:1灯变化中的 primary/auxiliary 贡献与收敛，判断是有限窗口采样还是系统误差；保留原门槛，再决定是否需要调整提案。古堡继续验证合并 submesh 和天空排除，不据它扩展通用 PT、GI 或最终 NPR 合成。
