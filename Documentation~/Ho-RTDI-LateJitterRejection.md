# Ho-RTDI：后续Atrous随机偏移控制拒绝

第94步复查55/63阶段证据：中心偏亮由后续空间滤波搬移高光，不是全平面同幅增能。57—67固定交换、正交view、RGB距离、历史预算已有失败证据，不重复相同候选。

固定NRD与用户旧fork `RenderingPlugin/External/NRD/Shaders/RELAX_Atrous.cs.hlsl` 在step>4加入随机offset，注释目的为减少大步ringing。隔离shader数组index10把该offset设零，保留行归一化、全部权重、variance、参数、初始/复用和真实辐射。公共stencil同时影响diffuse/specular，不能描述为只改specular；首Atrous不变。退出恢复原shader。

生产Triple混合环境两路径一致：

| mixed复用 | 生产filtered spec红/蓝 | 无后续jitter红/蓝 |
| --- | --- | --- |
| none | 1.050745 / 1.048828 | 1.050623 / 1.048494 |
| T/S | 1.044358 / 1.050032 | 1.044634 / 1.048941 |

候选3/4，但none红仍超5%，qualityPass=false、实际两进程exit1。T/S压到门槛内不足以接纳整个候选。原raw保持；Single动态镜面三项通过，frameL1 .109329/.342875/.346809，exit0。恢复生产环境2/4、exit1，原数据保持。没有把diag/构建成功代替正式质量。

初次生成有SourceAssetDB修改时间错误：旧helper反复写入内容不变的compute。改为内容变化才写入header/compute后实际重跑，错误消失、质量数据保持，仍exit1。Roslyn errors0、无新增DI shader warning/error、diff通过。未修改Runtime/vendor/generator/native/Player/GI。

报告 `rtdi-runtime78-env-baseline-nojitter94-rg/compat.json`、`rtdi-nojitter94-motion-rg.json`；修正导入日志 `nojitter94-import-fixed.log`，位于隔离工程。

下一项核对NRD输入距离。生产三样本RGB合成后，两lobe alpha仍只写primary selected light distance；官方说明ReSTIR选择的灯距离不能自动视作对应BRDF lobe距离，多path镜面应使用明确的距离合并合同。用户新forkDI `ShadeSamples.hlsl` 的lightDistance只用于直接输出，未展示NRD绑定，不能据它证明当前NRD距离正确。先做实际输入与合同审计，不直接给距离取平均、清零或添加通用PT框架。[NRD官方输入说明](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md)
