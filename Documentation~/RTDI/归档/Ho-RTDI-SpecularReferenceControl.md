# Ho-RTDI：独立无辐射采样噪声输入控制

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第97步沿第96步排除距离跨度为静态失败根因。核对用户fork ShadeSamples的diffuse/specular分离与固定NRD.hlsli的material demodulation合同：只替换进入原shader去调制之前的物理specular RGB，原NRD材质因子、全帧HDR峰值/缩放、距离、normal/depth、history、参数、采样预算和diffuse保持。

## 控制定义

128²正交平面、绿色常量天空和两红蓝Point，独立CPU Smith/GGX与半球积分生成完整RGBAFloat线性纹理。局部使用真实正交view、固定量化F0=10/255与roughness=102/255、URP距离/range项；天空使用相同物理参考积分。不是把物理颜色直接写入NRD去调制域。shader在Shade内覆盖物理specular后仍执行原material factors、前端pack与发布还原。

原Monte Carlo仍运行，实际RGB输出被参考替换；因此这是一项oracle输入控制，不是可交付的采样优化。距离与diffuse仍随机，不能描述为全部输入无噪声。额外纹理及CPU计算仅Editor隔离使用，退出恢复shader，不写Runtime/GI/native/Player。

首次保护因receiver声明有三处而拒绝生成，未作为验证通过；改为唯一Shade上下文锚点后实际重跑三入口。保留exact-count守卫。

## 实测

两路径sky-only/mixed × none/TS全部4/4、实际exit0。每组32帧全图读回524288像素，raw reference误差最大 .000792（.0792%），保留 .003输送误差门槛。观察证明独立参考实际到达shader输出，不将这种匹配当成真实采样正确性证明。

| mixed | 随机输入filtered spec红/蓝 | 参考输入filtered spec红/蓝 |
| --- | --- | --- |
| none | 1.050745 / 1.048828 | .987924 / 1.022054 |
| T/S | 1.044358 / 1.050032 | .987924 / 1.022054 |

阶段读取RG实际exit0，input检查与正式两路径一致。mixed/none：

| 阶段 | 红/蓝比例 | 实际RGB时间方差 | moment/variance统计 |
| --- | --- | --- | --- |
| raw | .999624 / .999624 | 0 | 不适用 |
| Temporal/Clamping | .998885 / .998885 | 数值零约6e-18 | alpha moment spread 0 |
| 首Atrous | .998243 / .998957 | 数值零约5e-18 | scalar variance 0 |
| 末Atrous | .987546 / 1.021674 | 1.91945e-9 | alpha为history，非variance |
| 发布 | .987924 / 1.022054 | 1.90511e-9 | 不适用 |

阶段副本额外ARGBHalf量化与比例恢复带来末位差，原末阶段/发布一致性门槛保持。参考仍有约-1.2%/+2.2%局部形状改变；无噪声不等于过滤恒等。对照支持采样噪声参与原5%失败，而非证明距离/滤波本身全部正确。

Roslyn errors0、GUID唯一、修正后三日志无新增shader warning/error或SourceAssetDB时间错误，diff通过。报告前缀 `rtdi-runtime78-env-baseline-specreference97-*`、`rtdi-specreference97-input-{energy,stages}-*`、`rtdi-auxenvmoments65-baseline-specreference97-rg.json`，位于隔离工程。

## 下一项取舍

生产失败继续保留，不接纳oracle输入。下一项优先评估真实DI选样目标是否匹配去调制后的两lobe信号量级，保持实际shade、PDF、预算及动态门槛，用可计算的信号权重作隔离对照，而不是继续试滤波指数/历史系数。距离lobe合同仍独立待完善，古堡仅支持审计，不扩张PT/GI或最终NPR接收。
