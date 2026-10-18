# Ho-RTDI 256/512动态镜面失败基线

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第46步沿用大尺寸动态Cutout场景，增加独立镜面参考与正式失败出口。只改Editor验证，没有接受运行时参数调整。古堡的合并天空外壳/模型仍用于资产支持审计，不作为这个质量门槛的参考场景。

## 参考与判定

`RunCutoutResolutionSpecular/Compatibility`渲染256²/512²，双点灯、固定平面、骨骼驱动Cutout。SB量化后F0=10/255、roughness=102/255；CPU用独立Smith GGX响应、平面射线交点与点灯距离衰减计算未遮挡红/蓝参考，每帧另按实际UV/alpha及caster位置判断红灯遮挡。蓝灯必须在全部观测点/帧无遮挡。

每组192帧预热、256帧观察，固定60Hz验证时钟、8个初始候选及temporal/spatial/RELAX。沿同一世界区域取3584点，256/512分别stride2/4；GPU仍渲染完整尺寸。这是采样网格验收，不是全图逐像素验收。slow/rapid/step运动与漫反射入口相同，阶跃每16帧切换，必须实际dispatch RELAX与可见性gradient。raw各通道3%、filtered各通道5%、逐帧L1<.35、阶跃真实变化像素最初三帧L1<.35均保留。

核对用户6000.3.15参考的`Assets/Scripts/NativePlugin/NRD/NrdSettings.cs`与本地固定NRD头文件：specularPhiLuminance默认1，Atrous默认5、合法范围2..8。另复查用户fixed fork的`RenderingPlugin/External/RTXDI/Samples/FullSample/Shaders/DenoisingPasses/ComputeGradients.hlsl`，其旧样本可见性依赖对应receiver与worldMotion。本步接收面固定，没有接入尚未验证的URP运动纹理，也不能据此证明移动primary正确。

## 默认失败结果

RenderGraph和Compatibility数值一致，各6组仅3组通过，qualityPass=false、Unity退出码1。原始能量全部通过，filtered平均红/蓝也全部通过；失败来自逐帧误差，不能用平均能量替代动态噪声门槛。

| 尺寸 / 模式 | filtered红 / 蓝 | frameL1 | 阶跃变化像素L1 | 通过 |
| --- | --- | --- | --- | --- |
| 256 / slow | .972425 / .986816 | .077365 | — | 是 |
| 256 / rapid | .996179 / .979210 | .507913 | — | 否 |
| 256 / step | .996171 / .975807 | .357448 | .244762 | 否 |
| 512 / slow | .977147 / .987256 | .068578 | — | 是 |
| 512 / rapid | 1.002328 / .970948 | .539075 | — | 否 |
| 512 / step | .999783 / .970253 | .346642 | .225428 | 是，接近门槛 |

slow/rapid/step分别255/256/15帧调度gradient；这个计数不能代表GPU成本。64尺寸镜面通过不能外推至256/512快速遮挡。

## 未接受的参数对照

两项均只由Editor回调覆盖，RenderGraph保持同一帧种子、CPU参考和门槛：

| 对照 | 256 rapid / step frameL1 | 512 rapid / step frameL1 | 结论 |
| --- | --- | --- | --- |
| specPhi=2 | .469027 / .327345 | .508687 / .322794 | rapid仍失败 |
| Atrous=7/8（256/512） | .497639 / .352287 | .517001 / .341189 | rapid及256 step仍失败 |

`RunCutoutResolutionSpecularPhiTwo`与`RunCutoutResolutionSpecularRadiusAudit`分别写`rtdi-resolutionspec46-phi-two-rg.json`、`rtdi-resolutionspec46-radius-rg.json`，不覆盖默认报告。每份报告记录audit、实际应用的specPhi和Atrous；正式默认报告为`rtdi-resolutionspec46-rg/compat.json`。这些对照没有解决问题，不调生产参数，不放宽.35。

## 下一步与范围

Roslyn Runtime/Editor errors0。默认两条与两项参数对照均按失败门槛退出1；原256/512动态漫反射两条六组、64镜面两条三组回归通过，数值与旧基线一致。

先用现有Editor阶段捕获接口，在失败的256 rapid上同时读raw、Temporal、Clamping、首/末Atrous与最终发布镜面，沿相同CPU参考比较能量和逐帧误差，检查最终缩放一致性。平均能量正确只能排除本测试的整体能量失败，尚不能断言具体噪声环节。

第47步[阶段定位](Ho-RTDI-LargeSpecularStages.md)已完成：rapid Temporal约.653，空间滤波逐步减到.508，关闭ReSTIR复用仍约.530失败。诊断还原材质因子/缩放通过，尚未接受运行时修复。

继续保留大尺寸漫反射、64镜面、彩灯/core及Player门槛。生产Shader/native、GI与Player模板没有变化，不重复构建相同Player或扩张最终材质合成/PT。1080、实际URP motion、任意天空/材质与有效GPU预算仍待证明。
