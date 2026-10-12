# Ho-RTDI 混合镜面颜色与亮度moment

第65步在第55步的真实阶段副本上统计逐像素时间方差，检查RGB噪声是否被亮度moment漏掉。数据不支持在这个混合天空case中直接替换为RGB covariance；更值得核对的是raw moment到积累后不确定度的单位一致性。

## 统计范围与单位

`RunAuxiliaryEnvironmentMomentsBaseline`与`RunAuxiliaryEnvironmentMoments/Compatibility`保持128²正交、中心1024点、192预热/512观察、独立CPU参考/同帧种子。每点分别累计RGB和Rec.709亮度的一、二阶统计后求时间方差，再对点平均；没有把空间照明梯度算入噪声。RGB variance为三通道平均，与luminance variance不是同一投影/权重，不能只据数值比值推完整协方差。

物理scale/材质因子沿既有uniform ortho恢复，阶段4/6 alpha读取`max(m2-L(current)²,0)`，9为传播的scalar variance，10是history length，明确notVariance。moment spread描述原采样统计，未直接称为积累RGB输出的实际方差。

## 三样本mixed实际结果

| 复用 / 阶段 | RGB时间方差 | 亮度时间方差 | alpha spread / scalar variance |
| --- | --- | --- | --- |
| none / raw | 6.987992e-4 | 1.032890e-3 | 不适用 |
| none / Temporal | 9.999171e-6 | 1.476984e-5 | 1.015876e-3 |
| none / 首Atrous | 2.428694e-6 | 3.623414e-6 | 2.147080e-5 |
| none / 发布 | 1.497149e-7 | 2.082716e-7 | 不适用 |
| T/S / raw | 7.350107e-4 | 1.087987e-3 | 不适用 |
| T/S / Temporal | 1.101339e-5 | 1.628919e-5 | 1.066961e-3 |
| T/S / 首Atrous | 2.878387e-6 | 4.294372e-6 | 2.257105e-5 |
| T/S / 发布 | 3.042695e-7 | 4.336287e-7 | 不适用 |

本输入的实际亮度方差并不很低，不能沿第41步某些零F0红/蓝等亮信号结论断言这组镜面颜色波动不可见。原单样本类似：Temporal亮度方差约1.46～1.61e-5，alpha spread约1.005～1.056e-3。

首Atrous scalar variance比实测积累输出亮度方差约大5～6倍。它并不自动证明错误：moment来自raw、滤波样本相关/量化、fast colour接纳及有效样本数模型都影响关系。但当前first range已除specularVarianceSamples，而传播表达是否处于相同不确定度域，需要先核对再增加统计资源。

## 复查与下一项

报告`rtdi-auxenvmoments65-baseline/partition-rg/compat.json`，partition两条24记录完全一致。stage/RGB比例与第55步一致，原阶段入口回归数值保持；diagnosticOnly=true、退出0不替代正式mixed质量失败。Roslyn errors0、无新增DI Shader诊断/diff whitespace通过，Runtime/native/GI/Player未改。

下一项核对首Atrous range和variance传播对raw second moment的有效样本换算是否一致，保留Temporal/Clamping统计语义与真实colour/高光参考；若有修复候选，须同时过mixed局部形状、动态噪声、旧core/彩灯/环境/Player门槛。不先扩大RGB covariance buffer或盲调滤波带宽。

第66步[预算核对](Ho-RTDI-SpecularNormalBudgetRejection.md)确认range与传播已共用换算后的sampleSpecularVar；改用normal预算使动态噪声退化，拒绝全局推广。
