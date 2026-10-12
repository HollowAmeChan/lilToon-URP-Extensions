# Ho-RTDI 第二最终样本的灯域退化

第51步扩大第50步隔离原型的解析灯范围，比较原单最终样本与均匀辅助样本。结果不支持生产接入：辅助样本在大量弱灯下严重退化，聚光灯快速组也仍失败。

## 参考与判定

核对用户RAB局部灯PDF/目标契约、fixed FullSample灯源准备，以及本地`BuildPowerPdf`的点灯功率与平方spot cone shaping。主ReSTIR在32灯配置使用已有Power RIS，辅助样本仍均匀；这是主动隔离辅助提案的退化，没有把整个管线改回Uniform。

`RunPairedLightDomainBaseline/Compatibility`和`RunPairedLightDomain/Compatibility`各四组：spotRapid、spotStep、manyWeakRapid、hdrRapid。完整256²渲染，原3584点世界区域网格、192预热/256观察、固定60Hz、8初始候选、T/S与RELAX；无环境输入。spot内/外角15/55度、朝向平面中心，CPU必须实际覆盖cone衰减区。多灯为两盏强红/蓝点灯加30盏分布在Far左侧的弱蓝灯（各.01强度），而非仅增加零贡献条目。HDR将两灯强度从4改为40000，并要求实际NRD线性scale<1。

CPU Smith GGX、距离衰减、平方cone、逐帧alpha遮挡独立计算。所有Far/弱灯与receiver的交点在完整blocker左侧，并且骨骼仅向非负x移动，因此整个轨迹无遮挡；预计算这一几何条件及每灯蓝响应。Near交点预计算后按实际骨骼位移/UV-alpha更新，并每帧抽查与原CPU逐射线参考一致，避免对弱灯逐帧重复无效求交。没有从GPU反推参考。

raw每通道3%、filtered5%、frameL1<.35、step实际变化像素前三帧L1<.35保留；RGB平方误差除以同批CPU参考R²+B²总和，报告为normalized MSE，用于跨强度比较，未把HDR绝对MSE与暗场直接相比。

## 两条正式结果

两条数值完全一致。baseline 0/4、均匀辅助原型2/4，均qualityPass=false、退出1；报告分别为`rtdi-lightdomain51-baseline-rg/compat.json`与`...-paired-...`。

| 配置 | baseline frameL1 | paired frameL1 | paired变化像素L1 | paired filtered红 / 蓝 | paired通过 |
| --- | --- | --- | --- | --- | --- |
| spotRapid | .530974 | .360202 | — | .985535 / .989369 | 否 |
| spotStep | .365338 | .226086 | .264976 | .990500 / .982383 | 是 |
| manyWeakRapid | .505110 | .527315 | — | .819039 / .875879 | 否 |
| hdrRapid | .514804 | .343575 | — | .985697 / .990156 | 是，接近门槛 |

manyWeak的raw normalized MSE从1.289396升到7.764652，filtered从.383570升到1.087441；平均raw红/蓝仍.993759/1.000220。当前均值能量检查通过不代表输入噪声可用；大量弱灯令均匀辅助提案稀有强灯权重放大，随后过滤出现严重能量损失。本步没有把损失直接解释为所有场景的NRD系统偏差，也没有靠增候选数或放宽5%掩盖它。

spotStep改善短期响应，HDR实际scale为baseline 1/512、paired 1/256；HDR与spotRapid仍余量有限，不能据两个通过组宣称完整灯域支持。

## 复查与下一项

生产Runtime/Shader/native/GI/Player未变，Roslyn errors0、无新增DI Shader诊断、GUID唯一和diff whitespace通过。原第50步两点灯镜面六组RenderGraph回归通过且数值不变；未重建未变Player/GI。所有实验入口与报告独立，正式失败保留。

下一项为辅助估计提供匹配实际灯索引的正功率分布，减少稀有强灯的大权重，再按相同参考/种子与Uniform对照。应保留RGB通道短窗口表现，核对真实CDF区间PDF与inverse PDF、灯启停/索引变化及native alpha路径；不能只把亮度目标直接复制后称已解决颜色权衡。环境MIS/无Cutout路径、实际Player与额外射线/提案GPU成本仍待处理。古堡继续只作资产支持审计，不扩张材质合成/PT。
