# Ho-RTDI 动态遮挡与历史取舍

第31步只增加隔离诊断，当时生产世界revision仍清空DI/NRD历史。已有[Cutout条件滤波](Ho-RTDI-CutoutFiltering.md)解决特定静态/相机质量，但移动caster逐帧噪声仍高。取消全局NRD重置看似直接，必须同时检验残影。第32步的真实可见性confidence与生产结果见[后续实现](Ho-RTDI-VisibilityConfidence.md)，以下保留原失败对照。

## 前提与参考

世界revision包含骨骼pose、transform、几何/材质/实例集合变更；它能使ReSTIR保守可见性失效，但不能据此断言所有接收表面历史都失效。另一方面，本地 `HoRTDIConfidence.hlsl` 仍假设静态世界：灯参数相同时直接令另一帧亮度等于已选亮度，且重新求可见性使用跳过Cutout的保守inline射线。直接保留NRD历史会让动态alpha阴影变化未被这条confidence检测。

核对用户工程的NativeRtxdiConfidencePass，以及其实际固定插件 `UnityPathTracing/RenderingPlugin/External/RTXDI/Samples/FullSample/Shaders/DenoisingPasses/ComputeGradients.hlsl`：重建另一帧的同一reservoir/light sample，`enableVisibilityReuse=false`求可见性，再比较同一采样亮度。[NRD官方History Confidence](https://github.com/NVIDIA-RTX/NRD/blob/master/README.md#history-confidence)要求隔离采样差异，用上一帧采样重求辐射变化；不能把两帧独立随机结果之差当作世界变化。官方当前资料与本地固定版本分别核对，不替换vendor版本。

## 隔离对照

维持两盏实际非零红/蓝点灯、64²/896ROI、初始8候选、TS和五次RELAX Atrous、phi=.5，192预热/256观察帧。仅Editor诊断允许NRD忽略world revision；ReSTIR temporal仍照原策略拒绝世界变化，settings/环境/time/lifetime重置仍有效。此开关没有序列化、Volume或Player路径。

独立平面/alpha射线参考逐像素验证红灯，新增逐帧断言蓝灯始终无遮挡，防止移动fixture改变参考前提。

- 慢速：原caster x=.12*sin(frame*.0175)。
- 快速：x=.25+.25*sin(frame*.27)，保持远灯无遮挡。
- 阶跃：每16帧在x=0/.5切换；除整幅统计外，单独评分每次突变后3帧中真正改变阴影的像素。

变化像素误差只比较红通道，按这些像素红灯全透光能量归一化；恒亮蓝灯和未变化像素不稀释残影。保持原整体能量5%、mean L1<.15、frame L1<.35，并要求阶跃变化像素L1<.35。该新增门槛没有放宽原条件。

| 模式 | NRD重置 | filtered红 | filtered蓝 | mean L1 | frame L1 | 最差帧L1 | 阶跃变化像素L1 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 慢速 | 是 | .996683 | 1.003196 | .068211 | 1.064071 | 1.139298 | — |
| 慢速 | 否 | .981705 | .997373 | .054479 | .125951 | .191294 | — |
| 快速 | 是 | .999707 | .995848 | .063999 | 1.024179 | 1.151504 | — |
| 快速 | 否 | .986111 | 1.009177 | .036843 | .237205 | .366560 | — |
| 阶跃 | 是 | .977486 | .997596 | .065292 | .204079 | 1.108166 | .157649 |
| 阶跃 | 否 | .969377 | 1.022307 | .070436 | .176262 | .393181 | .570931 |

两种策略各case的raw结果完全相同，分通道能量都在3%内。保留历史明显降噪，但阶跃残影失败；仅看整体frame L1会错误判通过。阶跃前三帧整幅L1还从.508054降至.287724，进一步说明需要评分变化像素，而非总画面平均。

## 结论与下一项

不把忽略世界revision的诊断开关变成产品选项。生产仍保守重置。下一项先为动态世界计算真实最终可见性梯度，特别是Cutout anyhit；只有变化附近降低confidence，稳定接收面保留历史。相同灯捷径必须限于世界稳定，NRD历史与ReSTIR可见性复用要分别处理。需要继续检验动态主表面、实例增加/移除、灯/alpha突变及拖影，不能把这个静态接收平面当成任意动态场景支持。

入口 `HoRTDIValidation.RunCutoutMotionReset/RunCutoutMotionKeep` 及各Compatibility版本；诊断exit0但JSON `qualityPass=false`。`RunCutoutMotionKeepQuality` 正式门槛必须失败退出。报告为隔离工程 `rtdi-cutout31-motionReset/motionKeep-rg/compat.json/csv`。Editor钩子默认false并在Player编译时移除；本步不改Shader/native/vendor/材质合成，不增加运行资源，不修改古堡源资产。
