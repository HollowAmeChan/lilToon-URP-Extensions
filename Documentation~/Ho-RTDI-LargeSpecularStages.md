# Ho-RTDI 大尺寸镜面阶段定位

第47步只加Editor阶段诊断，针对第46步256 rapid失败，使用既有`ValidationSpecularStage`接口，不增加运行时捕获资源或调生产参数。

## 可复现条件与单位

`RunCutoutResolutionSpecularStages/Compatibility`沿用256²、8候选、固定60Hz、192预热/256观察、3584点网格与独立CPU GGX/逐帧Cutout参考。三个配置为slowTS、rapidTS、rapidNone；最后一组只关闭ReSTIR temporal/spatial，RELAX仍启用。每组重置历史/帧种子，蓝灯在所有点/帧必须无遮挡。

同时读取raw、NRD Temporal（pipeline4）、Clamping（6）、首Atrous（9）、末Atrous（10）与最终发布。每帧必须捕获各阶段；pipeline10重复四轮，每帧读其最后一次输出，256观察帧产生1024次复制，其余各256次。复制计数不代表GPU成本。

NRD中间镜面已去除材质因子，且应用HDR缩放。初次仅除HDR scale的诊断被末阶段一致性检查拒绝，没有拿它作阶段结论。修正沿用既有镜面诊断的raw/归一化输入比例：本固定场景F0为均匀灰色、roughness/normal一致且正交观察，该比例包含公共材质因子与逆HDR scale。每帧从非零输入点恢复，并在全部观测点检查末阶段与发布RGB的差值<未遮挡参考×.003。CPU能量参考仍独立计算，不从GPU拟合。此还原方式不适用于任意材质/透视相机；未将去调制的镜面直接当物理照明，也未把alpha当RGB方差。

核对固定NRD `NRD.hlsli`的`NRD_MaterialFactors`、本地NormalizeNRD/ResolveNRD，以及用户参考NRD参数接口。specPhi=1、Atrous=5未变。各配置raw平均红/蓝必须在3%内、尺度有限、gradient有实际调度。

## 两条阶段结果

两条diagnosticOnly报告一致，捕获/尺度/CPU能量检查通过且退出0。这只代表诊断有效；rapid的qualityPass仍false，第46步正式质量失败未改变。

| 配置 | raw frameL1 | Temporal | Clamping | 首Atrous | 末Atrous | 发布 |
| --- | --- | --- | --- | --- | --- | --- |
| slowTS | 1.065496 | .175365 | .175356 | .106501 | .077348 | .077365 |
| rapidTS | 1.023884 | .653306 | .650717 | .578224 | .507934 | .507913 |
| rapidNone | 1.024574 | .652513 | .649968 | .592810 | .530527 | .530498 |

rapidTS的RGB MSE（每点/帧/通道）从raw 3.262382e-6，降到Temporal 1.494918e-6、首Atrous 1.255956e-6、发布9.922330e-7；rapidNone发布1.054663e-6。slowTS发布4.690217e-8。大小与亮度有关，MSE不作为跨场景比较的归一化误差；JSON保留double精度，日志用科学记数法。

slowTS/rapidTS最终值与第46步正式默认基线一致，末阶段与发布仅有FP16量化差异。快速遮挡中，Temporal至末Atrous都在减小逐帧误差，没有发现末阶段突然增加误差；关闭ReSTIR复用仍失败，不能以关闭复用当修复。这些数据尚不能区分快速confidence削弱历史、输入采样方差与空间过滤半径各自的贡献。

## 复查与下一项

Runtime/Shader/native/GI/Player模板无变化。Roslyn errors0，两条真实D3D12阶段诊断各18条结果一致；重新运行原RenderGraph正式默认入口，仍3/6、退出1、数值不变。正式报告/失败门槛保持独立，不以诊断退出0替代qualityPass。改动过滤到Editor入口/诊断与文档，不重复未变的Player构建。

下一项读同一快速场景的镜面confidence与实际历史，再以已有Editor设置做有明确目的的对照，区分历史利用不足和高输入方差。核对本地Temporal中confidence缩短最大累积帧数的实际分支，并沿用户FullSample gradient参考检查。只有证据支持时再改运行时；保留changed-pixel/core/彩灯与大尺寸镜面门槛。古堡合并天空模型继续只做资产支持审计，GPU预算/真实motion/最终材质合成不在本步扩张。
