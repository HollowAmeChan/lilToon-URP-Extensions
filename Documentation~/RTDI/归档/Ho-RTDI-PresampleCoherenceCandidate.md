# Ho-RTDI：初始RIS共享范围隔离候选

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第91步沿第90步定位的主样本弱灯波动，检查改变RIS tile的像素共享范围是否改善。只Editor生成临时compute、scoped替换并恢复Feature引用；不改生产、CDF、RIS存储、初始候选8、两类复用、实际最终射线或NRD参数。

用户参考工程 `RayTracing/DI/GenerateInitialSamples.hlsl` 使用 `pixelPosition / RTXDI_TILE_SIZE_IN_PIXELS` 初始化tile RNG；固定SDK `LocalLightSelection.hlsl/RISBuffer.hlsl` 从该RNG选tile，再从pixel RNG选tile内样本。改变共享范围属于缓存/统计取舍，不能只看一个画面就接受。

## 实测

原100:1三彩灯128²真实Cutout fixture，96帧，每阶段前16帧不计均值。第90步的逐点相对时间方差与packed shading/独立CPU能量分解保持。

| 共享范围 | primary红能量比 | raw红能量比 | filtered红能量比 | primary相对时间方差 | 九阶段 |
| --- | --- | --- | --- | --- | --- |
| 生产16×16 | 1.263596 | 1.116960 | 1.086514 | 165.8726 | 8/9 |
| 隔离4×4 | 1.014103 | .992241 | 1.057938 | 119.0211 | 8/9 |
| 隔离逐像素、独立stream | 1.317439 | 1.143867 | 1.121939 | 162.2076 | 7/9 |

4×4两路径一致，但filtered红误差5.79%仍超5%，两进程exit1。逐像素修正版100:1仍失败且blueDirectional raw红.955017超3%，exit1。辅助序列红.971107保持，不用主辅误差抵消来接受候选。

4×4独立四灯域全部通过、exit0。该fixture只有32弱灯配置使用Power presampling；Spot/HDR三配置仍用Uniform，结果不变是预期控制，不能描述为四个Power配置都得到改善。32弱灯frameL1 .310464，相对生产.311013只有小差异，无普适或成本结论。

## 随机序列复查

首个逐像素实验直接使用同一pixel position和pass seed初始化两个RNG，SDK首次tile/within-tile选择读取同一个随机数，不能作为独立采样对照。发现后隔离candidate改为不同pass seed并实际重跑；旧报告 `rtdi-coherence91-tile1-cutout-rg.json` 保留但不计有效候选证据。修正版使用 `tile1-separated` 文件名，导入shader明确检查seed替换合同。4×4没有更改原seed，所以逐像素修正版同时改变共享范围与stream，不能用它推导共享范围的单变量因果。

有效报告：`rtdi-coherence91-tile4-cutout-rg/compat.json`、`rtdi-coherence91-tile4-lightdomain-rg.json`、`rtdi-coherence91-tile1-separated-cutout-rg.json`。Roslyn errors0，GUID唯一、diff通过，有效日志无新增shader warning/error/SourceAssetDB时间错误。缓存访问成本没有有效GPU计时证明。

不生产化、不新增用户设置。下一项检查主reservoir target对弱彩灯相对误差的分配，保持真实辐射shade与RIS归一化不变；先隔离选择目标再扩展独立能量/动态验收，不通过放宽门槛或关闭现有复用完成任务。古堡继续只作支持审计。
