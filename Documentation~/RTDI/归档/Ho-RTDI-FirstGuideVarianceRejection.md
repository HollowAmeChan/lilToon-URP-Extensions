# Ho-RTDI：首Atrous guide颜色/variance对照拒绝

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第105步继续104，将固定guide来源改为首Atrous9。核对固定/用户NRD smem：此阶段alpha已经把raw moment按variance samples转换并经过权重平方传播，不是Clamping6的raw second moment。比较（A）仅固定颜色、仍用当前传播variance；（B）range同时使用固定guide9的scalar variance。均不更改输出RGB/variance传播公式、phi、normal/view/material权重、row normalization、sampler或visibility。

只在隔离index10和既有单张ARGBHalf快照测试，stage9本帧复制，late读取四轮；每环境组704captures/2816late，每64动态组448/1792。临时128² 128KiB、64² 32KiB，额外复制/读纹理；GPU毫秒仍未测，Runtime/vendor/generator/native/GI/Player未改。

## 两路径正交及动态

| mixed复用 | 固定颜色A红/蓝 | 固定颜色+variance B红/蓝 |
| --- | --- | --- |
| none | 1.030553 / 1.022253 | 1.080400 / 1.073969 |
| TS | 1.030096 / 1.027381 | 1.082340 / 1.079367 |

A两正交路径4/4/exit0；B两路径2/4/exit1，原raw/diffuse保持。固定guide variance扩张后续带宽但没有修复局部能量，不能按“同一来源”就认为它是真实有效不确定度。

64动态两组均3/3、exit0。A slow/rapid/step frameL1 .110886/.344653/.348361，最后两项余量很小；B .130622/.340216/.337673，slow较差而rapid/step较好。旧每帧.35与响应/均值门槛保持，不能只报告动态最佳项。

## 透视否决

FOV40原四组，两个候选均2/4/exit1，mixed：

| 复用 | A红/蓝 | B红/蓝 |
| --- | --- | --- |
| none | 1.095789 / 1.100037 | 1.248131 / 1.259582 |
| TS | 1.094050 / 1.112132 | 1.249259 / 1.269437 |

两者均拒绝生产化，没有构建失败候选Player。八个实际进程、原门槛/失败出口保留；Roslyn errors0、捕获/消费合同、shader/diff通过，无新增DI warning/error或SourceAssetDB时间错误。

报告 `rtdi-runtime78-env-baseline[-perspective]-stableguide104-first105-{colour,matched}-*` 与 `rtdi-firstguide105-{colour,matched}-motion-rg.json`，位于隔离工程。104原报告与入口未覆盖。

## 下一项

不继续按variance/phi常数压线。104/105稳定颜色仅用亮度差，可能仍将对立RGB变化抵消；第64步当前颜色RGB距离曾使动态噪声失败，不能直接重试。下一步在已稳定且空间降噪的首轮guide上比较RGB距离，保留当前variance，分别验证透视与动态，检验稳定来源是否消除原颜色距离的噪声代价。仅作为一次明确单变量控制，未证明前不新增产品资源或Feature，距离lobe/最终NPR仍保留边界。
