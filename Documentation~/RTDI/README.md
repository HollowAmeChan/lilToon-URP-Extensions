# RTDI 专题与证据目录

> 状态：现行导航，更新于2026-10-24。功能、使用方式与未完成项统一见 [Ho-RTDI 总文档](../Ho-RTDI.md)；GI/DI当前阶段和组合接入见[阶段收尾与UI整理](../架构/Ho-RT-GIDI-阶段收尾与UI整理.md)。此目录用于按需追溯详细合同和证据。

外层只保留总文档。技术专题含具体接口与接入时的验证数据；日期审计保留对应协议下的通过和失败；归档记录中的“当前”“下一项”仅属于当时步骤，不是现行任务队列。

## 技术专题

| 文档 | 内容 |
| --- | --- |
| [Ho-RTDI-Cutout](技术/Ho-RTDI-Cutout.md) | Ho-RTDI Cutout 最终可见性 |
| [Ho-RTDI-DiffuseConvexClamping](技术/Ho-RTDI-DiffuseConvexClamping.md) | Ho-RTDI：diffuse历史颜色凸裁剪 |
| [Ho-RTDI-DiffuseExchange](技术/Ho-RTDI-DiffuseExchange.md) | Ho-RTDI 漫反射方差传播与后续空间权重 |
| [Ho-RTDI-Environment](技术/Ho-RTDI-Environment.md) | Ho-RTDI 天空环境光 |
| [Ho-RTDI-LightDomainHistoryReset](技术/Ho-RTDI-LightDomainHistoryReset.md) | Ho-RTDI：灯集变化后的去噪历史失效 |
| [Ho-RTDI-MaterialExclusion](技术/Ho-RTDI-MaterialExclusion.md) | Ho-RTDI：按材质排除合并网格中的子网格 |
| [Ho-RTDI-NonnegativeDiffuseHistory](技术/Ho-RTDI-NonnegativeDiffuseHistory.md) | Ho-RTDI 漫反射历史的非负物理域 |
| [Ho-RTDI-NRD](技术/Ho-RTDI-NRD.md) | Ho-RTDI：NRD RELAX 技术专题与接入记录 |
| [Ho-RTDI-RgbContrast](技术/Ho-RTDI-RgbContrast.md) | Ho-RTDI 后续Atrous的RGB差判定 |
| [Ho-RTDI-RuntimeMultiSamples](技术/Ho-RTDI-RuntimeMultiSamples.md) | Ho-RTDI：可选三最终样本的Runtime接入 |
| [Ho-RTDI-SceneSupport](技术/Ho-RTDI-SceneSupport.md) | Ho-RTDI 场景支持：血月古堡 |
| [Ho-RTDI-SettingsSnapshot](技术/Ho-RTDI-SettingsSnapshot.md) | Ho-RTDI：dispatch参数的数值快照 |
| [Ho-RTDI-SingleDelta](技术/Ho-RTDI-SingleDelta.md) | Ho-RTDI 单个解析灯精确路径 |
| [Ho-RTDI-StratifiedAuxiliary](技术/Ho-RTDI-StratifiedAuxiliary.md) | Ho-RTDI：辅助最终样本随机分层生产接入 |
| [Ho-RTDI-TemperedTarget](技术/Ho-RTDI-TemperedTarget.md) | Ho-RTDI：零F0弱彩灯提案生产修复 |
| [Ho-RTDI-VisibilityConfidence](技术/Ho-RTDI-VisibilityConfidence.md) | Ho-RTDI 动态最终可见性 confidence |
| [Ho-RTDI-ZeroF0RgbSampling](技术/Ho-RTDI-ZeroF0RgbSampling.md) | Ho-RTDI 零F0表面的RGB重要性 |

## 日期与专项审计

| 文档 | 内容 |
| --- | --- |
| [Ho-RTDI-CompletionAudit-2026-10-15](审计/Ho-RTDI-CompletionAudit-2026-10-15.md) | Ho-RTDI 完成度审计（2026-10-15） |
| [Ho-RTDI-GpuTimingAudit](审计/Ho-RTDI-GpuTimingAudit.md) | Ho-RTDI 独立Player GPU计时审计 |
| [Ho-RTDI-MotionProtocol](审计/Ho-RTDI-MotionProtocol.md) | Ho-RTDI 既有运动向量协议审计 |
| [Ho-RTDI-ReferenceConvergence](审计/Ho-RTDI-ReferenceConvergence.md) | Ho-RTDI：参考工程收敛对照与静止历史修复 |

## 历史实验与过程

| 文档 | 内容 |
| --- | --- |
| [Ho-RTDI-AdaptiveSpecularExchangeRejection](归档/Ho-RTDI-AdaptiveSpecularExchangeRejection.md) | Ho-RTDI 连续接纳量策略拒绝 |
| [Ho-RTDI-AuxiliaryEnvironmentPartition](归档/Ho-RTDI-AuxiliaryEnvironmentPartition.md) | Ho-RTDI 辅助估计与天空贡献归属 |
| [Ho-RTDI-AuxiliaryVisibilitySwitch](归档/Ho-RTDI-AuxiliaryVisibilitySwitch.md) | Ho-RTDI：opaque/native辅助路径切换 |
| [Ho-RTDI-CastleFrameAudit](归档/Ho-RTDI-CastleFrameAudit.md) | Ho-RTDI：隔离古堡的实际帧支持审计 |
| [Ho-RTDI-CastleTemporalStability](归档/Ho-RTDI-CastleTemporalStability.md) | Ho-RTDI：静止古堡视点的时间波动 |
| [Ho-RTDI-ClampingColourAudit](归档/Ho-RTDI-ClampingColourAudit.md) | Ho-RTDI 稳定混合镜面的Clamping改色比例 |
| [Ho-RTDI-CompactAuxiliaryPayload](归档/Ho-RTDI-CompactAuxiliaryPayload.md) | Ho-RTDI：第二fresh解析样本的8-byte记录 |
| [Ho-RTDI-ComponentNoiseAudit](归档/Ho-RTDI-ComponentNoiseAudit.md) | Ho-RTDI：主/辅助贡献噪声分解 |
| [Ho-RTDI-CutoutFiltering](归档/Ho-RTDI-CutoutFiltering.md) | Ho-RTDI Cutout 漫反射滤波 |
| [Ho-RTDI-CutoutShadowCore](归档/Ho-RTDI-CutoutShadowCore.md) | Ho-RTDI 双灯阴影内部的过滤漏光门槛 |
| [Ho-RTDI-CutoutSpecular](归档/Ho-RTDI-CutoutSpecular.md) | Ho-RTDI Cutout 镜面质量验证 |
| [Ho-RTDI-DemodulatedProposalRejection](归档/Ho-RTDI-DemodulatedProposalRejection.md) | Ho-RTDI：去调制信号域目标对照拒绝 |
| [Ho-RTDI-DiffuseColourStageAudit](归档/Ho-RTDI-DiffuseColourStageAudit.md) | Ho-RTDI：关闭彩灯后的残色入口 |
| [Ho-RTDI-DynamicAuxiliaryCDFAudit](归档/Ho-RTDI-DynamicAuxiliaryCDFAudit.md) | Ho-RTDI：动态灯集与辅助 CDF |
| [Ho-RTDI-DynamicEnergyConvergence](归档/Ho-RTDI-DynamicEnergyConvergence.md) | Ho-RTDI：可控100:1灯变化的能量收敛 |
| [Ho-RTDI-DynamicVisibility](归档/Ho-RTDI-DynamicVisibility.md) | Ho-RTDI 动态遮挡与历史取舍 |
| [Ho-RTDI-EnvironmentDirectionAudit](归档/Ho-RTDI-EnvironmentDirectionAudit.md) | Ho-RTDI：primary环境方向与MIS单位审计 |
| [Ho-RTDI-FirstGuideVarianceRejection](归档/Ho-RTDI-FirstGuideVarianceRejection.md) | Ho-RTDI：首Atrous guide颜色/variance对照拒绝 |
| [Ho-RTDI-GuideDistanceAudit](归档/Ho-RTDI-GuideDistanceAudit.md) | Ho-RTDI：三最终样本NRD距离输入审计 |
| [Ho-RTDI-LargeDynamicCutout](归档/Ho-RTDI-LargeDynamicCutout.md) | Ho-RTDI 256/512动态Cutout验收 |
| [Ho-RTDI-LargeDynamicSpecular](归档/Ho-RTDI-LargeDynamicSpecular.md) | Ho-RTDI 256/512动态镜面失败基线 |
| [Ho-RTDI-LargeSpecularStages](归档/Ho-RTDI-LargeSpecularStages.md) | Ho-RTDI 大尺寸镜面阶段定位 |
| [Ho-RTDI-LateJitterRejection](归档/Ho-RTDI-LateJitterRejection.md) | Ho-RTDI：后续Atrous随机偏移控制拒绝 |
| [Ho-RTDI-LateSpecularExchangeAudit](归档/Ho-RTDI-LateSpecularExchangeAudit.md) | Ho-RTDI 镜面后续Atrous固定交换对照 |
| [Ho-RTDI-LateSpecularExchangeRejection](归档/Ho-RTDI-LateSpecularExchangeRejection.md) | Ho-RTDI 全局镜面固定交换候选拒绝 |
| [Ho-RTDI-MixedEnvironmentStages](归档/Ho-RTDI-MixedEnvironmentStages.md) | Ho-RTDI 混合天空/解析灯的NRD阶段定位 |
| [Ho-RTDI-MixedSpecularMomentAudit](归档/Ho-RTDI-MixedSpecularMomentAudit.md) | Ho-RTDI 混合镜面颜色与亮度moment |
| [Ho-RTDI-MultiCameraAudit](归档/Ho-RTDI-MultiCameraAudit.md) | Ho-RTDI：交替相机的状态与实际输出 |
| [Ho-RTDI-MultiCameraPointMeans](归档/Ho-RTDI-MultiCameraPointMeans.md) | Ho-RTDI：多相机逐像素期望验收 |
| [Ho-RTDI-MultiCameraReuseControl](归档/Ho-RTDI-MultiCameraReuseControl.md) | Ho-RTDI：多相机复用对照与质量判据复查 |
| [Ho-RTDI-MultiLightQuality](归档/Ho-RTDI-MultiLightQuality.md) | Ho-RTDI 真实双灯局部质量审计 |
| [Ho-RTDI-OpaqueAuxiliaryAudit](归档/Ho-RTDI-OpaqueAuxiliaryAudit.md) | Ho-RTDI：无 Cutout 的辅助样本执行审计 |
| [Ho-RTDI-OpaqueDirectAuxiliary](归档/Ho-RTDI-OpaqueDirectAuxiliary.md) | Ho-RTDI：opaque第二辅助的局部消费 |
| [Ho-RTDI-OrthographicSpecularAudit](归档/Ho-RTDI-OrthographicSpecularAudit.md) | Ho-RTDI 正交镜面view权重对照 |
| [Ho-RTDI-PairedFinalSamplePrototype](归档/Ho-RTDI-PairedFinalSamplePrototype.md) | Ho-RTDI 独立第二最终样本原型 |
| [Ho-RTDI-PairedLightDomainAudit](归档/Ho-RTDI-PairedLightDomainAudit.md) | Ho-RTDI 第二最终样本的灯域退化 |
| [Ho-RTDI-PairedPowerCDFPrototype](归档/Ho-RTDI-PairedPowerCDFPrototype.md) | Ho-RTDI 辅助样本的RGB功率CDF |
| [Ho-RTDI-PerspectiveMixedEnvironment](归档/Ho-RTDI-PerspectiveMixedEnvironment.md) | Ho-RTDI 透视混合天空/解析灯对照 |
| [Ho-RTDI-PresampleCoherenceCandidate](归档/Ho-RTDI-PresampleCoherenceCandidate.md) | Ho-RTDI：初始RIS共享范围隔离候选 |
| [Ho-RTDI-PrimaryFilteringAudit](归档/Ho-RTDI-PrimaryFilteringAudit.md) | Ho-RTDI 静态彩灯能量：NRD阶段定位 |
| [Ho-RTDI-PrimaryMotion](归档/Ho-RTDI-PrimaryMotion.md) | Ho-RTDI 移动主表面：梯度起点修正与未通过基线 |
| [Ho-RTDI-PrimaryReuseAudit](归档/Ho-RTDI-PrimaryReuseAudit.md) | Ho-RTDI：弱灯主样本复用对照 |
| [Ho-RTDI-ProposalBoundary](归档/Ho-RTDI-ProposalBoundary.md) | Ho-RTDI 采样策略相邻边界与分辨率 |
| [Ho-RTDI-ResolutionQuality](归档/Ho-RTDI-ResolutionQuality.md) | Ho-RTDI 256/512尺寸质量验收 |
| [Ho-RTDI-ReuseNoise](归档/Ho-RTDI-ReuseNoise.md) | Ho-RTDI 彩灯噪声与复用对照 |
| [Ho-RTDI-RTGI-可行性调查](归档/Ho-RTDI-RTGI-可行性调查.md) | Ho-RTDI / Ho-RTGI 可行性调查 |
| [Ho-RTDI-SecondAuxiliaryPrototype](归档/Ho-RTDI-SecondAuxiliaryPrototype.md) | Ho-RTDI 第二个独立辅助样本 |
| [Ho-RTDI-SpatialSpecularAudit](归档/Ho-RTDI-SpatialSpecularAudit.md) | Ho-RTDI 镜面中心偏差与全平面积分 |
| [Ho-RTDI-SpecularConfidenceAudit](归档/Ho-RTDI-SpecularConfidenceAudit.md) | Ho-RTDI 快速镜面置信度与历史审计 |
| [Ho-RTDI-SpecularDistanceControl](归档/Ho-RTDI-SpecularDistanceControl.md) | Ho-RTDI：specular距离单变量控制 |
| [Ho-RTDI-SpecularNormalBudgetRejection](归档/Ho-RTDI-SpecularNormalBudgetRejection.md) | Ho-RTDI 镜面normal预算方差对照拒绝 |
| [Ho-RTDI-SpecularReferenceControl](归档/Ho-RTDI-SpecularReferenceControl.md) | Ho-RTDI：独立无辐射采样噪声输入控制 |
| [Ho-RTDI-SpecularRgbDistanceRejection](归档/Ho-RTDI-SpecularRgbDistanceRejection.md) | Ho-RTDI 镜面RGB range距离对照拒绝 |
| [Ho-RTDI-SpecularRgbProposalAudit](归档/Ho-RTDI-SpecularRgbProposalAudit.md) | Ho-RTDI 非零F0的RGB提案对照 |
| [Ho-RTDI-SpecularWeightAudit](归档/Ho-RTDI-SpecularWeightAudit.md) | Ho-RTDI 稳定/动态镜面滤波接纳量 |
| [Ho-RTDI-StableRgbGuideRejection](归档/Ho-RTDI-StableRgbGuideRejection.md) | Ho-RTDI：稳定首轮 RGB guide 实验收尾 |
| [Ho-RTDI-StableSpecularGuideRejection](归档/Ho-RTDI-StableSpecularGuideRejection.md) | Ho-RTDI：稳定Clamping颜色guide隔离控制 |
| [Ho-RTDI-SurfaceAuxiliaryCandidate](归档/Ho-RTDI-SurfaceAuxiliaryCandidate.md) | Ho-RTDI：表面响应辅助 RIS 隔离候选 |
| [Ho-RTDI-VndfProposalRejection](归档/Ho-RTDI-VndfProposalRejection.md) | Ho-RTDI：相同预算VNDF BRDF候选拒绝 |
| [Ho-RTDI-持续推进记录](归档/Ho-RTDI-持续推进记录.md) | Ho-RTDI 持续推进记录 |

## 合并清理说明

以下独立候选/评审文档已被后续正式接入替代，原实测与边界说明已并入对应技术文档的历史证据节；旧文件删除，包内引用改指合并后的文档。

| 原独立文档 | 合并后入口 |
| --- | --- |
| `Ho-RTDI-DiffuseSegmentCandidate.md` | [Ho-RTDI-DiffuseConvexClamping](技术/Ho-RTDI-DiffuseConvexClamping.md) |
| `Ho-RTDI-TemperedTargetCandidate.md` | [Ho-RTDI-TemperedTarget](技术/Ho-RTDI-TemperedTarget.md) |
| `Ho-RTDI-StratifiedAuxiliaryCandidate.md` | [Ho-RTDI-StratifiedAuxiliary](技术/Ho-RTDI-StratifiedAuxiliary.md) |
| `Ho-RTDI-MultiSampleIntegrationDecision.md` | [Ho-RTDI-RuntimeMultiSamples](技术/Ho-RTDI-RuntimeMultiSamples.md) |

失败实验和独立报告仍保留，以免重复尝试已拒绝方案或覆盖原始质量门槛。测试代码、原始JSON与哈希清单不随文档清理删除。
