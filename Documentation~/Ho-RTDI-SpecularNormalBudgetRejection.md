# Ho-RTDI 镜面normal预算方差对照拒绝

第66步核对首Atrous的moment换算，并将保守fast预算改为normal预算作隔离对照。确认没有“range已换算、传播漏换算”的实现错误；normal预算改善部分高光搬移却破坏动态噪声，未进入生产。

## 一致性核对

固定Temporal根据history/confidence计算normal与responsive alpha，Clamping可能采用fast colour并进行acceleration/reset。现有DI first Atrous的`sampleSpecularVar`已经为`max(m2-L²,0)/specularVarianceSamples`，同一变量用于pair range及`sum(w²×variance)`传播，所以两者在同一个不确定度域。不能依据第65步统计比值在传播处再除一次预算。

目前varianceSamples取min(实际historyLength,fast最大+1)，是可能采用fast colour时的保守预算。`RunNormalBudgetPerspectiveEnvironment/Compatibility`与`RunNormalBudgetSpecularMotion/Compatibility`仅将该上限改为normal最大+1，其他权重、moment、当前raw、NRD参数/历史、可见性、迭代和归一化保持。它是偏乐观的对照，不是已证明的有效独立样本数；输入相关、EMA与clamping都使真实统计复杂。

## 两条实际结果

两条完全一致。透视sky-only两组通过，mixed两组仍失败，整体2/4、qualityPass=false、退出1：

| mixed复用 | 原filtered spec红 / 蓝 | normal预算 |
| --- | --- | --- |
| none | 1.078953 / 1.161082 | 1.081405 / 1.089789 |
| T/S | 1.089771 / 1.181745 | 1.078735 / 1.102189 |

中心蓝搬移减少，但红/蓝仍5%外。动态两条各只有slow通过，rapid/step失败：

| 模式 | 原frameL1 | normal预算 |
| --- | --- | --- |
| slow | .110073 | .127777 |
| rapid | .343487 | .384149 |
| step | .347752 | .398122 |

低估可能采用fast colour的噪声会收紧range、减少邻域降噪。不能据静态Temporal实际方差较小，就把normal预算推广到所有帧/像素；也没有修改fast历史本身来制造通过。

## 复查与下一项

JSON `rtdi-auxenv54-partition-exchange-perspective-normalbudget-rg/compat.json`、`rtdi-normalbudget66-motion-rg/compat.json`独立。原透视2/4、动态3/3恢复原数值；Roslyn errors0、无新增DI Shader诊断/diff whitespace通过，Runtime/Shader/native/GI/Player一直未改，未构建失败候选Player。

下一项应核对实际Clamping色彩变更/响应历史对有效不确定度的影响，或者评估现有DI采样/guide的实际接入收益，避免继续试固定预算或系数。非负修复仍有效，中心高光/动态噪声门槛保持；三样本、opaque、动态CDF、多相机/产品部署与GPU预算仍未完成，目标不收缩到本测试。
