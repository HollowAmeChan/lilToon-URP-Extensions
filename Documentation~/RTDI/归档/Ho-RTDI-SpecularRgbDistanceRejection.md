# Ho-RTDI 镜面RGB range距离对照拒绝

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第64步将已用于diffuse的RGB距离形式只应用到隔离镜面后续Atrous，减少不同颜色变化在亮度中的抵消。局部高光搬移改善，但动态噪声明显退化，未接纳生产。

## 唯一改动与参考

核对固定NRD及用户fork `RELAX_Atrous.cs.hlsl`的`abs(L(center)-L(sample))`，当前DI diffuse使用`L(abs(centerRGB-sampleRGB))`。对照specular采用后者，保持Rec.709单位、pair-min variance带宽、phi、相对差上限、confidence relaxation、几何/normal/F0/roughness和原row normalization；没有改moment或增加样本来掩盖它。

`RunRgbSpecularPerspectiveEnvironment/Compatibility`沿第62步透视逐像素参考；`RunRgbSpecularMotion/Compatibility`沿原64动态镜面。Shader数组index10临时替换并恢复；生产源码、native、GI与Player未变。

## 两条结果

两条完全一致，透视mixed中心搬移改善但仍失败，整体2/4、退出1：

| 复用 | 原filtered spec红 / 蓝 | RGB distance红 / 蓝 |
| --- | --- | --- |
| none | 1.078953 / 1.161082 | 1.017407 / 1.064578 |
| T/S | 1.089771 / 1.181745 | 1.015567 / 1.079489 |

RGB表达减少亮度抵消后，中心蓝偏差约16～18%降到6～8%；只说明该项参与空间高光搬移，未完全修复，也不代表variance统计已变成RGB covariance。

动态两条各仅slow通过，rapid/step失败，qualityPass=false、退出1：

| 模式 | 原frameL1 | RGB distance |
| --- | --- | --- |
| slow | .110073 | .110940 |
| rapid | .343487 | .382012 |
| step | .347752 | .401667 |

更强颜色拒绝同时减弱邻域降噪，不能以高光保持单项收益替代动态门槛。未调整.35/5%、phi、曝光或光强。

## 复查与下一项

报告`rtdi-auxenv54-partition-exchange-perspective-rgbdistance-rg/compat.json`（exchange命名是已有隔离入口标签，实际保留原row normalization），动态为`rtdi-rgbspec64-motion-rg/compat.json`。恢复原透视2/4、动态3/3及数值保持；Roslyn errors0、无新增DI Shader诊断/diff whitespace通过。没有生产候选需回滚，也不构建失败候选Player。

下一项应将高光梯度保留和噪声估计一并核对，避免只收紧range权重或只改归一化；可检查当前luminance moment对多色镜面输入的覆盖及scene分布的实际方差，再决定是否有值得接入的guide/统计调整。三样本/opaque/动态CDF/产品部署/GPU预算仍待完成，继续只做DI。

第65步[真实moment/时间方差](Ho-RTDI-MixedSpecularMomentAudit.md)没有显示该mixed输入的亮度波动被漏掉；先核对raw moment到积累信号不确定度的换算一致性。
