# Ho-RTDI：三最终样本的生产接入评审

第77步重新执行当前生产单样本与隔离三样本的同fixture对照，并区分已有滤波问题与新采样回归。它是接入决策，不新增未工作的Inspector选项，也不宣称全部质量门槛已通过。

## 当前源码与实测收益

沿用户fork ShadeSamples的inverse PDF/visibility/物理贡献契约，主ReSTIR仍只保存自己的历史；两辅助是fresh解析灯估计，固定 .5/.25/.25 混合。环境贡献按第54步分域保留一份。第74～76步已验证8-byte native sidecar、opaque局部消费及受控切换，无需搬入通用PT框架。

当前Runtime重跑 `RunPairedLightDomainBaseline/Compatibility`，四配置均正式失败；三样本compact两路径四配置均通过。两路径一致。

| 配置 | 单样本frame L1 | 三样本frame L1 | filtered normalized MSE减少 |
| --- | --- | --- | --- |
| spotRapid | .530974 | .324357 | 61.62% |
| spotStep | .365338 | .202892 | 68.67% |
| 32灯manyWeakRapid | .505110 | .307974 | 61.02% |
| hdrRapid | .514804 | .308588 | 61.96% |

这里只覆盖既定256²skinned alpha fixture，不能代表任意灯分布、分辨率、GPU或完整帧时间。收益主要是降低动态最终可见性与rare光源采样噪声；增加两条最终visibility query，现有总GPU成本仍未知。

## 天空镜面失败归属

重新执行当前单样本 `RunAuxiliaryEnvironmentBaseline/Compatibility` 与compact三样本 `RunCompactAuxiliaryEnvironment/Compatibility`，两者仍2/4、exit1。mixed filtered specular中心红/蓝比例：

| ReSTIR reuse | 单样本 | 三样本 |
| --- | --- | --- |
| none | 1.052769 / 1.043540 | 1.050745 / 1.048828 |
| temporal+spatial | 1.047027 / 1.050594 | 1.044358 / 1.050032 |

三样本none蓝有约.00529比例增加，但该配置最大通道误差略低；reuse最大误差也略低。既有局部滤波偏差不是新采样独有的问题，原5%失败不得删除，也不应仅凭同一旧失败无限期阻止采样接入工作。透视局部高光、短窗强弱灯与colour tails等更广的约束仍需保留。

## 接入决定与具体边界

下一步推进可选采样预算的产品实现，默认保留单样本。它必须实际调度并完成对应验证，不能先加一个不起作用的字段；选择多样本表示额外最终visibility成本，不能暗示已测得GPU毫秒或任意材质/PBR/NPR最终效果。

实施需要同时满足：

1. 从实际本帧AnalyticLight snapshot生成并上传辅助CDF，使用相同local/infinite顺序；proposal、所有inverse PDF与本帧灯集同步，不沿用Editor的live Light引用。
2. 每相机持有需要的GPU资源；opaque局部消费，native alpha完整8-byte sidecar；在scene同步、实际CutoutCount确定之后选路径与容量。资源销毁使用Unity/GPU生命周期，不能依赖测试readback同步。
3. current pass snapshot固定采样预算、CDF、绑定和shader变体，RG/compat两条一致；budget/资源/几何模式变化正确失效相关历史，保留primary的identity maps和明确的环境分域。
4. Raw及NRD两shade入口都能消费；one analytic delta/sky-only/零灯等路径保留合法行为，不为确定性单灯增加无收益的额外query。
5. 最终样本数变动不改变材质输入、lilToon暗部基底或最终合成策略，不新增AC terminal/通用PT/SSS/lilPBR支持。
6. 实际Editor与Win64 Player覆盖默认single和可选multi、opaque/Cutout切换、动态灯集、HDR/环境、每相机及不支持D3D11分支；已有质量失败单独明确记录，比较新路径是否引入回归。

默认single的SDK layout、material响应、ray/native ABI与最终输出需回归保持；只有可选多样本消耗新的辅助资源。Area/emissive UV灯暂不由此delta payload支持，但不改变长期DI目标。

本步未修改Runtime或Player，fresh测试均按正式失败退出并保存报告，无新增DI shader诊断。报告为现有 `rtdi-lightdomain51-baseline-rg/compat.json`、`rtdi-auxenv54-baseline-rg/compat.json` 和 `rtdi-compact74-env-partition-rg/compat.json`，日志 `promotion77-*`。后续提交必须以实际接入/部署为单位推进，而不是再增加一组无接入收益的固定NRD系数对照。
