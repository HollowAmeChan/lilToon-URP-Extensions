# Ho-RTDI 256/512动态Cutout验收

> 状态：历史过程记录。本文的“当前”“下一项”和通过/失败数值均属于写作步骤，不是待自动执行的任务或现行全产品结论。现行入口：[Ho-RTDI 总文档](../../Ho-RTDI.md)。

第45步把第44步的大尺寸静态验收扩展到caster运动，没有改生产运行时。结果覆盖固定平面/alpha图案、指定骨骼位移和采样网格，不代表任意动态材质、primary运动或真实URP motion输入已经验收。

## 判定与参考

`RunCutoutResolutionMotion/Compatibility`完整渲染256²/512²，使用原双灯与CPU平面交点、UV/alpha遮挡参考。Far灯在每个观察采样点/帧必须无遮挡；Near参考每帧按实际bone位置重新判定，未遮挡响应与灯衰减独立计算。每配置192帧预热、256帧观察，固定60Hz的NRD验证时钟；CPU沿同一世界区域stride2/4网格采3584点，不称全图逐像素。

三种运动沿用原64门槛的参数：slow骨骼局部x=.12sin(frame×.0175)，rapid=.25+.25sin(frame×.27)，step每16帧在0/.5切换。材质/world变化沿现有ReSTIR/NRD策略，必须实际dispatch RELAX、记录可见性gradient调度。raw各通道3%、filtered各通道5%、逐帧L1<.35不变；阶跃另外累计参考确实变化的像素在最初三帧的红通道误差，除该像素的未遮挡参考，保留<.35门槛，不用整体均值替代残影指标。

核对本地final visibility gradient仍从匹配当前receiver起点重求上一帧实际样本、shader世界/结构revision调度；参考用户fixed fork的`RenderingPlugin/External/RTXDI/Samples/FullSample/Shaders/DenoisingPasses/ComputeGradients.hlsl`，其旧sample位置以worldMotion推进后再求当前可见性。另核对用户6000.3.15的NRD FrameData/CommonSettings/尺寸接口。本步未给NRD绑定未验证的URP motion，也没有引入新的运动估计。

## 两条正式结果

两条六组qualityPass=true且数值一致，raw全在3%内：

| 尺寸 / 模式 | filtered红 / 蓝 | frameL1 | 阶跃变化像素L1 | gradient调度帧 |
| --- | --- | --- | --- | --- |
| 256 / slow | .987702 / 1.000145 | .118226 | — | 255 |
| 256 / rapid | .989007 / 1.004298 | .243120 | — | 256 |
| 256 / step | .991973 / 1.001468 | .171756 | .329556 | 15 |
| 512 / slow | .992855 / .997878 | .112981 | — | 255 |
| 512 / rapid | .993195 / 1.000299 | .243524 | — | 256 |
| 512 / step | .991862 / .999334 | .166512 | .314681 | 15 |

step首帧位置与预热一致，随后15次变化触发gradient；固定位置期间跳过新射线，不能把15当256全帧追踪。这个计数证明调度路径，不提供GPU时长。256 step .329556已接近.35，不宣称短期响应平滑或任意动画都稳定；快速整体误差约.243也继续保留。

## 复查与下一项

Roslyn errors0、无新增DI shader诊断，新报告保留真实pass/失败出口。旧256/512静态Cutout及64双灯/core入口回归通过。产品Runtime/Shader/native、Player模板与GI未变，不重建相同Player/GI。

第46步另做[大尺寸动态镜面验收](Ho-RTDI-LargeDynamicSpecular.md)：默认两条路径各3/6通过，快速遮挡噪声失败，参数对照未解决。不能把本页漫反射结果外推至镜面。下一项先定位镜面阶段误差，再考虑阶跃余量、灯分布/移动primary与实际帧证据。1080、真实motion与GPU预算仍未证明；原古堡仅作资产支持审计，不扩张最终材质合成或通用PT。
