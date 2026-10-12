# Ho-RTDI 全局镜面固定交换候选拒绝

第58步扩大第57步能量对照的验证。全局替换虽然改善混合天空平均能量，却使既定动态镜面门槛失败；运行时候选及生成器改动均已撤回，保留可复现Editor对照。

## 候选与必要回归

候选只在DI私有派生的RADIANCE specular后续Atrous采用固定邻居质量/拒绝质量留中心，variance按系数平方传播；其他mode、diffuse、参数、native库、输入和可见性不变。临时生成器可重建且原SHA校验通过，但未因生成器成功就接纳。

依次实测64动态镜面、primary运动、混合环境、曲面运动、材质/深度/薄影边界、环境roughness、30/60度斜视、小太阳HDR和三样本灯域。primary/曲面/边界/环境roughness/斜视/小太阳以及三样本四组均通过；64动态镜面的快速和阶跃失败，是明确接纳阻碍。

## 动态噪声退化

`RunLateSpecularExchangeMotion/Compatibility`在恢复生产后仍可作用域替换真实index10重现候选，不改材质、初始样本、ReSTIR、confidence或NRD时钟/预算。原CPU参考与.35门槛保持，报告写`rtdi-latespec58-motion-rg/compat.json`，两条完全一致、qualityPass=false、退出1。

| 模式 | 原生产filtered frameL1 | 固定交换frameL1 | 接纳 |
| --- | --- | --- | --- |
| slow | .110073 | .112491 | 仅此组通过 |
| rapid | .343487 | .365093 | 否 |
| step | .347752 | .380824 | 否 |

平均能量仍通过不能替代逐帧误差。三样本隔离原型虽然4/4，spotRapid也从.324357升到.347648，manyWeak .307974→.330026、HDR .308588→.328263，明显消耗质量余量；不能用更多最终样本掩盖滤波退化并默认增加成本。

窄环境rough≈.098 filtered normalized MSE≈.031918、小太阳raw/filtered≈.994679/.984017均通过，只能说明这些输入没有失败，不抵消动态case。既定直接门槛已失败，因此不进行不必要的候选Player接纳构建，也不宣称部署完成。

## 撤回与下一项

生成器和`HoRTDINrdAtrous.hlsl`恢复HEAD，原SHA/全部派生check通过。恢复生产的64动态镜面再次通过，.343487/.347752及旧能量保持；第54步原环境入口仍2/4、退出1，既有混合镜面偏差继续保留。Editor复现两条与恢复回归、Roslyn/diff检查完成，Runtime/Shader/native/GI/Player无最终变化。

下一项先读稳定混合环境与动态caster的实际邻居质量/归一化和confidence分布，确认哪种状态需要能量稳定、哪种需要更强降噪，再评估有依据的策略。保留source/seed/所有门槛，不用盲调交换比例或phi把单个case刚好压线，也不重定义DI目标。opaque、动态CDF、多相机、环境/三样本产品集成与GPU预算仍待完成。
