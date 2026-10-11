# Ho-RTDI 真实双灯局部质量审计

第29步扩展独立 Cutout 平面测试：Near 为红色点灯、Far 为蓝色点灯，两者强度4、位置(±1.5,0,-1)，均有实际贡献。Near 有几何边缘和中央alpha洞，Far在接收ROI内始终可见。与前步0通量transport控制或单灯退化不同，此处实际执行完整ReSTIR/RELAX。

CPU参考按接收平面像素中心、实际灯位置、range与Lambert求能量，用独立射线/平面+纹理采样判断Near遮挡；红/蓝分别检验，避免颜色误差在总亮度中抵消。64²、ROI896像素，192帧预热，静态512帧、相机/骨骼caster运动256帧。无/T/S/TS及全透光控制分别重置history；像素均值CSV和整体JSON保存在隔离工程。

## 第29步结果：质量门槛未通过

后续第30步的分阶段对照和条件滤波改善见 [Cutout漫反射滤波](Ho-RTDI-CutoutFiltering.md)：原八组中七组通过，移动caster的逐帧噪声仍失败；以下保留原配置失败基线。

两条路径结果一致，全部 raw 分通道能量在原3%条件内。下表是filtered/解析参考的能量比例，L1按红蓝参考总能量归一化；meanL1比较各像素的观察期均值，frameL1还包含逐帧噪声。

| 模式 | filtered红 | filtered蓝 | mean L1 | frame L1 |
| --- | ---: | ---: | ---: | ---: |
| 静态、无复用 | 1.143309 | 1.028107 | .129259 | .143761 |
| 相机运动、TS | 1.029124 | 1.063002 | .140868 | .215682 |
| caster运动、TS | .999657 | .998998 | .064867 | 1.060215 |
| 静态T | 1.117234 | 1.042867 | .147411 | .168879 |
| 静态S | 1.051494 | 1.052561 | .187941 | .201275 |
| 静态TS | 1.046648 | 1.047247 | .190498 | .210399 |
| 全透光、无复用 | .987806 | 1.018049 | .045234 | .062505 |
| 全透光、TS | .961741 | 1.063502 | .082937 | .097267 |

质量条件固定为分通道filtered能量5%、meanL1<15%、frameL1<35%。除全透光无复用控制外均有失败；`qualityPass=false` 是正式结果，不把 raw 能量通过当成完整质量通过。`RunCutoutMultiQuality` 保存全部数据后失败退出，已实际运行确认exit1；`RunCutoutMultiAudit` 只收集诊断、exit0也会保留JSON中的false。兼容入口同理。

## 本步判断与下一项

静态Near的+14%与局部形状损失需要修正。全透光TS仍有蓝色约+6.35%，说明问题不只来自alpha anyhit；这只是控制证据，不是NRD参数/输入错误的归因。caster运动时世界Revision每帧变化，当前策略清全部DI/NRD历史，frameL1高而均值能量接近参考；与代码契约一致，仍不能宣称动态遮挡物已有稳定降噪。

已核对用户 `Assets/RTXDI/Shaders/DenoisingPasses/ComputeGradients/ConfidencePass`、本地 HoRTDIConfidence 与 pinned `NRDSettings.h` 的history/edge设置。现有静态相同灯参数shortcut、front-end guides、历史clamping/spatial滤波及几何变动策略都应分开对照，不能凭此数据更改亮度、拉长history或调整phi就宣布修复。NRD官方信号/运动契约见 [集成README](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)。

本步只增加质量报告/硬gate，没有生产算法、资源、Shader、native ABI或材质合成变化，也没有修改古堡源工程。Roslyn Runtime/Editor通过；两条硬件诊断验证与正式失败gate都保留，不重做无关Player构建。下一步先用本报告的全透光/阴影与静态/运动控制定位能量和形状变化，再选择有证据的输入/调度修复。GPU成本仍另待有效capture/计时。

入口 `HoRTDIValidation.RunCutoutMultiQuality/RunCutoutMultiQualityCompatibility`、`RunCutoutMultiAudit/RunCutoutMultiAuditCompatibility`；日志及JSON/CSV前缀 `rtdi-cutout29-multi-*`。这是可复现的未达标基线，完整DI目标仍继续推进。
