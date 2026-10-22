# 031 默认5轮传播与独立权重追踪

保持Runtime/DI/SDK/Shader原文件、参数、单位64、5轮不变。固定新18点、旧反例(141,157)和其3×3的8个邻点，共27点；19点有既有合格同模型参考，8邻点只有实际primary/geometry/albedo，不伪造MC真值。四seed仍0/104729/5347609/6396185，各64warm/256观察。

**质量阶段结论只来自原版shader。** pipeline10按ordinal存四个真实输出（step2/4/8/16），同时保留input/Pre/Temporal/条件fast/Clamp/firstAtrous/pub。所有copy是RGBA32F typed copy/UAV，自测64分量逐bit过，writer count仍9×1+10×4。stock control的完整raw+filtered全图时间均值图对026相同seed逐bit一致。

首次debug-UAV资格16584 actual0、98请求，功能/绑定通过但没有满足“精确observer”要求：与旧短资格均值图最大差约2.38e−7，保留initial-debug。随后6360 stock短control actual0/98请求，raw和filtered全图map完全同。正式stock control9188 actual0/1314请求；最终debug短资格40664 actual0/98请求、debug权重正式45656 actual0/1314请求。五进程D3D12/Shader/Property/UAV/fence/drift0，不把初次numeric差异改成bit-exact通过。

debug版本只在当前GI实例临时替换shader10，stock算式逐行保留，只加UAV/目标像素和权重写入，finally原shader/kernel恢复。原版与debug分开：debug四seed整图均值最大差1.49e−8～2.98e−8，局部intermediate最大约3.26e−9、physical pub个别样本约3.81e−6（half量化级）；不能拿debug数据作精确画质门槛。其实际权重可重建自身输出，最大RGB差7.34e−11、sumW差≤1.20e−7，足以解释大幅传播方向，非宣称跨shader逐byte等价。

| stock阶段／同18点reference L1 | 值 |
| --- | --- |
| first Atrous step1 | .130687 |
| ordinal1 step2 | .215150 |
| ordinal2 step4 | .306872 |
| ordinal3 step8 | .386896 |
| ordinal4 step16 | .462370 |

反例却不同：Clamp L1 .04152，firstAtrous .14924，后续step2/4/8/16为.14305/.15325/.12229/.11055；蓝/reference从first .8259，经step4 .9072，最终.9967。后两轮在此恢复了部分首轮损失，因此F51全局cap3继续搁置。

权重只在0/32/64/96/128/160/192/224/255观察帧记录，短资格只0/last。每点每轮含center/input variance、normal/viewZ、offset/sumW/history、output、8个tap坐标/actual final weight/radiance-variance/geometry-normal-material-luma因子/guide。它不是全帧完整邻域统计。

采样的6个亮边中心在四轮均有负亮度贡献，step16平均约−3.744e−4；反例step8/16平均约+4.620e−5/+2.220e−5。亮边step16约8.16%总权重来自低于center一半亮度的donor，反例该项为0。所有27点、含邻点的几何与输出保留，不能只挑有利位置。

进一步机制线索：stock `diffusePhiLIlluminationInv=1/max(1e−4,phi*sqrt(variance))`；GI输入已除scale64。按固定phi2与记录的variance，亮边step8/16触及该下限的采样比例78.7%/97.2%，反例13.9%/69.4%；step16的phi×sigma中位数分别4.85e−5与7.88e−5。实际luma因子反推inverse中位数约10000，与源码下限相符。这里是可测的单位域/权重假说，不称已经确认集成bug或修好图像。

255精确source对象、12新小文本、5完整logs、47原JSON/bin lossless gzip、各模式生成shader/body SHA、observer-review/analysis/verify，约24.4MiB。HGP1保存27点12阶段RGB及写入计数；HGW1保存选定帧4轮×27点×37float4。旧HGS1默认入口不变。`run_validation.ps1`区分control/control-matrix（stock）和qualification/matrix（debug权重）；initial-debug保留原源/原结果。

下一032：只Editor GI私有Atrous diffuse亮度权重分母下限按AppliedSignalScale64作物理单位对齐（1e−4/64），保持5轮、phi/variance/采样/信号单位/格式/其它原算式不变，先验证实际作用及source差异，再同新18+旧15及反例/暗控制/full/16phase/noise取舍。不能重启全局迭代扫描或1/.125纯单位扫描，不能只看亮边；失败撤回并保存反例。所有原质量/F39/static/复杂动态/1080完整预算继续，目标active。
